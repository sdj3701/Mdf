using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Fusion;
using UnityEngine;

public class PooledNetworkObjectProvider : Fusion.Behaviour, INetworkObjectProvider
{
    private const int DefaultMaxPoolCountPerPrefab = 128;
    private const int DefaultPrewarmCreationsPerFrame = 2;
    private const float DefaultPrewarmFrameBudgetMilliseconds = 0.75f;
    private const int DefaultMonsterTrimMinimumFreeCount = 1;
    private const int DefaultMonsterTrimHeadroom = 2;

    [Tooltip("If true, Acquire will Retry while the scene manager is busy.")]
    public bool DelayIfSceneManagerIsBusy = true;

    [Tooltip("Maximum retained instances per network prefab. Extra releases are destroyed.")]
    [SerializeField, Min(1)] private int maxPoolCount = DefaultMaxPoolCountPerPrefab;

    [Header("Frame-budgeted prewarm")]
    [Tooltip("Maximum instances all asynchronous prewarm/trim producers on this Runner may process before yielding.")]
    [SerializeField, Range(1, 4)] private int prewarmCreationsPerFrame = DefaultPrewarmCreationsPerFrame;
    [Tooltip("Approximate shared main-thread budget for asynchronous prewarm/trim work on this Runner before yielding.")]
    [SerializeField, Min(0.1f)] private float prewarmFrameBudgetMilliseconds = DefaultPrewarmFrameBudgetMilliseconds;

    [Header("Monster pool retention")]
    [Tooltip("Minimum inactive instances retained for a monster prefab after a measured battle.")]
    [SerializeField, Min(0)] private int monsterTrimMinimumFreeCount = DefaultMonsterTrimMinimumFreeCount;
    [Tooltip("Inactive reserve retained above the most recent concurrent-active high-water mark.")]
    [SerializeField, Min(0)] private int monsterTrimHeadroom = DefaultMonsterTrimHeadroom;

    private readonly Dictionary<NetworkPrefabId, BoundedUnityObjectPool<NetworkObject>> _free = new Dictionary<NetworkPrefabId, BoundedUnityObjectPool<NetworkObject>>();
    private readonly Dictionary<NetworkObject, BoundedUnityObjectPool<NetworkObject>> _prewarmedByPrefab = new Dictionary<NetworkObject, BoundedUnityObjectPool<NetworkObject>>();
    private readonly Dictionary<NetworkObject, NetworkPrefabId> _knownPrefabIds = new Dictionary<NetworkObject, NetworkPrefabId>();
    private readonly Dictionary<NetworkObject, AsyncPrewarmState> _asyncPrewarms = new Dictionary<NetworkObject, AsyncPrewarmState>();
    private readonly Dictionary<NetworkPrefabId, PoolUsageState> _usageByPrefabId = new Dictionary<NetworkPrefabId, PoolUsageState>();
    private readonly Dictionary<NetworkObject, PoolUsageState> _usageByUnresolvedPrefab = new Dictionary<NetworkObject, PoolUsageState>();
    private readonly HashSet<NetworkPrefabId> _missingPrefabWarnings = new HashSet<NetworkPrefabId>();
    private CancellationTokenSource _poolLifetimeCancellation = new CancellationTokenSource();
    private UniTaskCompletionSource<int> _monsterTrimCompletion;
    private int _lastCompletedMonsterTrimGeneration = int.MinValue;
    private int _requestedMonsterTrimGeneration = int.MinValue;
    private int _poolWorkBudgetFrame = -1;
    private int _poolWorkBudgetEpoch;
    private int _poolWorkOperationsThisFrame;
    private long _poolWorkFrameStartedAt;
    private bool _isShutdown;
    private Transform _poolRoot;

    private sealed class AsyncPrewarmState
    {
        public int TargetFreeCount;
        public UniTaskCompletionSource<int> Completion;
    }

    private sealed class PoolUsageState
    {
        public int ActiveCount;
        public int RecentPeakActiveCount;
        public int RequestedFreeCount;
        public bool MonsterTrimEligible;
    }

    private Transform GetPoolRoot()
    {
        if (_poolRoot != null)
        {
            return _poolRoot;
        }

        var root = new GameObject("[PooledNetworkObjects]");
        root.transform.SetParent(transform, false);
        _poolRoot = root.transform;
        return _poolRoot;
    }

    private BoundedUnityObjectPool<NetworkObject> GetOrCreatePool(NetworkPrefabId prefabId)
    {
        if (!_free.TryGetValue(prefabId, out var freePool))
        {
            freePool = new BoundedUnityObjectPool<NetworkObject>(ResolveMaxPoolCount());
            _free.Add(prefabId, freePool);
        }

        return freePool;
    }

    private BoundedUnityObjectPool<NetworkObject> GetOrCreatePrewarmedPool(NetworkObject prefab)
    {
        if (!_prewarmedByPrefab.TryGetValue(prefab, out var freePool))
        {
            freePool = new BoundedUnityObjectPool<NetworkObject>(ResolveMaxPoolCount());
            _prewarmedByPrefab.Add(prefab, freePool);
        }

        return freePool;
    }

    private int ResolveMaxPoolCount()
    {
        return Mathf.Max(1, maxPoolCount);
    }

    private static string NormalizePrefabName(string prefabName)
    {
        return string.IsNullOrWhiteSpace(prefabName)
            ? string.Empty
            : prefabName.Replace("(Clone)", string.Empty).Trim();
    }

    public int GetFreeCount(NetworkPrefabId prefabId)
    {
        if (!_free.TryGetValue(prefabId, out var freePool))
        {
            return 0;
        }

        return freePool.Count;
    }

    public int GetFreeCount(string prefabName)
    {
        prefabName = NormalizePrefabName(prefabName);
        int count = 0;
        var countedPrefabIds = new HashSet<NetworkPrefabId>();
        foreach (var pair in _prewarmedByPrefab)
        {
            if (pair.Key != null && NormalizePrefabName(pair.Key.name) == prefabName)
            {
                count += pair.Value.Count;
            }
        }

        foreach (var pair in _knownPrefabIds)
        {
            if (pair.Key != null &&
                NormalizePrefabName(pair.Key.name) == prefabName &&
                countedPrefabIds.Add(pair.Value) &&
                _free.TryGetValue(pair.Value, out var freePool))
            {
                count += freePool.Count;
            }
        }

        return count;
    }

    public int GetFreeCount(NetworkObject prefab)
    {
        if (prefab == null)
        {
            return 0;
        }

        if (_knownPrefabIds.TryGetValue(prefab, out NetworkPrefabId prefabId))
        {
            return GetFreeCount(prefabId);
        }

        return _prewarmedByPrefab.TryGetValue(prefab, out var prewarmedPool)
            ? prewarmedPool.Count
            : 0;
    }

    public int PrewarmPrefab(NetworkRunner runner, NetworkObject prefab, int targetFreeCount)
    {
        if (runner == null || prefab == null || targetFreeCount <= 0)
        {
            return 0;
        }

        if (_knownPrefabIds.TryGetValue(prefab, out NetworkPrefabId prefabId))
        {
            return PrewarmPrefab(runner, prefab, prefabId, targetFreeCount);
        }

        return PrewarmUnresolvedPrefab(prefab, targetFreeCount);
    }

    /// <summary>
    /// Populates one prefab pool without concentrating all Instantiate work in one frame. Calls on
    /// the same provider/prefab share one producer, so every PlayerManager on a Runner contributes
    /// to one absolute target rather than starting competing batches.
    /// </summary>
    public async UniTask<int> PrewarmPrefabAsync(
        NetworkRunner runner,
        NetworkObject prefab,
        int targetFreeCount,
        bool registerForMonsterTrimming = false,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (runner == null || prefab == null || targetFreeCount <= 0)
        {
            return 0;
        }

        targetFreeCount = Mathf.Min(Mathf.Max(1, targetFreeCount), ResolveMaxPoolCount());
        if (registerForMonsterTrimming)
        {
            MarkMonsterTrimEligible(prefab, targetFreeCount);
        }

        if (GetFreeCount(prefab) >= targetFreeCount)
        {
            return 0;
        }

        int createdForCaller = 0;
        while (GetFreeCount(prefab) < targetFreeCount)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_isShutdown || _poolLifetimeCancellation == null || _poolLifetimeCancellation.IsCancellationRequested)
            {
                throw new OperationCanceledException(_poolLifetimeCancellation?.Token ?? cancellationToken);
            }

            bool ownsProducer = false;
            if (!_asyncPrewarms.TryGetValue(prefab, out AsyncPrewarmState state) || state?.Completion == null)
            {
                state = new AsyncPrewarmState
                {
                    TargetFreeCount = targetFreeCount,
                    Completion = new UniTaskCompletionSource<int>()
                };
                _asyncPrewarms[prefab] = state;
                ownsProducer = true;
                RunPrewarmProducerAsync(runner, prefab, state, cancellationToken).Forget();
            }
            else
            {
                state.TargetFreeCount = Mathf.Max(state.TargetFreeCount, targetFreeCount);
            }

            try
            {
                UniTask<int> waitTask = state.Completion.Task;
                int created = cancellationToken.CanBeCanceled
                    ? await waitTask.AttachExternalCancellation(cancellationToken)
                    : await waitTask;
                if (ownsProducer)
                {
                    // Only the producer owner reports shared work. Joined callers still wait for
                    // the same Prepare gate but do not multiply the created-instance telemetry.
                    createdForCaller += created;
                }
                return createdForCaller;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && !_isShutdown)
            {
                // A stale generation may have owned the shared producer. A still-valid newer
                // caller retries against the already-created free count with a fresh producer.
                if (_asyncPrewarms.TryGetValue(prefab, out AsyncPrewarmState current) &&
                    ReferenceEquals(current, state))
                {
                    _asyncPrewarms.Remove(prefab);
                }
                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
            }
        }

        return createdForCaller;
    }

    private async UniTaskVoid RunPrewarmProducerAsync(
        NetworkRunner runner,
        NetworkObject prefab,
        AsyncPrewarmState state,
        CancellationToken ownerCancellationToken)
    {
        using CancellationTokenSource linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            _poolLifetimeCancellation.Token,
            ownerCancellationToken);
        CancellationToken cancellationToken = linkedCancellation.Token;
        int createdTotal = 0;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        long performanceStart = MPTestPerformanceRecorder.StartTimestamp();
#endif
        try
        {
            while (runner != null && prefab != null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int target = Mathf.Min(Mathf.Max(1, state.TargetFreeCount), ResolveMaxPoolCount());
                int currentFreeCount = GetFreeCount(prefab);
                if (currentFreeCount >= target)
                {
                    break;
                }

                // Reuse the existing name-to-prefabId promotion path and request exactly one more
                // free instance. This keeps concurrent target escalation and Fusion ID resolution
                // inside one absolute pool capacity.
                await WaitForPoolWorkBudgetAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                int created = PrewarmOneInstanceTowardTarget(runner, prefab, target);
                if (created <= 0)
                {
                    break;
                }

                createdTotal += created;
                RecordPoolWorkOperation(created);
            }

            state.Completion.TrySetResult(createdTotal);
        }
        catch (OperationCanceledException)
        {
            state.Completion.TrySetCanceled(cancellationToken);
        }
        catch (Exception exception)
        {
            state.Completion.TrySetException(exception);
        }
        finally
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            MPTestPerformanceRecorder.RecordDuration("network_pool_prewarm_async", performanceStart, createdTotal);
#endif
            if (this != null &&
                prefab != null &&
                _asyncPrewarms.TryGetValue(prefab, out AsyncPrewarmState current) &&
                ReferenceEquals(current, state))
            {
                _asyncPrewarms.Remove(prefab);
            }
        }
    }

    private int PrewarmOneInstanceTowardTarget(
        NetworkRunner runner,
        NetworkObject prefab,
        int targetFreeCount)
    {
        int currentFreeCount = GetFreeCount(prefab);
        if (currentFreeCount >= targetFreeCount)
        {
            return 0;
        }

        // Always derive the next absolute target from the count observed after the budget wait.
        // A concurrent Acquire may have consumed several instances while this producer yielded;
        // this helper still creates at most one object for the current frame-budget operation.
        int nextTarget = Mathf.Min(targetFreeCount, currentFreeCount + 1);
        return PrewarmPrefab(runner, prefab, nextTarget);
    }

    private int ResolvePrewarmCreationsPerFrame()
    {
        return Mathf.Clamp(prewarmCreationsPerFrame, 1, 4);
    }

    private bool HasFrameBudgetElapsed(long startedAt)
    {
        long elapsedTicks = System.Diagnostics.Stopwatch.GetTimestamp() - startedAt;
        double elapsedMilliseconds = elapsedTicks * 1000d / System.Diagnostics.Stopwatch.Frequency;
        return elapsedMilliseconds >= Math.Max(0.1d, prewarmFrameBudgetMilliseconds);
    }

    private async UniTask WaitForPoolWorkBudgetAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RefreshPoolWorkBudgetFrame();
            if (_poolWorkOperationsThisFrame < ResolvePrewarmCreationsPerFrame() &&
                !HasFrameBudgetElapsed(_poolWorkFrameStartedAt))
            {
                return;
            }

            int observedBudgetEpoch = _poolWorkBudgetEpoch;
            await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
            if (_poolWorkBudgetEpoch == observedBudgetEpoch)
            {
                // Time.frameCount is not guaranteed to advance in EditMode player loops. An
                // explicit epoch makes exactly the first shared waiter open the next budget;
                // later waiters observe its operations instead of each resetting independently.
                ResetPoolWorkBudget(Time.frameCount);
            }
        }
    }

    private void RecordPoolWorkOperation(int operationCount = 1)
    {
        RefreshPoolWorkBudgetFrame();
        _poolWorkOperationsThisFrame += Mathf.Max(0, operationCount);
    }

    private void RefreshPoolWorkBudgetFrame()
    {
        int frame = Time.frameCount;
        if (_poolWorkBudgetFrame == frame)
        {
            return;
        }

        ResetPoolWorkBudget(frame);
    }

    private void ResetPoolWorkBudget(int frame)
    {
        _poolWorkBudgetFrame = frame;
        _poolWorkBudgetEpoch++;
        _poolWorkOperationsThisFrame = 0;
        _poolWorkFrameStartedAt = System.Diagnostics.Stopwatch.GetTimestamp();
    }

    /// <summary>
    /// Trims only pools explicitly registered by MonsterSpawner. The target is derived from the
    /// most recent concurrent-active high-water mark and never touches a live NetworkObject.
    /// Calls for the same Prepare generation share one local Runner operation.
    /// </summary>
    public async UniTask<int> TrimMonsterPoolsForGenerationAsync(
        int prepareGeneration,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_isShutdown || prepareGeneration <= _lastCompletedMonsterTrimGeneration)
        {
            return 0;
        }

        _requestedMonsterTrimGeneration = Mathf.Max(_requestedMonsterTrimGeneration, prepareGeneration);
        bool ownsProducer = false;
        UniTaskCompletionSource<int> completion = _monsterTrimCompletion;
        if (completion == null)
        {
            completion = new UniTaskCompletionSource<int>();
            _monsterTrimCompletion = completion;
            ownsProducer = true;
            RunMonsterTrimProducerAsync(completion).Forget();
        }

        UniTask<int> waitTask = completion.Task;
        int trimmed = cancellationToken.CanBeCanceled
            ? await waitTask.AttachExternalCancellation(cancellationToken)
            : await waitTask;
        return ownsProducer ? trimmed : 0;
    }

    private async UniTaskVoid RunMonsterTrimProducerAsync(UniTaskCompletionSource<int> completion)
    {
        CancellationToken cancellationToken = _poolLifetimeCancellation.Token;
        int trimmedTotal = 0;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        long performanceStart = MPTestPerformanceRecorder.StartTimestamp();
#endif
        try
        {
            var resolvedSnapshot = new List<KeyValuePair<NetworkPrefabId, PoolUsageState>>(_usageByPrefabId);
            foreach (KeyValuePair<NetworkPrefabId, PoolUsageState> pair in resolvedSnapshot)
            {
                cancellationToken.ThrowIfCancellationRequested();
                PoolUsageState usage = pair.Value;
                if (usage == null || !usage.MonsterTrimEligible)
                {
                    continue;
                }

                if (_free.TryGetValue(pair.Key, out BoundedUnityObjectPool<NetworkObject> pool))
                {
                    int target = ResolveMonsterTrimTarget(usage, pool.Capacity);
                    trimmedTotal += await TrimPoolAsync(pool, target, cancellationToken);
                }
                usage.RecentPeakActiveCount = usage.ActiveCount;
            }

            var unresolvedSnapshot = new List<KeyValuePair<NetworkObject, PoolUsageState>>(_usageByUnresolvedPrefab);
            foreach (KeyValuePair<NetworkObject, PoolUsageState> pair in unresolvedSnapshot)
            {
                cancellationToken.ThrowIfCancellationRequested();
                NetworkObject prefab = pair.Key;
                PoolUsageState usage = pair.Value;
                if (prefab == null || usage == null || !usage.MonsterTrimEligible)
                {
                    continue;
                }

                if (_prewarmedByPrefab.TryGetValue(prefab, out BoundedUnityObjectPool<NetworkObject> pool))
                {
                    int target = ResolveMonsterTrimTarget(usage, pool.Capacity);
                    trimmedTotal += await TrimPoolAsync(pool, target, cancellationToken);
                }
                usage.RecentPeakActiveCount = usage.ActiveCount;
            }

            _lastCompletedMonsterTrimGeneration = _requestedMonsterTrimGeneration;
            completion.TrySetResult(trimmedTotal);
        }
        catch (OperationCanceledException)
        {
            completion.TrySetCanceled(cancellationToken);
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
        }
        finally
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            MPTestPerformanceRecorder.RecordDuration("network_pool_monster_trim", performanceStart, trimmedTotal);
#endif
            if (ReferenceEquals(_monsterTrimCompletion, completion))
            {
                _monsterTrimCompletion = null;
            }
        }
    }

    private async UniTask<int> TrimPoolAsync(
        BoundedUnityObjectPool<NetworkObject> pool,
        int targetFreeCount,
        CancellationToken cancellationToken)
    {
        int trimmed = 0;
        while (pool != null && pool.Count > targetFreeCount)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await WaitForPoolWorkBudgetAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (pool == null || pool.Count <= targetFreeCount)
            {
                break;
            }

            if (!pool.TryRent(out NetworkObject instance))
            {
                break;
            }
            DestroyNetworkObject(instance);
            trimmed++;
            RecordPoolWorkOperation();
        }

        return trimmed;
    }

    private int ResolveMonsterTrimTarget(PoolUsageState usage, int capacity)
    {
        int measuredDemand = Mathf.Max(0, usage?.RecentPeakActiveCount ?? 0);
        int requestedReserve = Mathf.Max(0, usage?.RequestedFreeCount ?? 0);
        int retained = Mathf.Max(
            requestedReserve,
            Mathf.Max(
                Mathf.Max(0, monsterTrimMinimumFreeCount),
                measuredDemand + Mathf.Max(0, monsterTrimHeadroom)));
        return Mathf.Clamp(retained, 0, Mathf.Max(0, capacity));
    }

    private void MarkMonsterTrimEligible(NetworkObject prefab, int requestedFreeCount)
    {
        if (prefab == null)
        {
            return;
        }

        PoolUsageState usage = _knownPrefabIds.TryGetValue(prefab, out NetworkPrefabId prefabId)
            ? GetOrCreateUsage(prefabId)
            : GetOrCreateUnresolvedUsage(prefab);
        usage.MonsterTrimEligible = true;
        usage.RequestedFreeCount = Mathf.Max(usage.RequestedFreeCount, requestedFreeCount);
    }

    private PoolUsageState GetOrCreateUsage(NetworkPrefabId prefabId)
    {
        if (!_usageByPrefabId.TryGetValue(prefabId, out PoolUsageState usage))
        {
            usage = new PoolUsageState();
            _usageByPrefabId.Add(prefabId, usage);
        }
        return usage;
    }

    private PoolUsageState GetOrCreateUnresolvedUsage(NetworkObject prefab)
    {
        if (!_usageByUnresolvedPrefab.TryGetValue(prefab, out PoolUsageState usage))
        {
            usage = new PoolUsageState();
            _usageByUnresolvedPrefab.Add(prefab, usage);
        }
        return usage;
    }

    private void RecordPrefabAcquire(NetworkPrefabId prefabId)
    {
        PoolUsageState usage = GetOrCreateUsage(prefabId);
        usage.ActiveCount++;
        usage.RecentPeakActiveCount = Mathf.Max(usage.RecentPeakActiveCount, usage.ActiveCount);
    }

    private void RecordPrefabRelease(NetworkPrefabId prefabId)
    {
        if (_usageByPrefabId.TryGetValue(prefabId, out PoolUsageState usage))
        {
            usage.ActiveCount = Mathf.Max(0, usage.ActiveCount - 1);
        }
    }

    public int PrewarmPrefab(NetworkRunner runner, NetworkObject prefab, NetworkPrefabId prefabId, int targetFreeCount)
    {
        if (runner == null || prefab == null || targetFreeCount <= 0)
        {
            return 0;
        }

        PromotePrewarmedPool(prefabId, prefab);
        return PrewarmPool(prefab, GetOrCreatePool(prefabId), targetFreeCount);
    }

    private int PrewarmUnresolvedPrefab(NetworkObject prefab, int targetFreeCount)
    {
        if (prefab == null || targetFreeCount <= 0)
        {
            return 0;
        }

        return PrewarmPool(prefab, GetOrCreatePrewarmedPool(prefab), targetFreeCount);
    }

    private int PrewarmPool(
        NetworkObject prefab,
        BoundedUnityObjectPool<NetworkObject> freePool,
        int targetFreeCount)
    {
        targetFreeCount = Mathf.Min(targetFreeCount, freePool.Capacity);
        int currentFreeCount = freePool.Count;
        int createCount = Mathf.Max(0, targetFreeCount - currentFreeCount);
        if (createCount <= 0)
        {
            return 0;
        }

        Transform poolRoot = GetPoolRoot();
        int created = 0;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        long performanceStart = MPTestPerformanceRecorder.StartTimestamp();
#endif
        for (int i = 0; i < createCount; i++)
        {
            NetworkObject instance = Instantiate(prefab, poolRoot);
            if (instance == null || instance.gameObject == null)
            {
                continue;
            }

            instance.gameObject.SetActive(false);
            if (freePool.TryReturn(instance))
            {
                created++;
            }
            else
            {
                DestroyPoolObject(instance.gameObject);
            }
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        MPTestPerformanceRecorder.RecordDuration("network_pool_prewarm_batch", performanceStart, createCount);
#endif

        return created;
    }

    private void PromotePrewarmedPool(NetworkPrefabId prefabId, NetworkObject prefab)
    {
        if (prefab == null)
        {
            return;
        }

        _knownPrefabIds[prefab] = prefabId;
        if (_usageByUnresolvedPrefab.TryGetValue(prefab, out PoolUsageState unresolvedUsage))
        {
            PoolUsageState resolvedUsage = GetOrCreateUsage(prefabId);
            resolvedUsage.ActiveCount += unresolvedUsage.ActiveCount;
            resolvedUsage.RecentPeakActiveCount = Mathf.Max(
                resolvedUsage.RecentPeakActiveCount,
                unresolvedUsage.RecentPeakActiveCount);
            resolvedUsage.RequestedFreeCount = Mathf.Max(
                resolvedUsage.RequestedFreeCount,
                unresolvedUsage.RequestedFreeCount);
            resolvedUsage.MonsterTrimEligible |= unresolvedUsage.MonsterTrimEligible;
            _usageByUnresolvedPrefab.Remove(prefab);
        }
        if (!_prewarmedByPrefab.TryGetValue(prefab, out var prewarmedPool))
        {
            return;
        }

        var prefabPool = GetOrCreatePool(prefabId);
        while (prewarmedPool.TryRent(out var instance))
        {
            if (!prefabPool.TryReturn(instance))
            {
                DestroyPoolObject(instance.gameObject);
            }
        }

        _prewarmedByPrefab.Remove(prefab);
    }

    protected virtual NetworkObject InstantiatePrefab(NetworkRunner runner, NetworkObject prefab, NetworkPrefabId prefabId)
    {
        // 풀에서 재사용 가능한 인스턴스 확인
        if (_free.TryGetValue(prefabId, out var freePool) && freePool.TryRent(out var instance))
        {
            PreparePooledInstanceForAcquire(instance, prefab);
            return instance;
        }

        GetOrCreatePool(prefabId);

        // 프리팹이 null인지 확인
        if (prefab == null)
        {
            // Debug.LogError($"[PooledNetworkObjectProvider] ❌ 프리팹이 null입니다! PrefabId: {prefabId}. Fusion 프리팹 테이블을 확인하세요.");
            return null;
        }

        // Debug.Log($"[PooledNetworkObjectProvider] 새 인스턴스 생성: {prefab.name}, PrefabId: {prefabId}");
        return Instantiate(prefab);
    }

    private static void PreparePooledInstanceForAcquire(NetworkObject instance, NetworkObject prefab)
    {
        if (instance == null)
        {
            return;
        }

        // The inactive pool root is parented under NetworkManager. Some title/lobby layouts give
        // that object a non-identity transform, so handing a still-parented object to Fusion shifts
        // every replicated world pose by the manager's offset on that peer. Return a true root and
        // restore the prefab transform before Fusion applies the authoritative spawn pose.
        Transform instanceTransform = instance.transform;
        instanceTransform.SetParent(null, false);
        if (prefab != null)
        {
            Transform prefabTransform = prefab.transform;
            instanceTransform.localPosition = prefabTransform.localPosition;
            instanceTransform.localRotation = prefabTransform.localRotation;
            instanceTransform.localScale = prefabTransform.localScale;
        }

        instance.gameObject.SetActive(true);
    }

    protected virtual void DestroyPrefabInstance(NetworkRunner runner, NetworkPrefabId prefabId, NetworkObject instance)
    {
        var freePool = GetOrCreatePool(prefabId);
        if (freePool.Contains(instance))
        {
            return;
        }

        if (freePool.Count >= freePool.Capacity)
        {
            DestroyPoolObject(instance.gameObject);
            return;
        }

        instance.gameObject.SetActive(false);
        instance.transform.SetParent(GetPoolRoot(), false);
        if (!freePool.TryReturn(instance))
        {
            DestroyPoolObject(instance.gameObject);
        }
    }

    public NetworkObjectAcquireResult AcquirePrefabInstance(NetworkRunner runner, in NetworkPrefabAcquireContext context,
        out NetworkObject instance)
    {
        instance = null;

        if (DelayIfSceneManagerIsBusy && runner.SceneManager != null && runner.SceneManager.IsBusy)
        {
            return NetworkObjectAcquireResult.Retry;
        }

        NetworkObject prefab;
        try
        {
            prefab = runner.Prefabs.Load(context.PrefabId, isSynchronous: context.IsSynchronous);
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to load prefab: {ex}");
            return NetworkObjectAcquireResult.Failed;
        }

        if (!prefab)
        {
            if (_missingPrefabWarnings.Add(context.PrefabId))
            {
                string runnerName = runner != null ? runner.name : "null";
                Debug.LogWarning($"[PooledNetworkObjectProvider] Prefab load returned null. runner={runnerName}, prefabId={context.PrefabId}, sync={context.IsSynchronous}. Fusion prefab table/addressable load state를 확인하세요.");
            }

            return NetworkObjectAcquireResult.Retry;
        }

        PromotePrewarmedPool(context.PrefabId, prefab);
        instance = InstantiatePrefab(runner, prefab, context.PrefabId);
        Assert.Check(instance);

        if (context.DontDestroyOnLoad)
        {
            runner.MakeDontDestroyOnLoad(instance.gameObject);
        }
        else
        {
            runner.MoveToRunnerScene(instance.gameObject);
        }

        runner.Prefabs.AddInstance(context.PrefabId);
        RecordPrefabAcquire(context.PrefabId);
        return NetworkObjectAcquireResult.Success;
    }

    public void ReleaseInstance(NetworkRunner runner, in NetworkObjectReleaseContext context)
    {
        var instance = context.Object;

        if (!context.IsBeingDestroyed)
        {
            if (context.TypeId.IsPrefab)
            {
                DestroyPrefabInstance(runner, context.TypeId.AsPrefabId, instance);
            }
            else
            {
                DestroyPoolObject(instance.gameObject);
            }
        }

        if (context.TypeId.IsPrefab)
        {
            RecordPrefabRelease(context.TypeId.AsPrefabId);
            runner.Prefabs.RemoveInstance(context.TypeId.AsPrefabId);
        }
    }

    public NetworkPrefabId GetPrefabId(NetworkRunner runner, NetworkObjectGuid prefabGuid)
    {
        if (runner == null)
        {
            return default;
        }
        return runner.Prefabs.GetId(prefabGuid);
    }

    public void Initialize(NetworkRunner runner)
    {
        _isShutdown = false;
        _poolWorkBudgetFrame = -1;
        _poolWorkBudgetEpoch++;
        _poolWorkOperationsThisFrame = 0;
        _poolWorkFrameStartedAt = 0;
        if (_poolLifetimeCancellation == null || _poolLifetimeCancellation.IsCancellationRequested)
        {
            _poolLifetimeCancellation?.Dispose();
            _poolLifetimeCancellation = new CancellationTokenSource();
        }
    }

    public void Shutdown(NetworkRunner runner)
    {
        _isShutdown = true;
        CancelPoolLifetime();
        foreach (var pair in _free)
        {
            pair.Value?.Drain(DestroyNetworkObject);
        }

        foreach (var pair in _prewarmedByPrefab)
        {
            pair.Value?.Drain(DestroyNetworkObject);
        }

        _free.Clear();
        _prewarmedByPrefab.Clear();
        _knownPrefabIds.Clear();
        _asyncPrewarms.Clear();
        _usageByPrefabId.Clear();
        _usageByUnresolvedPrefab.Clear();
        _missingPrefabWarnings.Clear();
        _monsterTrimCompletion = null;
        _lastCompletedMonsterTrimGeneration = int.MinValue;
        _requestedMonsterTrimGeneration = int.MinValue;
        _poolWorkBudgetFrame = -1;
        _poolWorkBudgetEpoch++;
        _poolWorkOperationsThisFrame = 0;
        _poolWorkFrameStartedAt = 0;
        if (_poolRoot != null)
        {
            DestroyPoolObject(_poolRoot.gameObject);
            _poolRoot = null;
        }
    }

    public void SetMaxPoolCount(int count)
    {
        maxPoolCount = Mathf.Max(1, count);
        foreach (var pool in _free.Values)
        {
            pool.SetCapacity(maxPoolCount, DestroyNetworkObject);
        }

        foreach (var pool in _prewarmedByPrefab.Values)
        {
            pool.SetCapacity(maxPoolCount, DestroyNetworkObject);
        }
    }

    private void DestroyNetworkObject(NetworkObject instance)
    {
        if (instance != null && instance.gameObject != null)
        {
            DestroyPoolObject(instance.gameObject);
        }
    }

    private static void DestroyPoolObject(GameObject target)
    {
        if (target == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(target);
        }
        else
        {
            DestroyImmediate(target);
        }
    }

    private void OnDestroy()
    {
        CancelPoolLifetime();
    }

    private void CancelPoolLifetime()
    {
        if (_poolLifetimeCancellation == null)
        {
            return;
        }

        if (!_poolLifetimeCancellation.IsCancellationRequested)
        {
            _poolLifetimeCancellation.Cancel();
        }
        _poolLifetimeCancellation.Dispose();
        _poolLifetimeCancellation = null;
    }
}
