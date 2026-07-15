#if UNITY_EDITOR
using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Fusion;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;
using NUnitAssert = NUnit.Framework.Assert;

public sealed class KingLobbySelectionEditModeTests
{
    private const BindingFlags InstanceMembers =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags StaticMembers =
        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    [Test]
    public void CanonicalCatalogHasSevenUniqueAllowListedKingKeysAndArcherDefault()
    {
        KingSelectionCatalog.Entry[] entries = KingSelectionCatalog.Entries.ToArray();

        NUnitAssert.That(entries, Has.Length.EqualTo(7));
        NUnitAssert.That(entries.Select(entry => entry.ContentId), Is.Unique);
        NUnitAssert.That(entries.Select(entry => entry.KingUnitKey), Is.Unique);
        NUnitAssert.That(entries.Select(entry => entry.KeyHash), Is.Unique);
        NUnitAssert.That(entries.Select(entry => entry.LegacyKeyHash), Is.Unique);
        NUnitAssert.That(entries.All(entry => entry.KingUnitKey.StartsWith("UnitData_King_", StringComparison.Ordinal)),
            Is.True);
        NUnitAssert.That(entries.All(entry => entry.KeyHash == StableDataKeyUtility.StableContentIdHash(entry.ContentId)),
            Is.True);
        NUnitAssert.That(entries.All(entry => entry.LegacyKeyHash == StableDataKeyUtility.StableKeyHash(entry.KingUnitKey)),
            Is.True);
        NUnitAssert.That(KingSelectionCatalog.DefaultKey, Is.EqualTo("UnitData_King_Archer"));
        NUnitAssert.That(KingSelectionCatalog.IsAllowedHash(KingSelectionCatalog.DefaultKeyHash), Is.True);
        NUnitAssert.That(KingSelectionCatalog.NormalizeOrDefaultHash(0),
            Is.EqualTo(KingSelectionCatalog.DefaultKeyHash));
        NUnitAssert.That(KingSelectionCatalog.GetAssetKey(KingSelectionCatalog.DefaultKeyHash),
            Is.EqualTo(KingSelectionCatalog.DefaultKey));
        NUnitAssert.That(KingSelectionCatalog.IsAllowedHash(KingSelectionCatalog.DefaultLegacyKeyHash), Is.True);
        NUnitAssert.That(KingSelectionCatalog.NormalizeOrDefaultHash(KingSelectionCatalog.DefaultLegacyKeyHash),
            Is.EqualTo(KingSelectionCatalog.DefaultKeyHash),
            "old PlayerPrefs/network values must be accepted and upgraded to the durable contentId hash");
        NUnitAssert.That(KingSelectionCatalog.GetAssetKey(KingSelectionCatalog.DefaultLegacyKeyHash),
            Is.EqualTo(KingSelectionCatalog.DefaultKey));
        NUnitAssert.That(entries.All(entry =>
        {
            KingUnitData data = AssetDatabase.LoadAssetAtPath<KingUnitData>(
                $"Assets/GameData/Units/{entry.KingUnitKey}.asset");
            return data != null && data.ContentId == entry.ContentId && data.ContentIdHash == entry.KeyHash;
        }), Is.True, "catalog identities must be sourced from the authored KingUnitData.contentId values");
        NUnitAssert.That(KingSelectionCatalog.IsAllowedHash(0), Is.False);
        NUnitAssert.That(KingSelectionCatalog.IsAllowedHash(int.MinValue), Is.False);
    }

    [Test]
    public void NetworkPlayerReplicatesSelectionAndServerRpcOwnsValidationAndReadyReset()
    {
        PropertyInfo selection = typeof(NetworkPlayer).GetProperty(
            nameof(NetworkPlayer.SelectedKingUnitKeyHash),
            InstanceMembers);
        MethodInfo selectionRpc = typeof(NetworkPlayer).GetMethod(
            "RPC_SetKingSelection",
            InstanceMembers);
        MethodInfo initialRpc = typeof(NetworkPlayer).GetMethod(
            "RPC_SetInitialData",
            InstanceMembers);
        MethodInfo readyRpc = typeof(NetworkPlayer).GetMethod(
            nameof(NetworkPlayer.RPC_ToggleReady),
            InstanceMembers);

        NUnitAssert.That(selection, Is.Not.Null);
        NUnitAssert.That(selection.GetCustomAttribute<NetworkedAttribute>(), Is.Not.Null);
        NUnitAssert.That(selectionRpc, Is.Not.Null);
        NUnitAssert.That(initialRpc, Is.Not.Null);
        NUnitAssert.That(selectionRpc.GetCustomAttribute<RpcAttribute>(), Is.Not.Null);
        NUnitAssert.That(readyRpc, Is.Not.Null);
        NUnitAssert.That(MdfCompiledCodePolicy.ReferencesMethod(
            selectionRpc,
            typeof(KingSelectionCatalog),
            nameof(KingSelectionCatalog.IsAllowedHash)), Is.True);
        NUnitAssert.That(MdfCompiledCodePolicy.ReferencesMethod(
            selectionRpc,
            typeof(NetworkPlayer),
            "set_SelectedKingUnitKeyHash"), Is.True);
        NUnitAssert.That(MdfCompiledCodePolicy.ReferencesMethod(
            selectionRpc,
            typeof(NetworkPlayer),
            "set_IsReady"), Is.True,
            "A changed king selection must clear the server-owned ready state.");
        NUnitAssert.That(MdfCompiledCodePolicy.ReferencesMethod(
            selectionRpc,
            typeof(NetworkPlayer),
            "TryRememberKingSelectionForSession"), Is.True,
            "The authoritative RPC result must be mirrored into the durable session cache.");
        NUnitAssert.That(MdfCompiledCodePolicy.ReferencesMethod(
            readyRpc,
            typeof(KingSelectionCatalog),
            nameof(KingSelectionCatalog.IsAllowedHash)), Is.True);
        NUnitAssert.That(MdfCompiledCodePolicy.ReferencesMethod(
            initialRpc,
            typeof(NetworkManager),
            nameof(NetworkManager.ResolveInitialLobbyKingSelection)), Is.True,
            "Reconnect initialization must prefer the server's token-backed selection over local defaults.");
    }

    [Test]
    public void GameplaySpawnCopiesLobbySelectionIntoAuthoritativePlayerManager()
    {
        MethodInfo setup = typeof(GameManagers).GetMethod("SetupPlayersAndGrids", InstanceMembers);
        AsyncStateMachineAttribute stateMachine = setup?.GetCustomAttribute<AsyncStateMachineAttribute>();
        MethodInfo moveNext = stateMachine?.StateMachineType.GetMethod("MoveNext", InstanceMembers);

        NUnitAssert.That(setup, Is.Not.Null);
        NUnitAssert.That(moveNext, Is.Not.Null);
        NUnitAssert.That(MdfCompiledCodePolicy.ReferencesMethod(
            moveNext,
            typeof(PlayerManager),
            "SetSelectedKingKeyHashAuthoritative"), Is.True);
        NUnitAssert.That(MdfCompiledCodePolicy.ReferencesMethod(
            moveNext,
            typeof(KingSelectionCatalog),
            nameof(KingSelectionCatalog.IsAllowedHash)), Is.True);
        NUnitAssert.That(MdfCompiledCodePolicy.ReferencesMethod(
            moveNext,
            typeof(NetworkManager),
            nameof(NetworkManager.TryGetLobbyKingSelectionForGameplay)), Is.True,
            "Gameplay setup must use the scene-independent server cache after lobby unload.");
    }

    [Test]
    public void LobbySelectionCacheSurvivesReconnectWithoutLeakingAcrossReusedPlayerRefs()
    {
        const string tokenA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        const string tokenB = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        int mage = KingSelectionCatalog.Entries.Single(entry =>
            entry.KingUnitKey == "UnitData_King_Mage").KeyHash;

        var cache = new LobbyKingSelectionSessionCache();
        NUnitAssert.That(cache.Remember(0, tokenA, mage), Is.True);
        cache.ForgetPlayerRef(0);
        cache.PrepareJoinedPlayer(3, tokenA);
        int reconnectedSelection = cache.ResolveInitialSelection(
            3,
            tokenA,
            KingSelectionCatalog.DefaultKeyHash);
        NUnitAssert.That(reconnectedSelection, Is.EqualTo(mage));

        cache.PrepareJoinedPlayer(0, tokenB);
        NUnitAssert.That(cache.TryResolve(0, tokenB, out _), Is.False,
            "A reused PlayerRef must not inherit another connection's king.");
        NUnitAssert.That(cache.Remember(0, tokenB, int.MinValue), Is.False);
    }

    [Test]
    public void LobbyRosterAndDisconnectPoliciesExcludeGhostPlayers()
    {
        NUnitAssert.That(JoinLobbyUI.IsLobbyRosterComplete(2, 2), Is.True);
        NUnitAssert.That(JoinLobbyUI.IsLobbyRosterComplete(2, 1), Is.False);
        NUnitAssert.That(JoinLobbyUI.IsLobbyRosterComplete(2, 2, true), Is.False);
        NUnitAssert.That(JoinLobbyUI.IsLobbyRosterComplete(0, 0), Is.False);
        NUnitAssert.That(NetworkManager.ShouldCleanupDisconnectedLobbyObject(SceneDefine.JoinLobby, false), Is.True);
        NUnitAssert.That(NetworkManager.ShouldCleanupDisconnectedLobbyObject(SceneDefine.JoinLobby, true), Is.False);
        NUnitAssert.That(NetworkManager.ShouldCleanupDisconnectedLobbyObject(SceneDefine.Game, false), Is.False);
        NUnitAssert.That(NetworkManager.ShouldUseGameplayReconnectCache(SceneDefine.Game), Is.True);
        NUnitAssert.That(NetworkManager.ShouldUseGameplayReconnectCache(SceneDefine.JoinLobby), Is.False);
        NUnitAssert.That(NetworkManager.ShouldUseGameplayReconnectCache(SceneDefine.MatchingLobby), Is.False);

        MethodInfo update = typeof(JoinLobbyUI).GetMethod(nameof(JoinLobbyUI.UpdatePlayerList), InstanceMembers);
        MethodInfo start = typeof(JoinLobbyUI).GetMethod("TryStartGame", InstanceMembers);
        NUnitAssert.That(MdfCompiledCodePolicy.ReferencesMethod(
            update,
            typeof(JoinLobbyUI),
            "CollectActiveLobbyPlayers"), Is.True);
        NUnitAssert.That(MdfCompiledCodePolicy.ReferencesMethod(
            start,
            typeof(JoinLobbyUI),
            "CollectActiveLobbyPlayers"), Is.True);
    }

    [Test]
    public void SelectKingAutomationUsesTheOwningNetworkPlayerRequestPath()
    {
        MethodInfo runtimeExecute = typeof(MPTestAutomationServer).GetMethod(
            "ExecuteSelectKingCommand",
            InstanceMembers);
        MethodInfo runtimeRoute = typeof(MPTestAutomationServer).GetMethod(
            "ExecuteCommand",
            InstanceMembers);
        MethodInfo editorExecute = typeof(MPTestUnityCliTools).GetMethod(
            "ExecuteSelectKingCommand",
            StaticMembers);
        MethodInfo editorRoute = typeof(MPTestUnityCliTools).GetMethod(
            nameof(MPTestUnityCliTools.ExecuteCommand),
            StaticMembers);

        NUnitAssert.That(runtimeExecute, Is.Not.Null);
        NUnitAssert.That(MdfCompiledCodePolicy.ReferencesMethod(
            runtimeExecute,
            typeof(NetworkPlayer),
            nameof(NetworkPlayer.RequestKingSelection)), Is.True,
            "Runtime automation must not bypass the production input-authority RPC path.");
        NUnitAssert.That(MdfCompiledCodePolicy.ReferencesMethod(
            runtimeRoute,
            typeof(MPTestAutomationServer),
            "ExecuteSelectKingCommand"), Is.True);
        NUnitAssert.That(editorExecute, Is.Not.Null);
        NUnitAssert.That(MdfCompiledCodePolicy.ReferencesMethod(
            editorExecute,
            typeof(NetworkPlayer),
            nameof(NetworkPlayer.RequestKingSelection)), Is.True,
            "Editor automation must not bypass the production input-authority RPC path.");
        NUnitAssert.That(MdfCompiledCodePolicy.ReferencesMethod(
            editorRoute,
            typeof(MPTestUnityCliTools),
            "ExecuteSelectKingCommand"), Is.True);
        NUnitAssert.That(typeof(MPCommandTool.Parameters).GetProperty("KingKey"), Is.Not.Null,
            "mp_command must expose the king_key parameter through its tool schema.");
    }

    [Test]
    public void JoinLobbyTreeContainsSevenSelectableKingCardsAndPerPlayerKingLabels()
    {
        VisualTreeAsset treeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
            "Assets/UI/JoinLobby/JoinLobby.uxml");

        NUnitAssert.That(treeAsset, Is.Not.Null);
        TemplateContainer tree = treeAsset.CloneTree();
        NUnitAssert.That(tree.Q<VisualElement>("kingSelectionPanel"), Is.Not.Null);

        for (int i = 0; i < 7; i++)
        {
            NUnitAssert.That(tree.Q<VisualElement>($"kingCard{i}"), Is.Not.Null, $"kingCard{i}");
            NUnitAssert.That(tree.Q<VisualElement>($"kingPortrait{i}"), Is.Not.Null, $"kingPortrait{i}");
            NUnitAssert.That(tree.Q<Label>($"kingName{i}"), Is.Not.Null, $"kingName{i}");
            NUnitAssert.That(tree.Q<Label>($"kingSelection{i}"), Is.Not.Null, $"kingSelection{i}");
        }

        for (int i = 0; i < 4; i++)
        {
            NUnitAssert.That(tree.Q<Label>($"slotKing{i}"), Is.Not.Null, $"slotKing{i}");
        }
    }
}
#endif
