using System;
using System.Collections.Generic;
using Fusion;
using UnityEngine;

public class PooledNetworkObjectProvider : Fusion.Behaviour, INetworkObjectProvider
{
    [Tooltip("If true, Acquire will Retry while the scene manager is busy.")]
    public bool DelayIfSceneManagerIsBusy = true;

    [Tooltip("0 or negative keeps all released instances in the pool.")]
    [SerializeField] private int maxPoolCount = 0;

    private readonly Dictionary<NetworkPrefabId, Queue<NetworkObject>> _free = new Dictionary<NetworkPrefabId, Queue<NetworkObject>>();
    private readonly Dictionary<string, Queue<NetworkObject>> _freeByPrefabName = new Dictionary<string, Queue<NetworkObject>>();
    private readonly HashSet<NetworkPrefabId> _missingPrefabWarnings = new HashSet<NetworkPrefabId>();
    private Transform _poolRoot;

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

    private Queue<NetworkObject> GetOrCreateQueue(NetworkPrefabId prefabId)
    {
        if (!_free.TryGetValue(prefabId, out var freeQueue))
        {
            freeQueue = new Queue<NetworkObject>();
            _free.Add(prefabId, freeQueue);
        }

        return freeQueue;
    }

    private Queue<NetworkObject> GetOrCreateNamedQueue(string prefabName)
    {
        prefabName = NormalizePrefabName(prefabName);
        if (!_freeByPrefabName.TryGetValue(prefabName, out var freeQueue))
        {
            freeQueue = new Queue<NetworkObject>();
            _freeByPrefabName.Add(prefabName, freeQueue);
        }

        return freeQueue;
    }

    private static string NormalizePrefabName(string prefabName)
    {
        return string.IsNullOrWhiteSpace(prefabName)
            ? string.Empty
            : prefabName.Replace("(Clone)", string.Empty).Trim();
    }

    private static int PruneDeadInstances(Queue<NetworkObject> freeQueue)
    {
        if (freeQueue == null || freeQueue.Count == 0)
        {
            return 0;
        }

        int originalCount = freeQueue.Count;
        int liveCount = 0;
        for (int i = 0; i < originalCount; i++)
        {
            var instance = freeQueue.Dequeue();
            if (instance == null || instance.gameObject == null)
            {
                continue;
            }

            freeQueue.Enqueue(instance);
            liveCount++;
        }

        return liveCount;
    }

    public int GetFreeCount(NetworkPrefabId prefabId)
    {
        if (!_free.TryGetValue(prefabId, out var freeQueue))
        {
            return 0;
        }

        return PruneDeadInstances(freeQueue);
    }

    public int GetFreeCount(string prefabName)
    {
        prefabName = NormalizePrefabName(prefabName);
        if (!_freeByPrefabName.TryGetValue(prefabName, out var freeQueue))
        {
            return 0;
        }

        return PruneDeadInstances(freeQueue);
    }

    public int PrewarmPrefab(NetworkRunner runner, NetworkObject prefab, int targetFreeCount)
    {
        if (runner == null || prefab == null || targetFreeCount <= 0)
        {
            return 0;
        }

        return PrewarmNamedPrefab(prefab, targetFreeCount);
    }

    public int PrewarmPrefab(NetworkRunner runner, NetworkObject prefab, NetworkPrefabId prefabId, int targetFreeCount)
    {
        if (runner == null || prefab == null || targetFreeCount <= 0)
        {
            return 0;
        }

        if (maxPoolCount > 0)
        {
            targetFreeCount = Mathf.Min(targetFreeCount, maxPoolCount);
        }

        var freeQueue = GetOrCreateQueue(prefabId);
        int currentFreeCount = PruneDeadInstances(freeQueue);
        int createCount = Mathf.Max(0, targetFreeCount - currentFreeCount);
        if (createCount <= 0)
        {
            return 0;
        }

        Transform poolRoot = GetPoolRoot();
        int created = 0;
        for (int i = 0; i < createCount; i++)
        {
            NetworkObject instance = Instantiate(prefab, poolRoot);
            if (instance == null || instance.gameObject == null)
            {
                continue;
            }

            instance.gameObject.SetActive(false);
            freeQueue.Enqueue(instance);
            created++;
        }

        return created;
    }

    private int PrewarmNamedPrefab(NetworkObject prefab, int targetFreeCount)
    {
        if (prefab == null || targetFreeCount <= 0)
        {
            return 0;
        }

        if (maxPoolCount > 0)
        {
            targetFreeCount = Mathf.Min(targetFreeCount, maxPoolCount);
        }

        string prefabName = NormalizePrefabName(prefab.name);
        var freeQueue = GetOrCreateNamedQueue(prefabName);
        int currentFreeCount = PruneDeadInstances(freeQueue);
        int createCount = Mathf.Max(0, targetFreeCount - currentFreeCount);
        if (createCount <= 0)
        {
            return 0;
        }

        Transform poolRoot = GetPoolRoot();
        int created = 0;
        for (int i = 0; i < createCount; i++)
        {
            NetworkObject instance = Instantiate(prefab, poolRoot);
            if (instance == null || instance.gameObject == null)
            {
                continue;
            }

            instance.gameObject.SetActive(false);
            freeQueue.Enqueue(instance);
            created++;
        }

        return created;
    }

    private void PromoteNamedPool(NetworkPrefabId prefabId, string prefabName)
    {
        prefabName = NormalizePrefabName(prefabName);
        if (string.IsNullOrEmpty(prefabName) || !_freeByPrefabName.TryGetValue(prefabName, out var namedQueue))
        {
            return;
        }

        var prefabQueue = GetOrCreateQueue(prefabId);
        PruneDeadInstances(namedQueue);
        while (namedQueue.Count > 0)
        {
            var instance = namedQueue.Dequeue();
            if (instance == null || instance.gameObject == null)
            {
                continue;
            }

            prefabQueue.Enqueue(instance);
        }

        _freeByPrefabName.Remove(prefabName);
    }

    protected virtual NetworkObject InstantiatePrefab(NetworkRunner runner, NetworkObject prefab, NetworkPrefabId prefabId)
    {
        // 풀에서 재사용 가능한 인스턴스 확인
        if (_free.TryGetValue(prefabId, out var freeQueue) && freeQueue.Count > 0)
        {
            var instance = freeQueue.Dequeue();
            // 풀에 있던 인스턴스가 파괴되었는지 확인
            if (instance == null || instance.gameObject == null)
            {
                // Debug.Log($"[PooledNetworkObjectProvider] 풀에서 가져온 인스턴스가 null입니다. PrefabId: {prefabId}. 새로 생성합니다.");
                // 계속해서 새 인스턴스 생성으로 진행
            }
            else
            {
                instance.gameObject.SetActive(true);
                return instance;
            }
        }

        GetOrCreateQueue(prefabId);

        // 프리팹이 null인지 확인
        if (prefab == null)
        {
            // Debug.LogError($"[PooledNetworkObjectProvider] ❌ 프리팹이 null입니다! PrefabId: {prefabId}. Fusion 프리팹 테이블을 확인하세요.");
            return null;
        }

        // Debug.Log($"[PooledNetworkObjectProvider] 새 인스턴스 생성: {prefab.name}, PrefabId: {prefabId}");
        return Instantiate(prefab);
    }

    protected virtual void DestroyPrefabInstance(NetworkRunner runner, NetworkPrefabId prefabId, NetworkObject instance)
    {
        var freeQueue = GetOrCreateQueue(prefabId);
        PruneDeadInstances(freeQueue);

        if (maxPoolCount > 0 && freeQueue.Count >= maxPoolCount)
        {
            Destroy(instance.gameObject);
            return;
        }

        instance.transform.SetParent(GetPoolRoot(), false);
        instance.gameObject.SetActive(false);
        freeQueue.Enqueue(instance);
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

        PromoteNamedPool(context.PrefabId, prefab.name);
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
                Destroy(instance.gameObject);
            }
        }

        if (context.TypeId.IsPrefab)
        {
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
    }

    public void Shutdown(NetworkRunner runner)
    {
        foreach (var pair in _free)
        {
            var freeQueue = pair.Value;
            while (freeQueue != null && freeQueue.Count > 0)
            {
                var instance = freeQueue.Dequeue();
                if (instance != null && instance.gameObject != null)
                {
                    Destroy(instance.gameObject);
                }
            }
        }

        foreach (var pair in _freeByPrefabName)
        {
            var freeQueue = pair.Value;
            while (freeQueue != null && freeQueue.Count > 0)
            {
                var instance = freeQueue.Dequeue();
                if (instance != null && instance.gameObject != null)
                {
                    Destroy(instance.gameObject);
                }
            }
        }

        _free.Clear();
        _freeByPrefabName.Clear();
        _missingPrefabWarnings.Clear();
        if (_poolRoot != null)
        {
            Destroy(_poolRoot.gameObject);
            _poolRoot = null;
        }
    }

    public void SetMaxPoolCount(int count)
    {
        maxPoolCount = count;
    }
}
