using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Serialization;

public class VfxPoolManager : MonoBehaviour
{
    [System.Serializable]
    private class PoolConfig
    {
        public GameObject prefab;
        public int prewarmCount = 8;
        public Transform parentOverride;
    }

    [SerializeField] private List<PoolConfig> initialPools = new List<PoolConfig>();
    [SerializeField] private Transform defaultParent;
    [SerializeField] private bool prewarmOnStart = true;

    [Header("Basic Attack VFX Profile Prewarm")]
    [SerializeField, FormerlySerializedAs("prewarmProjectileProfilesOnStart")] private bool prewarmBasicAttackProfilesOnStart = true;
    [SerializeField, FormerlySerializedAs("projectileProfilePrewarmCount"), Min(0)] private int basicAttackProfilePrewarmCount = 20;
    [SerializeField, FormerlySerializedAs("projectileProfileParentOverride")] private Transform basicAttackProfileParentOverride;

    private readonly Dictionary<GameObject, Queue<GameObject>> _pools = new Dictionary<GameObject, Queue<GameObject>>();
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

        if (!_pools.TryGetValue(prefab, out var pool))
        {
            pool = new Queue<GameObject>();
            _pools[prefab] = pool;
        }

        GameObject instance = pool.Count > 0 ? pool.Dequeue() : CreateInstance(prefab, parent);
        if (instance == null)
        {
            return null;
        }

        var targetParent = parent != null ? parent : defaultParent;
        instance.transform.SetParent(targetParent, false);
        instance.transform.SetPositionAndRotation(position, rotation);
        instance.SetActive(true);
        return instance;
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

        ResetInstance(instance);
        instance.SetActive(false);
        instance.transform.SetParent(defaultParent, false);

        if (!_pools.TryGetValue(pooled.OriginPrefab, out var pool))
        {
            pool = new Queue<GameObject>();
            _pools[pooled.OriginPrefab] = pool;
        }
        pool.Enqueue(instance);
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

        if (!_pools.TryGetValue(prefab, out var pool))
        {
            pool = new Queue<GameObject>();
            _pools[prefab] = pool;
        }

        Transform targetParent = parent != null ? parent : defaultParent;
        for (int i = pool.Count; i < count; i++)
        {
            var instance = CreateInstance(prefab, targetParent);
            if (instance != null)
            {
                Despawn(instance);
            }
        }
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
        if (_basicAttackProfilePrewarmStarted || basicAttackProfilePrewarmCount <= 0)
        {
            return;
        }

        _basicAttackProfilePrewarmStarted = true;

        try
        {
            await UniTask.WaitUntil(() => LoadManager.Instance != null);
            await LoadManager.Instance.WaitUntilReady();

            HashSet<string> keys = CollectBasicAttackVfxKeys(LoadManager.Instance.GetAllUnitData());
            if (keys.Count == 0)
            {
                return;
            }

            int prewarmedCount = 0;
            Transform parent = basicAttackProfileParentOverride != null ? basicAttackProfileParentOverride : defaultParent;
            foreach (string key in keys)
            {
                GameObject prefab = await AssetLoader.LoadAssetAsync<GameObject>(key);
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

            Debug.Log($"[VfxPoolManager] Basic attack VFX profile prewarm complete. keys={prewarmedCount}/{keys.Count}, countPerKey={basicAttackProfilePrewarmCount}");
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
        var trails = instance.GetComponentsInChildren<TrailRenderer>(true);
        for (int i = 0; i < trails.Length; i++)
        {
            trails[i].Clear();
        }

        var particles = instance.GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < particles.Length; i++)
        {
            particles[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }
}
