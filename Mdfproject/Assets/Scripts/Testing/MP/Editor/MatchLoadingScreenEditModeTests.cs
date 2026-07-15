using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;

public sealed class MatchLoadingScreenEditModeTests
{
    [Test]
    public void LoadingScreenResourcesContainTheRequiredPresentationElements()
    {
        const string treePath = "Assets/Resources/UI/MatchLoading/MatchLoadingScreen.uxml";
        const string stylePath = "Assets/Resources/UI/MatchLoading/MatchLoadingScreen.uss";

        VisualTreeAsset tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(treePath);
        StyleSheet style = AssetDatabase.LoadAssetAtPath<StyleSheet>(stylePath);

        Assert.That(tree, Is.Not.Null);
        Assert.That(style, Is.Not.Null);

        TemplateContainer root = tree.CloneTree();
        Assert.That(root.Q<Label>("match-loading-stage"), Is.Not.Null);
        Assert.That(root.Q<VisualElement>("match-loading-art"), Is.Not.Null);
        Assert.That(root.Q<Label>("match-loading-tip"), Is.Not.Null);
        Assert.That(root.Q<VisualElement>("match-loading-track"), Is.Not.Null);
        Assert.That(root.Q<VisualElement>("match-loading-fill"), Is.Not.Null);
        Assert.That(root.Q<VisualElement>("match-loading-runner"), Is.Not.Null);
        Assert.That(root.Q<Label>("match-loading-percent"), Is.Not.Null);
        Assert.That(root.Q<Label>("match-loading-peers"), Is.Not.Null);
    }

    [Test]
    public void PeerProgressWaitsForLocalPrewarmAndNeverCompletesBeforeGameReadiness()
    {
        Assert.That(
            MatchLoadingProgressPolicy.ResolvePeerTarget(0.4f, 2, 2),
            Is.EqualTo(0.4f).Within(0.0001f));
        Assert.That(
            MatchLoadingProgressPolicy.ResolvePeerTarget(
                MatchLoadingProgressPolicy.LocalContentCeiling,
                0,
                2),
            Is.EqualTo(MatchLoadingProgressPolicy.LocalContentCeiling).Within(0.0001f));
        Assert.That(
            MatchLoadingProgressPolicy.ResolvePeerTarget(
                MatchLoadingProgressPolicy.LocalContentCeiling,
                2,
                2),
            Is.EqualTo(MatchLoadingProgressPolicy.PeerReadyCeiling).Within(0.0001f));
        Assert.That(MatchLoadingProgressPolicy.SceneLoadedTarget, Is.LessThan(1f));
        Assert.That(MatchLoadingProgressPolicy.IsGameScene(SceneDefine.Game), Is.True);
        Assert.That(MatchLoadingProgressPolicy.IsGameScene(SceneDefine.JoinLobby), Is.False);
        Assert.That(
            MatchLoadingProgressPolicy.ShouldBeginCompletionFade(true, 1f, 2f, 1f, -1f),
            Is.True);
        Assert.That(
            MatchLoadingProgressPolicy.ShouldBeginCompletionFade(true, 1f, 2f, 1f, 1.5f),
            Is.False,
            "Once fading starts, its start time must not be reset every frame.");
    }

    [Test]
    public void LobbyPrewarmAndFusionSceneCallbacksDriveThePersistentLoadingScreen()
    {
        string lobbySource = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/Network/JoinLobbyUI.cs");
        string loadSource = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/Managers/LoadManager.cs");
        string networkSource = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/Network/NetworkManager.cs");
        string controllerSource = MdfSourcePolicy.ReadStaticContract(
            "Assets/Scripts/UI/Loading/MatchLoadingScreenController.cs");

        Assert.That(lobbySource, Does.Contain("MatchLoadingScreenController.Show("));
        Assert.That(lobbySource, Does.Contain("MatchLoadingScreenController.SetPeerReadiness("));
        Assert.That(lobbySource, Does.Contain("MatchLoadingScreenController.BeginSceneTransition();"));
        Assert.That(loadSource, Does.Contain("ReportMatchContentPrewarmProgress("));
        Assert.That(networkSource, Does.Contain("MatchLoadingScreenController.NotifySceneLoadStart();"));
        Assert.That(networkSource, Does.Contain("MatchLoadingScreenController.NotifySceneLoadDone();"));
        Assert.That(controllerSource, Does.Contain("GameEvents.OnGameManagersReady += HandleGameManagersReady;"));
        Assert.That(controllerSource, Does.Contain("ObserveActiveScene();"));
    }
}
