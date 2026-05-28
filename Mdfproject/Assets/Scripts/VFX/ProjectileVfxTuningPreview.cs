using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
#endif

[DisallowMultipleComponent]
public sealed class ProjectileVfxTuningPreview : MonoBehaviour
{
    [Header("Save Target")]
    [SerializeField] private UnitData unitData;

    [Header("Preview Target")]
    [SerializeField] private Transform firePointOverride;
    [SerializeField] private Transform targetOverride;

    [Header("Muzzle Flash")]
    [SerializeField] private GameObject muzzleFlashPrefab;
    [SerializeField] private string muzzleFlashAddress;
    [SerializeField] private Vector3 muzzleLocalPositionOffset = Vector3.zero;
    [SerializeField] private Vector3 muzzleRotationOffsetEuler = Vector3.zero;
    [SerializeField] private float muzzleScaleMultiplier = 1f;
    [SerializeField] private float muzzlePlaybackSpeed = 1f;
    [SerializeField] private float muzzleLifetimeSeconds = 1f;

    [Header("Projectile")]
    [SerializeField] private GameObject projectilePrefab;
    [SerializeField] private string projectileAddress;
    [SerializeField] private Vector3 projectileLocalPositionOffset = Vector3.zero;
    [SerializeField] private Vector3 projectileRotationOffsetEuler = Vector3.zero;
    [SerializeField] private float projectileScaleMultiplier = 1f;
    [SerializeField] private float projectilePlaybackSpeed = 1f;
    [FormerlySerializedAs("projectileSpeedOverride")]
    [SerializeField] private float projectileSpeed = ProjectileVfxConfig.DefaultProjectileSpeed;
    [SerializeField] private bool alignProjectileToDirection = true;

    [Header("Impact Flash")]
    [SerializeField] private GameObject impactFlashPrefab;
    [SerializeField] private string impactFlashAddress;
    [SerializeField] private Vector3 impactLocalPositionOffset = Vector3.zero;
    [SerializeField] private Vector3 impactRotationOffsetEuler = Vector3.zero;
    [SerializeField] private float impactScaleMultiplier = 1f;
    [SerializeField] private float impactPlaybackSpeed = 1f;
    [SerializeField] private float impactLifetimeSeconds = 1f;
    [SerializeField] private bool alignImpactToDirection = true;

    [Header("Loop Preview")]
    [SerializeField] private bool loopAttackAndVfx = true;
    [SerializeField] private float previewFinalAttackSpeed = 1f;
    [SerializeField, Range(0f, 0.95f)] private float attackSpawnNormalizedTime = 0.2f;
    [SerializeField] private float previewAnimationSpeedCap = 3f;
    [SerializeField] private float fallbackAttackClipDuration = 1f;

    [Header("Animation")]
    [SerializeField] private string attackTriggerName = "AttackTrigger";

    [Header("Advanced")]
    [SerializeField] private bool pullFromUnitDataOnEnable = true;
    [SerializeField] private bool faceTargetOnEnable = true;
    [SerializeField] private bool destroyPreviewObjectsOnDisable = true;

    private readonly List<GameObject> _spawnedObjects = new List<GameObject>();
    private Animator _animator;
    private float _nextLoopTime;

    private void Awake()
    {
        _animator = GetComponent<Animator>();
        if (_animator == null)
        {
            _animator = GetComponentInChildren<Animator>(true);
        }
    }

    private void OnValidate()
    {
        muzzleScaleMultiplier = Mathf.Max(0.01f, muzzleScaleMultiplier);
        muzzlePlaybackSpeed = Mathf.Max(0.01f, muzzlePlaybackSpeed);
        muzzleLifetimeSeconds = Mathf.Max(0.01f, muzzleLifetimeSeconds);
        projectileScaleMultiplier = Mathf.Max(0.01f, projectileScaleMultiplier);
        projectilePlaybackSpeed = Mathf.Max(0.01f, projectilePlaybackSpeed);
        impactScaleMultiplier = Mathf.Max(0.01f, impactScaleMultiplier);
        impactPlaybackSpeed = Mathf.Max(0.01f, impactPlaybackSpeed);
        impactLifetimeSeconds = Mathf.Max(0.01f, impactLifetimeSeconds);
        previewFinalAttackSpeed = Mathf.Max(0.01f, previewFinalAttackSpeed);
        attackSpawnNormalizedTime = Mathf.Clamp(attackSpawnNormalizedTime, 0f, 0.95f);
        previewAnimationSpeedCap = Mathf.Max(0.01f, previewAnimationSpeedCap);
        fallbackAttackClipDuration = Mathf.Max(0.01f, fallbackAttackClipDuration);
        projectileSpeed = Mathf.Max(0.01f, projectileSpeed);

#if UNITY_EDITOR
        SyncAddressFromPrefab(muzzleFlashPrefab, ref muzzleFlashAddress);
        SyncAddressFromPrefab(projectilePrefab, ref projectileAddress);
        SyncAddressFromPrefab(impactFlashPrefab, ref impactFlashAddress);
#endif
    }

    private void OnEnable()
    {
        if (pullFromUnitDataOnEnable)
        {
            PullFromUnitData();
        }

        if (faceTargetOnEnable)
        {
            FaceTarget();
        }

        _nextLoopTime = 0f;
    }

    private void OnDisable()
    {
        if (destroyPreviewObjectsOnDisable)
        {
            DestroySpawnedPreviewObjects();
        }

        if (_animator != null)
        {
            _animator.speed = 1f;
        }
    }

    private void Update()
    {
        if (!Application.isPlaying || !loopAttackAndVfx)
        {
            return;
        }

        if (Time.time < _nextLoopTime)
        {
            return;
        }

        _nextLoopTime = Time.time + ResolvePreviewAttackIntervalSeconds();
        TriggerAttackAnimation();
        StartCoroutine(PlayProjectileSequenceAfterDelay(ResolvePreviewSpawnDelaySeconds()));
    }

    public void PullFromUnitData()
    {
        if (unitData == null)
        {
            return;
        }

        ProjectileVfxConfig config = unitData.GetProjectileVfxConfig();
        if (config == null)
        {
            return;
        }

        muzzleFlashAddress = config.muzzleFlashKey ?? string.Empty;
        projectileAddress = config.projectileKey ?? string.Empty;
        impactFlashAddress = config.impactFlashKey ?? string.Empty;
        muzzleLocalPositionOffset = config.muzzleLocalPositionOffset;
        muzzleRotationOffsetEuler = config.muzzleRotationOffsetEuler;
        muzzleScaleMultiplier = config.ResolveMuzzleScaleMultiplier();
        muzzlePlaybackSpeed = config.ResolveMuzzlePlaybackSpeed();
        muzzleLifetimeSeconds = config.ResolveMuzzleLifetimeSeconds();
        projectileLocalPositionOffset = config.projectileLocalPositionOffset;
        projectileRotationOffsetEuler = config.projectileRotationOffsetEuler;
        projectileScaleMultiplier = config.ResolveProjectileScaleMultiplier();
        projectilePlaybackSpeed = config.ResolveProjectilePlaybackSpeed();
        projectileSpeed = config.ResolveProjectileSpeed();
        alignProjectileToDirection = config.alignProjectileToDirection;
        impactLocalPositionOffset = config.impactLocalPositionOffset;
        impactRotationOffsetEuler = config.impactRotationOffsetEuler;
        impactScaleMultiplier = config.ResolveImpactScaleMultiplier();
        impactPlaybackSpeed = config.ResolveImpactPlaybackSpeed();
        impactLifetimeSeconds = config.ResolveImpactLifetimeSeconds();
        alignImpactToDirection = config.alignImpactToDirection;

#if UNITY_EDITOR
        muzzleFlashPrefab = ResolveEditorAddressableGameObject(muzzleFlashAddress);
        projectilePrefab = ResolveEditorAddressableGameObject(projectileAddress);
        impactFlashPrefab = ResolveEditorAddressableGameObject(impactFlashAddress);
#endif
    }

    public void CopySettingsToUnitData(bool saveAsset)
    {
        if (unitData == null)
        {
            Debug.LogWarning($"[ProjectileVfxTuningPreview] UnitData is missing on {name}.");
            return;
        }

#if UNITY_EDITOR
        BasicAttackVfxProfile profile = unitData.basicAttackVfxProfile;
        if (profile == null)
        {
            Debug.LogWarning($"[ProjectileVfxTuningPreview] BasicAttackVfxProfile is missing on {unitData.name}.");
            return;
        }

        Undo.RecordObject(profile, "Save Projectile VFX Tuning");
#else
        BasicAttackVfxProfile profile = unitData.basicAttackVfxProfile;
        if (profile == null)
        {
            Debug.LogWarning($"[ProjectileVfxTuningPreview] BasicAttackVfxProfile is missing on {unitData.name}.");
            return;
        }
#endif

        ProjectileVfxConfig config = profile.GetProjectileConfig();
        config.muzzleFlashKey = muzzleFlashAddress ?? string.Empty;
        config.projectileKey = projectileAddress ?? string.Empty;
        config.impactFlashKey = impactFlashAddress ?? string.Empty;
        config.muzzleLocalPositionOffset = muzzleLocalPositionOffset;
        config.muzzleRotationOffsetEuler = muzzleRotationOffsetEuler;
        config.muzzleScaleMultiplier = Mathf.Max(0.01f, muzzleScaleMultiplier);
        config.muzzlePlaybackSpeed = Mathf.Max(0.01f, muzzlePlaybackSpeed);
        config.muzzleLifetimeSeconds = Mathf.Max(0.01f, muzzleLifetimeSeconds);
        config.projectileLocalPositionOffset = projectileLocalPositionOffset;
        config.projectileRotationOffsetEuler = projectileRotationOffsetEuler;
        config.projectileScaleMultiplier = Mathf.Max(0.01f, projectileScaleMultiplier);
        config.projectilePlaybackSpeed = Mathf.Max(0.01f, projectilePlaybackSpeed);
        config.projectileSpeed = Mathf.Max(0.01f, projectileSpeed);
        config.alignProjectileToDirection = alignProjectileToDirection;
        config.impactLocalPositionOffset = impactLocalPositionOffset;
        config.impactRotationOffsetEuler = impactRotationOffsetEuler;
        config.impactScaleMultiplier = Mathf.Max(0.01f, impactScaleMultiplier);
        config.impactPlaybackSpeed = Mathf.Max(0.01f, impactPlaybackSpeed);
        config.impactLifetimeSeconds = Mathf.Max(0.01f, impactLifetimeSeconds);
        config.alignImpactToDirection = alignImpactToDirection;

#if UNITY_EDITOR
        EditorUtility.SetDirty(profile);
        if (saveAsset)
        {
            AssetDatabase.SaveAssets();
        }
#endif

        Debug.Log($"[ProjectileVfxTuningPreview] Saved projectile VFX tuning to {profile.name}. unit={unitData.name}");
    }

    public float ResolvePreviewAttackIntervalSeconds()
    {
        return 1f / ResolveCappedPreviewAttackRate();
    }

    public float ResolvePreviewSpawnDelaySeconds()
    {
        return Mathf.Clamp01(attackSpawnNormalizedTime) / ResolveCappedPreviewAttackRate();
    }

    private IEnumerator PlayProjectileSequenceAfterDelay(float delaySeconds)
    {
        if (delaySeconds > 0f)
        {
            yield return new WaitForSeconds(delaySeconds);
        }

        if (this == null || !isActiveAndEnabled)
        {
            yield break;
        }

        Vector3 firePos = ResolveFirePosition();
        Vector3 targetPos = ResolveTargetPosition(firePos);
        Vector3 direction = ResolveTravelDirection(firePos, targetPos);

        SpawnOneShot(muzzleFlashPrefab, muzzleFlashAddress, firePos, direction, muzzleLocalPositionOffset, muzzleRotationOffsetEuler, true,
            muzzleScaleMultiplier, muzzlePlaybackSpeed, muzzleLifetimeSeconds);

        GameObject projectile = SpawnProjectileVisual(firePos, targetPos, direction);
        float travelSeconds = ResolveProjectileTravelSeconds(firePos, targetPos);
        if (projectile != null)
        {
            yield return MoveProjectile(projectile, firePos, targetPos, travelSeconds);
            DestroyPreviewObject(projectile);
        }
        else if (travelSeconds > 0f)
        {
            yield return new WaitForSeconds(travelSeconds);
        }

        SpawnOneShot(impactFlashPrefab, impactFlashAddress, targetPos, direction, impactLocalPositionOffset, impactRotationOffsetEuler, alignImpactToDirection,
            impactScaleMultiplier, impactPlaybackSpeed, impactLifetimeSeconds);
    }

    private GameObject SpawnProjectileVisual(Vector3 firePos, Vector3 targetPos, Vector3 direction)
    {
        if (projectilePrefab == null)
        {
            return null;
        }

        Quaternion rotation = ProjectileVfxRuntimeUtility.ResolveVfxRotation(direction, alignProjectileToDirection, projectileRotationOffsetEuler);
        Vector3 position = ProjectileVfxRuntimeUtility.ApplyLocalOffset(firePos, rotation, projectileLocalPositionOffset);
        GameObject instance = Instantiate(projectilePrefab, position, rotation, transform.parent);
        instance.name = $"{projectilePrefab.name}_ProjectilePreview_{name}";
        instance.transform.localScale = ProjectileVfxRuntimeUtility.MultiplyScale(projectilePrefab.transform.localScale, projectileScaleMultiplier);
        ProjectileVfxRuntimeUtility.PrepareVisualProjectile(instance);
        ProjectileVfxRuntimeUtility.RestartParticles(instance, projectilePlaybackSpeed);
        _spawnedObjects.Add(instance);
        return instance;
    }

    private IEnumerator MoveProjectile(GameObject projectile, Vector3 firePos, Vector3 targetPos, float travelSeconds)
    {
        float elapsed = 0f;
        float duration = Mathf.Max(0.01f, travelSeconds);
        Vector3 direction = ResolveTravelDirection(firePos, targetPos);

        while (projectile != null && elapsed < duration)
        {
            float t = Mathf.Clamp01(elapsed / duration);
            Vector3 pathPos = Vector3.Lerp(firePos, targetPos, t);
            Quaternion rotation = ProjectileVfxRuntimeUtility.ResolveVfxRotation(direction, alignProjectileToDirection, projectileRotationOffsetEuler);
            projectile.transform.SetPositionAndRotation(
                ProjectileVfxRuntimeUtility.ApplyLocalOffset(pathPos, rotation, projectileLocalPositionOffset),
                rotation);
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (projectile != null)
        {
            Quaternion rotation = ProjectileVfxRuntimeUtility.ResolveVfxRotation(direction, alignProjectileToDirection, projectileRotationOffsetEuler);
            projectile.transform.SetPositionAndRotation(
                ProjectileVfxRuntimeUtility.ApplyLocalOffset(targetPos, rotation, projectileLocalPositionOffset),
                rotation);
        }
    }

    private void SpawnOneShot(GameObject prefab, string address, Vector3 basePosition, Vector3 direction, Vector3 localOffset, Vector3 rotationOffset, bool alignToDirection, float scale, float playbackSpeed, float lifetimeSeconds)
    {
        if (prefab == null || string.IsNullOrWhiteSpace(address))
        {
            return;
        }

        Quaternion rotation = ProjectileVfxRuntimeUtility.ResolveVfxRotation(direction, alignToDirection, rotationOffset);
        Vector3 position = ProjectileVfxRuntimeUtility.ApplyLocalOffset(basePosition, rotation, localOffset);
        GameObject instance = Instantiate(prefab, position, rotation, transform.parent);
        instance.name = $"{prefab.name}_OneShotPreview_{name}";
        instance.transform.localScale = ProjectileVfxRuntimeUtility.MultiplyScale(prefab.transform.localScale, scale);
        ProjectileVfxRuntimeUtility.RestartParticles(instance, playbackSpeed);
        _spawnedObjects.Add(instance);
        Destroy(instance, Mathf.Max(0.01f, lifetimeSeconds));
    }

    private Vector3 ResolveFirePosition()
    {
        if (firePointOverride != null)
        {
            return firePointOverride.position;
        }

        Unit unit = GetComponent<Unit>();
        if (unit != null && unit.firePoint != null)
        {
            return unit.firePoint.position;
        }

        return transform.position;
    }

    private Vector3 ResolveTargetPosition(Vector3 fallback)
    {
        return targetOverride != null ? targetOverride.position : fallback + transform.forward * 4f;
    }

    private Vector3 ResolveTravelDirection(Vector3 firePos, Vector3 targetPos)
    {
        Vector3 direction = targetPos - firePos;
        if (direction.sqrMagnitude <= 1e-6f)
        {
            direction = transform.forward;
        }

        return direction.sqrMagnitude > 1e-6f ? direction.normalized : Vector3.forward;
    }

    private float ResolveProjectileTravelSeconds(Vector3 firePos, Vector3 targetPos)
    {
        float speed = projectileSpeed > 0f ? projectileSpeed : ProjectileVfxConfig.DefaultProjectileSpeed;
        return Vector3.Distance(firePos, targetPos) / speed;
    }

    private void TriggerAttackAnimation()
    {
        if (_animator == null)
        {
            _animator = GetComponent<Animator>();
            if (_animator == null)
            {
                _animator = GetComponentInChildren<Animator>(true);
            }
        }

        if (_animator == null || string.IsNullOrWhiteSpace(attackTriggerName))
        {
            return;
        }

        _animator.speed = ResolvePreviewAnimationPlaybackSpeed();
        _animator.ResetTrigger(attackTriggerName);
        _animator.SetTrigger(attackTriggerName);
    }

    private void FaceTarget()
    {
        if (targetOverride == null)
        {
            return;
        }

        Vector3 direction = targetOverride.position - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude <= 1e-6f)
        {
            return;
        }

        transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
    }

    private float ResolvePreviewAnimationPlaybackSpeed()
    {
        float clipDuration = ResolveAttackClipDuration();
        return Mathf.Max(0.01f, clipDuration * ResolveCappedPreviewAttackRate());
    }

    private float ResolveCappedPreviewAttackRate()
    {
        return Mathf.Min(Mathf.Max(0.01f, previewFinalAttackSpeed), Mathf.Max(0.01f, previewAnimationSpeedCap));
    }

    private float ResolveAttackClipDuration()
    {
        if (_animator != null && _animator.runtimeAnimatorController != null)
        {
            AnimationClip[] clips = _animator.runtimeAnimatorController.animationClips;
            if (clips != null)
            {
                foreach (AnimationClip clip in clips)
                {
                    if (clip != null && clip.name.IndexOf("attack", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return Mathf.Max(0.01f, clip.length);
                    }
                }
            }
        }

        return Mathf.Max(0.01f, fallbackAttackClipDuration);
    }

    private void DestroySpawnedPreviewObjects()
    {
        for (int i = _spawnedObjects.Count - 1; i >= 0; i--)
        {
            DestroyPreviewObject(_spawnedObjects[i]);
        }

        _spawnedObjects.Clear();
    }

    private void DestroyPreviewObject(GameObject previewObject)
    {
        if (previewObject == null)
        {
            return;
        }

        _spawnedObjects.Remove(previewObject);
        Destroy(previewObject);
    }

#if UNITY_EDITOR
    private static void SyncAddressFromPrefab(GameObject prefab, ref string address)
    {
        if (prefab != null)
        {
            address = prefab.name;
        }
    }

    private static GameObject ResolveEditorAddressableGameObject(string address)
    {
        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null || string.IsNullOrWhiteSpace(address))
        {
            return null;
        }

        foreach (AddressableAssetGroup group in settings.groups)
        {
            if (group == null)
            {
                continue;
            }

            foreach (AddressableAssetEntry entry in group.entries)
            {
                if (entry == null || entry.address != address)
                {
                    continue;
                }

                string assetPath = AssetDatabase.GUIDToAssetPath(entry.guid);
                return string.IsNullOrWhiteSpace(assetPath) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            }
        }

        return null;
    }
#endif
}
