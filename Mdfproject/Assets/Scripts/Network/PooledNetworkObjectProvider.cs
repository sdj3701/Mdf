using System;
using System.Collections.Generic;
using Fusion;
using UnityEngine;

public class PooledNetworkObjectProvider : Fusion.Behaviour, INetworkObjectProvider
{
    private const int DefaultMaxPoolCountPerPrefab = 128;

    [Tooltip("If true, Acquire will Retry while the scene manager is busy.")]
    public bool DelayIfSceneManagerIsBusy = true;

    [Tooltip("Maximum retained instances per network prefab. Extra releases are destroyed.")]
    [SerializeField, Min(1)] private int maxPoolCount = DefaultMaxPoolCountPerPrefab;

    private readonly Dictionary<NetworkPrefabId, BoundedUnityObjectPool<NetworkObject>> _free = new Dictionary<NetworkPrefabId, BoundedUnityObjectPool<NetworkObject>>();
    private readonly Dictionary<string, BoundedUnityObjectPool<NetworkObject>> _freeByPrefabName = new Dictionary<string, BoundedUnityObjectPool<NetworkObject>>();
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

    private BoundedUnityObjectPool<NetworkObject> GetOrCreatePool(NetworkPrefabId prefabId)
    {
        if (!_free.TryGetValue(prefabId, out var freePool))
        {
            freePool = new BoundedUnityObjectPool<NetworkObject>(ResolveMaxPoolCount());
            _free.Add(prefabId, freePool);
        }

        return freePool;
    }

    private BoundedUnityObjectPool<NetworkObject> GetOrCreateNamedPool(string prefabName)
    {
        prefabName = NormalizePrefabName(prefabName);
        if (!_freeByPrefabName.TryGetValue(prefabName, out var freePool))
        {
            freePool = new BoundedUnityObjectPool<NetworkObject>(ResolveMaxPoolCount());
            _freeByPrefabName.Add(prefabName, freePool);
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
        if (!_freeByPrefabName.TryGetValue(prefabName, out var freePool))
        {
            return 0;
        }

        return freePool.Count;
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

        targetFreeCount = Mathf.Min(targetFreeCount, ResolveMaxPoolCount());

        var freePool = GetOrCreatePool(prefabId);
        int currentFreeCount = freePool.Count;
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
            if (freePool.TryReturn(instance))
            {
                created++;
            }
            else
            {
                Destroy(instance.gameObject);
            }
        }

        return created;
    }

    private int PrewarmNamedPrefab(NetworkObject prefab, int targetFreeCount)
    {
        if (prefab == null || targetFreeCount <= 0)
        {
            return 0;
        }

        targetFreeCount = Mathf.Min(targetFreeCount, ResolveMaxPoolCount());

        string prefabName = NormalizePrefabName(prefab.name);
        var freePool = GetOrCreateNamedPool(prefabName);
        int currentFreeCount = freePool.Count;
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
            if (freePool.TryReturn(instance))
            {
                created++;
            }
            else
            {
                Destroy(instance.gameObject);
            }
        }

        return created;
    }

    private void PromoteNamedPool(NetworkPrefabId prefabId, string prefabName)
    {
        prefabName = NormalizePrefabName(prefabName);
        if (string.IsNullOrEmpty(prefabName) || !_freeByPrefabName.TryGetValue(prefabName, out var namedPool))
        {
            return;
        }

        var prefabPool = GetOrCreatePool(prefabId);
        while (namedPool.TryRent(out var instance))
        {
            if (!prefabPool.TryReturn(instance))
            {
                Destroy(instance.gameObject);
            }
        }

        _freeByPrefabName.Remove(prefabName);
    }

    protected virtual NetworkObject InstantiatePrefab(NetworkRunner runner, NetworkObject prefab, NetworkPrefabId prefabId)
    {
        // 풀에서 재사용 가능한 인스턴스 확인
        if (_free.TryGetValue(prefabId, out var freePool) && freePool.TryRent(out var instance))
        {
            instance.gameObject.SetActive(true);
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

    protected virtual void DestroyPrefabInstance(NetworkRunner runner, NetworkPrefabId prefabId, NetworkObject instance)
    {
        var freePool = GetOrCreatePool(prefabId);
        if (freePool.Contains(instance))
        {
            return;
        }

        if (freePool.Count >= freePool.Capacity)
        {
            Destroy(instance.gameObject);
            return;
        }

        instance.transform.SetParent(GetPoolRoot(), false);
        instance.gameObject.SetActive(false);
        if (!freePool.TryReturn(instance))
        {
            Destroy(instance.gameObject);
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
            pair.Value?.Drain(DestroyNetworkObject);
        }

        foreach (var pair in _freeByPrefabName)
        {
            pair.Value?.Drain(DestroyNetworkObject);
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
        maxPoolCount = Mathf.Max(1, count);
        foreach (var pool in _free.Values)
        {
            pool.SetCapacity(maxPoolCount, DestroyNetworkObject);
        }

        foreach (var pool in _freeByPrefabName.Values)
        {
            pool.SetCapacity(maxPoolCount, DestroyNetworkObject);
        }
    }

    private void DestroyNetworkObject(NetworkObject instance)
    {
        if (instance != null && instance.gameObject != null)
        {
            Destroy(instance.gameObject);
        }
    }
}
