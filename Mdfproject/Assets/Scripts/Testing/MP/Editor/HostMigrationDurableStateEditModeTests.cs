#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public sealed class HostMigrationDurableStateEditModeTests
{
    private GameObject _survivorBossManagerObject;

    [TearDown]
    public void TearDown()
    {
        if (_survivorBossManagerObject != null)
        {
            UnityEngine.Object.DestroyImmediate(_survivorBossManagerObject);
            _survivorBossManagerObject = null;
        }
    }

    [Test]
    public void SurvivorBossNetworkPayloadRoundTripsPendingAssignmentsInvadedIdsAndNextId()
    {
        SurvivorBossManager manager = CreateSurvivorBossManager();
        var pending = new SurvivorBossData
        {
            BossDataKey = "BossPending",
            RemainingHP = 321.5f,
            MaxHP = 900f,
            OriginPlayerId = 2,
            BossUniqueId = 11
        };
        var assigned = new SurvivorBossData
        {
            BossDataKey = "BossAssigned",
            RemainingHP = 77.25f,
            MaxHP = 500f,
            OriginPlayerId = 3,
            BossUniqueId = 12
        };

        SetPrivateField(manager, "_pendingSurvivorBosses", new List<SurvivorBossData> { pending });
        SetPrivateField(manager, "_bossTargetAssignments", new Dictionary<int, List<SurvivorBossData>>
        {
            [4] = new List<SurvivorBossData> { assigned }
        });
        SetPrivateField(manager, "_bossesInvadedThisTurn", new HashSet<int> { assigned.BossUniqueId });
        SetPrivateField(manager, "_nextBossUniqueId", 77);

        manager.CaptureStableSnapshot(
            out string expectedPendingSnapshot,
            out string expectedAssignmentSnapshot,
            out int expectedPendingCount,
            out int expectedAssignmentCount);
        manager.CaptureNetworkSyncPayload(
            out string[] pendingParts,
            out int[] assignmentTargets,
            out string[] assignmentParts,
            out int[] invadedBossIds,
            out int nextBossUniqueId);

        Assert.That(pendingParts, Has.Length.EqualTo(1));
        Assert.That(assignmentTargets, Is.EqualTo(new[] { 4 }));
        Assert.That(assignmentParts, Has.Length.EqualTo(1));
        Assert.That(invadedBossIds, Is.EqualTo(new[] { 12 }));
        Assert.That(nextBossUniqueId, Is.EqualTo(77));

        manager.ApplyNetworkSyncFromAuthority(
            pendingParts,
            assignmentTargets,
            assignmentParts,
            invadedBossIds,
            nextBossUniqueId);

        manager.CaptureStableSnapshot(
            out string actualPendingSnapshot,
            out string actualAssignmentSnapshot,
            out int actualPendingCount,
            out int actualAssignmentCount);

        Assert.That(actualPendingCount, Is.EqualTo(expectedPendingCount));
        Assert.That(actualAssignmentCount, Is.EqualTo(expectedAssignmentCount));
        Assert.That(actualPendingSnapshot, Is.EqualTo(expectedPendingSnapshot));
        Assert.That(actualAssignmentSnapshot, Is.EqualTo(expectedAssignmentSnapshot));
        Assert.That(manager.HasBossInvadedThisTurn(assigned.BossUniqueId), Is.True);

        manager.CaptureNetworkSyncPayload(
            out string[] restoredPendingParts,
            out int[] restoredAssignmentTargets,
            out string[] restoredAssignmentParts,
            out int[] restoredInvadedBossIds,
            out int restoredNextBossUniqueId);
        Assert.That(restoredPendingParts, Is.EqualTo(pendingParts));
        Assert.That(restoredAssignmentTargets, Is.EqualTo(assignmentTargets));
        Assert.That(restoredAssignmentParts, Is.EqualTo(assignmentParts));
        Assert.That(restoredInvadedBossIds, Is.EqualTo(invadedBossIds));
        Assert.That(restoredNextBossUniqueId, Is.EqualTo(nextBossUniqueId));

        Assert.That(manager.GetNextBossUniqueId(), Is.EqualTo(77));

        manager.CaptureNetworkSyncPayload(
            out _, out _, out _, out _, out int incrementedNextBossUniqueId);
        Assert.That(incrementedNextBossUniqueId, Is.EqualTo(78));
    }

    [Test]
    public void DurableConnectionTokenHashIsDeterministicOneWayAndNeverReturnsRawToken()
    {
        const string token = "abc";
        const string expectedSha256 = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";

        string first = DurableConnectionTokenIdentity.BuildHash(token);
        string second = DurableConnectionTokenIdentity.BuildHash(token);

        Assert.That(first, Is.EqualTo(expectedSha256));
        Assert.That(second, Is.EqualTo(first));
        Assert.That(first, Has.Length.EqualTo(64));
        Assert.That(first, Does.Not.Contain(token));
        Assert.That(DurableConnectionTokenIdentity.BuildHash(string.Empty), Is.Empty);
        Assert.That(DurableConnectionTokenIdentity.BuildHash(null), Is.Empty);
    }

    [Test]
    public void HostMigrationSuccessWaitsForFlowResumedAndRejectsFailedRecovery()
    {
        string handler = ReadAssetSource("Scripts/Network/HostMigrationHandler.cs");
        int terminalGate = handler.IndexOf(
            "private IEnumerator WaitForGameManagersRecoveryTerminal(",
            StringComparison.Ordinal);
        int failedCheck = handler.IndexOf("gm.HasHostMigrationRecoveryFailed", terminalGate, StringComparison.Ordinal);
        int resumedCheck = handler.IndexOf("gm.IsHostMigrationFlowResumed", terminalGate, StringComparison.Ordinal);
        int successCommit = handler.IndexOf(
            "_migrationRecoverySucceeded = survivorBossRestored && durablePlayersRestored && _aiTakeoverReady;",
            terminalGate,
            StringComparison.Ordinal);

        Assert.That(terminalGate, Is.GreaterThanOrEqualTo(0));
        Assert.That(failedCheck, Is.GreaterThan(terminalGate));
        Assert.That(resumedCheck, Is.GreaterThan(failedCheck));
        Assert.That(successCommit, Is.GreaterThan(resumedCheck));
        Assert.That(handler, Does.Contain("yield return WaitForGameManagersRecoveryTerminal("));
        Assert.That(handler, Does.Contain("AreDurableFieldUnitRestoresTerminal"));
        Assert.That(handler, Does.Contain("? _cachedDurablePlayersById.Count"));

        string gameManagers = ReadAssetSource("Scripts/Managers/GameManagers.cs");
        Assert.That(gameManagers, Does.Contain("MigrationRestoreStage.FlowResumed"));
        Assert.That(gameManagers, Does.Contain("MigrationRestoreStage.Failed"));
        Assert.That(gameManagers, Does.Contain("public bool IsHostMigrationRecoveryTerminal"));
    }

    [Test]
    public void DurableGameplayPayloadsAreContinuouslyCarriedByFusionState()
    {
        string player = ReadAssetSource("Scripts/Managers/PlayerManager.MigrationState.cs");
        string playerCore = ReadAssetSource("Scripts/Managers/PlayerManager.cs");
        string field = ReadAssetSource("Scripts/Managers/FieldManager.cs");
        string wall = ReadAssetSource("Scripts/Game/Game Rules/DestructibleWall.cs");
        string survivor = ReadAssetSource("Scripts/Managers/GameManagers.SurvivorBossSnapshot.cs");

        Assert.That(player, Does.Contain("NetworkArray<AugmentRuntimeMigrationRow>"));
        Assert.That(player, Does.Contain("NetworkArray<WallHealthMigrationRow>"));
        Assert.That(player, Does.Contain("WallHealthMigrationRevision"));
        Assert.That(playerCore, Does.Contain("PublishAugmentRuntimeMigrationStateFromAuthority(\"register_active_summon\")"));
        Assert.That(playerCore, Does.Contain("PublishAugmentRuntimeMigrationStateFromAuthority(\"add_owned_boss\")"));
        Assert.That(playerCore, Does.Contain("PublishAugmentRuntimeMigrationStateFromAuthority(\"consume_owned_boss\")"));
        Assert.That(field, Does.Contain("PublishDestructibleWallHealthMigrationState(\"create_wall\")"));
        Assert.That(field, Does.Contain("PublishDestructibleWallHealthMigrationState(\"remove_wall\")"));
        Assert.That(wall, Does.Contain("PublishDestructibleWallHealthDelta(this, \"wall_damage\")"));
        Assert.That(wall, Does.Contain("PublishDestructibleWallHealthDelta(this, \"wall_heal\")"));
        Assert.That(survivor, Does.Contain("NetworkArray<SurvivorBossReplicatedRow>"));
        Assert.That(survivor, Does.Contain("CaptureSurvivorBossPayloadForMigration"));
    }

    [Test]
    public void TimedOutFailedAndLateMigrationRunnersAreInvalidatedAndCleanedUp()
    {
        string handler = ReadAssetSource("Scripts/Network/HostMigrationHandler.cs");
        int timeoutBranch = handler.IndexOf("if (!startTask.IsCompleted)", StringComparison.Ordinal);
        int generationInvalidation = handler.IndexOf("_migrationAttemptGeneration++;", timeoutBranch, StringComparison.Ordinal);
        int lateCompletion = handler.IndexOf(
            "attemptGeneration != _migrationAttemptGeneration || !_isMigrating",
            StringComparison.Ordinal);
        int lateCleanup = handler.IndexOf(
            "CleanupCreatedMigrationRunnerAsync(newRunner, newRunnerGO, \"late_completion\")",
            lateCompletion,
            StringComparison.Ordinal);

        Assert.That(timeoutBranch, Is.GreaterThanOrEqualTo(0));
        Assert.That(generationInvalidation, Is.GreaterThan(timeoutBranch));
        Assert.That(lateCompletion, Is.GreaterThan(generationInvalidation));
        Assert.That(lateCleanup, Is.GreaterThan(lateCompletion));
        Assert.That(handler, Does.Contain(
            "CleanupCreatedMigrationRunnerAsync(newRunner, newRunnerGO, \"start_game_failed\")"));
        Assert.That(handler, Does.Contain(
            "CleanupCreatedMigrationRunnerAsync(newRunner, newRunnerGO, \"start_game_exception\")"));
        Assert.That(handler, Does.Contain("runner.RemoveCallbacks(NetworkManager.Instance);"));
        Assert.That(handler, Does.Contain("UnityEngine.Object.Destroy(runnerGameObject);"));
    }

    [Test]
    public void NearExpiryMigrationTimerGetsPositiveGraceInsteadOfBecomingNone()
    {
        string recovery = ReadAssetSource("Scripts/Managers/GameManagers.MigrationRecovery.cs");
        int boundaryCondition = recovery.IndexOf("bool shouldRestoreExpiredBoundaryTimer", StringComparison.Ordinal);
        int timerWasNotRunning = recovery.IndexOf("!phaseTimer.IsRunning", boundaryCondition, StringComparison.Ordinal);
        int cachedTimerWasPositive = recovery.IndexOf(
            "cachedData.RemainingPhaseTime > 0f",
            boundaryCondition,
            StringComparison.Ordinal);
        int grace = recovery.IndexOf(
            "Mathf.Max(0.25f, adjustedCachedRemaining)",
            cachedTimerWasPositive,
            StringComparison.Ordinal);
        int restoredTimer = recovery.IndexOf(
            "TickTimer.CreateFromSeconds(Runner, restoredRemaining)",
            grace,
            StringComparison.Ordinal);

        Assert.That(boundaryCondition, Is.GreaterThanOrEqualTo(0));
        Assert.That(timerWasNotRunning, Is.GreaterThan(boundaryCondition));
        Assert.That(cachedTimerWasPositive, Is.GreaterThan(timerWasNotRunning));
        Assert.That(grace, Is.GreaterThan(cachedTimerWasPositive));
        Assert.That(restoredTimer, Is.GreaterThan(grace));
    }

    private SurvivorBossManager CreateSurvivorBossManager()
    {
        _survivorBossManagerObject = new GameObject("SurvivorBossManager_EditModeTest");
        return _survivorBossManagerObject.AddComponent<SurvivorBossManager>();
    }

    private static void SetPrivateField<T>(object target, string fieldName, T value)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, $"Missing private field: {fieldName}");
        field.SetValue(target, value);
    }

    private static string ReadAssetSource(string assetRelativePath)
    {
        string path = Path.Combine(Application.dataPath, assetRelativePath.Replace('/', Path.DirectorySeparatorChar));
        return File.ReadAllText(path);
    }
}
#endif
