#if UNITY_EDITOR
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

public sealed class AttackSlashTuningEditModeTests
{
    [Test]
    public void BasicAttackVfxConfigDefaultsRendererFlipToZero()
    {
        BasicAttackVfxConfig config = BasicAttackVfxConfig.CreateDefault("VFX_AttackSlash_SwordSlash5");

        Assert.That(config.prefabKey, Is.EqualTo("VFX_AttackSlash_SwordSlash5"));
        Assert.That(config.primaryRendererFlip, Is.EqualTo(Vector3.zero));
    }

    [Test]
    public void AttackSlashTuningPreviewCopiesSettingsToUnitData()
    {
        UnitData data = ScriptableObject.CreateInstance<UnitData>();
        data.basicAttackVfxPrefabsByStarLevel = new[] { "VFX_AttackSlash_SwordSlash5", "VFX_AttackSlash_SwordSlash5", "VFX_AttackSlash_SwordSlash5" };
        data.basicAttackVfxConfigsByStarLevel = new BasicAttackVfxConfig[3];

        GameObject root = new GameObject("PreviewRoot");
        GameObject originObject = new GameObject("SlashOrigin");
        originObject.transform.SetParent(root.transform, false);
        var preview = root.AddComponent<AttackSlashTuningPreview>();

        var serializedPreview = new SerializedObject(preview);
        serializedPreview.FindProperty("unitData").objectReferenceValue = data;
        serializedPreview.FindProperty("starLevel").intValue = 2;
        serializedPreview.FindProperty("applyToAllStarLevels").boolValue = false;
        serializedPreview.FindProperty("slashPrefabAddress").stringValue = "VFX_AttackSlash_SwordSlash5";
        serializedPreview.FindProperty("spawnOrigin").objectReferenceValue = originObject.transform;
        serializedPreview.FindProperty("localPositionOffset").vector3Value = new Vector3(0.1f, 0.2f, 0.3f);
        serializedPreview.FindProperty("rotationOffsetEuler").vector3Value = new Vector3(10f, 20f, 30f);
        serializedPreview.FindProperty("rotationMode").enumValueIndex = (int)BasicAttackVfxRotationMode.UnitForward;
        serializedPreview.FindProperty("scaleMultiplier").floatValue = 1.25f;
        serializedPreview.FindProperty("primaryRendererFlip").vector3Value = new Vector3(0f, 1f, 0f);
        serializedPreview.ApplyModifiedPropertiesWithoutUndo();

        preview.CopySettingsToUnitData(false);

        BasicAttackVfxConfig firstStar = data.basicAttackVfxConfigsByStarLevel[0];
        BasicAttackVfxConfig secondStar = data.basicAttackVfxConfigsByStarLevel[1];
        Assert.That(firstStar.calibrationSource, Is.Not.EqualTo("AttackSlashTuningScene"));
        Assert.That(secondStar.prefabKey, Is.EqualTo("VFX_AttackSlash_SwordSlash5"));
        Assert.That(secondStar.spawnOriginPath, Is.EqualTo("SlashOrigin"));
        Assert.That(secondStar.localPositionOffset, Is.EqualTo(new Vector3(0.1f, 0.2f, 0.3f)));
        Assert.That(secondStar.rotationOffsetEuler, Is.EqualTo(new Vector3(10f, 20f, 30f)));
        Assert.That(secondStar.rotationMode, Is.EqualTo(BasicAttackVfxRotationMode.UnitForward));
        Assert.That(secondStar.scaleMultiplier, Is.EqualTo(1.25f).Within(0.001f));
        Assert.That(secondStar.primaryRendererFlip, Is.EqualTo(new Vector3(0f, 1f, 0f)));
        Assert.That(secondStar.calibrationSource, Is.EqualTo("AttackSlashTuningScene"));

        Object.DestroyImmediate(root);
        Object.DestroyImmediate(data);
    }

    [Test]
    public void UnitAttackVfxPresenterUsesSharedRuntimeUtilityAndConfigFlip()
    {
        string presenterSource = File.ReadAllText("Assets/Scripts/VFX/UnitAttackVfxPresenter.cs");
        string utilitySource = File.ReadAllText("Assets/Scripts/VFX/BasicAttackVfxRuntimeUtility.cs");

        Assert.That(presenterSource, Does.Contain("config.primaryRendererFlip"));
        Assert.That(presenterSource, Does.Contain("BasicAttackVfxRuntimeUtility.RestartParticles(instance, primaryRendererFlip);"));
        Assert.That(utilitySource, Does.Contain("renderer.flip = primaryRendererFlip;"));
        Assert.That(utilitySource, Does.Contain("system.randomSeed = PrimarySlashRandomSeed;"));
    }

    [Test]
    public void AutomaticAttackSlashCalibratorWindowIsRemoved()
    {
        Assert.That(File.Exists("Assets/Scripts/Editor/AttackSlashCalibratorWindow.cs"), Is.False);
        Assert.That(File.Exists("Assets/Scripts/VFX/AttackSlashCalibrationUtility.cs"), Is.False);
        Assert.That(File.Exists("Assets/Scripts/Editor/AttackSlashVariantPrefabGenerator.cs"), Is.False);

        string installerSource = File.ReadAllText("Assets/Scripts/Editor/AttackSlashTuningSceneInstaller.cs");
        Assert.That(installerSource, Does.Contain("Tools/MDF/VFX/Rebuild Attack Slash Test Scene"));
        Assert.That(installerSource, Does.Contain("attack_slash_rebuild_test_scene"));
    }

    [Test]
    public void AttackSlashTuningPreviewSupportsLoopedAttackAndReusableVfx()
    {
        string previewSource = File.ReadAllText("Assets/Scripts/VFX/AttackSlashTuningPreview.cs");
        string editorSource = File.ReadAllText("Assets/Scripts/Editor/AttackSlashTuningPreviewEditor.cs");

        Assert.That(previewSource, Does.Contain("loopAttackAndVfx = true"));
        Assert.That(previewSource, Does.Contain("loopIntervalSeconds"));
        Assert.That(previewSource, Does.Contain("restartExistingPreviewInstance"));
        Assert.That(previewSource, Does.Contain("ReplayPreview(false);"));
        Assert.That(previewSource, Does.Contain("BasicAttackVfxRuntimeUtility.RestartParticles(_lastPreviewInstance, primaryRendererFlip);"));
        Assert.That(editorSource, Does.Contain("Replay VFX"));
        Assert.That(editorSource, Does.Not.Contain("Capture To Tuning Component"));
        Assert.That(editorSource, Does.Contain("Capture And Save To UnitData"));
    }

    [Test]
    public void RequestedSwordSlashVariantPrefabsExistAsAddressables()
    {
        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        Assert.That(settings, Is.Not.Null);

        int[] variants = { 1, 3, 9, 10, 11, 12, 14 };
        for (int i = 0; i < variants.Length; i++)
        {
            int variant = variants[i];
            string address = $"VFX_AttackSlash_SwordSlash{variant}";
            string path = $"Assets/Prefabs/VFX/Attack/{address}.prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

            Assert.That(prefab, Is.Not.Null, path);
            Assert.That(prefab.GetComponent<VFXAutoDestroy>(), Is.Not.Null, path);
            Assert.That(prefab.transform.childCount, Is.EqualTo(1), path);
            Assert.That(prefab.transform.GetChild(0).name, Is.EqualTo($"Sword Slash {variant}"), path);

            string guid = AssetDatabase.AssetPathToGUID(path);
            AddressableAssetEntry entry = settings.FindAssetEntry(guid);
            Assert.That(entry, Is.Not.Null, path);
            Assert.That(entry.address, Is.EqualTo(address), path);
        }
    }
}
#endif
