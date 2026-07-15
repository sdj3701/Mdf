#if UNITY_EDITOR
using System.Linq;
using MDF.Runtime.Grid;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class ArenaBackgroundPresentationEditModeTests
{
    private const string PrefabPath = "Assets/Prefabs/User_Grid3D.prefab";
    private const string GameManagersPrefabPath = "Assets/Prefabs/GameManagers.prefab";
    private const string MaterialPath = "Assets/Resource/Materials/Mat_ArenaBackground.mat";
    private const string TexturePath = "Assets/Resource/Image/UI/Game/arena_background.png";

    [Test]
    public void FieldPrefab_UsesArenaVisualWithoutChangingSpawnInputCollider()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Assert.That(prefab, Is.Not.Null);

        Transform visual = prefab.transform.Find("ArenaBackgroundVisual");
        Assert.That(visual, Is.Not.Null);
        Assert.That(visual.gameObject.layer, Is.EqualTo(LayerMask.NameToLayer("Default")));
        Assert.That(visual.GetComponent<Collider>(), Is.Null);

        MeshFilter visualMesh = visual.GetComponent<MeshFilter>();
        MeshRenderer visualRenderer = visual.GetComponent<MeshRenderer>();
        Assert.That(visualMesh, Is.Not.Null);
        Assert.That(visualMesh.sharedMesh, Is.Not.Null);
        Assert.That(visualRenderer, Is.Not.Null);
        Assert.That(visualRenderer.enabled, Is.True);
        Assert.That(visualRenderer.shadowCastingMode, Is.EqualTo(UnityEngine.Rendering.ShadowCastingMode.Off));
        Assert.That(visualRenderer.receiveShadows, Is.False);
        Assert.That(AssetDatabase.GetAssetPath(visualRenderer.sharedMaterial), Is.EqualTo(MaterialPath));
        Assert.That(AssetDatabase.GetAssetPath(visualRenderer.sharedMaterial.mainTexture), Is.EqualTo(TexturePath));

        Vector3 worldNormal = visual.TransformDirection(visualMesh.sharedMesh.normals[0]).normalized;
        Assert.That(Vector3.Dot(worldNormal, Vector3.up), Is.GreaterThan(0.99f));
        Vector3 imageUp = visual.TransformDirection(Vector3.up).normalized;
        Assert.That(Vector3.Dot(imageUp, Vector3.back), Is.GreaterThan(0.99f));

        Transform activeLegacyGround = prefab.transform
            .Cast<Transform>()
            .Single(child =>
                child.name == "Ground" &&
                child.gameObject.activeSelf &&
                child.GetComponent<MeshRenderer>() != null);
        Assert.That(activeLegacyGround.GetComponent<MeshRenderer>().enabled, Is.False);

        Transform inputBackground = prefab.transform.Find("BackGround");
        Assert.That(inputBackground, Is.Not.Null);
        Assert.That(inputBackground.gameObject.layer, Is.EqualTo(LayerMask.NameToLayer("SpawnArea")));
        Assert.That(inputBackground.localPosition, Is.EqualTo(new Vector3(3.5f, -0.01f, 3.5f)));
        Assert.That(inputBackground.localScale, Is.EqualTo(new Vector3(15f, 0.01f, 13f)));
        Assert.That(inputBackground.GetComponent<MeshRenderer>().enabled, Is.False);
        Assert.That(inputBackground.GetComponent<BoxCollider>().enabled, Is.True);

        FieldMapThemePresenter presenter = prefab.GetComponent<FieldMapThemePresenter>();
        Assert.That(presenter, Is.Not.Null);
        Assert.That(presenter.ClassicRenderers.Length, Is.EqualTo(2));
        Assert.That(presenter.ArenaRenderers.Length, Is.EqualTo(1));
        Assert.That(presenter.ClassicRenderers, Does.Contain(inputBackground.GetComponent<MeshRenderer>()));
        Assert.That(presenter.ClassicRenderers, Does.Contain(activeLegacyGround.GetComponent<MeshRenderer>()));
        Assert.That(presenter.ArenaRenderers, Does.Contain(visualRenderer));
    }

    [Test]
    public void PlayerFieldsLeaveAVisibleGapBetweenArenaBackgrounds()
    {
        GameObject fieldPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        GameObject managersPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(GameManagersPrefabPath);
        Assert.That(fieldPrefab, Is.Not.Null);
        Assert.That(managersPrefab, Is.Not.Null);

        Transform arenaVisual = fieldPrefab.transform.Find("ArenaBackgroundVisual");
        GameManagers managers = managersPrefab.GetComponent<GameManagers>();
        Assert.That(arenaVisual, Is.Not.Null);
        Assert.That(managers, Is.Not.Null);

        Vector3 offset = managers.GetResolvedPlayerOffset();
        float arenaDepth = Mathf.Abs(arenaVisual.localScale.y);
        Assert.That(offset.y, Is.EqualTo(0f).Within(0.0001f));
        Assert.That(Mathf.Abs(offset.z), Is.GreaterThanOrEqualTo(arenaDepth + 4f));
        Assert.That(
            managers.GetPlayerFieldPosition(1),
            Is.EqualTo(managers.player1BasePosition + offset));
    }

    [Test]
    public void FieldPrefab_ThemeSwitchChangesOnlyCosmeticRenderers()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        GameObject instance = Object.Instantiate(prefab);
        try
        {
            FieldMapThemePresenter presenter = instance.GetComponent<FieldMapThemePresenter>();
            Transform inputBackground = instance.transform.Find("BackGround");
            BoxCollider inputCollider = inputBackground.GetComponent<BoxCollider>();
            int inputLayer = inputBackground.gameObject.layer;
            Vector3 inputPosition = inputBackground.localPosition;
            Vector3 inputScale = inputBackground.localScale;

            presenter.ApplyTheme((int)MapThemeId.Classic);
            Assert.That(presenter.ClassicRenderers.All(renderer => renderer.enabled), Is.True);
            Assert.That(presenter.ArenaRenderers.All(renderer => !renderer.enabled), Is.True);

            presenter.ApplyTheme((int)MapThemeId.Arena);
            Assert.That(presenter.ClassicRenderers.All(renderer => !renderer.enabled), Is.True);
            Assert.That(presenter.ArenaRenderers.All(renderer => renderer.enabled), Is.True);

            Assert.That(inputBackground.gameObject.activeSelf, Is.True);
            Assert.That(inputCollider.enabled, Is.True);
            Assert.That(inputBackground.gameObject.layer, Is.EqualTo(inputLayer));
            Assert.That(inputBackground.localPosition, Is.EqualTo(inputPosition));
            Assert.That(inputBackground.localScale, Is.EqualTo(inputScale));
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }
}
#endif
