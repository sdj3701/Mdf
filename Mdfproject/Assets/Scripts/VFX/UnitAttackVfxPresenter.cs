using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using MDF.Runtime.Assets;
using UnityEngine;

public class UnitAttackVfxPresenter : MonoBehaviour
{
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
    private int _playGeneration;
    private readonly List<GameObject> _activeInstances = new List<GameObject>();
    private readonly Dictionary<string, AddressableAssetLease<GameObject>> _localPrefabLeases =
        new Dictionary<string, AddressableAssetLease<GameObject>>(System.StringComparer.Ordinal);

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

        foreach (AddressableAssetLease<GameObject> lease in _localPrefabLeases.Values)
        {
            lease.Dispose();
        }

        _localPrefabLeases.Clear();
    }

    public void PlayBasicAttack(Unit unit, Transform target)
    {
        if (unit == null || unit.IsDead || unit.Data == null || unit.Data.unitType != UnitType.Melee)
        {
            return;
        }

        PlayBasicAttackInternal(
            unit,
            unit.Data,
            unit.starLevel,
            unit.transform,
            target != null ? target.position : unit.transform.position + unit.transform.forward,
            unit.GetCappedAttackAnimationPlaybackSpeed());
    }

    public void PlayBasicAttack(
        UnitData unitData,
        int starLevel,
        Transform origin,
        Vector3 targetPosition,
        float animationPlaybackSpeed = 1f)
    {
        if (unitData == null || unitData.unitType != UnitType.Melee || origin == null)
        {
            return;
        }

        PlayBasicAttackInternal(
            null,
            unitData,
            starLevel,
            origin,
            targetPosition,
            animationPlaybackSpeed);
    }

    private void PlayBasicAttackInternal(
        Unit unit,
        UnitData unitData,
        int starLevel,
        Transform origin,
        Vector3 targetPosition,
        float animationPlaybackSpeed)
    {
        if (Time.time - _lastPlayTime < minimumIntervalSeconds)
        {
            return;
        }

        BasicAttackVfxConfig config = unitData.GetBasicAttackVfxConfig(starLevel);
        if (config == null || !config.HasPrefabKey)
        {
            return;
        }

        Vector3 direction = ResolveDirection(origin, targetPosition);
        if (direction.sqrMagnitude <= 1e-6f)
        {
            return;
        }

        _lastPlayTime = Time.time;
        PlayBasicAttackAsync(
            unit,
            unitData,
            origin,
            config,
            direction,
            Mathf.Max(0.01f, animationPlaybackSpeed),
            _playGeneration).Forget();
    }

    private async UniTaskVoid PlayBasicAttackAsync(
        Unit unit,
        UnitData unitData,
        Transform origin,
        BasicAttackVfxConfig config,
        Vector3 direction,
        float animationPlaybackSpeed,
        int playGeneration)
    {
        string vfxKey = config.prefabKey;
        GameObject prefab = await LoadPrefabAsync(vfxKey);
        if (prefab == null || this == null || playGeneration != _playGeneration ||
            unitData == null || origin == null || !origin.gameObject.activeInHierarchy ||
            (unit != null && (unit.IsDead || !unit.gameObject.activeInHierarchy)) ||
            !gameObject.activeInHierarchy)
        {
            return;
        }

        Spawn(origin, prefab, direction.normalized, config, animationPlaybackSpeed);
    }

    private async UniTask<GameObject> LoadPrefabAsync(string vfxKey)
    {
        if (_cachedPrefab != null && _cachedKey == vfxKey)
        {
            return _cachedPrefab;
        }

        VfxPoolManager poolManager = VfxPoolManager.Instance;
        GameObject loaded;
        if (poolManager != null)
        {
            loaded = await poolManager.LoadAddressablePrefabAsync(vfxKey);
        }
        else if (_localPrefabLeases.TryGetValue(vfxKey, out AddressableAssetLease<GameObject> existingLease))
        {
            if (existingLease.Asset != null)
            {
                loaded = existingLease.Asset;
            }
            else
            {
                existingLease.Dispose();
                _localPrefabLeases.Remove(vfxKey);
                loaded = await AcquireLocalPrefabAsync(vfxKey);
            }
        }
        else
        {
            loaded = await AcquireLocalPrefabAsync(vfxKey);
        }

        if (loaded != null && this != null)
        {
            _cachedKey = vfxKey;
            _cachedPrefab = loaded;
        }

        return loaded;
    }

    private async UniTask<GameObject> AcquireLocalPrefabAsync(string vfxKey)
    {
        AddressableAssetLease<GameObject> lease = await AssetLoader.AcquireAssetAsync<GameObject>(vfxKey);
        if (lease == null)
        {
            return null;
        }

        if (this == null)
        {
            lease.Dispose();
            return null;
        }

        if (_localPrefabLeases.TryGetValue(vfxKey, out AddressableAssetLease<GameObject> existingLease))
        {
            if (existingLease.Asset != null)
            {
                lease.Dispose();
                return existingLease.Asset;
            }

            existingLease.Dispose();
            _localPrefabLeases.Remove(vfxKey);
        }

        _localPrefabLeases.Add(vfxKey, lease);
        return lease.Asset;
    }

    private void Spawn(
        Transform origin,
        GameObject prefab,
        Vector3 direction,
        BasicAttackVfxConfig config,
        float animationPlaybackSpeed)
    {
        RemoveInactiveTrackedInstances();

        Quaternion attackRotation = ResolveAttackRotation(origin, direction, config);
        Vector3 localOffset = config != null ? config.localPositionOffset : new Vector3(0f, heightOffset, forwardOffset);
        Vector3 eulerOffset = config != null ? config.rotationOffsetEuler : rotationOffsetEuler;
        float resolvedScale = config != null && config.scaleMultiplier > 0f ? config.scaleMultiplier : scaleMultiplier;
        float configuredPlaybackSpeed = config != null && config.playbackSpeed > 0f ? config.playbackSpeed : 1f;
        float playbackSpeedCap = config != null ? config.ResolvePlaybackSpeedCap() : BasicAttackVfxConfig.DefaultPlaybackSpeedCap;
        float minimumVisibleSeconds = config != null ? config.ResolveMinimumVisibleSeconds() : BasicAttackVfxConfig.DefaultMinimumVisibleSeconds;
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
            if (!instance.activeInHierarchy)
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

    private static Quaternion ResolveAttackRotation(Transform basis, Vector3 direction, BasicAttackVfxConfig config)
    {
        BasicAttackVfxRotationMode rotationMode = config != null ? config.rotationMode : BasicAttackVfxRotationMode.TargetFacing;
        return BasicAttackVfxRuntimeUtility.ResolveAttackRotation(basis, direction, rotationMode);
    }

    private static Vector3 ResolveDirection(Transform origin, Vector3 targetPosition)
    {
        Vector3 direction = origin != null ? targetPosition - origin.position : Vector3.zero;
        direction.y = 0f;

        if (direction.sqrMagnitude <= 1e-6f)
        {
            direction = origin != null ? origin.forward : Vector3.forward;
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
