using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using UnityEngine;

public class UnitAttackVfxPresenter : MonoBehaviour
{
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
        float configuredPlaybackSpeed = config != null && config.playbackSpeed > 0f ? config.playbackSpeed : 1f;
        float playbackSpeedCap = config != null ? config.ResolvePlaybackSpeedCap() : BasicAttackVfxConfig.DefaultPlaybackSpeedCap;
        float minimumVisibleSeconds = config != null ? config.ResolveMinimumVisibleSeconds() : BasicAttackVfxConfig.DefaultMinimumVisibleSeconds;
        float animationPlaybackSpeed = unit != null ? unit.GetCappedAttackAnimationPlaybackSpeed() : 1f;
        float playbackSpeed = BasicAttackVfxRuntimeUtility.ResolvePlaybackSpeed(configuredPlaybackSpeed, animationPlaybackSpeed, playbackSpeedCap);
        Vector3 primaryRendererFlip = config != null ? config.primaryRendererFlip : Vector3.zero;
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
        BasicAttackVfxRuntimeUtility.RestartParticles(instance, primaryRendererFlip, playbackSpeed);
        EnsureAutoDestroy(instance, playbackSpeed, minimumVisibleSeconds);
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

            BasicAttackVfxRuntimeUtility.StopParticles(instance);
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
        Transform basis = unit != null ? unit.transform : transform;
        BasicAttackVfxRotationMode rotationMode = config != null ? config.rotationMode : BasicAttackVfxRotationMode.TargetFacing;
        return BasicAttackVfxRuntimeUtility.ResolveAttackRotation(basis, direction, rotationMode);
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

    private void EnsureAutoDestroy(GameObject instance, float playbackSpeed, float minimumVisibleSeconds)
    {
        if (!instance.TryGetComponent<VFXAutoDestroy>(out var autoDestroy))
        {
            autoDestroy = instance.AddComponent<VFXAutoDestroy>();
        }

        autoDestroy.Initialize(BasicAttackVfxRuntimeUtility.ResolveLifetimeSeconds(lifetimeSeconds, playbackSpeed, minimumVisibleSeconds));
    }
}
