#if UNITY_EDITOR
using System.IO;
using NUnit.Framework;

public sealed class StatusEffectSchedulerEditModeTests
{
    [Test]
    public void StatusEffectsUseCombatSchedulerNetworkSlots()
    {
        string schedulerSource = File.ReadAllText("Assets/Scripts/Managers/CombatScheduler.cs");
        string statusSource = File.ReadAllText("Assets/Scripts/Managers/CombatScheduler.StatusEffects.cs");

        Assert.That(schedulerSource, Does.Contain("public partial class CombatScheduler : NetworkBehaviour"));
        Assert.That(schedulerSource, Does.Contain("ProcessDueStatusEffects();"));
        Assert.That(statusSource, Does.Contain("private struct StatusEffectEntry : INetworkStruct"));
        Assert.That(statusSource, Does.Contain("public struct StatusEffectMigrationSnapshot"));
        Assert.That(statusSource, Does.Contain("NetworkArray<StatusEffectEntry> StatusEffects"));
        Assert.That(statusSource, Does.Contain("public bool ApplyStatusEffect("));
        Assert.That(statusSource, Does.Contain("public IEnumerable<string> BuildActiveStatusSnapshotParts"));
        Assert.That(statusSource, Does.Contain("CaptureStatusEffectsForMigration"));
        Assert.That(statusSource, Does.Contain("RestoreStatusEffectsFromMigration"));
        Assert.That(statusSource, Does.Contain("ClearStatusEffectsForTarget"));
        Assert.That(statusSource, Does.Contain("TickIntervalTicks"));
        Assert.That(statusSource, Does.Contain("ExpireTick"));
        Assert.That(statusSource, Does.Contain("NextTick"));
        Assert.That(schedulerSource, Does.Contain("!IsInstanceValidForRunner(Instance, Runner)"));
        Assert.That(schedulerSource, Does.Contain("private static bool IsInstanceValidForRunner"));
        Assert.That(schedulerSource, Does.Contain("public static void RebindInstanceForMigration"));
    }

    [Test]
    public void BuffManagerDelegatesNetworkedStatusToScheduler()
    {
        string buffSource = File.ReadAllText("Assets/Scripts/Game/Skills/BuffManager.cs");

        Assert.That(buffSource, Does.Contain("if (!IsNetworkStatusSchedulerActive())"));
        Assert.That(buffSource, Does.Contain("CombatScheduler.Instance.ApplyStatusEffect("));
        Assert.That(buffSource, Does.Contain("CombatScheduler.Instance.ClearStatusEffectType"));
        Assert.That(buffSource, Does.Contain("CombatScheduler.Instance.ClearStatusEffectsForTarget"));
        Assert.That(buffSource, Does.Contain("public void ApplyStatusSchedulerCache"));
        Assert.That(buffSource, Does.Contain("_statusCacheFromScheduler"));
    }

    [Test]
    public void StatusSnapshotsComeFromSchedulerWhenAvailable()
    {
        string snapshotSource = File.ReadAllText("Assets/Scripts/Testing/MP/MPTestStateSnapshot.cs");

        Assert.That(snapshotSource, Does.Contain("bool useSchedulerStatus = statusScheduler != null && statusScheduler.IsStatusEffectSchedulerActive"));
        Assert.That(snapshotSource, Does.Contain("activeStatusCount = SafeInt(() => statusScheduler.ActiveStatusEffectCount, 0);"));
        Assert.That(snapshotSource, Does.Contain("statusScheduler.BuildActiveStatusSnapshotParts(BuildEffectTargetKey)"));
    }

    [Test]
    public void DeathClearsSchedulerStatusEffects()
    {
        string unitSource = File.ReadAllText("Assets/Scripts/Game/Units/Unit.cs");
        string monsterSource = File.ReadAllText("Assets/Scripts/Game/Monsters/Monster.cs");

        Assert.That(unitSource, Does.Contain("_buffManager?.ClearAllStatusEffects();"));
        Assert.That(monsterSource, Does.Contain("_buffManager?.ClearAllStatusEffects();"));
    }

    [Test]
    public void MPHarnessCanInjectAndPreserveActiveStatusThroughHostMigration()
    {
        string automationServerSource = File.ReadAllText("Assets/Scripts/Testing/MP/MPTestAutomationServer.cs");
        string automationClientSource = File.ReadAllText("../tools/harness/mp/automation_client.py");
        string battleCommonSource = File.ReadAllText("../tools/harness/mp/battle_progression_common.py");
        string runMatrixSource = File.ReadAllText("../tools/harness/mp/run_matrix.py");
        string caseSource = File.ReadAllText("../tools/harness/mp/run_status_effect_host_migration.py");

        Assert.That(automationServerSource, Does.Contain("\"/test/applyStatusEffect\""));
        Assert.That(automationServerSource, Does.Contain("ApplyStatusEffectForTest"));
        Assert.That(automationServerSource, Does.Contain("TryFindStatusEffectTarget"));
        Assert.That(automationClientSource, Does.Contain("def apply_status_effect"));
        Assert.That(battleCommonSource, Does.Contain("apply_status_before_migration"));
        Assert.That(battleCommonSource, Does.Contain("require_active_status"));
        Assert.That(battleCommonSource, Does.Contain("active_status_count"));
        Assert.That(runMatrixSource, Does.Contain("\"status-effect-host-migration\""));
        Assert.That(caseSource, Does.Contain("apply_status_before_migration=True"));

        string hostMigrationSource = File.ReadAllText("Assets/Scripts/Network/HostMigrationHandler.cs");
        Assert.That(hostMigrationSource, Does.Contain("RebindCombatSchedulerForGameManagers"));
        Assert.That(hostMigrationSource, Does.Contain("ResolveGameManagersForRunner.RestoredCandidate"));
        Assert.That(hostMigrationSource, Does.Contain("CaptureDurableStatusEffects"));
        Assert.That(hostMigrationSource, Does.Contain("RestoreCachedStatusEffectsForMigration"));
    }
}
#endif
