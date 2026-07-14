using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public class CombatSchedulerCapacityEditModeTests
{
    private const BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags StaticMembers = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    [Test]
    public void CapacityConfigurationUsesProjectPopulationAndRequiresDeterministicRecovery()
    {
        Assert.That(CombatSchedulerCapacityConfig.ValidateConfiguration(out string reason), Is.True, reason);

        FieldInfo maxPlayers = typeof(GameManagers).GetField("MAX_PLAYERS", StaticMembers);
        Assert.That(maxPlayers, Is.Not.Null);
        Assert.That(maxPlayers.GetRawConstantValue(), Is.EqualTo(CombatSchedulerCapacityConfig.MaximumPlayers));

        var fieldObject = new GameObject("scheduler-capacity-field-schema");
        try
        {
            var field = fieldObject.AddComponent<FieldManager>();
            Assert.That(field.gridSize.x, Is.EqualTo(CombatSchedulerCapacityConfig.DefaultFieldGridWidth));
            Assert.That(field.gridSize.y, Is.EqualTo(CombatSchedulerCapacityConfig.DefaultFieldGridHeight));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(fieldObject);
        }

        Assert.That(CombatSchedulerCapacityConfig.MaximumPlacedUnitsPerField, Is.EqualTo(89));
        Assert.That(CombatSchedulerCapacityConfig.MaximumPlacedUnitsAcrossMatch, Is.EqualTo(356));
        Assert.That(CombatSchedulerCapacityConfig.PendingFireRecoveryRequired, Is.True);
        Assert.That(CombatSchedulerCapacityConfig.PendingHitRecoveryRequired, Is.True);
        Assert.That(CombatSchedulerCapacityConfig.HasFiniteMonsterAttackBurst, Is.False,
            "round/Black-Magic scaling has no finite code-level monster burst cap");
    }

    [Test]
    public void CapacityRecoveryOrderingIsEarliestTickThenLowestSequence()
    {
        Assert.That(CombatSchedulerCapacityConfig.IsEarlier(9, 100, 10, 1), Is.True);
        Assert.That(CombatSchedulerCapacityConfig.IsEarlier(10, 1, 10, 2), Is.True);
        Assert.That(CombatSchedulerCapacityConfig.IsEarlier(10, 2, 10, 1), Is.False);
        Assert.That(CombatSchedulerCapacityConfig.IsEarlier(11, 1, 10, 100), Is.False);
    }

    [Test]
    public void CapacityValidationRejectsLoadsBeyondEachReplicatedBudget()
    {
        Assert.That(CombatSchedulerCapacityConfig.ValidateSupportedLoad(
            CombatSchedulerCapacityConfig.PendingFireCapacity,
            CombatSchedulerCapacityConfig.PendingHitCapacity,
            CombatSchedulerCapacityConfig.StatusEffectCapacity,
            CombatSchedulerCapacityConfig.StatBuffCapacity,
            CombatSchedulerCapacityConfig.ZoneCapacity,
            out string reason), Is.True, reason);

        Assert.That(CombatSchedulerCapacityConfig.ValidateSupportedLoad(
            CombatSchedulerCapacityConfig.PendingFireCapacity + 1,
            0,
            0,
            0,
            0,
            out reason), Is.False);
        Assert.That(reason, Does.Contain("pendingFire"));
    }

    [Test]
    public void ZoneBackpressureRetainsTheOriginalTickCadenceUntilAllDueTicksDrain()
    {
        MethodInfo advance = typeof(CombatScheduler).GetMethod("AdvanceScheduledZoneTick", StaticMembers);
        MethodInfo hasTick = typeof(CombatScheduler).GetMethod("HasScheduledZoneTickBeforeExpiry", StaticMembers);
        MethodInfo countDue = typeof(CombatScheduler).GetMethod("CountDueZoneTicksAtPhaseTransition", StaticMembers);
        Assert.That(advance, Is.Not.Null);
        Assert.That(hasTick, Is.Not.Null);
        Assert.That(countDue, Is.Not.Null);

        Assert.That(advance.Invoke(null, new object[] { 100, 5 }), Is.EqualTo(105));
        Assert.That(advance.Invoke(null, new object[] { 105, 5 }), Is.EqualTo(110));
        Assert.That(hasTick.Invoke(null, new object[] { 109, 110 }), Is.EqualTo(true));
        Assert.That(hasTick.Invoke(null, new object[] { 110, 110 }), Is.EqualTo(false));
        Assert.That(countDue.Invoke(null, new object[] { 100, 121, 5, 112 }), Is.EqualTo(3));
        Assert.That(countDue.Invoke(null, new object[] { 115, 121, 5, 112 }), Is.EqualTo(0));

        var overdueWithBacklog = new CombatScheduler.ZoneMigrationSnapshot
        {
            Sequence = 1,
            NextTick = 109,
            ExpireTick = 110
        };
        Assert.That(CombatScheduler.IsZoneMigrationSnapshotTerminal(overdueWithBacklog, 120), Is.False);
        overdueWithBacklog.NextTick = 110;
        Assert.That(CombatScheduler.IsZoneMigrationSnapshotTerminal(overdueWithBacklog, 120), Is.True);
    }

    [Test]
    public void BackpressurePathsPreserveDueWorkAndMigrationSourceContracts()
    {
        Assert.That(typeof(CombatScheduler).GetMethod("TryFlushEarliestPendingFireForCapacity", InstanceMembers), Is.Null);
        Assert.That(typeof(CombatScheduler).GetMethod("TryFlushEarliestPendingHitForCapacity", InstanceMembers), Is.Null);

        MethodInfo scheduleHit = RequireMethod(nameof(CombatScheduler.ScheduleHit));
        MethodInfo scheduleResolvedHit = RequireMethod("ScheduleResolvedHit");
        MethodInfo processDueFires = RequireMethod("ProcessDueFires");
        Assert.That(scheduleHit.ReturnType, Is.EqualTo(typeof(bool)));
        Assert.That(scheduleResolvedHit.ReturnType, Is.EqualTo(typeof(bool)));
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(processDueFires, typeof(CombatScheduler), "ScheduleResolvedHit"), Is.True);
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(processDueFires, typeof(CombatScheduler), "ClearPendingFireSnapshot"), Is.True);

        MethodInfo applyStatus = RequireMethod(nameof(CombatScheduler.ApplyStatusEffect));
        MethodInfo applyStatBuff = RequireMethod("ApplyStatBuffInternal");
        MethodInfo applyZone = RequireMethod(nameof(CombatScheduler.TryScheduleZone));
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(applyStatus, typeof(CombatScheduler), "TryCoalesceBooleanStatusAtCapacity"), Is.True);
        Assert.That(typeof(CombatScheduler).GetMethod("TryCoalesceCompatibleStatusAtCapacity", InstanceMembers), Is.Null);
        Assert.That(typeof(CombatScheduler).GetMethod("TryCoalesceCompatibleStatBuffAtCapacity", InstanceMembers), Is.Null);
        Assert.That(typeof(CombatScheduler).GetMethod("TryCoalesceCompatibleZoneAtCapacity", InstanceMembers), Is.Null);
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(applyStatBuff, typeof(CombatScheduler), "RecordCapacityRecovery"), Is.True);
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(applyZone, typeof(CombatScheduler), "RecordCapacityRecovery"), Is.True);

        Assert.That(HasMethod(typeof(CombatScheduler), nameof(CombatScheduler.CanApplyStatusEffectBatch)), Is.True);
        Assert.That(HasMethod(typeof(CombatScheduler), nameof(CombatScheduler.CanApplyStatBuffBatch)), Is.True);
        Assert.That(typeof(CombatScheduler).GetMethod(nameof(CombatScheduler.CanScheduleZone), InstanceMembers), Is.Not.Null);
        Assert.That(typeof(CombatScheduler).GetMethod(nameof(CombatScheduler.HasUnresolvedDueZoneDebt), InstanceMembers), Is.Not.Null);
        Assert.That(typeof(CombatScheduler).GetMethod(nameof(CombatScheduler.CanScheduleImmediateHitBatch), InstanceMembers), Is.Not.Null);
        Assert.That(typeof(SkillEffect).GetMethod(nameof(SkillEffect.CanApplyAllEffects), StaticMembers), Is.Not.Null);
        Assert.That(typeof(ZoneController).GetMethod(nameof(ZoneController.ApplyScheduledTick), InstanceMembers)?.ReturnType,
            Is.EqualTo(typeof(bool)));

        AssertNoStackField("StatusEffectEntry");
        AssertNoStackField("StatBuffEntry");
        AssertNoStackField("ZoneEntry");
        AssertNoStackField("StatusEffectMigrationSnapshot", BindingFlags.Public);
        AssertNoStackField("StatBuffMigrationSnapshot", BindingFlags.Public);
        AssertNoStackField("ZoneMigrationSnapshot", BindingFlags.Public);

        MethodInfo unitAttack = RequireMethod(typeof(Unit), "Attack");
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(
            unitAttack,
            typeof(CombatScheduler),
            nameof(CombatScheduler.CanScheduleImmediateHitBatch)), Is.True,
            "melee splash must preflight the exact valid NetworkObject batch before enqueue");

        Assert.That(typeof(Unit).GetProperty("NetworkedBasicAttackCapacityBackpressurePending", InstanceMembers), Is.Not.Null);
        Assert.That(typeof(Monster).GetProperty("NetworkedBasicAttackCapacityBackpressurePending", InstanceMembers), Is.Not.Null);
        Assert.That(typeof(Monster).GetProperty("NetworkedBasicAttackCapacityBackpressureTargetId", InstanceMembers), Is.Not.Null);
        Assert.That(typeof(FieldUnitMigrationSnapshot).GetField(
            nameof(FieldUnitMigrationSnapshot.BasicAttackCapacityBackpressurePending)), Is.Not.Null);
    }

    [Test]
    public void BattleExitWaitsForLosslessPagedZoneDebtWithoutTimeoutSafeStop()
    {
        MethodInfo begin = RequireMethod(typeof(GameManagers), "BeginSequenceTransition");
        MethodInfo complete = RequireMethod(typeof(GameManagers), "CompleteSequenceTransition");
        MethodInfo closeZone = RequireMethod(typeof(CombatScheduler), "CloseScheduledZoneForPhaseTransition", 4);
        MethodInfo applyZoneTick = RequireMethod(typeof(CombatScheduler), "ApplyZoneTick");

        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(
            begin,
            typeof(GameManagers),
            "TryGetUnresolvedCombatExitDebt"), Is.True);
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(
            complete,
            typeof(GameManagers),
            "TryGetUnresolvedCombatExitDebt"), Is.True);
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(
            closeZone,
            typeof(CombatScheduler),
            "RecordNetworkBudgetDrop"), Is.True,
            "a gate bypass must be an explicit test-visible loss, never silent cleanup");
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(
            applyZoneTick,
            typeof(SkillEffect),
            nameof(SkillEffect.CanApplyEffectsFromIndex)), Is.True,
            "zone pulses must page through exact per-target capacity admission instead of requiring the whole crowd to fit");

        Assert.That(typeof(GameManagers).GetProperty(nameof(GameManagers.IsCombatExitDebtGateActive), InstanceMembers), Is.Not.Null);
        Assert.That(typeof(GameManagers).GetMethod("EnterCombatExitDebtSafeStop", InstanceMembers), Is.Null);
        Assert.That(typeof(GameManagers).GetField("CombatExitDebtTimeoutTimer", InstanceMembers), Is.Null);
        Assert.That(typeof(CombatScheduler).GetProperty(nameof(CombatScheduler.IsZonePulseBackpressured), InstanceMembers), Is.Not.Null);
        Type zoneEntry = typeof(CombatScheduler).GetNestedType("ZoneEntry", InstanceMembers);
        Assert.That(zoneEntry?.GetField("ActivePulseToken", InstanceMembers), Is.Not.Null);
        Assert.That(zoneEntry?.GetField("PendingTargetCount", InstanceMembers), Is.Not.Null);
        Assert.That(typeof(CombatScheduler.ZoneMigrationSnapshot).GetField(
            nameof(CombatScheduler.ZoneMigrationSnapshot.ActivePulseToken)), Is.Not.Null);
        Assert.That(typeof(Unit).GetProperty("PendingZonePulseDebtToken", InstanceMembers), Is.Not.Null);
        Assert.That(typeof(Monster).GetProperty("PendingZonePulseDebtToken", InstanceMembers), Is.Not.Null);
        Assert.That(typeof(FieldUnitMigrationSnapshot).GetField(
            nameof(FieldUnitMigrationSnapshot.PendingZonePulseDebtToken)), Is.Not.Null);
        Assert.That(typeof(FieldUnitMigrationSnapshot).GetField(
            nameof(FieldUnitMigrationSnapshot.PendingZonePulseNextEffectIndex)), Is.Not.Null);
        Assert.That(typeof(FieldUnitMigrationSnapshot).GetField(
            nameof(FieldUnitMigrationSnapshot.BasicAttackDebtTargetId)), Is.Not.Null);
        Assert.That(typeof(FieldUnitMigrationSnapshot).GetField(
            nameof(FieldUnitMigrationSnapshot.BasicAttackDebtFireDelaySeconds)), Is.Not.Null);
        Assert.That(typeof(MPTestStateSnapshot.GameSnapshot).GetField("CombatExitDebtSafeStopped"), Is.Not.Null);
        Assert.That(typeof(GameManagers).GetProperty(
            nameof(GameManagers.IsCombatExitDebtTerminalFailureSafeStopped), InstanceMembers), Is.Not.Null);
        Assert.That(typeof(CombatScheduler).GetMethod(
            nameof(CombatScheduler.TryGetZoneDebtTerminalFailure), InstanceMembers), Is.Not.Null);
    }

    [Test]
    public void MaterializedZoneDiscoveryGapRetriesAndEffectCursorAdvancesMonotonically()
    {
        MethodInfo discoveryComplete = typeof(CombatScheduler).GetMethod(
            "IsMaterializedZoneTargetSetComplete",
            StaticMembers);
        Assert.That(discoveryComplete, Is.Not.Null);
        Assert.That(discoveryComplete.Invoke(null, new object[] { 3, 0 }), Is.EqualTo(false));
        Assert.That(discoveryComplete.Invoke(null, new object[] { 3, 2 }), Is.EqualTo(false));
        Assert.That(discoveryComplete.Invoke(null, new object[] { 3, 3 }), Is.EqualTo(true));

        var targetObject = new GameObject("zone-effect-cursor-target");
        try
        {
            Unit unit = targetObject.AddComponent<Unit>();
            MethodInfo mark = RequireMethod(typeof(Unit), "TryMarkPendingZonePulseDebt");
            MethodInfo advance = RequireMethod(typeof(Unit), "TryAdvancePendingZonePulseEffect");
            MethodInfo clear = RequireMethod(typeof(Unit), "ClearPendingZonePulseDebt");
            PropertyInfo cursor = typeof(Unit).GetProperty("PendingZonePulseNextEffectIndex", InstanceMembers);
            Assert.That(cursor, Is.Not.Null);

            Assert.That(mark.Invoke(unit, new object[] { 17 }), Is.EqualTo(true));
            Assert.That(cursor.GetValue(unit), Is.EqualTo(0));
            Assert.That(advance.Invoke(unit, new object[] { 17, 1 }), Is.EqualTo(true));
            Assert.That(advance.Invoke(unit, new object[] { 17, 2 }), Is.EqualTo(true));
            Assert.That(advance.Invoke(unit, new object[] { 17, 1 }), Is.EqualTo(false));
            Assert.That(cursor.GetValue(unit), Is.EqualTo(2),
                "a retry must resume after the committed effect prefix, never replay it");
            clear.Invoke(unit, new object[] { 17 });
            Assert.That(cursor.GetValue(unit), Is.EqualTo(0));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(targetObject);
        }
    }

    [Test]
    public void DevelopmentRuntimeProbeExercisesRealUnitAndMonsterCapacityDebt()
    {
        MethodInfo probe = typeof(CombatScheduler).GetMethod(
            "MPTestRunCapacityRecoveryProbe",
            InstanceMembers);
        Assert.That(probe, Is.Not.Null);
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(
            probe,
            typeof(Unit),
            "MPTestBeginBasicAttackCapacityRecoveryProbe"), Is.True);
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(
            probe,
            typeof(Unit),
            "MPTestResumeBasicAttackCapacityRecoveryProbe"), Is.True);
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(
            probe,
            typeof(Monster),
            "MPTestBeginBasicAttackCapacityRecoveryProbe"), Is.True);
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(
            probe,
            typeof(Monster),
            "MPTestResumeBasicAttackCapacityRecoveryProbe"), Is.True);

        MethodInfo unitSchedule = RequireMethod(typeof(Unit), "TryScheduleBasicAttackOrCaptureDebt");
        MethodInfo unitRetry = RequireMethod(typeof(Unit), "TryExecutePendingAttack");
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(
            unitSchedule,
            typeof(Unit),
            "CaptureBasicAttackCapacityDebt"), Is.True);
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(
            unitRetry,
            typeof(CombatScheduler),
            nameof(CombatScheduler.ScheduleHit)), Is.True);
    }

    [Test]
    public void SnapshotRebuildPreservesOriginalDueTicksAndOnlyBucketsClampToNow()
    {
        var schedulerObject = new GameObject("scheduler-snapshot-tick-test");
        try
        {
            var scheduler = schedulerObject.AddComponent<CombatScheduler>();
            AssertSnapshotTickRoundTrip(scheduler, "PendingFireSnapshot", "ReadPendingFireSnapshot", "FireTick", 17);
            AssertSnapshotTickRoundTrip(scheduler, "PendingHitSnapshot", "ReadPendingHitSnapshot", "HitTick", 23);

            MethodInfo addFire = RequireMethod("AddPendingFireToBucket");
            MethodInfo addHit = RequireMethod("AddPendingHitToBucket");
            Assert.That(MdfCompiledCodePolicy.ReferencesMethod(addFire, typeof(Mathf), nameof(Mathf.Max)), Is.True);
            Assert.That(MdfCompiledCodePolicy.ReferencesMethod(addHit, typeof(Mathf), nameof(Mathf.Max)), Is.True);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(schedulerObject);
        }
    }

    [Test]
    public void MultiplayerBasicAssertionRejectsTrueDropsButAllowsRecoveryCounters()
    {
        var snapshot = new MPTestStateSnapshot.Snapshot
        {
            Version = 2,
            Game = new MPTestStateSnapshot.GameSnapshot { HasGameManagers = true },
            Players = Array.Empty<MPTestStateSnapshot.PlayerSnapshot>(),
            NetworkBudget = new MPTestStateSnapshot.NetworkBudgetSnapshot
            {
                PendingFireCapacityFallbacks = 3,
                PendingHitCapacityFallbacks = 2,
                StatusCapacityCoalesces = 4
            }
        };

        MPTestAssertions.AssertionResult recovered = MPTestAssertions.AssertBasic(snapshot, expectedPlayers: 0);
        Assert.That(recovered.Errors, Is.Empty, "recovered pressure is diagnostic and must not be treated as loss");

        snapshot.NetworkBudget.PendingHitCapacityDrops = 1;
        MPTestAssertions.AssertionResult dropped = MPTestAssertions.AssertBasic(snapshot, expectedPlayers: 0);
        Assert.That(dropped.Errors, Does.Contain("networkBudget.pendingHitCapacityDrops expected=0 actual=1"));
    }

    [Test]
    public void ManualSkillCommandAwaitsCapacityAdmissionBeforeRecordingExecution()
    {
        Assert.That(typeof(IAsyncCommand).IsAssignableFrom(typeof(ActivateSkillCommand)), Is.True);

        MethodInfo executeAsync = typeof(ActivateSkillCommand).GetMethod(
            nameof(IAsyncCommand.ExecuteAsync),
            InstanceMembers);
        Assert.That(executeAsync, Is.Not.Null);
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(
            typeof(ActivateSkillCommand),
            typeof(Unit),
            nameof(Unit.ActivateSkillAsync)), Is.True);
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(
            typeof(ActivateSkillCommand),
            typeof(Unit),
            nameof(Unit.ActivateSkill)), Is.False,
            "manual commands must not fire-and-forget the legacy automatic-skill wrapper");
        Assert.That(MdfCompiledCodePolicy.ContainsStringLiteral(
            typeof(ActivateSkillCommand),
            "skill_capacity_backpressure"), Is.True);

        SkillActivationResult backpressure = SkillActivationResult.CapacityBackpressure();
        Assert.That(backpressure.Executed, Is.False);
        Assert.That(backpressure.CapacityBackpressured, Is.True);
        Assert.That(backpressure.ErrorCode, Is.EqualTo("skill_capacity_backpressure"));

        SkillActivationResult completed = SkillActivationResult.Completed();
        Assert.That(completed.Executed, Is.True);
        Assert.That(completed.CapacityBackpressured, Is.False);
        Assert.That(completed.ErrorCode, Is.Empty);
    }

    private static MethodInfo RequireMethod(string name)
    {
        MethodInfo method = typeof(CombatScheduler).GetMethod(name, InstanceMembers);
        Assert.That(method, Is.Not.Null, name);
        return method;
    }

    private static MethodInfo RequireMethod(Type type, string name)
    {
        MethodInfo method = type.GetMethod(name, InstanceMembers);
        Assert.That(method, Is.Not.Null, $"{type.Name}.{name}");
        return method;
    }

    private static MethodInfo RequireMethod(Type type, string name, int parameterCount)
    {
        MethodInfo[] methods = type.GetMethods(InstanceMembers);
        for (int i = 0; i < methods.Length; i++)
        {
            if (methods[i].Name == name && methods[i].GetParameters().Length == parameterCount)
            {
                return methods[i];
            }
        }

        Assert.Fail($"{type.Name}.{name}/{parameterCount}");
        return null;
    }

    private static bool HasMethod(Type type, string name)
    {
        MethodInfo[] methods = type.GetMethods(InstanceMembers);
        for (int i = 0; i < methods.Length; i++)
        {
            if (methods[i].Name == name)
            {
                return true;
            }
        }

        return false;
    }

    private static void AssertSnapshotTickRoundTrip(
        CombatScheduler scheduler,
        string snapshotTypeName,
        string readerName,
        string tickFieldName,
        int tick)
    {
        Type snapshotType = typeof(CombatScheduler).GetNestedType(snapshotTypeName, BindingFlags.NonPublic);
        MethodInfo reader = RequireMethod(readerName);
        Assert.That(snapshotType, Is.Not.Null, snapshotTypeName);
        Assert.That(reader.GetParameters().Length, Is.EqualTo(1), "reader must not accept migration-now tick");

        object snapshot = Activator.CreateInstance(snapshotType);
        snapshotType.GetField("Sequence", InstanceMembers)?.SetValue(snapshot, 1);
        snapshotType.GetField(tickFieldName, InstanceMembers)?.SetValue(snapshot, tick);
        object pending = reader.Invoke(scheduler, new[] { snapshot });
        FieldInfo pendingTick = pending.GetType().GetField(tickFieldName, InstanceMembers);
        Assert.That(pendingTick, Is.Not.Null);
        Assert.That(pendingTick.GetValue(pending), Is.EqualTo(tick));
    }

    private static void AssertNoStackField(string nestedTypeName, BindingFlags visibility = BindingFlags.NonPublic)
    {
        Type nested = typeof(CombatScheduler).GetNestedType(nestedTypeName, visibility);
        Assert.That(nested, Is.Not.Null, nestedTypeName);
        bool hasStackMember = nested.GetField("StackCount", InstanceMembers) != null ||
                              nested.GetProperty("StackCount", InstanceMembers) != null;
        Assert.That(hasStackMember, Is.False, $"{nestedTypeName} must preserve source identity instead of cross-source stack coalescing");
    }
}
