#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using NetworkRunner = Fusion.NetworkRunner;

public sealed class HostMigrationRestoreReportEditModeTests
{
    [Test]
    public void EmptySnapshot_IsExplicitTerminalSuccess()
    {
        MigrationRestoreReport report = MigrationRestoreReport.Empty("empty");

        Assert.That(report.Captured, Is.Zero);
        Assert.That(report.IsTerminal, Is.True);
        Assert.That(report.Succeeded, Is.True);
        Assert.That(report.Missing, Is.Zero);
    }

    [Test]
    public void CombinedReport_PropagatesLateFailureToTerminalGate()
    {
        var attackPool = new MigrationRestoreReport("attack_pool", 3, 3, 0, 0);
        var statuses = new MigrationRestoreReport("statuses", 2, 1, 0, 1, "target_missing");

        MigrationRestoreReport combined = attackPool.Combine(statuses, "host_migration");

        Assert.That(combined.Captured, Is.EqualTo(5));
        Assert.That(combined.Restored, Is.EqualTo(4));
        Assert.That(combined.Failed, Is.EqualTo(1));
        Assert.That(combined.IsTerminal, Is.True);
        Assert.That(combined.Succeeded, Is.False);
        Assert.That(combined.FailureReason, Does.Contain("target_missing"));
    }

    [Test]
    public void SkippedExpiredEffects_AreTerminalWithoutBeingFailures()
    {
        var report = new MigrationRestoreReport("statuses", 4, 2, 2, 0);

        Assert.That(report.Accounted, Is.EqualTo(4));
        Assert.That(report.IsTerminal, Is.True);
        Assert.That(report.Succeeded, Is.True);
    }

    [Test]
    public void ExpiredZoneWithBackpressuredTick_IsNotSkippedDuringMigrationRestore()
    {
        var snapshot = new CombatScheduler.ZoneMigrationSnapshot
        {
            ExpireTick = 100,
            NextTick = 90
        };

        Assert.That(
            CombatScheduler.IsZoneMigrationSnapshotTerminal(snapshot, nowTick: 120),
            Is.False,
            "An overdue tick scheduled before expiry must survive migration restore.");
    }

    [Test]
    public void ExpiredZoneWithoutPendingTick_IsSkippedDuringMigrationRestore()
    {
        var snapshot = new CombatScheduler.ZoneMigrationSnapshot
        {
            ExpireTick = 100,
            NextTick = 100
        };

        Assert.That(
            CombatScheduler.IsZoneMigrationSnapshotTerminal(snapshot, nowTick: 120),
            Is.True);
    }

    [Test]
    public void OverAccountedRestore_IsNotAcceptedAsTerminalSuccess()
    {
        var report = new MigrationRestoreReport("duplicate_restore", 1, 2, 0, 0);

        Assert.That(report.Accounted, Is.EqualTo(2));
        Assert.That(report.IsTerminal, Is.False);
        Assert.That(report.Succeeded, Is.False);
    }

    [Test]
    public void FieldUnitPayload_CarriesDurableCombatState()
    {
        FieldInfo[] fields = typeof(FieldUnitMigrationSnapshot).GetFields(BindingFlags.Instance | BindingFlags.Public);
        string[] names = Array.ConvertAll(fields, field => field.Name);

        CollectionAssert.IsSupersetOf(names, new[]
        {
            nameof(FieldUnitMigrationSnapshot.NetworkIdRaw),
            nameof(FieldUnitMigrationSnapshot.UnitDataKey),
            nameof(FieldUnitMigrationSnapshot.Position),
            nameof(FieldUnitMigrationSnapshot.CurrentHealth),
            nameof(FieldUnitMigrationSnapshot.MaxHealth),
            nameof(FieldUnitMigrationSnapshot.CurrentMana),
            nameof(FieldUnitMigrationSnapshot.MaxMana),
            nameof(FieldUnitMigrationSnapshot.ActivationMode),
            nameof(FieldUnitMigrationSnapshot.AttackCooldownRemaining),
            nameof(FieldUnitMigrationSnapshot.SkillCastLockRemaining),
            nameof(FieldUnitMigrationSnapshot.IsDead)
        });
    }

    [Test]
    public void AttackPoolMigrationRestore_ReturnsAwaitableReport()
    {
        MethodInfo method = typeof(PlayerManager).GetMethod(
            "RestoreAttackMonsterPoolFromMigrationSnapshotAsync",
            BindingFlags.Instance | BindingFlags.Public);

        Assert.That(method, Is.Not.Null);
        Assert.That(method.ReturnType, Is.EqualTo(typeof(UniTask<MigrationRestoreReport>)));
        ParameterInfo[] parameters = method.GetParameters();
        Assert.That(parameters[^2].ParameterType, Is.EqualTo(typeof(NetworkRunner)));
        Assert.That(parameters[^1].ParameterType, Is.EqualTo(typeof(CancellationToken)));
    }

    [Test]
    public void Handler_ExposesPreResumeRestoreGate()
    {
        MethodInfo method = typeof(HostMigrationHandler).GetMethod(
            "IsMigrationRestoreReadyForFlow",
            BindingFlags.Instance | BindingFlags.Public);

        Assert.That(method, Is.Not.Null);
        Assert.That(method.ReturnType, Is.EqualTo(typeof(bool)));
    }

    [Test]
    public void FieldUnitRestore_RejectsOutOfGridSnapshotWithoutShrinkingCapturedCount()
    {
        var gameObject = new GameObject("field-unit-invalid-snapshot-test");
        try
        {
            FieldManager field = gameObject.AddComponent<FieldManager>();
            field.gridSize = new Vector2Int(2, 2);
            var snapshots = new[]
            {
                new FieldUnitMigrationSnapshot { UnitDataKey = "valid", Position = new Vector3Int(0, 0, 0) },
                new FieldUnitMigrationSnapshot { UnitDataKey = "invalid", Position = new Vector3Int(8, 8, 0) }
            };

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("unit_position_out_of_grid"));
            Assert.That(field.RestoreFieldUnitsAfterHostMigration(snapshots, "invalid-position-test"), Is.False);

            MigrationRestoreReport report = field.HostMigrationUnitRestoreReport;
            Assert.That(report.Captured, Is.EqualTo(snapshots.Length));
            Assert.That(report.Failed, Is.EqualTo(snapshots.Length));
            Assert.That(report.Succeeded, Is.False);
            Assert.That(report.FailureReason, Does.Contain("unit_position_out_of_grid"));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(gameObject);
        }
    }

    [Test]
    public void FieldUnitRestore_RejectsDuplicateCellsBeforeMutation()
    {
        var gameObject = new GameObject("field-unit-duplicate-snapshot-test");
        try
        {
            FieldManager field = gameObject.AddComponent<FieldManager>();
            field.gridSize = new Vector2Int(2, 2);
            var snapshots = new[]
            {
                new FieldUnitMigrationSnapshot { UnitDataKey = "first", Position = Vector3Int.zero },
                new FieldUnitMigrationSnapshot { UnitDataKey = "second", Position = Vector3Int.zero }
            };

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("unit_position_duplicate"));
            Assert.That(field.RestoreFieldUnitsAfterHostMigration(snapshots, "duplicate-position-test"), Is.False);
            Assert.That(field.HostMigrationUnitRestoreReport.Captured, Is.EqualTo(snapshots.Length));
            Assert.That(field.HostMigrationUnitRestoreReport.Succeeded, Is.False);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(gameObject);
        }
    }

    [Test]
    public void AttackPoolMigrationSnapshot_RejectsMismatchedArrayShapes()
    {
        MethodInfo method = typeof(PlayerManager).GetMethod(
            "TryValidateAttackMonsterPoolMigrationSnapshot",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null);

        MonsterData data = ScriptableObject.CreateInstance<MonsterData>();
        data.name = "MigrationShapeMonster";
        try
        {
            object[] args =
            {
                new[] { data },
                new[] { data.name },
                Array.Empty<int>(),
                new[] { 1 },
                new[] { 0 },
                new[] { -1 },
                new[] { -1 },
                new[] { -1 },
                0,
                string.Empty
            };

            bool valid = (bool)method.Invoke(null, args);

            Assert.That(valid, Is.False);
            Assert.That((int)args[8], Is.EqualTo(1));
            Assert.That((string)args[9], Does.Contain("shape_mismatch"));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(data);
        }
    }

    [Test]
    public void AttackPoolMigrationSnapshot_AcceptsExplicitEmptyPayload()
    {
        MethodInfo method = typeof(PlayerManager).GetMethod(
            "TryValidateAttackMonsterPoolMigrationSnapshot",
            BindingFlags.Static | BindingFlags.NonPublic);
        object[] args =
        {
            Array.Empty<MonsterData>(),
            Array.Empty<string>(),
            Array.Empty<int>(),
            Array.Empty<int>(),
            Array.Empty<int>(),
            Array.Empty<int>(),
            Array.Empty<int>(),
            Array.Empty<int>(),
            -1,
            "unset"
        };

        bool valid = (bool)method.Invoke(null, args);

        Assert.That(valid, Is.True);
        Assert.That((int)args[8], Is.Zero);
        Assert.That((string)args[9], Is.Empty);
    }

    [Test]
    public void OwnedScrollMigrationRestore_ReturnsAwaitableReportWithLifecycleGuard()
    {
        MethodInfo method = typeof(PlayerManager).GetMethod(
            "RestoreOwnedMagicScrollsFromMigrationSnapshotAsync",
            BindingFlags.Instance | BindingFlags.Public);

        Assert.That(method, Is.Not.Null);
        Assert.That(method.ReturnType, Is.EqualTo(typeof(UniTask<MigrationRestoreReport>)));
        ParameterInfo[] parameters = method.GetParameters();
        Assert.That(parameters[parameters.Length - 2].ParameterType, Is.EqualTo(typeof(NetworkRunner)));
        Assert.That(parameters[parameters.Length - 1].ParameterType, Is.EqualTo(typeof(CancellationToken)));
    }

    [Test]
    public void OwnedScrollMigrationSnapshot_RejectsMismatchedArrayShapes()
    {
        MethodInfo method = typeof(PlayerManager).GetMethod(
            "TryValidateOwnedMagicScrollMigrationSnapshot",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null);

        object[] args =
        {
            3,
            Array.Empty<MagicScrollData>(),
            Array.Empty<string>(),
            new[] { "Scroll_Heal" },
            -1,
            string.Empty
        };

        bool valid = (bool)method.Invoke(null, args);

        Assert.That(valid, Is.False);
        Assert.That((string)args[5], Does.Contain("shape_mismatch"));
    }

    [Test]
    public void OwnedScrollMigrationSnapshot_AcceptsExplicitEmptyPayload()
    {
        MethodInfo method = typeof(PlayerManager).GetMethod(
            "TryValidateOwnedMagicScrollMigrationSnapshot",
            BindingFlags.Static | BindingFlags.NonPublic);
        object[] args =
        {
            0,
            Array.Empty<MagicScrollData>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            -1,
            "unset"
        };

        bool valid = (bool)method.Invoke(null, args);

        Assert.That(valid, Is.True);
        Assert.That((int)args[4], Is.Zero);
        Assert.That((string)args[5], Is.Empty);
    }

    [Test]
    public void OwnedScrollMigrationSnapshot_ContentIdSurvivesLegacyAssetRename()
    {
        MethodInfo method = typeof(PlayerManager).GetMethod(
            "TryValidateOwnedMagicScrollMigrationSnapshot",
            BindingFlags.Static | BindingFlags.NonPublic);
        var scroll = ScriptableObject.CreateInstance<MagicScrollData>();
        try
        {
            scroll.name = "Scroll_Renamed";
            FieldInfo contentId = typeof(MagicScrollData).GetField(
                "contentId",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(contentId, Is.Not.Null);
            contentId.SetValue(scroll, "scroll.test.durable");
            object[] args =
            {
                2,
                new[] { scroll },
                new[] { "scroll.test.durable" },
                new[] { "Scroll_OldName" },
                -1,
                "unset"
            };

            Assert.That((bool)method.Invoke(null, args), Is.True,
                "a durable contentId must take precedence over a stale legacy asset name");
            Assert.That((int)args[4], Is.EqualTo(1));
            Assert.That((string)args[5], Is.Empty);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(scroll);
        }
    }

    [Test]
    public void PermanentWallMigrationSnapshot_RejectsMalformedShapeBeforeMutation()
    {
        var gameObject = new GameObject("permanent-wall-shape-test");
        try
        {
            FieldManager field = gameObject.AddComponent<FieldManager>();
            MethodInfo method = typeof(FieldManager).GetMethod(
                "TryParsePermanentWallMigrationSnapshot",
                BindingFlags.Instance | BindingFlags.NonPublic);
            object[] args =
            {
                new[] { 0 },
                Array.Empty<int>(),
                null,
                null,
                string.Empty
            };

            bool valid = (bool)method.Invoke(field, args);

            Assert.That(valid, Is.False);
            Assert.That((string)args[4], Does.Contain("shape_mismatch"));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(gameObject);
        }
    }

    [Test]
    public void PermanentWallMigrationRestore_ReturnsTerminalReport()
    {
        MethodInfo method = typeof(FieldManager).GetMethod(
            "RestorePermanentWallsAfterHostMigration",
            BindingFlags.Instance | BindingFlags.Public);

        Assert.That(method, Is.Not.Null);
        Assert.That(method.ReturnType, Is.EqualTo(typeof(MigrationRestoreReport)));
    }

    [Test]
    public void Handler_RequiresUniqueGameplayPlayerIdentityMap()
    {
        MethodInfo method = typeof(HostMigrationHandler).GetMethod(
            "TryBuildUniqueGameplayPlayerMap",
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.That(method, Is.Not.Null);
        Assert.That(method.ReturnType, Is.EqualTo(typeof(bool)));
    }

    [Test]
    public void DurablePlayerSnapshot_DistinguishesCapturedEmptyPayloadFromCaptureFailure()
    {
        Type snapshotType = typeof(HostMigrationHandler).GetNestedType(
            "DurablePlayerMigrationSnapshot",
            BindingFlags.NonPublic);

        Assert.That(snapshotType, Is.Not.Null);
        Assert.That(snapshotType.GetField("HasShopSnapshot", BindingFlags.Instance | BindingFlags.Public), Is.Not.Null);
        Assert.That(snapshotType.GetField("HasPresentedAugmentSnapshot", BindingFlags.Instance | BindingFlags.Public), Is.Not.Null);
        Assert.That(snapshotType.GetField("HasSelectedAugmentSnapshot", BindingFlags.Instance | BindingFlags.Public), Is.Not.Null);
        Assert.That(snapshotType.GetField("HasAttackPoolSnapshot", BindingFlags.Instance | BindingFlags.Public), Is.Not.Null);
        Assert.That(snapshotType.GetField("HasOwnedScrollSnapshot", BindingFlags.Instance | BindingFlags.Public), Is.Not.Null);
        Assert.That(snapshotType.GetField("OwnedScrollContentIds", BindingFlags.Instance | BindingFlags.Public), Is.Not.Null);
    }

    [Test]
    public void SurvivorBossCapture_SourceUnavailable_IsRejectedByRestoreGate()
    {
        MethodInfo method = typeof(HostMigrationHandler).GetMethod(
            "IsSurvivorBossCaptureRestorable",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null);
        var capture = new GameMigrationData
        {
            SurvivorBossCaptureAttempted = true,
            SurvivorBossCaptureSourceAvailable = false,
            SurvivorBossCaptureValid = false,
            HasSurvivorBossPayload = false,
            SurvivorBossRows = Array.Empty<SurvivorBossReplicatedRow>(),
            SurvivorBossNextUniqueId = 1
        };
        object[] args = { capture, null };

        bool restorable = (bool)method.Invoke(null, args);

        Assert.That(restorable, Is.False);
        Assert.That((string)args[1], Is.EqualTo("survivor_boss_capture_source_unavailable"));
    }

    [Test]
    public void SurvivorBossCapture_ExplicitCapturedEmpty_IsAcceptedByRestoreGate()
    {
        MethodInfo method = typeof(HostMigrationHandler).GetMethod(
            "IsSurvivorBossCaptureRestorable",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null);
        var capture = new GameMigrationData
        {
            SurvivorBossCaptureAttempted = true,
            SurvivorBossCaptureSourceAvailable = true,
            SurvivorBossCaptureValid = true,
            HasSurvivorBossPayload = true,
            SurvivorBossRows = Array.Empty<SurvivorBossReplicatedRow>(),
            SurvivorBossNextUniqueId = 1,
            SurvivorBossPayloadOverflow = false
        };
        object[] args = { capture, null };

        bool restorable = (bool)method.Invoke(null, args);

        Assert.That(restorable, Is.True);
        Assert.That((string)args[1], Is.Empty);
    }

    [Test]
    public void DurablePlayerMigrationPayload_AcceptsExplicitCapturedEmptyShop()
    {
        MethodInfo method = typeof(PlayerManager).GetMethod(
            "TryValidateDurablePlayerMigrationPayload",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null);
        object[] args =
        {
            7,
            100,
            10,
            5,
            true,
            Array.Empty<string>(),
            Array.Empty<int>(),
            Array.Empty<bool>(),
            1,
            0,
            null,
            null
        };

        bool valid = (bool)method.Invoke(null, args);

        Assert.That(valid, Is.True);
        Assert.That((string[])args[10], Is.Empty);
        Assert.That((string)args[11], Is.Empty);
    }

    [Test]
    public void DurablePlayerMigrationPayload_RejectsTruncatedShopShapeAndNegativeCoreState()
    {
        MethodInfo method = typeof(PlayerManager).GetMethod(
            "TryValidateDurablePlayerMigrationPayload",
            BindingFlags.Static | BindingFlags.NonPublic);
        object[] malformedShape =
        {
            7,
            100,
            10,
            5,
            true,
            new[] { "UnitData_Any" },
            Array.Empty<int>(),
            new[] { false },
            1,
            0,
            null,
            null
        };
        object[] negativeGold =
        {
            7,
            100,
            -1,
            5,
            true,
            Array.Empty<string>(),
            Array.Empty<int>(),
            Array.Empty<bool>(),
            1,
            0,
            null,
            null
        };

        Assert.That((bool)method.Invoke(null, malformedShape), Is.False);
        Assert.That((string)malformedShape[11], Does.Contain("shape_mismatch"));
        Assert.That((bool)method.Invoke(null, negativeGold), Is.False);
        Assert.That((string)negativeGold[11], Does.Contain("gold_out_of_range"));
    }

    [Test]
    public void AugmentMigrationPayload_AcceptsExplicitEmptySnapshotsButRejectsMissingCaptureFlags()
    {
        MethodInfo presentedMethod = typeof(PlayerManager).GetMethod(
            "TryNormalizePresentedAugmentMigrationSnapshot",
            BindingFlags.Static | BindingFlags.NonPublic);
        MethodInfo selectedMethod = typeof(PlayerManager).GetMethod(
            "TryNormalizeSelectedAugmentMigrationSnapshot",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(presentedMethod, Is.Not.Null);
        Assert.That(selectedMethod, Is.Not.Null);

        object[] presentedEmpty = { true, Array.Empty<string>(), null, null };
        object[] selectedEmpty = { true, Array.Empty<string>(), null, null, null, null };
        object[] presentedMissing = { false, Array.Empty<string>(), null, null };

        Assert.That((bool)presentedMethod.Invoke(null, presentedEmpty), Is.True);
        Assert.That((string[])presentedEmpty[2], Is.Empty);
        Assert.That((bool)selectedMethod.Invoke(null, selectedEmpty), Is.True);
        Assert.That((int[])selectedEmpty[2], Is.Empty);
        Assert.That((string[])selectedEmpty[4], Is.Empty);
        Assert.That((bool)presentedMethod.Invoke(null, presentedMissing), Is.False);
        Assert.That((string)presentedMissing[3], Does.Contain("not_captured"));
    }

    [Test]
    public void AugmentMigrationPayload_RejectsAmbiguousLegacyDisplayName()
    {
        MethodInfo normalizeMethod = typeof(PlayerManager).GetMethod(
            "TryNormalizePresentedAugmentMigrationSnapshot",
            BindingFlags.Static | BindingFlags.NonPublic);
        FieldInfo contentIdField = typeof(AugmentData).GetField(
            "contentId",
            BindingFlags.Instance | BindingFlags.NonPublic);
        string legacyName = $"LegacyDuplicate_{Guid.NewGuid():N}";
        AugmentData first = ScriptableObject.CreateInstance<AugmentData>();
        AugmentData second = ScriptableObject.CreateInstance<AugmentData>();
        first.name = $"First_{Guid.NewGuid():N}";
        second.name = $"Second_{Guid.NewGuid():N}";
        first.augmentName = legacyName;
        second.augmentName = legacyName;
        contentIdField.SetValue(first, $"augment.test.{Guid.NewGuid():N}");
        contentIdField.SetValue(second, $"augment.test.{Guid.NewGuid():N}");
        try
        {
            object[] args = { true, new[] { legacyName }, null, null };

            bool valid = (bool)normalizeMethod.Invoke(null, args);

            Assert.That(valid, Is.False);
            Assert.That((string)args[3], Does.Contain("ambiguous"));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(first);
            UnityEngine.Object.DestroyImmediate(second);
        }
    }

    [Test]
    public void DurableAndAugmentRestores_ReturnTerminalReports()
    {
        MethodInfo durableRestore = typeof(PlayerManager).GetMethod(
            "RestoreDurableStateAfterHostMigration",
            BindingFlags.Instance | BindingFlags.Public);
        MethodInfo augmentRestore = typeof(PlayerManager).GetMethod(
            "RestoreAugmentSnapshotsAfterHostMigration",
            BindingFlags.Instance | BindingFlags.Public);

        Assert.That(durableRestore, Is.Not.Null);
        Assert.That(durableRestore.ReturnType, Is.EqualTo(typeof(MigrationRestoreReport)));
        Assert.That(augmentRestore, Is.Not.Null);
        Assert.That(augmentRestore.ReturnType, Is.EqualTo(typeof(MigrationRestoreReport)));
    }

    [Test]
    public void HistoricalDuplicateScrollName_IsAlwaysRejectedAsAmbiguousLegacyPayload()
    {
        MethodInfo normalizeMethod = typeof(PlayerManager).GetMethod(
            "TryNormalizePresentedAugmentMigrationSnapshot",
            BindingFlags.Static | BindingFlags.NonPublic);
        object[] args =
        {
            true,
            new[] { "\uB9C8\uBC95\uC2A4\uD06C\uB864(\uD68C\uBCF5)" },
            null,
            null
        };

        bool valid = (bool)normalizeMethod.Invoke(null, args);

        Assert.That(valid, Is.False);
        Assert.That((string)args[3], Does.Contain("known_ambiguous_legacy"));
    }

    [Test]
    public void ShopRuntimeDuplicateRevisionSkip_RequiresExactSlotContents()
    {
        var gameObject = new GameObject("ShopRuntimeExactTest");
        UnitData unitData = ScriptableObject.CreateInstance<UnitData>();
        unitData.name = "UnitData_RuntimeExact";
        try
        {
            ShopManager manager = gameObject.AddComponent<ShopManager>();
            manager.GetCurrentShopItems().Add(new ShopItem(unitData, 1));
            MethodInfo exactMethod = typeof(ShopManager).GetMethod(
                "DoesRuntimeShopExactlyMatch",
                BindingFlags.Instance | BindingFlags.NonPublic);

            bool exact = (bool)exactMethod.Invoke(
                manager,
                new object[]
                {
                    new[] { StableDataKeyUtility.NormalizeKey(unitData.name) },
                    new[] { 1 },
                    new[] { false }
                });
            bool wrongStar = (bool)exactMethod.Invoke(
                manager,
                new object[]
                {
                    new[] { StableDataKeyUtility.NormalizeKey(unitData.name) },
                    new[] { 2 },
                    new[] { false }
                });
            bool staleForEmpty = (bool)exactMethod.Invoke(
                manager,
                new object[]
                {
                    Array.Empty<string>(),
                    Array.Empty<int>(),
                    Array.Empty<bool>()
                });

            Assert.That(exact, Is.True);
            Assert.That(wrongStar, Is.False);
            Assert.That(staleForEmpty, Is.False);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(unitData);
            UnityEngine.Object.DestroyImmediate(gameObject);
        }
    }

    [Test]
    public void ShopRuntimeExactMatch_AcceptsEmptySnapshotOnlyWhenSoldTailIsClear()
    {
        var gameObject = new GameObject("ShopRuntimeEmptySnapshotTest");
        try
        {
            ShopManager manager = gameObject.AddComponent<ShopManager>();
            MethodInfo exactMethod = typeof(ShopManager).GetMethod(
                "DoesRuntimeShopExactlyMatch",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(exactMethod, Is.Not.Null);

            bool emptyExact = (bool)exactMethod.Invoke(
                manager,
                new object[]
                {
                    Array.Empty<string>(),
                    Array.Empty<int>(),
                    Array.Empty<bool>()
                });
            manager.MarkSlotAsPurchased(0);
            bool hiddenSoldTailExact = (bool)exactMethod.Invoke(
                manager,
                new object[]
                {
                    Array.Empty<string>(),
                    Array.Empty<int>(),
                    Array.Empty<bool>()
                });

            Assert.That(emptyExact, Is.True,
                "an authoritative empty shop is valid and must clear the local runtime cache");
            Assert.That(hiddenSoldTailExact, Is.False,
                "empty snapshots cannot be treated as applied while a stale sold bit remains");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(gameObject);
        }
    }

    [Test]
    public void PresentedAugmentRuntimeExactRestore_IsAtomicOnUnknownContentId()
    {
        var gameObject = new GameObject("PresentedAugmentAtomicTest");
        AugmentData existing = ScriptableObject.CreateInstance<AugmentData>();
        AugmentData replacement = ScriptableObject.CreateInstance<AugmentData>();
        FieldInfo contentIdField = typeof(AugmentData).GetField(
            "contentId",
            BindingFlags.Instance | BindingFlags.NonPublic);
        string existingId = $"augment.test.{Guid.NewGuid():N}";
        string replacementId = $"augment.test.{Guid.NewGuid():N}";
        contentIdField.SetValue(existing, existingId);
        contentIdField.SetValue(replacement, replacementId);
        try
        {
            AugmentManager manager = gameObject.AddComponent<AugmentManager>();
            FieldInfo loadedField = typeof(AugmentManager).GetField(
                "isDataLoaded",
                BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo registryField = typeof(AugmentManager).GetField(
                "augmentsByContentId",
                BindingFlags.Instance | BindingFlags.NonPublic);
            loadedField.SetValue(manager, true);
            var registry = (Dictionary<string, AugmentData>)registryField.GetValue(manager);
            registry.Add(existingId, existing);
            registry.Add(replacementId, replacement);
            manager.GetPresentedAugments().Add(existing);

            bool unknownApplied = manager.TrySetPresentedAugmentsByContentIdsExactAsync(
                    new[] { "augment.test.missing" },
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            Assert.That(unknownApplied, Is.False);
            Assert.That(manager.GetPresentedAugments(), Is.EqualTo(new[] { existing }));

            bool replacementApplied = manager.TrySetPresentedAugmentsByContentIdsExactAsync(
                    new[] { replacementId },
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            Assert.That(replacementApplied, Is.True);
            Assert.That(manager.GetPresentedAugments(), Is.EqualTo(new[] { replacement }));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(existing);
            UnityEngine.Object.DestroyImmediate(replacement);
            UnityEngine.Object.DestroyImmediate(gameObject);
        }
    }

    [Test]
    public void RuntimeCacheRestores_AreAwaitableTerminalReports()
    {
        MethodInfo shopRuntime = typeof(PlayerManager).GetMethod(
            "RestoreShopRuntimeAfterHostMigrationAsync",
            BindingFlags.Instance | BindingFlags.Public);
        MethodInfo augmentRuntime = typeof(PlayerManager).GetMethod(
            "RestorePresentedAugmentRuntimeAfterHostMigrationAsync",
            BindingFlags.Instance | BindingFlags.Public);
        MethodInfo augmentGameplay = typeof(PlayerManager).GetMethod(
            "RestoreAugmentGameplayStateAfterHostMigration",
            BindingFlags.Instance | BindingFlags.Public);

        Assert.That(shopRuntime, Is.Not.Null);
        Assert.That(shopRuntime.ReturnType, Is.EqualTo(typeof(UniTask<MigrationRestoreReport>)));
        Assert.That(augmentRuntime, Is.Not.Null);
        Assert.That(augmentRuntime.ReturnType, Is.EqualTo(typeof(UniTask<MigrationRestoreReport>)));
        Assert.That(augmentGameplay, Is.Not.Null);
        Assert.That(augmentGameplay.ReturnType, Is.EqualTo(typeof(MigrationRestoreReport)));
    }

    [Test]
    public void ShopSyncCommandSerialization_RoundTripsSnapshotRevisionAndRound()
    {
        const BindingFlags members = BindingFlags.Instance | BindingFlags.NonPublic;
        var processor = new CommandProcessor();
        try
        {
            MethodInfo serialize = typeof(CommandProcessor).GetMethod("SerializeCommand", members);
            MethodInfo deserialize = typeof(CommandProcessor).GetMethod("DeserializeCommand", members);
            Assert.That(serialize, Is.Not.Null);
            Assert.That(deserialize, Is.Not.Null);

            var command = new SyncShopItemsCommand(
                3,
                new[] { "UnitData_A", "UnitData_B" },
                new[] { 1, 2 },
                snapshotRevision: 17,
                snapshotRound: 4);
            var payload = ((CommandType, int[], string[], Vector3[]))serialize.Invoke(
                processor,
                new object[] { command });

            Assert.That(payload.Item1, Is.EqualTo(CommandType.SyncShopItems));
            CollectionAssert.AreEqual(new[] { 3, 2, 17, 4 }, payload.Item2);

            var deserializeTask = (UniTask<ICommand>)deserialize.Invoke(
                processor,
                new object[]
                {
                    payload.Item1,
                    payload.Item2,
                    payload.Item3,
                    payload.Item4,
                    CancellationToken.None
                });
            var restored = deserializeTask.GetAwaiter().GetResult() as SyncShopItemsCommand;

            Assert.That(restored, Is.Not.Null);
            Assert.That(restored.SnapshotRevision, Is.EqualTo(17));
            Assert.That(restored.SnapshotRound, Is.EqualTo(4));
            CollectionAssert.AreEqual(command.UnitDataNames, restored.UnitDataNames);
            CollectionAssert.AreEqual(command.StarLevels, restored.StarLevels);

            var emptyCommand = new SyncShopItemsCommand(
                3,
                Array.Empty<string>(),
                Array.Empty<int>(),
                snapshotRevision: 18,
                snapshotRound: 4);
            var emptyPayload = ((CommandType, int[], string[], Vector3[]))serialize.Invoke(
                processor,
                new object[] { emptyCommand });
            CollectionAssert.AreEqual(new[] { 3, 0, 18, 4 }, emptyPayload.Item2);
            Assert.That(emptyPayload.Item3, Is.Empty);

            var legacyTask = (UniTask<ICommand>)deserialize.Invoke(
                processor,
                new object[]
                {
                    CommandType.SyncShopItems,
                    new[] { 3, 0 },
                    Array.Empty<string>(),
                    Array.Empty<Vector3>(),
                    CancellationToken.None
                });
            var legacy = legacyTask.GetAwaiter().GetResult() as SyncShopItemsCommand;
            Assert.That(legacy, Is.Not.Null);
            Assert.That(legacy.SnapshotRevision, Is.Zero);
            Assert.That(legacy.SnapshotRound, Is.Zero);
            Assert.That(legacy.UnitDataNames, Is.Empty);
            Assert.That(legacy.StarLevels, Is.Empty);
        }
        finally
        {
            processor.CancelPendingCommands();
        }
    }

    [Test]
    public void ShopSnapshotRevisionGate_NeverLetsStalePayloadRollBackNewerState()
    {
        MethodInfo gate = typeof(ShopManager).GetMethod(
            "IsSnapshotAtOrAfter",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(gate, Is.Not.Null);

        bool Evaluate(int candidateRevision, int candidateRound, int expectedRevision, int expectedRound)
        {
            return (bool)gate.Invoke(
                null,
                new object[] { candidateRevision, candidateRound, expectedRevision, expectedRound });
        }

        Assert.That(Evaluate(10, 3, 10, 3), Is.True);
        Assert.That(Evaluate(10, 2, 10, 3), Is.False,
            "an equal revision from an older round must wait");
        Assert.That(Evaluate(11, 3, 10, 3), Is.True,
            "a late revision-10 broadcast must apply the current revision-11 snapshot");
        Assert.That(Evaluate(10, 3, 11, 3), Is.False,
            "a revision-11 broadcast must not apply revision 10 while replication is pending");
        Assert.That(Evaluate(1, 9, int.MaxValue - 1, 8), Is.True,
            "revision wrap must still recognize the replicated snapshot as newer");
        Assert.That(Evaluate(0, 0, 10, 3), Is.False);
    }

    [Test]
    public void LegacyShopSyncPaths_CannotResetSoldFlagsFromNamesAndStars()
    {
        MethodInfo requestRpc = typeof(PlayerManager).GetMethod(
            "RPC_RequestSyncData",
            BindingFlags.Instance | BindingFlags.Public);
        MethodInfo syncRpc = typeof(PlayerManager).GetMethod(
            "RPC_SyncShopItems",
            BindingFlags.Instance | BindingFlags.Public);
        MethodInfo migrationEntry = typeof(GameManagers).GetMethod(
            "RestoreAfterHostMigration",
            BindingFlags.Instance | BindingFlags.Public);

        Assert.That(requestRpc, Is.Not.Null);
        Assert.That(syncRpc, Is.Not.Null);
        Assert.That(migrationEntry, Is.Not.Null);
        ParameterInfo[] syncRpcParameters = syncRpc.GetParameters();
        Assert.That(syncRpcParameters, Has.Length.EqualTo(4));
        Assert.That(syncRpcParameters[2].ParameterType, Is.EqualTo(typeof(int)));
        Assert.That(syncRpcParameters[3].ParameterType, Is.EqualTo(typeof(int)));
        Assert.That(typeof(ShopManager).GetMethod(
            "SetShopItemsFromServerAsync",
            BindingFlags.Instance | BindingFlags.Public), Is.Null,
            "names/stars-only mutation must not remain callable");
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(
            typeof(SyncShopItemsCommand),
            typeof(ShopManager),
            "ApplySnapshotFromNetworkAtOrAfterRevisionAsync"), Is.True);
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(
            typeof(RequestSyncDataCommand),
            typeof(PlayerManager),
            "TryGetShopSnapshot"), Is.True);
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(
            typeof(RequestSyncDataCommand),
            typeof(ShopManager),
            "GetCurrentShopItems"), Is.False);
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(
            requestRpc,
            typeof(PlayerManager),
            "TryGetShopSnapshot"), Is.True,
            "RPC_RequestSyncData must source revisioned Networked state, not the runtime cache");
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(
            requestRpc,
            typeof(ShopManager),
            "GetCurrentShopItems"), Is.False);
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(
            migrationEntry,
            typeof(SyncShopItemsCommand),
            ".ctor"), Is.False,
            "the initial migration pass must not broadcast before post-pass exact restore");
    }

    [Test]
    public void HostMigrationFailure_HasDistinctEventFromSuccessfulCompletion()
    {
        EventInfo completed = typeof(GameEvents).GetEvent(
            "OnHostMigrationCompleted",
            BindingFlags.Static | BindingFlags.Public);
        EventInfo failed = typeof(GameEvents).GetEvent(
            "OnHostMigrationFailed",
            BindingFlags.Static | BindingFlags.Public);

        Assert.That(completed, Is.Not.Null);
        Assert.That(failed, Is.Not.Null);
        Assert.That(failed.EventHandlerType, Is.EqualTo(typeof(Action<string>)));
    }
}
#endif
