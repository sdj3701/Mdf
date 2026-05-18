using System;
using UnityEngine;

public enum BasicAttackVfxRotationMode
{
    TargetFacing = 0,
    UnitForward = 1
}

[Serializable]
public sealed class BasicAttackVfxConfig
{
    public const float DefaultPlaybackSpeedCap = 2f;
    public const float DefaultMinimumVisibleSeconds = 0.16f;

    [AddressableKey(typeof(GameObject))]
    public string prefabKey;

    [Tooltip("Optional transform path relative to the unit prefab root. Empty means the unit root is used.")]
    public string spawnOriginPath;

    [Tooltip("Offset in attack-direction space. X is right, Y is up, Z is forward.")]
    public Vector3 localPositionOffset = new Vector3(0f, 0.6f, 0.75f);

    [Tooltip("Rotation applied after the selected attack rotation.")]
    public Vector3 rotationOffsetEuler = Vector3.zero;

    [Tooltip("TargetFacing is legacy behavior. UnitForward matches the slash calibrator's sampled animation space.")]
    public BasicAttackVfxRotationMode rotationMode = BasicAttackVfxRotationMode.TargetFacing;

    public float scaleMultiplier = 1f;

    [Tooltip("Normalized attack animation time used by the tuning preview to spawn the slash. 0 is immediate, 1 is the end of the attack state.")]
    [Range(0f, 0.95f)]
    public float spawnNormalizedTime = 0.2f;

    [Tooltip("Particle playback speed for this slash VFX. 1 is the prefab's original speed.")]
    public float playbackSpeed = 1f;

    [Tooltip("Maximum resolved particle playback speed after attack animation speed is applied. Keeps fast attacks readable.")]
    public float playbackSpeedCap = DefaultPlaybackSpeedCap;

    [Tooltip("Minimum time this VFX object stays alive, even when playback speed is high.")]
    public float minimumVisibleSeconds = DefaultMinimumVisibleSeconds;

    [Tooltip("Renderer flip applied to the primary slash mesh particle. Use this for visual sweep direction without moving the VFX root.")]
    public Vector3 primaryRendererFlip = Vector3.zero;

    [Range(0f, 1f)]
    public float calibrationQuality;

    public string calibratedAttackClipGuid;

    public string calibrationSource;

    public bool HasPrefabKey => !string.IsNullOrWhiteSpace(prefabKey);

    public static BasicAttackVfxConfig CreateDefault(string key = null)
    {
        return new BasicAttackVfxConfig
        {
            prefabKey = key ?? string.Empty,
            localPositionOffset = new Vector3(0f, 0.6f, 0.75f),
            rotationOffsetEuler = Vector3.zero,
            rotationMode = BasicAttackVfxRotationMode.TargetFacing,
            scaleMultiplier = 1f,
            spawnNormalizedTime = 0.2f,
            playbackSpeed = 1f,
            playbackSpeedCap = DefaultPlaybackSpeedCap,
            minimumVisibleSeconds = DefaultMinimumVisibleSeconds,
            primaryRendererFlip = Vector3.zero,
            calibrationQuality = 0f
        };
    }

    public float ResolvePlaybackSpeedCap()
    {
        return playbackSpeedCap > 0f ? playbackSpeedCap : DefaultPlaybackSpeedCap;
    }

    public float ResolveMinimumVisibleSeconds()
    {
        return minimumVisibleSeconds > 0f ? minimumVisibleSeconds : DefaultMinimumVisibleSeconds;
    }
}
