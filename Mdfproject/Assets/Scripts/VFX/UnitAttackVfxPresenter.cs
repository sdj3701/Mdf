using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using UnityEngine;

public class UnitAttackVfxPresenter : MonoBehaviour
{
    private const uint PrimarySlashRandomSeed = 1u;

    [SerializeField] private Transform spawnOrigin;
    [SerializeField] private float forwardOffset = 0.75f;
    [SerializeField] private float heightOffset = 0.6f;
    [SerializeField] private Vector3 rotationOffsetEuler = Vector3.zero;
    [SerializeField] private float scaleMultiplier = 1f;
    [SerializeField] private float lifetimeSeconds = 2.1f;
    [SerializeField] private float minimumIntervalSeconds = 0.05f;
    [SerializeField] private bool usePool = true;

    private string _cachedKey;
    private GameObject _cachedPrefab;
    private float _lastPlayTime = -999f;
    private bool _isLoading;
    private int _playGeneration;
    private readonly List<GameObject> _activeInstances = new List<GameObject>();

    public void InvalidatePendingPlays()
    {
        unchecked
        {
            _playGeneration++;
        }

        StopActiveInstances();
    }

    private void OnDisable()
    {
        InvalidatePendingPlays();
    }

    private void OnDestroy()
    {
        InvalidatePendingPlays();
    }

    public void PlayBasicAttack(Unit unit, Transform target)
    {
        if (unit == null || unit.IsDead || unit.Data == null || unit.Data.unitType != UnitType.Melee)
        {
            return;
        }

        if (Time.time - _lastPlayTime < minimumIntervalSeconds)
        {
            return;
        }

        BasicAttackVfxConfig config = unit.Data.GetBasicAttackVfxConfig(unit.starLevel);
        if (config == null || !config.HasPrefabKey)
        {
            return;
        }

        Vector3 direction = ResolveDirection(target, config);
        if (direction.sqrMagnitude <= 1e-6f)
        {
            return;
        }

        _lastPlayTime = Time.time;
        PlayBasicAttackAsync(unit, config, direction, _playGeneration).Forget();
    }

    private async UniTaskVoid PlayBasicAttackAsync(Unit unit, BasicAttackVfxConfig config, Vector3 direction, int playGeneration)
    {
        string vfxKey = config.prefabKey;
        GameObject prefab = await LoadPrefabAsync(vfxKey);
        if (prefab == null || this == null || playGeneration != _playGeneration ||
            unit == null || unit.IsDead || !unit.gameObject.activeInHierarchy || !gameObject.activeInHierarchy)
        {
            return;
        }

        Spawn(unit, prefab, direction.normalized, config);
    }

    private async UniTask<GameObject> LoadPrefabAsync(string vfxKey)
    {
        if (_cachedPrefab != null && _cachedKey == vfxKey)
        {
            return _cachedPrefab;
        }

        if (_isLoading)
        {
            return null;
        }

        _isLoading = true;
        GameObject loaded = await AssetLoader.LoadAssetAsync<GameObject>(vfxKey);
        _isLoading = false;

        if (loaded != null)
        {
            _cachedKey = vfxKey;
            _cachedPrefab = loaded;
        }

        return loaded;
    }

    private void Spawn(Unit unit, GameObject prefab, Vector3 direction, BasicAttackVfxConfig config)
    {
        RemoveInactiveTrackedInstances();

        Transform origin = ResolveSpawnOrigin(config);
        Quaternion attackRotation = ResolveAttackRotation(unit, direction, config);
        Vector3 localOffset = config != null ? config.localPositionOffset : new Vector3(0f, heightOffset, forwardOffset);
        Vector3 eulerOffset = config != null ? config.rotationOffsetEuler : rotationOffsetEuler;
        float resolvedScale = config != null && config.scaleMultiplier > 0f ? config.scaleMultiplier : scaleMultiplier;
        Vector3 position = origin.position + attackRotation * localOffset;
        Quaternion rotation = attackRotation * Quaternion.Euler(eulerOffset);

        GameObject instance = null;
        if (usePool && VfxPoolManager.Instance != null)
        {
            instance = VfxPoolManager.Instance.Spawn(prefab, position, rotation);
        }

        if (instance == null)
        {
            instance = Instantiate(prefab, position, rotation);
        }

        TrackActiveInstance(instance);
        instance.transform.localScale = prefab.transform.localScale * resolvedScale;
        RestartParticles(instance);
        EnsureAutoDestroy(instance);
    }

    private void TrackActiveInstance(GameObject instance)
    {
        if (instance == null)
        {
            return;
        }

        var marker = instance.GetComponent<UnitAttackVfxInstance>();
        if (marker == null)
        {
            marker = instance.AddComponent<UnitAttackVfxInstance>();
        }

        marker.Initialize(this);
        _activeInstances.Add(instance);
    }

    private void StopActiveInstances()
    {
        for (int i = _activeInstances.Count - 1; i >= 0; i--)
        {
            GameObject instance = _activeInstances[i];
            _activeInstances.RemoveAt(i);
            if (instance == null)
            {
                continue;
            }

            if (!instance.TryGetComponent<UnitAttackVfxInstance>(out var marker) || !marker.IsOwnedBy(this))
            {
                continue;
            }

            marker.ClearOwner();
            if (instance.TryGetComponent<VFXAutoDestroy>(out var autoDestroy))
            {
                autoDestroy.Cancel();
            }

            StopParticles(instance);
            if (instance.TryGetComponent<PooledObject>(out var pooled))
            {
                pooled.ReturnToPool();
            }
            else
            {
                Destroy(instance);
            }
        }
    }

    private void RemoveInactiveTrackedInstances()
    {
        for (int i = _activeInstances.Count - 1; i >= 0; i--)
        {
            GameObject instance = _activeInstances[i];
            if (instance == null || !instance.activeInHierarchy)
            {
                _activeInstances.RemoveAt(i);
                continue;
            }

            if (!instance.TryGetComponent<UnitAttackVfxInstance>(out var marker) || !marker.IsOwnedBy(this))
            {
                _activeInstances.RemoveAt(i);
            }
        }
    }

    private Transform ResolveSpawnOrigin(BasicAttackVfxConfig config)
    {
        if (config != null && !string.IsNullOrWhiteSpace(config.spawnOriginPath))
        {
            Transform configuredOrigin = transform.Find(config.spawnOriginPath);
            if (configuredOrigin != null)
            {
                return configuredOrigin;
            }
        }

        return spawnOrigin != null ? spawnOrigin : transform;
    }

    private Quaternion ResolveAttackRotation(Unit unit, Vector3 direction, BasicAttackVfxConfig config)
    {
        if (config != null && config.rotationMode == BasicAttackVfxRotationMode.UnitForward)
        {
            Transform basis = unit != null ? unit.transform : transform;
            Vector3 unitForward = basis.forward;
            unitForward.y = 0f;
            if (unitForward.sqrMagnitude > 1e-6f)
            {
                return Quaternion.LookRotation(unitForward.normalized, Vector3.up);
            }
        }

        return Quaternion.LookRotation(direction, Vector3.up);
    }

    private Vector3 ResolveDirection(Transform target, BasicAttackVfxConfig config)
    {
        Transform origin = ResolveSpawnOrigin(config);
        Vector3 direction = target != null ? target.position - origin.position : transform.forward;
        direction.y = 0f;

        if (direction.sqrMagnitude <= 1e-6f)
        {
            direction = transform.forward;
            direction.y = 0f;
        }

        return direction.normalized;
    }
    private static void RestartParticles(GameObject instance)
    {
        var trails = instance.GetComponentsInChildren<TrailRenderer>(true);
        for (int i = 0; i < trails.Length; i++)
        {
            trails[i].Clear();
        }

        StopParticles(instance);

        var particles = instance.GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < particles.Length; i++)
        {
            PrepareDeterministicPrimarySlashParticle(particles[i]);

            var main = particles[i].main;
            main.loop = false;
            particles[i].Play(true);
        }
    }

    private static void PrepareDeterministicPrimarySlashParticle(ParticleSystem system)
    {
        if (system == null)
        {
            return;
        }

        var renderer = system.GetComponent<ParticleSystemRenderer>();
        if (renderer == null ||
            renderer.renderMode != ParticleSystemRenderMode.Mesh ||
            renderer.mesh == null ||
            !NameContains(renderer.mesh.name, "Slash"))
        {
            return;
        }

        Material material = renderer.sharedMaterial;
        if (material != null && !NameContains(material.name, "SwordSlash"))
        {
            return;
        }

        system.useAutoRandomSeed = false;
        system.randomSeed = PrimarySlashRandomSeed;
    }

    private static void StopParticles(GameObject instance)
    {
        var particles = instance.GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < particles.Length; i++)
        {
            var main = particles[i].main;
            main.loop = false;
            particles[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }

    private static bool NameContains(string value, string pattern)
    {
        return !string.IsNullOrEmpty(value) &&
            value.IndexOf(pattern, System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private void EnsureAutoDestroy(GameObject instance)
    {
        if (!instance.TryGetComponent<VFXAutoDestroy>(out var autoDestroy))
        {
            autoDestroy = instance.AddComponent<VFXAutoDestroy>();
        }

        autoDestroy.Initialize(lifetimeSeconds);
    }
}
