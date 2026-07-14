#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Fusion;
using NUnit.Framework;
using UnityEngine;
using Assert = NUnit.Framework.Assert;

public sealed class CombatSchedulerMigrationRemapEditModeTests
{
    private const BindingFlags InstanceMembers =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags StaticMembers =
        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    [Test]
    public void UnitNetworkIdRemapIsVisibleOnlyForCommittedCurrentSuccessfulGeneration()
    {
        var fieldObject = new GameObject("field-unit-migration-remap-test");
        try
        {
            var fieldManager = fieldObject.AddComponent<FieldManager>();
            IDictionary<uint, NetworkId> remap = GetField<IDictionary<uint, NetworkId>>(
                fieldManager,
                "_hostMigrationUnitNetworkIdRemap");
            remap.Add(101u, NetworkIdFromRaw(501u));

            SetField(fieldManager, "_hostMigrationUnitRestoreGeneration", 7);
            SetField(fieldManager, "_hostMigrationUnitNetworkIdRemapGeneration", 7);
            SetField(fieldManager, "_hostMigrationUnitNetworkIdRemapCommitted", true);
            SetField(fieldManager, "_hostMigrationUnitRestoreRunning", false);
            SetField(fieldManager, "_hostMigrationUnitRestoreSucceeded", true);

            Assert.That(
                fieldManager.TryResolveHostMigrationUnitNetworkId(101u, out NetworkId resolved),
                Is.True);
            Assert.That(resolved.Raw, Is.EqualTo(501u));
            Assert.That(fieldManager.HostMigrationUnitNetworkIdRemapGeneration, Is.EqualTo(7));
            Assert.That(
                fieldManager.TryResolveHostMigrationUnitNetworkId(NetworkIdFromRaw(101u), out resolved),
                Is.True);
            Assert.That(resolved.Raw, Is.EqualTo(501u));

            SetField(fieldManager, "_hostMigrationUnitRestoreRunning", true);
            Assert.That(fieldManager.TryResolveHostMigrationUnitNetworkId(101u, out _), Is.False);

            SetField(fieldManager, "_hostMigrationUnitRestoreRunning", false);
            SetField(fieldManager, "_hostMigrationUnitRestoreGeneration", 8);
            Assert.That(fieldManager.TryResolveHostMigrationUnitNetworkId(101u, out _), Is.False);

            InvokeInstance(fieldManager, "BeginHostMigrationUnitNetworkIdRemapGeneration", 8);
            Assert.That(remap, Is.Empty, "a new reconcile generation must discard the old table");
            Assert.That(fieldManager.HostMigrationUnitNetworkIdRemapGeneration, Is.EqualTo(-1));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(fieldObject);
        }
    }

    [Test]
    public void RestoreApisExposeExactReportsAndLegacyWrappersUseThem()
    {
        MethodInfo statusReport = RequireInstanceMethod(
            nameof(CombatScheduler.RestoreStatusEffectsFromMigrationWithReport));
        MethodInfo statBuffReport = RequireInstanceMethod(
            nameof(CombatScheduler.RestoreStatBuffsFromMigrationWithReport));
        MethodInfo pendingReport = RequireInstanceMethod(
            nameof(CombatScheduler.RestorePendingCombatFromMigrationWithReport));
        MethodInfo zoneReport = RequireInstanceMethod(
            nameof(CombatScheduler.RestoreZonesFromMigrationWithReport));

        Assert.That(statusReport.ReturnType, Is.EqualTo(typeof(MigrationRestoreReport)));
        Assert.That(statBuffReport.ReturnType, Is.EqualTo(typeof(MigrationRestoreReport)));
        Assert.That(pendingReport.ReturnType, Is.EqualTo(typeof(MigrationRestoreReport)));
        Assert.That(zoneReport.ReturnType, Is.EqualTo(typeof(MigrationRestoreReport)));
        Assert.That(statusReport.GetParameters().Last().ParameterType,
            Is.EqualTo(typeof(CombatScheduler.MigrationNetworkIdResolver)));
        Assert.That(statBuffReport.GetParameters().Last().ParameterType,
            Is.EqualTo(typeof(CombatScheduler.MigrationNetworkIdResolver)));
        Assert.That(pendingReport.GetParameters().Last().ParameterType,
            Is.EqualTo(typeof(CombatScheduler.MigrationNetworkIdResolver)));
        Assert.That(zoneReport.GetParameters().Last().ParameterType,
            Is.EqualTo(typeof(CombatScheduler.MigrationNetworkIdResolver)));
        Assert.That(
            MdfCompiledCodePolicy.ReferencesMethod(
                zoneReport,
                typeof(CombatScheduler),
                "ResolveMigrationNetworkId"),
            Is.True,
            "a recreated unit used as a zone caster must be rebound before payload resolution");

        Assert.That(
            MdfCompiledCodePolicy.ReferencesMethod(
                RequireInstanceMethod(nameof(CombatScheduler.RestoreStatusEffectsFromMigration)),
                typeof(CombatScheduler),
                nameof(CombatScheduler.RestoreStatusEffectsFromMigrationWithReport)),
            Is.True);
        Assert.That(
            MdfCompiledCodePolicy.ReferencesMethod(
                RequireInstanceMethod(nameof(CombatScheduler.RestoreStatBuffsFromMigration)),
                typeof(CombatScheduler),
                nameof(CombatScheduler.RestoreStatBuffsFromMigrationWithReport)),
            Is.True);
        Assert.That(
            MdfCompiledCodePolicy.ReferencesMethod(
                RequireInstanceMethod(nameof(CombatScheduler.RestorePendingCombatFromMigration)),
                typeof(CombatScheduler),
                nameof(CombatScheduler.RestorePendingCombatFromMigrationWithReport)),
            Is.True);
        Assert.That(
            MdfCompiledCodePolicy.ReferencesMethod(
                RequireInstanceMethod(nameof(CombatScheduler.RestoreZonesFromMigration)),
                typeof(CombatScheduler),
                nameof(CombatScheduler.RestoreZonesFromMigrationWithReport)),
            Is.True);
    }

    [Test]
    public void FieldReconcilePublishesRemapsForExactAndAsyncRecreatedPaths()
    {
        MethodInfo restore = typeof(FieldManager).GetMethod(
            nameof(FieldManager.RestoreFieldUnitsAfterHostMigration),
            InstanceMembers,
            null,
            new[] { typeof(FieldUnitMigrationSnapshot[]), typeof(string) },
            null);
        Assert.That(restore, Is.Not.Null);
        Assert.That(
            MdfCompiledCodePolicy.ReferencesMethod(
                restore,
                typeof(FieldManager),
                "BeginHostMigrationUnitNetworkIdRemapGeneration"),
            Is.True);
        Assert.That(
            MdfCompiledCodePolicy.ReferencesMethod(
                restore,
                typeof(FieldManager),
                "TryStageHostMigrationUnitNetworkIdRemap"),
            Is.True);
        Assert.That(
            MdfCompiledCodePolicy.ReferencesMethod(
                restore,
                typeof(FieldManager),
                "CommitHostMigrationUnitNetworkIdRemap"),
            Is.True);

        MethodInfo reconcile = typeof(FieldManager).GetMethod(
            "RestoreFieldUnitsAfterHostMigrationAsync",
            InstanceMembers);
        AsyncStateMachineAttribute stateMachine =
            reconcile?.GetCustomAttribute<AsyncStateMachineAttribute>();
        MethodInfo moveNext = stateMachine?.StateMachineType.GetMethod("MoveNext", InstanceMembers);
        Assert.That(moveNext, Is.Not.Null);
        Assert.That(
            MdfCompiledCodePolicy.ReferencesMethod(
                moveNext,
                typeof(FieldManager),
                "TryStageHostMigrationUnitNetworkIdRemap"),
            Is.True,
            "cell-preserved and recreated units must both stage old-to-actual identities");
        Assert.That(
            MdfCompiledCodePolicy.ReferencesMethod(
                moveNext,
                typeof(FieldManager),
                "CommitHostMigrationUnitNetworkIdRemap"),
            Is.True);
        Assert.That(
            MdfCompiledCodePolicy.ReferencesMethod(
                moveNext,
                typeof(FieldManager),
                "InvalidateHostMigrationUnitNetworkIdRemap"),
            Is.True,
            "a failed generation must never leave a partially published table");
    }

    [Test]
    public void RestoreReportsAccountForMissingInputAndUnavailableScheduler()
    {
        var schedulerObject = new GameObject("combat-scheduler-migration-report-test");
        try
        {
            var scheduler = schedulerObject.AddComponent<CombatScheduler>();
            MigrationRestoreReport missingStatus =
                scheduler.RestoreStatusEffectsFromMigrationWithReport(null);
            MigrationRestoreReport missingStatBuff =
                scheduler.RestoreStatBuffsFromMigrationWithReport(null);
            MigrationRestoreReport missingZone =
                scheduler.RestoreZonesFromMigrationWithReport(null);

            AssertFailedTerminalReport(missingStatus, 1);
            AssertFailedTerminalReport(missingStatBuff, 1);
            AssertFailedTerminalReport(missingZone, 1);

            var pendingFires = new[]
            {
                new CombatScheduler.PendingFireMigrationSnapshot
                {
                    Sequence = 1,
                    RemainingTicks = 2,
                    TargetId = NetworkIdFromRaw(10u)
                }
            };
            MigrationRestoreReport pendingUnavailable =
                scheduler.RestorePendingCombatFromMigrationWithReport(
                    pendingFires,
                    Array.Empty<CombatScheduler.PendingHitMigrationSnapshot>(),
                    "editmode-no-runner");
            AssertFailedTerminalReport(pendingUnavailable, 1);

            MigrationRestoreReport pendingEmpty =
                scheduler.RestorePendingCombatFromMigrationWithReport(null, null, "editmode-empty");
            Assert.That(pendingEmpty.Captured, Is.Zero);
            Assert.That(pendingEmpty.Succeeded, Is.True);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(schedulerObject);
        }
    }

    [Test]
    public void MigrationResolverRemapsKnownIdsAndPreservesUnknownIds()
    {
        MethodInfo resolve = typeof(CombatScheduler).GetMethod(
            "ResolveMigrationNetworkId",
            StaticMembers);
        Assert.That(resolve, Is.Not.Null);

        var expected = NetworkIdFromRaw(401u);
        CombatScheduler.MigrationNetworkIdResolver knownResolver =
            (NetworkId captured, out NetworkId actual) =>
            {
                actual = expected;
                return captured.Raw == 101u;
            };
        var remapped = (NetworkId)resolve.Invoke(
            null,
            new object[] { NetworkIdFromRaw(101u), knownResolver });
        Assert.That(remapped.Raw, Is.EqualTo(401u));

        CombatScheduler.MigrationNetworkIdResolver unknownResolver =
            (NetworkId captured, out NetworkId actual) =>
            {
                actual = default;
                return false;
            };
        var preserved = (NetworkId)resolve.Invoke(
            null,
            new object[] { NetworkIdFromRaw(102u), unknownResolver });
        Assert.That(preserved.Raw, Is.EqualTo(102u));
    }

    [Test]
    public void PendingFallbackUsesRemainingTickThenOriginalSequenceAndRetainsSplashHits()
    {
        MethodInfo compareFire = typeof(CombatScheduler).GetMethod(
            "ComparePendingFireMigrationSnapshots",
            StaticMembers);
        MethodInfo compareHit = typeof(CombatScheduler).GetMethod(
            "ComparePendingHitMigrationSnapshots",
            StaticMembers);
        MethodInfo retainSplash = typeof(CombatScheduler).GetMethod(
            "CanRestorePendingHitWithoutPrimaryTarget",
            StaticMembers);
        Assert.That(compareFire, Is.Not.Null);
        Assert.That(compareHit, Is.Not.Null);
        Assert.That(retainSplash, Is.Not.Null);

        var earlierFireTick = new CombatScheduler.PendingFireMigrationSnapshot
        {
            RemainingTicks = 2,
            Sequence = 90
        };
        var laterFireTick = new CombatScheduler.PendingFireMigrationSnapshot
        {
            RemainingTicks = 3,
            Sequence = 1
        };
        var lowerFireSequence = new CombatScheduler.PendingFireMigrationSnapshot
        {
            RemainingTicks = 2,
            Sequence = 10
        };
        Assert.That(InvokeComparison(compareFire, earlierFireTick, laterFireTick), Is.LessThan(0));
        Assert.That(InvokeComparison(compareFire, lowerFireSequence, earlierFireTick), Is.LessThan(0));

        var earlierHitTick = new CombatScheduler.PendingHitMigrationSnapshot
        {
            RemainingTicks = 4,
            Sequence = 90
        };
        var laterHitTick = new CombatScheduler.PendingHitMigrationSnapshot
        {
            RemainingTicks = 5,
            Sequence = 1
        };
        var lowerHitSequence = new CombatScheduler.PendingHitMigrationSnapshot
        {
            RemainingTicks = 4,
            Sequence = 10
        };
        Assert.That(InvokeComparison(compareHit, earlierHitTick, laterHitTick), Is.LessThan(0));
        Assert.That(InvokeComparison(compareHit, lowerHitSequence, earlierHitTick), Is.LessThan(0));

        Assert.That((bool)retainSplash.Invoke(null, new object[] { 0.01f }), Is.True);
        Assert.That((bool)retainSplash.Invoke(null, new object[] { 0f }), Is.False);
    }

    private static NetworkId NetworkIdFromRaw(uint raw)
    {
        return new NetworkId { Raw = raw };
    }

    private static MethodInfo RequireInstanceMethod(string name)
    {
        MethodInfo method = typeof(CombatScheduler).GetMethods(InstanceMembers)
            .SingleOrDefault(candidate => candidate.Name == name);
        Assert.That(method, Is.Not.Null, $"CombatScheduler.{name}");
        return method;
    }

    private static int InvokeComparison<T>(MethodInfo comparison, T left, T right)
    {
        return (int)comparison.Invoke(null, new object[] { left, right });
    }

    private static void AssertFailedTerminalReport(MigrationRestoreReport report, int captured)
    {
        Assert.That(report.Captured, Is.EqualTo(captured));
        Assert.That(report.Failed, Is.EqualTo(captured));
        Assert.That(report.IsTerminal, Is.True);
        Assert.That(report.Succeeded, Is.False);
    }

    private static T GetField<T>(object instance, string name)
    {
        FieldInfo field = instance.GetType().GetField(name, InstanceMembers);
        Assert.That(field, Is.Not.Null, $"{instance.GetType().Name}.{name}");
        return (T)field.GetValue(instance);
    }

    private static void SetField(object instance, string name, object value)
    {
        FieldInfo field = instance.GetType().GetField(name, InstanceMembers);
        Assert.That(field, Is.Not.Null, $"{instance.GetType().Name}.{name}");
        field.SetValue(instance, value);
    }

    private static object InvokeInstance(object instance, string name, params object[] args)
    {
        MethodInfo method = instance.GetType().GetMethod(name, InstanceMembers);
        Assert.That(method, Is.Not.Null, $"{instance.GetType().Name}.{name}");
        return method.Invoke(instance, args);
    }
}
#endif
