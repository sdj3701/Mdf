using System.Collections.Generic;
using UnityEngine;

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

    private readonly Dictionary<GameObject, Queue<GameObject>> _pools = new Dictionary<GameObject, Queue<GameObject>>();

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

            for (int i = 0; i < config.prewarmCount; i++)
            {
                var instance = CreateInstance(config.prefab, config.parentOverride);
                if (instance != null)
                {
                    Despawn(instance);
                }
            }
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
