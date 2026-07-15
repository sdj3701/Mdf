#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using Fusion;
using MDF.Runtime.UI;
using NUnit.Framework;
using Assert = NUnit.Framework.Assert;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public sealed class WallUpgradeAndFirstSpawnPrewarmEditModeTests
{
    private const string WallProgressionPath = "Assets/GameData/Walls/WallProgression_Default.asset";
    private const string WallPrefabPath = "Assets/Prefabs/Structure/DestructibleWallPrefab.prefab";
    private const string WallActionPanelPath = "Assets/Prefabs/UI/Unit/UI_Can_WallRemove.prefab";

    [Test]
    public void WallProgressionAssetsDefineThreeIncreasingVisualLevels()
    {
        WallProgressionData progression = AssetDatabase.LoadAssetAtPath<WallProgressionData>(WallProgressionPath);
        Assert.That(progression, Is.Not.Null, WallProgressionPath);
        Assert.That(progression.LevelCount, Is.EqualTo(3));
        Assert.That(progression.MaxLevel, Is.EqualTo(3));
        Assert.That(progression.IsValid(out string reason), Is.True, reason);

        float[] expectedHealth = { 200f, 350f, 550f };
        int[] expectedUpgradeCosts = { 0, 2, 4 };
        var visualMaterialPaths = new HashSet<string>(StringComparer.Ordinal);
        var visualColors = new HashSet<string>(StringComparer.Ordinal);

        for (int level = 1; level <= 3; level++)
        {
            string levelPath = $"Assets/GameData/Walls/WallLevel_{level}.asset";
            WallLevelData expectedAsset = AssetDatabase.LoadAssetAtPath<WallLevelData>(levelPath);
            Assert.That(expectedAsset, Is.Not.Null, levelPath);
            Assert.That(progression.TryGetLevel(level, out WallLevelData data), Is.True, levelPath);
            Assert.That(data, Is.SameAs(expectedAsset));
            Assert.That(data.Level, Is.EqualTo(level));
            Assert.That(data.MaxHealth, Is.EqualTo(expectedHealth[level - 1]).Within(0.001f));
            Assert.That(data.UpgradeCostFromPreviousLevel, Is.EqualTo(expectedUpgradeCosts[level - 1]));
            Assert.That(data.VisualMesh, Is.Not.Null, $"level {level} visual mesh");
            Assert.That(data.VisualMaterials, Is.Not.Null.And.Not.Empty, $"level {level} materials");
            CollectionAssert.AllItemsAreNotNull(data.VisualMaterials);

            Material material = data.VisualMaterials[0];
            string materialPath = AssetDatabase.GetAssetPath(material);
            Assert.That(materialPath, Is.Not.Empty, $"level {level} material must be a persistent asset");
            Assert.That(visualMaterialPaths.Add(materialPath), Is.True,
                $"level {level} must own a distinct presentation material");
            Assert.That(visualColors.Add(ColorUtility.ToHtmlStringRGBA(material.color)), Is.True,
                $"level {level} must be visually distinguishable by color");
        }

        Assert.That(progression.TryGetNextLevel(1, out WallLevelData levelTwo), Is.True);
        Assert.That(levelTwo.Level, Is.EqualTo(2));
        Assert.That(progression.TryGetNextLevel(2, out WallLevelData levelThree), Is.True);
        Assert.That(levelThree.Level, Is.EqualTo(3));
        Assert.That(progression.TryGetNextLevel(3, out _), Is.False);
    }

    [Test]
    public void WallRuntimeProgressionPreservesDamageAndAccumulatesRefundableInvestment()
    {
        WallProgressionData progression = AssetDatabase.LoadAssetAtPath<WallProgressionData>(WallProgressionPath);
        Assert.That(progression, Is.Not.Null, WallProgressionPath);

        var wallObject = new GameObject("WallProgressionRuntimeTest");
        try
        {
            wallObject.AddComponent<MeshFilter>();
            MeshRenderer renderer = wallObject.AddComponent<MeshRenderer>();
            DestructibleWall wall = wallObject.AddComponent<DestructibleWall>();
            FieldInfo progressionField = typeof(DestructibleWall).GetField(
                "progressionData",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(progressionField, Is.Not.Null);
            progressionField.SetValue(wall, progression);

            wall.Initialize(null, new Vector3Int(2, 4, 0));
            Assert.That(wall.CurrentLevel, Is.EqualTo(1));
            Assert.That(wall.MaxHealth, Is.EqualTo(200f).Within(0.001f));
            Assert.That(wall.InvestedUpgradeGold, Is.Zero);
            Assert.That(wall.RestoreHealthAfterMigration(150f, 200f, 9), Is.True);

            Assert.That(wall.TryGetUpgradeQuote(1, out int nextLevel, out int cost, out string reason),
                Is.True, reason);
            Assert.That(nextLevel, Is.EqualTo(2));
            Assert.That(cost, Is.EqualTo(2));
            Assert.That(wall.TryUpgradeFromAuthority(1, cost, out reason), Is.True, reason);
            Assert.That(wall.CurrentLevel, Is.EqualTo(2));
            Assert.That(wall.MaxHealth, Is.EqualTo(350f).Within(0.001f));
            Assert.That(wall.CurrentHealth, Is.EqualTo(300f).Within(0.01f),
                "upgrading must preserve the 50 points of existing damage");
            Assert.That(wall.InvestedUpgradeGold, Is.EqualTo(2));

            Assert.That(wall.TryGetUpgradeQuote(2, out nextLevel, out cost, out reason), Is.True, reason);
            Assert.That(nextLevel, Is.EqualTo(3));
            Assert.That(cost, Is.EqualTo(4));
            Assert.That(wall.TryUpgradeFromAuthority(2, cost, out reason), Is.True, reason);
            Assert.That(wall.CurrentLevel, Is.EqualTo(3));
            Assert.That(wall.MaxHealth, Is.EqualTo(550f).Within(0.001f));
            Assert.That(wall.CurrentHealth, Is.EqualTo(500f).Within(0.01f));
            Assert.That(wall.InvestedUpgradeGold, Is.EqualTo(6),
                "a level-three demolition must be able to refund both upgrade purchases in full");
            Assert.That(renderer.sharedMaterials, Is.EquivalentTo(GetLevelThreeMaterials(progression)));

            Assert.That(wall.TryGetUpgradeQuote(3, out _, out _, out reason), Is.False);
            Assert.That(reason, Is.EqualTo("wall_upgrade_max_level"));

            MethodInfo resetForPool = typeof(DestructibleWall).GetMethod(
                "ResetTransientStateForPoolReuse",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(resetForPool, Is.Not.Null);
            resetForPool.Invoke(wall, new object[] { true });
            Assert.That(wall.CurrentLevel, Is.EqualTo(1));
            Assert.That(wall.InvestedUpgradeGold, Is.Zero);
            Assert.That(wall.MaxHealth, Is.EqualTo(200f).Within(0.001f));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(wallObject);
        }
    }

    [Test]
    public void WallUpgradeCodecRoundTripsAndRejectsOverflow()
    {
        AssertWallUpgradeRoundTrip(1, 0);
        AssertWallUpgradeRoundTrip(2, 2);
        AssertWallUpgradeRoundTrip(3, 6);
        AssertWallUpgradeRoundTrip(byte.MaxValue, 0x00FFFFFF);

        Assert.That(PlayerSnapshotCodec.TryPackWallUpgradeState(0, 0, out _), Is.False);
        Assert.That(PlayerSnapshotCodec.TryPackWallUpgradeState(byte.MaxValue + 1, 0, out _), Is.False);
        Assert.That(PlayerSnapshotCodec.TryPackWallUpgradeState(1, -1, out _), Is.False);
        Assert.That(PlayerSnapshotCodec.TryPackWallUpgradeState(1, 0x01000000, out _), Is.False);
        Assert.Throws<ArgumentOutOfRangeException>(() => PlayerSnapshotCodec.PackWallUpgradeState(0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PlayerSnapshotCodec.PackWallUpgradeState(1, 0x01000000));
    }

    [Test]
    public void UpgradeWallCommandKeepsStableTypeAndRoundTripsItsObservedLevel()
    {
        Assert.That((int)CommandType.ActivateKingSkill, Is.EqualTo(15),
            "existing command ids must not be renumbered");
        Assert.That((int)CommandType.UpgradeWall, Is.EqualTo(16));

        const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic;
        var processor = new CommandProcessor();
        try
        {
            MethodInfo serialize = typeof(CommandProcessor).GetMethod("SerializeCommand", Members);
            MethodInfo deserialize = typeof(CommandProcessor).GetMethod("DeserializeCommand", Members);
            Assert.That(serialize, Is.Not.Null);
            Assert.That(deserialize, Is.Not.Null);

            var position = new Vector3Int(3, 5, 0);
            var command = new UpgradeWallCommand(7, position, 2);
            var payload = ((CommandType, int[], string[], Vector3[]))serialize.Invoke(
                processor,
                new object[] { command });

            Assert.That(payload.Item1, Is.EqualTo(CommandType.UpgradeWall));
            CollectionAssert.AreEqual(new[] { 7, 2 }, payload.Item2);
            Assert.That(payload.Item3, Is.Empty);
            Assert.That(payload.Item4, Has.Length.EqualTo(1));
            Assert.That(payload.Item4[0], Is.EqualTo((Vector3)position));

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
            UpgradeWallCommand restored = deserializeTask.GetAwaiter().GetResult() as UpgradeWallCommand;
            Assert.That(restored, Is.Not.Null);
            Assert.That(restored.PlayerId, Is.EqualTo(7));
            Assert.That(restored.Position, Is.EqualTo(position));
            Assert.That(restored.ExpectedCurrentLevel, Is.EqualTo(2));
        }
        finally
        {
            processor.CancelPendingCommands();
        }

        string source = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/Commands/Core/CommandProcessor.cs");
        Assert.That(source, Does.Contain("case UpgradeWallCommand cmd:"));
        Assert.That(source, Does.Contain("cmd.ExpectedCurrentLevel"));
        Assert.That(source, Does.Contain("case CommandType.UpgradeWall:"));
        Assert.That(source, Does.Contain("new UpgradeWallCommand(ints[0], Vector3Int.RoundToInt(vectors[0]), ints[1])"));
    }

    [Test]
    public void UpgradeAndDemolitionContractsSpendOnAuthorityAndRefundAfterSuccessfulRemoval()
    {
        string upgrade = MdfSourcePolicy.ReadStaticContract(
            "Assets/Scripts/Commands/PlayerActions/UpgradeWallCommand.cs");
        string remove = MdfSourcePolicy.ReadStaticContract(
            "Assets/Scripts/Commands/PlayerActions/RemoveWallCommand.cs");
        string validator = MdfSourcePolicy.ReadStaticContract(
            "Assets/Scripts/Managers/PlayerCommandRequestValidator.cs");

        Assert.That(validator, Does.Contain("case CommandType.UpgradeWall:"));
        Assert.That(validator, Does.Contain("ValidateUpgradeWallRequest"));
        Assert.That(validator, Does.Contain("UpgradeWallCommand.TryValidate"));
        Assert.That(upgrade, Does.Contain("wall_upgrade_requires_stable_prepare"));
        Assert.That(upgrade, Does.Contain("wall_upgrade_field_ownership_mismatch"));
        Assert.That(upgrade, Does.Contain("wall_upgrade_insufficient_gold"));

        int spendIndex = upgrade.IndexOf("player.SpendGold(upgradeCost)", StringComparison.Ordinal);
        int commitIndex = upgrade.IndexOf("wall.TryUpgradeFromAuthority", StringComparison.Ordinal);
        int rollbackIndex = upgrade.IndexOf("player.AddGold(upgradeCost)", StringComparison.Ordinal);
        Assert.That(spendIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(commitIndex, Is.GreaterThan(spendIndex));
        Assert.That(rollbackIndex, Is.GreaterThan(commitIndex),
            "a failed wall mutation must roll the reserved gold back");

        int captureRefundIndex = remove.IndexOf("destructibleWall.InvestedUpgradeGold", StringComparison.Ordinal);
        int removeWallIndex = remove.IndexOf("fm.RemoveWallAt(Position)", StringComparison.Ordinal);
        int successfulRemovalGateIndex = remove.IndexOf("if (removed)", StringComparison.Ordinal);
        int returnStockIndex = remove.IndexOf("player.ReturnWall()", StringComparison.Ordinal);
        int refundGoldIndex = remove.IndexOf("player.AddGold(upgradeRefund)", StringComparison.Ordinal);
        Assert.That(captureRefundIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(removeWallIndex, Is.GreaterThan(captureRefundIndex));
        Assert.That(successfulRemovalGateIndex, Is.GreaterThan(removeWallIndex));
        Assert.That(returnStockIndex, Is.GreaterThan(successfulRemovalGateIndex));
        Assert.That(refundGoldIndex, Is.GreaterThan(successfulRemovalGateIndex));
        Assert.That(remove, Does.Contain("ReturnPermanentWallPlacement"),
            "permanent-wall demolition must retain its separate stock refund");
    }

    [Test]
    public void WallPrefabBindsNetworkedProgressionAndLevelOnePresentation()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(WallPrefabPath);
        Assert.That(prefab, Is.Not.Null, WallPrefabPath);
        DestructibleWall wall = prefab.GetComponent<DestructibleWall>();
        NetworkObject networkObject = prefab.GetComponent<NetworkObject>();
        Assert.That(wall, Is.Not.Null);
        Assert.That(networkObject, Is.Not.Null);
        Assert.That(wall, Is.InstanceOf<NetworkBehaviour>());

        var serializedWall = new SerializedObject(wall);
        WallProgressionData progression = serializedWall.FindProperty("progressionData").objectReferenceValue
            as WallProgressionData;
        MeshFilter meshFilter = serializedWall.FindProperty("visualMeshFilter").objectReferenceValue as MeshFilter;
        MeshRenderer renderer = serializedWall.FindProperty("visualRenderer").objectReferenceValue as MeshRenderer;
        Assert.That(progression, Is.Not.Null);
        Assert.That(AssetDatabase.GetAssetPath(progression), Is.EqualTo(WallProgressionPath));
        Assert.That(meshFilter, Is.Not.Null);
        Assert.That(renderer, Is.Not.Null);
        Assert.That(progression.TryGetLevel(1, out WallLevelData levelOne), Is.True);
        Assert.That(meshFilter.sharedMesh, Is.SameAs(levelOne.VisualMesh));
        Assert.That(renderer.sharedMaterials, Is.EquivalentTo(levelOne.VisualMaterials));

        foreach (string propertyName in new[]
                 {
                     "SyncedUpgradeLevel",
                     "SyncedInvestedUpgradeGold",
                     "SyncedOwnerPlayerId",
                     "SyncedGridX",
                     "SyncedGridY"
                 })
        {
            PropertyInfo property = typeof(DestructibleWall).GetProperty(
                propertyName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(property, Is.Not.Null, propertyName);
            Assert.That(property.PropertyType, Is.EqualTo(typeof(int)), propertyName);
            Assert.That(property.GetCustomAttributes(false).Any(attribute =>
                attribute.GetType().Name == "NetworkedAttribute" ||
                attribute.GetType().Name == "NetworkedWeavedAttribute"), Is.True, propertyName);
        }

        PropertyInfo placementReady = typeof(DestructibleWall).GetProperty(
            "SyncedPlacementReady",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(placementReady, Is.Not.Null);
        Assert.That(placementReady.PropertyType, Is.EqualTo(typeof(NetworkBool)));
        Assert.That(placementReady.GetCustomAttributes(false).Any(attribute =>
            attribute.GetType().Name == "NetworkedAttribute" ||
            attribute.GetType().Name == "NetworkedWeavedAttribute"), Is.True);

        foreach ((string Name, Type Type) networkedHealthProperty in new[]
                 {
                     ("SyncedHealthReady", typeof(NetworkBool)),
                     ("SyncedCurrentHealth", typeof(float)),
                     ("SyncedHealthRevision", typeof(int))
                 })
        {
            PropertyInfo property = typeof(DestructibleWall).GetProperty(
                networkedHealthProperty.Name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(property, Is.Not.Null, networkedHealthProperty.Name);
            Assert.That(property.PropertyType, Is.EqualTo(networkedHealthProperty.Type),
                networkedHealthProperty.Name);
            Assert.That(property.GetCustomAttributes(false).Any(attribute =>
                attribute.GetType().Name == "NetworkedAttribute" ||
                attribute.GetType().Name == "NetworkedWeavedAttribute"), Is.True,
                networkedHealthProperty.Name);
        }

        MethodInfo placementQuery = typeof(DestructibleWall).GetMethod(
            "TryGetReplicatedPlacement",
            BindingFlags.Instance | BindingFlags.Public,
            null,
            new[] { typeof(int).MakeByRefType(), typeof(Vector3Int).MakeByRefType() },
            null);
        Assert.That(placementQuery, Is.Not.Null);
        Assert.That(placementQuery.ReturnType, Is.EqualTo(typeof(bool)));

        string wallSource = MdfSourcePolicy.ReadStaticContract(
            "Assets/Scripts/Game/Game Rules/DestructibleWall.cs");
        Assert.That(wallSource, Does.Contain("public override void Despawned"));
        Assert.That(wallSource, Does.Contain("SyncedUpgradeLevel = 1;"));
        Assert.That(wallSource, Does.Contain("SyncedInvestedUpgradeGold = 0;"));
        Assert.That(wallSource, Does.Contain("ApplyReplicatedProgressionState(updateHealth: true);"),
            "a replicated level-two/three pooled wall must enter its new lifecycle with that level's max HP");
        Assert.That(wallSource, Does.Contain("ApplyReplicatedHealthState(force: false);"),
            "clients must apply live health/revision changes, not only migration snapshot hashes");
        Assert.That(wallSource, Does.Contain("PublishAuthoritativeHealthState();"),
            "authority mutations must publish the wall health used by the remote status bar");
        Assert.That(wallSource, Does.Not.Contain("SyncedUpgradeLevel = Mathf.Max(1, localUpgradeLevel)"),
            "a recycled level-three clone must never seed a new wall with stale local progression");
    }

    [Test]
    public void WallMapUsesReplicatedOwnerAndCellBeforeWorldSpaceFallback()
    {
        string wallSource = MdfSourcePolicy.ReadStaticContract(
            "Assets/Scripts/Game/Game Rules/DestructibleWall.cs");
        string fieldSource = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/Managers/FieldManager.cs");

        Assert.That(wallSource, Does.Contain("Object.HasStateAuthority"));
        Assert.That(wallSource, Does.Contain("SyncedOwnerPlayerId = ownerPlayerId;"));
        Assert.That(wallSource, Does.Contain("SyncedGridX = gridPosition.x;"));
        Assert.That(wallSource, Does.Contain("SyncedGridY = gridPosition.y;"));
        Assert.That(wallSource, Does.Contain("SyncedPlacementReady = ownerPlayerId >= 0;"));

        int resolverIndex = fieldSource.IndexOf(
            "private bool TryResolveOwnedDestructibleWallCell",
            StringComparison.Ordinal);
        int replicatedQueryIndex = fieldSource.IndexOf(
            "wall.TryGetReplicatedPlacement",
            resolverIndex,
            StringComparison.Ordinal);
        int replicatedCellIndex = fieldSource.IndexOf(
            "cell = replicatedCell;",
            resolverIndex,
            StringComparison.Ordinal);
        int worldFallbackIndex = fieldSource.IndexOf(
            "IsWorldPositionInsideOwnedGrid(wall.transform.position)",
            resolverIndex,
            StringComparison.Ordinal);
        Assert.That(resolverIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(replicatedQueryIndex, Is.GreaterThan(resolverIndex));
        Assert.That(replicatedCellIndex, Is.GreaterThan(replicatedQueryIndex));
        Assert.That(worldFallbackIndex, Is.GreaterThan(replicatedCellIndex),
            "known replicated owner/grid metadata must win over a transient client transform");
        Assert.That(fieldSource, Does.Contain("ownerPlayerId != playerManager.playerId"),
            "a stale wallParent entry from another field must be rejected by durable playerId");
        Assert.That(fieldSource, Does.Contain(
            "TryResolveOwnedDestructibleWallCell(wall, out Vector3Int cell)"),
            "all collected candidates, not only global candidates, must be ownership-filtered");
    }

    [Test]
    public void WallLevelMaxHealthResolvesFromConfiguredPrefabWithoutLocalWallMap()
    {
        GameObject wallPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(WallPrefabPath);
        Assert.That(wallPrefab, Is.Not.Null, WallPrefabPath);

        var fieldObject = new GameObject("WallMaxHealthResolverTest");
        try
        {
            FieldManager field = fieldObject.AddComponent<FieldManager>();
            field.destructibleWallPrefab = wallPrefab;

            Assert.That(field.TryResolveDestructibleWallMaxHealth(1, out float levelOne), Is.True);
            Assert.That(field.TryResolveDestructibleWallMaxHealth(2, out float levelTwo), Is.True);
            Assert.That(field.TryResolveDestructibleWallMaxHealth(3, out float levelThree), Is.True);
            Assert.That(levelOne, Is.EqualTo(200f).Within(0.001f));
            Assert.That(levelTwo, Is.EqualTo(350f).Within(0.001f));
            Assert.That(levelThree, Is.EqualTo(550f).Within(0.001f));
            Assert.That(field.TryResolveDestructibleWallMaxHealth(4, out _), Is.False);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(fieldObject);
        }

        string migrationSource = MdfSourcePolicy.ReadStaticContract(
            "Assets/Scripts/Managers/PlayerManager.MigrationState.cs");
        int configuredMaxIndex = migrationSource.IndexOf(
            "fieldManager.TryResolveDestructibleWallMaxHealth",
            StringComparison.Ordinal);
        int localWallFallbackIndex = migrationSource.IndexOf(
            "else if (wall != null)",
            configuredMaxIndex,
            StringComparison.Ordinal);
        Assert.That(configuredMaxIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(localWallFallbackIndex, Is.GreaterThan(configuredMaxIndex),
            "durable health decoding must not depend on a transient client wall-map entry");
    }

    [Test]
    public void WallActionPrefabPlacesUpgradeBesideRemoveAndBindsItsLabel()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(WallActionPanelPath);
        Assert.That(prefab, Is.Not.Null, WallActionPanelPath);
        WallRemovePanelController controller = prefab.GetComponent<WallRemovePanelController>();
        Assert.That(controller, Is.Not.Null);

        var serializedController = new SerializedObject(controller);
        Button removeButton = serializedController.FindProperty("removeButton").objectReferenceValue as Button;
        Button upgradeButton = serializedController.FindProperty("upgradeButton").objectReferenceValue as Button;
        TMP_Text upgradeLabel = serializedController.FindProperty("upgradeLabel").objectReferenceValue as TMP_Text;
        Assert.That(removeButton, Is.Not.Null);
        Assert.That(upgradeButton, Is.Not.Null);
        Assert.That(upgradeLabel, Is.Not.Null);
        Assert.That(upgradeButton.name, Is.EqualTo("UI_Btn_Upgrade"));
        Assert.That(upgradeLabel.transform.IsChildOf(upgradeButton.transform), Is.True);
        Assert.That(upgradeLabel.text, Is.EqualTo("UP 2"));
        Assert.That(upgradeLabel.text, Does.Not.Contain("\n").And.Not.Contain("\r"));

        MethodInfo labelFormatter = typeof(WallRemovePanelController).GetMethod(
            "BuildUpgradeButtonLabel",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(labelFormatter, Is.Not.Null);
        string formatted = (string)labelFormatter.Invoke(null, new object[] { true, 6 });
        Assert.That(formatted, Is.EqualTo("UP 6"));
        Assert.That(formatted, Does.Not.Contain("\n").And.Not.Contain("\r"));

        RectTransform removeRect = removeButton.transform as RectTransform;
        RectTransform upgradeRect = upgradeButton.transform as RectTransform;
        Assert.That(removeRect, Is.Not.Null);
        Assert.That(upgradeRect, Is.Not.Null);
        Assert.That(upgradeRect.parent, Is.SameAs(removeRect.parent));
        Assert.That(upgradeRect.anchoredPosition.x, Is.GreaterThan(removeRect.anchoredPosition.x));
        Assert.That(upgradeRect.anchoredPosition.y, Is.EqualTo(removeRect.anchoredPosition.y).Within(0.01f));

        string controllerSource = MdfSourcePolicy.ReadStaticContract(
            "Assets/Scripts/UI/WallRemovePanelController.cs");
        string inputSource = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/Managers/MdfInput.cs");
        Assert.That(controllerSource, Does.Contain("new UpgradeWallCommand"));
        Assert.That(controllerSource, Does.Contain("bool isUpgradeableWall = _currentDestructibleWall != null"),
            "permanent walls must keep demolition without exposing the destructible-wall upgrade action");
        Assert.That(controllerSource, Does.Contain("IsPointerOverActiveActionButton"));
        FieldInfo removeGate = typeof(WallRemovePanelController).GetField(
            "_removeDispatchGate",
            BindingFlags.Instance | BindingFlags.NonPublic);
        FieldInfo upgradeGate = typeof(WallRemovePanelController).GetField(
            "_upgradeDispatchGate",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(removeGate?.FieldType, Is.EqualTo(typeof(FrameDispatchGate)),
            "manual world-space fallback and Button.onClick must share one remove dispatch gate");
        Assert.That(upgradeGate?.FieldType, Is.EqualTo(typeof(FrameDispatchGate)),
            "host-side synchronous completion must still share one upgrade dispatch gate");
        Assert.That(inputSource, Does.Contain("WallRemovePanelController.IsPointerOverActiveActionButton"));
    }

    [Test]
    public void MigrationCarriesLevelAndInvestmentAndRestoresProgressionBeforeHealth()
    {
        string playerMigration = MdfSourcePolicy.ReadStaticContract(
            "Assets/Scripts/Managers/PlayerManager.MigrationState.cs");
        string fieldMigration = MdfSourcePolicy.ReadStaticContract(
            "Assets/Scripts/Managers/FieldManager.MigrationWallState.cs");
        string hostMigration = MdfSourcePolicy.ReadStaticContract(
            "Assets/Scripts/Network/HostMigrationHandler.cs");
        string snapshot = MdfSourcePolicy.ReadStaticContract(
            "Assets/Scripts/Testing/MP/MPTestStateSnapshot.cs");

        Assert.That(playerMigration, Does.Contain("PackedUpgradeState"));
        Assert.That(playerMigration, Does.Contain("TryPackWallUpgradeState"));
        Assert.That(playerMigration, Does.Contain("UnpackWallUpgradeState"));
        Assert.That(fieldMigration, Does.Contain("out int[] levels"));
        Assert.That(fieldMigration, Does.Contain("out int[] upgradeInvestments"));
        Assert.That(fieldMigration, Does.Contain("wall.CurrentLevel"));
        Assert.That(fieldMigration, Does.Contain("wall.InvestedUpgradeGold"));
        Assert.That(hostMigration, Does.Contain("DestructibleWallLevels"));
        Assert.That(hostMigration, Does.Contain("DestructibleWallUpgradeInvestments"));

        int restoreProgressionIndex = fieldMigration.IndexOf(
            "wall.RestoreProgressionAfterMigration",
            StringComparison.Ordinal);
        int restoreHealthIndex = fieldMigration.IndexOf(
            "wall.RestoreHealthAfterMigration",
            StringComparison.Ordinal);
        Assert.That(restoreProgressionIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(restoreHealthIndex, Is.GreaterThan(restoreProgressionIndex),
            "the restored level must establish the correct max HP before applying the saved health ratio");
        Assert.That(fieldMigration, Does.Contain("levels[i].ToString"));
        Assert.That(fieldMigration, Does.Contain("upgradeInvestments[i].ToString"));
        Assert.That(snapshot, Does.Contain("BuildDestructibleWallHealthSnapshot"),
            "multiplayer snapshot comparison must hash the level and investment rows too");
    }

    [Test]
    public void PreparePrewarmsOwnedUnitAndMonsterAssetsBeforePhaseTimerStarts()
    {
        string loadManager = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/Managers/LoadManager.cs");
        string playerManager = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/Managers/PlayerManager.cs");
        string monsterSpawner = MdfSourcePolicy.ReadStaticContract(
            "Assets/Scripts/Game/Monsters/MonsterSpawner.cs");
        string syncAugments = MdfSourcePolicy.ReadStaticContract(
            "Assets/Scripts/Commands/Sync/SyncAugmentsCommand.cs");
        string gameManagers = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/Managers/GameManagers.cs");
        string prewarmer = MdfSourcePolicy.ReadStaticContract(
            "Assets/Scripts/RuntimeAssets/FirstSpawnPresentationPrewarmer.cs");

        int initializeStart = loadManager.IndexOf(
            "public async UniTask InitializeAsync()",
            StringComparison.Ordinal);
        int initializeEnd = loadManager.IndexOf(
            "public UniTask WaitUntilReady()",
            initializeStart,
            StringComparison.Ordinal);
        string initializeSection = loadManager.Substring(initializeStart, initializeEnd - initializeStart);
        Assert.That(initializeSection, Does.Not.Contain("PrewarmUnitPresentationsAsync"),
            "Title/login initialization must remain semantic-data-only");

        int matchWarmupStart = loadManager.IndexOf(
            "public async UniTask PrewarmMatchContentAsync",
            StringComparison.Ordinal);
        int unitPresentationWarmupIndex = loadManager.IndexOf(
            "await PrewarmUnitPresentationsAsync(cancellationToken)",
            matchWarmupStart,
            StringComparison.Ordinal);
        int monsterPresentationWarmupIndex = loadManager.IndexOf(
            "PrewarmMonsterPresentationsAsync",
            unitPresentationWarmupIndex,
            StringComparison.Ordinal);
        int unitPoolWarmupIndex = loadManager.IndexOf(
            "PrewarmUnitNetworkPoolAsync",
            monsterPresentationWarmupIndex,
            StringComparison.Ordinal);
        int matchReadyIndex = loadManager.IndexOf(
            "_matchContentPrewarmComplete = true",
            unitPoolWarmupIndex,
            StringComparison.Ordinal);
        Assert.That(matchWarmupStart, Is.GreaterThanOrEqualTo(0));
        Assert.That(unitPresentationWarmupIndex, Is.GreaterThan(matchWarmupStart));
        Assert.That(monsterPresentationWarmupIndex, Is.GreaterThan(unitPresentationWarmupIndex));
        Assert.That(unitPoolWarmupIndex, Is.GreaterThan(monsterPresentationWarmupIndex));
        Assert.That(matchReadyIndex, Is.GreaterThan(unitPoolWarmupIndex),
            "the lobby ACK must not publish ready before presentations and Runner pools are warm");
        Assert.That(loadManager, Does.Contain("if (!_isUnitDataReady)"),
            "the initializer needs a private data-ready state so its own warmup cannot recurse");
        Assert.That(loadManager, Does.Contain("AssetLoader.LoadAssetAsync<GameObject>(key, _unitPrefabAssets)"));
        Assert.That(loadManager, Does.Contain("FirstSpawnPresentationPrewarmer.WarmPrefabAsync"));
        Assert.That(loadManager, Does.Contain("provider.PrewarmPrefab"));
        Assert.That(loadManager, Does.Contain("UnitNetworkPoolPrewarmState"),
            "all PlayerManager initializers on one peer must share one bounded pool warmup");
        Assert.That(loadManager, Does.Contain("_unitPrefabAssets?.Dispose()"));
        Assert.That(playerManager, Does.Contain("ResolveUnitNetworkPoolTarget(Runner)"));
        Assert.That(playerManager, Does.Contain(
            "await LoadManager.Instance.PrewarmUnitNetworkPoolAsync(Runner, unitPoolTarget)"));

        int monsterLoadIndex = monsterSpawner.IndexOf(
            "AssetLoader.LoadAssetAsync<GameObject>(request.MonsterData.monsterPrefab, _addressableAssets)",
            StringComparison.Ordinal);
        int monsterPresentationIndex = monsterSpawner.IndexOf(
            "FirstSpawnPresentationPrewarmer.WarmPrefabAsync",
            monsterLoadIndex,
            StringComparison.Ordinal);
        int monsterPoolIndex = monsterSpawner.IndexOf(
            "await provider.PrewarmPrefabAsync",
            monsterPresentationIndex,
            StringComparison.Ordinal);
        Assert.That(monsterLoadIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(monsterPresentationIndex, Is.GreaterThan(monsterLoadIndex));
        Assert.That(monsterPoolIndex, Is.GreaterThan(monsterPresentationIndex));
        Assert.That(monsterSpawner, Does.Contain("PrewarmMonsterDataSetAsync"));
        Assert.That(monsterSpawner, Does.Contain("EstimateNormalPrewarmTarget"),
            "repeatable black-magic monsters must use projected player demand, not their sentinel count of one");
        Assert.That(monsterSpawner, Does.Contain("entry.MonsterData.blackMagicCost"),
            "each monster must size its reserve from its own black-magic cost");
        Assert.That(monsterSpawner, Does.Contain("TrimMonsterPoolsForGenerationAsync"),
            "Prepare must trim only measured inactive monster excess before refilling targets");
        Assert.That(monsterSpawner, Does.Contain("waveCountPerBattleForPrefab"),
            "the absolute target must reserve simultaneous base-wave occupancy too");
        Assert.That(monsterSpawner, Does.Contain("_addressableAssets?.Dispose()"));
        Assert.That(syncAugments, Does.Contain("await player.monsterSpawner.PrewarmMonsterDataSetAsync"),
            "each client must warm presented boss candidates before exposing their augment UI");

        int prepareCatalogIndex = gameManagers.IndexOf(
            "player.RefreshAttackMonsterPool(currentRound, schedulePrewarm: false)",
            StringComparison.Ordinal);
        int prepareWarmupIndex = gameManagers.IndexOf(
            "PrewarmAttackMonsterPoolAsync",
            prepareCatalogIndex,
            StringComparison.Ordinal);
        int prepareWaitIndex = gameManagers.IndexOf(
            "await UniTask.WhenAll(monsterPrewarmTasks)",
            prepareWarmupIndex,
            StringComparison.Ordinal);
        int prepareTimerIndex = gameManagers.IndexOf(
            "phaseTimer = TickTimer.CreateFromSeconds",
            prepareWaitIndex,
            StringComparison.Ordinal);
        Assert.That(prepareCatalogIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(prepareWarmupIndex, Is.GreaterThan(prepareCatalogIndex));
        Assert.That(prepareWaitIndex, Is.GreaterThan(prepareWarmupIndex));
        Assert.That(prepareTimerIndex, Is.GreaterThan(prepareWaitIndex),
            "the Prepare countdown may start only after the next attack catalog is warm");
        Assert.That(gameManagers, Does.Contain("StartNextRound.PresentedBosses"),
            "bosses offered during this Prepare must be warm before they can be selected");
        Assert.That(gameManagers, Does.Contain("StartNextRound.NextWave"),
            "wave-only monsters must be explicitly warm before the Prepare timer starts");

        Assert.That(prewarmer, Does.Contain("GetComponentsInChildren<Renderer>(true)"));
        Assert.That(prewarmer, Does.Contain("warmupCamera.enabled = true"));
        Assert.That(prewarmer, Does.Contain("WaitForPipelineRenderFramesAsync"));
        Assert.That(prewarmer, Does.Contain("proxy.AddComponent<SkinnedMeshRenderer>()"));
        Assert.That(prewarmer, Does.Not.Contain("warmupCamera.Render()"),
            "URP must render the warmup camera through its normal SRP pipeline");
        Assert.That(prewarmer, Does.Contain("ShaderUtil.anythingCompiling"));
        Assert.That(prewarmer, Does.Contain("GraphicsSettings.currentRenderPipeline"));
        Assert.That(prewarmer, Does.Contain("\"supportsHDR\""));
        Assert.That(prewarmer, Does.Contain("\"msaaSampleCount\""));
        Assert.That(prewarmer, Does.Contain("GetRenderTextureSupportedMSAASampleCount"));
        Assert.That(prewarmer, Does.Contain("EditorShaderCompileTotalBudgetSeconds"),
            "sequential prefab warmups must share one bounded Editor shader wait budget");
        Assert.That(prewarmer, Does.Contain("[PREWARM] presentation complete"),
            "the build/E2E logs need per-key evidence that the render warmup finished");
        Assert.That(prewarmer, Does.Contain("prefab.GetInstanceID()"),
            "an Addressables unload/reload must create a new warmup generation for the same key");
        Assert.That(prewarmer, Does.Not.Contain("Instantiate(prefab"),
            "presentation warmup must never instantiate networking or gameplay components");
    }

    private static Material[] GetLevelThreeMaterials(WallProgressionData progression)
    {
        Assert.That(progression.TryGetLevel(3, out WallLevelData levelThree), Is.True);
        return levelThree.VisualMaterials;
    }

    private static void AssertWallUpgradeRoundTrip(int level, int investedGold)
    {
        Assert.That(PlayerSnapshotCodec.TryPackWallUpgradeState(level, investedGold, out int packed), Is.True);
        PlayerSnapshotCodec.UnpackWallUpgradeState(packed, out int restoredLevel, out int restoredInvestment);
        Assert.That(restoredLevel, Is.EqualTo(level));
        Assert.That(restoredInvestment, Is.EqualTo(investedGold));
    }
}
#endif
