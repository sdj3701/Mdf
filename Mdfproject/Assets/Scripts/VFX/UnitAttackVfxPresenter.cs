using Cysharp.Threading.Tasks;
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

    public void PlayBasicAttack(Unit unit, Transform target)
    {
        if (unit == null || unit.Data == null || unit.Data.unitType != UnitType.Melee)
        {
            return;
        }

        if (Time.time - _lastPlayTime < minimumIntervalSeconds)
        {
            return;
        }

        if (!TryResolveVfxKey(unit, out string vfxKey))
        {
            return;
        }

        Vector3 direction = ResolveDirection(target);
        if (direction.sqrMagnitude <= 1e-6f)
        {
            return;
        }

        _lastPlayTime = Time.time;
        PlayBasicAttackAsync(vfxKey, direction).Forget();
    }

    private async UniTaskVoid PlayBasicAttackAsync(string vfxKey, Vector3 direction)
    {
        GameObject prefab = await LoadPrefabAsync(vfxKey);
        if (prefab == null || this == null || !gameObject.activeInHierarchy)
        {
            return;
        }

        Spawn(prefab, direction.normalized);
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

    private void Spawn(GameObject prefab, Vector3 direction)
    {
        Transform origin = spawnOrigin != null ? spawnOrigin : transform;
        Vector3 position = origin.position + Vector3.up * heightOffset + direction * forwardOffset;
        Quaternion rotation = Quaternion.LookRotation(direction, Vector3.up) * Quaternion.Euler(rotationOffsetEuler);

        GameObject instance = null;
        if (usePool && VfxPoolManager.Instance != null)
        {
            instance = VfxPoolManager.Instance.Spawn(prefab, position, rotation);
        }

        if (instance == null)
        {
            instance = Instantiate(prefab, position, rotation);
        }

        instance.transform.localScale = prefab.transform.localScale * scaleMultiplier;
        RestartParticles(instance);
        EnsureAutoDestroy(instance);
    }

    private Vector3 ResolveDirection(Transform target)
    {
        Transform origin = spawnOrigin != null ? spawnOrigin : transform;
        Vector3 direction = target != null ? target.position - origin.position : transform.forward;
        direction.y = 0f;

        if (direction.sqrMagnitude <= 1e-6f)
        {
            direction = transform.forward;
            direction.y = 0f;
        }

        return direction.normalized;
    }

    private static bool TryResolveVfxKey(Unit unit, out string vfxKey)
    {
        vfxKey = null;
        string[] keys = unit.Data.basicAttackVfxPrefabsByStarLevel;
        if (keys == null || keys.Length == 0)
        {
            return false;
        }

        int starIndex = Mathf.Clamp(unit.starLevel - 1, 0, keys.Length - 1);
        vfxKey = keys[starIndex];
        return !string.IsNullOrWhiteSpace(vfxKey);
    }

    private static void RestartParticles(GameObject instance)
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
            particles[i].Play(true);
        }
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
