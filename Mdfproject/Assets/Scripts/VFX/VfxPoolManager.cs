using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using MDF.Runtime.Assets;
using UnityEngine;
using UnityEngine.Serialization;

public class VfxPoolManager : MonoBehaviour
{
    [System.Serializable]
    private class PoolConfig
    {
        public GameObject prefab;
        public int prewarmCount = 4;
        public Transform parentOverride;
    }

    [SerializeField] private List<PoolConfig> initialPools = new List<PoolConfig>();
    [SerializeField] private Transform defaultParent;
    [SerializeField] private bool prewarmOnStart = true;

    [Header("Optional Bounded VFX Warmup")]
    [SerializeField, FormerlySerializedAs("prewarmProjectileProfilesOnStart")] private bool prewarmBasicAttackProfilesOnStart;
    [SerializeField, FormerlySerializedAs("projectileProfilePrewarmCount"), Min(0)] private int basicAttackProfilePrewarmCount = 1;
    [SerializeField, Min(0)] private int basicAttackProfilePrewarmKeyBudget = 8;
    [SerializeField, FormerlySerializedAs("projectileProfileParentOverride")] private Transform basicAttackProfileParentOverride;
    [SerializeField, Min(1)] private int maxRetainedInstancesPerPrefab = 32;

    private readonly Dictionary<GameObject, BoundedUnityObjectPool<GameObject>> _pools =
        new Dictionary<GameObject, BoundedUnityObjectPool<GameObject>>();
    private readonly Dictionary<string, AddressableAssetLease<GameObject>> _addressablePrefabLeases =
        new Dictionary<string, AddressableAssetLease<GameObject>>(StringComparer.Ordinal);
    private readonly Dictionary<string, UniTaskCompletionSource<GameObject>> _addressablePrefabLoads =
        new Dictionary<string, UniTaskCompletionSource<GameObject>>(StringComparer.Ordinal);
    private bool _basicAttackProfilePrewarmStarted;

    public static VfxPoolManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else if (Instance != this)
        {
            return;
        }

        if (defaultParent == null)
        {
            defaultParent = transform;
        }
    }

    private void Start()
    {
        if (prewarmOnStart)
        {
            PrewarmInitialPools();
        }

        if (prewarmBasicAttackProfilesOnStart)
        {
            PrewarmBasicAttackProfilesAsync().Forget();
        }
    }

    public GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation, Transform parent = null)
    {
        if (prefab == null)
        {
            return null;
        }

        BoundedUnityObjectPool<GameObject> pool = GetOrCreatePool(prefab);
        GameObject instance = pool.TryRent(out GameObject rented) ? rented : CreateInstance(prefab, parent);
        if (instance == null)
        {
            return null;
        }

        if (instance.TryGetComponent(out PooledObject pooled))
        {
            pooled.MarkRented();
        }

        var targetParent = parent != null ? parent : defaultParent;
        instance.transform.SetParent(targetParent, false);
        instance.transform.SetPositionAndRotation(position, rotation);
        instance.SetActive(true);
        return instance;
    }

    /// <summary>
    /// Lazily loads a VFX prefab once and keeps its Addressables lease for the lifetime of this pool.
    /// Concurrent requests for the same key share one in-flight load.
    /// </summary>
    public async UniTask<GameObject> LoadAddressablePrefabAsync(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        string normalizedKey = key.Trim();
        if (_addressablePrefabLeases.TryGetValue(normalizedKey, out AddressableAssetLease<GameObject> lease))
        {
            if (lease.Asset != null)
            {
                return lease.Asset;
            }

            lease.Dispose();
            _addressablePrefabLeases.Remove(normalizedKey);
            _addressablePrefabLoads.Remove(normalizedKey);
        }

        if (!_addressablePrefabLoads.TryGetValue(normalizedKey, out UniTaskCompletionSource<GameObject> pendingLoad))
        {
            pendingLoad = new UniTaskCompletionSource<GameObject>();
            _addressablePrefabLoads.Add(normalizedKey, pendingLoad);
            PublishAddressablePrefabLoadAsync(normalizedKey, pendingLoad).Forget();
        }

        return await pendingLoad.Task;
    }

    private async UniTaskVoid PublishAddressablePrefabLoadAsync(
        string key,
        UniTaskCompletionSource<GameObject> completion)
    {
        try
        {
            GameObject prefab = await LoadAndRetainAddressablePrefabAsync(key);
            completion.TrySetResult(prefab);
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
        }
        finally
        {
            if (this != null &&
                _addressablePrefabLoads.TryGetValue(key, out UniTaskCompletionSource<GameObject> current) &&
                ReferenceEquals(current, completion))
            {
                _addressablePrefabLoads.Remove(key);
            }
        }
    }

    public void Despawn(GameObject instance)
    {
        if (instance == null)
        {
            return;
        }

        var pooled = instance.GetComponent<PooledObject>();
        if (pooled == null || pooled.OriginPrefab == null)
        {
            Destroy(instance);
            return;
        }
        if (!pooled.TryBeginReturn())
        {
            return;
        }

        ResetInstance(instance);
        instance.SetActive(false);
        instance.transform.SetParent(defaultParent, false);

        BoundedUnityObjectPool<GameObject> pool = GetOrCreatePool(pooled.OriginPrefab);
        if (!pool.TryReturn(instance))
        {
            Destroy(instance);
        }
    }

    public void Prewarm(GameObject prefab, int count, Transform parent = null)
    {
        if (prefab == null || count <= 0)
        {
            return;
        }

        if (defaultParent == null)
        {
            defaultParent = transform;
        }

        BoundedUnityObjectPool<GameObject> pool = GetOrCreatePool(prefab);

        Transform targetParent = parent != null ? parent : defaultParent;
        int targetCount = Mathf.Min(count, Mathf.Max(1, maxRetainedInstancesPerPrefab));
        for (int i = pool.Count; i < targetCount; i++)
        {
            var instance = CreateInstance(prefab, targetParent);
            if (instance != null)
            {
                Despawn(instance);
            }
        }
    }

    private BoundedUnityObjectPool<GameObject> GetOrCreatePool(GameObject prefab)
    {
        if (!_pools.TryGetValue(prefab, out BoundedUnityObjectPool<GameObject> pool))
        {
            pool = new BoundedUnityObjectPool<GameObject>(Mathf.Max(1, maxRetainedInstancesPerPrefab));
            _pools.Add(prefab, pool);
        }

        return pool;
    }

    private GameObject CreateInstance(GameObject prefab, Transform parent)
    {
        GameObject instance = Instantiate(prefab, parent);
        var pooled = instance.GetComponent<PooledObject>();
        if (pooled == null)
        {
            pooled = instance.AddComponent<PooledObject>();
        }
        pooled.Initialize(this, prefab);
        instance.SetActive(false);
        return instance;
    }

    private void PrewarmInitialPools()
    {
        foreach (var config in initialPools)
        {
            if (config == null || config.prefab == null || config.prewarmCount <= 0)
            {
                continue;
            }

            Prewarm(config.prefab, config.prewarmCount, config.parentOverride);
        }
    }

    private async UniTaskVoid PrewarmBasicAttackProfilesAsync()
    {
        if (_basicAttackProfilePrewarmStarted || basicAttackProfilePrewarmCount <= 0 ||
            basicAttackProfilePrewarmKeyBudget <= 0)
        {
            return;
        }

        _basicAttackProfilePrewarmStarted = true;

        try
        {
            await UniTask.WaitUntil(() => LoadManager.Instance != null);
            await LoadManager.Instance.WaitUntilReady();

            var keys = new List<string>(CollectBasicAttackVfxKeys(LoadManager.Instance.GetAllUnitData()));
            if (keys.Count == 0)
            {
                return;
            }

            keys.Sort(StringComparer.Ordinal);

            int prewarmedCount = 0;
            Transform parent = basicAttackProfileParentOverride != null ? basicAttackProfileParentOverride : defaultParent;
            foreach (string key in keys)
            {
                if (prewarmedCount >= basicAttackProfilePrewarmKeyBudget)
                {
                    break;
                }

                GameObject prefab = await LoadAddressablePrefabAsync(key);
                if (prefab == null)
                {
                    Debug.LogWarning($"[VfxPoolManager] Basic attack VFX profile prewarm skipped. key={key}");
                    continue;
                }

                if (this == null)
                {
                    return;
                }

                Prewarm(prefab, basicAttackProfilePrewarmCount, parent);
                prewarmedCount++;
            }

            Debug.Log($"[VfxPoolManager] Bounded VFX warmup complete. keys={prewarmedCount}/{keys.Count}, " +
                      $"keyBudget={basicAttackProfilePrewarmKeyBudget}, countPerKey={basicAttackProfilePrewarmCount}");
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[VfxPoolManager] Basic attack VFX profile prewarm failed: {e.Message}");
        }
    }

    internal static HashSet<string> CollectBasicAttackVfxKeys(IEnumerable<UnitData> unitDataList)
    {
        var keys = new HashSet<string>();
        if (unitDataList == null)
        {
            return keys;
        }

        foreach (UnitData unitData in unitDataList)
        {
            if (unitData == null)
            {
                continue;
            }

            for (int starLevel = 1; starLevel <= 3; starLevel++)
            {
                BasicAttackVfxConfig slashConfig = unitData.GetBasicAttackVfxConfig(starLevel);
                if (slashConfig != null)
                {
                    AddBasicAttackVfxKey(keys, slashConfig.prefabKey);
                }
            }

            ProjectileVfxConfig projectileConfig = unitData.GetProjectileVfxConfig();
            if (projectileConfig == null)
            {
                continue;
            }

            AddBasicAttackVfxKey(keys, projectileConfig.muzzleFlashKey);
            AddBasicAttackVfxKey(keys, projectileConfig.projectileKey);
            AddBasicAttackVfxKey(keys, projectileConfig.impactFlashKey);
        }

        return keys;
    }

    private static void AddBasicAttackVfxKey(HashSet<string> keys, string key)
    {
        if (!string.IsNullOrWhiteSpace(key))
        {
            keys.Add(key.Trim());
        }
    }

    private static void ResetInstance(GameObject instance)
    {
        if (instance.TryGetComponent(out UnitAttackVfxInstance attackVfxInstance))
        {
            attackVfxInstance.ClearOwner();
        }
        if (instance.TryGetComponent(out VFXAutoDestroy autoDestroy))
        {
            autoDestroy.Cancel();
        }

        ProjectileVfxComponentCache cache = ProjectileVfxComponentCache.GetOrCreate(instance);
        TrailRenderer[] trails = cache.Trails;
        for (int i = 0; i < trails.Length; i++)
        {
            if (trails[i] != null)
            {
                trails[i].Clear();
            }
        }

        ParticleSystem[] particles = cache.Particles;
        for (int i = 0; i < particles.Length; i++)
        {
            if (particles[i] != null)
            {
                particles[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }
    }

    private async UniTask<GameObject> LoadAndRetainAddressablePrefabAsync(string key)
    {
        AddressableAssetLease<GameObject> lease = await AssetLoader.AcquireAssetAsync<GameObject>(key);
        if (lease == null)
        {
            return null;
        }

        if (this == null)
        {
            lease.Dispose();
            return null;
        }

        if (_addressablePrefabLeases.TryGetValue(key, out AddressableAssetLease<GameObject> existing))
        {
            lease.Dispose();
            return existing.Asset;
        }

        _addressablePrefabLeases.Add(key, lease);
        return lease.Asset;
    }

    private void OnDestroy()
    {
        foreach (AddressableAssetLease<GameObject> lease in _addressablePrefabLeases.Values)
        {
            lease.Dispose();
        }

        _addressablePrefabLeases.Clear();
        _addressablePrefabLoads.Clear();

        if (Instance == this)
        {
            Instance = null;
        }
    }
}
