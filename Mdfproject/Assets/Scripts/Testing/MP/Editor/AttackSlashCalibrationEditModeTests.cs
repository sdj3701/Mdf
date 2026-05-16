#if UNITY_EDITOR
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public sealed class AttackSlashCalibrationEditModeTests
{
    [Test]
    public void AttackSlashCalibrationUtilitySolvesAlignedForwardFit()
    {
        var weaponPoints = new List<Vector3>
        {
            new Vector3(0f, 1f, 0f),
            new Vector3(0f, 1.1f, 0.5f),
            new Vector3(0f, 1.2f, 1f),
            new Vector3(0f, 1.1f, 1.5f),
            new Vector3(0f, 1f, 2f)
        };
        var slashPoints = new List<Vector3>
        {
            new Vector3(0f, 0f, -0.5f),
            new Vector3(0f, 0f, 0.5f),
            new Vector3(0.05f, 0.1f, 0f)
        };

        var trajectory = AttackSlashCalibrationUtility.AnalyzeTrajectory(weaponPoints, Vector3.forward, Vector3.up);
        var slashShape = AttackSlashCalibrationUtility.AnalyzePointCloud(slashPoints, Vector3.forward, Vector3.up);
        var result = AttackSlashCalibrationUtility.CalculateFit(trajectory, slashShape, Vector3.zero, Vector3.forward);

        Assert.That(result.isValid, Is.True);
        Assert.That(result.quality, Is.GreaterThan(0.9f));
        Assert.That(result.localPositionOffset.y, Is.EqualTo(1.08f).Within(0.25f));
        Assert.That(result.localPositionOffset.z, Is.EqualTo(1f).Within(0.25f));
        Assert.That(result.scaleMultiplier, Is.GreaterThan(1f));
    }

    [Test]
    public void AttackSlashCalibrationUtilityUsesStrikeSpanForAutoScale()
    {
        var weaponPoints = new List<Vector3>
        {
            new Vector3(0f, 1f, 0f),
            new Vector3(0f, 1f, 0.2f)
        };
        var slashPoints = new List<Vector3>
        {
            new Vector3(0f, 0f, -0.5f),
            new Vector3(0f, 0f, 0.5f)
        };

        var trajectory = AttackSlashCalibrationUtility.AnalyzeTrajectory(weaponPoints, Vector3.forward, Vector3.up);
        var slashShape = AttackSlashCalibrationUtility.AnalyzePointCloud(slashPoints, Vector3.forward, Vector3.up);
        var result = AttackSlashCalibrationUtility.CalculateFit(trajectory, slashShape, Vector3.zero, Vector3.forward, 2f);

        Assert.That(result.isValid, Is.True);
        Assert.That(result.scaleMultiplier, Is.EqualTo(2f).Within(0.05f));
        Assert.That(AttackSlashCalibrationUtility.ResolveTargetVisualLength(0.2f, 2f), Is.EqualTo(2f).Within(0.001f));
    }

    [Test]
    public void AttackSlashCalibrationUtilityAppliesOutputScaleMultiplier()
    {
        var weaponPoints = new List<Vector3>
        {
            new Vector3(0f, 1f, 0f),
            new Vector3(0f, 1f, 1f)
        };
        var slashPoints = new List<Vector3>
        {
            new Vector3(0f, 0f, -0.5f),
            new Vector3(0f, 0f, 0.5f)
        };

        var trajectory = AttackSlashCalibrationUtility.AnalyzeTrajectory(weaponPoints, Vector3.forward, Vector3.up);
        var slashShape = AttackSlashCalibrationUtility.AnalyzePointCloud(slashPoints, Vector3.forward, Vector3.up);
        var normalResult = AttackSlashCalibrationUtility.CalculateFit(trajectory, slashShape, Vector3.zero, Vector3.forward);
        var boostedResult = AttackSlashCalibrationUtility.CalculateFit(trajectory, slashShape, Vector3.zero, Vector3.forward, 0f, 2f);

        Assert.That(normalResult.isValid, Is.True);
        Assert.That(boostedResult.isValid, Is.True);
        Assert.That(boostedResult.scaleMultiplier, Is.EqualTo(normalResult.scaleMultiplier * 2f).Within(0.05f));
    }

    [Test]
    public void UnitDataBasicAttackVfxConfigFallsBackToLegacyKey()
    {
        var data = ScriptableObject.CreateInstance<UnitData>();
        try
        {
            data.basicAttackVfxPrefabsByStarLevel = new[] { "VFX_AttackSlash_SwordSlash5", string.Empty, string.Empty };

            BasicAttackVfxConfig config = data.GetBasicAttackVfxConfig(1);

            Assert.That(config, Is.Not.Null);
            Assert.That(config.prefabKey, Is.EqualTo("VFX_AttackSlash_SwordSlash5"));
            Assert.That(config.localPositionOffset, Is.EqualTo(new Vector3(0f, 0.6f, 0.75f)));
        }
        finally
        {
            Object.DestroyImmediate(data);
        }
    }
}
#endif
