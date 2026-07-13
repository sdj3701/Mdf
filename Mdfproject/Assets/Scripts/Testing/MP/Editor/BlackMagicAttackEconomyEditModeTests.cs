#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class BlackMagicAttackEconomyEditModeTests
{
    private const string WaveDatabasePath = "Assets/GameData/MonsterWave/WaveDatabase.asset";
    private const string SkeletonStrengthenPath = "Assets/GameData/Augments/Aug_Spawn_Skelton_Normal.asset";
    private const string GolemStrengthenPath = "Assets/GameData/Augments/Aug_Spawn_Golem_Normal.asset";
    private const string BlackMagicMaximumPath = "Assets/GameData/Augments/Aug_Util_BlackMagicMax_Normal.asset";
    private const string PermanentWallCountPath = "Assets/GameData/Augments/Aug_Util_PermanentWallCount_Normal.asset";

    [Test]
    public void AttackCatalogContainsEveryNonBossMonsterExactlyOnceWithPositiveCosts()
    {
        WaveDatabase database = AssetDatabase.LoadAssetAtPath<WaveDatabase>(WaveDatabasePath);
        Assert.That(database, Is.Not.Null, WaveDatabasePath);
        Assert.That(database.attackSequenceMonsterCatalog, Is.Not.Null);

        MonsterData[] catalog = database.attackSequenceMonsterCatalog.ToArray();
        Assert.That(catalog, Has.Length.EqualTo(8));
        Assert.That(catalog.All(monster => monster != null), Is.True);
        Assert.That(catalog.Select(monster => monster.monsterName).Distinct(StringComparer.Ordinal).Count(),
            Is.EqualTo(catalog.Length), "the reusable attack catalog must not contain duplicate monster types");
        Assert.That(catalog.All(monster => !monster.IsBoss), Is.True);
        Assert.That(catalog.All(monster => monster.monsterRank != MonsterRank.Boss), Is.True);
        Assert.That(catalog.All(monster => monster.blackMagicCost > 0), Is.True);

        var expectedNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "Bat", "EvilMage", "Golem", "Orc", "Skeleton", "Slime", "Spider", "TurtleShell"
        };
        CollectionAssert.AreEquivalent(expectedNames, catalog.Select(monster => monster.monsterName));
        var expectedCosts = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["Slime"] = 2,
            ["Bat"] = 2,
            ["Spider"] = 2,
            ["Skeleton"] = 3,
            ["TurtleShell"] = 3,
            ["EvilMage"] = 3,
            ["Orc"] = 4,
            ["Golem"] = 5
        };
        foreach (MonsterData monster in catalog)
        {
            Assert.That(monster.blackMagicCost, Is.EqualTo(expectedCosts[monster.monsterName]), monster.monsterName);
        }
    }

    [Test]
    public void BossesAreExcludedFromReusableCatalogAndHaveNoBlackMagicCost()
    {
        WaveDatabase database = AssetDatabase.LoadAssetAtPath<WaveDatabase>(WaveDatabasePath);
        MonsterData[] bosses = AssetDatabase.FindAssets("t:MonsterData", new[] { "Assets/GameData/Monsters" })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(path => AssetDatabase.LoadAssetAtPath<MonsterData>(path))
            .Where(monster => monster != null && monster.monsterRank == MonsterRank.Boss)
            .ToArray();

        Assert.That(database, Is.Not.Null, WaveDatabasePath);
        Assert.That(bosses, Has.Length.EqualTo(4));
        Assert.That(bosses.All(monster => monster.IsBoss), Is.True);
        Assert.That(bosses.All(monster => monster.blackMagicCost == 0), Is.True);
        Assert.That(database.attackSequenceMonsterCatalog.Intersect(bosses), Is.Empty);
    }

    [Test]
    public void BlackMagicMaximumStartsEqualAndGrowsOnlyByRoundAndPersonalBonus()
    {
        WaveDatabase database = AssetDatabase.LoadAssetAtPath<WaveDatabase>(WaveDatabasePath);
        Assert.That(database, Is.Not.Null, WaveDatabasePath);
        Assert.That(database.baseBlackMagicMaximum, Is.GreaterThan(0));
        Assert.That(database.blackMagicMaximumPerRound, Is.EqualTo(10));

        int roundOne = database.GetBlackMagicMaximumForRound(1, 0);
        int roundTwo = database.GetBlackMagicMaximumForRound(2, 0);
        int personalBonus = 40;
        Assert.That(roundOne, Is.EqualTo(database.baseBlackMagicMaximum));
        Assert.That(roundTwo, Is.EqualTo(roundOne + database.blackMagicMaximumPerRound));
        Assert.That(database.GetBlackMagicMaximumForRound(2, personalBonus), Is.EqualTo(roundTwo + personalBonus));
        Assert.That(database.GetBlackMagicMaximumForRound(0, 0), Is.EqualTo(roundOne),
            "invalid/pre-round values must not lower the initial attack budget");
    }

    [Test]
    public void BlackMagicSequenceKeysAreStableAndSeparateBothAttackPhases()
    {
        int battleOne = PlayerManager.CalculateBlackMagicSequenceId(3, GameManagers.GameState.Battle1);
        int battleTwo = PlayerManager.CalculateBlackMagicSequenceId(3, GameManagers.GameState.Battle2);

        Assert.That(battleOne, Is.EqualTo(PlayerManager.CalculateBlackMagicSequenceId(3, GameManagers.GameState.Battle1)));
        Assert.That(battleTwo, Is.Not.EqualTo(battleOne));
        Assert.That(PlayerManager.CalculateBlackMagicSequenceId(4, GameManagers.GameState.Battle1), Is.Not.EqualTo(battleOne));
        Assert.That(PlayerManager.CalculateBlackMagicSequenceId(3, GameManagers.GameState.Prepare), Is.EqualTo(-1));
        Assert.That(PlayerManager.CalculateBlackMagicMaximum(3, 10, 10, 40), Is.EqualTo(70));
        Assert.That(PlayerManager.CalculateBlackMagicMaximum(0, 10, 2, 0), Is.EqualTo(10));
        FieldInfo fallbackGrowth = typeof(PlayerManager).GetField(
            "FallbackBlackMagicMaximumPerRound",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(fallbackGrowth?.GetRawConstantValue(), Is.EqualTo(10));
    }

    [Test]
    public void FormerNormalSummonAugmentsNowStrengthenTheirExactMonsterType()
    {
        AssertStrengthenAugment(SkeletonStrengthenPath, "Skeleton");
        AssertStrengthenAugment(GolemStrengthenPath, "Golem");

        AugmentData[] allAugments = AssetDatabase.FindAssets("t:AugmentData", new[] { "Assets/GameData/Augments" })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(path => AssetDatabase.LoadAssetAtPath<AugmentData>(path))
            .Where(augment => augment != null)
            .ToArray();
        Assert.That(allAugments.Where(augment => augment.effectType == EffectType.SpawnMonsterOnEnemyField)
                .All(augment => augment.isBossSummon), Is.True,
            "only augment-owned bosses may remain finite summon entitlements");
    }

    [Test]
    public void PersonalMaximumAugmentIncreasesTheNextAttackBudget()
    {
        AugmentData augment = AssetDatabase.LoadAssetAtPath<AugmentData>(BlackMagicMaximumPath);
        Assert.That(augment, Is.Not.Null, BlackMagicMaximumPath);
        Assert.That(augment.effectType, Is.EqualTo(EffectType.IncreaseBlackMagicMaximum));
        Assert.That(augment.targetType, Is.EqualTo(TargetType.Player));
        Assert.That(augment.value, Is.EqualTo(20f));
        Assert.That(augment.isBossSummon, Is.False);

        WaveDatabase database = AssetDatabase.LoadAssetAtPath<WaveDatabase>(WaveDatabasePath);
        int twoSelectionBonus = Mathf.RoundToInt(augment.value) * 2;
        Assert.That(database.GetBlackMagicMaximumForRound(2, twoSelectionBonus),
            Is.EqualTo(database.baseBlackMagicMaximum + 10 + 40),
            "selecting the +20 augment twice must stack to a +40 personal maximum");

        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        AddressableAssetEntry entry = settings?.FindAssetEntry(AssetDatabase.AssetPathToGUID(BlackMagicMaximumPath));
        Assert.That(entry, Is.Not.Null, "the maximum augment must be offered by the runtime augment data loader");
        Assert.That(entry.address, Is.EqualTo("Aug_Util_BlackMagicMax_Normal"));
        Assert.That(entry.labels, Does.Contain("Augment"));
        Assert.That(entry.labels, Does.Contain("mdf-gameplay-data"));
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(
            typeof(AugmentManager), typeof(PlayerManager), "AddBlackMagicMaximumBonus"), Is.True);

        MethodInfo addBonus = typeof(PlayerManager).GetMethod(
            "AddBlackMagicMaximumBonus",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.That(addBonus, Is.Not.Null);
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(addBonus, typeof(PlayerManager), "set_BlackMagicCurrent"), Is.False,
            "a newly selected augment must not refill the in-progress attack sequence");
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(addBonus, typeof(PlayerManager), "set_BlackMagicMaximum"), Is.False,
            "the new personal bonus is included when the next sequence begins");
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(addBonus, typeof(PlayerManager), "get_BlackMagicMaxBonus"), Is.True);
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(addBonus, typeof(PlayerManager), "set_BlackMagicMaxBonus"), Is.True,
            "repeated selections must add to the existing personal bonus rather than replace it");
    }

    [Test]
    public void TypeStrengtheningIsAppliedByTheAuthorityMonsterSpawnPath()
    {
        foreach (string fieldName in new[]
                 {
                     "strengthenedMonsterData", "monsterHealthBonusPercent",
                     "monsterDamageBonusPercent", "monsterMoveSpeedBonusPercent"
                 })
        {
            Assert.That(MdfCompiledCodePolicy.ReferencesField(typeof(MonsterSpawner), typeof(AugmentData), fieldName),
                Is.True, fieldName);
        }
    }

    [Test]
    public void AttackToolkitProvidesTwelveMonsterCardsAndBlackMagicReadout()
    {
        VisualTreeAsset layout = Resources.Load<VisualTreeAsset>("UI/GamePrepare/GamePreparePanels");
        VisualElement tree = layout != null ? layout.CloneTree() : null;

        Assert.That(GamePrepareUIToolkitController.MonsterCardCount, Is.EqualTo(12));
        Assert.That(tree, Is.Not.Null);
        for (var slot = 0; slot < GamePrepareUIToolkitController.MonsterCardCount; slot++)
        {
            Assert.That(tree.Q<VisualElement>($"attack-monster-card-{slot}"), Is.Not.Null, $"monster card {slot}");
            Assert.That(tree.Q<Label>($"attack-monster-name-{slot}"), Is.Null, $"monster name {slot} must be hidden");
            Assert.That(tree.Q<Label>($"attack-monster-count-{slot}"), Is.Not.Null, $"monster cost/count {slot}");
        }
        Assert.That(tree.Q<Label>("attack-black-magic-label"), Is.Not.Null);
        Assert.That(tree.Q<VisualElement>("game-wall-kind-toggle-button"), Is.Not.Null);
        Assert.That(MdfCompiledCodePolicy.ReferencesField(
            typeof(GamePrepareUIToolkitController), typeof(MonsterData), "blackMagicCost"), Is.True);
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(
            typeof(GamePrepareUIToolkitController), typeof(PlayerManager), "get_AppliedBlackMagicCurrent"), Is.True);

        Type monsterCardView = typeof(GamePrepareUIToolkitController).GetNestedType(
            "MonsterCardView",
            BindingFlags.NonPublic);
        Assert.That(monsterCardView, Is.Not.Null);
        Assert.That(MdfCompiledCodePolicy.ContainsStringLiteralFragment(monsterCardView, "\uD751\uB9C8\uB825"), Is.False,
            "individual monster cards must show only the numeric cost");
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(monsterCardView, typeof(int), "ToString"), Is.True);
        Assert.That(MdfCompiledCodePolicy.ContainsStringLiteralFragment(typeof(MonsterSlotUI), "\uD751\uB9C8\uB825"), Is.False,
            "legacy monster cards must also show only the numeric cost");
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(typeof(MonsterSlotUI), typeof(int), "ToString"), Is.True);
    }

    [Test]
    public void PermanentWallAugmentGrantsThreeStackableAuthorityOwnedPlacements()
    {
        AugmentData augment = AssetDatabase.LoadAssetAtPath<AugmentData>(PermanentWallCountPath);
        Assert.That(augment, Is.Not.Null, PermanentWallCountPath);
        Assert.That(augment.effectType, Is.EqualTo(EffectType.GrantPermanentWallPlacementCount));
        Assert.That(augment.targetType, Is.EqualTo(TargetType.Player));
        Assert.That(augment.tier, Is.EqualTo(AugmentTier.Silver));
        Assert.That(augment.value, Is.EqualTo(3f));

        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        AddressableAssetEntry entry = settings?.FindAssetEntry(AssetDatabase.AssetPathToGUID(PermanentWallCountPath));
        Assert.That(entry, Is.Not.Null, "the permanent-wall augment must be offered by the runtime augment loader");
        Assert.That(entry.address, Is.EqualTo("Aug_Util_PermanentWallCount_Normal"));
        Assert.That(entry.labels, Does.Contain("Augment"));
        Assert.That(entry.labels, Does.Contain("mdf-gameplay-data"));
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(
            typeof(AugmentManager), typeof(PlayerManager), "AddPermanentWallPlacementCount"), Is.True);

        MethodInfo addPlacements = typeof(PlayerManager).GetMethod("AddPermanentWallPlacementCount");
        Assert.That(addPlacements, Is.Not.Null);
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(
            addPlacements, typeof(PlayerManager), "get_PermanentWallPlacementCount"), Is.True);
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(
            addPlacements, typeof(PlayerManager), "set_PermanentWallPlacementCount"), Is.True,
            "repeated +3 augment selections must accumulate in the existing stock");
    }

    [Test]
    public void PlayerFieldsStartWithoutRandomInteriorPermanentWalls()
    {
        const string PlayerPrefabPath = "Assets/Prefabs/Player_Root.prefab";
        GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        Assert.That(playerPrefab, Is.Not.Null, PlayerPrefabPath);
        FieldManager field = playerPrefab.GetComponentInChildren<FieldManager>(true);
        Assert.That(field, Is.Not.Null);
        Assert.That(field.initialPermanentWallCount, Is.Zero,
            "the structural border remains deterministic, but random interior permanent walls must not spawn");
    }

    [Test]
    public void PermanentWallStockAndLayoutAreNetworkedAndTransactional()
    {
        const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        foreach (string propertyName in new[]
                 {
                     "PermanentWallPlacementCount", "PermanentWallStockRevision", "PermanentWallLayoutRevision"
                 })
        {
            PropertyInfo property = typeof(PlayerManager).GetProperty(propertyName, Members);
            Assert.That(property, Is.Not.Null, propertyName);
            Assert.That(property.PropertyType, Is.EqualTo(typeof(int)), propertyName);
            Assert.That(property.GetCustomAttributes(false).Any(attribute =>
                attribute.GetType().Name == "NetworkedAttribute" ||
                attribute.GetType().Name == "NetworkedWeavedAttribute"), Is.True, propertyName);
        }

        foreach (string methodName in new[]
                 {
                     "GetPermanentWallPlacementCount", "AddPermanentWallPlacementCount",
                     "TryUsePermanentWallPlacement", "ReturnPermanentWallPlacement",
                     "NotifyPermanentWallLayoutChanged", "RestorePermanentWallStateAfterHostMigration"
                 })
        {
            Assert.That(typeof(PlayerManager).GetMethods(Members).Any(method => method.Name == methodName), Is.True, methodName);
        }

        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(
            typeof(PlaceWallCommand), typeof(PlayerManager), "TryUsePermanentWallPlacement"), Is.True);
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(
            typeof(PlaceWallCommand), typeof(FieldManager), "TryCreatePlayerPlacedPermanentWallAt"), Is.True);
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(
            typeof(PlaceWallCommand), typeof(PlayerManager), "ReturnPermanentWallPlacement"), Is.True);
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(
            typeof(RemoveWallCommand), typeof(FieldManager), "IsPlayerPlacedPermanentWallAt"), Is.True,
            "remove commands must derive the wall kind from authority state");
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(
            typeof(RemoveWallCommand), typeof(FieldManager), "TryRemovePlayerPlacedPermanentWallAt"), Is.True);
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(
            typeof(RemoveWallCommand), typeof(PlayerManager), "ReturnPermanentWallPlacement"), Is.True);
        Assert.That(typeof(RemoveWallCommand).GetProperty("Kind", Members), Is.Null,
            "clients must not choose which stock a removal refunds");
    }

    [Test]
    public void PlaceWallKindRoundTripsThroughCommandSerializationWhileRemoveStaysAuthorityDerived()
    {
        const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic;
        var processor = new CommandProcessor();
        try
        {
            MethodInfo serialize = typeof(CommandProcessor).GetMethod("SerializeCommand", Members);
            MethodInfo deserialize = typeof(CommandProcessor).GetMethod("DeserializeCommand", Members);
            Assert.That(serialize, Is.Not.Null);
            Assert.That(deserialize, Is.Not.Null);

            var position = new Vector3Int(2, 3, 0);
            var place = new PlaceWallCommand(7, position, WallPlacementKind.Permanent);
            var placePayload = ((CommandType, int[], string[], Vector3[]))serialize.Invoke(
                processor,
                new object[] { place });
            Assert.That(placePayload.Item1, Is.EqualTo(CommandType.PlaceWall));
            CollectionAssert.AreEqual(new[] { 7, (int)WallPlacementKind.Permanent }, placePayload.Item2);
            Assert.That(placePayload.Item4, Has.Length.EqualTo(1));
            Assert.That(placePayload.Item4[0], Is.EqualTo((Vector3)position));

            var placeTask = (UniTask<ICommand>)deserialize.Invoke(
                processor,
                new object[]
                {
                    placePayload.Item1, placePayload.Item2, placePayload.Item3, placePayload.Item4,
                    CancellationToken.None
                });
            var restoredPlace = placeTask.GetAwaiter().GetResult() as PlaceWallCommand;
            Assert.That(restoredPlace, Is.Not.Null);
            Assert.That(restoredPlace.Kind, Is.EqualTo(WallPlacementKind.Permanent));
            Assert.That(restoredPlace.Position, Is.EqualTo(position));

            var remove = new RemoveWallCommand(7, position);
            var removePayload = ((CommandType, int[], string[], Vector3[]))serialize.Invoke(
                processor,
                new object[] { remove });
            Assert.That(removePayload.Item1, Is.EqualTo(CommandType.RemoveWall));
            CollectionAssert.AreEqual(new[] { 7 }, removePayload.Item2,
                "remove payloads must not contain a client-selected wall kind");
        }
        finally
        {
            processor.CancelPendingCommands();
        }
    }

    [Test]
    public void AuthoritySpawnCommandObservesAndTransactionsBlackMagicRevision()
    {
        const BindingFlags Members = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        PropertyInfo observedRevision = typeof(BattleSpawnMonsterCommand).GetProperty("ObservedBlackMagicRevision", Members);
        Assert.That(observedRevision, Is.Not.Null);
        Assert.That(observedRevision.PropertyType, Is.EqualTo(typeof(int)));

        foreach (string propertyName in new[]
                 {
                     "BlackMagicCurrent", "BlackMagicMaximum", "BlackMagicMaxBonus", "BlackMagicRevision", "BlackMagicSequenceId"
                 })
        {
            PropertyInfo property = typeof(PlayerManager).GetProperty(propertyName, Members);
            Assert.That(property, Is.Not.Null, propertyName);
            Assert.That(property.PropertyType, Is.EqualTo(typeof(int)), propertyName);
            Assert.That(property.GetCustomAttributes(false).Any(attribute =>
                attribute.GetType().Name == "NetworkedAttribute" ||
                attribute.GetType().Name == "NetworkedWeavedAttribute"), Is.True, propertyName);
        }

        foreach (string methodName in new[]
                 {
                     "TryReserveBattleSpawnResource", "CommitBattleSpawnReservation", "TryRefundBattleSpawnReservation"
                 })
        {
            Assert.That(typeof(PlayerManager).GetMethods(Members).Any(method => method.Name == methodName), Is.True, methodName);
            Assert.That(MdfCompiledCodePolicy.ReferencesMethod(typeof(BattleSpawnMonsterCommand), typeof(PlayerManager), methodName),
                Is.True, methodName);
        }
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(
            typeof(BattleSpawnMonsterCommand), typeof(PlayerManager), "get_BlackMagicRevision"), Is.True);
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(
            typeof(GameManagers), typeof(PlayerManager), "BeginAttackSequenceBlackMagic"), Is.True,
            "only the authority battle-start path should refill the per-sequence resource");
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(
            typeof(BattleDecisionPolicy), typeof(PlayerManager), "CanAffordAttackMonster"), Is.True,
            "AI and HumanBot decisions must use the same authority-owned affordability rule");
    }

    private static void AssertStrengthenAugment(string path, string expectedMonsterName)
    {
        AugmentData augment = AssetDatabase.LoadAssetAtPath<AugmentData>(path);
        Assert.That(augment, Is.Not.Null, path);
        Assert.That(augment.effectType, Is.EqualTo(EffectType.StrengthenMonsterType));
        Assert.That(augment.isBossSummon, Is.False);
        Assert.That(augment.bossMonsterData, Is.Null);
        Assert.That(augment.strengthenedMonsterData, Is.Not.Null);
        Assert.That(augment.strengthenedMonsterData.monsterName, Is.EqualTo(expectedMonsterName));
        Assert.That(augment.monsterSpawnEntries, Is.Empty);
        Assert.That(augment.monsterHealthBonusPercent + augment.monsterDamageBonusPercent + augment.monsterMoveSpeedBonusPercent,
            Is.GreaterThan(0f), "a type-strengthening augment must change at least one combat stat");
    }
}
#endif
