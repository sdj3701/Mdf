using System;
using UnityEngine;

[Serializable]
public sealed class ProjectileVfxConfig
{
    public const float DefaultOneShotLifetimeSeconds = 1f;
    public const float DefaultProjectileSpeed = 10f;

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
    public float muzzleScaleMultiplier = 1f;
    public float muzzlePlaybackSpeed = 1f;
    public float muzzleLifetimeSeconds = DefaultOneShotLifetimeSeconds;

    [Header("Projectile")]
    [Tooltip("Visual projectile travel speed in world units per second.")]
    public float projectileSpeed = DefaultProjectileSpeed;
    [Tooltip("Offset in projectile direction space. X is right, Y is up, Z is forward.")]
    public Vector3 projectileLocalPositionOffset = Vector3.zero;
    public Vector3 projectileRotationOffsetEuler = Vector3.zero;
    public float projectileScaleMultiplier = 1f;
    public float projectilePlaybackSpeed = 1f;
    public bool alignProjectileToDirection = true;

    [Header("Impact Flash")]
    [Tooltip("Offset in projectile direction space. X is right, Y is up, Z is forward.")]
    public Vector3 impactLocalPositionOffset = Vector3.zero;
    public Vector3 impactRotationOffsetEuler = Vector3.zero;
    public float impactScaleMultiplier = 1f;
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
            muzzlePlaybackSpeed = 1f,
            muzzleLifetimeSeconds = DefaultOneShotLifetimeSeconds,
            projectileSpeed = DefaultProjectileSpeed,
            projectileLocalPositionOffset = Vector3.zero,
            projectileRotationOffsetEuler = Vector3.zero,
            projectileScaleMultiplier = 1f,
            projectilePlaybackSpeed = 1f,
            alignProjectileToDirection = true,
            impactLocalPositionOffset = Vector3.zero,
            impactRotationOffsetEuler = Vector3.zero,
            impactScaleMultiplier = 1f,
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
        return muzzleScaleMultiplier > 0f ? muzzleScaleMultiplier : 1f;
    }

    public float ResolveProjectileScaleMultiplier()
    {
        return projectileScaleMultiplier > 0f ? projectileScaleMultiplier : 1f;
    }

    public float ResolveProjectileSpeed(float fallbackSpeed = DefaultProjectileSpeed)
    {
        if (projectileSpeed > 0f)
        {
            return projectileSpeed;
        }

        return fallbackSpeed > 0f ? fallbackSpeed : DefaultProjectileSpeed;
    }

    public float ResolveImpactScaleMultiplier()
    {
        return impactScaleMultiplier > 0f ? impactScaleMultiplier : 1f;
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
}
