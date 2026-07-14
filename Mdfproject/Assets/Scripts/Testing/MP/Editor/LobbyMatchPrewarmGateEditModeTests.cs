#if UNITY_EDITOR
using System;
using System.Reflection;
using Fusion;
using NUnit.Framework;
using Assert = NUnit.Framework.Assert;

public sealed class LobbyMatchPrewarmGateEditModeTests
{
    [Test]
    public void TitleBootIsCatalogOnlyAndDoesNotStartGameplayWarmup()
    {
        string bootstrap = MdfSourcePolicy.ReadStaticContract(
            "Assets/Scripts/Bootstrap/AppBootstrapper.cs");
        Assert.That(bootstrap, Does.Contain("await AddressablesManager.Instance.InitializeAsync()"));
        Assert.That(bootstrap, Does.Not.Contain("await LoadManager.Instance.InitializeAsync()"));

        string loadManager = MdfSourcePolicy.ReadStaticContract(
            "Assets/Scripts/Managers/LoadManager.cs");
        string initialize = Slice(
            loadManager,
            "public async UniTask InitializeAsync()",
            "public UniTask WaitUntilReady()");
        Assert.That(initialize, Does.Not.Contain("PrewarmUnitPresentationsAsync"));
        Assert.That(initialize, Does.Not.Contain("FirstSpawnPresentationPrewarmer"));
        Assert.That(initialize, Does.Not.Contain("PrewarmPrefabAsync"));
    }

    [Test]
    public void MatchGateWarmsRequiredGameplayGraphsAndRunnerPools()
    {
        string loadManager = MdfSourcePolicy.ReadStaticContract(
            "Assets/Scripts/Managers/LoadManager.cs");
        string matchWarmup = Slice(
            loadManager,
            "public async UniTask PrewarmMatchContentAsync",
            "public async UniTask<int> PrewarmUnitNetworkPoolAsync");

        Assert.That(matchWarmup, Does.Contain("LoadGamePrefabsAsync"));
        Assert.That(matchWarmup, Does.Contain("PrewarmUnitPresentationsAsync"));
        Assert.That(matchWarmup, Does.Contain("MatchMonsterDataLabel"));
        Assert.That(matchWarmup, Does.Contain("MatchAugmentDataLabel"));
        Assert.That(matchWarmup, Does.Contain("MatchKingDataLabel"));
        Assert.That(matchWarmup, Does.Contain("MatchScrollDataLabel"));
        Assert.That(matchWarmup, Does.Contain("PrewarmAdditionalCombatPresentationsAsync"));
        Assert.That(matchWarmup, Does.Contain("FirstSpawnPresentationPrewarmer.WarmPrefabAsync"));
        Assert.That(matchWarmup, Does.Contain("data.projectilePrefab"),
            "Ranged monster projectiles must be loaded and GPU-warmed before the peer ACKs ready.");
        Assert.That(matchWarmup, Does.Contain("CollectSkillPresentationPrefabs(monsterData.skillData)"),
            "Monster skill and zone presentations must be warmed by the same lobby gate.");
        Assert.That(matchWarmup, Does.Contain("provider.PrewarmPrefabAsync"));
        Assert.That(matchWarmup, Does.Contain("PrewarmUnitNetworkPoolAsync"));
        Assert.That(matchWarmup, Does.Contain("_matchContentPrewarmComplete = true"));
        Assert.That(matchWarmup, Does.Not.Contain("if (_matchContentPrewarmComplete)"),
            "Static content readiness must never skip pool target enforcement for a new Runner.");
    }

    [Test]
    public void AuthorityOwnsPeerAckStateAndRejectsStaleOrForeignReports()
    {
        PropertyInfo revision = typeof(NetworkPlayer).GetProperty(
            nameof(NetworkPlayer.MatchContentLoadRevision));
        PropertyInfo state = typeof(NetworkPlayer).GetProperty(
            nameof(NetworkPlayer.MatchContentLoadStateValue));
        Assert.That(revision, Is.Not.Null);
        Assert.That(state, Is.Not.Null);
        Assert.That(revision.GetCustomAttribute<NetworkedAttribute>(), Is.Not.Null);
        Assert.That(state.GetCustomAttribute<NetworkedAttribute>(), Is.Not.Null);

        string player = MdfSourcePolicy.ReadStaticContract(
            "Assets/Scripts/Network/NetworkPlayer.cs");
        Assert.That(player, Does.Contain("[Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]"));
        Assert.That(player, Does.Contain("source != Object.InputAuthority"));
        Assert.That(player, Does.Contain("TryRecordMatchContentLoadingAuthority(revision, succeeded, info.Source)"));
        Assert.That(player, Does.Contain("TryRecordMatchContentLoadingAuthority("));
        Assert.That(player, Does.Contain("Object.InputAuthority);"),
            "A host must commit its own ready result with the real owner because a local RPC source is None.");
        Assert.That(player, Does.Contain("revision != MatchContentLoadRevision"));
        Assert.That(player, Does.Contain("MatchContentLoadState != LobbyMatchLoadingState.Warming"));
        Assert.That(player, Does.Contain("public override void Despawned(NetworkRunner runner, bool hasState)"));
        Assert.That(player, Does.Contain("attemptCancellation.Token"));

        string loadManager = MdfSourcePolicy.ReadStaticContract(
            "Assets/Scripts/Managers/LoadManager.cs");
        Assert.That(loadManager, Does.Contain("CancellationTokenSource.CreateLinkedTokenSource"));
        Assert.That(loadManager, Does.Contain("PrewarmUnitNetworkPoolCoreAsync("));
        Assert.That(loadManager, Does.Contain("CancellationTokenSource operationCancellation"));
    }

    [Test]
    public void HostLoadsGameOnlyAfterStableRosterAndEveryReadyAck()
    {
        string lobby = MdfSourcePolicy.ReadStaticContract(
            "Assets/Scripts/Network/JoinLobbyUI.cs");
        string startGate = Slice(
            lobby,
            "private async UniTaskVoid TryStartGameAsync()",
            "private void FailMatchStart(");

        int begin = startGate.IndexOf("BeginMatchContentLoadingAuthority", StringComparison.Ordinal);
        int rosterGate = startGate.IndexOf("expectedAuthorities.SequenceEqual", StringComparison.Ordinal);
        int readyGate = startGate.IndexOf("currentPlayers.All(player => player.IsMatchContentReadyFor(revision))", StringComparison.Ordinal);
        int loadScene = startGate.IndexOf("runner.LoadScene", StringComparison.Ordinal);
        Assert.That(begin, Is.GreaterThanOrEqualTo(0));
        Assert.That(rosterGate, Is.GreaterThan(begin));
        Assert.That(readyGate, Is.GreaterThan(rosterGate));
        Assert.That(loadScene, Is.GreaterThan(readyGate));

        Assert.That(startGate, Does.Contain("LobbyMatchLoadingState.Failed"));
        Assert.That(startGate, Does.Contain("MatchContentLoadTimeoutSeconds"));
        Assert.That(lobby, Does.Contain("networkBlockOverlay?.Q<Label>(className: \"jl-network-block-label\")"));
        Assert.That(lobby, Does.Contain("전투 데이터를 준비하고 있습니다"));
        Assert.That(lobby, Does.Contain("ResetOrphanedAuthorityMatchLoad"));
        Assert.That(lobby, Does.Contain("orphaned_authority_gate"));
    }

    [Test]
    public void HarnessUsesTheProductionLobbyStartPath()
    {
        string server = MdfSourcePolicy.ReadStaticContract(
            "Assets/Scripts/Testing/MP/MPTestAutomationServer.cs");
        string client = MdfSourcePolicy.ReadStaticContract(
            "../tools/harness/mp/automation_client.py");
        string progression = MdfSourcePolicy.ReadStaticContract(
            "../tools/harness/mp/run_human_bot_3round_progression.py");
        Assert.That(server, Does.Contain("path == \"/lobby/startGame\""));
        Assert.That(server, Does.Contain("lobby.RequestMatchStart()"));
        Assert.That(server, Does.Contain("automation_lobby_start_game"));
        Assert.That(server, Does.Contain("path == \"/lobby/ready\""));
        Assert.That(server, Does.Contain("localPlayer.RPC_ToggleReady()"));
        Assert.That(client, Does.Contain("def lobby_start_game"));
        Assert.That(client, Does.Contain("/lobby/startGame"));
        Assert.That(progression, Does.Contain("--use-lobby-start-gate"));
        Assert.That(progression, Does.Contain("host.lobby_start_game()"));
        Assert.That(progression, Does.Contain("match-prewarm-logs.json"));
    }

    private static string Slice(string source, string startMarker, string endMarker)
    {
        int start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), startMarker);
        int end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.That(end, Is.GreaterThan(start), endMarker);
        return source.Substring(start, end - start);
    }
}
#endif
