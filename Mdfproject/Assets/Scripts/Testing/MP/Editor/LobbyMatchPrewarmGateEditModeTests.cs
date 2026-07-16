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
        Assert.That(matchWarmup, Does.Contain("data.projectilePrefab"));
        Assert.That(matchWarmup, Does.Contain("CollectSkillPresentationPrefabs(monsterData.skillData)"));
        Assert.That(matchWarmup, Does.Contain("provider.PrewarmPrefabAsync"));
        Assert.That(matchWarmup, Does.Contain("PrewarmUnitNetworkPoolAsync"));
        Assert.That(matchWarmup, Does.Contain("_matchContentPrewarmComplete = true"));
        Assert.That(matchWarmup, Does.Not.Contain("if (_matchContentPrewarmComplete)"));
    }

    [Test]
    public void AuthorityAckPolicyRejectsStaleForeignAndDuplicateReports()
    {
        PropertyInfo revision = typeof(NetworkPlayer).GetProperty(
            nameof(NetworkPlayer.MatchContentLoadRevision));
        PropertyInfo state = typeof(NetworkPlayer).GetProperty(
            nameof(NetworkPlayer.MatchContentLoadStateValue));
        Assert.That(revision?.GetCustomAttribute<NetworkedAttribute>(), Is.Not.Null);
        Assert.That(state?.GetCustomAttribute<NetworkedAttribute>(), Is.Not.Null);

        const int owner = 3;
        const int currentRevision = 9;
        Assert.That(LobbyMatchLoadPolicy.CanRecordAcknowledgement(
            true, true, owner, owner, currentRevision, currentRevision,
            LobbyMatchLoadingState.Warming), Is.True);
        Assert.That(LobbyMatchLoadPolicy.CanRecordAcknowledgement(
            true, true, owner, owner + 1, currentRevision, currentRevision,
            LobbyMatchLoadingState.Warming), Is.False);
        Assert.That(LobbyMatchLoadPolicy.CanRecordAcknowledgement(
            true, true, owner, owner, currentRevision - 1, currentRevision,
            LobbyMatchLoadingState.Warming), Is.False);
        Assert.That(LobbyMatchLoadPolicy.CanRecordAcknowledgement(
            true, true, owner, owner, currentRevision, currentRevision,
            LobbyMatchLoadingState.Ready), Is.False);
    }

    [Test]
    public void HostGateRequiresStableRosterAndEveryReadyAck()
    {
        int revision = LobbyMatchLoadPolicy.NextRevision(new[] { 2, 4, 4 });
        int[] expected = { 1, 2, 4 };
        Assert.That(revision, Is.EqualTo(5));
        Assert.That(LobbyMatchLoadPolicy.HasSameRoster(expected, new[] { 1, 2, 4 }), Is.True);
        Assert.That(LobbyMatchLoadPolicy.HasSameRoster(expected, new[] { 1, 3, 4 }), Is.False);

        var warming = new[]
        {
            new LobbyMatchPeerLoadState(1, revision, LobbyMatchLoadingState.Ready),
            new LobbyMatchPeerLoadState(2, revision, LobbyMatchLoadingState.Warming),
            new LobbyMatchPeerLoadState(4, revision, LobbyMatchLoadingState.Ready)
        };
        Assert.That(LobbyMatchLoadPolicy.CanLoadScene(revision, expected, warming), Is.False);

        var ready = new[]
        {
            new LobbyMatchPeerLoadState(1, revision, LobbyMatchLoadingState.Ready),
            new LobbyMatchPeerLoadState(2, revision, LobbyMatchLoadingState.Ready),
            new LobbyMatchPeerLoadState(4, revision, LobbyMatchLoadingState.Ready)
        };
        Assert.That(LobbyMatchLoadPolicy.CanLoadScene(revision, expected, ready), Is.True);
        Assert.That(LobbyMatchLoadPolicy.CanLoadScene(revision - 1, expected, ready), Is.False);
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
