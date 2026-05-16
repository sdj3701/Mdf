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
            calibrationQuality = 0f
        };
    }
}
