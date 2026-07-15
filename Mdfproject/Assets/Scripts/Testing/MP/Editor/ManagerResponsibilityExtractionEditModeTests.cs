using NUnit.Framework;
using UnityEngine;

public sealed class ManagerResponsibilityExtractionEditModeTests
{
    [TestCase(false, true, true, true, 3, new[] { 3 }, "player_missing_state_authority")]
    [TestCase(true, false, true, true, 3, new[] { 3 }, "missing_rpc_source")]
    [TestCase(true, true, false, true, 3, new[] { 3 }, "rpc_source_not_input_authority")]
    [TestCase(true, true, true, false, 3, new[] { 3 }, "player_not_ready")]
    [TestCase(true, true, true, true, 3, new int[0], "missing_player_id")]
    [TestCase(true, true, true, true, 3, new[] { 7 }, "player_id_mismatch:7")]
    public void CommandValidatorEnvelope_PreservesRejectReasonOrder(
        bool hasStateAuthority,
        bool hasRpcSource,
        bool sourceMatches,
        bool playerReady,
        int playerId,
        int[] payload,
        string expectedReason)
    {
        bool accepted = PlayerCommandRequestValidator.ValidateEnvelope(
            hasStateAuthority,
            hasRpcSource,
            sourceMatches,
            playerReady,
            playerId,
            payload,
            out string reason);

        Assert.That(accepted, Is.False);
        Assert.That(reason, Is.EqualTo(expectedReason));
    }

    [Test]
    public void CommandValidatorEnvelope_AcceptsMatchingAuthorityEnvelope()
    {
        Assert.That(PlayerCommandRequestValidator.ValidateEnvelope(
            true, true, true, true, 3, new[] { 3 }, out string reason), Is.True);
        Assert.That(reason, Is.Null);
    }

    [Test]
    public void FieldAiPlacementService_CenterAndPathCoverageRemainDeterministic()
    {
        float center = FieldAiPlacementService.CalculateFieldCenterScore(
            new Vector2Int(5, 5),
            new Vector3Int(2, 2, 0));
        float corner = FieldAiPlacementService.CalculateFieldCenterScore(
            new Vector2Int(5, 5),
            Vector3Int.zero);
        Assert.That(center, Is.EqualTo(1f).Within(0.0001f));
        Assert.That(corner, Is.EqualTo(0f).Within(0.0001f));

        var path = new System.Collections.Generic.HashSet<Vector3Int>
        {
            new Vector3Int(1, 2, 0),
            new Vector3Int(2, 2, 0),
            new Vector3Int(3, 2, 0),
            new Vector3Int(4, 2, 0)
        };
        Assert.That(FieldAiPlacementService.CountCoveredPathTiles(
            new Vector3Int(2, 2, 0), path, 1f), Is.EqualTo(3));
    }

    [Test]
    public void ExtractedPoliciesCompileIntoNamedRuntimeAssemblies()
    {
        Assert.That(typeof(PlayerSnapshotCodec).Assembly.GetName().Name, Is.EqualTo("MDF.Runtime.Foundation"));
        Assert.That(typeof(MatchFlowPolicy).Assembly.GetName().Name, Is.EqualTo("MDF.Runtime.Foundation"));
        Assert.That(typeof(OrderedInventory<>).Assembly.GetName().Name, Is.EqualTo("MDF.Runtime.Foundation"));
        Assert.That(typeof(FieldGridGeometry).Assembly.GetName().Name, Is.EqualTo("MDF.Runtime.Grid"));
        Assert.That(typeof(GridOccupancyIndex<>).Assembly.GetName().Name, Is.EqualTo("MDF.Runtime.Grid"));
        Assert.That(typeof(BoundedUnityObjectPool<>).Assembly.GetName().Name, Is.EqualTo("MDF.Runtime.Pooling"));
        Assert.That(typeof(LifecycleGeneration).Assembly.GetName().Name, Is.EqualTo("MDF.Runtime.Foundation"));
        Assert.That(typeof(PlayerMagicScrollInventory).Assembly.GetName().Name, Is.EqualTo("Assembly-CSharp"));
    }

    [Test]
    public void ExtractedResponsibilities_AreWiredThroughTypedRuntimeServices()
    {
        const System.Reflection.BindingFlags PrivateInstance =
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;

        Assert.That(typeof(FieldManager).GetField("placedUnits", PrivateInstance)?.FieldType,
            Is.EqualTo(typeof(GridOccupancyIndex<Unit>)));
        Assert.That(typeof(PlayerMagicScrollInventory).GetField("_inventory", PrivateInstance)?.FieldType,
            Is.EqualTo(typeof(OrderedInventory<MagicScrollData>)));
        Assert.That(typeof(Unit).GetField("_asyncLifecycle", PrivateInstance)?.FieldType,
            Is.EqualTo(typeof(LifecycleGeneration)));
        Assert.That(typeof(Monster).GetField("_spawnLifecycle", PrivateInstance)?.FieldType,
            Is.EqualTo(typeof(LifecycleGeneration)));
    }

    [Test]
    public void MatchFlowPolicy_PreservesTimerAndTransitionGateRules()
    {
        Assert.That(MatchFlowPolicy.ResolveDisplayedPhaseTime(7.5f, false), Is.EqualTo(7.5f));
        Assert.That(MatchFlowPolicy.ResolveDisplayedPhaseTime(-1f, false), Is.Zero);
        Assert.That(MatchFlowPolicy.ResolveDisplayedPhaseTime(7.5f, true), Is.Zero);

        Assert.That(MatchFlowPolicy.ShouldIgnoreTransitionRequest(true, false, false), Is.True);
        Assert.That(MatchFlowPolicy.ShouldIgnoreTransitionRequest(false, true, false), Is.True);
        Assert.That(MatchFlowPolicy.ShouldIgnoreTransitionRequest(false, false, true), Is.True);
        Assert.That(MatchFlowPolicy.ShouldIgnoreTransitionRequest(false, false, false), Is.False);

        Assert.That(MatchFlowPolicy.ShouldWaitForCombatDebt(true, true), Is.True);
        Assert.That(MatchFlowPolicy.ShouldWaitForCombatDebt(false, true), Is.False);
        Assert.That(MatchFlowPolicy.ResolvePendingCombatDebt(true, 4), Is.EqualTo(4));
        Assert.That(MatchFlowPolicy.ResolvePendingCombatDebt(false, 4), Is.Zero);
        Assert.That(MatchFlowPolicy.ShouldCompleteImmediately(false, 0f), Is.True);
        Assert.That(MatchFlowPolicy.ShouldCompleteImmediately(true, 0f), Is.False);
    }

    [Test]
    public void OrderedInventory_OwnsOrderingIdentityAndReadOnlyView()
    {
        var inventory = new OrderedInventory<string>(
            value => !string.IsNullOrWhiteSpace(value),
            (left, right) => string.Equals(left, right, System.StringComparison.OrdinalIgnoreCase));

        Assert.That(inventory.TryAdd(null), Is.False);
        Assert.That(inventory.TryAdd("First"), Is.True);
        Assert.That(inventory.TryAdd("Second"), Is.True);
        Assert.That(inventory.FindIndex("first"), Is.Zero);
        Assert.That(inventory.TryRemoveAt(0, out string removed), Is.True);
        Assert.That(removed, Is.EqualTo("First"));
        Assert.That(inventory.TryInsertAt(0, removed), Is.True);
        Assert.That(inventory.Items, Is.EqualTo(new[] { "First", "Second" }));

        var mutableView = inventory.Items as System.Collections.Generic.IList<string>;
        Assert.That(mutableView, Is.Not.Null);
        Assert.Throws<System.NotSupportedException>(() => mutableView.Add("Third"));
    }

    [Test]
    public void FoundationSnapshotCodec_RoundTripsPackedValues()
    {
        Assert.That(StableDataKeyUtility.NormalizeKey(" Fighter(Clone) "), Is.EqualTo("Fighter"));
        Assert.That(StableDataKeyUtility.StableKeyHash("Fighter(Clone)"),
            Is.EqualTo(StableDataKeyUtility.StableKeyHash("Fighter")));

        int playerIds = PlayerSnapshotCodec.PackPlayerIds(2, -1);
        Assert.That(PlayerSnapshotCodec.UnpackPlayerId(playerIds & 0xFFFF), Is.EqualTo(2));
        Assert.That(PlayerSnapshotCodec.UnpackPlayerId((playerIds >> 16) & 0xFFFF), Is.EqualTo(-1));

        int cell = PlayerSnapshotCodec.PackCell(-3, 17);
        PlayerSnapshotCodec.UnpackCell(cell, out int x, out int y);
        Assert.That(x, Is.EqualTo(-3));
        Assert.That(y, Is.EqualTo(17));

        int packedHealth = PlayerSnapshotCodec.PackHealthAndRevision(37.5f, 50f, 9);
        PlayerSnapshotCodec.UnpackHealthAndRevision(packedHealth, 50f, out float health, out int revision);
        Assert.That(health, Is.EqualTo(37.5f).Within(0.001f));
        Assert.That(revision, Is.EqualTo(9));
    }

    [Test]
    public void FieldGridGeometry_ConvertsInnerAndNavigationCoordinates()
    {
        var geometry = new FieldGridGeometry(
            new Vector3(10f, 2f, 20f),
            2f,
            new Vector2Int(10, 9),
            2);

        Assert.That(geometry.TotalSize, Is.EqualTo(new Vector2Int(14, 13)));
        Assert.That(geometry.TotalOrigin, Is.EqualTo(new Vector3(6f, 2f, 16f)));
        Assert.That(geometry.InnerCellToWorld(new Vector2Int(1, 2), 3f),
            Is.EqualTo(new Vector3(13f, 3f, 25f)));
        Assert.That(geometry.WorldToInnerCell(new Vector3(13f, 99f, 25f)), Is.EqualTo(new Vector2Int(1, 2)));
        Assert.That(geometry.InnerToNavigation(new Vector2Int(1, 2)), Is.EqualTo(new Vector2Int(3, 4)));
        Assert.That(geometry.TryNavigationToInner(new Vector2Int(3, 4), out Vector3Int inner), Is.True);
        Assert.That(inner, Is.EqualTo(new Vector3Int(1, 2, 0)));
        Assert.That(geometry.BuildBorderGapCells(), Has.Length.EqualTo(4));
    }

    [Test]
    public void MagicScrollInventory_ConsumeAndRefundPreserveSlotOrder()
    {
        var first = ScriptableObject.CreateInstance<MagicScrollData>();
        var second = ScriptableObject.CreateInstance<MagicScrollData>();
        first.name = "FirstScroll";
        second.name = "SecondScroll";

        try
        {
            var inventory = new PlayerMagicScrollInventory();
            Assert.That(inventory.Items, Is.Not.InstanceOf<System.Collections.Generic.List<MagicScrollData>>());
            var readOnlyList = inventory.Items as System.Collections.Generic.IList<MagicScrollData>;
            Assert.That(readOnlyList, Is.Not.Null);
            Assert.Throws<System.NotSupportedException>(() => readOnlyList.Add(first));
            Assert.That(inventory.TryAdd(null), Is.False);
            Assert.That(inventory.TryAdd(first), Is.True);
            Assert.That(inventory.TryAdd(second), Is.True);
            Assert.That(inventory.TryConsumeAt(0, out MagicScrollData consumed, out string reason), Is.True, reason);
            Assert.That(consumed, Is.SameAs(first));
            Assert.That(inventory.TryRefundAt(0, consumed), Is.True);
            Assert.That(inventory.Items[0], Is.SameAs(first));
            Assert.That(inventory.Items[1], Is.SameAs(second));
            Assert.That(inventory.BuildAssetNames(), Is.EqualTo(new[] { "FirstScroll", "SecondScroll" }));

            inventory.Replace(new[] { first, null, second });
            Assert.That(inventory.Count, Is.EqualTo(2));
            Assert.That(inventory.Items, Is.EqualTo(new[] { first, second }));
        }
        finally
        {
            Object.DestroyImmediate(first);
            Object.DestroyImmediate(second);
        }
    }

    [Test]
    public void FoundationSnapshotCodec_RejectsAmbiguousOverflowValues()
    {
        Assert.That(PlayerSnapshotCodec.PackPlayerId(PlayerSnapshotCodec.MinPlayerId), Is.EqualTo(0));
        Assert.That(PlayerSnapshotCodec.PackPlayerId(PlayerSnapshotCodec.MaxPlayerId), Is.EqualTo(ushort.MaxValue));
        Assert.That(PlayerSnapshotCodec.TryPackPlayerId(PlayerSnapshotCodec.MaxPlayerId + 1, out _), Is.False);
        Assert.Throws<System.ArgumentOutOfRangeException>(() =>
            PlayerSnapshotCodec.PackPlayerId(PlayerSnapshotCodec.MaxPlayerId + 1));

        Assert.That(PlayerSnapshotCodec.TryPackCell(short.MinValue, short.MaxValue, out int packedCell), Is.True);
        PlayerSnapshotCodec.UnpackCell(packedCell, out int x, out int y);
        Assert.That(x, Is.EqualTo(short.MinValue));
        Assert.That(y, Is.EqualTo(short.MaxValue));
        Assert.That(PlayerSnapshotCodec.TryPackCell(short.MinValue - 1, 0, out _), Is.False);
        Assert.That(PlayerSnapshotCodec.TryPackCell(0, short.MaxValue + 1, out _), Is.False);

        Assert.That(PlayerSnapshotCodec.TryPackHealthAndRevision(1f, 1f, ushort.MaxValue, out _), Is.True);
        Assert.That(PlayerSnapshotCodec.TryPackHealthAndRevision(1f, 1f, ushort.MaxValue + 1, out _), Is.False);
        Assert.Throws<System.ArgumentOutOfRangeException>(() =>
            PlayerSnapshotCodec.PackHealthAndRevision(1f, 1f, ushort.MaxValue + 1));
    }
}
