#if UNITY_EDITOR
using System.IO;
using NUnit.Framework;

public sealed class LocalVfxVisibilityEditModeTests
{
    [Test]
    public void FieldOwnerVisibilityAllowsOnlyViewedField()
    {
        Assert.That(LocalVfxVisibility.ShouldPlayFieldOwner(1, 1), Is.True);
        Assert.That(LocalVfxVisibility.ShouldPlayFieldOwner(1, 2), Is.False);
    }

    [Test]
    public void FieldOwnerVisibilityFailsOpenWhenContextIsUnknown()
    {
        Assert.That(LocalVfxVisibility.ShouldPlayFieldOwner(-1, 2), Is.True);
        Assert.That(LocalVfxVisibility.ShouldPlayFieldOwner(2, -1), Is.True);
        Assert.That(LocalVfxVisibility.ShouldPlayFieldOwner(-1, -1), Is.True);
    }

    [Test]
    public void CombatSchedulerFiltersLocalVfxRpcPlayback()
    {
        string schedulerSource = File.ReadAllText("Assets/Scripts/Managers/CombatScheduler.cs");

        Assert.That(schedulerSource, Does.Contain("LocalVfxVisibility.ShouldPlay(attacker, target, LocalVfxVisibilityEventKind.Projectile)"));
        Assert.That(schedulerSource, Does.Contain("ProjectileVfxManager.RecordSkippedCombatEvent(Runner, attacker, target, fireTick, hitTick);"));
        Assert.That(schedulerSource, Does.Contain("ProjectileVfxManager.PlayFromCombatEvent(Runner, attacker, target, fireTick, hitTick);"));
        Assert.That(schedulerSource, Does.Contain("LocalVfxVisibility.ShouldPlay(attacker, target, LocalVfxVisibilityEventKind.BasicAttack)"));
        Assert.That(schedulerSource, Does.Contain("attackerUnit.PlayBasicAttackVfxFromCombatEvent(target);"));
    }

    [Test]
    public void LocalVfxVisibilityUsesCameraFieldAndGameplayOwnerIds()
    {
        string visibilitySource = File.ReadAllText("Assets/Scripts/VFX/LocalVfxVisibility.cs");

        Assert.That(visibilitySource, Does.Contain("CameraManager.Instance"));
        Assert.That(visibilitySource, Does.Contain("CurrentViewingField"));
        Assert.That(visibilitySource, Does.Contain("GameManagers.Instance"));
        Assert.That(visibilitySource, Does.Contain("localPlayer"));
        Assert.That(visibilitySource, Does.Contain("OwnerPlayerIdForRoster"));
        Assert.That(visibilitySource, Does.Contain("SnapshotOwnerPlayerId"));
    }

    [Test]
    public void ProjectileVfxCatchUpUsesLocalSkippedEventBuffer()
    {
        string managerSource = File.ReadAllText("Assets/Scripts/VFX/ProjectileVfxManager.cs");
        string cameraSource = File.ReadAllText("Assets/Scripts/Managers/CameraManager.cs");
        string schedulerSource = File.ReadAllText("Assets/Scripts/Managers/CombatScheduler.cs");

        Assert.That(cameraSource, Does.Contain("public static event System.Action<PlayerManager> OnCurrentViewingFieldChanged"));
        Assert.That(cameraSource, Does.Contain("OnCurrentViewingFieldChanged?.Invoke(targetPlayer);"));
        Assert.That(managerSource, Does.Contain("CameraManager.OnCurrentViewingFieldChanged += HandleViewingFieldChanged"));
        Assert.That(managerSource, Does.Contain("private struct SkippedProjectileEvent"));
        Assert.That(managerSource, Does.Contain("RecordSkippedCombatEvent"));
        Assert.That(managerSource, Does.Contain("CatchUpVisibleProjectiles"));
        Assert.That(managerSource, Does.Contain("SuppressMuzzleFlash = true"));
        Assert.That(managerSource, Does.Contain("AllowFullCatchUp = true"));
        Assert.That(managerSource, Does.Contain("HasFirePositionOverride"));
        Assert.That(managerSource, Does.Contain("HasTargetPositionOverride"));
        Assert.That(schedulerSource, Does.Contain("public bool AllowFullCatchUp;"));
        Assert.That(schedulerSource, Does.Contain("public bool SuppressMuzzleFlash;"));
    }
}
#endif
