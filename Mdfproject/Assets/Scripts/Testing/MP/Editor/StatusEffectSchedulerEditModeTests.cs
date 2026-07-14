#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using NetworkObject = Fusion.NetworkObject;
using NetworkRunner = Fusion.NetworkRunner;

public sealed class StatusEffectSchedulerEditModeTests
{
    private static readonly OpCode[] SingleByteOpCodes = new OpCode[0x100];
    private static readonly OpCode[] MultiByteOpCodes = new OpCode[0x100];

    private static string _automationClientSource;
    private static string _battleCommonSource;
    private static string _runMatrixSource;
    private static string _pendingStressCaseSource;
    private static string _statusMigrationCaseSource;
    private static string _statBuffMigrationCaseSource;
    private static string _zoneMigrationCaseSource;

    static StatusEffectSchedulerEditModeTests()
    {
        foreach (FieldInfo field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (!(field.GetValue(null) is OpCode opCode))
            {
                continue;
            }

            ushort value = unchecked((ushort)opCode.Value);
            if (value < 0x100)
            {
                SingleByteOpCodes[value] = opCode;
            }
            else if ((value & 0xFF00) == 0xFE00)
            {
                MultiByteOpCodes[value & 0xFF] = opCode;
            }
        }
    }

    [OneTimeSetUp]
    public void LoadExternalHarnessContracts()
    {
        _automationClientSource = MdfSourcePolicy.ReadStaticContract("../tools/harness/mp/automation_client.py");
        _battleCommonSource = MdfSourcePolicy.ReadStaticContract("../tools/harness/mp/battle_progression_common.py");
        _runMatrixSource = MdfSourcePolicy.ReadStaticContract("../tools/harness/mp/run_matrix.py");
        _pendingStressCaseSource = MdfSourcePolicy.ReadStaticContract("../tools/harness/mp/run_network_budget_pending_stress.py");
        _statusMigrationCaseSource = MdfSourcePolicy.ReadStaticContract("../tools/harness/mp/run_status_effect_host_migration.py");
        _statBuffMigrationCaseSource = MdfSourcePolicy.ReadStaticContract("../tools/harness/mp/run_stat_buff_host_migration.py");
        _zoneMigrationCaseSource = MdfSourcePolicy.ReadStaticContract("../tools/harness/mp/run_zone_host_migration.py");
    }

    [Test]
    public void StatusEffectsUseCombatSchedulerNetworkSlots()
    {
        AssertNetworkSlotContract(
            "StatusEffectEntry",
            "StatusEffects",
            "MaxActiveStatusEffects",
            40,
            "StatusEffectMigrationSnapshot",
            new[] { "PackedMeta", "TickIntervalTicks", "ExpireTick", "NextTick" },
            new[]
            {
                "ApplyStatusEffect", "BuildActiveStatusSnapshotParts", "CaptureStatusEffectsForMigration",
                "RestoreStatusEffectsFromMigration", "ClearStatusEffectsForTarget", "PackStatusMeta"
            });

        MethodInfo fixedUpdate = typeof(CombatScheduler).GetMethod(nameof(CombatScheduler.FixedUpdateNetwork), InstanceMembers);
        MethodInfo spawned = typeof(CombatScheduler).GetMethod(nameof(CombatScheduler.Spawned), InstanceMembers);
        AssertMethodReferences(fixedUpdate, typeof(CombatScheduler), "ProcessDueStatusEffects");
        AssertMethodReferences(spawned, typeof(CombatScheduler), "IsInstanceValidForRunner");
        AssertHasStaticMethod(typeof(CombatScheduler), "RebindInstanceForMigration");
    }

    [Test]
    public void BuffManagerDelegatesNetworkedStatusToScheduler()
    {
        MethodInfo apply = typeof(BuffManager).GetMethod(nameof(BuffManager.ApplyStatusEffect), InstanceMembers);
        MethodInfo remove = typeof(BuffManager).GetMethod(nameof(BuffManager.RemoveStatusEffect), InstanceMembers);
        MethodInfo clear = typeof(BuffManager).GetMethod(nameof(BuffManager.ClearAllStatusEffects), InstanceMembers);
        Assert.That(apply, Is.Not.Null);
        Assert.That(remove, Is.Not.Null);
        Assert.That(clear, Is.Not.Null);
        AssertMethodReferences(apply, typeof(CombatScheduler), nameof(CombatScheduler.ApplyStatusEffect));
        AssertMethodReferences(remove, typeof(CombatScheduler), nameof(CombatScheduler.ClearStatusEffectType));
        AssertMethodReferences(clear, typeof(CombatScheduler), nameof(CombatScheduler.ClearStatusEffectsForTarget));
        Assert.That(typeof(BuffManager).GetMethod(nameof(BuffManager.ApplyStatusSchedulerCache)), Is.Not.Null);
        Assert.That(typeof(BuffManager).GetField("_statusCacheFromScheduler", InstanceMembers), Is.Not.Null);
        Assert.That(typeof(BuffManager).GetField("_activeStatusEffects", InstanceMembers), Is.Null);
        Assert.That(typeof(BuffManager).GetMethod("UpdateStatusEffects", InstanceMembers), Is.Null);
        Assert.That(
            AssetDatabase.LoadAssetAtPath<MonoScript>("Assets/Scripts/Game/Skills/ActiveStatusEffect.cs"),
            Is.Null);
    }

    [Test]
    public void StatusSnapshotsComeFromSchedulerWhenAvailable()
    {
        MethodInfo captureEffects = GetRequiredMethod(typeof(MPTestStateSnapshot), "CaptureEffects", StaticMembers);
        AssertMethodReferences(captureEffects, typeof(CombatScheduler), "get_IsStatusEffectSchedulerActive");
        AssertMethodReferences(captureEffects, typeof(CombatScheduler), "get_ActiveStatusEffectCount");
        AssertMethodReferences(captureEffects, typeof(CombatScheduler), "BuildActiveStatusSnapshotParts");
        AssertNoGenericMethodReference(captureEffects, typeof(UnityEngine.Object), "FindObjectsOfType", typeof(BuffManager));
    }

    [Test]
    public void StatBuffsUseCombatSchedulerNetworkSlots()
    {
        AssertNetworkSlotContract(
            "StatBuffEntry",
            "StatBuffs",
            "MaxActiveStatBuffs",
            96,
            "StatBuffMigrationSnapshot",
            new[] { "PackedMeta", "ExpireTick" },
            new[]
            {
                "ApplyStatBuff", "BuildActiveStatBuffSnapshotParts", "CaptureStatBuffsForMigration",
                "RestoreStatBuffsFromMigration", "ClearStatBuffsForTarget", "PackStatBuffMeta",
                "ApplyStatBuffInternal"
            });
        Assert.That(typeof(CombatScheduler).GetField("StatBuffFlagBerserkBundle", StaticMembers), Is.Null);
        Assert.That(typeof(CombatScheduler).GetMethod("ApplyBerserkStatBuffs", InstanceMembers), Is.Null);
        Assert.That(typeof(BuffManager).GetMethod(nameof(BuffManager.ApplyStatBuffSchedulerCache)), Is.Not.Null);
        Assert.That(typeof(BuffManager).GetField("_statBuffCacheFromScheduler", InstanceMembers), Is.Not.Null);
        Assert.That(typeof(BuffManager).GetField("_activeBuffs", InstanceMembers), Is.Null);
        Assert.That(typeof(BuffManager).GetMethod("UpdateBuffs", InstanceMembers), Is.Null);
        Assert.That(typeof(BuffManager).Assembly.GetType("ActiveBuff"), Is.Null);

        MethodInfo applyBuff = GetRequiredMethod(typeof(BuffManager), nameof(BuffManager.ApplyBuff), InstanceMembers);
        MethodInfo fixedUpdate = GetRequiredMethod(typeof(CombatScheduler), nameof(CombatScheduler.FixedUpdateNetwork), InstanceMembers);
        MethodInfo captureEffects = GetRequiredMethod(typeof(MPTestStateSnapshot), "CaptureEffects", StaticMembers);
        AssertMethodReferences(applyBuff, typeof(CombatScheduler), nameof(CombatScheduler.ApplyStatBuff));
        AssertMethodReferences(fixedUpdate, typeof(CombatScheduler), "ProcessDueStatBuffs");
        AssertMethodReferences(captureEffects, typeof(CombatScheduler), "get_IsStatBuffSchedulerActive");
        AssertMethodReferences(captureEffects, typeof(CombatScheduler), "get_ActiveStatBuffCount");
        AssertMethodReferences(captureEffects, typeof(CombatScheduler), "BuildActiveStatBuffSnapshotParts");

        MethodInfo recalculate = GetRequiredMethod(typeof(BuffManager), nameof(BuffManager.RecalculateStats), InstanceMembers);
        MethodInfo unitBerserk = GetRequiredMethod(typeof(Unit), nameof(Unit.ApplyBerserkMode), InstanceMembers);
        MethodInfo monsterBerserk = GetRequiredMethod(typeof(Monster), nameof(Monster.ApplyBerserkMode), InstanceMembers);
        Assert.That(typeof(Unit).GetProperty(nameof(Unit.IsBerserkModeActive), InstanceMembers), Is.Not.Null);
        Assert.That(typeof(Monster).GetProperty(nameof(Monster.IsBerserkModeActive), InstanceMembers), Is.Not.Null);
        Assert.That(typeof(Unit).GetProperty("NetworkedBerserkModeActive", InstanceMembers), Is.Not.Null);
        Assert.That(typeof(Monster).GetProperty("NetworkedBerserkModeActive", InstanceMembers), Is.Not.Null);
        Assert.That(typeof(FieldUnitMigrationSnapshot).GetField(nameof(FieldUnitMigrationSnapshot.BerserkModeActive)), Is.Not.Null);
        AssertMethodReferences(recalculate, typeof(Unit), "get_IsBerserkModeActive");
        AssertMethodReferences(recalculate, typeof(Monster), "get_IsBerserkModeActive");
        AssertMethodReferences(unitBerserk, typeof(Unit), "SetBerserkModeActive");
        AssertMethodReferences(monsterBerserk, typeof(Monster), "SetBerserkModeActive");
    }

    [Test]
    public void ZonesUseCombatSchedulerNetworkSlots()
    {
        AssertNetworkSlotContract(
            "ZoneEntry",
            "Zones",
            "MaxActiveZones",
            16,
            "ZoneMigrationSnapshot",
            new[] { "PackedMeta", "ExpireTick", "NextTick", "TickIntervalTicks" },
            new[]
            {
                "TryScheduleZone", "BuildActiveZoneSnapshotParts", "CaptureZonesForMigration",
                "RestoreZonesFromMigration", "ClearScheduledZone", "PackZoneMeta",
                "RebuildZonePayloadsFromNetworkEntries"
            });
        Assert.That(typeof(ZoneController).GetMethod(nameof(ZoneController.DestroyScheduledZone)), Is.Not.Null);
        Assert.That(typeof(ZoneController).GetField("remainingDuration", InstanceMembers), Is.Null);
        Assert.That(typeof(ZoneController).GetField("tickTimer", InstanceMembers), Is.Null);

        MethodInfo fixedUpdate = GetRequiredMethod(typeof(CombatScheduler), nameof(CombatScheduler.FixedUpdateNetwork), InstanceMembers);
        MethodInfo spawned = GetRequiredMethod(typeof(CombatScheduler), nameof(CombatScheduler.Spawned), InstanceMembers);
        MethodInfo applyEffect = GetRequiredMethod(typeof(ZoneEffect), nameof(ZoneEffect.ApplyEffect), InstanceMembers);
        MethodInfo tryApplyEffect = GetRequiredMethod(typeof(ZoneEffect), nameof(ZoneEffect.TryApplyEffect), InstanceMembers);
        MethodInfo captureEffects = GetRequiredMethod(typeof(MPTestStateSnapshot), "CaptureEffects", StaticMembers);
        AssertMethodReferences(fixedUpdate, typeof(CombatScheduler), "ProcessDueZones");
        AssertMethodReferences(spawned, typeof(CombatScheduler), "RebuildZonePayloadsFromNetworkEntries");
        Assert.That(
            GetReachableInstructions(applyEffect).Any(instruction =>
                instruction.Member is MethodBase referenced &&
                referenced.Name == nameof(ZoneEffect.TryApplyEffect) &&
                typeof(SkillEffect).IsAssignableFrom(referenced.DeclaringType)),
            Is.True,
            "ZoneEffect.ApplyEffect must route through the virtual TryApplyEffect contract");
        AssertMethodReferences(tryApplyEffect, typeof(CombatScheduler), nameof(CombatScheduler.TryScheduleZone));
        AssertNoMethodReference(applyEffect, typeof(UnityEngine.Object), nameof(UnityEngine.Object.Instantiate));
        AssertTypeMethodsDoNotReference(typeof(ZoneController), typeof(Time), "get_deltaTime");
        AssertTypeMethodsReference(
            typeof(ZoneController),
            typeof(CombatScheduler),
            nameof(CombatScheduler.CloseScheduledZoneForPhaseTransition));
        AssertMethodReferences(captureEffects, typeof(CombatScheduler), "get_IsZoneSchedulerActive");
        AssertMethodReferences(captureEffects, typeof(CombatScheduler), "BuildActiveZoneSnapshotParts");
        AssertNoGenericMethodReference(captureEffects, typeof(UnityEngine.Object), "FindObjectsOfType", typeof(ZoneController));
        AssertHasInstanceMethod(typeof(HostMigrationHandler), "CaptureDurableZones");
        AssertHasInstanceMethod(typeof(HostMigrationHandler), "RestoreCachedZonesForMigration");
    }

    private const BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags StaticMembers = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private static void AssertNetworkSlotContract(
        string entryTypeName,
        string propertyName,
        string capacityFieldName,
        int expectedCapacity,
        string migrationSnapshotName,
        string[] entryFieldNames,
        string[] methodNames)
    {
        Type scheduler = typeof(CombatScheduler);
        Assert.That(typeof(Fusion.NetworkBehaviour).IsAssignableFrom(scheduler), Is.True);

        Type entry = scheduler.GetNestedType(entryTypeName, BindingFlags.NonPublic);
        Assert.That(entry, Is.Not.Null, entryTypeName);
        Assert.That(typeof(Fusion.INetworkStruct).IsAssignableFrom(entry), Is.True, entryTypeName);
        foreach (string fieldName in entryFieldNames)
        {
            Assert.That(entry.GetField(fieldName, InstanceMembers), Is.Not.Null, $"{entryTypeName}.{fieldName}");
        }

        PropertyInfo slots = scheduler.GetProperty(propertyName, InstanceMembers);
        Assert.That(slots, Is.Not.Null, propertyName);
        Assert.That(slots.PropertyType.IsGenericType, Is.True, propertyName);
        Assert.That(slots.PropertyType.GetGenericTypeDefinition(), Is.EqualTo(typeof(Fusion.NetworkArray<>)), propertyName);
        Assert.That(slots.PropertyType.GetGenericArguments()[0], Is.EqualTo(entry), propertyName);
        AssertHasAttribute(slots, "NetworkedAttribute");

        CustomAttributeData capacityAttribute = slots.CustomAttributes.SingleOrDefault(
            attribute => attribute.AttributeType.Name == "CapacityAttribute");
        Assert.That(capacityAttribute, Is.Not.Null, $"{propertyName} must retain Fusion Capacity metadata");
        Assert.That(capacityAttribute.ConstructorArguments.Count, Is.EqualTo(1), propertyName);
        Assert.That(capacityAttribute.ConstructorArguments[0].Value, Is.EqualTo(expectedCapacity), propertyName);

        FieldInfo capacity = scheduler.GetField(capacityFieldName, StaticMembers);
        Assert.That(capacity, Is.Not.Null, capacityFieldName);
        Assert.That(capacity.GetRawConstantValue(), Is.EqualTo(expectedCapacity), capacityFieldName);
        Assert.That(scheduler.GetNestedType(migrationSnapshotName, BindingFlags.Public), Is.Not.Null, migrationSnapshotName);
        foreach (string methodName in methodNames)
        {
            Assert.That(
                scheduler.GetMethods(InstanceMembers | StaticMembers).Any(method => method.Name == methodName),
                Is.True,
                methodName);
        }
    }

    private static void AssertHasInstanceMethod(Type type, string methodName)
    {
        Assert.That(type.GetMethods(InstanceMembers).Any(method => method.Name == methodName), Is.True, methodName);
    }

    private static void AssertHasStaticMethod(Type type, string methodName)
    {
        Assert.That(type.GetMethods(StaticMembers).Any(method => method.Name == methodName), Is.True, methodName);
    }

    private static void AssertHasPublicNestedType(Type type, string typeName)
    {
        Assert.That(type.GetNestedType(typeName, BindingFlags.Public), Is.Not.Null, typeName);
    }

    [Test]
    public void NetworkBudgetSeparatesRecoveredCapacityPressureFromTrueDrops()
    {
        Type budgetReport = typeof(CombatScheduler).GetNestedType("NetworkBudgetReport", BindingFlags.Public);
        Assert.That(budgetReport, Is.Not.Null);
        foreach (string fieldName in new[]
                 {
                     "StatusCapacityDrops", "StatBuffCapacityDrops", "ZoneCapacityDrops",
                     "PendingFireCapacityDrops", "PendingHitCapacityDrops", "PresentationEventDrops",
                     "StatusCapacityCoalesces", "StatBuffCapacityCoalesces", "ZoneCapacityCoalesces",
                     "PendingFireCapacityFallbacks", "PendingHitCapacityFallbacks",
                     "StatusCapacityBackpressures", "StatBuffCapacityBackpressures", "ZoneCapacityBackpressures"
                 })
        {
            Assert.That(budgetReport.GetField(fieldName, InstanceMembers), Is.Not.Null, fieldName);
        }

        var schedulerObject = new GameObject("network-budget-drop-test");
        try
        {
            var scheduler = schedulerObject.AddComponent<CombatScheduler>();
            Type dropKind = typeof(CombatScheduler).GetNestedType("NetworkBudgetDropKind", BindingFlags.NonPublic);
            MethodInfo recordDrop = typeof(CombatScheduler).GetMethod("RecordNetworkBudgetDrop", InstanceMembers);
            Assert.That(dropKind, Is.Not.Null);
            Assert.That(recordDrop, Is.Not.Null);
            var fieldByKind = new[]
            {
                ("Status", "_statusCapacityDrops"),
                ("StatBuff", "_statBuffCapacityDrops"),
                ("Zone", "_zoneCapacityDrops"),
                ("PendingFire", "_pendingFireCapacityDrops"),
                ("PendingHit", "_pendingHitCapacityDrops"),
                ("PresentationEvent", "_presentationEventDrops")
            };
            foreach (var pair in fieldByKind)
            {
                object value = Enum.Parse(dropKind, pair.Item1);
                recordDrop.Invoke(scheduler, new[] { value });
                Assert.That(typeof(CombatScheduler).GetField(pair.Item2, InstanceMembers)?.GetValue(scheduler), Is.EqualTo(1), pair.Item1);
            }

            CombatScheduler.NetworkBudgetReport report = scheduler.GetNetworkBudgetReport();
            Assert.That(report.StatusCapacityDrops, Is.EqualTo(1));
            Assert.That(report.StatBuffCapacityDrops, Is.EqualTo(1));
            Assert.That(report.ZoneCapacityDrops, Is.EqualTo(1));
            Assert.That(report.PendingFireCapacityDrops, Is.EqualTo(1));
            Assert.That(report.PendingHitCapacityDrops, Is.EqualTo(1));
            Assert.That(report.PresentationEventDrops, Is.EqualTo(1));

            Type recoveryKind = typeof(CombatScheduler).GetNestedType("CapacityRecoveryKind", BindingFlags.NonPublic);
            MethodInfo recordRecovery = typeof(CombatScheduler).GetMethod("RecordCapacityRecovery", InstanceMembers);
            Assert.That(recoveryKind, Is.Not.Null);
            Assert.That(recordRecovery, Is.Not.Null);
            foreach (string recoveryName in new[]
                     {
                         "StatusCoalesce", "StatBuffCoalesce", "ZoneCoalesce",
                         "PendingFireBackpressure", "PendingHitBackpressure",
                         "StatusBackpressure", "StatBuffBackpressure", "ZoneBackpressure"
                     })
            {
                recordRecovery.Invoke(scheduler, new[] { Enum.Parse(recoveryKind, recoveryName) });
            }

            report = scheduler.GetNetworkBudgetReport();
            Assert.That(report.StatusCapacityCoalesces, Is.EqualTo(1));
            Assert.That(report.StatBuffCapacityCoalesces, Is.EqualTo(1));
            Assert.That(report.ZoneCapacityCoalesces, Is.EqualTo(1));
            Assert.That(report.PendingFireCapacityFallbacks, Is.EqualTo(1));
            Assert.That(report.PendingHitCapacityFallbacks, Is.EqualTo(1));
            Assert.That(report.StatusCapacityBackpressures, Is.EqualTo(1));
            Assert.That(report.StatBuffCapacityBackpressures, Is.EqualTo(1));
            Assert.That(report.ZoneCapacityBackpressures, Is.EqualTo(1));
            Assert.That(report.PendingFireCapacityDrops, Is.EqualTo(1), "recovery telemetry must not increment true drops");
            Assert.That(report.PendingHitCapacityDrops, Is.EqualTo(1), "recovery telemetry must not increment true drops");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(schedulerObject);
        }

        MethodInfo applyStatus = GetRequiredMethod(typeof(CombatScheduler), nameof(CombatScheduler.ApplyStatusEffect), InstanceMembers);
        MethodInfo applyStatBuffInternal = GetRequiredMethod(typeof(CombatScheduler), "ApplyStatBuffInternal", InstanceMembers);
        MethodInfo applyZone = GetRequiredMethod(typeof(CombatScheduler), nameof(CombatScheduler.TryScheduleZone), InstanceMembers);
        MethodInfo writePendingFire = GetRequiredMethod(typeof(CombatScheduler), "WritePendingFireSnapshot", InstanceMembers);
        MethodInfo writePendingHit = GetRequiredMethod(typeof(CombatScheduler), "WritePendingHitSnapshot", InstanceMembers);
        MethodInfo clearPendingFire = GetRequiredMethod(typeof(CombatScheduler), "ClearPendingFireSnapshot", InstanceMembers);
        MethodInfo clearPendingHit = GetRequiredMethod(typeof(CombatScheduler), "ClearPendingHitSnapshot", InstanceMembers);
        MethodInfo publishProjectile = GetRequiredMethod(typeof(CombatScheduler), "PublishProjectileVfxEvent", InstanceMembers);

        AssertCapacityPressureBackpressuresWithoutDrop(applyStatus, "FindEmptyStatusSlot");
        AssertCapacityPressureBackpressuresWithoutDrop(applyStatBuffInternal, "FindEmptyStatBuffSlot");
        AssertCapacityPressureBackpressuresWithoutDrop(applyZone, "FindEmptyZoneSlot");
        AssertCapacityPressureBackpressuresWithoutDrop(writePendingFire, "FindEmptyPendingFireSnapshotSlot");
        AssertCapacityPressureBackpressuresWithoutDrop(writePendingHit, "FindEmptyPendingHitSnapshotSlot");
        AssertMethodReferences(writePendingFire, typeof(CombatScheduler), "RecordCapacityRecovery");
        AssertMethodReferences(writePendingHit, typeof(CombatScheduler), "RecordCapacityRecovery");
        AssertMethodReferences(clearPendingFire, typeof(CombatScheduler), "FindPendingFireSnapshotSlot");
        AssertMethodReferences(clearPendingHit, typeof(CombatScheduler), "FindPendingHitSnapshotSlot");
        AssertMethodReferences(publishProjectile, typeof(CombatScheduler), "RecordNetworkBudgetDrop");
        Assert.That(typeof(CombatScheduler).GetMethod("TryFlushEarliestPendingFireForCapacity", InstanceMembers), Is.Null);
        Assert.That(typeof(CombatScheduler).GetMethod("TryFlushEarliestPendingHitForCapacity", InstanceMembers), Is.Null);
        AssertMethodDoesNotUseRemainder(writePendingFire);
        AssertMethodDoesNotUseRemainder(writePendingHit);
        AssertMethodDoesNotUseRemainder(clearPendingFire);
        AssertMethodDoesNotUseRemainder(clearPendingHit);
        AssertHasPublicNestedType(typeof(CombatScheduler), "PendingFireMigrationSnapshot");
        AssertHasPublicNestedType(typeof(CombatScheduler), "PendingHitMigrationSnapshot");
        AssertHasInstanceMethod(typeof(CombatScheduler), "CapturePendingCombatForMigration");
        AssertHasInstanceMethod(typeof(CombatScheduler), "RestorePendingCombatFromMigration");
        AssertHasInstanceMethod(typeof(HostMigrationHandler), "CaptureDurablePendingCombat");
        AssertHasInstanceMethod(typeof(HostMigrationHandler), "RestoreCachedPendingCombatForMigration");

        MethodInfo capture = GetRequiredMethod(typeof(MPTestStateSnapshot), nameof(MPTestStateSnapshot.Capture), StaticMembers);
        MethodInfo captureBudget = GetRequiredMethod(typeof(MPTestStateSnapshot), "CaptureNetworkBudget", StaticMembers);
        AssertMethodReferences(capture, typeof(MPTestStateSnapshot), "CaptureNetworkBudget");
        AssertMethodReferences(captureBudget, typeof(CombatScheduler), nameof(CombatScheduler.GetNetworkBudgetReport));
        AssertMethodReferences(captureBudget, typeof(Fusion.NetworkObject), "GetWordCount");
        AssertJsonFieldName(typeof(MPTestStateSnapshot.Snapshot), "NetworkBudget", "networkBudget");
        AssertPythonComparatorRejectsNetworkBudgetDrops();

        AssertHasInstanceMethod(typeof(CombatScheduler), "MPTestInjectPendingCombatLoad");
        AssertHasInstanceMethod(typeof(CombatScheduler), "MPTestClearInjectedPendingCombatLoad");
        MethodInfo route = GetRequiredMethod(typeof(MPTestAutomationServer), "Route", InstanceMembers);
        AssertMethodHasStringLiteral(route, "/test/injectPendingCombatLoad");
        AssertMethodReferences(route, typeof(MPTestAutomationServer), "InjectPendingCombatLoadForTest");
        Assert.That(_automationClientSource, Does.Contain("def inject_pending_combat_load"));
        Assert.That(_battleCommonSource, Does.Contain("inject_pending_load_before_migration"));
        Assert.That(_battleCommonSource, Does.Contain("pending_fire_not_restored"));
        Assert.That(_battleCommonSource, Does.Contain("pending_hit_not_restored"));
        Assert.That(_runMatrixSource, Does.Contain("\"network-budget-pending-stress\""));
        Assert.That(_pendingStressCaseSource, Does.Contain("inject_pending_load_before_migration=True"));
    }

    [Test]
    public void DeathClearsSchedulerStatusEffects()
    {
        MethodInfo unitDie = GetRequiredMethod(typeof(Unit), "Die", InstanceMembers);
        MethodInfo monsterDie = GetRequiredMethod(typeof(Monster), "Die", InstanceMembers);
        AssertMethodReferences(unitDie, typeof(BuffManager), nameof(BuffManager.ClearAllStatusEffects));
        AssertMethodReferences(monsterDie, typeof(BuffManager), nameof(BuffManager.ClearAllStatusEffects));
    }

    [Test]
    public void MPHarnessCanInjectAndPreserveActiveStatusThroughHostMigration()
    {
        MethodInfo route = GetRequiredMethod(typeof(MPTestAutomationServer), "Route", InstanceMembers);
        AssertMethodHasStringLiteral(route, "/test/applyStatusEffect");
        AssertMethodReferences(route, typeof(MPTestAutomationServer), "ApplyStatusEffectForTest");
        AssertMethodReferences(
            GetRequiredMethod(typeof(MPTestAutomationServer), "ApplyStatusEffectForTest", InstanceMembers),
            typeof(MPTestAutomationServer),
            "TryFindStatusEffectTarget");

        MethodInfo targetFinder = GetRequiredMethod(
            typeof(MPTestAutomationServer),
            "TryFindStatusEffectTarget",
            StaticMembers);
        MethodInfo targetOrderKey = GetRequiredMethod(
            typeof(MPTestAutomationServer),
            "GetNetworkObjectOrderKey",
            StaticMembers);
        AssertMethodReferences(targetFinder, typeof(MPTestAutomationServer), "GetNetworkObjectOrderKey");
        Assert.That(targetOrderKey.ReturnType, Is.EqualTo(typeof(uint)));

        MethodInfo spawnExecuted = GetRequiredMethod(
            typeof(BattleSpawnMonsterCommand),
            "Executed",
            InstanceMembers);
        MethodInfo spawnRejected = GetRequiredMethod(
            typeof(BattleSpawnMonsterCommand),
            "Reject",
            InstanceMembers);
        AssertMethodReferences(spawnExecuted, typeof(NetworkRunner), "get_IsRunning");
        AssertMethodReferences(spawnExecuted, typeof(NetworkObject), "get_HasStateAuthority");
        AssertMethodReferences(
            spawnExecuted,
            typeof(HostMigrationHandler),
            nameof(HostMigrationHandler.TryPushHostMigrationSnapshot));
        AssertNoMethodReference(
            spawnRejected,
            typeof(HostMigrationHandler),
            nameof(HostMigrationHandler.TryPushHostMigrationSnapshot));
        AssertMethodHasStringLiteral(spawnExecuted, "BattleSpawnMonster:Committed");

        AssertMethodHasStringLiteral(route, "/test/pushHostMigrationSnapshot");
        AssertMethodReferences(
            route,
            typeof(MPTestAutomationServer),
            "PushHostMigrationSnapshotForTestAsync");
        MethodInfo pushEndpoint = GetRequiredMethod(
            typeof(MPTestAutomationServer),
            "PushHostMigrationSnapshotForTestAsync",
            InstanceMembers);
        AssertMethodReferences(
            pushEndpoint,
            typeof(HostMigrationHandler),
            nameof(HostMigrationHandler.PushHostMigrationSnapshotAsync));

        MethodInfo pushSnapshotAsync = GetRequiredMethod(
            typeof(HostMigrationHandler),
            nameof(HostMigrationHandler.PushHostMigrationSnapshotAsync),
            InstanceMembers);
        Assert.That(pushSnapshotAsync.ReturnType, Is.EqualTo(typeof(UniTask<bool>)));
        Assert.That(
            pushSnapshotAsync.GetParameters().Last().ParameterType,
            Is.EqualTo(typeof(CancellationToken)));
        MethodInfo fusionPushSnapshot = typeof(NetworkRunner)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Single(method =>
                method.Name == "PushHostMigrationSnapshot" &&
                method.GetParameters().Length == 0);
        Assert.That(
            fusionPushSnapshot.ReturnType,
            Is.EqualTo(typeof(Task<bool>)),
            "Fusion snapshot publication must be observed through its real Task<bool> result.");
        MethodInfo invokeSnapshotPush = GetRequiredMethod(
            typeof(HostMigrationHandler),
            "InvokeHostMigrationSnapshotPushAsync",
            InstanceMembers);
        AssertMethodReferences(invokeSnapshotPush, typeof(Task<bool>), "GetAwaiter");
        AssertMethodHasStringLiteral(invokeSnapshotPush, "push_task_returned_false");

        FieldInfo pushMinInterval = typeof(HostMigrationHandler).GetField(
            "HostMigrationSnapshotPushMinIntervalSeconds",
            StaticMembers);
        FieldInfo pushMaxAttempts = typeof(HostMigrationHandler).GetField(
            "HostMigrationSnapshotPushMaxAttempts",
            StaticMembers);
        Assert.That(pushMinInterval, Is.Not.Null);
        Assert.That((float)pushMinInterval.GetRawConstantValue(), Is.GreaterThanOrEqualTo(1.2f));
        Assert.That(pushMaxAttempts, Is.Not.Null);
        Assert.That(pushMaxAttempts.GetRawConstantValue(), Is.EqualTo(5));

        MethodInfo processSnapshotPush = GetRequiredMethod(
            typeof(HostMigrationHandler),
            "ProcessHostMigrationSnapshotPushQueueAsync",
            InstanceMembers);
        AssertMethodReferences(
            processSnapshotPush,
            typeof(HostMigrationHandler),
            "InvokeHostMigrationSnapshotPushAsync");
        AssertMethodHasStringLiteral(invokeSnapshotPush, "handler_snapshot_push_retry");
        MethodInfo waitForSnapshotWindow = GetRequiredMethod(
            typeof(HostMigrationHandler),
            "WaitForHostMigrationSnapshotPushWindowAsync",
            InstanceMembers);
        AssertMethodReferences(
            waitForSnapshotWindow,
            typeof(HostMigrationHandler),
            "IsPreviousHostMigrationSnapshotConfirmationPending");
        MethodInfo snapshotTickReader = GetRequiredMethod(
            typeof(HostMigrationHandler),
            "TryReadHostMigrationSnapshotTicks",
            InstanceMembers);
        AssertMethodHasStringLiteral(snapshotTickReader, "LastSnapshotTick");
        AssertMethodHasStringLiteral(snapshotTickReader, "LastConfirmedSnapshotTick");
        MethodInfo waitForSnapshotConfirmation = GetRequiredMethod(
            typeof(HostMigrationHandler),
            "WaitForHostMigrationSnapshotConfirmationAsync",
            InstanceMembers);
        AssertMethodReferences(
            waitForSnapshotConfirmation,
            typeof(HostMigrationHandler),
            "TryReadHostMigrationSnapshotTicks");

        MethodInfo deferredCombatRestore = GetRequiredMethod(
            typeof(HostMigrationHandler),
            "TrackCombatSchedulerRestoreAfterFieldUnitsAsync",
            InstanceMembers);
        AssertMethodReferences(
            deferredCombatRestore,
            typeof(HostMigrationHandler),
            "AreDurableFieldUnitRestoresTerminal");
        AssertMethodReferences(
            deferredCombatRestore,
            typeof(HostMigrationHandler),
            "RestoreCachedZonesForMigration");
        AssertMethodReferences(
            deferredCombatRestore,
            typeof(HostMigrationHandler),
            "RestoreCachedStatBuffsForMigration");
        AssertMethodReferences(
            deferredCombatRestore,
            typeof(HostMigrationHandler),
            "RestoreCachedStatusEffectsForMigration");
        AssertMethodReferences(
            deferredCombatRestore,
            typeof(HostMigrationHandler),
            "RestoreCachedPendingCombatForMigration");
        AssertMethodReferences(
            deferredCombatRestore,
            typeof(CancellationToken),
            nameof(CancellationToken.ThrowIfCancellationRequested));
        AssertMethodReferences(deferredCombatRestore, typeof(NetworkObject), "get_HasStateAuthority");
        AssertMethodHasStringLiteral(deferredCombatRestore, "field_unit_restore_timeout");

        Assert.That(_automationClientSource, Does.Contain("def push_host_migration_snapshot"));
        Assert.That(_battleCommonSource, Does.Contain("require_host_migration_snapshot_push=True"));
        Assert.That(_battleCommonSource, Does.Contain("hostMigrationSnapshotPushReady"));
        Assert.That(_automationClientSource, Does.Contain("def apply_status_effect"));
        Assert.That(_battleCommonSource, Does.Contain("apply_status_before_migration"));
        Assert.That(_battleCommonSource, Does.Contain("require_active_status"));
        Assert.That(_battleCommonSource, Does.Contain("active_status_count"));
        Assert.That(_runMatrixSource, Does.Contain("\"status-effect-host-migration\""));
        Assert.That(_statusMigrationCaseSource, Does.Contain("apply_status_before_migration=True"));

        MethodInfo resolveGameManagers = GetRequiredMethod(typeof(HostMigrationHandler), "ResolveGameManagersForRunner", InstanceMembers);
        AssertMethodReferences(resolveGameManagers, typeof(HostMigrationHandler), "RebindCombatSchedulerForGameManagers");
        AssertMethodHasStringLiteral(resolveGameManagers, "ResolveGameManagersForRunner.RestoredCandidate");
        AssertHasInstanceMethod(typeof(HostMigrationHandler), "CaptureDurableStatusEffects");
        AssertHasInstanceMethod(typeof(HostMigrationHandler), "RestoreCachedStatusEffectsForMigration");
    }

    [Test]
    public void MPHarnessCanInjectAndPreserveActiveStatBuffThroughHostMigration()
    {
        MethodInfo route = GetRequiredMethod(typeof(MPTestAutomationServer), "Route", InstanceMembers);
        AssertMethodHasStringLiteral(route, "/test/applyStatBuff");
        AssertMethodReferences(route, typeof(MPTestAutomationServer), "ApplyStatBuffForTest");
        Assert.That(_automationClientSource, Does.Contain("def apply_stat_buff"));
        Assert.That(_battleCommonSource, Does.Contain("apply_stat_buff_before_migration"));
        Assert.That(_battleCommonSource, Does.Contain("require_active_buff"));
        Assert.That(_battleCommonSource, Does.Contain("active_buff_count"));
        Assert.That(_runMatrixSource, Does.Contain("\"stat-buff-host-migration\""));
        Assert.That(_statBuffMigrationCaseSource, Does.Contain("apply_stat_buff_before_migration=True"));

        AssertHasInstanceMethod(typeof(HostMigrationHandler), "CaptureDurableStatBuffs");
        AssertHasInstanceMethod(typeof(HostMigrationHandler), "RestoreCachedStatBuffsForMigration");
    }

    [Test]
    public void MPHarnessCanInjectAndPreserveActiveZoneThroughHostMigration()
    {
        MethodInfo route = GetRequiredMethod(typeof(MPTestAutomationServer), "Route", InstanceMembers);
        AssertMethodHasStringLiteral(route, "/test/applyZone");
        AssertMethodReferences(route, typeof(MPTestAutomationServer), "ApplyZoneForTest");
        Assert.That(_automationClientSource, Does.Contain("def apply_zone"));
        Assert.That(_battleCommonSource, Does.Contain("apply_zone_before_migration"));
        Assert.That(_battleCommonSource, Does.Contain("require_active_zone"));
        Assert.That(_battleCommonSource, Does.Contain("active_zone_count"));
        Assert.That(_runMatrixSource, Does.Contain("\"zone-host-migration\""));
        Assert.That(_zoneMigrationCaseSource, Does.Contain("apply_zone_before_migration=True"));

        AssertHasInstanceMethod(typeof(HostMigrationHandler), "CaptureDurableZones");
        AssertHasInstanceMethod(typeof(HostMigrationHandler), "RestoreCachedZonesForMigration");
    }

    private static MethodInfo GetRequiredMethod(Type type, string methodName, BindingFlags flags)
    {
        MethodInfo[] matches = type.GetMethods(flags).Where(method => method.Name == methodName).ToArray();
        Assert.That(matches, Has.Length.EqualTo(1), $"Expected one {type.Name}.{methodName} method");
        return matches[0];
    }

    private static void AssertHasAttribute(MemberInfo member, string attributeTypeName)
    {
        Assert.That(
            member.CustomAttributes.Any(attribute => attribute.AttributeType.Name == attributeTypeName),
            Is.True,
            $"{member.DeclaringType?.Name}.{member.Name} must retain {attributeTypeName} metadata");
    }

    private static void AssertJsonFieldName(Type type, string fieldName, string expectedJsonName)
    {
        FieldInfo field = type.GetField(fieldName, InstanceMembers);
        Assert.That(field, Is.Not.Null, $"{type.Name}.{fieldName}");
        JsonPropertyAttribute jsonProperty = field.GetCustomAttribute<JsonPropertyAttribute>();
        Assert.That(jsonProperty, Is.Not.Null, $"{type.Name}.{fieldName} JsonProperty metadata");
        Assert.That(jsonProperty.PropertyName, Is.EqualTo(expectedJsonName));
    }

    private static void AssertMethodReferences(MethodBase method, Type declaringType, string referencedMethodName)
    {
        Assert.That(method, Is.Not.Null);
        bool found = GetReachableInstructions(method)
            .Any(instruction => instruction.Member is MethodBase referenced &&
                                referenced.DeclaringType == declaringType &&
                                referenced.Name == referencedMethodName);
        Assert.That(found, Is.True, $"{method.DeclaringType?.Name}.{method.Name} must reference {declaringType.Name}.{referencedMethodName}");
    }

    private static void AssertNoMethodReference(MethodBase method, Type declaringType, string referencedMethodName)
    {
        bool found = GetReachableInstructions(method)
            .Any(instruction => instruction.Member is MethodBase referenced &&
                                referenced.DeclaringType == declaringType &&
                                referenced.Name == referencedMethodName);
        Assert.That(found, Is.False, $"{method.DeclaringType?.Name}.{method.Name} must not reference {declaringType.Name}.{referencedMethodName}");
    }

    private static void AssertNoGenericMethodReference(
        MethodBase method,
        Type declaringType,
        string referencedMethodName,
        Type genericArgument)
    {
        bool found = GetReachableInstructions(method)
            .Any(instruction => instruction.Member is MethodInfo referenced &&
                                referenced.DeclaringType == declaringType &&
                                referenced.Name == referencedMethodName &&
                                referenced.IsGenericMethod &&
                                referenced.GetGenericArguments().Contains(genericArgument));
        Assert.That(found, Is.False, $"{method.DeclaringType?.Name}.{method.Name} must not scan {genericArgument.Name}");
    }

    private static void AssertTypeMethodsReference(Type type, Type declaringType, string referencedMethodName)
    {
        bool found = GetDeclaredMethods(type)
            .SelectMany(GetReachableInstructions)
            .Any(instruction => instruction.Member is MethodBase referenced &&
                                referenced.DeclaringType == declaringType &&
                                referenced.Name == referencedMethodName);
        Assert.That(found, Is.True, $"{type.Name} must reference {declaringType.Name}.{referencedMethodName}");
    }

    private static void AssertTypeMethodsDoNotReference(Type type, Type declaringType, string referencedMethodName)
    {
        bool found = GetDeclaredMethods(type)
            .SelectMany(GetReachableInstructions)
            .Any(instruction => instruction.Member is MethodBase referenced &&
                                referenced.DeclaringType == declaringType &&
                                referenced.Name == referencedMethodName);
        Assert.That(found, Is.False, $"{type.Name} must not reference {declaringType.Name}.{referencedMethodName}");
    }

    private static IEnumerable<MethodInfo> GetDeclaredMethods(Type type)
    {
        return type.GetMethods(InstanceMembers | StaticMembers | BindingFlags.DeclaredOnly)
            .Where(method => method.GetMethodBody() != null);
    }

    private static void AssertMethodHasStringLiteral(MethodBase method, string expectedLiteralPart)
    {
        bool found = GetReachableInstructions(method)
            .Any(instruction => instruction.StringOperand != null &&
                                instruction.StringOperand.Contains(expectedLiteralPart));
        Assert.That(found, Is.True, $"{method.DeclaringType?.Name}.{method.Name} must retain literal '{expectedLiteralPart}'");
    }

    private static void AssertMethodDoesNotUseRemainder(MethodBase method)
    {
        Assert.That(
            ReadInstructions(method).Any(instruction =>
                instruction.OpCode == OpCodes.Rem || instruction.OpCode == OpCodes.Rem_Un),
            Is.False,
            $"{method.DeclaringType?.Name}.{method.Name} must not use modulo slot selection");
    }

    private static void AssertCapacityPressureBackpressuresWithoutDrop(MethodBase method, string emptySlotMethodName)
    {
        List<IlInstruction> instructions = ReadInstructions(method);
        Assert.That(
            instructions.Any(instruction => instruction.Member is MethodBase referenced &&
                                            referenced.DeclaringType == typeof(CombatScheduler) &&
                                            referenced.Name == emptySlotMethodName),
            Is.True,
            $"{method.Name} must search for a free slot using {emptySlotMethodName}");

        List<int> recoveryIndices = instructions
            .Select((instruction, index) => (instruction, index))
            .Where(pair =>
                pair.instruction.Member is MethodBase referenced &&
                referenced.DeclaringType == typeof(CombatScheduler) &&
                referenced.Name == "RecordCapacityRecovery")
            .Select(pair => pair.index)
            .ToList();
        Assert.That(recoveryIndices, Is.Not.Empty, $"{method.Name} must record recoverable capacity pressure");
        Assert.That(
            instructions.Any(instruction =>
                instruction.Member is MethodBase referenced &&
                referenced.DeclaringType == typeof(CombatScheduler) &&
                referenced.Name == "RecordNetworkBudgetDrop"),
            Is.False,
            $"{method.Name} must not classify recoverable capacity pressure as a true drop");

        bool hasBackpressureReturn = recoveryIndices.Any(recoveryIndex =>
        {
            int returnIndex = instructions.FindIndex(recoveryIndex + 1, instruction => instruction.OpCode == OpCodes.Ret);
            if (returnIndex <= recoveryIndex)
            {
                return false;
            }

            int valueIndex = returnIndex - 1;
            while (valueIndex > recoveryIndex && instructions[valueIndex].OpCode == OpCodes.Nop)
            {
                valueIndex--;
            }

            return instructions[valueIndex].OpCode == OpCodes.Ldc_I4_0;
        });
        Assert.That(hasBackpressureReturn, Is.True, $"{method.Name} must have a recoverable capacity branch that returns false/zero");
    }

    private static void AssertPythonComparatorRejectsNetworkBudgetDrops()
    {
        const string script =
            "from compare_state_snapshots import assert_no_network_budget_drops\n" +
            "errors = []\n" +
            "assert_no_network_budget_drops(errors, 'budget', {'statusCapacityDrops': 0, 'pendingHitCapacityDrops': 0})\n" +
            "assert errors == [], errors\n" +
            "assert_no_network_budget_drops(errors, 'budget', {'statusCapacityDrops': 2})\n" +
            "assert errors == ['budget.statusCapacityDrops expected=0 actual=2'], errors\n";

        string harnessDirectory = Path.GetFullPath(Path.Combine(
            Application.dataPath,
            "..",
            "..",
            "tools",
            "harness",
            "mp"));
        Assert.That(Directory.Exists(harnessDirectory), Is.True, harnessDirectory);

        string payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(script));
        var startInfo = new ProcessStartInfo
        {
            FileName = "python",
            Arguments = $"-c \"import base64;exec(base64.b64decode('{payload}'))\"",
            WorkingDirectory = harnessDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.EnvironmentVariables["PYTHONDONTWRITEBYTECODE"] = "1";

        using (Process process = Process.Start(startInfo))
        {
            Assert.That(process, Is.Not.Null, "python comparator process");
            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();
            bool exited = process.WaitForExit(15000);
            if (!exited)
            {
                process.Kill();
            }

            Assert.That(exited, Is.True, "Python comparator behavior check timed out");
            Assert.That(process.ExitCode, Is.EqualTo(0), $"stdout={stdout}\nstderr={stderr}");
        }
    }

    private static List<IlInstruction> GetReachableInstructions(MethodBase root)
    {
        var instructions = new List<IlInstruction>();
        var pending = new Queue<MethodBase>();
        var visited = new HashSet<MethodBase>();
        Type rootType = GetTopLevelType(root.DeclaringType);
        EnqueueImplementation(root, pending);

        while (pending.Count > 0)
        {
            MethodBase method = pending.Dequeue();
            if (method == null || !visited.Add(method))
            {
                continue;
            }

            List<IlInstruction> methodInstructions = ReadInstructions(method);
            instructions.AddRange(methodInstructions);
            foreach (MethodBase referenced in methodInstructions
                         .Select(instruction => instruction.Member)
                         .OfType<MethodBase>())
            {
                if (GetTopLevelType(referenced.DeclaringType) == rootType)
                {
                    EnqueueImplementation(referenced, pending);
                }
            }
        }

        return instructions;
    }

    private static void EnqueueImplementation(MethodBase method, Queue<MethodBase> pending)
    {
        pending.Enqueue(method);
        if (method is MethodInfo methodInfo)
        {
            AsyncStateMachineAttribute asyncStateMachine = methodInfo.GetCustomAttribute<AsyncStateMachineAttribute>();
            MethodInfo moveNext = asyncStateMachine?.StateMachineType.GetMethod("MoveNext", InstanceMembers);
            if (moveNext != null)
            {
                pending.Enqueue(moveNext);
            }
        }
    }

    private static Type GetTopLevelType(Type type)
    {
        while (type?.DeclaringType != null)
        {
            type = type.DeclaringType;
        }

        return type;
    }

    private static List<IlInstruction> ReadInstructions(MethodBase method)
    {
        var instructions = new List<IlInstruction>();
        MethodBody body = method?.GetMethodBody();
        byte[] bytes = body?.GetILAsByteArray();
        if (bytes == null)
        {
            return instructions;
        }

        Type[] typeArguments = method.DeclaringType?.IsGenericType == true
            ? method.DeclaringType.GetGenericArguments()
            : Type.EmptyTypes;
        Type[] methodArguments = method.IsGenericMethod
            ? method.GetGenericArguments()
            : Type.EmptyTypes;

        int position = 0;
        while (position < bytes.Length)
        {
            int offset = position;
            byte first = bytes[position++];
            OpCode opCode = first == 0xFE
                ? MultiByteOpCodes[bytes[position++]]
                : SingleByteOpCodes[first];
            MemberInfo member = null;
            string stringOperand = null;

            switch (opCode.OperandType)
            {
                case OperandType.InlineNone:
                    break;
                case OperandType.ShortInlineBrTarget:
                case OperandType.ShortInlineI:
                case OperandType.ShortInlineVar:
                    position += 1;
                    break;
                case OperandType.InlineVar:
                    position += 2;
                    break;
                case OperandType.InlineBrTarget:
                case OperandType.InlineField:
                case OperandType.InlineI:
                case OperandType.InlineSig:
                case OperandType.InlineTok:
                case OperandType.InlineType:
                    position += 4;
                    break;
                case OperandType.InlineMethod:
                {
                    int token = BitConverter.ToInt32(bytes, position);
                    position += 4;
                    try
                    {
                        member = method.Module.ResolveMethod(token, typeArguments, methodArguments);
                    }
                    catch (ArgumentException)
                    {
                        member = method.Module.ResolveMember(token, typeArguments, methodArguments);
                    }

                    break;
                }
                case OperandType.InlineString:
                {
                    int token = BitConverter.ToInt32(bytes, position);
                    position += 4;
                    stringOperand = method.Module.ResolveString(token);
                    break;
                }
                case OperandType.ShortInlineR:
                    position += 4;
                    break;
                case OperandType.InlineI8:
                case OperandType.InlineR:
                    position += 8;
                    break;
                case OperandType.InlineSwitch:
                {
                    int count = BitConverter.ToInt32(bytes, position);
                    position += 4 + (count * 4);
                    break;
                }
                default:
                    throw new InvalidOperationException($"Unsupported IL operand {opCode.OperandType} in {method.Name} at {offset}");
            }

            instructions.Add(new IlInstruction(opCode, member, stringOperand));
        }

        return instructions;
    }

    private readonly struct IlInstruction
    {
        public IlInstruction(OpCode opCode, MemberInfo member, string stringOperand)
        {
            OpCode = opCode;
            Member = member;
            StringOperand = stringOperand;
        }

        public OpCode OpCode { get; }
        public MemberInfo Member { get; }
        public string StringOperand { get; }
    }
}
#endif
