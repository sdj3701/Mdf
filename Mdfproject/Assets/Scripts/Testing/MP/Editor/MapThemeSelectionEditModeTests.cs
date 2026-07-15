#if UNITY_EDITOR
using System.Linq;
using System.Reflection;
using System.Text;
using Fusion;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;
using Assert = NUnit.Framework.Assert;

public sealed class MapThemeSelectionEditModeTests
{
    private const string JoinLobbyUxmlPath = "Assets/UI/JoinLobby/JoinLobby.uxml";
    private const string TokenA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string TokenB = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    [Test]
    public void Catalog_UsesStableIdsAndKeepsCurrentArenaAsDefault()
    {
        Assert.That(MapThemeCatalog.DefaultId, Is.EqualTo((int)MapThemeId.Arena));
        Assert.That(MapThemeCatalog.Entries.Count, Is.EqualTo(2));
        Assert.That(MapThemeCatalog.IsAllowed((int)MapThemeId.Classic), Is.True);
        Assert.That(MapThemeCatalog.IsAllowed((int)MapThemeId.Arena), Is.True);
        Assert.That(MapThemeCatalog.IsAllowed(0), Is.False);
        Assert.That(
            MapThemeCatalog.Entries.Select(entry => entry.ContentId).Distinct().Count(),
            Is.EqualTo(MapThemeCatalog.Entries.Count));
    }

    [Test]
    public void SessionCache_RestoresSameTokenWithoutLeakingReusedPlayerRef()
    {
        var cache = new LobbyMapThemeSessionCache();
        Assert.That(cache.Remember(4, TokenA, (int)MapThemeId.Classic), Is.True);

        cache.ForgetPlayerRef(4);
        cache.PrepareJoinedPlayer(9, TokenA);
        Assert.That(cache.TryResolve(9, TokenA, out int reconnectedTheme), Is.True);
        Assert.That(reconnectedTheme, Is.EqualTo((int)MapThemeId.Classic));

        cache.PrepareJoinedPlayer(9, TokenB);
        Assert.That(cache.TryResolve(9, TokenB, out _), Is.False,
            "a reused PlayerRef must not inherit another player's cosmetic selection");

        cache.Remember(9, TokenA, (int)MapThemeId.Classic);
        Assert.That(cache.TryResolve(9, TokenB, out _), Is.False,
            "an unknown valid token must not fall back to a stale runner-local PlayerRef");

        cache.ClearPlayerRefs();
        Assert.That(cache.TryResolve(9, string.Empty, out _), Is.False);
        Assert.That(cache.TryResolve(9, TokenA, out reconnectedTheme), Is.True,
            "clearing runner-local refs must preserve durable token selections");
    }

    [Test]
    public void NetworkState_ReplicatesThemeOnLobbyAndGameplayObjects()
    {
        PropertyInfo lobbyTheme = typeof(NetworkPlayer).GetProperty(nameof(NetworkPlayer.SelectedMapThemeId));
        PropertyInfo gameplayTheme = typeof(PlayerManager).GetProperty(nameof(PlayerManager.SelectedMapThemeId));
        Assert.That(lobbyTheme, Is.Not.Null);
        Assert.That(gameplayTheme, Is.Not.Null);
        Assert.That(lobbyTheme.GetCustomAttributes(typeof(NetworkedAttribute), true), Is.Not.Empty);
        Assert.That(gameplayTheme.GetCustomAttributes(typeof(NetworkedAttribute), true), Is.Not.Empty);

        MethodInfo request = typeof(NetworkPlayer).GetMethod(nameof(NetworkPlayer.RequestMapThemeSelection));
        MethodInfo authorityRpc = typeof(NetworkPlayer).GetMethod(
            "RPC_SetMapThemeSelection",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(request, Is.Not.Null);
        Assert.That(authorityRpc, Is.Not.Null);
        Assert.That(authorityRpc.GetCustomAttributes(typeof(RpcAttribute), true), Is.Not.Empty);
    }

    [Test]
    public void JoinLobby_HasTwoPersonalThemeCardsAndThemeOnEveryPlayerSlot()
    {
        VisualTreeAsset tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(JoinLobbyUxmlPath);
        Assert.That(tree, Is.Not.Null);
        TemplateContainer root = tree.CloneTree();

        Assert.That(root.Q<VisualElement>("mapThemePanel"), Is.Not.Null);
        for (int i = 0; i < MapThemeCatalog.Entries.Count; i++)
        {
            Assert.That(root.Q<VisualElement>($"mapThemeCard{i}"), Is.Not.Null);
            Assert.That(root.Q<Label>($"mapThemeName{i}"), Is.Not.Null);
        }

        for (int i = 0; i < 4; i++)
        {
            Assert.That(root.Q<Label>($"slotTheme{i}"), Is.Not.Null);
        }
    }

    [Test]
    public void ProtocolVersion_IsolatesTheNewFusionStateLayout()
    {
        Assert.That(MdfNetworkProtocol.AppVersion, Is.EqualTo("mdf-p3-player-map-theme"));
    }

    [Test]
    public void DurableMigrationFallbackCarriesEachPlayersTheme()
    {
        System.Type snapshot = typeof(HostMigrationHandler).GetNestedType(
            "DurablePlayerMigrationSnapshot",
            BindingFlags.NonPublic);
        Assert.That(snapshot, Is.Not.Null);
        Assert.That(snapshot.GetField("SelectedMapThemeId"), Is.Not.Null);
        Assert.That(
            typeof(PlayerManager).GetMethod(nameof(PlayerManager.RestoreMapThemeAfterHostMigration)),
            Is.Not.Null);
        Assert.That(
            typeof(NetworkManager).GetMethod(nameof(NetworkManager.TryGetLobbyMapThemeForGameplay)),
            Is.Not.Null);
    }

    [Test]
    public void CosmeticTheme_InvalidValuesAlwaysHaveANonBlockingDefault()
    {
        Assert.That(MapThemeCatalog.NormalizeOrDefault(0), Is.EqualTo(MapThemeCatalog.DefaultId));
        Assert.That(MapThemeCatalog.NormalizeOrDefault(int.MinValue), Is.EqualTo(MapThemeCatalog.DefaultId));
        Assert.That(MapThemeCatalog.IsAllowed(MapThemeCatalog.DefaultId), Is.True);
    }

    [Test]
    public void HarnessConnectionTokenOverride_RemainsProcessLocalInsteadOfUsingSharedPlayerPrefs()
    {
        MethodInfo resolve = typeof(NetworkManager).GetMethod(
            "ResolveLocalConnectionTokenBytes",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(resolve, Is.Not.Null);

        byte[] first = (byte[])resolve.Invoke(null, new object[] { "map-theme-peer-a" });
        byte[] second = (byte[])resolve.Invoke(null, new object[] { "map-theme-peer-b" });
        Assert.That(Encoding.UTF8.GetString(first), Is.EqualTo("map-theme-peer-a"));
        Assert.That(Encoding.UTF8.GetString(second), Is.EqualTo("map-theme-peer-b"));
        Assert.That(first, Is.Not.EqualTo(second));
    }
}
#endif
