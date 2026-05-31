#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Newtonsoft.Json;
using UnityEngine;
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
        string source = File.ReadAllText("Assets/Scripts/UI/RankingUIController.cs");
        string registrySource = File.ReadAllText("Assets/Scripts/Managers/GameManagers.PlayerRegistry.cs");
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
        Assert.That(source, Does.Contain("UIDocument"));
        Assert.That(source, Does.Contain("ResolveOpponent"));
        Assert.That(source, Does.Contain("GetBattleOpponent"));
        Assert.That(source, Does.Contain("GetHealthFillPercentForDisplay"));
        Assert.That(source, Does.Contain("ShouldUseAttackBattleRoleIconForDisplay"));
        Assert.That(source, Does.Contain("TryGetBattleRoleSnapshot"));
        Assert.That(source, Does.Contain("IsAttackerInCurrentBattle"));
        Assert.That(source, Does.Contain("PanelSortingOrder = 260"));
        Assert.That(source, Does.Contain("SetPickingModeRecursive(toolkitRoot, PickingMode.Ignore)"));
        Assert.That(source, Does.Contain("Root.pickingMode = PickingMode.Position"));
        Assert.That(source, Does.Contain("RegisterCallback<PointerUpEvent>"));
        Assert.That(source, Does.Contain("MoveToPlayerField"));
        Assert.That(source, Does.Contain("ReturnToOwnField"));
        Assert.That(source, Does.Contain("ShouldUseAttackModeCamera"));
        Assert.That(source, Does.Contain("HandleToolkitPointerInput"));
        Assert.That(source, Does.Contain("FindToolkitCardAtScreenPosition"));
        Assert.That(source, Does.Contain("RuntimePanelUtils.ScreenToPanel"));
        Assert.That(source, Does.Contain("ContainsPanelPoint"));
        Assert.That(source, Does.Contain("lastToolkitCardClickFrame"));
        Assert.That(source, Does.Contain("Display-only overlay"));
        string styleSource = File.ReadAllText("Assets/Resources/UI/PlayerRanking/PlayerRankingPanelStyles.uss");
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
        string sceneSource = File.ReadAllText("Assets/Scenes/01_MatchingLobby.unity");

        Assert.That(sceneSource, Does.Contain("m_Name: EventSystem"));
        Assert.That(sceneSource, Does.Contain("guid: 01614664b831546d2ae94a42149d80ac"));
        Assert.That(sceneSource, Does.Contain("m_Name: TestMatching UI Toolkit"));
        Assert.That(sceneSource, Does.Contain("m_PanelSettings: {fileID: 11400000, guid: 8273b236cf53499fbd9d38004ec35985"));
    }

    [Test]
    public void LobbyToolkitKeepsBackgroundImagesButRemovesLeftMenus()
    {
        string matchingUxml = File.ReadAllText("Assets/UI/TestMatching/TestMatching.uxml");
        string matchingStyle = File.ReadAllText("Assets/UI/TestMatching/TestMatching.uss");
        string joinUxml = File.ReadAllText("Assets/UI/JoinLobby/JoinLobby.uxml");
        string joinStyle = File.ReadAllText("Assets/UI/JoinLobby/JoinLobby.uss");

        Assert.That(matchingUxml, Does.Not.Contain("name=\"leftMenu\""));
        Assert.That(joinUxml, Does.Not.Contain("name=\"leftMenu\""));
        Assert.That(matchingUxml, Does.Contain("name=\"leftMenuImageCover\""));
        Assert.That(joinUxml, Does.Contain("name=\"leftMenuImageCover\""));
        Assert.That(matchingStyle, Does.Contain("bg_test_matching_full.png"));
        Assert.That(joinStyle, Does.Contain("bg_join_lobby_full.png"));
        Assert.That(matchingStyle, Does.Contain(".tm-left-menu-image-cover"));
        Assert.That(joinStyle, Does.Contain(".jl-left-menu-image-cover"));
        Assert.That(joinStyle, Does.Contain("player_portrait_0.png"));
        Assert.That(joinStyle, Does.Contain("player_portrait_1.png"));
        Assert.That(joinStyle, Does.Contain("player_portrait_2.png"));
    }

    [Test]
    public void GameSceneKeepsRuntimeRoots()
    {
        string sceneSource = File.ReadAllText("Assets/Scenes/03_Game.unity");

        Assert.That(sceneSource, Does.Contain("m_Name: Main Camera"));
        Assert.That(sceneSource, Does.Contain("m_Name: EventSystem"));
        Assert.That(sceneSource, Does.Contain("guid: 01614664b831546d2ae94a42149d80ac"));
        Assert.That(sceneSource, Does.Contain("m_Name: GameInitialrizer"));
        Assert.That(sceneSource, Does.Contain("m_Name: Addressable Manager"));
        Assert.That(sceneSource, Does.Contain("m_Name: VfxManager"));
    }

    [Test]
    public void TitleNetworkManagerKeepsPlayerPrefabReference()
    {
        string titleSceneSource = File.ReadAllText("Assets/Scenes/00_Title.unity");
        string playerPrefabSource = File.ReadAllText("Assets/Prefabs/Player_Root.prefab");

        Assert.That(titleSceneSource, Does.Contain("m_Name: NetworkManager"));
        Assert.That(titleSceneSource, Does.Contain("_playerPrefab: {fileID: -6962349454403488643, guid: 1831533972272eb408da1971d3a1e504"));

        Assert.That(playerPrefabSource, Does.StartWith("%YAML"));
        Assert.That(playerPrefabSource, Does.Contain("m_Name: Player_Root"));
        Assert.That(playerPrefabSource, Does.Contain("m_Name: MonsterSpawner"));
        Assert.That(playerPrefabSource, Does.Contain("m_Name: FieldManager"));
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
        string source = File.ReadAllText("Assets/Scripts/Game/Skills/ZoneController.cs");

        Assert.That(source, Does.Contain("GameEvents.OnGameStateChanged += HandleGameStateChanged"));
        Assert.That(source, Does.Contain("GameEvents.OnGameStateChanged -= HandleGameStateChanged"));
        Assert.That(source, Does.Contain("newState != GameManagers.GameState.Battle1"));
        Assert.That(source, Does.Contain("newState != GameManagers.GameState.Battle2"));
        Assert.That(source, Does.Contain("Destroy(gameObject);"));
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
        string source = File.ReadAllText("Assets/Scripts/Testing/MP/MPTestAutomationServer.cs");

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
    public void BattleSpawnClickZoneAcceptsWholeGroundMap()
    {
        var go = new GameObject("battle-spawn-click-zone-test");
        try
        {
            var field = go.AddComponent<FieldManager>();
            field.gridOrigin = Vector3.zero;
            field.cellSize = 1f;
            field.gridSize = new Vector2Int(10, 9);

            Assert.That(BattleCommandValidator.IsInsideBattleSpawnZone(field, new Vector3(0.5f, 0f, 0.5f)), Is.True);
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
        string source = File.ReadAllText("Assets/Scripts/Commands/Battle/ServerBattleCommandExecutor.cs");

        Assert.That(source, Does.Contain("validation_delegate_required"));
        Assert.That(source, Does.Contain("execution_delegate_required"));
        Assert.That(source, Does.Not.Contain("no_validation_delegate"));
        Assert.That(source, Does.Not.Contain("no_execution_delegate"));
    }

    [Test]
    public void BattleCommandOpponentResolutionKeepsClientPathsReadOnly()
    {
        string source = File.ReadAllText("Assets/Scripts/Commands/Battle/BattleCommandValidator.cs");

        Assert.That(source, Does.Contain("battle_opponent_snapshot_missing"));
        Assert.That(source, Does.Contain("CanUseAuthorityOpponentFallback"));
        Assert.That(source, Does.Contain("scope == CommandExecutionScope.ServerAuthorityOnly"));
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
        string commandSource = File.ReadAllText("Assets/Scripts/Commands/PlayerActions/ActivateSkillCommand.cs");
        string unitSource = File.ReadAllText("Assets/Scripts/Game/Units/Unit.cs");
        string loggerSource = File.ReadAllText("Assets/Scripts/Commands/PlayerActions/SkillCommandMpTestLogger.cs");

        Assert.That(commandSource, Does.Contain("skill_not_manual_or_ai_strategic"));
        Assert.That(commandSource, Does.Contain("skill_mana_not_ready"));
        Assert.That(commandSource, Does.Contain("skill_unit_disabled_or_silenced"));
        Assert.That(commandSource, Does.Contain("skill_target_unavailable"));
        Assert.That(commandSource, Does.Contain("skill_unit_dead"));
        Assert.That(unitSource, Does.Contain("HasSkillTargetsAvailable"));
        Assert.That(loggerSource, Does.Contain("skill_command_request"));
        Assert.That(loggerSource, Does.Contain("skill_command_accepted"));
        Assert.That(loggerSource, Does.Contain("skill_command_rejected"));
        Assert.That(loggerSource, Does.Contain("skill_command_skipped"));
        Assert.That(loggerSource, Does.Contain("skill_command_executed"));

        string playerManagerSource = File.ReadAllText("Assets/Scripts/Managers/PlayerManager.cs");
        string commandProcessorSource = File.ReadAllText("Assets/Scripts/Commands/Core/CommandProcessor.cs");
        Assert.That(commandSource, Does.Contain("IsVolatileNoOpReason"));
        Assert.That(playerManagerSource, Does.Contain("ActivateSkillCommand.IsVolatileNoOp"));
        Assert.That(playerManagerSource, Does.Contain("type == CommandType.ActivateSkill"));
        Assert.That(playerManagerSource, Does.Contain("new ActivateSkillCommand(playerId"));
        Assert.That(commandProcessorSource, Does.Contain("command is ActivateSkillCommand"));
        Assert.That(commandProcessorSource, Does.Contain("command.Execute()"));
    }

    [Test]
    public void DefenderSkillPolicyEmitsOnlyActivateSkillCommandDecision()
    {
        string policySource = File.ReadAllText("Assets/Scripts/AI/Planning/DefenderSkillPolicy.cs");

        Assert.That(policySource, Does.Contain("new ActivateSkillCommand"));
        Assert.That(policySource, Does.Contain("defender_skill_evaluated"));
        Assert.That(policySource, Does.Contain("defender_skill_selected"));
        Assert.That(policySource, Does.Not.Contain(".ActivateSkill("));
        Assert.That(policySource, Does.Not.Contain("ApplyEffect("));
    }

    [Test]
    public void BehaviorTreeV2WiresAiAndHumanBotThroughSharedPolicies()
    {
        string aiSource = File.ReadAllText("Assets/Scripts/Commands/AI/AIPlayerController.cs");
        string humanBotSource = File.ReadAllText("Assets/Scripts/Testing/MP/MPTestHumanBotDriver.cs");
        string journalSource = File.ReadAllText("Assets/Scripts/Testing/MP/MPTestBotJournal.cs");

        Assert.That(aiSource, Does.Contain("MdfBotProfile"));
        Assert.That(aiSource, Does.Contain("MdfBotProfile.ServerAiDefault"));
        Assert.That(aiSource, Does.Contain("new PrepareDecisionPolicy(_profile)"));
        Assert.That(aiSource, Does.Contain("BattleDecisionPolicy"));
        Assert.That(aiSource, Does.Contain("ServerAiCommandEmitter"));
        Assert.That(aiSource, Does.Contain("MdfDecisionContext.Create"));
        Assert.That(aiSource, Does.Contain("isServerAi: true"));
        Assert.That(aiSource, Does.Not.Contain("BuildBehaviorTrees"));
        Assert.That(aiSource, Does.Not.Contain("GetActiveTree"));
        Assert.That(humanBotSource, Does.Contain("MdfBotProfile"));
        Assert.That(humanBotSource, Does.Contain("new PrepareDecisionPolicy(_profile)"));
        Assert.That(humanBotSource, Does.Contain("BattleDecisionPolicy"));
        Assert.That(humanBotSource, Does.Contain("HumanClientCommandEmitter"));
        Assert.That(humanBotSource, Does.Contain("isHumanBot: true"));
        Assert.That(humanBotSource, Does.Not.Contain("ComponentRegistry.Register<AIPlayerController>"));
        Assert.That(journalSource, Does.Contain("BuildDecisionEntry(MPTestHumanBotDriver.BotStatus status, MdfDecision decision)"));
    }

    [Test]
    public void PreparePolicyDoesNotPaceServerAiInsidePolicy()
    {
        string source = File.ReadAllText("Assets/Scripts/AI/Planning/PrepareDecisionPolicy.cs");

        Assert.That(source, Does.Not.Contain("AIPacer.Ready"));
        Assert.That(source, Does.Not.Contain("AIPacer.Arm"));
        Assert.That(source, Does.Not.Contain("context.IsServerAi &&"));
    }

    [Test]
    public void BattleStartDoesNotAutoSpawnForAiAttackers()
    {
        string gameManagersSource = File.ReadAllText("Assets/Scripts/Managers/GameManagers.cs");
        string migrationRecoverySource = File.ReadAllText("Assets/Scripts/Managers/GameManagers.MigrationRecovery.cs");
        string monsterSpawnerSource = File.ReadAllText("Assets/Scripts/Game/Monsters/MonsterSpawner.cs");
        string aiSource = File.ReadAllText("Assets/Scripts/Commands/AI/AIPlayerController.cs");

        Assert.That(gameManagersSource, Does.Not.Contain("StartBattleForPlayers/SpawnAllMonstersToTargetField"));
        Assert.That(migrationRecoverySource, Does.Not.Contain("BattleRebootstrap/SpawnAllMonstersToTargetField"));
        Assert.That(monsterSpawnerSource, Does.Contain("AI bootstrap is disabled"));
        Assert.That(monsterSpawnerSource, Does.Not.Contain("await StartAutoSpawnFromPool"));
        Assert.That(aiSource, Does.Contain("BattleDecisionPolicy"));
        Assert.That(aiSource, Does.Contain("ServerAiCommandEmitter"));
    }

    [Test]
    public void FirstPrepareStartsAfterPlayersAreReadable()
    {
        string gameManagersSource = File.ReadAllText("Assets/Scripts/Managers/GameManagers.cs");

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
        string source = File.ReadAllText("Assets/Scripts/Testing/MP/MPTestHumanBotDriver.cs");

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
        string shopSource = File.ReadAllText("Assets/Scripts/UI/ShopUIController.cs");
        string gameEventsSource = File.ReadAllText("Assets/Scripts/Managers/GameEvents.cs");

        Assert.That(shopSource, Does.Contain("TryGetLocalShopPlayerId(out var localPlayerId)"));
        Assert.That(shopSource, Does.Contain("shopSlots == null || slotIndex < 0 || slotIndex >= shopSlots.Length"));
        Assert.That(shopSource, Does.Contain("var slot = shopSlots[slotIndex];"));
        Assert.That(shopSource, Does.Contain("if (slot == null)"));

        Assert.That(gameEventsSource, Does.Contain("foreach (Action<int, ShopItem, int> handler in handlers.GetInvocationList())"));
        Assert.That(gameEventsSource, Does.Contain("OnUnitPurchaseSucceeded handler exception"));
        Assert.That(gameEventsSource, Does.Contain("Debug.LogException(ex);"));
    }

    [Test]
    public void GamePrepareToolkitKeepsChoiceCountsAndCommandRoutes()
    {
        string controllerSource = File.ReadAllText("Assets/Scripts/UI/Game/GamePrepareUIToolkitController.cs");
        string shopSource = File.ReadAllText("Assets/Scripts/UI/ShopUIController.cs");
        string augmentSource = File.ReadAllText("Assets/Scripts/UI/AugmentUIController.cs");
        string playerHudSource = File.ReadAllText("Assets/Scripts/UI/PlayerHUDController.cs");
        string attackUiSource = File.ReadAllText("Assets/Scripts/UI/AttackSequence/AttackSequenceUIController.cs");
        string attackManagerSource = File.ReadAllText("Assets/Scripts/Game/Battle/AttackSequenceManager.cs");
        string inputSource = File.ReadAllText("Assets/Scripts/Managers/MdfInput.cs");
        string fieldSource = File.ReadAllText("Assets/Scripts/Managers/FieldManager.cs");
        string placementSource = File.ReadAllText("Assets/Scripts/Managers/PlacementManager.cs");
        string playerManagerSource = File.ReadAllText("Assets/Scripts/Managers/PlayerManager.cs");
        string removeWallCommandSource = File.ReadAllText("Assets/Scripts/Commands/PlayerActions/RemoveWallCommand.cs");
        string automationSource = File.ReadAllText("Assets/Scripts/Testing/MP/MPTestAutomationServer.cs");
        string uxml = File.ReadAllText("Assets/Resources/UI/GamePrepare/GamePreparePanels.uxml");
        string styleSource = File.ReadAllText("Assets/Resources/UI/GamePrepare/GamePreparePanelsStyles.uss");
        var layout = Resources.Load<VisualTreeAsset>("UI/GamePrepare/GamePreparePanels");
        var style = Resources.Load<StyleSheet>("UI/GamePrepare/GamePreparePanelsStyles");
        var theme = Resources.Load<ThemeStyleSheet>("UI/GamePrepare/GamePrepareRuntimeTheme");
        var tree = layout != null ? layout.CloneTree() : null;

        Assert.That(GamePrepareUIToolkitController.ShopCardCount, Is.EqualTo(5));
        Assert.That(GamePrepareUIToolkitController.AugmentCardCount, Is.EqualTo(3));
        Assert.That(GamePrepareUIToolkitController.MonsterCardCount, Is.EqualTo(9));
        Assert.That(GamePrepareUIToolkitController.ScrollCardCount, Is.EqualTo(5));
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
        Assert.That(tree?.Q<VisualElement>("game-wall-button"), Is.Not.Null);
        Assert.That(tree?.Q<VisualElement>("game-option-button"), Is.Not.Null);
        Assert.That(tree?.Q<VisualElement>("game-gold-value"), Is.Not.Null);
        Assert.That(tree?.Q<Label>("game-gold-count-value"), Is.Not.Null);
        Assert.That(tree?.Q<VisualElement>("game-wall-icon"), Is.Not.Null);
        Assert.That(tree?.Q<Label>("game-wall-count-label"), Is.Not.Null);
        Assert.That(Regex.Matches(uxml, "name=\"shop-card-\\d\"").Count, Is.EqualTo(5));
        Assert.That(Regex.Matches(uxml, "class=\"shop-art-frame\"").Count, Is.EqualTo(5));
        Assert.That(Regex.Matches(uxml, "class=\"shop-text-overlay\"").Count, Is.EqualTo(5));
        Assert.That(Regex.Matches(uxml, "name=\"augment-card-\\d\"").Count, Is.EqualTo(3));
        Assert.That(Regex.Matches(uxml, "name=\"attack-monster-card-\\d\"").Count, Is.EqualTo(9));
        Assert.That(Regex.Matches(uxml, "name=\"attack-scroll-card-\\d\"").Count, Is.EqualTo(5));
        Assert.That(uxml, Does.Contain("project://database/Assets/Resources/UI/GamePrepare/GamePreparePanelsStyles.uss"));
        Assert.That(uxml, Does.Contain("name=\"shop-panel\" class=\"prepare-panel shop-panel\""));
        Assert.That(tree?.Q<VisualElement>("shop-card-0")?.ClassListContains("shop-card-star-1"), Is.True);
        Assert.That(tree?.Q<VisualElement>("shop-card-4")?.ClassListContains("shop-card-star-5"), Is.True);
        Assert.That(controllerSource, Does.Contain("new BuyUnitCommand(playerId, slotIndex)"));
        Assert.That(controllerSource, Does.Contain("new RerollShopCommand(playerId)"));
        Assert.That(controllerSource, Does.Contain("new SelectAugmentCommand(playerId, index)"));
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
        Assert.That(controllerSource, Does.Contain("GetShopCardStarClass"));
        Assert.That(controllerSource, Does.Contain("topGem.style.display = DisplayStyle.None"));
        Assert.That(controllerSource, Does.Contain("body.style.backgroundColor = Color.clear"));
        Assert.That(controllerSource, Does.Contain("footer.style.backgroundColor = new Color"));
        Assert.That(controllerSource, Does.Contain("attackSequenceManager?.SelectMonsterSlot(slotIndex)"));
        Assert.That(controllerSource, Does.Contain("attackSequenceManager?.SelectMagicScroll(scrolls[slotIndex])"));
        Assert.That(controllerSource, Does.Contain("game-gold-count-value"));
        Assert.That(controllerSource, Does.Contain("game-wall-count-label"));
        Assert.That(controllerSource, Does.Contain("reroll-gold-mode"));
        Assert.That(controllerSource, Does.Contain("EnableInClassList(\"reroll-gold-mode\", !shopVisible)"));
        Assert.That(controllerSource, Does.Contain("hudShopLabel.text = $\"\\uC0C1\\uC810\\n{shopAction}\\n{goldCount}G\";"));
        Assert.That(controllerSource, Does.Contain("SetPickingMode(rerollButton, shopVisible ? PickingMode.Position : PickingMode.Ignore)"));
        Assert.That(styleSource, Does.Contain("Spr_UnitCost.png"));
        Assert.That(styleSource, Does.Contain("Bricks.png"));
        Assert.That(controllerSource, Does.Contain("TogglePlacementMode(PlacementMode.Wall)"));
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
        Assert.That(controllerSource, Does.Contain("RegisterCallback<PointerDownEvent>"));
        Assert.That(controllerSource, Does.Contain("SuppressBattleMapInputForCurrentPointer"));
        Assert.That(controllerSource, Does.Contain("IsBlockingElementOrDescendant"));
        Assert.That(attackUiSource, Does.Contain("TryShowAttackSequenceFromLegacy"));
        Assert.That(attackUiSource, Does.Contain("SetLegacyContentVisibilityOnly(false)"));
        Assert.That(attackManagerSource, Does.Contain("IsPointerOverBattleActionBlocker"));
        Assert.That(attackManagerSource, Does.Contain("TryGetSpawnPositionUnderPointer"));
        Assert.That(attackManagerSource, Does.Contain("ShouldSuppressBattleMapInput"));
        Assert.That(attackManagerSource, Does.Contain("BattleCommandValidator.IsInsideBattleSpawnZone"));
        Assert.That(inputSource, Does.Contain("HasNonGamePrepareToolkitUiHit"));
        Assert.That(inputSource, Does.Contain("eventSystem.RaycastAll"));
        Assert.That(inputSource, Does.Contain("IsPointerOverFieldBlockingUI"));
        Assert.That(inputSource, Does.Contain("IsFieldPassthroughUi"));
        Assert.That(inputSource, Does.Contain("GetComponentInParent<StatusBarUI>()"));
        Assert.That(fieldSource, Does.Contain("ShouldAllowUnitDragThroughPrepareToolkit"));
        Assert.That(fieldSource, Does.Contain("MdfInput.IsPointerOverFieldBlockingUI()"));
        Assert.That(fieldSource, Does.Contain("GamePrepareUIToolkitController.IsPointerOverBlockingElement(MdfInput.PointerPosition)"));
        Assert.That(fieldSource, Does.Contain("TryRequestRemoveWallAt"));
        Assert.That(fieldSource, Does.Contain("new RemoveWallCommand(playerManager.playerId, gridPosition)"));
        Assert.That(placementSource, Does.Contain("bool pointerOverUI = MdfInput.IsPointerOverFieldBlockingUI()"));
        Assert.That(placementSource, Does.Contain("!pointerOverUI"));
        Assert.That(placementSource, Does.Contain("SecondaryPointerWasPressedThisFrame"));
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
        Assert.That(shopSource, Does.Contain("TryToggleShopFromLegacy"));
        Assert.That(augmentSource, Does.Contain("TryShowAugmentsFromLegacy"));
        Assert.That(playerHudSource, Does.Contain("SetLegacyHudButtonsVisible"));
        Assert.That(playerHudSource, Does.Contain("SetLegacyResourceHudVisible"));
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
        Assert.That(File.ReadAllText("Assets/Resource/Image/UI/SlotUI/Spr_SlotGray.png.meta"), Does.Contain("spriteMeshType: 0"));
        Assert.That(File.ReadAllText("Assets/Resource/Image/UI/SlotUI/Spr_SlotGreen.png.meta"), Does.Contain("spriteMeshType: 0"));
        Assert.That(File.ReadAllText("Assets/Resource/Image/UI/SlotUI/Spr_SlotBlue.png.meta"), Does.Contain("spriteMeshType: 0"));
        Assert.That(File.ReadAllText("Assets/Resource/Image/UI/SlotUI/Spr_SlotPurple.png.meta"), Does.Contain("spriteMeshType: 0"));
        Assert.That(File.ReadAllText("Assets/Resource/Image/UI/SlotUI/Spr_SlotOrange.png.meta"), Does.Contain("spriteMeshType: 0"));
        Assert.That(uxml, Does.Contain("game-round-timer-label"));
        Assert.That(uxml, Does.Contain("game-prepare-design-space"));
        Assert.That(uxml, Does.Contain("wall-action-button"));
        Assert.That(GamePrepareUIToolkitController.GetShopCardStarClass(1), Is.EqualTo("shop-card-star-1"));
        Assert.That(GamePrepareUIToolkitController.GetShopCardStarClass(2), Is.EqualTo("shop-card-star-2"));
        Assert.That(GamePrepareUIToolkitController.GetShopCardStarClass(3), Is.EqualTo("shop-card-star-3"));
        Assert.That(GamePrepareUIToolkitController.GetShopCardStarClass(4), Is.EqualTo("shop-card-star-4"));
        Assert.That(GamePrepareUIToolkitController.GetShopCardStarClass(5), Is.EqualTo("shop-card-star-5"));
        Assert.That(GamePrepareUIToolkitController.GetShopCardStarClass(6), Is.EqualTo(string.Empty));
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
        string fieldSource = File.ReadAllText("Assets/Scripts/Managers/FieldManager.cs");
        string playerSource = File.ReadAllText("Assets/Scripts/Managers/PlayerManager.cs");
        string gameManagersSource = File.ReadAllText("Assets/Scripts/Managers/GameManagers.cs");

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
        Assert.That(playerSource, Does.Contain("move_source_not_owned_by_player"));
        Assert.That(playerSource, Does.Contain("OwnsUnitForCommand"));
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
        string fieldSource = File.ReadAllText("Assets/Scripts/Managers/FieldManager.cs");

        Assert.That(fieldSource, Does.Contain("previousCells.Count == 1 && previousCells[0] == gridPosition"));
        Assert.That(fieldSource, Does.Contain("(unit.transform.position - targetWorldPos).sqrMagnitude > 0.0001f"));
        Assert.That(fieldSource, Does.Contain("MoveUnitImmediate(unit, worldPos);"));
        Assert.That(fieldSource, Does.Contain("RemovePlacedUnitEntries(unit);"));
    }

    [Test]
    public void NewlyPurchasedUnitMovesAreQueuedUntilRegistrationCompletes()
    {
        string fieldSource = File.ReadAllText("Assets/Scripts/Managers/FieldManager.cs");
        string playerSource = File.ReadAllText("Assets/Scripts/Managers/PlayerManager.cs");

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

        Assert.That(playerSource, Does.Contain("fieldManager.HasPendingUnitAt(from)"));
        Assert.That(playerSource, Does.Contain("fieldManager.TryGetPendingUnitDataAt(from"));
        Assert.That(playerSource, Does.Contain("fieldManager.IsUnitAt(to)"));
        Assert.That(playerSource, Does.Contain("move_pending_unit_type_unknown_for_wall"));
        Assert.That(playerSource, Does.Contain("move_source_not_owned_by_player"));
    }

    [Test]
    public void MoveUnitCommandHasFinalAuthorityAndPlacementGuards()
    {
        string commandSource = File.ReadAllText("Assets/Scripts/Commands/PlayerActions/MoveUnitCommand.cs");
        string processorSource = File.ReadAllText("Assets/Scripts/Commands/Core/CommandProcessor.cs");
        string gameManagersSource = File.ReadAllText("Assets/Scripts/Managers/GameManagers.cs");
        string playerManagerSource = File.ReadAllText("Assets/Scripts/Managers/PlayerManager.cs");

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
        string unitSource = File.ReadAllText("Assets/Scripts/Game/Units/Unit.cs");

        Assert.That(unitSource, Does.Contain("private bool CanReadNetworkedIdentity()"));
        Assert.That(unitSource, Does.Contain("private bool CanWriteNetworkedIdentity()"));
        Assert.That(unitSource, Does.Contain("TryGetOwnerPlayerIdForRoster(out int ownerPlayerId)"));
        Assert.That(unitSource, Does.Contain("GetSnapshotUnitDataKey()"));
        Assert.That(unitSource, Does.Contain("UnitDataKeyForRoster"));
        Assert.That(unitSource, Does.Contain("StarLevelForRoster"));
        Assert.That(unitSource, Does.Contain("starLevel = TryGetNetworkedStarLevel(out int networkedStarLevel) ? networkedStarLevel : 1;"));
        Assert.That(unitSource, Does.Contain("UpdateLocalNetworkIdentityMirror();"));
        Assert.That(unitSource, Does.Contain("if (CanReadNetworkedIdentity() && NetworkedHasOwnerPlayerId)"));
    }

    [Test]
    public void HostMigrationDurableSnapshotRestoresFieldUnitRoster()
    {
        string handlerSource = File.ReadAllText("Assets/Scripts/Network/HostMigrationHandler.cs");
        string fieldSource = File.ReadAllText("Assets/Scripts/Managers/FieldManager.cs");
        string networkSource = File.ReadAllText("Assets/Scripts/Network/NetworkManager.cs");

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
        string serverSource = File.ReadAllText("Assets/Scripts/Testing/MP/MPTestAutomationServer.cs");
        string snapshotSource = File.ReadAllText("Assets/Scripts/Testing/MP/MPTestStateSnapshot.cs");

        Assert.That(serverSource, Does.Contain("move_unit"));
        Assert.That(serverSource, Does.Contain("ExecuteMoveUnitCommand"));
        Assert.That(serverSource, Does.Contain("TryFindMoveUnitPositions"));
        Assert.That(serverSource, Does.Contain("ValidateMoveUnitTarget"));
        Assert.That(serverSource, Does.Contain("new MoveUnitCommand(playerId, from, to)"));
        Assert.That(snapshotSource, Does.Contain("if (MPTestCommandLine.IsEnabled)"));
        Assert.That(snapshotSource, Does.Contain("return true;"));
    }

    [Test]
    public void BattleDeathDoesNotDropUnitsBeforePrepareRespawn()
    {
        string unitSource = File.ReadAllText("Assets/Scripts/Game/Units/Unit.cs");
        string manaSource = File.ReadAllText("Assets/Scripts/Game/Game Rules/ManaController.cs");
        string fieldSource = File.ReadAllText("Assets/Scripts/Managers/FieldManager.cs");
        string gameManagersSource = File.ReadAllText("Assets/Scripts/Managers/GameManagers.cs");
        string snapshotSource = File.ReadAllText("Assets/Scripts/Testing/MP/MPTestStateSnapshot.cs");

        Assert.That(unitSource, Does.Contain("else if (!NetworkedIsDead && IsDead)"));
        Assert.That(unitSource, Does.Contain("HandleNetworkedDeathStateChanged();"));
        Assert.That(unitSource, Does.Contain("if (!CanReadNetworkedState)"));
        Assert.That(unitSource, Does.Contain("Object.HasStateAuthority"));
        Assert.That(unitSource, Does.Contain("if (!IsDead && !inactive) return;"));
        Assert.That(unitSource, Does.Contain("SetDeathPresentationActive(false);"));
        Assert.That(unitSource, Does.Not.Contain("gameObject.SetActive(false);"));
        Assert.That(unitSource, Does.Contain("private bool CanWriteNetworkedStats()"));
        Assert.That(unitSource, Does.Contain("float berserkDamage = currentAttackDamage * 1.5f;"));
        Assert.That(unitSource, Does.Not.Contain("_networkedAttackDamage *= 1.5f;"));
        Assert.That(manaSource, Does.Contain("private bool CanWriteNetworkedMana()"));
        Assert.That(manaSource, Does.Contain("public float CurrentMana => CanReadNetworkedMana() ? _currentMana : _localCurrentMana;"));
        Assert.That(manaSource, Does.Not.Contain("public float CurrentMana => _currentMana;"));
        Assert.That(fieldSource, Does.Contain("bool wasAlreadyRegistered"));
        Assert.That(fieldSource, Does.Contain("inactiveOrDead && !wasAlreadyRegistered && !belongsToPlayer"));
        Assert.That(fieldSource, Does.Contain("unit.IsDead || !unit.gameObject.activeSelf || !unit.gameObject.activeInHierarchy"));
        Assert.That(fieldSource, Does.Contain("public void RespawnAllUnits()"));
        Assert.That(fieldSource, Does.Contain("bool networkRunning = runner != null && runner.IsRunning;"));
        Assert.That(fieldSource, Does.Contain("if (networkRunning && !unit.HasValidNetworkObject)"));
        Assert.That(gameManagersSource, Does.Contain("player?.fieldManager?.RespawnAllUnits();"));
        Assert.That(snapshotSource, Does.Contain("deadUnitCount"));
        Assert.That(snapshotSource, Does.Contain("DeadUnitsHash"));
        Assert.That(snapshotSource, Does.Contain("RefreshPlayerRuntimeForSnapshot"));
        Assert.That(snapshotSource, Does.Contain("MPTestStateSnapshot.CapturePlayer"));
    }

    [Test]
    public void AttackSequenceMonsterSelectionTracksAuthorityPoolSlot()
    {
        string managerSource = File.ReadAllText("Assets/Scripts/Game/Battle/AttackSequenceManager.cs");
        string uiSource = File.ReadAllText("Assets/Scripts/UI/AttackSequence/AttackSequenceUIController.cs");

        Assert.That(managerSource, Does.Contain("_selectedMonsterSlotIndex"));
        Assert.That(managerSource, Does.Contain("SelectMonsterSlot(int slotIndex)"));
        Assert.That(managerSource, Does.Contain("TryResolveSelectedMonster(out var selectedMonster, out int poolSlotIndex)"));
        Assert.That(managerSource, Does.Contain("HasPendingBattleSpawnForCurrentSnapshot(poolSlotIndex)"));
        Assert.That(managerSource, Does.Contain("MarkPendingBattleSpawn(poolSlotIndex)"));
        Assert.That(managerSource, Does.Contain("SuppressBattleMapInputForCurrentPointer"));
        Assert.That(managerSource, Does.Not.Contain("FirstOrDefault(entry => entry != null && !entry.IsEmpty)"));
        Assert.That(uiSource, Does.Contain("_attackSequenceManager?.SelectMonsterSlot(slotIndex)"));
        Assert.That(uiSource, Does.Contain("GamePrepareUIToolkitController.TrySyncMonsterSelectionFromLegacy(slotIndex)"));
        Assert.That(uiSource, Does.Not.Contain("SelectFirstAvailableMonsterSlot(pool);"));
    }

    [Test]
    public void AiFillIdentityReplicatesToClientSnapshots()
    {
        string playerSource = File.ReadAllText("Assets/Scripts/Managers/PlayerManager.cs");
        string gameManagersSource = File.ReadAllText("Assets/Scripts/Managers/GameManagers.cs");
        string snapshotSource = File.ReadAllText("Assets/Scripts/Testing/MP/MPTestStateSnapshot.cs");

        Assert.That(playerSource, Does.Contain("[Networked] public NetworkBool IsAiControlled"));
        Assert.That(playerSource, Does.Contain("SetAiControlled(bool isAi)"));
        Assert.That(gameManagersSource, Does.Contain("newPlayer.SetAiControlled(isAI);"));
        Assert.That(snapshotSource, Does.Contain("player.IsAiControlled"));
        Assert.That(snapshotSource, Does.Contain("|| ComponentRegistry.Has<AIPlayerController>"));
    }

    [Test]
    public void ManualSkillSnapshotAvoidsFrameLocalReadinessInputs()
    {
        string source = File.ReadAllText("Assets/Scripts/Testing/MP/MPTestStateSnapshot.cs");
        int start = source.IndexOf("private static string CaptureManualSkillReadyHash", System.StringComparison.Ordinal);
        int end = source.IndexOf("private static bool PlayerRefIsConnected", System.StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0));
        Assert.That(end, Is.GreaterThan(start));

        string method = source.Substring(start, end - start);
        Assert.That(method, Does.Contain("skill="));
        Assert.That(method, Does.Not.Contain("LoadedSkillData"));
        Assert.That(method, Does.Not.Contain("IsManualOrAiStrategicSkill"));
        Assert.That(method, Does.Not.Contain("currentSkillActivationType"));
        Assert.That(method, Does.Not.Contain("SkillCurrentMana"));
        Assert.That(method, Does.Not.Contain("SkillMaxMana"));
        Assert.That(method, Does.Not.Contain("CountSkillTargets"));
        Assert.That(method, Does.Not.Contain("HasSkillTargetsAvailable"));
        Assert.That(method, Does.Not.Contain("manaBucket="));
        Assert.That(method, Does.Not.Contain("ready="));
    }

    [Test]
    public void PrepareDecisionPolicyPreservesExpectedActionOrder()
    {
        string source = File.ReadAllText("Assets/Scripts/AI/Planning/PrepareDecisionPolicy.cs");

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
        string source = File.ReadAllText("Assets/Scripts/AI/Planning/PrepareDecisionPolicy.cs");

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
        string source = File.ReadAllText("Assets/Scripts/Managers/FieldManager.cs");

        Assert.That(source, Does.Contain("RestoreSelectedUnitNetworkTransform"));
        Assert.That(source, Does.Contain("originalUnitPosition = GetUnitPosition(selectedUnit) ?? WorldToGridInt(selectedUnit.transform.position);"));

        int stateChangeStart = source.IndexOf("private void HandleGameStateChange", System.StringComparison.Ordinal);
        int stateChangeEnd = source.IndexOf("#endregion", stateChangeStart, System.StringComparison.Ordinal);
        Assert.That(stateChangeStart, Is.GreaterThanOrEqualTo(0));
        Assert.That(stateChangeEnd, Is.GreaterThan(stateChangeStart));
        string stateChangeMethod = source.Substring(stateChangeStart, stateChangeEnd - stateChangeStart);
        Assert.That(stateChangeMethod, Does.Contain("RestoreSelectedUnitNetworkTransform();"));
        Assert.That(stateChangeMethod.IndexOf("RestoreSelectedUnitNetworkTransform();", System.StringComparison.Ordinal),
            Is.LessThan(stateChangeMethod.IndexOf("SnapbackSelectedUnit(originalWorldPos);", System.StringComparison.Ordinal)));

        int releaseStart = source.IndexOf("if (MdfInput.PrimaryPointerWasReleasedThisFrame() && selectedUnit != null)", System.StringComparison.Ordinal);
        int releaseEnd = source.IndexOf("private bool ShouldAllowUnitDragThroughPrepareToolkit", releaseStart, System.StringComparison.Ordinal);
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
        string source = File.ReadAllText("Assets/Scripts/AI/Planning/PrepareDecisionPolicy.cs");

        Assert.That(source, Does.Contain("BuildMonsterPathContext"));
        Assert.That(source, Does.Contain("FindBestSpotForAI(unit.Data, monsterPath"));
        Assert.That(source, Does.Contain("TryGetSingleOpenEntryNavigationCell"));
        Assert.That(source, Does.Contain("GetOpenBorderGaps"));
        Assert.That(source, Does.Contain("ConvertNavigationPathToInnerField"));
        Assert.That(source, Does.Contain("pathAwarePlacement"));
        Assert.That(source, Does.Contain("monsterPathCount"));

        string coverageSource = File.ReadAllText("Assets/Scripts/AI/UtilitySystem/Considerations/Placement/AttackRangeCoverageConsideration.cs");
        Assert.That(coverageSource, Does.Contain("BuildTargetTiles"));
        Assert.That(coverageSource, Does.Contain("context.MonsterPath"));

        string fieldSource = File.ReadAllText("Assets/Scripts/Managers/FieldManager.cs");
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
        Assert.That(fieldSource, Does.Contain("if (!HasWallAt(pos))"));
        Assert.That(fieldSource, Does.Contain("return new List<Vector3Int>();"));
        Assert.That(fieldSource, Does.Contain("return movingUnitOriginalPos;"));

        string mazeSource = File.ReadAllText("Assets/Scripts/AI/Planning/MazePlanner.cs");
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
        string driverSource = File.ReadAllText("Assets/Scripts/Testing/MP/MPTestHumanBotDriver.cs");
        Assert.That(driverSource, Does.Contain("new PrepareDecisionPolicy"));
        Assert.That(driverSource, Does.Not.Contain("new MPTestHumanBotPolicy"));
    }

    [Test]
    public void BattleDecisionLayerRoutesOnlyThroughCommandEmitters()
    {
        string policySource = File.ReadAllText("Assets/Scripts/AI/Planning/BattleDecisionPolicy.cs");
        string emitterSource = File.ReadAllText("Assets/Scripts/AI/Planning/MdfCommandEmitter.cs");

        Assert.That(policySource, Does.Contain("new BattleSpawnMonsterCommand"));
        Assert.That(policySource, Does.Contain("new UseMagicScrollCommand"));
        Assert.That(policySource, Does.Contain("DefenderSkillPolicy"));
        Assert.That(policySource, Does.Not.Contain("SpawnMonsterAtPositionAsync"));
        Assert.That(policySource, Does.Not.Contain("TryConsumeMonsterPoolSlot"));
        Assert.That(policySource, Does.Not.Contain("TryConsumeMagicScrollSlot"));
        Assert.That(policySource, Does.Not.Contain("CastGameplay"));
        Assert.That(policySource, Does.Not.Contain(".ActivateSkill("));
        Assert.That(emitterSource, Does.Contain("CommandProcessor.RequestCommandExecution"));
        Assert.That(emitterSource, Does.Contain("ExecuteBattleSpawnMonsterCommandAsync"));
        Assert.That(emitterSource, Does.Contain("RPC_RequestBattleSpawnMonster"));
        Assert.That(emitterSource, Does.Contain("ExecuteUseMagicScrollCommandAsync"));
        Assert.That(emitterSource, Does.Contain("RPC_RequestUseMagicScrollCommand"));
    }

    [Test]
    public void AttackStrategyEvaluatesFullPathAndKeepsTankSpawnOnGroundRoute()
    {
        string source = File.ReadAllText("Assets/Scripts/AI/BehaviorTree/Nodes/Actions/AIAttackStrategy.cs");

        Assert.That(source, Does.Contain("var candidates = kvp.Value;"));
        Assert.That(source, Does.Not.Contain("GetClosestCandidates(kvp.Value"));
        Assert.That(source, Does.Contain("phase0.Orders.Add(new AISpawnOrder(firstTank, groundSpawnPos"));
        Assert.That(source, Does.Contain("phase1.Orders.Add(new AISpawnOrder(entry, destroyerSpawnPos"));
    }

    [Test]
    public void BehaviorTreeV2DoesNotMutateDurableStateInPoliciesOrTestLogging()
    {
        string prepareSource = File.ReadAllText("Assets/Scripts/AI/Planning/PrepareDecisionPolicy.cs");
        string loggerSource = File.ReadAllText("Assets/Scripts/Testing/MP/MPTestLogger.cs");
        string emitterSource = File.ReadAllText("Assets/Scripts/AI/Planning/MdfCommandEmitter.cs");

        Assert.That(prepareSource, Does.Not.Contain("unitPurchaseComplete = true"));
        Assert.That(loggerSource, Does.Contain("#if !(UNITY_EDITOR || DEVELOPMENT_BUILD)"));
        Assert.That(loggerSource, Does.Contain("!options.Enabled"));
        Assert.That(emitterSource, Does.Contain("missing_mp_test"));
    }

    [Test]
    public void NotificationDoesNotApplyPeerPersistentEffects()
    {
        string source = File.ReadAllText("Assets/Scripts/Commands/Sync/NotifyAugmentSelectedCommand.cs");
        string snapshotSource = File.ReadAllText("Assets/Scripts/Testing/MP/MPTestStateSnapshot.cs");

        Assert.That(source, Does.Not.Contain("player.chosenAugments.Add"));
        Assert.That(source, Does.Not.Contain("player.AddOwnedBoss"));
        Assert.That(source, Does.Not.Contain("player.RegisterActiveMonsterSummonAugment"));
        Assert.That(source, Does.Not.Contain("permanentAttackDamagePercent +="));
        Assert.That(source, Does.Not.Contain("permanentAttackSpeedPercent +="));
        Assert.That(source, Does.Not.Contain("GetPresentedAugments().Clear"));
        Assert.That(source, Does.Not.Contain("presentedAugments.Clear"));
        Assert.That(snapshotSource, Does.Contain("EnumerateSelectedAugmentsForSnapshot"));
        Assert.That(snapshotSource, Does.Contain("GetSelectedAugmentSnapshotNames"));
    }

    [Test]
    public void FieldRosterReconcileKeepsRegistrationMetadataForCompactRosterOrdering()
    {
        string playerSource = File.ReadAllText("Assets/Scripts/Managers/PlayerManager.cs");
        string fieldSource = File.ReadAllText("Assets/Scripts/Managers/FieldManager.cs");
        string unitSource = File.ReadAllText("Assets/Scripts/Game/Units/Unit.cs");
        string gameManagersRosterSource = File.ReadAllText("Assets/Scripts/Managers/GameManagers.UnitRosterSync.cs");

        Assert.That(playerSource, Does.Contain("_latestUnitRegistrationById"));
        Assert.That(playerSource, Does.Contain("_pendingUnitRosterDataKeyHashes"));
        Assert.That(playerSource, Does.Contain("RememberLatestUnitRegistration"));
        Assert.That(playerSource, Does.Contain("metadataForKey.starLevel == starLevel"));
        Assert.That(playerSource, Does.Contain("unitDataKey = metadataForKey.unitDataKey"));
        Assert.That(playerSource, Does.Contain("ResolveUnitDataKeyByStableHashAsync"));
        Assert.That(playerSource, Does.Contain("StableUnitDataKeyHash(data.name) == unitDataKeyHash"));
        Assert.That(playerSource, Does.Contain("_latestUnitRegistrationById.Remove(unitIdRaw)"));
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
        Assert.That(unitSource, Does.Contain("NetworkedOwnerPlayerId"));
        Assert.That(unitSource, Does.Contain("NetworkedHasOwnerPlayerId"));
        Assert.That(unitSource, Does.Contain("OwnerPlayerIdForRoster"));
        Assert.That(unitSource, Does.Contain("SyncFieldPlacementIdentity"));
        Assert.That(gameManagersRosterSource, Does.Contain("RPC_ReconcilePlayerUnitRosterCompact"));
        Assert.That(gameManagersRosterSource, Does.Contain("ApplyCompactUnitRosterFromAuthority"));
    }

    [Test]
    public void MeleeUnitsRecheckRangeBeforeApplyingDamage()
    {
        string unitSource = File.ReadAllText("Assets/Scripts/Game/Units/Unit.cs");

        Assert.That(unitSource, Does.Contain("PruneBlockedMonsters();"));
        Assert.That(unitSource, Does.Contain("IsCurrentTargetValidForAttack(false)"));
        Assert.That(unitSource, Does.Contain("IsTargetWithinAttackRange(targetTransform, AttackRangePadding)"));
        Assert.That(unitSource, Does.Contain("IsPendingMeleeAttackStillValid()"));
        Assert.That(unitSource, Does.Contain("GetClosestTargetPoint(target, transform.position)"));
        Assert.That(unitSource, Does.Contain("monster.Unblock();"));
    }

    [Test]
    public void AttackMonsterPoolSnapshotApplyDoesNotDropUnresolvedEntries()
    {
        string playerSource = File.ReadAllText("Assets/Scripts/Managers/PlayerManager.cs");
        string augmentSource = File.ReadAllText("Assets/Scripts/Managers/AugmentManager.cs");

        Assert.That(playerSource, Does.Contain("ResolveAttackMonsterDataAsync"));
        Assert.That(playerSource, Does.Contain("FindLoadedMonsterDataByName"));
        Assert.That(playerSource, Does.Contain("FindWaveMonsterDataByName"));
        Assert.That(playerSource, Does.Contain("AttackMonsterPool snapshot apply aborted"));
        Assert.That(playerSource, Does.Not.Match(@"if \(monsterData == null\)\s*\{\s*continue;"));
        Assert.That(augmentSource, Does.Contain("FindMonsterDataByName"));
        Assert.That(augmentSource, Does.Contain("augment?.bossMonsterData"));
        Assert.That(augmentSource, Does.Contain("augment?.monsterSpawnEntries"));
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
