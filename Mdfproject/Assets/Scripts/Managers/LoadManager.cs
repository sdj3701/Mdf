using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Fusion;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using Cysharp.Threading.Tasks;
using MDF.Runtime.Assets;

public class LoadManager : MonoBehaviour
{
    private const string BootUnitDataLabel = "mdf-boot-data";
    private const string MatchMonsterDataLabel = "MonsterData";
    private const string MatchAugmentDataLabel = "Augment";
    private const string MatchKingDataLabel = "KingUnitData";
    private const string MatchScrollDataLabel = "Scroll";
    private const int MatchMonsterNetworkPoolTargetPerPrefab = 1;
    private const int DefaultUnitNetworkPoolTargetPerPrefab = 4;
    private const int DefaultUnitNetworkPoolTargetUpperBound = 8;
    public static LoadManager Instance { get; private set; }

    private bool _isReady = false;
    [Header("Bounded First-Purchase Pool Warmup")]
    [SerializeField, Min(1)] private int minimumUnitNetworkPoolTargetPerPrefab =
        DefaultUnitNetworkPoolTargetPerPrefab;
    [SerializeField, Min(1)] private int maximumUnitNetworkPoolTargetPerPrefab =
        DefaultUnitNetworkPoolTargetUpperBound;
    private bool _isUnitDataReady;
    private List<UnitData> _allUnits = new List<UnitData>();
    private Dictionary<string, UnitData> _unitByKey = new Dictionary<string, UnitData>();
    private UniTaskCompletionSource<bool> _unitLoadTcs = new UniTaskCompletionSource<bool>();
    private AsyncOperationHandle<IList<UnitData>> _unitDataHandle;
    private bool _hasUnitDataHandle;
    private bool _isInitializing;
    private bool _hasLoadAttempted;
    // Retains the full first-purchase graph (unit prefabs, SkillData, and attack VFX). The field
    // keeps its original name because source-contract tests and diagnostics already identify it.
    private AddressableAssetOwner _unitPrefabAssets = new AddressableAssetOwner();
    private readonly Dictionary<string, GameObject> _prewarmedUnitPrefabs =
        new Dictionary<string, GameObject>(StringComparer.Ordinal);
    private readonly Dictionary<string, SkillData> _prewarmedUnitSkills =
        new Dictionary<string, SkillData>(StringComparer.Ordinal);
    private readonly Dictionary<string, GameObject> _prewarmedUnitVfxPrefabs =
        new Dictionary<string, GameObject>(StringComparer.Ordinal);
    private readonly HashSet<int> _prewarmedDirectSkillPresentationIds = new HashSet<int>();
    private UniTaskCompletionSource<bool> _unitPresentationPrewarmCompletion;
    private bool _isUnitPresentationPrewarming;
    private bool _unitPresentationPrewarmComplete;
    private CancellationTokenSource _unitPresentationPrewarmCancellation;
    private bool _isMatchContentPrewarming;
    private bool _matchContentPrewarmComplete;
    private UniTaskCompletionSource<bool> _matchContentPrewarmCompletion;
    private NetworkRunner _matchContentPrewarmRunner;
    private CancellationTokenSource _matchContentPrewarmCancellation;
    private AsyncOperationHandle<IList<MonsterData>> _matchMonsterDataHandle;
    private AsyncOperationHandle<IList<AugmentData>> _matchAugmentDataHandle;
    private AsyncOperationHandle<IList<KingUnitData>> _matchKingDataHandle;
    private AsyncOperationHandle<IList<MagicScrollData>> _matchScrollDataHandle;
    private bool _hasMatchMonsterDataHandle;
    private bool _hasMatchAugmentDataHandle;
    private bool _hasMatchKingDataHandle;
    private bool _hasMatchScrollDataHandle;
    private readonly AddressableAssetOwner _matchPresentationAssets = new AddressableAssetOwner();
    private readonly Dictionary<string, GameObject> _prewarmedMonsterPrefabs =
        new Dictionary<string, GameObject>(StringComparer.Ordinal);
    private readonly Dictionary<string, GameObject> _prewarmedMonsterProjectilePrefabs =
        new Dictionary<string, GameObject>(StringComparer.Ordinal);
    private CancellationTokenSource _lifetimeCancellation = new CancellationTokenSource();
    private readonly Dictionary<NetworkRunner, UnitNetworkPoolPrewarmState> _unitNetworkPoolPrewarms =
        new Dictionary<NetworkRunner, UnitNetworkPoolPrewarmState>();

    private sealed class UnitNetworkPoolPrewarmState
    {
        public int CompletedTarget;
        public UniTaskCompletionSource<int> Completion;
    }

    /// <summary>
    /// The complete address-based presentation dependency set reachable from the boot UnitData
    /// catalog. Direct Unity object references (for example SkillData.vfxPrefab) are retained by
    /// their owning UnitData/SkillData Addressables handle and therefore do not need an address.
    /// </summary>
    private sealed class UnitPresentationDependencyKeys
    {
        public readonly List<string> UnitPrefabKeys = new List<string>();
        public readonly List<string> SkillDataKeys = new List<string>();
        public readonly List<string> BasicAttackVfxKeys = new List<string>();
    }

    /// <summary>
    /// 싱글톤 인스턴스를 초기화하고 씬 전환 시에도 유지되도록 설정합니다.
    /// </summary>
    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public bool IsReady => _isReady;
    public bool UnitPresentationPrewarmComplete => _unitPresentationPrewarmComplete;
    public bool MatchContentPrewarmComplete => _matchContentPrewarmComplete;
    public int RetainedUnitPrefabCount => _prewarmedUnitPrefabs.Count;
    public int RetainedUnitSkillCount => _prewarmedUnitSkills.Count;
    public int RetainedUnitVfxPrefabCount => _prewarmedUnitVfxPrefabs.Count;
    public int RetainedUnitPresentationAssetCount => _unitPrefabAssets?.RetainedAssetCount ?? 0;

    public int ResolveUnitNetworkPoolTarget(NetworkRunner runner)
    {
        int maximum = Mathf.Max(1, maximumUnitNetworkPoolTargetPerPrefab);
        int minimum = Mathf.Clamp(minimumUnitNetworkPoolTargetPerPrefab, 1, maximum);
        int activePlayerCount = runner != null && runner.IsRunning
            ? runner.ActivePlayers.Count()
            : 0;
        return Mathf.Clamp(Mathf.Max(minimum, activePlayerCount), 1, maximum);
    }

    /// <summary>
    /// 인스펙터 또는 Addressables에서 UnitData를 로드하고 조회용 캐시를 구성합니다.
    /// </summary>
    public async UniTask InitializeAsync()
    {
        if (_isReady) return;

        if (_isInitializing)
        {
            await WaitUntilReady();
            return;
        }

        if (_hasLoadAttempted)
        {
            _unitLoadTcs = new UniTaskCompletionSource<bool>();
        }
        else
        {
            _hasLoadAttempted = true;
        }
        _isInitializing = true;

        try
        {
            // 데이터 소스 결정: 인스펙터 우선, 없으면 Addressables
            _unitDataHandle = StartOwnedLabelLoad<UnitData>(BootUnitDataLabel);
            _hasUnitDataHandle = true;
            var result = await _unitDataHandle.Task;

            if (_unitDataHandle.Status != AsyncOperationStatus.Succeeded)
            {
                throw _unitDataHandle.OperationException ??
                      new System.InvalidOperationException("UnitData boot label load failed.");
            }

            _allUnits = result?.Where(unit => unit != null).ToList() ?? new List<UnitData>();

            // 딕셔너리 생성 (공통)
            _unitByKey = _allUnits
                .GroupBy(u => u.name)
                .ToDictionary(g => g.Key, g => g.First());

            _isUnitDataReady = true;
            // UnitData is cheap semantic data needed by gameplay. Prefab, skill, VFX, GPU and
            // network-pool warmup is deliberately deferred to PrewarmMatchContentAsync, which is
            // invoked only after the lobby host presses Start Game.
            _isReady = true;
            _unitLoadTcs.TrySetResult(true);

            Debug.Log($"[LoadManager] {_allUnits.Count}개 UnitData 로드 완료");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[LoadManager] UnitData 로드 실패: {e.Message}");
            _unitLoadTcs.TrySetException(e);
            ReleaseUnitDataHandle();
            throw;
        }
        finally
        {
            _isInitializing = false;
        }
    }

    /// <summary>
    /// InitializeAsync가 완료되면 끝나는 작업을 반환합니다. 이미 준비되었다면 즉시 완료됩니다.
    /// </summary>
    public UniTask WaitUntilReady()
    {
        if (_isReady) return UniTask.CompletedTask;
        return _unitLoadTcs.Task.AsUniTask();
    }

    /// <summary>
    /// 로드된 모든 UnitData의 읽기 전용 리스트를 반환합니다.
    /// </summary>
    public IReadOnlyList<UnitData> GetAllUnitData()
    {
        return _allUnits;
    }

    /// <summary>
    /// 키(이름)로 UnitData를 조회합니다. 없거나 키가 유효하지 않으면 null을 반환합니다.
    /// </summary>
    public UnitData GetUnitData(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        _unitByKey.TryGetValue(key, out var data);
        return data;
    }

    /// <summary>
    /// Loads and retains every unique unit prefab, star-level SkillData, and basic-attack VFX
    /// referenced by the boot UnitData set. Direct skill VFX and all shared renderer materials are
    /// warmed on mesh-only off-screen proxies. This performs no gameplay spawn and can safely run
    /// during Prepare on every peer.
    /// </summary>
    public async UniTask PrewarmUnitPresentationsAsync(CancellationToken cancellationToken = default)
    {
        if (!_isUnitDataReady)
        {
            UniTask initializeTask = InitializeAsync();
            if (cancellationToken.CanBeCanceled)
            {
                await initializeTask.AttachExternalCancellation(cancellationToken);
            }
            else
            {
                await initializeTask;
            }
        }

        if (_unitPresentationPrewarmComplete)
        {
            return;
        }

        UniTaskCompletionSource<bool> completion =
            GetOrStartUnitPresentationPrewarm(cancellationToken);
        UniTask<bool> waitTask = completion.Task;
        if (cancellationToken.CanBeCanceled)
        {
            await waitTask.AttachExternalCancellation(cancellationToken);
        }
        else
        {
            await waitTask;
        }
    }

    /// <summary>
    /// Loads and warms the local content required to enter a match. This is a peer-local operation;
    /// JoinLobbyUI/NetworkPlayer own the authority ACK gate that prevents the host from loading
    /// 03_Game before every active peer reports success.
    /// </summary>
    public async UniTask PrewarmMatchContentAsync(
        NetworkRunner runner,
        CancellationToken cancellationToken = default)
    {
        if (_isMatchContentPrewarming
            && _matchContentPrewarmRunner != null
            && _matchContentPrewarmRunner != runner)
        {
            await UniTask.WaitUntil(
                () => !_isMatchContentPrewarming,
                PlayerLoopTiming.Update,
                cancellationToken);
            await PrewarmMatchContentAsync(runner, cancellationToken);
            return;
        }

        if (!_isMatchContentPrewarming || _matchContentPrewarmCompletion == null)
        {
            EnsurePrewarmLifetime();
            _isMatchContentPrewarming = true;
            _matchContentPrewarmRunner = runner;
            _matchContentPrewarmCancellation?.Dispose();
            _matchContentPrewarmCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                _lifetimeCancellation.Token,
                cancellationToken);
            _matchContentPrewarmCompletion = new UniTaskCompletionSource<bool>();
            PrewarmMatchContentCoreAsync(
                    runner,
                    _matchContentPrewarmCompletion,
                    _matchContentPrewarmCancellation.Token)
                .Forget();
        }

        UniTask<bool> waitTask = _matchContentPrewarmCompletion.Task;
        if (cancellationToken.CanBeCanceled)
        {
            await waitTask.AttachExternalCancellation(cancellationToken);
        }
        else
        {
            await waitTask;
        }
    }

    private async UniTaskVoid PrewarmMatchContentCoreAsync(
        NetworkRunner runner,
        UniTaskCompletionSource<bool> completion,
        CancellationToken cancellationToken)
    {
        try
        {
            if (runner == null || !runner.IsRunning)
            {
                throw new InvalidOperationException("The lobby NetworkRunner is not running.");
            }

            if (AddressablesManager.Instance == null)
            {
                throw new InvalidOperationException("AddressablesManager is unavailable.");
            }

            Debug.Log("[MatchPrewarm] Local match content preparation started.");
            await AddressablesManager.Instance.InitializeAsync();
            cancellationToken.ThrowIfCancellationRequested();
            await InitializeAsync();
            cancellationToken.ThrowIfCancellationRequested();

            bool requiredPrefabsReady = await AddressablesManager.Instance.LoadGamePrefabsAsync();
            cancellationToken.ThrowIfCancellationRequested();
            if (!requiredPrefabsReady || !AddressablesManager.Instance.GamePrefabsLoaded)
            {
                throw new InvalidOperationException("Required game prefabs did not finish loading.");
            }

            await PrewarmUnitPresentationsAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            await EnsureMatchDataHandlesAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            await PrewarmAdditionalCombatPresentationsAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            int monsterPoolCreations = await PrewarmMonsterPresentationsAsync(runner, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            int unitPoolTarget = ResolveUnitNetworkPoolTarget(runner);
            int unitPoolCreations = await PrewarmUnitNetworkPoolAsync(
                runner,
                unitPoolTarget,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            _matchContentPrewarmComplete = true;
            completion.TrySetResult(true);
            Debug.Log(
                $"[MatchPrewarm] Local match content ready. units={_allUnits.Count}, " +
                $"monsters={_matchMonsterDataHandle.Result?.Count ?? 0}, " +
                $"monsterPrefabs={_prewarmedMonsterPrefabs.Count}, " +
                $"monsterProjectiles={_prewarmedMonsterProjectilePrefabs.Count}, " +
                $"unitPoolCreated={unitPoolCreations}, monsterPoolCreated={monsterPoolCreations}");
        }
        catch (OperationCanceledException)
        {
            completion.TrySetCanceled(cancellationToken);
        }
        catch (Exception exception)
        {
            ReleaseFailedMatchDataHandles();
            completion.TrySetException(exception);
            Debug.LogError($"[MatchPrewarm] Local match content preparation failed: {exception.Message}");
        }
        finally
        {
            if (ReferenceEquals(_matchContentPrewarmCompletion, completion))
            {
                _isMatchContentPrewarming = false;
                _matchContentPrewarmRunner = null;
                _matchContentPrewarmCancellation?.Dispose();
                _matchContentPrewarmCancellation = null;
            }
        }
    }

    private async UniTask EnsureMatchDataHandlesAsync(CancellationToken cancellationToken)
    {
        if (!_hasMatchMonsterDataHandle || !_matchMonsterDataHandle.IsValid())
        {
            _matchMonsterDataHandle = StartOwnedLabelLoad<MonsterData>(MatchMonsterDataLabel);
            _hasMatchMonsterDataHandle = true;
        }

        await _matchMonsterDataHandle.Task;
        cancellationToken.ThrowIfCancellationRequested();
        if (_matchMonsterDataHandle.Status != AsyncOperationStatus.Succeeded
            || _matchMonsterDataHandle.Result == null
            || _matchMonsterDataHandle.Result.Count == 0)
        {
            throw _matchMonsterDataHandle.OperationException
                  ?? new InvalidOperationException("The MonsterData match label returned no content.");
        }

        if (!_hasMatchAugmentDataHandle || !_matchAugmentDataHandle.IsValid())
        {
            _matchAugmentDataHandle = StartOwnedLabelLoad<AugmentData>(MatchAugmentDataLabel);
            _hasMatchAugmentDataHandle = true;
        }

        await _matchAugmentDataHandle.Task;
        cancellationToken.ThrowIfCancellationRequested();
        if (_matchAugmentDataHandle.Status != AsyncOperationStatus.Succeeded
            || _matchAugmentDataHandle.Result == null
            || _matchAugmentDataHandle.Result.Count == 0)
        {
            throw _matchAugmentDataHandle.OperationException
                  ?? new InvalidOperationException("The Augment match label returned no content.");
        }

        if (!_hasMatchKingDataHandle || !_matchKingDataHandle.IsValid())
        {
            _matchKingDataHandle = StartOwnedLabelLoad<KingUnitData>(MatchKingDataLabel);
            _hasMatchKingDataHandle = true;
        }

        await _matchKingDataHandle.Task;
        cancellationToken.ThrowIfCancellationRequested();
        if (_matchKingDataHandle.Status != AsyncOperationStatus.Succeeded
            || _matchKingDataHandle.Result == null
            || _matchKingDataHandle.Result.Count == 0)
        {
            throw _matchKingDataHandle.OperationException
                  ?? new InvalidOperationException("The KingUnitData match label returned no content.");
        }

        if (!_hasMatchScrollDataHandle || !_matchScrollDataHandle.IsValid())
        {
            _matchScrollDataHandle = StartOwnedLabelLoad<MagicScrollData>(MatchScrollDataLabel);
            _hasMatchScrollDataHandle = true;
        }

        await _matchScrollDataHandle.Task;
        cancellationToken.ThrowIfCancellationRequested();
        if (_matchScrollDataHandle.Status != AsyncOperationStatus.Succeeded
            || _matchScrollDataHandle.Result == null
            || _matchScrollDataHandle.Result.Count == 0)
        {
            throw _matchScrollDataHandle.OperationException
                  ?? new InvalidOperationException("The Scroll match label returned no content.");
        }
    }

    private async UniTask PrewarmAdditionalCombatPresentationsAsync(CancellationToken cancellationToken)
    {
        var failures = new List<string>();
        foreach (MagicScrollData scrollData in _matchScrollDataHandle.Result)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (scrollData == null || scrollData.skillData == null)
            {
                continue;
            }

            List<GameObject> presentationPrefabs = CollectSkillPresentationPrefabs(scrollData.skillData);
            foreach (GameObject prefab in presentationPrefabs)
            {
                if (prefab == null)
                {
                    continue;
                }

                int prefabId = prefab.GetInstanceID();
                if (!_prewarmedDirectSkillPresentationIds.Add(prefabId))
                {
                    continue;
                }

                int failureCountBeforeWarmup = failures.Count;
                try
                {
                    await TryWarmPresentationAsync(
                        prefab,
                        $"match-scroll:{scrollData.name}:{prefabId}",
                        $"scroll:{scrollData.name}:{prefab.name}",
                        failures,
                        cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    _prewarmedDirectSkillPresentationIds.Remove(prefabId);
                    throw;
                }

                if (failures.Count > failureCountBeforeWarmup)
                {
                    _prewarmedDirectSkillPresentationIds.Remove(prefabId);
                }
            }
        }

        if (failures.Count > 0)
        {
            throw new InvalidOperationException(
                $"Required scroll presentation warmup failed: {string.Join(", ", failures.Distinct())}");
        }
    }

    private void ReleaseFailedMatchDataHandles()
    {
        if (_hasMatchMonsterDataHandle
            && _matchMonsterDataHandle.IsValid()
            && _matchMonsterDataHandle.Status == AsyncOperationStatus.Failed)
        {
            Addressables.Release(_matchMonsterDataHandle);
            _hasMatchMonsterDataHandle = false;
        }

        if (_hasMatchAugmentDataHandle
            && _matchAugmentDataHandle.IsValid()
            && _matchAugmentDataHandle.Status == AsyncOperationStatus.Failed)
        {
            Addressables.Release(_matchAugmentDataHandle);
            _hasMatchAugmentDataHandle = false;
        }

        if (_hasMatchKingDataHandle
            && _matchKingDataHandle.IsValid()
            && _matchKingDataHandle.Status == AsyncOperationStatus.Failed)
        {
            Addressables.Release(_matchKingDataHandle);
            _hasMatchKingDataHandle = false;
        }

        if (_hasMatchScrollDataHandle
            && _matchScrollDataHandle.IsValid()
            && _matchScrollDataHandle.Status == AsyncOperationStatus.Failed)
        {
            Addressables.Release(_matchScrollDataHandle);
            _hasMatchScrollDataHandle = false;
        }
    }

    private static AsyncOperationHandle<IList<T>> StartOwnedLabelLoad<T>(string label)
    {
        return Addressables.LoadAssetsAsync<T>(label, null);
    }

    private void ReleaseMatchDataHandles()
    {
        if (_hasMatchMonsterDataHandle && _matchMonsterDataHandle.IsValid())
        {
            Addressables.Release(_matchMonsterDataHandle);
        }

        if (_hasMatchAugmentDataHandle && _matchAugmentDataHandle.IsValid())
        {
            Addressables.Release(_matchAugmentDataHandle);
        }

        if (_hasMatchKingDataHandle && _matchKingDataHandle.IsValid())
        {
            Addressables.Release(_matchKingDataHandle);
        }

        if (_hasMatchScrollDataHandle && _matchScrollDataHandle.IsValid())
        {
            Addressables.Release(_matchScrollDataHandle);
        }

        _hasMatchMonsterDataHandle = false;
        _hasMatchAugmentDataHandle = false;
        _hasMatchKingDataHandle = false;
        _hasMatchScrollDataHandle = false;
    }

    private async UniTask<int> PrewarmMonsterPresentationsAsync(
        NetworkRunner runner,
        CancellationToken cancellationToken)
    {
        PooledNetworkObjectProvider provider = runner.GetComponent<PooledNetworkObjectProvider>();
        if (provider == null)
        {
            throw new InvalidOperationException("The lobby runner has no pooled object provider.");
        }

        IEnumerable<string> prefabKeys = _matchMonsterDataHandle.Result
            .Where(data => data != null && !string.IsNullOrWhiteSpace(data.monsterPrefab))
            .Select(data => data.monsterPrefab.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(key => key, StringComparer.Ordinal);

        int createdTotal = 0;
        foreach (string prefabKey in prefabKeys)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GameObject prefab = await AssetLoader.LoadAssetAsync<GameObject>(
                prefabKey,
                _matchPresentationAssets);
            cancellationToken.ThrowIfCancellationRequested();
            if (prefab == null)
            {
                throw new InvalidOperationException($"Monster prefab '{prefabKey}' could not be loaded.");
            }

            if (!prefab.TryGetComponent(out NetworkObject networkPrefab))
            {
                throw new InvalidOperationException($"Monster prefab '{prefabKey}' has no NetworkObject.");
            }

            _prewarmedMonsterPrefabs[prefabKey] = prefab;
            await FirstSpawnPresentationPrewarmer.WarmPrefabAsync(
                prefab,
                $"match-monster:{prefabKey}",
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            createdTotal += await provider.PrewarmPrefabAsync(
                runner,
                networkPrefab,
                MatchMonsterNetworkPoolTargetPerPrefab,
                registerForMonsterTrimming: true,
                cancellationToken: cancellationToken);
            await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
        }

        IEnumerable<string> projectileKeys = _matchMonsterDataHandle.Result
            .Where(data => data != null && !string.IsNullOrWhiteSpace(data.projectilePrefab))
            .Select(data => data.projectilePrefab.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(key => key, StringComparer.Ordinal);
        foreach (string projectileKey in projectileKeys)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GameObject projectilePrefab = await AssetLoader.LoadAssetAsync<GameObject>(
                projectileKey,
                _matchPresentationAssets);
            cancellationToken.ThrowIfCancellationRequested();
            if (projectilePrefab == null)
            {
                throw new InvalidOperationException(
                    $"Monster projectile prefab '{projectileKey}' could not be loaded.");
            }

            _prewarmedMonsterProjectilePrefabs[projectileKey] = projectilePrefab;
            await FirstSpawnPresentationPrewarmer.WarmPrefabAsync(
                projectilePrefab,
                $"match-monster-projectile:{projectileKey}",
                cancellationToken);
            await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
        }

        var skillPresentationFailures = new List<string>();
        foreach (MonsterData monsterData in _matchMonsterDataHandle.Result
                     .Where(data => data != null && data.skillData != null)
                     .OrderBy(data => data.name, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            List<GameObject> skillPrefabs = CollectSkillPresentationPrefabs(monsterData.skillData);
            foreach (GameObject skillPrefab in skillPrefabs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (skillPrefab == null)
                {
                    continue;
                }

                int prefabId = skillPrefab.GetInstanceID();
                if (!_prewarmedDirectSkillPresentationIds.Add(prefabId))
                {
                    continue;
                }

                int failureCountBeforeWarmup = skillPresentationFailures.Count;
                try
                {
                    await TryWarmPresentationAsync(
                        skillPrefab,
                        $"match-monster-skill:{monsterData.name}:{prefabId}",
                        $"monster-skill:{monsterData.name}:{skillPrefab.name}",
                        skillPresentationFailures,
                        cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    _prewarmedDirectSkillPresentationIds.Remove(prefabId);
                    throw;
                }

                if (skillPresentationFailures.Count > failureCountBeforeWarmup)
                {
                    _prewarmedDirectSkillPresentationIds.Remove(prefabId);
                }
            }
        }

        if (skillPresentationFailures.Count > 0)
        {
            throw new InvalidOperationException(
                $"Required monster skill presentation warmup failed: " +
                string.Join(", ", skillPresentationFailures.Distinct()));
        }

        return createdTotal;
    }

    /// <summary>
    /// Populates the Fusion provider only after the retained unit prefabs and their presentation
    /// have been warmed. The provider owns the inactive instances; LoadManager keeps the matching
    /// Addressables leases alive for its lifecycle.
    /// </summary>
    public async UniTask<int> PrewarmUnitNetworkPoolAsync(
        NetworkRunner runner,
        int targetFreeCountPerPrefab = 0,
        CancellationToken cancellationToken = default)
    {
        PruneStoppedUnitNetworkPoolPrewarms();
        if (runner == null || !runner.IsRunning)
        {
            return 0;
        }

        int configuredMaximum = Mathf.Max(1, maximumUnitNetworkPoolTargetPerPrefab);
        targetFreeCountPerPrefab = targetFreeCountPerPrefab > 0
            ? Mathf.Clamp(targetFreeCountPerPrefab, 1, configuredMaximum)
            : ResolveUnitNetworkPoolTarget(runner);

        await PrewarmUnitPresentationsAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        PruneStoppedUnitNetworkPoolPrewarms();
        if (runner == null || !runner.IsRunning)
        {
            return 0;
        }

        int createdTotal = 0;
        while (runner != null && runner.IsRunning)
        {
            if (!_unitNetworkPoolPrewarms.TryGetValue(runner, out UnitNetworkPoolPrewarmState state))
            {
                state = new UnitNetworkPoolPrewarmState();
                _unitNetworkPoolPrewarms.Add(runner, state);
            }

            if (state.CompletedTarget >= targetFreeCountPerPrefab)
            {
                return createdTotal;
            }

            UniTaskCompletionSource<int> completion = state.Completion;
            if (completion == null)
            {
                completion = new UniTaskCompletionSource<int>();
                state.Completion = completion;
                CancellationTokenSource operationCancellation =
                    CancellationTokenSource.CreateLinkedTokenSource(
                        _lifetimeCancellation.Token,
                        cancellationToken);
                PrewarmUnitNetworkPoolCoreAsync(
                        runner,
                        state,
                        completion,
                        targetFreeCountPerPrefab,
                        operationCancellation)
                    .Forget();
            }

            UniTask<int> waitTask = completion.Task;
            int created = cancellationToken.CanBeCanceled
                ? await waitTask.AttachExternalCancellation(cancellationToken)
                : await waitTask;
            createdTotal += created;
        }

        return createdTotal;
    }

    private void PruneStoppedUnitNetworkPoolPrewarms()
    {
        if (_unitNetworkPoolPrewarms.Count == 0)
        {
            return;
        }

        List<NetworkRunner> stoppedRunners = null;
        foreach (NetworkRunner cachedRunner in _unitNetworkPoolPrewarms.Keys)
        {
            if (cachedRunner != null && cachedRunner.IsRunning)
            {
                continue;
            }

            stoppedRunners ??= new List<NetworkRunner>();
            stoppedRunners.Add(cachedRunner);
        }

        if (stoppedRunners == null)
        {
            return;
        }

        foreach (NetworkRunner stoppedRunner in stoppedRunners)
        {
            _unitNetworkPoolPrewarms.Remove(stoppedRunner);
        }
    }

    private async UniTaskVoid PrewarmUnitNetworkPoolCoreAsync(
        NetworkRunner runner,
        UnitNetworkPoolPrewarmState state,
        UniTaskCompletionSource<int> completion,
        int targetFreeCountPerPrefab,
        CancellationTokenSource operationCancellation)
    {
        CancellationToken cancellationToken = operationCancellation.Token;
        try
        {
            if (runner == null || !runner.IsRunning)
            {
                throw new InvalidOperationException("Unit network pool prewarm requires a running NetworkRunner.");
            }

            PooledNetworkObjectProvider provider = runner.GetComponent<PooledNetworkObjectProvider>();
            if (provider == null)
            {
                throw new InvalidOperationException("PooledNetworkObjectProvider is missing from the active runner.");
            }

            int created = 0;
            var failures = new List<string>();
            foreach (KeyValuePair<string, GameObject> pair in
                     _prewarmedUnitPrefabs.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (runner == null || !runner.IsRunning)
                {
                    throw new InvalidOperationException("NetworkRunner stopped during unit pool prewarm.");
                }

                GameObject prefab = pair.Value;
                if (prefab == null || !prefab.TryGetComponent(out NetworkObject networkPrefab))
                {
                    failures.Add(pair.Key);
                    continue;
                }

                created += provider.PrewarmPrefab(runner, networkPrefab, targetFreeCountPerPrefab);
                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
            }

            if (failures.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Unit network pool prewarm found invalid prefabs: {string.Join(", ", failures)}");
            }

            state.CompletedTarget = Mathf.Max(state.CompletedTarget, targetFreeCountPerPrefab);
            completion.TrySetResult(created);
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
            if (ReferenceEquals(state.Completion, completion))
            {
                state.Completion = null;
            }
            if (!ReferenceEquals(runner, null) && (runner == null || !runner.IsRunning))
            {
                _unitNetworkPoolPrewarms.Remove(runner);
            }

            operationCancellation.Dispose();
        }
    }

    internal static List<string> CollectUniqueUnitPrefabKeys(IEnumerable<UnitData> unitData)
    {
        return CollectUnitPresentationDependencyKeys(unitData).UnitPrefabKeys;
    }

    private static UnitPresentationDependencyKeys CollectUnitPresentationDependencyKeys(
        IEnumerable<UnitData> unitData)
    {
        var unitPrefabKeys = new HashSet<string>(StringComparer.Ordinal);
        var skillDataKeys = new HashSet<string>(StringComparer.Ordinal);
        var basicAttackVfxKeys = new HashSet<string>(StringComparer.Ordinal);

        if (unitData != null)
        {
            foreach (UnitData data in unitData)
            {
                if (data == null)
                {
                    continue;
                }

                AddAddressableKeys(unitPrefabKeys, data.prefabsByStarLevel);
                AddAddressableKeys(skillDataKeys, data.skillsByStarLevel);

                BasicAttackVfxProfile profile = data.basicAttackVfxProfile;
                if (profile == null)
                {
                    continue;
                }

                BasicAttackVfxConfig[] slashConfigs = profile.slashConfigsByStarLevel;
                if (slashConfigs != null)
                {
                    for (int index = 0; index < slashConfigs.Length; index++)
                    {
                        AddAddressableKey(basicAttackVfxKeys, slashConfigs[index]?.prefabKey);
                    }
                }

                ProjectileVfxConfig projectile = profile.projectileVfxConfig;
                if (projectile != null)
                {
                    AddAddressableKey(basicAttackVfxKeys, projectile.muzzleFlashKey);
                    AddAddressableKey(basicAttackVfxKeys, projectile.projectileKey);
                    AddAddressableKey(basicAttackVfxKeys, projectile.impactFlashKey);
                }
            }
        }

        var result = new UnitPresentationDependencyKeys();
        result.UnitPrefabKeys.AddRange(unitPrefabKeys.OrderBy(key => key, StringComparer.Ordinal));
        result.SkillDataKeys.AddRange(skillDataKeys.OrderBy(key => key, StringComparer.Ordinal));
        result.BasicAttackVfxKeys.AddRange(basicAttackVfxKeys.OrderBy(key => key, StringComparer.Ordinal));
        return result;
    }

    private static void AddAddressableKeys(HashSet<string> destination, IEnumerable<string> keys)
    {
        if (keys == null)
        {
            return;
        }

        foreach (string key in keys)
        {
            AddAddressableKey(destination, key);
        }
    }

    private static void AddAddressableKey(HashSet<string> destination, string key)
    {
        if (!string.IsNullOrWhiteSpace(key))
        {
            destination.Add(key.Trim());
        }
    }

    private static List<GameObject> CollectSkillPresentationPrefabs(SkillData skillData)
    {
        var prefabs = new List<GameObject>();
        var prefabIds = new HashSet<int>();
        var visitedEffects = new HashSet<int>();

        AddDirectPresentationPrefab(prefabs, prefabIds, skillData != null ? skillData.vfxPrefab : null);
        if (skillData?.effects != null)
        {
            for (int index = 0; index < skillData.effects.Count; index++)
            {
                CollectSkillEffectPresentationPrefabs(
                    skillData.effects[index],
                    prefabs,
                    prefabIds,
                    visitedEffects);
            }
        }

        return prefabs;
    }

    private static void CollectSkillEffectPresentationPrefabs(
        SkillEffect effect,
        List<GameObject> prefabs,
        HashSet<int> prefabIds,
        HashSet<int> visitedEffects)
    {
        if (effect == null || !visitedEffects.Add(effect.GetInstanceID()))
        {
            return;
        }

        // These are the only direct GameObject presentation references in the current SkillEffect
        // schema. Address-based fields added by a future effect should be added to the dependency
        // key collector above so the lease remains explicit.
        if (effect is ZoneEffect zoneEffect)
        {
            AddDirectPresentationPrefab(prefabs, prefabIds, zoneEffect.zonePrefab);
            if (zoneEffect.effectsPerTick != null)
            {
                for (int index = 0; index < zoneEffect.effectsPerTick.Count; index++)
                {
                    CollectSkillEffectPresentationPrefabs(
                        zoneEffect.effectsPerTick[index],
                        prefabs,
                        prefabIds,
                        visitedEffects);
                }
            }
        }
    }

    private static void AddDirectPresentationPrefab(
        List<GameObject> prefabs,
        HashSet<int> prefabIds,
        GameObject prefab)
    {
        if (prefab != null && prefabIds.Add(prefab.GetInstanceID()))
        {
            prefabs.Add(prefab);
        }
    }

    private UniTaskCompletionSource<bool> GetOrStartUnitPresentationPrewarm(
        CancellationToken attemptCancellation)
    {
        if (_isUnitPresentationPrewarming && _unitPresentationPrewarmCompletion != null)
        {
            return _unitPresentationPrewarmCompletion;
        }

        EnsurePrewarmLifetime();
        _isUnitPresentationPrewarming = true;
        _unitPresentationPrewarmCompletion = new UniTaskCompletionSource<bool>();
        _unitPresentationPrewarmCancellation?.Dispose();
        _unitPresentationPrewarmCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            _lifetimeCancellation.Token,
            attemptCancellation);
        PrewarmUnitPresentationsCoreAsync(
                _unitPresentationPrewarmCompletion,
                _unitPresentationPrewarmCancellation)
            .Forget();
        return _unitPresentationPrewarmCompletion;
    }

    private async UniTaskVoid PrewarmUnitPresentationsCoreAsync(
        UniTaskCompletionSource<bool> completion,
        CancellationTokenSource operationCancellation)
    {
        CancellationToken cancellationToken = operationCancellation.Token;
        try
        {
            UnitPresentationDependencyKeys dependencies =
                CollectUnitPresentationDependencyKeys(_allUnits);
            var requiredPrefabLoadFailures = new List<string>();
            var optionalDependencyFailures = new List<string>();

            await LoadAndWarmPrefabDependenciesAsync(
                dependencies.UnitPrefabKeys,
                "unit",
                _prewarmedUnitPrefabs,
                requiredPrefabLoadFailures,
                optionalDependencyFailures,
                cancellationToken);

            for (int index = 0; index < dependencies.SkillDataKeys.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string skillKey = dependencies.SkillDataKeys[index];
                SkillData skillData = await AssetLoader.LoadAssetAsync<SkillData>(
                    skillKey,
                    _unitPrefabAssets);
                cancellationToken.ThrowIfCancellationRequested();
                if (skillData == null)
                {
                    optionalDependencyFailures.Add($"skill:{skillKey}:load");
                    continue;
                }

                _prewarmedUnitSkills[skillKey] = skillData;
                List<GameObject> directPrefabs = CollectSkillPresentationPrefabs(skillData);
                for (int prefabIndex = 0; prefabIndex < directPrefabs.Count; prefabIndex++)
                {
                    GameObject directPrefab = directPrefabs[prefabIndex];
                    if (directPrefab == null ||
                        !_prewarmedDirectSkillPresentationIds.Add(directPrefab.GetInstanceID()))
                    {
                        continue;
                    }

                    await TryWarmPresentationAsync(
                        directPrefab,
                        $"unit-skill:{directPrefab.GetInstanceID()}",
                        $"skill:{skillKey}:{directPrefab.name}",
                        optionalDependencyFailures,
                        cancellationToken);
                }

                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
            }

            await LoadAndWarmPrefabDependenciesAsync(
                dependencies.BasicAttackVfxKeys,
                "unit-basic-attack",
                _prewarmedUnitVfxPrefabs,
                optionalDependencyFailures,
                optionalDependencyFailures,
                cancellationToken);

            if (requiredPrefabLoadFailures.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Unit presentation prewarm could not load required prefabs: " +
                    string.Join(", ", requiredPrefabLoadFailures));
            }

            if (optionalDependencyFailures.Count > 0)
            {
                Debug.LogWarning(
                    $"[LoadManager] Unit presentation prewarm completed with optional misses: " +
                    string.Join(", ", optionalDependencyFailures.Distinct()));
            }

            _unitPresentationPrewarmComplete = true;
            completion.TrySetResult(true);
            Debug.Log(
                $"[LoadManager] Unit presentation prewarm complete. " +
                $"prefabs={_prewarmedUnitPrefabs.Count}, skills={_prewarmedUnitSkills.Count}, " +
                $"basicAttackVfx={_prewarmedUnitVfxPrefabs.Count}, " +
                $"directSkillVfx={_prewarmedDirectSkillPresentationIds.Count}, " +
                $"retainedAssets={_unitPrefabAssets.RetainedAssetCount}");
        }
        catch (OperationCanceledException)
        {
            // Direct-presentation IDs are recorded before their asynchronous warmup. Clear the
            // partial index so a later lobby attempt retries every dependency safely; completed
            // GPU work remains deduplicated inside FirstSpawnPresentationPrewarmer.
            _prewarmedDirectSkillPresentationIds.Clear();
            completion.TrySetCanceled(cancellationToken);
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
            Debug.LogWarning($"[LoadManager] Unit presentation prewarm failed: {exception.Message}");
        }
        finally
        {
            if (ReferenceEquals(_unitPresentationPrewarmCompletion, completion))
            {
                _isUnitPresentationPrewarming = false;
            }

            if (ReferenceEquals(_unitPresentationPrewarmCancellation, operationCancellation))
            {
                _unitPresentationPrewarmCancellation = null;
            }

            operationCancellation.Dispose();
        }
    }

    private async UniTask LoadAndWarmPrefabDependenciesAsync(
        IReadOnlyList<string> keys,
        string stableKeyPrefix,
        Dictionary<string, GameObject> destination,
        List<string> loadFailures,
        List<string> presentationFailures,
        CancellationToken cancellationToken)
    {
        for (int index = 0; index < keys.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string key = keys[index];
            GameObject prefab = await AssetLoader.LoadAssetAsync<GameObject>(key, _unitPrefabAssets);
            cancellationToken.ThrowIfCancellationRequested();
            if (prefab == null)
            {
                loadFailures.Add($"{stableKeyPrefix}:{key}:load");
                continue;
            }

            destination[key] = prefab;
            await TryWarmPresentationAsync(
                prefab,
                $"{stableKeyPrefix}:{key}",
                $"{stableKeyPrefix}:{key}",
                presentationFailures,
                cancellationToken);
            await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
        }
    }

    private static async UniTask TryWarmPresentationAsync(
        GameObject prefab,
        string stableKey,
        string diagnosticKey,
        List<string> failures,
        CancellationToken cancellationToken)
    {
        try
        {
            await FirstSpawnPresentationPrewarmer.WarmPrefabAsync(
                prefab,
                stableKey,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            failures.Add($"{diagnosticKey}:presentation");
        }
    }

    private void EnsurePrewarmLifetime()
    {
        if (_unitPrefabAssets == null || _unitPrefabAssets.IsDisposed)
        {
            _unitPrefabAssets = new AddressableAssetOwner();
        }

        if (_lifetimeCancellation == null || _lifetimeCancellation.IsCancellationRequested)
        {
            _lifetimeCancellation?.Dispose();
            _lifetimeCancellation = new CancellationTokenSource();
        }
    }

    private void OnDestroy()
    {
        if (_lifetimeCancellation != null)
        {
            _lifetimeCancellation.Cancel();
            _lifetimeCancellation.Dispose();
            _lifetimeCancellation = null;
        }

        _matchContentPrewarmCancellation?.Cancel();
        _matchContentPrewarmCancellation?.Dispose();
        _matchContentPrewarmCancellation = null;

        _unitPresentationPrewarmCancellation?.Cancel();
        _unitPresentationPrewarmCancellation?.Dispose();
        _unitPresentationPrewarmCancellation = null;

        _unitPrefabAssets?.Dispose();
        _matchPresentationAssets.Dispose();
        _prewarmedUnitPrefabs.Clear();
        _prewarmedUnitSkills.Clear();
        _prewarmedUnitVfxPrefabs.Clear();
        _prewarmedDirectSkillPresentationIds.Clear();
        _prewarmedMonsterPrefabs.Clear();
        _prewarmedMonsterProjectilePrefabs.Clear();
        _unitNetworkPoolPrewarms.Clear();
        ReleaseMatchDataHandles();

        if (Instance != this)
        {
            return;
        }

        ReleaseUnitDataHandle();
        Instance = null;
    }

    private void ReleaseUnitDataHandle()
    {
        if (_hasUnitDataHandle && _unitDataHandle.IsValid())
        {
            Addressables.Release(_unitDataHandle);
        }

        _hasUnitDataHandle = false;
    }
}
