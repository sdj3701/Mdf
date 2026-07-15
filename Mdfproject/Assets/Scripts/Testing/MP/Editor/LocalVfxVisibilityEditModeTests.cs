#if UNITY_EDITOR
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

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
    public void MonsterStatusBarVisibilityAllowsOnlyViewedFieldAndFailsOpen()
    {
        Assert.That(StatusBarUI.ShouldShowMonsterStatusBar(1, 1), Is.True);
        Assert.That(StatusBarUI.ShouldShowMonsterStatusBar(1, 2), Is.False);
        Assert.That(StatusBarUI.ShouldShowMonsterStatusBar(-1, 2), Is.True);
        Assert.That(StatusBarUI.ShouldShowMonsterStatusBar(2, -1), Is.True);
    }

    [Test]
    public void MonsterStatusBarKeepsCameraSubscriptionWhileInactive()
    {
        const BindingFlags members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        MethodInfo awake = typeof(StatusBarUI).GetMethod("Awake", members);
        MethodInfo onDisable = typeof(StatusBarUI).GetMethod("OnDisable", members);
        MethodInfo onDestroy = typeof(StatusBarUI).GetMethod("OnDestroy", members);
        MethodInfo refresh = typeof(StatusBarUI).GetMethod(nameof(StatusBarUI.RefreshCameraFieldVisibility), members);
        MethodInfo reset = typeof(StatusBarUI).GetMethod(nameof(StatusBarUI.ResetForReuse), members);

        Assert.That(awake, Is.Not.Null);
        Assert.That(onDisable, Is.Not.Null);
        Assert.That(onDestroy, Is.Not.Null);
        Assert.That(refresh, Is.Not.Null);
        Assert.That(reset, Is.Not.Null);
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(
            awake,
            typeof(CameraManager),
            "add_OnCurrentViewingFieldChanged"), Is.True);
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(
            onDestroy,
            typeof(CameraManager),
            "remove_OnCurrentViewingFieldChanged"), Is.True);
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(
            onDisable,
            typeof(CameraManager),
            "remove_OnCurrentViewingFieldChanged"), Is.False,
            "Camera-hidden status bars must stay subscribed so a later field switch can reactivate them.");
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(
            refresh,
            typeof(CameraManager),
            "get_CurrentViewingPlayerId"), Is.True);
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(
            refresh,
            typeof(Monster),
            "get_SnapshotOwnerPlayerId"), Is.True);
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(
            refresh,
            typeof(GameObject),
            nameof(GameObject.SetActive)), Is.True);
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(
            reset,
            typeof(StatusBarUI),
            nameof(StatusBarUI.RefreshCameraFieldVisibility)), Is.True);
    }

    [Test]
    public void CombatSchedulerFiltersLocalVfxRpcPlayback()
    {
        string schedulerSource = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/Managers/CombatScheduler.cs");

        Assert.That(schedulerSource, Does.Contain("LocalVfxVisibility.ShouldPlay(attacker, target, LocalVfxVisibilityEventKind.Projectile)"));
        Assert.That(schedulerSource, Does.Contain("ProjectileVfxManager.RecordSkippedCombatEvent(Runner, attacker, target, fireTick, hitTick);"));
        Assert.That(schedulerSource, Does.Contain("ProjectileVfxManager.PlayFromCombatEvent(Runner, attacker, target, fireTick, hitTick);"));
        Assert.That(schedulerSource, Does.Contain("LocalVfxVisibility.ShouldPlay(attacker, target, LocalVfxVisibilityEventKind.BasicAttack)"));
        Assert.That(schedulerSource, Does.Contain("attackerUnit.PlayBasicAttackVfxFromCombatEvent(target);"));
    }

    [Test]
    public void LocalVfxVisibilityUsesCameraFieldAndGameplayOwnerIds()
    {
        string visibilitySource = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/VFX/LocalVfxVisibility.cs");

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
        string managerSource = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/VFX/ProjectileVfxManager.cs");
        string cameraSource = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/Managers/CameraManager.cs");
        string schedulerSource = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/Managers/CombatScheduler.cs");

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
