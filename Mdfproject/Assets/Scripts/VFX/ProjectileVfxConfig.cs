using System;
using UnityEngine;

[Serializable]
public sealed class ProjectileVfxConfig
{
    public const float DefaultOneShotLifetimeSeconds = 1f;
    public const float DefaultProjectileSpeed = 10f;
    public const float DefaultProjectileVisualHeightOffset = 0.65f;
    public const float DefaultProjectileDynamicLightIntensity = 0f;
    public const float DefaultProjectileDynamicLightRange = 0f;

    [Header("Addressables")]
    [AddressableKey(typeof(GameObject))]
    public string muzzleFlashKey;

    [AddressableKey(typeof(GameObject))]
    public string projectileKey;

    [AddressableKey(typeof(GameObject))]
    public string impactFlashKey;

    [Header("Muzzle Flash")]
    [Tooltip("Offset in projectile direction space. X is right, Y is up, Z is forward.")]
    public Vector3 muzzleLocalPositionOffset = Vector3.zero;
    public Vector3 muzzleRotationOffsetEuler = Vector3.zero;
    [HideInInspector]
    public float muzzleScaleMultiplier = 1f;
    [InspectorName("Muzzle Scale Multiplier")]
    public Vector3 muzzleScaleMultiplierVector = Vector3.one;
    public float muzzlePlaybackSpeed = 1f;
    public float muzzleLifetimeSeconds = DefaultOneShotLifetimeSeconds;

    [Header("Projectile")]
    [Tooltip("Visual projectile travel speed in world units per second.")]
    public float projectileSpeed = DefaultProjectileSpeed;
    [Tooltip("Raises only the visual projectile path so it does not clip through board walls. Impact/muzzle positions stay unchanged.")]
    public float projectileVisualHeightOffset = DefaultProjectileVisualHeightOffset;
    [Tooltip("0 disables projectile dynamic lights. Use a small value only for VFX that become unreadable without light.")]
    public float projectileDynamicLightIntensity = DefaultProjectileDynamicLightIntensity;
    [Tooltip("0 disables projectile dynamic lights. Keep this low to avoid visible light circles on the board.")]
    public float projectileDynamicLightRange = DefaultProjectileDynamicLightRange;
    [Tooltip("Offset in projectile direction space. X is right, Y is up, Z is forward.")]
    public Vector3 projectileLocalPositionOffset = Vector3.zero;
    public Vector3 projectileRotationOffsetEuler = Vector3.zero;
    [HideInInspector]
    public float projectileScaleMultiplier = 1f;
    [InspectorName("Projectile Scale Multiplier")]
    public Vector3 projectileScaleMultiplierVector = Vector3.one;
    public float projectilePlaybackSpeed = 1f;
    public bool alignProjectileToDirection = true;

    [Header("Impact Flash")]
    [Tooltip("Offset in projectile direction space. X is right, Y is up, Z is forward.")]
    public Vector3 impactLocalPositionOffset = Vector3.zero;
    public Vector3 impactRotationOffsetEuler = Vector3.zero;
    [HideInInspector]
    public float impactScaleMultiplier = 1f;
    [InspectorName("Impact Scale Multiplier")]
    public Vector3 impactScaleMultiplierVector = Vector3.one;
    public float impactPlaybackSpeed = 1f;
    public float impactLifetimeSeconds = DefaultOneShotLifetimeSeconds;
    public bool alignImpactToDirection = true;

    public bool HasMuzzleFlashKey => !string.IsNullOrWhiteSpace(muzzleFlashKey);
    public bool HasProjectileKey => !string.IsNullOrWhiteSpace(projectileKey);
    public bool HasImpactFlashKey => !string.IsNullOrWhiteSpace(impactFlashKey);

    public static ProjectileVfxConfig CreateDefault(string projectileKey = null)
    {
        return new ProjectileVfxConfig
        {
            muzzleFlashKey = string.Empty,
            projectileKey = projectileKey ?? string.Empty,
            impactFlashKey = string.Empty,
            muzzleLocalPositionOffset = Vector3.zero,
            muzzleRotationOffsetEuler = Vector3.zero,
            muzzleScaleMultiplier = 1f,
            muzzleScaleMultiplierVector = Vector3.one,
            muzzlePlaybackSpeed = 1f,
            muzzleLifetimeSeconds = DefaultOneShotLifetimeSeconds,
            projectileSpeed = DefaultProjectileSpeed,
            projectileVisualHeightOffset = DefaultProjectileVisualHeightOffset,
            projectileDynamicLightIntensity = DefaultProjectileDynamicLightIntensity,
            projectileDynamicLightRange = DefaultProjectileDynamicLightRange,
            projectileLocalPositionOffset = Vector3.zero,
            projectileRotationOffsetEuler = Vector3.zero,
            projectileScaleMultiplier = 1f,
            projectileScaleMultiplierVector = Vector3.one,
            projectilePlaybackSpeed = 1f,
            alignProjectileToDirection = true,
            impactLocalPositionOffset = Vector3.zero,
            impactRotationOffsetEuler = Vector3.zero,
            impactScaleMultiplier = 1f,
            impactScaleMultiplierVector = Vector3.one,
            impactPlaybackSpeed = 1f,
            impactLifetimeSeconds = DefaultOneShotLifetimeSeconds,
            alignImpactToDirection = true
        };
    }

    public float ResolveMuzzleLifetimeSeconds()
    {
        return muzzleLifetimeSeconds > 0f ? muzzleLifetimeSeconds : DefaultOneShotLifetimeSeconds;
    }

    public float ResolveImpactLifetimeSeconds()
    {
        return impactLifetimeSeconds > 0f ? impactLifetimeSeconds : DefaultOneShotLifetimeSeconds;
    }

    public float ResolveMuzzleScaleMultiplier()
    {
        return ResolveUniformScaleMultiplier(ResolveMuzzleScaleMultiplierVector());
    }

    public Vector3 ResolveMuzzleScaleMultiplierVector()
    {
        return ResolveScaleMultiplierVector(muzzleScaleMultiplierVector, muzzleScaleMultiplier);
    }

    public float ResolveProjectileScaleMultiplier()
    {
        return ResolveUniformScaleMultiplier(ResolveProjectileScaleMultiplierVector());
    }

    public Vector3 ResolveProjectileScaleMultiplierVector()
    {
        return ResolveScaleMultiplierVector(projectileScaleMultiplierVector, projectileScaleMultiplier);
    }

    public float ResolveProjectileSpeed(float fallbackSpeed = DefaultProjectileSpeed)
    {
        if (projectileSpeed > 0f)
        {
            return projectileSpeed;
        }

        return fallbackSpeed > 0f ? fallbackSpeed : DefaultProjectileSpeed;
    }

    public float ResolveProjectileVisualHeightOffset()
    {
        return Mathf.Max(0f, projectileVisualHeightOffset);
    }

    public float ResolveProjectileDynamicLightIntensity()
    {
        return Mathf.Max(0f, projectileDynamicLightIntensity);
    }

    public float ResolveProjectileDynamicLightRange()
    {
        return Mathf.Max(0f, projectileDynamicLightRange);
    }

    public float ResolveImpactScaleMultiplier()
    {
        return ResolveUniformScaleMultiplier(ResolveImpactScaleMultiplierVector());
    }

    public Vector3 ResolveImpactScaleMultiplierVector()
    {
        return ResolveScaleMultiplierVector(impactScaleMultiplierVector, impactScaleMultiplier);
    }

    public float ResolveMuzzlePlaybackSpeed()
    {
        return muzzlePlaybackSpeed > 0f ? muzzlePlaybackSpeed : 1f;
    }

    public float ResolveProjectilePlaybackSpeed()
    {
        return projectilePlaybackSpeed > 0f ? projectilePlaybackSpeed : 1f;
    }

    public float ResolveImpactPlaybackSpeed()
    {
        return impactPlaybackSpeed > 0f ? impactPlaybackSpeed : 1f;
    }

    private static Vector3 ResolveScaleMultiplierVector(Vector3 configuredScale, float legacyUniformScale)
    {
        float resolvedLegacyScale = legacyUniformScale > 0f ? legacyUniformScale : 1f;
        bool configuredScaleIsValid = configuredScale.x > 0f && configuredScale.y > 0f && configuredScale.z > 0f;
        bool configuredScaleIsDefault = Approximately(configuredScale.x, 1f)
            && Approximately(configuredScale.y, 1f)
            && Approximately(configuredScale.z, 1f);

        if (configuredScaleIsValid && (!configuredScaleIsDefault || Approximately(resolvedLegacyScale, 1f)))
        {
            return SanitizeScaleMultiplierVector(configuredScale);
        }

        return Vector3.one * resolvedLegacyScale;
    }

    private static Vector3 SanitizeScaleMultiplierVector(Vector3 scale)
    {
        return new Vector3(
            Mathf.Max(0.01f, scale.x),
            Mathf.Max(0.01f, scale.y),
            Mathf.Max(0.01f, scale.z));
    }

    private static float ResolveUniformScaleMultiplier(Vector3 scale)
    {
        return Mathf.Max(0.01f, (scale.x + scale.y + scale.z) / 3f);
    }

    private static bool Approximately(float a, float b)
    {
        return Mathf.Abs(a - b) <= 0.0001f;
    }
}
