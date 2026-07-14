#if UNITY_EDITOR
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class ArenaBackgroundPresentationEditModeTests
{
    private const string PrefabPath = "Assets/Prefabs/User_Grid3D.prefab";
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
    }
}
#endif
