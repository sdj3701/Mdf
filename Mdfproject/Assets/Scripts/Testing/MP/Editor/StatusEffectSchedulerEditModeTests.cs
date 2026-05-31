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

        Assert.That(buffSource, Does.Contain("CombatScheduler.Instance.ApplyStatusEffect("));
        Assert.That(buffSource, Does.Contain("CombatScheduler.Instance.ClearStatusEffectType"));
        Assert.That(buffSource, Does.Contain("CombatScheduler.Instance.ClearStatusEffectsForTarget"));
        Assert.That(buffSource, Does.Contain("public void ApplyStatusSchedulerCache"));
        Assert.That(buffSource, Does.Contain("_statusCacheFromScheduler"));
        Assert.That(buffSource, Does.Not.Contain("_activeStatusEffects"));
        Assert.That(buffSource, Does.Not.Contain("UpdateStatusEffects"));
        Assert.That(File.Exists("Assets/Scripts/Game/Skills/ActiveStatusEffect.cs"), Is.False);
    }

    [Test]
    public void StatusSnapshotsComeFromSchedulerWhenAvailable()
    {
        string snapshotSource = File.ReadAllText("Assets/Scripts/Testing/MP/MPTestStateSnapshot.cs");

        Assert.That(snapshotSource, Does.Contain("bool useSchedulerStatus = scheduler != null && scheduler.IsStatusEffectSchedulerActive"));
        Assert.That(snapshotSource, Does.Contain("activeStatusCount = SafeInt(() => scheduler.ActiveStatusEffectCount, 0);"));
        Assert.That(snapshotSource, Does.Contain("scheduler.BuildActiveStatusSnapshotParts(BuildEffectTargetKey)"));
        Assert.That(snapshotSource, Does.Not.Contain("FindObjectsOfType<BuffManager>"));
        Assert.That(snapshotSource, Does.Not.Contain("BuildActiveStatusSnapshotParts(targetKey)"));
    }

    [Test]
    public void StatBuffsUseCombatSchedulerNetworkSlots()
    {
        string schedulerSource = File.ReadAllText("Assets/Scripts/Managers/CombatScheduler.cs");
        string statBuffSource = File.ReadAllText("Assets/Scripts/Managers/CombatScheduler.StatBuffs.cs");
        string buffSource = File.ReadAllText("Assets/Scripts/Game/Skills/BuffManager.cs");
        string snapshotSource = File.ReadAllText("Assets/Scripts/Testing/MP/MPTestStateSnapshot.cs");

        Assert.That(schedulerSource, Does.Contain("ProcessDueStatBuffs();"));
        Assert.That(statBuffSource, Does.Contain("private struct StatBuffEntry : INetworkStruct"));
        Assert.That(statBuffSource, Does.Contain("public struct StatBuffMigrationSnapshot"));
        Assert.That(statBuffSource, Does.Contain("NetworkArray<StatBuffEntry> StatBuffs"));
        Assert.That(statBuffSource, Does.Contain("public bool ApplyStatBuff("));
        Assert.That(statBuffSource, Does.Contain("public IEnumerable<string> BuildActiveStatBuffSnapshotParts"));
        Assert.That(statBuffSource, Does.Contain("CaptureStatBuffsForMigration"));
        Assert.That(statBuffSource, Does.Contain("RestoreStatBuffsFromMigration"));
        Assert.That(statBuffSource, Does.Contain("ClearStatBuffsForTarget"));
        Assert.That(statBuffSource, Does.Contain("ExpireTick"));
        Assert.That(buffSource, Does.Contain("CombatScheduler.Instance.ApplyStatBuff(this, buffEffect, caster);"));
        Assert.That(buffSource, Does.Contain("public void ApplyStatBuffSchedulerCache"));
        Assert.That(buffSource, Does.Contain("_statBuffCacheFromScheduler"));
        Assert.That(buffSource, Does.Not.Contain("_activeBuffs"));
        Assert.That(buffSource, Does.Not.Contain("UpdateBuffs"));
        Assert.That(buffSource, Does.Not.Contain("public class ActiveBuff"));
        Assert.That(snapshotSource, Does.Contain("bool useSchedulerBuffs = scheduler != null && scheduler.IsStatBuffSchedulerActive"));
        Assert.That(snapshotSource, Does.Contain("activeBuffCount = SafeInt(() => scheduler.ActiveStatBuffCount, 0);"));
        Assert.That(snapshotSource, Does.Contain("scheduler.BuildActiveStatBuffSnapshotParts(BuildEffectTargetKey)"));
        Assert.That(snapshotSource, Does.Not.Contain("BuildActiveBuffSnapshotParts(targetKey)"));
    }

    [Test]
    public void ZonesUseCombatSchedulerNetworkSlots()
    {
        string schedulerSource = File.ReadAllText("Assets/Scripts/Managers/CombatScheduler.cs");
        string zoneSchedulerSource = File.ReadAllText("Assets/Scripts/Managers/CombatScheduler.Zones.cs");
        string zoneEffectSource = File.ReadAllText("Assets/Scripts/Game/Skills/ZoneEffect.cs");
        string zoneControllerSource = File.ReadAllText("Assets/Scripts/Game/Skills/ZoneController.cs");
        string snapshotSource = File.ReadAllText("Assets/Scripts/Testing/MP/MPTestStateSnapshot.cs");
        string hostMigrationSource = File.ReadAllText("Assets/Scripts/Network/HostMigrationHandler.cs");

        Assert.That(schedulerSource, Does.Contain("ProcessDueZones();"));
        Assert.That(schedulerSource, Does.Contain("RebuildZonePayloadsFromNetworkEntries();"));
        Assert.That(zoneSchedulerSource, Does.Contain("private struct ZoneEntry : INetworkStruct"));
        Assert.That(zoneSchedulerSource, Does.Contain("public struct ZoneMigrationSnapshot"));
        Assert.That(zoneSchedulerSource, Does.Contain("NetworkArray<ZoneEntry> Zones"));
        Assert.That(zoneSchedulerSource, Does.Contain("public bool TryScheduleZone("));
        Assert.That(zoneSchedulerSource, Does.Contain("public IEnumerable<string> BuildActiveZoneSnapshotParts"));
        Assert.That(zoneSchedulerSource, Does.Contain("CaptureZonesForMigration"));
        Assert.That(zoneSchedulerSource, Does.Contain("RestoreZonesFromMigration"));
        Assert.That(zoneEffectSource, Does.Contain("scheduler.TryScheduleZone"));
        Assert.That(zoneControllerSource, Does.Contain("ClearScheduledZone"));
        Assert.That(zoneControllerSource, Does.Not.Contain("Time.deltaTime"));
        Assert.That(zoneControllerSource, Does.Not.Contain("remainingDuration"));
        Assert.That(zoneControllerSource, Does.Not.Contain("tickTimer"));
        Assert.That(zoneEffectSource, Does.Not.Contain("Object.Instantiate"));
        Assert.That(snapshotSource, Does.Contain("bool useSchedulerZones = scheduler != null && scheduler.IsZoneSchedulerActive"));
        Assert.That(snapshotSource, Does.Contain("scheduler.BuildActiveZoneSnapshotParts()"));
        Assert.That(snapshotSource, Does.Not.Contain("FindObjectsOfType<ZoneController>"));
        Assert.That(hostMigrationSource, Does.Contain("CaptureDurableZones"));
        Assert.That(hostMigrationSource, Does.Contain("RestoreCachedZonesForMigration"));
    }

    [Test]
    public void NetworkBudgetDropsAreExposedAndCapacityFailuresReturnFalse()
    {
        string schedulerSource = File.ReadAllText("Assets/Scripts/Managers/CombatScheduler.cs");
        string budgetSource = File.ReadAllText("Assets/Scripts/Managers/CombatScheduler.NetworkBudget.cs");
        string statusSource = File.ReadAllText("Assets/Scripts/Managers/CombatScheduler.StatusEffects.cs");
        string statBuffSource = File.ReadAllText("Assets/Scripts/Managers/CombatScheduler.StatBuffs.cs");
        string zoneSource = File.ReadAllText("Assets/Scripts/Managers/CombatScheduler.Zones.cs");
        string snapshotSource = File.ReadAllText("Assets/Scripts/Testing/MP/MPTestStateSnapshot.cs");
        string compareSource = File.ReadAllText("../tools/harness/mp/compare_state_snapshots.py");

        Assert.That(budgetSource, Does.Contain("public struct NetworkBudgetReport"));
        Assert.That(budgetSource, Does.Contain("case NetworkBudgetDropKind.Status:"));
        Assert.That(budgetSource, Does.Contain("case NetworkBudgetDropKind.StatBuff:"));
        Assert.That(budgetSource, Does.Contain("case NetworkBudgetDropKind.Zone:"));
        Assert.That(budgetSource, Does.Contain("case NetworkBudgetDropKind.PendingFire:"));
        Assert.That(budgetSource, Does.Contain("case NetworkBudgetDropKind.PendingHit:"));
        Assert.That(budgetSource, Does.Contain("case NetworkBudgetDropKind.PresentationEvent:"));
        Assert.That(statusSource, Does.Contain("RecordNetworkBudgetDrop(NetworkBudgetDropKind.Status);"));
        Assert.That(statBuffSource, Does.Contain("RecordNetworkBudgetDrop(NetworkBudgetDropKind.StatBuff);"));
        Assert.That(zoneSource, Does.Contain("RecordNetworkBudgetDrop(NetworkBudgetDropKind.Zone);"));
        Assert.That(schedulerSource, Does.Contain("RecordNetworkBudgetDrop(NetworkBudgetDropKind.PendingFire);"));
        Assert.That(schedulerSource, Does.Contain("RecordNetworkBudgetDrop(NetworkBudgetDropKind.PendingHit);"));
        Assert.That(schedulerSource, Does.Contain("RecordNetworkBudgetDrop(NetworkBudgetDropKind.PresentationEvent);"));
        Assert.That(schedulerSource, Does.Contain("Pending fire capacity exceeded"));
        Assert.That(schedulerSource, Does.Contain("Pending hit capacity exceeded"));
        Assert.That(schedulerSource, Does.Contain("FindEmptyPendingFireSnapshotSlot"));
        Assert.That(schedulerSource, Does.Contain("FindEmptyPendingHitSnapshotSlot"));
        Assert.That(schedulerSource, Does.Contain("FindPendingFireSnapshotSlot(sequence)"));
        Assert.That(schedulerSource, Does.Contain("FindPendingHitSnapshotSlot(sequence)"));
        Assert.That(schedulerSource, Does.Not.Contain("sequence % PendingFireCapacity"));
        Assert.That(schedulerSource, Does.Not.Contain("sequence % PendingHitCapacity"));
        Assert.That(schedulerSource, Does.Not.Contain("nextSeq % PendingFireCapacity"));
        Assert.That(schedulerSource, Does.Not.Contain("nextSeq % PendingHitCapacity"));
        Assert.That(statusSource, Does.Not.Contain("Active status capacity exceeded. capacity={MaxActiveStatusEffects}, target={targetObject.Id}, type={type}\");\r\n            return true;"));
        Assert.That(statBuffSource, Does.Not.Contain("Active stat buff capacity exceeded. capacity={MaxActiveStatBuffs}, target={targetObject.Id}, stat={statType}\");\r\n            return true;"));
        Assert.That(zoneSource, Does.Not.Contain("Active zone capacity exceeded. capacity={MaxActiveZones}, effect={effect.name}\");\r\n            return true;"));
        Assert.That(snapshotSource, Does.Contain("NetworkBudget = CaptureNetworkBudget(gameManagers)"));
        Assert.That(snapshotSource, Does.Contain("[JsonProperty(\"networkBudget\")] public NetworkBudgetSnapshot NetworkBudget;"));
        Assert.That(snapshotSource, Does.Contain("NetworkObject.GetWordCount(networkObject)"));
        Assert.That(compareSource, Does.Contain("assert_no_network_budget_drops"));
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

    [Test]
    public void MPHarnessCanInjectAndPreserveActiveStatBuffThroughHostMigration()
    {
        string automationServerSource = File.ReadAllText("Assets/Scripts/Testing/MP/MPTestAutomationServer.cs");
        string automationClientSource = File.ReadAllText("../tools/harness/mp/automation_client.py");
        string battleCommonSource = File.ReadAllText("../tools/harness/mp/battle_progression_common.py");
        string runMatrixSource = File.ReadAllText("../tools/harness/mp/run_matrix.py");
        string caseSource = File.ReadAllText("../tools/harness/mp/run_stat_buff_host_migration.py");

        Assert.That(automationServerSource, Does.Contain("\"/test/applyStatBuff\""));
        Assert.That(automationServerSource, Does.Contain("ApplyStatBuffForTest"));
        Assert.That(automationClientSource, Does.Contain("def apply_stat_buff"));
        Assert.That(battleCommonSource, Does.Contain("apply_stat_buff_before_migration"));
        Assert.That(battleCommonSource, Does.Contain("require_active_buff"));
        Assert.That(battleCommonSource, Does.Contain("active_buff_count"));
        Assert.That(runMatrixSource, Does.Contain("\"stat-buff-host-migration\""));
        Assert.That(caseSource, Does.Contain("apply_stat_buff_before_migration=True"));

        string hostMigrationSource = File.ReadAllText("Assets/Scripts/Network/HostMigrationHandler.cs");
        Assert.That(hostMigrationSource, Does.Contain("CaptureDurableStatBuffs"));
        Assert.That(hostMigrationSource, Does.Contain("RestoreCachedStatBuffsForMigration"));
    }

    [Test]
    public void MPHarnessCanInjectAndPreserveActiveZoneThroughHostMigration()
    {
        string automationServerSource = File.ReadAllText("Assets/Scripts/Testing/MP/MPTestAutomationServer.cs");
        string automationClientSource = File.ReadAllText("../tools/harness/mp/automation_client.py");
        string battleCommonSource = File.ReadAllText("../tools/harness/mp/battle_progression_common.py");
        string runMatrixSource = File.ReadAllText("../tools/harness/mp/run_matrix.py");
        string caseSource = File.ReadAllText("../tools/harness/mp/run_zone_host_migration.py");

        Assert.That(automationServerSource, Does.Contain("\"/test/applyZone\""));
        Assert.That(automationServerSource, Does.Contain("ApplyZoneForTest"));
        Assert.That(automationClientSource, Does.Contain("def apply_zone"));
        Assert.That(battleCommonSource, Does.Contain("apply_zone_before_migration"));
        Assert.That(battleCommonSource, Does.Contain("require_active_zone"));
        Assert.That(battleCommonSource, Does.Contain("active_zone_count"));
        Assert.That(runMatrixSource, Does.Contain("\"zone-host-migration\""));
        Assert.That(caseSource, Does.Contain("apply_zone_before_migration=True"));

        string hostMigrationSource = File.ReadAllText("Assets/Scripts/Network/HostMigrationHandler.cs");
        Assert.That(hostMigrationSource, Does.Contain("CaptureDurableZones"));
        Assert.That(hostMigrationSource, Does.Contain("RestoreCachedZonesForMigration"));
    }
}
#endif
