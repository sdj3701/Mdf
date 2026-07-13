#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

public sealed class MPTestHarnessEditModeTests
{
    [Test]
    public void CommandLineParserReadsHarnessOptions()
    {
        var options = MPTestCommandLine.Parse(new[]
        {
            "MDF.exe",
            "--mpTest",
            "--mpRole", "client",
            "--mpSession", "phase8-session",
            "--mpMaxPlayers", "4",
            "--mpScene", "Game",
            "--mpAutoStart",
            "--mpLoadGame",
            "--mpExitAfterSeconds", "7",
            "--mpAutomationPort", "19001",
            "--mpAutomationToken", "automation-secret",
            "--mpConnectionToken", "connection-secret",
            "--mpCase", "phase8",
            "--mpArtifactDir", "artifacts/mp/phase8",
            "--mpSeed", "1234",
            "--mpScenario", "prepare_smoke",
            "--mpDisableAiFill",
            "--mpFreezeGameFlow",
            "--mpHumanBot",
            "--mpBotPersona", "maze",
            "--mpBotSeed", "222",
            "--mpBotDurationSeconds", "33",
            "--mpBotStopAtRound", "2",
            "--mpBotMaxCommands", "9",
            "--mpBotPrepareAugmentOnly",
            "--mpBotPreferScrollAugment",
            "--mpBotRecordJournal", "artifacts/mp/phase8/bot.jsonl"
        });

        Assert.That(options.Enabled, Is.True);
        Assert.That(options.SafeRole, Is.EqualTo("client"));
        Assert.That(options.Session, Is.EqualTo("phase8-session"));
        Assert.That(options.MaxPlayers, Is.EqualTo(4));
        Assert.That(options.Scene, Is.EqualTo("Game"));
        Assert.That(options.AutoStart, Is.True);
        Assert.That(options.LoadGame, Is.True);
        Assert.That(options.ExitAfterSeconds, Is.EqualTo(7));
        Assert.That(options.AutomationPort, Is.EqualTo(19001));
        Assert.That(options.AutomationTokenHash, Is.Not.EqualTo("automation-secret"));
        Assert.That(options.ConnectionTokenHash, Is.Not.EqualTo("connection-secret"));
        Assert.That(options.CaseName, Is.EqualTo("phase8"));
        Assert.That(options.Seed, Is.EqualTo(1234));
        Assert.That(options.Scenario, Is.EqualTo("prepare_smoke"));
        Assert.That(options.DisableAiFill, Is.True);
        Assert.That(options.FreezeGameFlow, Is.True);
        Assert.That(options.HumanBot, Is.True);
        Assert.That(options.BotPersona, Is.EqualTo("maze"));
        Assert.That(options.BotSeed, Is.EqualTo(222));
        Assert.That(options.BotDurationSeconds, Is.EqualTo(33));
        Assert.That(options.BotStopAtRound, Is.EqualTo(2));
        Assert.That(options.BotMaxCommands, Is.EqualTo(9));
        Assert.That(options.BotPrepareAugmentOnly, Is.True);
        Assert.That(options.BotPreferScrollAugment, Is.True);
        Assert.That(options.BotSkipPrepare, Is.False);
        Assert.That(options.BotRecordJournal, Is.EqualTo("artifacts/mp/phase8/bot.jsonl"));
    }

    [Test]
    public void RankingUiSplitsFourPlayersEvenlyAcrossSides()
    {
        Assert.That(RankingUIController.GetLeftSideSlotCountForDisplay(4), Is.EqualTo(2));
        Assert.That(RankingUIController.ShouldPlaceDisplayIndexOnLeft(0, 4), Is.True);
        Assert.That(RankingUIController.ShouldPlaceDisplayIndexOnLeft(1, 4), Is.True);
        Assert.That(RankingUIController.ShouldPlaceDisplayIndexOnLeft(2, 4), Is.False);
        Assert.That(RankingUIController.ShouldPlaceDisplayIndexOnLeft(3, 4), Is.False);

        Assert.That(RankingUIController.GetLeftSideSlotCountForDisplay(3), Is.EqualTo(2));
        Assert.That(RankingUIController.GetLeftSideSlotCountForDisplay(2), Is.EqualTo(1));
        Assert.That(RankingUIController.GetLeftSideSlotCountForDisplay(1), Is.EqualTo(1));
        Assert.That(RankingUIController.GetLeftSideSlotCountForDisplay(0), Is.EqualTo(0));
        Assert.That(RankingUIController.GetTopRightReserveCount(4), Is.EqualTo(2));
        Assert.That(RankingUIController.GetTopRightReserveCount(3), Is.EqualTo(1));
        Assert.That(RankingUIController.GetTopRightReserveCount(2), Is.EqualTo(0));
    }

    [Test]
    public void RankingUiToolkitLayoutProvidesFixedSelfOpponentAndReserveSlots()
    {
        string registrySource = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/Managers/GameManagers.PlayerRegistry.cs");
        var layout = Resources.Load<VisualTreeAsset>("UI/PlayerRanking/PlayerRankingPanel");
        var style = Resources.Load<StyleSheet>("UI/PlayerRanking/PlayerRankingPanelStyles");
        var tree = layout != null ? layout.CloneTree() : null;

        Assert.That(layout, Is.Not.Null);
        Assert.That(style, Is.Not.Null);
        Assert.That(tree?.Q<VisualElement>("ranking-strip"), Is.Not.Null);
        Assert.That(tree?.Q<VisualElement>("ranking-self-card"), Is.Not.Null);
        Assert.That(tree?.Q<VisualElement>("ranking-opponent-card"), Is.Not.Null);
        Assert.That(tree?.Q<VisualElement>("ranking-reserve-card-0"), Is.Not.Null);
        Assert.That(tree?.Q<VisualElement>("ranking-reserve-card-1"), Is.Not.Null);
        Assert.That(tree?.Q<VisualElement>("ranking-self-avatar"), Is.Not.Null);
        Assert.That(tree?.Q<VisualElement>("ranking-opponent-avatar"), Is.Not.Null);
        Assert.That(tree?.Q<Label>("ranking-self-name"), Is.Not.Null);
        Assert.That(tree?.Q<Label>("ranking-opponent-name"), Is.Not.Null);
        Assert.That(tree?.Q<VisualElement>("ranking-self-battle-role-icon"), Is.Not.Null);
        Assert.That(tree?.Q<VisualElement>("ranking-opponent-battle-role-icon"), Is.Not.Null);
        Assert.That(tree?.Q<VisualElement>("ranking-reserve-0-battle-role-icon"), Is.Not.Null);
        Assert.That(tree?.Q<VisualElement>("ranking-reserve-1-battle-role-icon"), Is.Not.Null);
        const BindingFlags rankingMembers = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        Assert.That(typeof(RankingUIController).GetField("toolkitDocument", rankingMembers)?.FieldType, Is.EqualTo(typeof(UIDocument)));
        Assert.That(typeof(RankingUIController).GetField("PanelSortingOrder", rankingMembers)?.GetRawConstantValue(), Is.EqualTo(260));
        Assert.That(typeof(RankingUIController).GetField("lastToolkitCardClickFrame", rankingMembers), Is.Not.Null);
        foreach (string methodName in new[]
                 {
                     "ResolveOpponent", "GetHealthFillPercentForDisplay", "ShouldUseAttackBattleRoleIconForDisplay",
                     "SetPickingModeRecursive", "ShouldUseAttackModeCamera", "HandleToolkitPointerInput",
                     "FindToolkitCardAtScreenPosition", "FindToolkitCardAtPanelPosition", "ToPanelScreenPosition"
                 })
        {
            Assert.That(typeof(RankingUIController).GetMethods(rankingMembers).Any(method => method.Name == methodName), Is.True, methodName);
        }
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(typeof(RankingUIController), typeof(CameraManager), "MoveToPlayerField"), Is.True);
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(typeof(RankingUIController), typeof(CameraManager), "ReturnToOwnField"), Is.True);
        MethodInfo panelConversion = typeof(RankingUIController).GetMethod("ToPanelScreenPosition", rankingMembers);
        var converted = (Vector2)panelConversion.Invoke(null, new object[] { new Vector2(17f, 23f) });
        Assert.That(converted, Is.EqualTo(new Vector2(17f, Screen.height - 23f)));
        string styleSource = MdfSourcePolicy.ReadStaticContract("Assets/Resources/UI/PlayerRanking/PlayerRankingPanelStyles.uss");
        Assert.That(styleSource, Does.Contain("left: 104px;"));
        Assert.That(styleSource, Does.Contain("left: 122px;"));
        Assert.That(styleSource, Does.Contain("left: 332px;"));
        Assert.That(styleSource, Does.Contain("left: 878px;"));
        Assert.That(styleSource, Does.Contain("left: 1078px;"));
        Assert.That(styleSource, Does.Contain("PlayerStatus/battle.png"));
        Assert.That(styleSource, Does.Contain("PlayerStatus/shield.png"));
        Assert.That(registrySource, Does.Contain("public bool TryGetBattleRoleSnapshot"));
        Assert.That(registrySource, Does.Contain("TryGetMatchFirstAttackerSnapshot(playerId, out int firstAttackerId)"));
        Assert.That(registrySource, Does.Contain("currentState == GameState.Battle1 ? isFirstAttacker : !isFirstAttacker"));
    }

    [Test]
    public void HostMigrationRankingClickRebindsByDurablePlayerId()
    {
        const BindingFlags members = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        MethodInfo moveByPlayerId = typeof(CameraManager).GetMethod(
            "MoveToPlayerField",
            members,
            null,
            new[] { typeof(int), typeof(bool) },
            null);
        Assert.That(moveByPlayerId, Is.Not.Null, "Camera navigation must accept durable playerId.");
        Assert.That(typeof(CameraManager).GetProperty("OwnPlayerId", members), Is.Not.Null);
        Assert.That(typeof(CameraManager).GetProperty("CurrentViewingPlayerId", members), Is.Not.Null);
        Assert.That(typeof(CameraManager).GetProperty("IsTransitioning", members), Is.Not.Null);

        MethodInfo legacyClick = typeof(PlayerRankSlot).GetMethod("OnSlotClicked", members);
        MethodInfo toolkitClick = typeof(RankingUIController).GetMethod("OnToolkitCardClicked", members);
        MethodInfo playersReady = typeof(RankingUIController).GetMethod("OnPlayersDataReady", members);
        Assert.That(legacyClick, Is.Not.Null);
        Assert.That(toolkitClick, Is.Not.Null);
        Assert.That(playersReady, Is.Not.Null);
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(legacyClick, typeof(GameManagers), "GetPlayer"), Is.True);
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(legacyClick, typeof(CameraManager), "MoveToPlayerField"), Is.True);
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(toolkitClick, typeof(GameManagers), "GetPlayer"), Is.True);
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(toolkitClick, typeof(CameraManager), "MoveToPlayerField"), Is.True);
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(
            playersReady,
            typeof(RankingUIController),
            "RebindLegacyPlayersFromActiveRegistry"), Is.True);
        Assert.That(typeof(PlayerRankSlot).GetProperty("TrackedPlayerId", members), Is.Not.Null);

        System.Type toolkitCard = typeof(RankingUIController).GetNestedType(
            "RankingCardView",
            BindingFlags.Public | BindingFlags.NonPublic);
        Assert.That(toolkitCard?.GetProperty("TrackedPlayerId", members), Is.Not.Null);
        Assert.That(toolkitCard?.GetProperty("TrackedPlayer", members), Is.Null,
            "Toolkit cards must not retain a replaceable PlayerManager object.");

        MethodInfo presentationRebind = typeof(GameManagers).GetMethod(
            "RebindLocalPresentationAfterPlayerRegistryChanged",
            members);
        MethodInfo migrationRestore = typeof(GameManagers).GetMethod("RestoreAfterHostMigration", members);
        Assert.That(presentationRebind, Is.Not.Null);
        Assert.That(migrationRestore, Is.Not.Null);
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(
            presentationRebind,
            typeof(CameraManager),
            "RebindAfterPlayerRegistryChanged"), Is.True);
        Assert.That(MdfCompiledCodePolicy.ReferencesField(
            presentationRebind,
            typeof(GameManagers),
            "OnPlayersDataReady"), Is.True);
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(
            migrationRestore,
            typeof(GameManagers),
            "RebindLocalPresentationAfterPlayerRegistryChanged"), Is.True);
    }

    [Test]
    public void RankingUiHealthFillIsClampedToPlayerMax()
    {
        Assert.That(RankingUIController.GetHealthFillPercentForDisplay(-5, 80), Is.EqualTo(0f));
        Assert.That(RankingUIController.GetHealthFillPercentForDisplay(40, 80), Is.EqualTo(0.5f));
        Assert.That(RankingUIController.GetHealthFillPercentForDisplay(80, 80), Is.EqualTo(1f));
        Assert.That(RankingUIController.GetHealthFillPercentForDisplay(150, 80), Is.EqualTo(1f));
        Assert.That(RankingUIController.GetHealthFillPercentForDisplay(50, 0), Is.EqualTo(0f));
    }

    [Test]
    public void RankingUiBattleRoleIconOnlyUsesSwordForActiveAttackers()
    {
        Assert.That(RankingUIController.ShouldUseAttackBattleRoleIconForDisplay(true, true), Is.True);
        Assert.That(RankingUIController.ShouldUseAttackBattleRoleIconForDisplay(true, false), Is.False);
        Assert.That(RankingUIController.ShouldUseAttackBattleRoleIconForDisplay(false, true), Is.False);
        Assert.That(RankingUIController.ShouldUseAttackBattleRoleIconForDisplay(false, false), Is.False);
    }

    [Test]
    public void GameSceneCameraStartupUsesStableCameraManagerBaseline()
    {
        WithOpenScene("Assets/Scenes/03_Game.unity", scene =>
        {
            Assert.That(FindSceneObject(scene, "Main Camera"), Is.Not.Null);
            CameraManager manager = FindSceneObject(scene, "CameraManager")?.GetComponent<CameraManager>();
            Assert.That(manager, Is.Not.Null);
            const BindingFlags members = BindingFlags.Instance | BindingFlags.NonPublic;
            MethodInfo capture = typeof(CameraManager).GetMethod("CaptureSceneCameraPoseIfNeeded", members);
            Assert.That(capture, Is.Not.Null);
            capture.Invoke(manager, null);
            Assert.That(typeof(CameraManager).GetField("_hasSceneCameraPose", members)?.GetValue(manager), Is.True);
            Assert.That((Vector3)typeof(CameraManager).GetField("_sceneCameraPosition", members)?.GetValue(manager),
                Is.EqualTo(FindSceneObject(scene, "Main Camera").transform.position));
        });
        Assert.That(typeof(GetPlayerCamera).GetMethod("HasCameraManagerInScene", BindingFlags.Static | BindingFlags.NonPublic), Is.Not.Null);
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(typeof(GetPlayerCamera), typeof(UnityEngine.Object), "FindObjectOfType"), Is.True);
    }

    [Test]
    public void MPTestLoggerEmitsStablePrefixAndFields()
    {
        try
        {
            MPTestLogger.EditorTestLoggingEnabled = true;
            LogAssert.Expect(LogType.Log, new Regex(@"\[MPTEST\].*phase=phase8_log_smoke.*result=pass"));
            MPTestLogger.Pass("phase8_log_smoke");
        }
        finally
        {
            MPTestLogger.EditorTestLoggingEnabled = false;
        }
    }

    [Test]
    public void SnapshotSerializesSchemaFields()
    {
        var snapshot = BuildSnapshot("host");
        string json = MPTestStateSnapshot.ToJson(snapshot);
        var restored = JsonConvert.DeserializeObject<MPTestStateSnapshot.Snapshot>(json);

        Assert.That(restored.Version, Is.EqualTo(1));
        Assert.That(restored.Scene, Is.EqualTo("Game"));
        Assert.That(restored.Game.CurrentState, Is.EqualTo("Prepare"));
        Assert.That(restored.Game.BattlePhase, Is.EqualTo("None"));
        Assert.That(restored.Players.Length, Is.EqualTo(1));
        Assert.That(restored.Players[0].Shop.ItemsHash, Does.StartWith("sha256:"));
        Assert.That(restored.Players[0].ManualSkillReadyHash, Does.StartWith("sha256:"));
        Assert.That(restored.Players[0].Monsters.TypeHash, Does.StartWith("sha256:"));
        Assert.That(restored.Effects.ActiveBuffHash, Does.StartWith("sha256:"));
        Assert.That(restored.Commands.ActivateSkillSeq, Is.EqualTo(0));
    }

    [Test]
    public void SnapshotComparisonIgnoresPeerLocalFields()
    {
        var host = BuildSnapshot("host");
        var client = BuildSnapshot("client");
        client.Runner.IsServer = false;
        client.Runner.IsClient = true;
        client.Runner.Tick = host.Runner.Tick + 30;
        client.Runner.LocalPlayerRef = "PlayerRef:2";
        client.Players[0].PlayerRef = "PlayerRef:2";

        var result = MPTestAssertions.CompareDurable(host, client);

        Assert.That(result.Success, Is.True, string.Join("\n", result.Errors));
    }

    [Test]
    public void MPTestSceneAliasesNormalizeLegacyAndCanonicalNames()
    {
        Assert.That(MPTestSceneAliases.Normalize("Title"), Is.EqualTo("00_Title"));
        Assert.That(MPTestSceneAliases.Normalize("MatchingLobby"), Is.EqualTo("01_MatchingLobby"));
        Assert.That(MPTestSceneAliases.Normalize("TestMatching"), Is.EqualTo("01_MatchingLobby"));
        Assert.That(MPTestSceneAliases.Normalize("JoinLobby"), Is.EqualTo("02_JoinLobby"));
        Assert.That(MPTestSceneAliases.Normalize("Game"), Is.EqualTo("03_Game"));

        Assert.That(MPTestSceneAliases.Matches("03_Game", "Game"), Is.True);
        Assert.That(MPTestSceneAliases.Matches("Game", "03_Game"), Is.True);
        Assert.That(MPTestSceneAliases.Matches("TestMatching", "01_MatchingLobby"), Is.True);
    }

    [Test]
    public void MatchingLobbySceneKeepsRuntimeUiToolkitInput()
    {
        WithOpenScene("Assets/Scenes/01_MatchingLobby.unity", scene =>
        {
            GameObject eventSystem = FindSceneObject(scene, "EventSystem");
            Assert.That(eventSystem, Is.Not.Null);
            Assert.That(eventSystem.GetComponents<Component>().Any(component => component.GetType().Name == "InputSystemUIInputModule"), Is.True);

            GameObject toolkitRoot = FindSceneObject(scene, "TestMatching UI Toolkit");
            Assert.That(toolkitRoot, Is.Not.Null);
            Assert.That(toolkitRoot.GetComponent<UIDocument>()?.panelSettings, Is.Not.Null);
        });
    }

    [Test]
    public void LobbyToolkitKeepsBackgroundImagesButRemovesLeftMenus()
    {
        VisualTreeAsset matchingLayout = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/UI/TestMatching/TestMatching.uxml");
        VisualTreeAsset joinLayout = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/UI/JoinLobby/JoinLobby.uxml");
        TemplateContainer matchingTree = matchingLayout?.CloneTree();
        TemplateContainer joinTree = joinLayout?.CloneTree();

        Assert.That(matchingLayout, Is.Not.Null);
        Assert.That(joinLayout, Is.Not.Null);
        Assert.That(matchingTree.Q<VisualElement>("leftMenu"), Is.Null);
        Assert.That(joinTree.Q<VisualElement>("leftMenu"), Is.Null);
        Assert.That(matchingTree.Q<VisualElement>("leftMenuImageCover"), Is.Not.Null);
        Assert.That(joinTree.Q<VisualElement>("leftMenuImageCover"), Is.Not.Null);
        Assert.That(matchingTree.Q<VisualElement>("leftMenuImageCover").ClassListContains("tm-left-menu-image-cover"), Is.True);
        Assert.That(joinTree.Q<VisualElement>("leftMenuImageCover").ClassListContains("jl-left-menu-image-cover"), Is.True);
        AssertDependenciesContainFileNames("Assets/UI/TestMatching/TestMatching.uss", "bg_test_matching_full.png");
        AssertDependenciesContainFileNames(
            "Assets/UI/JoinLobby/JoinLobby.uss",
            "bg_join_lobby_full.png", "player_portrait_0.png", "player_portrait_1.png", "player_portrait_2.png");
    }

    [Test]
    public void NetworkManagerGuardsDuplicateSessionStartRequests()
    {
        string networkSource = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/Network/NetworkManager.cs");
        string matchingSource = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/UI/TestMatching/TestMatchingUIToolkitController.cs");

        int startMethod = networkSource.IndexOf("public async void StartGame", System.StringComparison.Ordinal);
        int duplicateGuard = networkSource.IndexOf("if (_startGameInProgress)", startMethod, System.StringComparison.Ordinal);
        int armGuard = networkSource.IndexOf("_startGameInProgress = true;", startMethod, System.StringComparison.Ordinal);
        int fusionStart = networkSource.IndexOf("await _runner.StartGame", startMethod, System.StringComparison.Ordinal);
        int playerJoined = networkSource.IndexOf("public void OnPlayerJoined", System.StringComparison.Ordinal);
        int clearOnJoined = networkSource.IndexOf("_startGameInProgress = false;", playerJoined, System.StringComparison.Ordinal);
        int shutdown = networkSource.IndexOf("public void OnShutdown", System.StringComparison.Ordinal);
        int clearOnShutdown = networkSource.IndexOf("_startGameInProgress = false;", shutdown, System.StringComparison.Ordinal);

        Assert.That(networkSource, Does.Contain("private bool _startGameInProgress"));
        Assert.That(networkSource, Does.Contain("StartGame ignored because another session start is already in progress"));
        Assert.That(duplicateGuard, Is.GreaterThan(startMethod));
        Assert.That(duplicateGuard, Is.LessThan(armGuard));
        Assert.That(armGuard, Is.LessThan(fusionStart));
        Assert.That(clearOnJoined, Is.GreaterThan(playerJoined));
        Assert.That(clearOnShutdown, Is.GreaterThan(shutdown));
        Assert.That(matchingSource, Does.Contain("networkManager.IsNetworkUiBlocked"));
    }

    [Test]
    public void GameSceneKeepsRuntimeRoots()
    {
        WithOpenScene("Assets/Scenes/03_Game.unity", scene =>
        {
            Assert.That(FindSceneObject(scene, "Main Camera"), Is.Not.Null);
            GameObject eventSystem = FindSceneObject(scene, "EventSystem");
            Assert.That(eventSystem, Is.Not.Null);
            Assert.That(eventSystem.GetComponents<Component>().Any(component => component.GetType().Name == "InputSystemUIInputModule"), Is.True);
            Assert.That(FindSceneObject(scene, "GameInitialrizer"), Is.Not.Null);
            Assert.That(FindSceneObject(scene, "Addressable Manager")?.GetComponent<AddressablesManager>(), Is.Not.Null);
            Assert.That(FindSceneObject(scene, "VfxManager")?.GetComponent<VfxPoolManager>(), Is.Not.Null);
        });
    }

    [Test]
    public void TitleNetworkManagerKeepsPlayerPrefabReference()
    {
        GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player_Root.prefab");
        Assert.That(playerPrefab, Is.Not.Null);
        Assert.That(playerPrefab.name, Is.EqualTo("Player_Root"));
        Assert.That(FindChildByName(playerPrefab, "MonsterSpawner")?.GetComponent<MonsterSpawner>(), Is.Not.Null);
        Assert.That(FindChildByName(playerPrefab, "FieldManager")?.GetComponent<FieldManager>(), Is.Not.Null);

        WithOpenScene("Assets/Scenes/00_Title.unity", scene =>
        {
            NetworkManager networkManager = FindSceneObject(scene, "NetworkManager")?.GetComponent<NetworkManager>();
            Assert.That(networkManager, Is.Not.Null);
            var serializedManager = new SerializedObject(networkManager);
            Object configuredPrefab = serializedManager.FindProperty("_playerPrefab")?.objectReferenceValue;
            Assert.That(configuredPrefab, Is.Not.Null);
            Assert.That(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(configuredPrefab)),
                Is.EqualTo(AssetDatabase.AssetPathToGUID("Assets/Prefabs/Player_Root.prefab")));
        });
    }

    [Test]
    public void BasicAssertionComparesScenesAliasAware()
    {
        var snapshot = BuildSnapshot("host");
        snapshot.Scene = "03_Game";

        var canonicalActual = MPTestAssertions.AssertBasic(snapshot, expectedPlayers: 1, expectedScene: "Game", expectedGameState: "Prepare");
        Assert.That(canonicalActual.Success, Is.True, string.Join("\n", canonicalActual.Errors));

        snapshot.Scene = "Game";
        var legacyActual = MPTestAssertions.AssertBasic(snapshot, expectedPlayers: 1, expectedScene: "03_Game", expectedGameState: "Prepare");
        Assert.That(legacyActual.Success, Is.True, string.Join("\n", legacyActual.Errors));

        snapshot.Scene = "TestMatching";
        snapshot.Game.HasGameManagers = true;
        var matchingLobby = MPTestAssertions.AssertBasic(snapshot, expectedPlayers: 1, expectedScene: "01_MatchingLobby");
        Assert.That(matchingLobby.Success, Is.True, string.Join("\n", matchingLobby.Errors));
    }

    [Test]
    public void SnapshotComparisonComparesScenesAliasAware()
    {
        var host = BuildSnapshot("host");
        var client = BuildSnapshot("client");
        host.Scene = "03_Game";
        client.Scene = "Game";

        var result = MPTestAssertions.CompareDurable(host, client);

        Assert.That(result.Success, Is.True, string.Join("\n", result.Errors));
    }

    [Test]
    public void SnapshotComparisonRequiresBattleHashesWhenInBattle()
    {
        AssertComparisonFails((host, client) =>
        {
            string hash = MPTestStateSnapshot.HashStableString("battle");
            host.Game.CurrentState = "Battle1";
            client.Game.CurrentState = "Battle1";
            host.Game.BattlePhase = "Battle1";
            client.Game.BattlePhase = "Battle1";
            host.Game.BattleOpponentsHash = hash;
            host.Game.MatchFirstAttackerHash = hash;
            host.Game.BattleActiveHash = hash;
            client.Game.BattleOpponentsHash = "unknown";
            client.Game.MatchFirstAttackerHash = "unknown";
            client.Game.BattleActiveHash = "unknown";
        }, "game.battleOpponentsHash");
    }

    [Test]
    public void SnapshotComparisonRequiresBattleHashesEvenWhenBothPeersMissThem()
    {
        AssertComparisonFails((host, client) =>
        {
            host.Game.CurrentState = "Battle1";
            client.Game.CurrentState = "Battle1";
            host.Game.BattlePhase = "Battle1";
            client.Game.BattlePhase = "Battle1";
            host.Game.BattleOpponentsHash = "unknown";
            client.Game.BattleOpponentsHash = "unknown";
            host.Game.MatchFirstAttackerHash = "unknown";
            client.Game.MatchFirstAttackerHash = "unknown";
            host.Game.BattleActiveHash = "unknown";
            client.Game.BattleActiveHash = "unknown";
        }, "game.battleOpponentsHash");
    }

    [Test]
    public void SnapshotComparisonRequiresSurvivorHashesWhenCountsAreNonZero()
    {
        AssertComparisonFails((host, client) =>
        {
            string hash = MPTestStateSnapshot.HashStableString("survivor-boss");
            host.Game.SurvivorBossPendingCount = 1;
            client.Game.SurvivorBossPendingCount = 1;
            host.Game.SurvivorBossPendingHash = hash;
            client.Game.SurvivorBossPendingHash = "unknown";
        }, "game.survivorBossPendingHash");
    }

    [Test]
    public void SnapshotComparisonRequiresSurvivorHashesEvenWhenBothPeersMissThem()
    {
        AssertComparisonFails((host, client) =>
        {
            host.Game.SurvivorBossPendingCount = 1;
            client.Game.SurvivorBossPendingCount = 1;
            host.Game.SurvivorBossPendingHash = "unknown";
            client.Game.SurvivorBossPendingHash = "unknown";
        }, "game.survivorBossPendingHash");

        AssertComparisonFails((host, client) =>
        {
            host.Game.SurvivorBossAssignmentCount = 1;
            client.Game.SurvivorBossAssignmentCount = 1;
            host.Game.SurvivorBossAssignmentHash = "unknown";
            client.Game.SurvivorBossAssignmentHash = "unknown";
        }, "game.survivorBossAssignmentHash");
    }

    [Test]
    public void SnapshotComparisonRequiresCommandNameWhenCommandCountersAdvance()
    {
        AssertComparisonFails((host, client) =>
        {
            host.Commands.AcceptedBattleCommandSeq = 1;
            client.Commands.AcceptedBattleCommandSeq = 1;
            host.Commands.LastCommand = CommandType.BattleSpawnMonster.ToString();
            client.Commands.LastCommand = "unknown";
        }, "commands.lastCommand");
    }

    [Test]
    public void SnapshotComparisonRequiresCommandNameEvenWhenBothPeersMissIt()
    {
        AssertComparisonFails((host, client) =>
        {
            host.Commands.AcceptedBattleCommandSeq = 1;
            client.Commands.AcceptedBattleCommandSeq = 1;
            host.Commands.LastCommand = "unknown";
            client.Commands.LastCommand = "unknown";
        }, "commands.lastCommand");
    }

    [Test]
    public void SnapshotComparisonRejectsOneSidedRequiredBattleHashes()
    {
        AssertComparisonFails((host, client) =>
        {
            client.Players[0].AttackMonsterPoolHash = "unknown";
        }, "player.0.attackMonsterPoolHash");

        AssertComparisonFails((host, client) =>
        {
            client.Players[0].OwnedScrollsHash = "unknown";
        }, "player.0.ownedScrollsHash");

        AssertComparisonFails((host, client) =>
        {
            client.Players[0].ManualSkillReadyHash = "unknown";
        }, "player.0.manualSkillReadyHash");

        AssertComparisonFails((host, client) =>
        {
            client.Players[0].Monsters.TypeHash = "unknown";
        }, "player.0.monsters.typeHash");

        AssertComparisonFails((host, client) =>
        {
            client.Players[0].Monsters.OwnerOriginHash = "unknown";
        }, "player.0.monsters.ownerOriginHash");

        AssertComparisonFails((host, client) =>
        {
            client.Players[0].Monsters.HpBucketHash = "unknown";
        }, "player.0.monsters.hpBucketHash");

        AssertComparisonFails((host, client) =>
        {
            client.Players[0].Monsters.BossPoolIdentityHash = "unknown";
        }, "player.0.monsters.bossPoolIdentityHash");

        AssertComparisonFails((host, client) =>
        {
            client.Effects.ActiveBuffHash = "unknown";
        }, "effects.activeBuffHash");
    }

    [Test]
    public void ZoneControllerClearsBattleOnlyZonesOnPrepareTransition()
    {
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(typeof(ZoneController), typeof(GameEvents), "add_OnGameStateChanged"), Is.True);
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(typeof(ZoneController), typeof(GameEvents), "remove_OnGameStateChanged"), Is.True);
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(typeof(ZoneController), typeof(UnityEngine.Object), "Destroy"), Is.True);
        Assert.That(typeof(ZoneController).GetMethod("HandleGameStateChanged", BindingFlags.Instance | BindingFlags.NonPublic), Is.Not.Null);
    }

    [Test]
    public void BasicAssertionRejectsMissingGameManagers()
    {
        var snapshot = BuildSnapshot("host");
        snapshot.Game.HasGameManagers = false;

        var result = MPTestAssertions.AssertBasic(snapshot, expectedPlayers: 1, expectedScene: "Game", expectedGameState: "Prepare");

        Assert.That(result.Success, Is.False);
        Assert.That(result.Errors, Does.Contain("gameManagers_missing"));
    }

    [Test]
    public void AutomationServerRuntimeGateRequiresTestPortAndToken()
    {
        var missing = new MPTestCommandLine.Options
        {
            Enabled = true,
            AutomationPort = 0,
            AutomationToken = "token"
        };
        Assert.That(MPTestAutomationServer.CanStart(missing, out string missingReason), Is.False);
        Assert.That(missingReason, Does.Contain("--mpAutomationPort"));

        var ready = new MPTestCommandLine.Options
        {
            Enabled = true,
            AutomationPort = 19002,
            AutomationToken = "token"
        };
        Assert.That(MPTestAutomationServer.CanStart(ready, out _), Is.True);
    }

    [Test]
    public void AutomationServerSourceKeepsProductionSafetyGates()
    {
        string source = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/Testing/MP/MPTestAutomationServer.cs");

        Assert.That(source, Does.Contain("UNITY_EDITOR || DEVELOPMENT_BUILD"));
        Assert.That(source, Does.Contain("missing --mpTest"));
        Assert.That(source, Does.Contain("missing --mpAutomationPort"));
        Assert.That(source, Does.Contain("missing --mpAutomationToken"));
        Assert.That(source, Does.Contain("IPAddress.Loopback"));
        Assert.That(source, Does.Contain("X-MPTest-Token"));
        Assert.That(source, Does.Contain("Authorization"));
        Assert.That(source, Does.Contain("/bot/start"));
        Assert.That(source, Does.Contain("/bot/status"));
        Assert.That(source, Does.Contain("/test/freezeGameFlow"));
        Assert.That(source, Does.Contain("MPTestGracefulQuit.RequestQuit"));
    }

    [Test]
    public void PrecommitFixtureCatchesUnsafeAutomationServer()
    {
        const string unsafeServer = "class MPTestAutomationServer { TcpListener listener; }";
        Assert.That(unsafeServer, Does.Contain("MPTestAutomationServer"));
        Assert.That(unsafeServer, Does.Not.Contain("UNITY_EDITOR || DEVELOPMENT_BUILD"));
        Assert.That(unsafeServer, Does.Not.Contain("--mpTest"));
        Assert.That(unsafeServer, Does.Not.Contain("IPAddress.Loopback"));
    }

    [Test]
    public void BattleCommandFoundationCarriesRequiredResultFields()
    {
        var result = BattleCommandResult.Rejected(
            CommandType.ActivateSkill,
            playerId: 2,
            errorCode: "unit_not_ready",
            message: "unit cannot act",
            opponentPlayerId: 1,
            scope: CommandExecutionScope.ClientRequest,
            source: "editmode",
            sequence: 7);

        Assert.That(result.Success, Is.False);
        Assert.That(result.ErrorCode, Is.EqualTo("unit_not_ready"));
        Assert.That(result.Message, Is.EqualTo("unit cannot act"));
        Assert.That(result.CommandType, Is.EqualTo(CommandType.ActivateSkill));
        Assert.That(result.PlayerId, Is.EqualTo(2));
        Assert.That(result.OpponentPlayerId, Is.EqualTo(1));
        Assert.That(result.Scope, Is.EqualTo(CommandExecutionScope.ClientRequest));
        Assert.That(result.Source, Is.EqualTo("editmode"));
        Assert.That(result.Sequence, Is.EqualTo(7));
    }

    [Test]
    public void BattleCommandFoundationDefinesStableExecutionScopes()
    {
        Assert.That(CommandExecutionScope.ClientRequest.ToString(), Is.EqualTo("ClientRequest"));
        Assert.That(CommandExecutionScope.ServerAuthorityOnly.ToString(), Is.EqualTo("ServerAuthorityOnly"));
        Assert.That(CommandExecutionScope.PresentationOnly.ToString(), Is.EqualTo("PresentationOnly"));
    }

    [Test]
    public void BattleCommandValidatorRejectsInvalidTargetPositions()
    {
        Assert.That(BattleCommandValidator.IsFiniteTargetPosition(Vector3.zero), Is.True);
        Assert.That(BattleCommandValidator.IsFiniteTargetPosition(new Vector3(float.NaN, 0f, 0f)), Is.False);
        Assert.That(BattleCommandValidator.IsFiniteTargetPosition(new Vector3(0f, float.PositiveInfinity, 0f)), Is.False);
    }

    [Test]
    public void BattleSpawnClickZoneRejectsDefenderInnerField()
    {
        var go = new GameObject("battle-spawn-click-zone-test");
        try
        {
            var field = go.AddComponent<FieldManager>();
            field.gridOrigin = Vector3.zero;
            field.cellSize = 1f;
            field.gridSize = new Vector2Int(10, 9);

            Assert.That(BattleCommandValidator.IsInsideBattleSpawnZone(field, new Vector3(0.5f, 0f, 0.5f)), Is.False);
            Assert.That(BattleCommandValidator.IsInsideBattleSpawnZone(field, new Vector3(9.5f, 0f, 8.5f)), Is.False);
            Assert.That(BattleCommandValidator.IsInsideBattleSpawnZone(field, new Vector3(-1.5f, 0f, 0.5f)), Is.True);
            Assert.That(BattleCommandValidator.IsInsideBattleSpawnZone(field, new Vector3(-3.5f, 0f, 0.5f)), Is.False);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void ServerBattleCommandExecutorRequiresExplicitDelegates()
    {
        Assert.That(MdfCompiledCodePolicy.ContainsStringLiteral(typeof(ServerBattleCommandExecutor), "validation_delegate_required"), Is.True);
        Assert.That(MdfCompiledCodePolicy.ContainsStringLiteral(typeof(ServerBattleCommandExecutor), "execution_delegate_required"), Is.True);
        Assert.That(MdfCompiledCodePolicy.ContainsStringLiteral(typeof(ServerBattleCommandExecutor), "no_validation_delegate"), Is.False);
        Assert.That(MdfCompiledCodePolicy.ContainsStringLiteral(typeof(ServerBattleCommandExecutor), "no_execution_delegate"), Is.False);
        BattleCommandResult result = ServerBattleCommandExecutor.TryExecute(
            CommandType.ActivateSkill,
            1,
            CommandExecutionScope.PresentationOnly,
            "editmode",
            validate: null,
            execute: null);
        Assert.That(result.Success, Is.False);
        Assert.That(result.ErrorCode, Is.EqualTo("presentation_scope_not_executable"));
    }

    [Test]
    public void BattleCommandOpponentResolutionKeepsClientPathsReadOnly()
    {
        const BindingFlags members = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        MethodInfo resolveOpponent = typeof(BattleCommandValidator).GetMethod("ResolveOpponent", members);
        MethodInfo fallback = typeof(BattleCommandValidator).GetMethod("CanUseAuthorityOpponentFallback", members);
        Assert.That(resolveOpponent, Is.Not.Null);
        Assert.That(fallback, Is.Not.Null);
        Assert.That(MdfCompiledCodePolicy.ContainsStringLiteral(typeof(BattleCommandValidator), "battle_opponent_snapshot_missing"), Is.True);
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(resolveOpponent, typeof(BattleCommandValidator), "CanUseAuthorityOpponentFallback"), Is.True);
        Assert.That((bool)fallback.Invoke(null, new object[] { null, CommandExecutionScope.ClientRequest }), Is.False);
        Assert.That((bool)fallback.Invoke(null, new object[] { null, CommandExecutionScope.ServerAuthorityOnly }), Is.False);
    }

    [Test]
    public void ScrollTargetEvaluatorScoresDebuffNearDefenderClusterAndAlliedMonsters()
    {
        var scroll = CreateScrollForTest(
            MagicScrollTacticalRole.Debuff,
            MagicScrollTargetDomain.EnemyUnitsNearAlliedMonsters,
            range: 2.5f,
            aiMinValue: 0f);
        var heatmap = new BattleHeatmap(
            null,
            null,
            null,
            new[]
            {
                new BattleHeatmap.UnitSample(null, new Vector3(2f, 0f, 2f), "unit=a", 5f, 1f, true, false, false),
                new BattleHeatmap.UnitSample(null, new Vector3(2.4f, 0f, 2.1f), "unit=b", 7f, 0.3f, true, true, false),
                new BattleHeatmap.UnitSample(null, new Vector3(8f, 0f, 8f), "unit=c", 4f, 1f, false, false, false)
            },
            new[]
            {
                new BattleHeatmap.MonsterSample(null, new Vector3(2.2f, 0f, 1.6f), "monster=ally-a", 20f, 1f, false, false, false, false),
                new BattleHeatmap.MonsterSample(null, new Vector3(7.8f, 0f, 8.1f), "monster=ally-b", 15f, 1f, false, false, false, false)
            },
            false,
            Vector3.zero);

        try
        {
            var evaluator = new ScrollTargetEvaluator();
            bool found = evaluator.TryFindBestTarget(scroll, heatmap, out var result);

            Assert.That(found, Is.True, result.Reason);
            Assert.That(result.GameplayPosition.x, Is.LessThan(4f));
            Assert.That(result.GameplayPosition.z, Is.LessThan(4f));
            Assert.That(result.VisualPosition.y, Is.GreaterThan(result.GameplayPosition.y));
            Assert.That(result.JournalFields["defenderUnits"], Is.EqualTo(2));
            Assert.That(result.JournalFields["alliedMonsters"], Is.EqualTo(1));
        }
        finally
        {
            Object.DestroyImmediate(scroll.skillData);
            Object.DestroyImmediate(scroll);
        }
    }

    [Test]
    public void ScrollTargetEvaluatorScoresBuffOnAlliedMonsterCluster()
    {
        var scroll = CreateScrollForTest(
            MagicScrollTacticalRole.Buff,
            MagicScrollTargetDomain.AlliedMonsters,
            range: 2.25f,
            aiMinValue: 0f);
        var heatmap = new BattleHeatmap(
            null,
            null,
            null,
            new BattleHeatmap.UnitSample[0],
            new[]
            {
                new BattleHeatmap.MonsterSample(null, new Vector3(1f, 0f, 1f), "monster=boss", 80f, 1f, true, false, true, false),
                new BattleHeatmap.MonsterSample(null, new Vector3(1.4f, 0f, 1.2f), "monster=destroyer", 50f, 0.5f, false, true, false, false),
                new BattleHeatmap.MonsterSample(null, new Vector3(8f, 0f, 8f), "monster=lone", 15f, 1f, false, false, false, false)
            },
            true,
            new Vector3(1.5f, 0f, 1.5f));

        try
        {
            var evaluator = new ScrollTargetEvaluator();
            bool found = evaluator.TryFindBestTarget(scroll, heatmap, out var result);

            Assert.That(found, Is.True, result.Reason);
            Assert.That(result.GameplayPosition.x, Is.LessThan(4f));
            Assert.That(result.GameplayPosition.z, Is.LessThan(4f));
            Assert.That(result.JournalFields["alliedMonsters"], Is.EqualTo(2));
            Assert.That(result.JournalFields["highValue"], Is.EqualTo(3));
        }
        finally
        {
            Object.DestroyImmediate(scroll.skillData);
            Object.DestroyImmediate(scroll);
        }
    }

    [Test]
    public void ScrollTargetEvaluatorHonorsAiMinimumValue()
    {
        var scroll = CreateScrollForTest(
            MagicScrollTacticalRole.Debuff,
            MagicScrollTargetDomain.EnemyUnits,
            range: 2f,
            aiMinValue: 9999f);
        var heatmap = new BattleHeatmap(
            null,
            null,
            null,
            new[]
            {
                new BattleHeatmap.UnitSample(null, new Vector3(2f, 0f, 2f), "unit=a", 1f, 1f, false, false, false)
            },
            new BattleHeatmap.MonsterSample[0],
            false,
            Vector3.zero);

        try
        {
            var evaluator = new ScrollTargetEvaluator();
            bool found = evaluator.TryFindBestTarget(scroll, heatmap, out var result);

            Assert.That(found, Is.False);
            Assert.That(result.Reason, Does.Contain("below_min_value"));
        }
        finally
        {
            Object.DestroyImmediate(scroll.skillData);
            Object.DestroyImmediate(scroll);
        }
    }

    [Test]
    public void ActivateSkillCommandSourceContainsStrategicManualSkillGuards()
    {
        foreach (string errorCode in new[]
                 {
                     "skill_not_manual_or_ai_strategic", "skill_mana_not_ready", "skill_unit_disabled_or_silenced",
                     "skill_target_unavailable", "skill_unit_dead"
                 })
        {
            Assert.That(MdfCompiledCodePolicy.ContainsStringLiteral(typeof(ActivateSkillCommand), errorCode), Is.True, errorCode);
        }
        Assert.That(typeof(Unit).GetMethod("HasSkillTargetsAvailable"), Is.Not.Null);
        foreach (string phase in new[]
                 {
                     "skill_command_request", "skill_command_accepted", "skill_command_rejected",
                     "skill_command_skipped", "skill_command_executed"
                 })
        {
            Assert.That(MdfCompiledCodePolicy.ContainsStringLiteral(typeof(SkillCommandMpTestLogger), phase), Is.True, phase);
        }

        Assert.That(ActivateSkillCommand.IsVolatileNoOpReason("skill_target_unavailable"), Is.True);
        Assert.That(ActivateSkillCommand.IsVolatileNoOpReason("skill_unit_dead"), Is.True);
        Assert.That(ActivateSkillCommand.IsVolatileNoOpReason("skill_mana_not_ready"), Is.False);
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(typeof(PlayerManager), typeof(ActivateSkillCommand), "IsVolatileNoOpReason"), Is.True);
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(typeof(PlayerCommandRequestValidator), typeof(ActivateSkillCommand), "IsVolatileNoOp"), Is.True);
        Assert.That(typeof(PlayerCommandRequestValidator).GetMethod(
            "ValidateActivateSkillRequest",
            BindingFlags.Instance | BindingFlags.NonPublic), Is.Not.Null);
        MethodInfo requestRpc = typeof(PlayerManager).GetMethod("RPC_RequestCommandToServer", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.That(requestRpc, Is.Not.Null);
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(requestRpc, typeof(ActivateSkillCommand), ".ctor"), Is.False,
            "The player RPC must validate and enqueue the serialized command without constructing an ActivateSkillCommand.");
        MethodInfo sequentialWorker = typeof(CommandProcessor).GetMethod(
            "ProcessCommandsSequentiallyAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(sequentialWorker, Is.Not.Null);
        Assert.That(MdfCompiledCodePolicy.ContainsIsInstanceOf(sequentialWorker, typeof(ActivateSkillCommand)), Is.False,
            "The generic processor must not special-case ActivateSkillCommand through a runtime type test.");
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(typeof(CommandProcessor), typeof(CommandProcessor), "ReceiveAndEnqueueCommand"), Is.True);
    }

    [Test]
    public void DefenderSkillPolicyEmitsOnlyActivateSkillCommandDecision()
    {
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(typeof(DefenderSkillDecision), typeof(ActivateSkillCommand), ".ctor"), Is.True);
        Assert.That(MdfCompiledCodePolicy.ContainsStringLiteral(typeof(DefenderSkillPolicy), "defender_skill_evaluated"), Is.True);
        Assert.That(MdfCompiledCodePolicy.ContainsStringLiteral(typeof(DefenderSkillPolicy), "defender_skill_selected"), Is.True);
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(typeof(DefenderSkillPolicy), typeof(Unit), "ActivateSkill"), Is.False);
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(typeof(DefenderSkillPolicy), typeof(SkillEffect), "ApplyEffect"), Is.False);
    }

    [Test]
    public void BehaviorTreeV2WiresAiAndHumanBotThroughSharedPolicies()
    {
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
        Assert.That(typeof(AIPlayerController).GetField("_profile", fields)?.FieldType, Is.EqualTo(typeof(MdfBotProfile)));
        Assert.That(typeof(AIPlayerController).GetField("_prepareDecisionPolicy", fields)?.FieldType, Is.EqualTo(typeof(PrepareDecisionPolicy)));
        Assert.That(typeof(AIPlayerController).GetField("_battleDecisionPolicy", fields)?.FieldType, Is.EqualTo(typeof(BattleDecisionPolicy)));
        Assert.That(typeof(AIPlayerController).GetField("_serverAiCommandEmitter", fields)?.FieldType, Is.EqualTo(typeof(ServerAiCommandEmitter)));
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(typeof(AIPlayerController), typeof(MdfBotProfile), "ServerAiDefault"), Is.True);
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(typeof(AIPlayerController), typeof(MdfDecisionContext), "Create"), Is.True);
        Assert.That(typeof(AIPlayerController).GetMethod("BuildBehaviorTrees", fields), Is.Null);
        Assert.That(typeof(AIPlayerController).GetMethod("GetActiveTree", fields), Is.Null);

        Assert.That(typeof(MPTestHumanBotDriver).GetField("_profile", fields)?.FieldType, Is.EqualTo(typeof(MdfBotProfile)));
        Assert.That(typeof(MPTestHumanBotDriver).GetField("_preparePolicy", fields)?.FieldType, Is.EqualTo(typeof(PrepareDecisionPolicy)));
        Assert.That(typeof(MPTestHumanBotDriver).GetField("_battlePolicy", fields)?.FieldType, Is.EqualTo(typeof(BattleDecisionPolicy)));
        Assert.That(typeof(MPTestHumanBotDriver).GetField("_commandEmitter", fields)?.FieldType, Is.EqualTo(typeof(HumanClientCommandEmitter)));
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(typeof(MPTestHumanBotDriver), typeof(ComponentRegistry), "Register"), Is.False);
        Assert.That(typeof(MPTestBotJournal).GetMethods(BindingFlags.Static | BindingFlags.Public)
            .Any(method => method.Name == "BuildDecisionEntry" && method.GetParameters().Any(parameter => parameter.ParameterType == typeof(MdfDecision))), Is.True);
    }

    [Test]
    public void PreparePolicyDoesNotPaceServerAiInsidePolicy()
    {
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(typeof(PrepareDecisionPolicy), typeof(AI.BehaviorTree.AIPacer), "Ready"), Is.False);
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(typeof(PrepareDecisionPolicy), typeof(AI.BehaviorTree.AIPacer), "Arm"), Is.False);
    }

    [Test]
    public void BattleStartDoesNotAutoSpawnForAiAttackers()
    {
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(typeof(GameManagers), typeof(MonsterSpawner), "SpawnAllMonstersToTargetField"), Is.False);
        MethodInfo legacySpawn = typeof(MonsterSpawner).GetMethod("SpawnAllMonstersToTargetField");
        Assert.That(legacySpawn, Is.Not.Null);
        Assert.That(MdfCompiledCodePolicy.ContainsStringLiteralFragment(legacySpawn, "AI bootstrap is disabled"), Is.True);
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(legacySpawn, typeof(MonsterSpawner), "StartAutoSpawnFromPool"), Is.False);
        Assert.That(typeof(AIPlayerController).GetField("_battleDecisionPolicy", BindingFlags.Instance | BindingFlags.NonPublic)?.FieldType, Is.EqualTo(typeof(BattleDecisionPolicy)));
        Assert.That(typeof(AIPlayerController).GetField("_serverAiCommandEmitter", BindingFlags.Instance | BindingFlags.NonPublic)?.FieldType, Is.EqualTo(typeof(ServerAiCommandEmitter)));
    }

    [Test]
    public void FirstPrepareStartsAfterPlayersAreReadable()
    {
        string gameManagersSource = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/Managers/GameManagers.cs");

        int initStart = gameManagersSource.IndexOf("private async UniTask InitializeAndStartGame()", System.StringComparison.Ordinal);
        int gameFlowCall = gameManagersSource.IndexOf("await GameFlow();", initStart, System.StringComparison.Ordinal);
        int earlySpawnReady = gameManagersSource.IndexOf("_isSpawned = true;", initStart, System.StringComparison.Ordinal);
        int firstRoundStart = gameManagersSource.IndexOf("await StartNextRound();", gameManagersSource.IndexOf("private async UniTask GameFlow()", System.StringComparison.Ordinal), System.StringComparison.Ordinal);
        int waitForPlayers = gameManagersSource.IndexOf("await WaitForPlayerInitializationAsync();", gameManagersSource.IndexOf("private async UniTask StartNextRound()", System.StringComparison.Ordinal), System.StringComparison.Ordinal);
        int transitionToPrepare = gameManagersSource.IndexOf("TransitionToPrepareState(\"StartNextRound\")", gameManagersSource.IndexOf("private async UniTask StartNextRound()", System.StringComparison.Ordinal), System.StringComparison.Ordinal);

        Assert.That(initStart, Is.GreaterThanOrEqualTo(0));
        Assert.That(earlySpawnReady, Is.GreaterThan(initStart));
        Assert.That(earlySpawnReady, Is.LessThan(gameFlowCall));
        Assert.That(firstRoundStart, Is.GreaterThan(gameFlowCall));
        Assert.That(waitForPlayers, Is.LessThan(transitionToPrepare));
    }

    [Test]
    public void HumanBotClosesShopUiBeforeBoardActionCommands()
    {
        string source = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/Testing/MP/MPTestHumanBotDriver.cs");

        Assert.That(source, Does.Contain("CloseShopUiBeforeBoardAction(decision);"));
        Assert.That(source.IndexOf("CloseShopUiBeforeBoardAction(decision);", System.StringComparison.Ordinal),
            Is.LessThan(source.IndexOf("_commandEmitter.TryEmit(decision", System.StringComparison.Ordinal)));
        Assert.That(source, Does.Contain("CloseShopUiAfterPrepareIdle(context, decision);"));
        Assert.That(source, Does.Contain("shop_close_after_shopping_complete"));
        Assert.That(source, Does.Contain("CommandType.PlaceWall"));
        Assert.That(source, Does.Contain("CommandType.MoveUnit"));
        Assert.That(source, Does.Contain("FindObjectOfType<ShopUIController>(true)"));
        Assert.That(source, Does.Contain("SetContentVisibility(false)"));
        Assert.That(source, Does.Contain("shop_close_before_board_action"));
        Assert.That(source, Does.Contain("human_bot_ui"));
        Assert.That(source, Does.Contain("MinimumCommandIntervalSeconds = 0.7f"));
        Assert.That(source, Does.Contain("Mathf.Max(decisionInterval, MinimumCommandIntervalSeconds)"));
    }

    [Test]
    public void PurchaseSuccessUiEventCannotBreakCommandProcessing()
    {
        string shopSource = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/UI/ShopUIController.cs");

        Assert.That(shopSource, Does.Contain("TryGetLocalShopPlayerId(out var localPlayerId)"));
        Assert.That(shopSource, Does.Contain("shopSlots == null || slotIndex < 0 || slotIndex >= shopSlots.Length"));
        Assert.That(shopSource, Does.Contain("var slot = shopSlots[slotIndex];"));
        Assert.That(shopSource, Does.Contain("if (slot == null)"));

        int laterSubscriberCalls = 0;
        System.Action<int, ShopItem, int> throwingSubscriber = (_, __, ___) =>
            throw new System.InvalidOperationException("purchase-subscriber-failure");
        System.Action<int, ShopItem, int> laterSubscriber = (_, __, ___) => laterSubscriberCalls++;
        try
        {
            LogAssert.Expect(LogType.Error, new Regex("OnUnitPurchaseSucceeded handler exception"));
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: purchase-subscriber-failure"));
            GameEvents.OnUnitPurchaseSucceeded += throwingSubscriber;
            GameEvents.OnUnitPurchaseSucceeded += laterSubscriber;

            GameEvents.TriggerUnitPurchaseSucceeded(3, default, 2);

            Assert.That(laterSubscriberCalls, Is.EqualTo(1), "A broken UI subscriber must not stop later purchase-success handlers.");
        }
        finally
        {
            GameEvents.OnUnitPurchaseSucceeded -= throwingSubscriber;
            GameEvents.OnUnitPurchaseSucceeded -= laterSubscriber;
        }
    }

    [Test]
    public void GamePrepareToolkitKeepsChoiceCountsAndCommandRoutes()
    {
        string controllerSource = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/UI/Game/GamePrepareUIToolkitController.cs");
        string attackManagerSource = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/Game/Battle/AttackSequenceManager.cs");
        string inputSource = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/Managers/MdfInput.cs");
        string placementSource = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/Managers/PlacementManager.cs");
        string playerManagerSource = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/Managers/PlayerManager.cs");
        string removeWallCommandSource = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/Commands/PlayerActions/RemoveWallCommand.cs");
        string automationSource = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/Testing/MP/MPTestAutomationServer.cs");
        string styleSource = MdfSourcePolicy.ReadStaticContract("Assets/Resources/UI/GamePrepare/GamePreparePanelsStyles.uss");
        var layout = Resources.Load<VisualTreeAsset>("UI/GamePrepare/GamePreparePanels");
        var style = Resources.Load<StyleSheet>("UI/GamePrepare/GamePreparePanelsStyles");
        var theme = Resources.Load<ThemeStyleSheet>("UI/GamePrepare/GamePrepareRuntimeTheme");
        var tree = layout != null ? layout.CloneTree() : null;

        Assert.That(GamePrepareUIToolkitController.ShopCardCount, Is.EqualTo(5));
        Assert.That(GamePrepareUIToolkitController.AugmentCardCount, Is.EqualTo(3));
        Assert.That(GamePrepareUIToolkitController.MonsterCardCount, Is.EqualTo(12));
        Assert.That(GamePrepareUIToolkitController.ScrollCardCount, Is.EqualTo(5));
        Assert.That(
            GamePrepareUIToolkitController.FormatAugmentDisplayName("\uBCF4\uC2A4\uBAAC\uC2A4\uD130 \uC18C\uD658(\uACF5\uC911)"),
            Is.EqualTo("\uBCF4\uC2A4\uBAAC\uC2A4\uD130 \uC18C\uD658\n(\uACF5\uC911)"));
        Assert.That(
            GamePrepareUIToolkitController.FormatAugmentDisplayName("\uBCF4\uC2A4\uBAAC\uC2A4\uD130 \uC18C\uD658(\uC800\uC9C0\uBD88\uAC00)"),
            Is.EqualTo("\uBCF4\uC2A4\uBAAC\uC2A4\uD130 \uC18C\uD658\n(\uC800\uC9C0\uBD88\uAC00)"));
        Assert.That(
            GamePrepareUIToolkitController.FormatAugmentDisplayName("\uB9C8\uBC95\uC2A4\uD06C\uB864(\uD68C\uBCF5)"),
            Is.EqualTo("\uB9C8\uBC95\uC2A4\uD06C\uB864(\uD68C\uBCF5)"));
        Assert.That(layout, Is.Not.Null);
        Assert.That(style, Is.Not.Null);
        Assert.That(theme, Is.Not.Null);
        Assert.That(tree?.Q<VisualElement>("shop-panel"), Is.Not.Null);
        Assert.That(tree?.Q<VisualElement>("game-prepare-design-space"), Is.Not.Null);
        Assert.That(tree?.Q<VisualElement>("augment-panel"), Is.Not.Null);
        Assert.That(tree?.Q<VisualElement>("attack-sequence-panel"), Is.Not.Null);
        Assert.That(tree?.Q<VisualElement>("game-resource-root"), Is.Not.Null);
        Assert.That(tree?.Q<VisualElement>("game-left-wireframe-rail"), Is.Not.Null);
        Assert.That(tree?.Q<VisualElement>("game-shop-toggle-button"), Is.Not.Null);
        Assert.That(tree?.Q<Label>("game-shop-gold-count-label"), Is.Not.Null);
        Assert.That(tree?.Q<VisualElement>("game-shop-gold-icon"), Is.Not.Null);
        Assert.That(tree?.Q<Label>("shop-reroll-gold-count-label"), Is.Not.Null);
        Assert.That(tree?.Q<VisualElement>("shop-reroll-gold-icon"), Is.Not.Null);
        Assert.That(tree?.Q<VisualElement>("game-wall-button"), Is.Not.Null);
        Assert.That(tree?.Q<VisualElement>("game-option-button"), Is.Not.Null);
        Assert.That(tree?.Q<VisualElement>("game-gold-value"), Is.Not.Null);
        Assert.That(tree?.Q<Label>("game-gold-count-value"), Is.Not.Null);
        Assert.That(tree?.Q<VisualElement>("shop-cost-icon-0"), Is.Not.Null);
        Assert.That(tree?.Q<Label>("shop-cost-0"), Is.Not.Null);
        Assert.That(tree?.Q<VisualElement>("game-wall-icon"), Is.Not.Null);
        Assert.That(tree?.Q<Label>("game-wall-count-label"), Is.Not.Null);
        AssertNamedElements(tree, "shop-card-", 5);
        Assert.That(CountElementsWithClass(tree, "shop-art-frame"), Is.EqualTo(5));
        Assert.That(CountElementsWithClass(tree, "shop-text-overlay"), Is.EqualTo(5));
        AssertNamedElements(tree, "augment-card-", 3);
        AssertNamedElements(tree, "attack-monster-card-", 12);
        AssertNamedElements(tree, "attack-scroll-card-", 5);
        Assert.That(tree.styleSheets.Contains(style), Is.True);
        Assert.That(tree.Q<VisualElement>("shop-panel")?.ClassListContains("prepare-panel"), Is.True);
        Assert.That(tree.Q<VisualElement>("shop-panel")?.ClassListContains("shop-panel"), Is.True);
        Assert.That(tree?.Q<VisualElement>("shop-card-0")?.ClassListContains("shop-card-star-1"), Is.True);
        Assert.That(tree?.Q<VisualElement>("shop-card-4")?.ClassListContains("shop-card-star-5"), Is.True);
        Assert.That(controllerSource, Does.Contain("new BuyUnitCommand(playerId, slotIndex)"));
        Assert.That(controllerSource, Does.Contain("new RerollShopCommand(playerId)"));
        Assert.That(controllerSource, Does.Contain("new SelectAugmentCommand(playerId, index)"));
        Assert.That(controllerSource, Does.Contain("FormatAugmentDisplayName(augment.augmentName)"));
        Assert.That(controllerSource, Does.Contain("!root.styleSheets.Contains(styleSheet)"));
        Assert.That(controllerSource, Does.Contain("TryShowAttackSequenceFromLegacy"));
        Assert.That(controllerSource, Does.Contain("ShopCardReferenceWidth"));
        Assert.That(controllerSource, Does.Contain("WallButtonSize"));
        Assert.That(controllerSource, Does.Contain("private const float WallButtonSize = RerollButtonSize;"));
        Assert.That(controllerSource, Does.Contain("private const float ShopToggleButtonSize = RerollButtonSize;"));
        Assert.That(controllerSource, Does.Contain("private const float ShopToggleButtonBottom = HudBottomInset;"));
        Assert.That(controllerSource, Does.Contain("UpdateDesignScale"));
        Assert.That(controllerSource, Does.Contain("game-prepare-design-space"));
        Assert.That(controllerSource, Does.Contain("leftWireframeRail"));
        Assert.That(controllerSource, Does.Contain("RerollButtonSize"));
        Assert.That(controllerSource, Does.Contain("StyleKeyword.Auto"));
        Assert.That(controllerSource, Does.Contain("ApplyStarBackground"));
        Assert.That(controllerSource, Does.Contain("ScaleMode.ScaleAndCrop"));
        Assert.That(controllerSource, Does.Contain("icon.style.flexShrink = 0f"));
        Assert.That(controllerSource, Does.Contain("shop-art-frame"));
        Assert.That(controllerSource, Does.Contain("item.StarLevel"));
        Assert.That(controllerSource, Does.Contain("ApplyStarBackground(hasItem && item.UnitData != null ? item.UnitData.cost : 0);"));
        Assert.That(controllerSource, Does.Contain("GetShopCardStarClass"));
        Assert.That(controllerSource, Does.Contain("topGem.style.display = DisplayStyle.None"));
        Assert.That(controllerSource, Does.Contain("body.style.backgroundColor = Color.clear"));
        Assert.That(controllerSource, Does.Contain("footer.style.backgroundColor = new Color"));
        Assert.That(controllerSource, Does.Contain("attackSequenceManager?.SelectMonsterSlot(slotIndex)"));
        Assert.That(controllerSource, Does.Contain("attackSequenceManager?.SelectMagicScroll(scrolls[slotIndex])"));
        Assert.That(styleSource, Does.Match(@"(?s)\.monster-attack-card \.attack-card-icon\s*\{.*?position:\s*absolute;.*?width:\s*auto;.*?height:\s*auto;.*?scale-and-crop;"));
        Assert.That(styleSource, Does.Match(@"(?s)\.monster-attack-card \.attack-card-name\s*\{.*?bottom:\s*18px;"));
        Assert.That(styleSource, Does.Match(@"(?s)\.monster-attack-card \.attack-card-count\s*\{.*?bottom:\s*1px;"));
        Assert.That(controllerSource, Does.Contain("game-gold-count-value"));
        Assert.That(controllerSource, Does.Contain("game-wall-count-label"));
        Assert.That(controllerSource, Does.Contain("root?.Q<VisualElement>($\"shop-cost-icon-{i}\")"));
        Assert.That(controllerSource, Does.Contain("return cost <= 0 ? \"\\uBB34\\uB8CC\" : cost.ToString();"));
        Assert.That(controllerSource, Does.Contain("SetVisible(costIcon, item.CalculatedCost > 0);"));
        Assert.That(controllerSource, Does.Contain("reroll-gold-mode"));
        Assert.That(controllerSource, Does.Contain("EnableInClassList(\"reroll-gold-mode\", !shopVisible)"));
        Assert.That(controllerSource, Does.Contain("SetText(rerollGoldLabel, Mathf.Max(0, cost).ToString());"));
        Assert.That(controllerSource, Does.Contain("SetVisible(rerollGoldRow, cost > 0);"));
        Assert.That(controllerSource, Does.Contain("SetText(hudShopLabel, $\"\\uC0C1\\uC810\\n{shopAction}\");"));
        Assert.That(controllerSource, Does.Contain("SetText(hudShopGoldLabel, Mathf.Max(0, goldCount).ToString());"));
        Assert.That(controllerSource, Does.Contain("SetPickingMode(rerollButton, shopVisible ? PickingMode.Position : PickingMode.Ignore)"));
        Assert.That(styleSource, Does.Contain("Spr_UnitCost.png"));
        Assert.That(styleSource, Does.Contain("Bricks.png"));
        Assert.That(controllerSource, Does.Contain("TogglePlacementMode(PlacementMode.Wall)"));
        Assert.That(controllerSource, Does.Contain("CameraManager.Instance.ReturnToOwnField()"));
        Assert.That(controllerSource, Does.Contain("SetShopVisible(false)"));
        Assert.That(controllerSource, Does.Contain("GetUIElement(\"OptionCanvas\")"));
        Assert.That(controllerSource, Does.Contain("\"\\uB2EB\\uAE30\""));
        Assert.That(controllerSource, Does.Contain("\"\\uC5F4\\uAE30\""));
        Assert.That(controllerSource, Does.Contain("Mathf.Max(0, wallCount).ToString()"));
        Assert.That(controllerSource, Does.Contain("Mathf.Clamp(area.xMin, 0f, screenWidth)"));
        Assert.That(controllerSource, Does.Contain("UpdateRoundTimerLabel"));
        Assert.That(controllerSource, Does.Contain("currentPhaseTimer"));
        Assert.That(controllerSource, Does.Contain("currentSequenceTransitionTimer"));
        Assert.That(controllerSource, Does.Contain("IsPointerOverBlockingElement"));
        Assert.That(controllerSource, Does.Contain("IsToolkitRaycastObject"));
        Assert.That(controllerSource, Does.Contain("IsRuntimePanelRaycasterObject"));
        Assert.That(controllerSource, Does.Contain("GamePrepareRuntimePanelSettings"));
        Assert.That(controllerSource, Does.Contain("RegisterCallback<PointerDownEvent>"));
        Assert.That(controllerSource, Does.Contain("SuppressBattleMapInputForCurrentPointer"));
        Assert.That(controllerSource, Does.Contain("IsBlockingElementOrDescendant"));
        Assert.That(controllerSource, Does.Contain("ToPanelScreenPosition(screenPosition)"));
        Assert.That(controllerSource, Does.Not.Contain("invertedPanelPosition"));
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(typeof(AttackSequenceUIController), typeof(GamePrepareUIToolkitController), "TryShowAttackSequenceFromLegacy"), Is.True);
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(typeof(AttackSequenceUIController), typeof(AttackSequenceUIController), "SetLegacyContentVisibilityOnly"), Is.True);
        Assert.That(attackManagerSource, Does.Contain("IsPointerOverBattleActionBlocker"));
        Assert.That(attackManagerSource, Does.Contain("TryGetSpawnPositionUnderPointer"));
        Assert.That(attackManagerSource, Does.Contain("ShouldSuppressBattleMapInput"));
        Assert.That(attackManagerSource, Does.Contain("BattleCommandValidator.IsInsideBattleSpawnZone"));
        Assert.That(inputSource, Does.Contain("HasNonGamePrepareToolkitUiHit"));
        Assert.That(inputSource, Does.Contain("eventSystem.RaycastAll"));
        Assert.That(inputSource, Does.Contain("IsPointerOverFieldBlockingUI"));
        Assert.That(inputSource, Does.Contain("IsFieldPassthroughUi"));
        Assert.That(inputSource, Does.Contain("GetComponentInParent<StatusBarUI>()"));
        Assert.That(inputSource, Does.Contain("GetComponentInParent<RankingUIController>()"));
        Assert.That(inputSource, Does.Contain("RankingUIController.IsToolkitRaycastObject"));
        Assert.That(inputSource, Does.Contain("RankingUIController.IsPointerOverBlockingElement(pointerPosition)"));
        Assert.That(inputSource, Does.Contain("DescribeFieldBlockingUiHits"));
        Assert.That(typeof(RankingUIController).GetMethod("IsPointerOverBlockingElement"), Is.Not.Null);
        Assert.That(typeof(RankingUIController).GetMethod("IsToolkitRaycastObject"), Is.Not.Null);
        Assert.That(typeof(RankingUIController).GetMethod("IsRuntimePanelRaycasterObject", BindingFlags.Instance | BindingFlags.NonPublic), Is.Not.Null);
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(typeof(PlacementButtonsUI), typeof(CameraManager), "ReturnToOwnField"), Is.True);
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(typeof(PlacementButtonsUI), typeof(GamePrepareUIToolkitController), "TrySetShopVisibilityFromLegacy"), Is.True);
        Assert.That(placementSource, Does.Contain("bool pointerOverUI = MdfInput.IsPointerOverFieldBlockingUI()"));
        Assert.That(placementSource, Does.Contain("if (currentMode == PlacementMode.None)"));
        Assert.That(placementSource, Does.Not.Contain("currentMode == PlacementMode.None || !showPreview"));
        Assert.That(placementSource, Does.Contain("BuildWallPlacementInvalidReason"));
        Assert.That(placementSource, Does.Contain("[ManualWall]"));
        Assert.That(placementSource, Does.Contain("!pointerOverUI"));
        Assert.That(placementSource, Does.Contain("SecondaryPointerWasPressedThisFrame"));
        Assert.That(placementSource, Does.Match(@"(?s)if \(secondaryPressed\)\s*\{\s*StopPlacementMode\(\);\s*return;\s*\}"));
        Assert.That(placementSource, Does.Not.Match(@"(?s)if \(secondaryPressed\)\s*\{[^}]*TryRemoveWall\("));
        Assert.That(placementSource, Does.Contain("currentMode == PlacementMode.Wall && TryRemoveWall()"));
        Assert.That(placementSource, Does.Contain("fieldManager.GetWallAt(currentMouseGridPosition) == null"));
        Assert.That(removeWallCommandSource, Does.Contain("if (fm.GetWallAt(Position) == null)"));
        Assert.That(removeWallCommandSource, Does.Contain("player.ReturnWall();"));
        Assert.That(playerManagerSource, Does.Contain("public void ReturnWall()"));
        Assert.That(playerManagerSource, Does.Not.Contain("wallCount < MAX_WALL_COUNT"));
        Assert.That(automationSource, Does.Contain("ExecutePlaceWallCommand"));
        Assert.That(automationSource, Does.Contain("ExecuteRemoveWallCommand"));
        Assert.That(automationSource, Does.Contain("place_wall"));
        Assert.That(automationSource, Does.Contain("remove_wall"));
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(typeof(ShopUIController), typeof(GamePrepareUIToolkitController), "TryToggleShopFromLegacy"), Is.True);
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(typeof(AugmentUIController), typeof(GamePrepareUIToolkitController), "TryShowAugmentsFromLegacy"), Is.True);
        Assert.That(typeof(PlayerHUDController).GetMethod("SetLegacyHudButtonsVisible"), Is.Not.Null);
        Assert.That(typeof(PlayerHUDController).GetMethod("SetLegacyResourceHudVisible"), Is.Not.Null);
        Assert.That(styleSource, Does.Contain(".game-left-wireframe-rail"));
        Assert.That(styleSource, Does.Contain(".game-prepare-design-space"));
        Assert.That(styleSource, Does.Contain("NotoSansKR-VariableFont_wght_UITK.asset"));
        Assert.That(styleSource, Does.Contain(".wire-status-row"));
        Assert.That(styleSource, Does.Contain("padding-left: 120px;"));
        Assert.That(styleSource, Does.Contain("width: 280px;"));
        Assert.That(styleSource, Does.Contain("height: 350px;"));
        Assert.That(styleSource, Does.Contain("scale-and-crop"));
        Assert.That(styleSource, Does.Contain("flex-shrink: 0;"));
        Assert.That(styleSource, Does.Contain(".shop-art-frame"));
        Assert.That(styleSource, Does.Contain(".shop-text-overlay"));
        Assert.That(styleSource, Does.Contain(".shop-footer-row"));
        Assert.That(styleSource, Does.Contain(".shop-star-label"));
        Assert.That(styleSource, Does.Match(@"(?s)\.shop-card \.card-name\s*\{.*?-unity-font-style:\s*bold;"));
        Assert.That(styleSource, Does.Match(@"(?s)\.shop-card \.card-description\s*\{.*?-unity-font-style:\s*bold;"));
        Assert.That(styleSource, Does.Contain("width: 180px;"));
        Assert.That(styleSource, Does.Contain("height: 180px;"));
        Assert.That(styleSource, Does.Contain(".wall-action-button"));
        Assert.That(styleSource, Does.Match(@"(?s)\.wall-action-button\s*\{.*?left:\s*28px;.*?bottom:\s*26px;.*?width:\s*180px;"));
        Assert.That(styleSource, Does.Match(@"(?s)\.game-hud-root\s*\{.*?right:\s*28px;.*?bottom:\s*26px;.*?width:\s*180px;"));
        Assert.That(styleSource, Does.Match(@"(?s)\.hud-action-button\s*\{.*?width:\s*180px;.*?height:\s*180px;"));
        Assert.That(styleSource, Does.Contain("width: 84px;"));
        Assert.That(styleSource, Does.Contain("height: 84px;"));
        Assert.That(styleSource, Does.Contain("width: 320px;"));
        Assert.That(styleSource, Does.Contain("height: 40px;"));
        Assert.That(styleSource, Does.Contain("margin-left: 12px;"));
        Assert.That(styleSource, Does.Contain(".round-timer-label"));
        Assert.That(styleSource, Does.Contain(".hud-icon-button"));
        Assert.That(styleSource, Does.Contain("position: absolute;"));
        Assert.That(styleSource, Does.Contain(".shop-card"));
        Assert.That(styleSource, Does.Contain("background-image: url"));
        Assert.That(styleSource, Does.Contain(".shop-card-star-1"));
        Assert.That(styleSource, Does.Contain(".shop-card-star-2"));
        Assert.That(styleSource, Does.Contain(".shop-card-star-3"));
        Assert.That(styleSource, Does.Contain(".shop-card-star-4"));
        Assert.That(styleSource, Does.Contain(".shop-card-star-5"));
        Assert.That(styleSource, Does.Contain("Spr_SlotGray.png"));
        Assert.That(styleSource, Does.Contain("Spr_SlotGreen.png"));
        Assert.That(styleSource, Does.Contain("Spr_SlotBlue.png"));
        Assert.That(styleSource, Does.Contain("Spr_SlotPurple.png"));
        Assert.That(styleSource, Does.Contain("Spr_SlotOrange.png"));
        AssertSpriteUsesFullRectMesh("Assets/Resource/Image/UI/SlotUI/Spr_SlotGray.png");
        AssertSpriteUsesFullRectMesh("Assets/Resource/Image/UI/SlotUI/Spr_SlotGreen.png");
        AssertSpriteUsesFullRectMesh("Assets/Resource/Image/UI/SlotUI/Spr_SlotBlue.png");
        AssertSpriteUsesFullRectMesh("Assets/Resource/Image/UI/SlotUI/Spr_SlotPurple.png");
        AssertSpriteUsesFullRectMesh("Assets/Resource/Image/UI/SlotUI/Spr_SlotOrange.png");
        Assert.That(tree.Q<VisualElement>("game-round-timer-label"), Is.Not.Null);
        Assert.That(tree.Q<VisualElement>("game-prepare-design-space"), Is.Not.Null);
        Assert.That(tree.Q<VisualElement>(className: "wall-action-button"), Is.Not.Null);
        Assert.That(GamePrepareUIToolkitController.GetShopCardStarClass(1), Is.EqualTo("shop-card-star-1"));
        Assert.That(GamePrepareUIToolkitController.GetShopCardStarClass(2), Is.EqualTo("shop-card-star-2"));
        Assert.That(GamePrepareUIToolkitController.GetShopCardStarClass(3), Is.EqualTo("shop-card-star-3"));
        Assert.That(GamePrepareUIToolkitController.GetShopCardStarClass(4), Is.EqualTo("shop-card-star-4"));
        Assert.That(GamePrepareUIToolkitController.GetShopCardStarClass(5), Is.EqualTo("shop-card-star-5"));
        Assert.That(GamePrepareUIToolkitController.GetShopCardStarClass(6), Is.EqualTo(string.Empty));
        Assert.That(GamePrepareUIToolkitController.FormatStarText(0), Is.EqualTo(string.Empty));
        Assert.That(GamePrepareUIToolkitController.FormatStarText(1), Is.EqualTo(string.Empty));
        Assert.That(GamePrepareUIToolkitController.FormatStarText(2), Is.EqualTo("\u2605\u2605"));
        Assert.That(GamePrepareUIToolkitController.FormatStarText(3), Is.EqualTo("\u2605\u2605\u2605"));
        Assert.That(GamePrepareUIToolkitController.CalculateCardSize(true, new Vector2(2340, 1080)).x, Is.EqualTo(336f).Within(0.01f));
        Assert.That(GamePrepareUIToolkitController.CalculateCardSize(true, new Vector2(2340, 1080)).y, Is.EqualTo(420f).Within(0.01f));
        Assert.That(GamePrepareUIToolkitController.CalculateCardSize(false, new Vector2(2340, 1080)).y, Is.GreaterThanOrEqualTo(64f * 6.0f));
        Assert.That(GamePrepareUIToolkitController.CalculateShopTopPadding(new Vector2(2340, 1080)), Is.EqualTo(172.8f).Within(0.01f));
        Assert.That(GamePrepareUIToolkitController.CalculateRerollButtonTop(new Vector2(2340, 1080)), Is.EqualTo(621.6f).Within(0.01f));
        Assert.That(GamePrepareUIToolkitController.CalculateAugmentTopPadding(new Vector2(2340, 1080)), Is.GreaterThanOrEqualTo(320f));
    }

    [Test]
    public void FieldUnitRegistrationHandlesLateMovesAndRetiredUnits()
    {
        string fieldSource = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/Managers/FieldManager.cs");
        string playerSource = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/Managers/PlayerManager.cs");
        string gameManagersSource = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/Managers/GameManagers.cs");

        Assert.That(fieldSource, Does.Contain("pendingNetworkMoves"));
        Assert.That(fieldSource, Does.Contain("retiredNetworkUnitIds"));
        Assert.That(fieldSource, Does.Contain("retiredNetworkUnitIds.Remove"));
        Assert.That(fieldSource, Does.Contain("QueuePendingNetworkMove(from, to);"));
        Assert.That(fieldSource, Does.Contain("ProcessPendingNetworkMoves();"));
        Assert.That(fieldSource, Does.Contain("BroadcastUnitUnregistered"));
        Assert.That(fieldSource, Does.Contain("BroadcastAuthoritativeUnitRoster"));
        Assert.That(fieldSource, Does.Contain("ReconcileUnitsToAuthoritativeRoster"));
        Assert.That(fieldSource, Does.Contain("UnregisterUnitAt"));
        Assert.That(fieldSource, Does.Contain("RemovePlacedUnitEntries"));
        Assert.That(fieldSource, Does.Contain("RemoveOwnedUnitReference"));
        Assert.That(fieldSource, Does.Contain("networkRunning && !hasUnitNetworkId"));
        Assert.That(fieldSource, Does.Contain("GetPlacedCellsForUnit"));
        Assert.That(fieldSource, Does.Contain("GroupBy(kvp => kvp.Value)"));
        Assert.That(fieldSource, Does.Contain("combinableGroup.Select(entry => entry.Unit).Take(3).ToList()"));
        Assert.That(fieldSource, Does.Contain("RefreshWallMapsFromSceneIfPlaying(\"BuildWallCellHash\")"));
        Assert.That(fieldSource, Does.Contain("RefreshWallMapsFromSceneIfPlaying(\"GetValidPlacementTiles\")"));
        Assert.That(fieldSource, Does.Contain("UnitBelongsToFieldOwner"));
        Assert.That(fieldSource, Does.Contain("IsLegalMoveDestinationForUnit"));
        Assert.That(fieldSource, Does.Contain("melee_unit_cannot_move_to_wall"));
        Assert.That(typeof(PlayerCommandRequestValidator).GetMethod(nameof(PlayerCommandRequestValidator.IsUnitOwnedByPlayer)), Is.Not.Null);
        Assert.That(typeof(PlayerManager).GetMethod(nameof(PlayerManager.IsUnitOwnedByPlayerForCommand)), Is.Not.Null);
        Assert.That(fieldSource, Does.Contain("IsLiveDestructibleWallCandidate"));
        Assert.That(fieldSource, Does.Contain("wall.gameObject.activeSelf"));
        Assert.That(playerSource, Does.Contain("RPC_UnregisterUnitAt"));
        Assert.That(playerSource, Does.Contain("RPC_ReconcileUnitRoster"));
        Assert.That(playerSource, Does.Contain("ApplyUnitRosterFromAuthority"));
        Assert.That(playerSource, Does.Contain("_retiredUnitRegistrationIds"));
        Assert.That(playerSource, Does.Contain("_retiredUnitRegistrationIds.Remove"));
        Assert.That(playerSource, Does.Contain("RemoveAll(reg => reg.unitIdRaw == unitIdRaw)"));
        Assert.That(gameManagersSource, Does.Contain("StartBattleForPlayers.PreBattle"));
    }

    [Test]
    public void FieldUnitRosterDoesNotReapplySameCellTransformEverySync()
    {
        string fieldSource = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/Managers/FieldManager.cs");

        Assert.That(fieldSource, Does.Contain("previousCells.Count == 1 && previousCells[0] == gridPosition"));
        Assert.That(fieldSource, Does.Contain("(unit.transform.position - targetWorldPos).sqrMagnitude > 0.0001f"));
        Assert.That(fieldSource, Does.Contain("MoveUnitImmediate(unit, worldPos);"));
        Assert.That(fieldSource, Does.Contain("RemovePlacedUnitEntries(unit);"));
    }

    [Test]
    public void NewlyPurchasedUnitMovesAreQueuedUntilRegistrationCompletes()
    {
        string fieldSource = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/Managers/FieldManager.cs");
        string playerSource = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/Managers/PlayerCommandRequestValidator.cs");

        Assert.That(fieldSource, Does.Contain("pendingUnitDataByPosition"));
        Assert.That(fieldSource, Does.Contain("public bool HasPendingUnitAt"));
        Assert.That(fieldSource, Does.Contain("public bool TryGetPendingUnitDataAt"));
        Assert.That(fieldSource, Does.Contain("public List<PendingUnitPlacement> GetPendingUnitPlacements"));
        Assert.That(fieldSource, Does.Contain("public bool HasPendingNetworkMoveFrom"));
        Assert.That(fieldSource, Does.Contain("TryReserveUnitPosition(gridPosition, data)"));
        Assert.That(fieldSource, Does.Contain("ShouldQueuePendingNetworkMove(Vector3Int from)"));
        Assert.That(fieldSource, Does.Contain("HasPendingUnitAt(from)"));
        Assert.That(fieldSource, Does.Contain("preservedPendingUnitPositions"));
        Assert.That(fieldSource, Does.Contain("preservedPendingUnitDataByPosition"));
        Assert.That(fieldSource, Does.Contain("preservedPendingNetworkMoves"));
        Assert.That(fieldSource, Does.Contain("RestorePendingStateAfterUnitMapRebuild"));

        int rebuildStart = fieldSource.IndexOf("public bool RebuildUnitMapAfterMigration", System.StringComparison.Ordinal);
        int preservePendingMoves = fieldSource.IndexOf("preservedPendingNetworkMoves", rebuildStart, System.StringComparison.Ordinal);
        int unitMapAssigned = fieldSource.IndexOf("placedUnits = rebuiltUnits;", rebuildStart, System.StringComparison.Ordinal);
        int restorePendingState = fieldSource.IndexOf("RestorePendingStateAfterUnitMapRebuild", unitMapAssigned, System.StringComparison.Ordinal);
        int replayPendingMoves = fieldSource.IndexOf("ProcessPendingNetworkMoves();", restorePendingState, System.StringComparison.Ordinal);
        Assert.That(rebuildStart, Is.GreaterThanOrEqualTo(0));
        Assert.That(preservePendingMoves, Is.GreaterThan(rebuildStart));
        Assert.That(preservePendingMoves, Is.LessThan(unitMapAssigned));
        Assert.That(restorePendingState, Is.GreaterThan(unitMapAssigned));
        Assert.That(replayPendingMoves, Is.GreaterThan(restorePendingState));

        int createdUnitAdded = fieldSource.IndexOf("placedUnits.Add(gridPosition, newUnitComponent);", System.StringComparison.Ordinal);
        int pendingMovesProcessed = fieldSource.IndexOf("ProcessPendingNetworkMoves();", createdUnitAdded, System.StringComparison.Ordinal);
        int createRosterBroadcast = fieldSource.IndexOf("BroadcastAuthoritativeUnitRoster(\"CreateUnitAt\")", createdUnitAdded, System.StringComparison.Ordinal);
        Assert.That(createdUnitAdded, Is.GreaterThanOrEqualTo(0));
        Assert.That(pendingMovesProcessed, Is.GreaterThan(createdUnitAdded));
        Assert.That(pendingMovesProcessed, Is.LessThan(createRosterBroadcast));

        Assert.That(playerSource, Does.Contain("field.HasPendingUnitAt(from)"));
        Assert.That(playerSource, Does.Contain("field.TryGetPendingUnitDataAt(from"));
        Assert.That(playerSource, Does.Contain("field.IsUnitAt(to)"));
        Assert.That(playerSource, Does.Contain("move_pending_unit_type_unknown_for_wall"));
        Assert.That(playerSource, Does.Contain("move_source_not_owned_by_player"));
    }

    [Test]
    public void MoveUnitCommandHasFinalAuthorityAndPlacementGuards()
    {
        string commandSource = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/Commands/PlayerActions/MoveUnitCommand.cs");
        string processorSource = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/Commands/Core/CommandProcessor.cs");
        string gameManagersSource = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/Managers/GameManagers.cs");
        string playerManagerSource = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/Managers/PlayerManager.cs");

        Assert.That(commandSource, Does.Contain("move_requires_prepare_phase"));
        Assert.That(commandSource, Does.Contain("field_ownership_mismatch"));
        Assert.That(commandSource, Does.Contain("move_source_not_owned_by_player"));
        Assert.That(commandSource, Does.Contain("hasFieldStateAuthority"));
        Assert.That(commandSource, Does.Contain("melee_unit_cannot_move_to_wall"));
        Assert.That(commandSource, Does.Contain("IsOwnedByPlayer"));
        Assert.That(processorSource, Does.Contain("ReceiveAndEnqueueCommand(type, intParams, stringParams, vectorParams);"));
        Assert.That(processorSource.IndexOf("ReceiveAndEnqueueCommand(type, intParams, stringParams, vectorParams);", System.StringComparison.Ordinal),
            Is.LessThan(processorSource.IndexOf("gm.RPC_BroadcastCommandToClients(type, intParams, stringParams, vectorParams);", System.StringComparison.Ordinal)));
        Assert.That(gameManagersSource, Does.Contain("if (Object != null && Object.HasStateAuthority)"));
        Assert.That(playerManagerSource, Does.Contain("gm.CommandProcessor?.ReceiveAndEnqueueCommand(type, intParams, stringParams, vectorParams);"));
        Assert.That(playerManagerSource.IndexOf("gm.CommandProcessor?.ReceiveAndEnqueueCommand(type, intParams, stringParams, vectorParams);", System.StringComparison.Ordinal),
            Is.LessThan(playerManagerSource.IndexOf("gm.RPC_BroadcastCommandToClients(type, intParams, stringParams, vectorParams);", System.StringComparison.Ordinal)));
    }

    [Test]
    public void UnitMigrationIdentityReadsAreSpawnGuarded()
    {
        const BindingFlags members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        foreach (string methodName in new[]
                 {
                     "CanReadNetworkedIdentity", "CanWriteNetworkedIdentity", "TryGetOwnerPlayerIdForRoster",
                     "GetSnapshotUnitDataKey", "TryGetNetworkedStarLevel", "UpdateLocalNetworkIdentityMirror"
                 })
        {
            Assert.That(typeof(Unit).GetMethods(members).Any(method => method.Name == methodName), Is.True, methodName);
        }
        Assert.That(typeof(Unit).GetProperty("UnitDataKeyForRoster", members), Is.Not.Null);
        Assert.That(typeof(Unit).GetProperty("StarLevelForRoster", members), Is.Not.Null);
        MethodInfo ownerGetter = typeof(Unit).GetProperty("OwnerPlayerIdForRoster", members)?.GetGetMethod(true);
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(ownerGetter, typeof(Unit), "TryGetOwnerPlayerIdForRoster"), Is.True);

        var unitObject = new GameObject("unit-migration-identity-fallback-test");
        try
        {
            var unit = unitObject.AddComponent<Unit>();
            Assert.That(unit.StarLevelForRoster, Is.EqualTo(1));
        }
        finally
        {
            Object.DestroyImmediate(unitObject);
        }
    }

    [Test]
    public void HostMigrationDurableSnapshotRestoresFieldUnitRoster()
    {
        string handlerSource = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/Network/HostMigrationHandler.cs");
        string fieldSource = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/Managers/FieldManager.cs");
        string networkSource = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/Network/NetworkManager.cs");

        Assert.That(handlerSource, Does.Contain("FieldUnitDataKeys"));
        Assert.That(handlerSource, Does.Contain("TryGetFieldUnitSnapshot"));
        Assert.That(handlerSource, Does.Contain("RestoreFieldUnitsAfterHostMigration"));
        Assert.That(handlerSource, Does.Contain("ShouldRestoreFieldUnitsForContext"));
        Assert.That(handlerSource, Does.Contain("preserving previous snapshot"));
        Assert.That(handlerSource, Does.Contain("OrderBy(kv => (kv.Value.FieldUnitFlatPositions?.Length ?? 0) > 0 ? 1 : 0)"));
        Assert.That(fieldSource, Does.Contain("TryGetFieldUnitSnapshot"));
        Assert.That(fieldSource, Does.Contain("RestoreFieldUnitsAfterHostMigration"));
        Assert.That(fieldSource, Does.Contain("suppressCombination: true"));
        Assert.That(fieldSource, Does.Contain("if (!belongsToPlayer && playerManager != null && networkRunning)"));
        Assert.That(networkSource, Does.Contain("ShouldDelayFallbackForHostMigration"));
        Assert.That(networkSource, Does.Contain("ScheduleHostMigrationFallbackGrace"));
        Assert.That(networkSource, Does.Contain("CancelPendingConnectionLossFallback();"));
    }

    [Test]
    public void RuntimeHarnessSupportsPostMigrationMoveUnitCommand()
    {
        string snapshotSource = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/Testing/MP/MPTestStateSnapshot.cs");

        Assert.That(MdfCompiledCodePolicy.ContainsStringLiteral(typeof(MPTestAutomationServer), "move_unit"), Is.True);
        foreach (string methodName in new[] { "ExecuteMoveUnitCommand", "TryFindMoveUnitPositions", "ValidateMoveUnitTarget" })
        {
            Assert.That(typeof(MPTestAutomationServer).GetMethod(methodName, BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic), Is.Not.Null, methodName);
        }
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(typeof(MPTestAutomationServer), typeof(MoveUnitCommand), ".ctor"), Is.True);
        Assert.That(snapshotSource, Does.Contain("if (MPTestCommandLine.IsEnabled)"));
        Assert.That(snapshotSource, Does.Contain("return true;"));
    }

    [Test]
    public void RuntimeHarnessProbesPostMigrationPortraitNavigationByDurablePlayerId()
    {
        const BindingFlags members = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        MethodInfo probe = typeof(MPTestAutomationServer).GetMethod("ExecuteViewPlayerFieldCommand", members);

        Assert.That(probe, Is.Not.Null);
        Assert.That(MdfCompiledCodePolicy.ContainsStringLiteral(typeof(MPTestAutomationServer), "view_player_field"), Is.True);
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(probe, typeof(GameManagers), "GetPlayer"), Is.True);
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(probe, typeof(CameraManager), "MoveToPlayerField"), Is.True);
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(probe, typeof(CameraManager), "get_CurrentViewingPlayerId"), Is.True);
    }

    [Test]
    public void BattleDeathDoesNotDropUnitsBeforePrepareRespawn()
    {
        string fieldSource = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/Managers/FieldManager.cs");

        const BindingFlags unitMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        MethodInfo deathHandler = typeof(Unit).GetMethod("HandleNetworkedDeathStateChanged", unitMembers);
        Assert.That(deathHandler, Is.Not.Null);
        Assert.That(typeof(Unit).GetMethod("SetDeathPresentationActive", unitMembers), Is.Not.Null);
        Assert.That(typeof(Unit).GetMethod("CanWriteNetworkedStats", unitMembers), Is.Not.Null);
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(deathHandler, typeof(GameObject), "SetActive"), Is.False);

        var berserkObject = new GameObject("berserk-local-stat-test");
        try
        {
            var unit = berserkObject.AddComponent<Unit>();
            typeof(Unit).GetField("_localAttackDamage", unitMembers)?.SetValue(unit, 20f);
            typeof(Unit).GetField("_localAttackSpeed", unitMembers)?.SetValue(unit, 2f);
            unit.ApplyBerserkMode();
            Assert.That(unit.currentAttackDamage, Is.EqualTo(30f).Within(0.001f));
            Assert.That(unit.currentAttackSpeed, Is.EqualTo(3f).Within(0.001f));
        }
        finally
        {
            Object.DestroyImmediate(berserkObject);
        }
        var manaObject = new GameObject("mana-fallback-test");
        try
        {
            var mana = manaObject.AddComponent<ManaController>();
            typeof(ManaController).GetField("_localCurrentMana", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(mana, 37f);
            Assert.That(mana.CurrentMana, Is.EqualTo(37f));
            Assert.That(typeof(ManaController).GetMethod("CanWriteNetworkedMana", BindingFlags.Instance | BindingFlags.NonPublic), Is.Not.Null);
        }
        finally
        {
            Object.DestroyImmediate(manaObject);
        }
        Assert.That(fieldSource, Does.Contain("bool wasAlreadyRegistered"));
        Assert.That(fieldSource, Does.Contain("inactiveOrDead && !wasAlreadyRegistered && !belongsToPlayer"));
        Assert.That(fieldSource, Does.Contain("unit.IsDead || !unit.gameObject.activeSelf || !unit.gameObject.activeInHierarchy"));
        Assert.That(fieldSource, Does.Contain("public void RespawnAllUnits()"));
        Assert.That(fieldSource, Does.Contain("bool networkRunning = runner != null && runner.IsRunning;"));
        Assert.That(fieldSource, Does.Contain("if (networkRunning && !unit.HasValidNetworkObject)"));
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(typeof(GameManagers), typeof(FieldManager), "RespawnAllUnits"), Is.True);
        Assert.That(typeof(MPTestStateSnapshot.FieldSnapshot).GetField("DeadUnitCount"), Is.Not.Null);
        Assert.That(typeof(MPTestStateSnapshot.FieldSnapshot).GetField("DeadUnitsHash"), Is.Not.Null);
        Assert.That(typeof(MPTestStateSnapshot).GetMethod("RefreshPlayerRuntimeForSnapshot", BindingFlags.Static | BindingFlags.NonPublic), Is.Not.Null);
        Assert.That(typeof(MPTestStateSnapshot).GetMethod("CapturePlayer", BindingFlags.Static | BindingFlags.NonPublic), Is.Not.Null);
    }

    [Test]
    public void BattleStartDoesNotRebuildUnitRosterFromWorldPositions()
    {
        string gameManagersSource = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/Managers/GameManagers.cs");
        string playerSource = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/Managers/PlayerManager.cs");
        string snapshotSource = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/Testing/MP/MPTestStateSnapshot.cs");

        Assert.That(playerSource, Does.Contain("bool rebuildUnitMap = true"));
        Assert.That(playerSource, Does.Contain("bool repairUnitPresentation = true"));
        Assert.That(playerSource, Does.Contain("if (rebuildUnitMap)"));
        Assert.That(playerSource, Does.Contain("repairPresentation: repairUnitPresentation"));
        Assert.That(gameManagersSource, Does.Contain("StartBattleForPlayers(Player {playerId})"));
        Assert.That(gameManagersSource, Does.Contain("rebuildUnitMap: false"));
        Assert.That(gameManagersSource, Does.Contain("repairUnitPresentation: false"));
        Assert.That(snapshotSource, Does.Contain("MPTestStateSnapshot.CapturePlayer"));
        Assert.That(snapshotSource, Does.Contain("rebuildUnitMap: false"));
        Assert.That(snapshotSource, Does.Contain("repairUnitPresentation: false"));
    }

    [Test]
    public void AiUnitOwnershipDoesNotTreatPlayerRefNoneAsDurableOwner()
    {
        string unitSource = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/Game/Units/Unit.cs");
        string fieldSource = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/Managers/FieldManager.cs");

        Assert.That(unitSource, Does.Contain("Object.InputAuthority != PlayerRef.None"));
        Assert.That(unitSource, Does.Contain("SetOwnerReference"));
        Assert.That(fieldSource, Does.Not.Contain("UnitBelongsToFieldOwnerDurable(unit) || ownedUnits.Contains(unit)"));
        Assert.That(fieldSource, Does.Contain("rosterOwnerId >= 0 && rosterOwnerId != playerManager.playerId"));
        Assert.That(fieldSource, Does.Contain("RemovePlacedUnitEntriesFromOtherFields"));
        Assert.That(PlayerCommandRequestValidator.IsUnitOwnedByPlayer(null, null), Is.False);
        Assert.That(PlayerManager.IsUnitOwnedByPlayerForCommand(null, null), Is.False);
    }

    [Test]
    public void DirectSingleplayerSceneRunnerPassesActiveRunnerGate()
    {
        const BindingFlags members = BindingFlags.Instance | BindingFlags.NonPublic;
        MethodInfo runnerGate = typeof(GameManagers).GetMethod("IsBoundToActiveRunner", members);
        MethodInfo aiGate = typeof(GameManagers).GetMethod("IsMigrationAiTakeoverReady", members);
        Assert.That(runnerGate, Is.Not.Null);
        Assert.That(aiGate, Is.Not.Null);
        Assert.That(typeof(NetworkManager).GetProperty("_runner", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic), Is.Not.Null);
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(runnerGate, typeof(NetworkManager), "get_Instance"), Is.True);
        Assert.That(MdfCompiledCodePolicy.ContainsStringLiteral(typeof(GameManagers), "hostMigrationHandler=null"), Is.True);

        var managersObject = new GameObject("single-runner-gate-test");
        try
        {
            var managers = managersObject.AddComponent<GameManagers>();
            Assert.That((bool)runnerGate.Invoke(managers, null), Is.False);
            object[] args = { null };
            Assert.That((bool)aiGate.Invoke(managers, args), Is.True, "A missing runner has no migration AI dependency.");
        }
        finally
        {
            Object.DestroyImmediate(managersObject);
        }
    }

    [Test]
    public void PlayerBuildToolBuildsAddressablesForRequestedTarget()
    {
        string buildSource = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/Testing/MP/Editor/BuildAutomation.cs");

        Assert.That(buildSource, Does.Contain("EnsureActiveBuildTarget(target, out buildTargetSwitched)"));
        Assert.That(buildSource, Does.Contain("SwitchActiveBuildTarget(group, target)"));
        Assert.That(buildSource, Does.Contain("build_addressables"));
        Assert.That(buildSource, Does.Contain("restore_build_target"));
        Assert.That(buildSource, Does.Contain("originalBuildTarget"));
        Assert.That(buildSource, Does.Contain("BuildAddressablesForTarget(target)"));
        Assert.That(buildSource, Does.Contain("AddressableAssetSettings.BuildPlayerContent(out AddressablesPlayerBuildResult result)"));
        Assert.That(buildSource, Does.Contain("finally"));
        Assert.That(buildSource, Does.Contain("SwitchActiveBuildTarget(originalBuildTargetGroup, originalBuildTarget)"));
        Assert.That(buildSource, Does.Contain("BuildScriptPackedMode.asset"));
        Assert.That(buildSource, Does.Contain("addressables = addressablesMetadata"));
    }

    [Test]
    public void AttackSequenceMonsterSelectionTracksAuthorityPoolSlot()
    {
        const BindingFlags members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        Assert.That(typeof(AttackSequenceManager).GetField("_selectedMonsterSlotIndex", members), Is.Not.Null);
        foreach (string methodName in new[]
                 {
                     "SelectMonsterSlot", "TryResolveSelectedMonster", "HasPendingBattleSpawnForCurrentSnapshot",
                     "MarkPendingBattleSpawn", "SuppressBattleMapInputForCurrentPointer"
                 })
        {
            Assert.That(typeof(AttackSequenceManager).GetMethods(members).Any(method => method.Name == methodName), Is.True, methodName);
        }
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(typeof(AttackSequenceUIController), typeof(AttackSequenceManager), "SelectMonsterSlot"), Is.True);
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(typeof(AttackSequenceUIController), typeof(GamePrepareUIToolkitController), "TrySyncMonsterSelectionFromLegacy"), Is.True);
        Assert.That(typeof(AttackSequenceUIController).GetMethod("SelectFirstAvailableMonsterSlot", members), Is.Null);
    }

    [Test]
    public void AiFillIdentityReplicatesToClientSnapshots()
    {
        PropertyInfo aiControlled = typeof(PlayerManager).GetProperty("IsAiControlled", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.That(aiControlled, Is.Not.Null);
        Assert.That(aiControlled.PropertyType, Is.EqualTo(typeof(Fusion.NetworkBool)));
        Assert.That(aiControlled.GetCustomAttributes(false).Any(attribute =>
            attribute.GetType().Name == "NetworkedAttribute" || attribute.GetType().Name == "NetworkedWeavedAttribute"), Is.True);
        Assert.That(typeof(PlayerManager).GetMethod("SetAiControlled", new[] { typeof(bool) }), Is.Not.Null);
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(typeof(GameManagers), typeof(PlayerManager), "SetAiControlled"), Is.True);
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(typeof(MPTestStateSnapshot), typeof(PlayerManager), "get_IsAiControlled"), Is.True);
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(typeof(MPTestStateSnapshot), typeof(ComponentRegistry), "Has"), Is.True);
    }

    [Test]
    public void ManualSkillSnapshotAvoidsFrameLocalReadinessInputs()
    {
        MethodInfo capture = typeof(MPTestStateSnapshot).GetMethod("CaptureManualSkillReadyHash", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(capture, Is.Not.Null);
        Assert.That(MdfCompiledCodePolicy.ContainsStringLiteralFragment(capture, "skill="), Is.True);
        foreach (string getter in new[]
                 {
                     "get_LoadedSkillData", "IsManualOrAiStrategicSkill",
                     "get_SkillCurrentMana", "get_SkillMaxMana", "CountSkillTargets", "HasSkillTargetsAvailable"
                 })
        {
            Assert.That(MdfCompiledCodePolicy.ReferencesMethod(capture, typeof(Unit), getter), Is.False, getter);
        }
        Assert.That(MdfCompiledCodePolicy.ReferencesField(capture, typeof(Unit), "currentSkillActivationType"), Is.False);
        Assert.That(MdfCompiledCodePolicy.ContainsStringLiteralFragment(capture, "manaBucket="), Is.False);
        Assert.That(MdfCompiledCodePolicy.ContainsStringLiteralFragment(capture, "ready="), Is.False);
    }

    [Test]
    public void PrepareDecisionPolicyPreservesExpectedActionOrder()
    {
        string source = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/AI/Planning/PrepareDecisionPolicy.cs");

        Assert.That(source, Does.Contain("yield return TryChooseAugment"));
        Assert.That(source.IndexOf("yield return TryChooseBuy;", System.StringComparison.Ordinal),
            Is.LessThan(source.IndexOf("yield return TryChooseReroll;", System.StringComparison.Ordinal)));
        Assert.That(source.IndexOf("yield return TryChooseReroll;", System.StringComparison.Ordinal),
            Is.LessThan(source.IndexOf("yield return TryChooseWall;", System.StringComparison.Ordinal)));
        Assert.That(source.IndexOf("yield return TryChooseWall;", System.StringComparison.Ordinal),
            Is.LessThan(source.IndexOf("yield return TryChooseMoveFromDefaultArea;", System.StringComparison.Ordinal)));
        Assert.That(source, Does.Contain("ShouldPrioritizeBuyBeforeWall"));
        Assert.That(source, Does.Contain("ShouldPrioritizeWallControl"));
        Assert.That(source, Does.Contain("PrepareRoutineStage"));
        Assert.That(source, Does.Contain("_prepareStageByPlayerRound"));
        Assert.That(source, Does.Contain("RememberPrepareRoutineStage(player, round, PrepareRoutineStage.Maze);"));
        Assert.That(source, Does.Not.Contain("RememberPrepareRoutineStage(player, round, PrepareRoutineStage.Placement);"));
        Assert.That(source, Does.Contain("GetPrepareRoutineStage(player, round) >= PrepareRoutineStage.Maze"));
        Assert.That(source, Does.Contain("GetPolicyUnits(player, field)"));
        Assert.That(source, Does.Contain("IsOwnedByPlayer(player, unit)"));
        Assert.That(source, Does.Contain("TryChoosePendingPurchasedUnitMove"));
        Assert.That(source, Does.Contain("GetPendingUnitPlacements()"));
        Assert.That(source, Does.Contain("pendingPurchasedUnit"));
        Assert.That(source, Does.Contain("pendingMoveSourceSuppression"));
        Assert.That(source, Does.Contain("wallUnblockDoesNotConsumePlacementMove"));
        Assert.That(source, Does.Contain("TryChooseMoveBlockingWall"));
        Assert.That(source, Does.Contain("TryFindBlockingWallPlanUnit"));
        Assert.That(source, Does.Contain("TryChooseMoveFromDefaultArea"));
        Assert.That(source, Does.Contain("default_area_unit_reposition"));
        Assert.That(source, Does.Contain("defaultAreaPriority"));
        Assert.That(source, Does.Contain("_lastMoveRoundByUnitKey"));
        Assert.That(source, Does.Contain("_wallUnblockMoveRoundByUnitKey"));
        Assert.That(source, Does.Contain("_pendingMoveSourceRoundByCellKey"));
        Assert.That(source, Does.Contain("_pendingMoveTargetRoundByCellKey"));
        Assert.That(source, Does.Contain("_pendingWallRoundByCellKey"));
        Assert.That(source, Does.Contain("_builtWallCellKeys"));
        Assert.That(source, Does.Contain("_pendingBuyRoundBySlotKey"));
        Assert.That(source, Does.Contain("move_unit_off_wall_blueprint"));
        Assert.That(source, Does.Contain("blockedWallBlueprint"));
        Assert.That(source, Does.Contain("moveOncePerRound"));
        Assert.That(source, Does.Contain("pendingMoveTargetSuppression"));
        Assert.That(source, Does.Contain("pendingWallSuppression"));
        Assert.That(source, Does.Contain("pendingRound == round"));
        Assert.That(source, Does.Contain("persistentWallBlueprint"));
        Assert.That(source, Does.Contain("IsReservedUnbuiltWallPlanCell"));
        Assert.That(source, Does.Contain("pendingBuySuppression"));
        Assert.That(source, Does.Contain("CompositionDistanceToTarget <= 2"));
        Assert.That(source, Does.Contain("WouldCloseLastOpenBorderGap"));
        Assert.That(source, Does.Contain("remainingOpenGapsAfterCandidate"));
        Assert.That(source, Does.Contain("HasRepairableMissingWallPlan"));
        Assert.That(source, Does.Contain("HasRecordedBuiltWallCandidate"));
        Assert.That(source, Does.Contain("TryGetRepairWallPosition"));
        Assert.That(source, Does.Contain("maze_policy_repair_missing_wall"));
        Assert.That(source, Does.Contain("repairMissingMazeWall"));
        Assert.That(source, Does.Contain("round < 2"));
        Assert.That(source, Does.Contain("GetWallBuildReserve"));
        Assert.That(source, Does.Contain("MinimumRepairReserveWalls"));
        Assert.That(source, Does.Contain("cached.FieldInstanceId == fieldInstanceId"));
        Assert.That(source, Does.Contain("cached.Signature == signature"));
        Assert.That(source, Does.Contain("player.GetWallCount() > GetWallBuildReserve(player)"));
        Assert.That(source, Does.Contain("OrderBy(unit => unit.Data.unitType == UnitType.Ranged ? 0 : 1)"));
        Assert.That(source, Does.Contain("OrderBy(entry => entry.UnitData != null && entry.UnitData.unitType == UnitType.Ranged ? 0 : 1)"));
        Assert.That(source, Does.Contain("sold_slots_below_3"));
        Assert.That(source, Does.Contain("high_value_affordable_purchase_remaining"));
        Assert.That(source, Does.Contain("GetPresentedAugmentSnapshotNames"));
        Assert.That(source, Does.Contain("IsShopSlotSoldForPolicy"));
        Assert.That(source, Does.Contain("TryGetShopSnapshot"));
        Assert.That(source, Does.Contain("field.IsUnitAt(to"));
        Assert.That(source, Does.Contain("GetMoveCandidatePriority"));
        Assert.That(source, Does.Contain("IsLikelyPurchaseDefaultArea"));
    }

    [Test]
    public void PrepareDecisionPolicyPendingMoveSuppressionDoesNotBlockNextRound()
    {
        string source = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/AI/Planning/PrepareDecisionPolicy.cs");

        int sourceStart = source.IndexOf("private bool IsPendingMoveSource", System.StringComparison.Ordinal);
        int targetStart = source.IndexOf("private bool IsPendingMoveTarget", System.StringComparison.Ordinal);
        Assert.That(sourceStart, Is.GreaterThanOrEqualTo(0));
        Assert.That(targetStart, Is.GreaterThanOrEqualTo(0));

        int sourceEnd = source.IndexOf("private void RememberPendingMoveSource", sourceStart, System.StringComparison.Ordinal);
        int targetEnd = source.IndexOf("private void RememberMoveTarget", targetStart, System.StringComparison.Ordinal);
        Assert.That(sourceEnd, Is.GreaterThan(sourceStart));
        Assert.That(targetEnd, Is.GreaterThan(targetStart));

        string sourceMethod = source.Substring(sourceStart, sourceEnd - sourceStart);
        string targetMethod = source.Substring(targetStart, targetEnd - targetStart);

        Assert.That(sourceMethod, Does.Contain("pendingRound == round"));
        Assert.That(targetMethod, Does.Contain("pendingRound == round"));
        Assert.That(sourceMethod, Does.Not.Contain("round - 1"));
        Assert.That(targetMethod, Does.Not.Contain("round - 1"));
        Assert.That(source, Does.Contain("lastRound == round"));
        Assert.That(source, Does.Contain("_lastMoveRoundByUnitKey"));
        Assert.That(source, Does.Contain("_wallUnblockMoveRoundByUnitKey"));
        Assert.That(source, Does.Contain("_pendingWallRoundByCellKey"));
        Assert.That(source, Does.Contain("_pendingBuyRoundBySlotKey"));
        Assert.That(source, Does.Contain(".Where(pair => pair.Value < round)"));
        Assert.That(source, Does.Not.Contain("pendingRound >= round - 1"));
    }

    [Test]
    public void FieldManagerRestoresDragNetworkTransformForRoundTransitionsAndInvalidDrops()
    {
        string source = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/Managers/FieldManager.InputPresentation.cs");
        string stateSource = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/Managers/FieldManager.cs");

        Assert.That(source, Does.Contain("RestoreSelectedUnitNetworkTransform"));
        Assert.That(source, Does.Contain("originalUnitPosition = GetUnitPosition(selectedUnit) ?? WorldToGridInt(selectedUnit.transform.position);"));
        Assert.That(source, Does.Contain("ShouldAllowUnitDragThroughPrepareToolkit"));
        Assert.That(source, Does.Contain("MdfInput.IsPointerOverFieldBlockingUI()"));
        Assert.That(source, Does.Contain("GamePrepareUIToolkitController.IsPointerOverBlockingElement(MdfInput.PointerPosition)"));
        Assert.That(source, Does.Contain("TryRequestRemoveWallAt"));
        Assert.That(source, Does.Contain("new RemoveWallCommand(playerManager.playerId, gridPosition)"));

        int stateChangeStart = stateSource.IndexOf("private void HandleGameStateChange", System.StringComparison.Ordinal);
        int stateChangeEnd = stateSource.IndexOf("#endregion", stateChangeStart, System.StringComparison.Ordinal);
        Assert.That(stateChangeStart, Is.GreaterThanOrEqualTo(0));
        Assert.That(stateChangeEnd, Is.GreaterThan(stateChangeStart));
        string stateChangeMethod = stateSource.Substring(stateChangeStart, stateChangeEnd - stateChangeStart);
        Assert.That(stateChangeMethod, Does.Contain("RestoreSelectedUnitNetworkTransform();"));
        Assert.That(stateChangeMethod.IndexOf("RestoreSelectedUnitNetworkTransform();", System.StringComparison.Ordinal),
            Is.LessThan(stateChangeMethod.IndexOf("SnapbackSelectedUnit(originalWorldPos);", System.StringComparison.Ordinal)));

        int releaseStart = source.IndexOf("if (MdfInput.PrimaryPointerWasReleasedThisFrame() && selectedUnit != null)", System.StringComparison.Ordinal);
        int releaseEnd = source.IndexOf("private bool TryRequestRemoveWallAt", releaseStart, System.StringComparison.Ordinal);
        Assert.That(releaseStart, Is.GreaterThanOrEqualTo(0));
        Assert.That(releaseEnd, Is.GreaterThan(releaseStart));
        string releaseBlock = source.Substring(releaseStart, releaseEnd - releaseStart);
        Assert.That(releaseBlock, Does.Contain("RestoreSelectedUnitNetworkTransform();"));
        Assert.That(releaseBlock.IndexOf("RestoreSelectedUnitNetworkTransform();", System.StringComparison.Ordinal),
            Is.LessThan(releaseBlock.IndexOf("Vector3Int bestGrid = GetBestGridUnderMouse();", System.StringComparison.Ordinal)));
    }

    [Test]
    public void PrepareDecisionPolicyUsesMonsterPathForHumanBotRepositioning()
    {
        string source = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/AI/Planning/PrepareDecisionPolicy.cs");

        Assert.That(source, Does.Contain("BuildMonsterPathContext"));
        Assert.That(source, Does.Contain("FindBestSpotForAI(unit.Data, monsterPath"));
        Assert.That(source, Does.Contain("TryGetSingleOpenEntryNavigationCell"));
        Assert.That(source, Does.Contain("GetOpenBorderGaps"));
        Assert.That(source, Does.Contain("ConvertNavigationPathToInnerField"));
        Assert.That(source, Does.Contain("pathAwarePlacement"));
        Assert.That(source, Does.Contain("monsterPathCount"));

        string coverageSource = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/AI/UtilitySystem/Considerations/Placement/AttackRangeCoverageConsideration.cs");
        Assert.That(coverageSource, Does.Contain("BuildTargetTiles"));
        Assert.That(coverageSource, Does.Contain("context.MonsterPath"));

        string fieldSource = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/Managers/FieldAiPlacementService.cs");
        Assert.That(fieldSource, Does.Contain("FilterRangedCandidatesForMonsterPath"));
        Assert.That(fieldSource, Does.Contain("SelectPreferredRangedCandidateTier"));
        Assert.That(fieldSource, Does.Contain("FindBestRangedFallbackAwayFromOriginal"));
        Assert.That(fieldSource, Does.Contain("FindBestMeleeFallbackAwayFromOriginal"));
        Assert.That(fieldSource, Does.Contain("ShouldForceMoveAwayFromOriginal"));
        Assert.That(fieldSource, Does.Contain("IsStrictInteriorRangedCell"));
        Assert.That(fieldSource, Does.Contain("HasAvailablePlacementTile"));
        Assert.That(fieldSource, Does.Contain("IsOuterRingCell"));
        Assert.That(fieldSource, Does.Contain("IsNearFieldEdgeCell"));
        Assert.That(fieldSource, Does.Contain("CountCoveredMonsterPathTiles"));
        Assert.That(fieldSource, Does.Contain("coverageByTile"));
        Assert.That(fieldSource, Does.Contain("minStrongCovered"));
        Assert.That(fieldSource, Does.Contain("centralStrongCoverageTiles"));
        Assert.That(fieldSource, Does.Contain("maxCoverageTiles"));
        Assert.That(fieldSource, Does.Contain("kvp.Value == maxCovered"));
        Assert.That(fieldSource, Does.Contain("centralMaxCoverageTiles"));
        Assert.That(fieldSource, Does.Contain("centralAnyCoverageTiles"));
        Assert.That(fieldSource, Does.Contain("minCentralCovered"));
        Assert.That(fieldSource, Does.Contain("CalculateFieldCenterScore(tile) >= 0.55f"));
        Assert.That(fieldSource, Does.Contain("CalculateFieldCenterScore(tile) >= 0.45f"));
        Assert.That(fieldSource, Does.Contain("centralInteriorTiles"));
        Assert.That(fieldSource, Does.Contain("CalculateRangedPathPriorityBonus"));
        Assert.That(fieldSource, Does.Contain("CalculateNearbyRangedAllyBonus"));
        Assert.That(fieldSource, Does.Contain("ally.Data.unitType != UnitType.Ranged"));
        Assert.That(fieldSource, Does.Contain("currentScore += CalculateNearbyRangedAllyBonus(tilePos, alliedUnits);"));
        Assert.That(fieldSource, Does.Contain("CalculateFieldCenterScore"));
        Assert.That(fieldSource, Does.Contain("covered * 4.0f"));
        Assert.That(fieldSource, Does.Contain("centerScore * 8.0f"));
        Assert.That(fieldSource, Does.Contain("currentScore -= 20.0f"));
        Assert.That(fieldSource, Does.Contain("currentScore += 20.0f"));
        Assert.That(fieldSource, Does.Contain("return new List<Vector3Int>();"));
        Assert.That(fieldSource, Does.Contain("return movingUnitOriginalPos;"));

        var fieldObject = new GameObject("placement-policy-field-test");
        try
        {
            var field = fieldObject.AddComponent<FieldManager>();
            field.gridSize = new Vector2Int(3, 2);
            Assert.That(field.GetValidPlacementTiles(UnitType.Melee), Has.Count.EqualTo(6),
                "Every empty melee cell should remain a valid candidate when no walls exist.");
        }
        finally
        {
            Object.DestroyImmediate(fieldObject);
        }

        string mazeSource = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/AI/Planning/MazePlanner.cs");
        Assert.That(mazeSource, Does.Contain("PruneRedundantWalls"));
        Assert.That(mazeSource, Does.Contain("lengthWithoutWall > currentLength"));
        Assert.That(mazeSource, Does.Contain("harmful maze walls that shortened the final monster path when kept"));

        int planStart = mazeSource.IndexOf("private static MazePlanResult PlanWallsFromInput", System.StringComparison.Ordinal);
        int planEnd = mazeSource.IndexOf("private static MazePlanResult PlanAdditionalWallsFromInput", System.StringComparison.Ordinal);
        Assert.That(planStart, Is.GreaterThanOrEqualTo(0));
        Assert.That(planEnd, Is.GreaterThan(planStart));
        string runtimePlanMethod = mazeSource.Substring(planStart, planEnd - planStart);
        Assert.That(runtimePlanMethod, Does.Contain("GenerateBudgetedMaze"));
        Assert.That(runtimePlanMethod, Does.Not.Contain("GenerateFlawlessMazeWithFixedEndpoints"));
    }

    [Test]
    public void PrepareCompositionClassifiesClericSkillHealAsHealer()
    {
        var cleric = CreateUnitForPrepareTest("UnitData_Cleric", "Cleric", UnitType.Ranged, new[] { "Skill_Heal", "Skill_Heal", "Skill_Heal" });
        try
        {
            Assert.That(UnitCompositionAnalyzer.Classify(cleric), Is.EqualTo(PrepareUnitRole.Healer));
        }
        finally
        {
            Object.DestroyImmediate(cleric);
        }
    }

    [Test]
    public void PrepareCompositionCountsMeleeRangedAndHealer()
    {
        var melee = CreateUnitForPrepareTest("UnitData_Warrior", "Warrior", UnitType.Melee);
        var extraMelee = CreateUnitForPrepareTest("UnitData_Guardian", "Guardian", UnitType.Melee);
        var ranged = CreateUnitForPrepareTest("UnitData_Archer", "Archer", UnitType.Ranged);
        var healer = CreateUnitForPrepareTest("UnitData_Cleric", "Cleric", UnitType.Ranged, new[] { "Skill_Heal", "Skill_Heal", "Skill_Heal" });
        try
        {
            var composition = new PrepareArmyComposition();
            composition.AddUnit(melee, 1);
            composition.AddUnit(ranged, 1);
            composition.AddUnit(healer, 1);

            Assert.That(composition.MeleeCount, Is.EqualTo(1));
            Assert.That(composition.RangedDpsCount, Is.EqualTo(1));
            Assert.That(composition.HealerCount, Is.EqualTo(1));
            Assert.That(composition.FieldUnitCount, Is.EqualTo(3));
        }
        finally
        {
            Object.DestroyImmediate(melee);
            Object.DestroyImmediate(ranged);
            Object.DestroyImmediate(healer);
        }
    }

    [Test]
    public void PrepareBuyScoringPrefersMissingRoleAndPenalizesOverrepresentedRole()
    {
        var melee = CreateUnitForPrepareTest("UnitData_Warrior", "Warrior", UnitType.Melee);
        var extraMelee = CreateUnitForPrepareTest("UnitData_Guardian", "Guardian", UnitType.Melee);
        var ranged = CreateUnitForPrepareTest("UnitData_Archer", "Archer", UnitType.Ranged);
        var healer = CreateUnitForPrepareTest("UnitData_Cleric", "Cleric", UnitType.Ranged, new[] { "Skill_Heal", "Skill_Heal", "Skill_Heal" });
        try
        {
            var composition = new PrepareArmyComposition();
            composition.AddUnit(melee, 1);
            composition.AddUnit(melee, 1);
            composition.AddUnit(melee, 1);
            composition.AddUnit(melee, 1);

            var meleeScore = PrepareDecisionPolicy.ScoreShopItemForTest(new ShopItem(extraMelee, 1), composition, 20);
            var rangedScore = PrepareDecisionPolicy.ScoreShopItemForTest(new ShopItem(ranged, 1), composition, 20);
            var healerScore = PrepareDecisionPolicy.ScoreShopItemForTest(new ShopItem(healer, 1), composition, 20);

            Assert.That(rangedScore.FinalScore, Is.GreaterThan(meleeScore.FinalScore));
            Assert.That(healerScore.FinalScore, Is.GreaterThan(meleeScore.FinalScore));
            Assert.That(meleeScore.RoleOverTargetPenalty, Is.LessThan(0f));
            Assert.That(rangedScore.RoleDeficitBonus, Is.GreaterThan(0f));
        }
        finally
        {
            Object.DestroyImmediate(melee);
            Object.DestroyImmediate(extraMelee);
            Object.DestroyImmediate(ranged);
            Object.DestroyImmediate(healer);
        }
    }

    [Test]
    public void PrepareBuyScoringRewardsThreeOfKindMergeOpportunity()
    {
        var archer = CreateUnitForPrepareTest("UnitData_Archer", "Archer", UnitType.Ranged);
        try
        {
            var composition = new PrepareArmyComposition();
            composition.AddUnit(archer, 1);
            composition.AddUnit(archer, 1);

            var score = PrepareDecisionPolicy.ScoreShopItemForTest(new ShopItem(archer, 1), composition, 20);

            Assert.That(score.MatchingSameUnitSameStarCount, Is.EqualTo(2));
            Assert.That(score.MergeBonus, Is.GreaterThanOrEqualTo(80f));
            Assert.That(score.FinalScore, Is.GreaterThan(80f));
        }
        finally
        {
            Object.DestroyImmediate(archer);
        }
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public void PrepareRerollGateRejectsBeforeThreeSoldSlots(int soldSlots)
    {
        var gate = PrepareDecisionPolicy.EvaluateRerollGateForTest(
            shopReady: true,
            isPreparePhase: true,
            playerReady: true,
            gold: 20,
            rerollCost: 2,
            shopSlotCount: 5,
            soldSlotCount: soldSlots,
            bestAffordablePurchaseScore: 0f);

        Assert.That(gate.CanReroll, Is.False);
        Assert.That(gate.Reason, Is.EqualTo("sold_slots_below_3"));
    }

    [Test]
    public void PrepareRerollGateAllowsLateShopWhenNoHighValuePurchaseRemains()
    {
        var gate = PrepareDecisionPolicy.EvaluateRerollGateForTest(
            shopReady: true,
            isPreparePhase: true,
            playerReady: true,
            gold: 20,
            rerollCost: 2,
            shopSlotCount: 5,
            soldSlotCount: 3,
            bestAffordablePurchaseScore: 5f);

        Assert.That(gate.CanReroll, Is.True);
        Assert.That(gate.Reason, Is.EqualTo("gate_passed"));
    }

    [Test]
    public void PrepareRerollGateRejectsHighValueAffordablePurchase()
    {
        var gate = PrepareDecisionPolicy.EvaluateRerollGateForTest(
            shopReady: true,
            isPreparePhase: true,
            playerReady: true,
            gold: 20,
            rerollCost: 2,
            shopSlotCount: 5,
            soldSlotCount: 3,
            bestAffordablePurchaseScore: 40f);

        Assert.That(gate.CanReroll, Is.False);
        Assert.That(gate.Reason, Is.EqualTo("high_value_affordable_purchase_remaining"));
    }

    [Test]
    public void MazePersonaPrioritizesBuyingWhenArmyCoreIsEmpty()
    {
        var empty = new PrepareArmyComposition();
        var core = new PrepareArmyComposition();
        var melee = CreateUnitForPrepareTest("UnitData_Warrior", "Warrior", UnitType.Melee);
        var ranged = CreateUnitForPrepareTest("UnitData_Archer", "Archer", UnitType.Ranged);
        try
        {
            core.AddUnit(melee, 1);
            core.AddUnit(melee, 1);
            core.AddUnit(ranged, 1);

            Assert.That(PrepareDecisionPolicy.ShouldPrioritizeBuyBeforeWall(empty, "maze"), Is.True);
            Assert.That(PrepareDecisionPolicy.ShouldPrioritizeBuyBeforeWall(core, "maze"), Is.False);
            Assert.That(PrepareDecisionPolicy.ShouldPrioritizeBuyBeforeWall(empty, "balanced"), Is.True);
        }
        finally
        {
            Object.DestroyImmediate(melee);
            Object.DestroyImmediate(ranged);
        }
    }

    [Test]
    public void LegacyHumanBotPolicyIsNotConstructedByRuntimeDriver()
    {
        Assert.That(typeof(MPTestHumanBotDriver).GetField("_preparePolicy", BindingFlags.Instance | BindingFlags.NonPublic)?.FieldType,
            Is.EqualTo(typeof(PrepareDecisionPolicy)));
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(typeof(MPTestHumanBotDriver), typeof(MPTestHumanBotPolicy), ".ctor"), Is.False);
    }

    [Test]
    public void BattleDecisionLayerRoutesOnlyThroughCommandEmitters()
    {
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(typeof(BattleDecisionPolicy), typeof(BattleSpawnMonsterCommand), ".ctor"), Is.True);
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(typeof(BattleDecisionPolicy), typeof(UseMagicScrollCommand), ".ctor"), Is.True);
        Assert.That(typeof(BattleDecisionPolicy).GetField("MinimumBattleCommandLeadTime", BindingFlags.Static | BindingFlags.NonPublic), Is.Not.Null);
        Assert.That(typeof(BattleDecisionPolicy).GetField("_defenderSkillPolicy", BindingFlags.Instance | BindingFlags.NonPublic)?.FieldType,
            Is.EqualTo(typeof(DefenderSkillPolicy)));
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(typeof(BattleDecisionPolicy), typeof(MonsterSpawner), "SpawnMonsterAtPositionAsync"), Is.False);
        Assert.That(MdfCompiledCodePolicy.ReferencesAnyMethod(
            typeof(BattleDecisionPolicy),
            typeof(PlayerManager),
            "TryReserveBattleSpawnResource", "CommitBattleSpawnReservation", "TryRefundBattleSpawnReservation"), Is.False);
        Assert.That(MdfCompiledCodePolicy.ReferencesAnyMethod(typeof(BattleDecisionPolicy), typeof(PlayerManager), "TryConsumeMagicScrollSlot"), Is.False);
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(typeof(BattleDecisionPolicy), typeof(SkillEffect), "CastGameplay"), Is.False);
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(typeof(BattleDecisionPolicy), typeof(Unit), "ActivateSkill"), Is.False);

        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(typeof(MdfCommandEmitter), typeof(CommandProcessor), "RequestCommandExecution"), Is.True);
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(typeof(HumanClientCommandEmitter), typeof(GameManagers), "ExecuteBattleSpawnMonsterCommandAsync"), Is.True);
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(typeof(HumanClientCommandEmitter), typeof(GameManagers), "RPC_RequestBattleSpawnMonster"), Is.True);
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(typeof(HumanClientCommandEmitter), typeof(PlayerManager), "MarkAttackMonsterPoolCommandSubmitted"), Is.True);
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(typeof(HumanClientCommandEmitter), typeof(GameManagers), "ExecuteUseMagicScrollCommandAsync"), Is.True);
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(typeof(HumanClientCommandEmitter), typeof(GameManagers), "RPC_RequestUseMagicScrollCommand"), Is.True);
    }

    [Test]
    public void ClientBattleOpponentSnapshotsPreferNetworkedStateOverLocalCache()
    {
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(typeof(GameManagers), typeof(GameManagers), "TryReadNetworkedBattleOpponentSnapshot"), Is.True);
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(typeof(GameManagers), typeof(GameManagers), "TryReadNetworkedMatchFirstAttackerSnapshot"), Is.True);
        Assert.That(typeof(GameManagers).GetField("_battleOpponents", BindingFlags.Instance | BindingFlags.NonPublic), Is.Not.Null);
        Assert.That(typeof(GameManagers).GetField("_matchFirstAttacker", BindingFlags.Instance | BindingFlags.NonPublic), Is.Not.Null);
    }

    [Test]
    public void GameToEndRunnerDoesNotFailWhenTransientCheckpointStateSlips()
    {
        string source = MdfSourcePolicy.ReadStaticContract("../tools/harness/mp/long_progression_common.py");

        Assert.That(source, Does.Contain("state_slipped_success"));
        Assert.That(source, Does.Contain("checkpoint_state_slipped_before_capture"));
        Assert.That(source, Does.Contain("\"skipped\": state_slipped_success"));
    }

    [Test]
    public void AttackStrategyEvaluatesFullPathAndKeepsTankSpawnOnGroundRoute()
    {
        string source = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/AI/BehaviorTree/Nodes/Actions/AIAttackStrategy.cs");

        Assert.That(source, Does.Contain("var candidates = kvp.Value;"));
        Assert.That(source, Does.Not.Contain("GetClosestCandidates(kvp.Value"));
        Assert.That(source, Does.Contain("phase0.Orders.Add(new AISpawnOrder(firstTank, groundSpawnPos"));
        Assert.That(source, Does.Contain("phase1.Orders.Add(new AISpawnOrder(entry, destroyerSpawnPos"));
    }

    [Test]
    public void BehaviorTreeV2DoesNotMutateDurableStateInPoliciesOrTestLogging()
    {
        string loggerSource = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/Testing/MP/MPTestLogger.cs");

        Assert.That(MdfCompiledCodePolicy.ReferencesField(typeof(PrepareDecisionPolicy), typeof(PlayerManager), "unitPurchaseComplete"), Is.False);
        Assert.That(loggerSource, Does.Contain("#if !(UNITY_EDITOR || DEVELOPMENT_BUILD)"));
        Assert.That(loggerSource, Does.Contain("!options.Enabled"));
        Assert.That(MdfCompiledCodePolicy.ContainsStringLiteral(typeof(TestAutomationCommandEmitter), "missing_mp_test"), Is.True);
    }

    [Test]
    public void NotificationDoesNotApplyPeerPersistentEffects()
    {
        Assert.That(MdfCompiledCodePolicy.ReferencesField(typeof(NotifyAugmentSelectedCommand), typeof(PlayerManager), "chosenAugments"), Is.False);
        Assert.That(MdfCompiledCodePolicy.ReferencesField(typeof(NotifyAugmentSelectedCommand), typeof(PlayerManager), "permanentAttackDamagePercent"), Is.False);
        Assert.That(MdfCompiledCodePolicy.ReferencesField(typeof(NotifyAugmentSelectedCommand), typeof(PlayerManager), "permanentAttackSpeedPercent"), Is.False);
        Assert.That(MdfCompiledCodePolicy.ReferencesAnyMethod(
            typeof(NotifyAugmentSelectedCommand),
            typeof(PlayerManager),
            "AddOwnedBoss", "RegisterActiveMonsterSummonAugment", "GetPresentedAugments"), Is.False);
        Assert.That(typeof(MPTestStateSnapshot).GetMethod("EnumerateSelectedAugmentsForSnapshot", BindingFlags.Static | BindingFlags.NonPublic), Is.Not.Null);
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(typeof(MPTestStateSnapshot), typeof(PlayerManager), "GetSelectedAugmentSnapshotNames"), Is.True);
    }

    [Test]
    public void FieldRosterReconcileKeepsRegistrationMetadataForCompactRosterOrdering()
    {
        string fieldSource = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/Managers/FieldManager.cs");
        const BindingFlags members = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        Assert.That(typeof(PlayerManager).GetField("_latestUnitRegistrationById", members), Is.Not.Null);
        Assert.That(typeof(PlayerManager).GetField("_pendingUnitRosterDataKeyHashes", members), Is.Not.Null);
        foreach (string methodName in new[] { "RememberLatestUnitRegistration", "ResolveUnitDataKeyByStableHashAsync", "StableUnitDataKeyHash" })
        {
            Assert.That(typeof(PlayerManager).GetMethods(members).Any(method => method.Name == methodName), Is.True, methodName);
        }
        Assert.That(fieldSource, Does.Contain("compactRoster[0] = -2"));
        Assert.That(fieldSource, Does.Contain("UnitDataKeyHash"));
        Assert.That(fieldSource, Does.Contain("StableUnitDataKeyHash(GetUnitDataRegistrationKey(entry.Value))"));
        Assert.That(fieldSource, Does.Contain("ReconcileClientUnitMapFromWorldIfNeeded"));
        Assert.That(fieldSource, Does.Contain("ClientRoster.{context}"));
        Assert.That(fieldSource, Does.Contain("playerManager.Object.HasStateAuthority"));
        Assert.That(fieldSource, Does.Contain("UnitHasReplicatedFieldOwner"));
        Assert.That(fieldSource, Does.Contain("SyncUnitPlacementIdentity"));
        Assert.That(fieldSource, Does.Contain("RPC_ReconcileUnitRosterCompact(compactRoster)"));
        Assert.That(fieldSource, Does.Contain("RPC_ReconcilePlayerUnitRosterCompact(playerManager.playerId, compactRoster)"));
        Assert.That(fieldSource, Does.Not.Contain("Full unit roster broadcast failed"));
        Assert.That(typeof(Unit).GetProperty("NetworkedOwnerPlayerId", members), Is.Not.Null);
        Assert.That(typeof(Unit).GetProperty("NetworkedHasOwnerPlayerId", members), Is.Not.Null);
        Assert.That(typeof(Unit).GetProperty("OwnerPlayerIdForRoster", members), Is.Not.Null);
        Assert.That(typeof(Unit).GetMethod("SyncFieldPlacementIdentity", members), Is.Not.Null);
        Assert.That(typeof(GameManagers).GetMethod("RPC_ReconcilePlayerUnitRosterCompact", members), Is.Not.Null);
        Assert.That(typeof(PlayerManager).GetMethod("ApplyCompactUnitRosterFromAuthority", members), Is.Not.Null);
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(typeof(GameManagers), typeof(PlayerManager), "ApplyCompactUnitRosterFromAuthority"), Is.True);
    }

    [Test]
    public void MeleeUnitsRecheckRangeBeforeApplyingDamage()
    {
        const BindingFlags members = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        foreach (string methodName in new[]
                 {
                     "PruneBlockedMonsters", "IsCurrentTargetValidForAttack", "IsTargetWithinAttackRange",
                     "IsPendingMeleeAttackStillValid", "GetClosestTargetPoint"
                 })
        {
            Assert.That(typeof(Unit).GetMethods(members).Any(method => method.Name == methodName), Is.True, methodName);
        }
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(typeof(Unit), typeof(Monster), "Unblock"), Is.True);
    }

    [Test]
    public void AttackMonsterPoolSnapshotApplyDoesNotDropUnresolvedEntries()
    {
        string playerSource = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/Managers/PlayerManager.cs");

        Assert.That(playerSource, Does.Contain("ResolveAttackMonsterDataAsync"));
        Assert.That(playerSource, Does.Contain("FindLoadedMonsterDataByName"));
        Assert.That(playerSource, Does.Contain("FindWaveMonsterDataByName"));
        Assert.That(playerSource, Does.Contain("AttackMonsterPool snapshot apply aborted"));
        Assert.That(playerSource, Does.Not.Match(@"if \(monsterData == null\)\s*\{\s*continue;"));
        Assert.That(typeof(AugmentManager).GetMethod("FindMonsterDataByName", BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic), Is.Not.Null);
        Assert.That(typeof(AugmentData).GetField("bossMonsterData"), Is.Not.Null);
        Assert.That(typeof(AugmentData).GetField("monsterSpawnEntries"), Is.Not.Null);
    }

    [Test]
    public void PlayerManagerDurableSnapshotsUseCompactStableIds()
    {
        const BindingFlags members = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        System.Type shopSlot = typeof(PlayerManager).GetNestedType("ShopSnapshotSlot", BindingFlags.NonPublic);
        System.Type monsterSlot = typeof(PlayerManager).GetNestedType("AttackMonsterPoolSnapshotSlot", BindingFlags.NonPublic);
        Assert.That(shopSlot, Is.Not.Null);
        Assert.That(monsterSlot, Is.Not.Null);
        Assert.That(typeof(Fusion.INetworkStruct).IsAssignableFrom(shopSlot), Is.True);
        Assert.That(typeof(Fusion.INetworkStruct).IsAssignableFrom(monsterSlot), Is.True);

        AssertNetworkArrayElementType(typeof(PlayerManager), "ShopSnapshotSlots", shopSlot);
        AssertNetworkArrayElementType(typeof(PlayerManager), "PresentedAugmentSnapshotIds", typeof(int));
        AssertNetworkArrayElementType(typeof(PlayerManager), "SelectedAugmentSnapshotIds", typeof(int));
        AssertNetworkArrayElementType(typeof(PlayerManager), "SelectedAugmentSnapshotCounts", typeof(int));
        AssertNetworkArrayElementType(typeof(PlayerManager), "AttackMonsterPoolSnapshotSlots", monsterSlot);
        Assert.That(typeof(PlayerManager).GetField("SELECTED_AUGMENT_SNAPSHOT_CAPACITY", members)?.GetRawConstantValue(), Is.EqualTo(64));

        foreach (string methodName in new[]
                 {
                     "PackShopSnapshotMeta", "PackAttackMonsterCounts", "ResolveLoadedUnitDataKeyByStableHash",
                     "ResolveLoadedAugmentNameByStableId", "ResolveLoadedMonsterDataNameByStableHash"
                 })
        {
            Assert.That(typeof(PlayerManager).GetMethods(members).Any(method => method.Name == methodName), Is.True, methodName);
        }

        foreach (string obsoleteProperty in new[]
                 {
                     "ShopSnapshotUnitKeyHashes", "ShopSnapshotStarLevels", "ShopSnapshotSoldFlags",
                     "AttackMonsterPoolSnapshotDataIds", "AttackMonsterPoolSnapshotRemainingCounts",
                     "AttackMonsterPoolSnapshotMaxCounts", "ShopSnapshotUnitKeys", "PresentedAugmentSnapshotNames",
                     "SelectedAugmentSnapshotNames", "AttackMonsterPoolSnapshotNames"
                 })
        {
            Assert.That(typeof(PlayerManager).GetProperty(obsoleteProperty, members), Is.Null, obsoleteProperty);
        }
    }

    [Test]
    public void RuntimeCombatObjectsUseCompactStableIdentityKeys()
    {
        const BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        PropertyInfo unitKey = typeof(Unit).GetProperty("NetworkedUnitDataKeyHash", InstanceMembers);
        PropertyInfo monsterKey = typeof(Monster).GetProperty("NetworkedMonsterDataKeyHash", InstanceMembers);
        Assert.That(unitKey, Is.Not.Null);
        Assert.That(unitKey.PropertyType, Is.EqualTo(typeof(int)));
        Assert.That(System.Array.Exists(
            unitKey.GetCustomAttributes(false),
            attribute => attribute.GetType().Name == "NetworkedAttribute" || attribute.GetType().Name == "NetworkedWeavedAttribute"), Is.True);
        Assert.That(monsterKey, Is.Not.Null);
        Assert.That(monsterKey.PropertyType, Is.EqualTo(typeof(int)));
        Assert.That(System.Array.Exists(
            monsterKey.GetCustomAttributes(false),
            attribute => attribute.GetType().Name == "NetworkedAttribute" || attribute.GetType().Name == "NetworkedWeavedAttribute"), Is.True);

        Assert.That(System.Array.Exists(
            typeof(Unit).GetMethods(InstanceMembers),
            method => method.Name == "TryResolveUnitDataKeyByStableHash"), Is.True);
        Assert.That(System.Array.Exists(
            typeof(Monster).GetMethods(InstanceMembers),
            method => method.Name == "TryResolveMonsterDataKeyByStableHash"), Is.True);
        Assert.That(StableDataKeyUtility.StableKeyHash(" Fighter(Clone) "),
            Is.EqualTo(StableDataKeyUtility.StableKeyHash("Fighter")));
    }

    private static void WithOpenScene(string assetPath, System.Action<Scene> assertion)
    {
        Scene scene = SceneManager.GetSceneByPath(assetPath);
        bool openedForTest = !scene.IsValid() || !scene.isLoaded;
        if (openedForTest)
        {
            scene = EditorSceneManager.OpenScene(assetPath, OpenSceneMode.Additive);
        }

        try
        {
            Assert.That(scene.IsValid() && scene.isLoaded, Is.True, assetPath);
            assertion(scene);
        }
        finally
        {
            if (openedForTest && scene.IsValid() && scene.isLoaded)
            {
                EditorSceneManager.CloseScene(scene, removeScene: true);
            }
        }
    }

    private static GameObject FindSceneObject(Scene scene, string objectName)
    {
        return scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
            .FirstOrDefault(transform => transform.name == objectName)
            ?.gameObject;
    }

    private static GameObject FindChildByName(GameObject root, string objectName)
    {
        return root.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(transform => transform.name == objectName)
            ?.gameObject;
    }

    private static void AssertNamedElements(VisualElement root, string namePrefix, int count)
    {
        Assert.That(root, Is.Not.Null);
        for (int i = 0; i < count; i++)
        {
            Assert.That(root.Q<VisualElement>($"{namePrefix}{i}"), Is.Not.Null, $"{namePrefix}{i}");
        }
    }

    private static int CountElementsWithClass(VisualElement root, string className)
    {
        int count = 0;
        root.Query<VisualElement>(className: className).ForEach(_ => count++);
        return count;
    }

    private static void AssertSpriteUsesFullRectMesh(string assetPath)
    {
        var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        Assert.That(importer, Is.Not.Null, assetPath);
        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        Assert.That(settings.spriteMeshType, Is.EqualTo(SpriteMeshType.FullRect), assetPath);
    }

    private static void AssertDependenciesContainFileNames(string assetPath, params string[] expectedFileNames)
    {
        string[] dependencyNames = AssetDatabase.GetDependencies(assetPath, recursive: true)
            .Select(System.IO.Path.GetFileName)
            .ToArray();
        foreach (string expected in expectedFileNames)
        {
            Assert.That(dependencyNames, Does.Contain(expected), $"{assetPath} dependency {expected}");
        }
    }

    private static void AssertNetworkArrayElementType(System.Type ownerType, string propertyName, System.Type expectedElementType)
    {
        const BindingFlags members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        PropertyInfo property = ownerType.GetProperty(propertyName, members);
        Assert.That(property, Is.Not.Null, propertyName);
        Assert.That(property.PropertyType.IsGenericType, Is.True, propertyName);
        Assert.That(property.PropertyType.GetGenericArguments()[0], Is.EqualTo(expectedElementType), propertyName);
    }

    private static void AssertComparisonFails(System.Action<MPTestStateSnapshot.Snapshot, MPTestStateSnapshot.Snapshot> mutate, string expectedErrorField)
    {
        var host = BuildSnapshot("host");
        var client = BuildSnapshot("client");
        mutate(host, client);

        var result = MPTestAssertions.CompareDurable(host, client);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Errors, Has.Some.Contains(expectedErrorField), string.Join("\n", result.Errors));
    }

    [Test]
    public void BattleCommandTelemetryTracksActivateSkillSequence()
    {
        BattleCommandTelemetry.ApplySnapshot(0, 0, 0, 0, 0, "unknown");

        int sequence = BattleCommandTelemetry.RecordActivateSkillExecuted();

        Assert.That(sequence, Is.EqualTo(1));
        Assert.That(BattleCommandTelemetry.ActivateSkillSeq, Is.EqualTo(1));
        Assert.That(BattleCommandTelemetry.LastCommand, Is.EqualTo(CommandType.ActivateSkill.ToString()));

        BattleCommandTelemetry.ApplySnapshot(0, 0, 0, 0, 0, "unknown");
    }

    private static MPTestStateSnapshot.Snapshot BuildSnapshot(string role)
    {
        string hash = MPTestStateSnapshot.HashStableString("stable");
        return new MPTestStateSnapshot.Snapshot
        {
            Version = 1,
            Role = role,
            CaseName = "phase8",
            Session = "phase8-session",
            Scene = "Game",
            TimestampUtc = "2026-05-05T00:00:00.0000000Z",
            Runner = new MPTestStateSnapshot.RunnerSnapshot
            {
                IsRunning = true,
                GameMode = "Host",
                IsServer = true,
                IsClient = false,
                Tick = 100,
                ActivePlayerCount = 1,
                MaxPlayers = 2,
                LocalPlayerRef = "PlayerRef:1"
            },
            Game = new MPTestStateSnapshot.GameSnapshot
            {
                HasGameManagers = true,
                CurrentState = "Prepare",
                BattlePhase = "None",
                CurrentRound = 1,
                PhaseTimerRemaining = 12.5f,
                FirstAttackerPlayerId = 0,
                BattleOpponentsHash = hash,
                MatchFirstAttackerHash = hash
            },
            Players = new[]
            {
                new MPTestStateSnapshot.PlayerSnapshot
                {
                    PlayerId = 0,
                    NetworkId = "net-0",
                    PlayerRef = "PlayerRef:1",
                    ConnectionTokenHash = "unknown",
                    HasInputAuthority = true,
                    HasStateAuthority = true,
                    IsLocal = true,
                    IsAI = false,
                    IsConnected = true,
                    Health = 100,
                    Gold = 10,
                    WallCount = 5,
                    IsActivelyFighting = false,
                    IsAttackerInCurrentBattle = false,
                    AttackMonsterPoolHash = hash,
                    OwnedScrollsHash = hash,
                    OwnedScrollRevision = 0,
                    ManualSkillReadyHash = hash,
                    Shop = new MPTestStateSnapshot.ShopSnapshot
                    {
                        Available = true,
                        Revision = 1,
                        Round = 1,
                        Count = 5,
                        ItemsHash = hash
                    },
                    Augment = new MPTestStateSnapshot.AugmentSnapshot
                    {
                        Available = true,
                        SelectedCount = 0,
                        PresentedCount = 3,
                        PresentedHash = hash,
                        SelectedHash = "unknown"
                    },
                    Field = new MPTestStateSnapshot.FieldSnapshot
                    {
                        Ready = true,
                        GridHash = hash,
                        PlacedUnitCount = 0,
                        PlacedUnitsHash = hash,
                        DestructibleWallCount = 0,
                        PermanentWallCount = 0,
                        WallHash = hash,
                        PathReady = true,
                        GoalReady = true
                    },
                    Monsters = new MPTestStateSnapshot.MonsterSnapshot
                    {
                        Ready = true,
                        AliveCount = 0,
                        LivingHash = hash,
                        TypeHash = hash,
                        TypeCountHpHash = hash,
                        OwnerOriginHash = hash,
                        TargetPlayerHash = hash,
                        HpBucketHash = hash,
                        BossPoolIdentityHash = hash,
                        AutoSpawnRunning = null
                    },
                    Ai = new MPTestStateSnapshot.AiSnapshot
                    {
                        ControllerRegistered = false,
                        PrepareReady = null,
                        CombatReady = null
                    }
                }
            },
            Objects = new MPTestStateSnapshot.ObjectsSnapshot
            {
                NetworkObjectCount = 1,
                PlayerManagerCount = 1,
                UnitCount = 0,
                MonsterCount = 0,
                WallCount = 0
            },
            Effects = new MPTestStateSnapshot.EffectsSnapshot
            {
                ActiveBuffCount = 0,
                ActiveStatusCount = 0,
                ZoneCount = 0,
                ActiveBuffHash = hash,
                ActiveStatusHash = hash,
                ZoneHash = hash
            },
            Commands = new MPTestStateSnapshot.CommandsSnapshot
            {
                LastSequence = null,
                QueueDepth = 0,
                LastCommand = "unknown",
                AcceptedBattleCommandSeq = 0,
                SpawnMonsterSeq = 0,
                UseMagicScrollSeq = 0,
                ActivateSkillSeq = 0,
                RejectedBattleCommandCount = 0
            },
            HostMigration = new MPTestStateSnapshot.HostMigrationSnapshot
            {
                HandlerExists = true,
                IsMigrating = false,
                RecoverySucceeded = null,
                AiTakeoverReady = null,
                LastEvent = "unknown",
                EventCount = 0,
                OnHostMigrationCount = 0,
                NonNullTokenCount = 0,
                ResumeCount = 0,
                StartGameSuccessCount = 0,
                CompleteCount = 0,
                FailureCount = 0
            },
            Test = new MPTestStateSnapshot.TestSnapshot
            {
                Bot = new MPTestStateSnapshot.BotSnapshot
                {
                    Enabled = false,
                    Running = false,
                    Persona = "none",
                    CommandsIssued = 0,
                    LastDecision = null,
                    LastCommandType = null,
                    LastError = null,
                    JournalPath = null,
                    PlayerId = -1,
                    HasLocalInputAuthority = false,
                    StopReason = null
                },
                RandomOutcomes = new MPTestStateSnapshot.RandomOutcomesSnapshot()
            },
            Errors = new System.Collections.Generic.List<string>()
        };
    }

    private static MagicScrollData CreateScrollForTest(
        MagicScrollTacticalRole role,
        MagicScrollTargetDomain targetDomain,
        float range,
        float aiMinValue)
    {
        var skill = ScriptableObject.CreateInstance<SkillData>();
        skill.skillName = "test_scroll_skill";
        skill.range = range;
        skill.effects = new List<SkillEffect>();

        var scroll = ScriptableObject.CreateInstance<MagicScrollData>();
        scroll.scrollName = "test_scroll";
        scroll.canAiUse = true;
        scroll.tacticalRole = role;
        scroll.targetDomain = targetDomain;
        scroll.aiMinValue = aiMinValue;
        scroll.skillData = skill;
        return scroll;
    }

    private static UnitData CreateUnitForPrepareTest(
        string assetName,
        string unitName,
        UnitType unitType,
        string[] skills = null)
    {
        var unit = ScriptableObject.CreateInstance<UnitData>();
        unit.name = assetName;
        unit.unitName = unitName;
        unit.unitType = unitType;
        unit.cost = 3;
        unit.baseHealth = unitType == UnitType.Melee ? 300f : 180f;
        unit.baseAttackDamage = unitType == UnitType.Melee ? 16f : 24f;
        unit.attackRange = unitType == UnitType.Ranged ? 5f : 1f;
        unit.attackSpeed = 1f;
        unit.skillsByStarLevel = skills ?? new string[3];
        return unit;
    }
}
#endif
