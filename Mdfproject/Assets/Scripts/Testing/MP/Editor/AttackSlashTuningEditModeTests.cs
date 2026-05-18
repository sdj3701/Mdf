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
        Assert.That(config.spawnNormalizedTime, Is.EqualTo(0.2f).Within(0.001f));
        Assert.That(config.playbackSpeed, Is.EqualTo(1f).Within(0.001f));
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
        serializedPreview.FindProperty("attackSpawnNormalizedTime").floatValue = 0.37f;
        serializedPreview.FindProperty("playbackSpeed").floatValue = 1.8f;
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
        Assert.That(secondStar.spawnNormalizedTime, Is.EqualTo(0.37f).Within(0.001f));
        Assert.That(secondStar.playbackSpeed, Is.EqualTo(1.8f).Within(0.001f));
        Assert.That(secondStar.primaryRendererFlip, Is.EqualTo(new Vector3(0f, 1f, 0f)));
        Assert.That(secondStar.calibrationSource, Is.EqualTo("AttackSlashTuningScene"));

        Object.DestroyImmediate(root);
        Object.DestroyImmediate(data);
    }

    [Test]
    public void AttackSlashTuningPreviewPullsTimingAndSpeedFromUnitData()
    {
        UnitData data = ScriptableObject.CreateInstance<UnitData>();
        data.basicAttackVfxPrefabsByStarLevel = new[] { "VFX_AttackSlash_SwordSlash5", "VFX_AttackSlash_SwordSlash5", "VFX_AttackSlash_SwordSlash5" };
        data.basicAttackVfxConfigsByStarLevel = new[]
        {
            new BasicAttackVfxConfig
            {
                prefabKey = "VFX_AttackSlash_SwordSlash5",
                localPositionOffset = new Vector3(1.25f, 2.5f, 3.75f),
                rotationOffsetEuler = new Vector3(11f, 22f, 33f),
                rotationMode = BasicAttackVfxRotationMode.UnitForward,
                scaleMultiplier = 1.5f,
                spawnNormalizedTime = 0.44f,
                playbackSpeed = 0.75f
            },
            null,
            null
        };

        GameObject slashPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/VFX/Attack/VFX_AttackSlash_SwordSlash5.prefab");
        Assert.That(slashPrefab, Is.Not.Null);

        GameObject root = new GameObject("PreviewRoot");
        root.SetActive(false);
        var preview = root.AddComponent<AttackSlashTuningPreview>();

        var serializedPreview = new SerializedObject(preview);
        serializedPreview.FindProperty("unitData").objectReferenceValue = data;
        serializedPreview.FindProperty("starLevel").intValue = 1;
        serializedPreview.FindProperty("slashPrefab").objectReferenceValue = slashPrefab;
        serializedPreview.FindProperty("localPositionOffset").vector3Value = new Vector3(-9f, -9f, -9f);
        serializedPreview.ApplyModifiedPropertiesWithoutUndo();
        preview.PullFromUnitData();

        var refreshedPreview = new SerializedObject(preview);
        Assert.That(refreshedPreview.FindProperty("slashPrefab").objectReferenceValue, Is.Not.Null);
        Assert.That(refreshedPreview.FindProperty("localPositionOffset").vector3Value, Is.EqualTo(new Vector3(1.25f, 2.5f, 3.75f)));
        Assert.That(refreshedPreview.FindProperty("rotationOffsetEuler").vector3Value, Is.EqualTo(new Vector3(11f, 22f, 33f)));
        Assert.That(refreshedPreview.FindProperty("scaleMultiplier").floatValue, Is.EqualTo(1.5f).Within(0.001f));
        Assert.That(refreshedPreview.FindProperty("attackSpawnNormalizedTime").floatValue, Is.EqualTo(0.44f).Within(0.001f));
        Assert.That(refreshedPreview.FindProperty("playbackSpeed").floatValue, Is.EqualTo(0.75f).Within(0.001f));

        Object.DestroyImmediate(root);
        Object.DestroyImmediate(data);
    }

    [Test]
    public void UnitAttackVfxPresenterUsesSharedRuntimeUtilityAndConfigFlip()
    {
        string presenterSource = File.ReadAllText("Assets/Scripts/VFX/UnitAttackVfxPresenter.cs");
        string utilitySource = File.ReadAllText("Assets/Scripts/VFX/BasicAttackVfxRuntimeUtility.cs");
        string unitSource = File.ReadAllText("Assets/Scripts/Game/Units/Unit.cs");

        Assert.That(presenterSource, Does.Contain("config.primaryRendererFlip"));
        Assert.That(presenterSource, Does.Contain("config.playbackSpeed"));
        Assert.That(presenterSource, Does.Contain("unit.GetCappedAttackAnimationPlaybackSpeed()"));
        Assert.That(presenterSource, Does.Contain("configuredPlaybackSpeed * animationPlaybackSpeed"));
        Assert.That(presenterSource, Does.Contain("BasicAttackVfxRuntimeUtility.RestartParticles(instance, primaryRendererFlip, playbackSpeed);"));
        Assert.That(unitSource, Does.Contain("public float GetCappedAttackAnimationPlaybackSpeed()"));
        Assert.That(unitSource, Does.Contain("Mathf.Min(currentAttackSpeed, maxAttackAnimationsPerSecond)"));
        Assert.That(unitSource, Does.Contain("CalculateAttackAnimationPlaybackSpeed(animRate)"));
        Assert.That(utilitySource, Does.Contain("renderer.flip = primaryRendererFlip;"));
        Assert.That(utilitySource, Does.Contain("main.simulationSpeed = resolvedPlaybackSpeed;"));
        Assert.That(utilitySource, Does.Contain("system.randomSeed = PrimarySlashRandomSeed;"));
    }

    [Test]
    public void OneOffAttackSlashGenerationToolsAreRemoved()
    {
        Assert.That(File.Exists("Assets/Scripts/Editor/AttackSlashCalibratorWindow.cs"), Is.False);
        Assert.That(File.Exists("Assets/Scripts/VFX/AttackSlashCalibrationUtility.cs"), Is.False);
        Assert.That(File.Exists("Assets/Scripts/Editor/AttackSlashVariantPrefabGenerator.cs"), Is.False);
        Assert.That(File.Exists("Assets/Scripts/Editor/AttackSlashTuningSceneInstaller.cs"), Is.False);
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
        Assert.That(previewSource, Does.Contain("attackSpawnNormalizedTime = Mathf.Clamp(config.spawnNormalizedTime"));
        Assert.That(previewSource, Does.Contain("playbackSpeed = config.playbackSpeed"));
        Assert.That(previewSource, Does.Contain("BasicAttackVfxRuntimeUtility.RestartParticles(_lastPreviewInstance, primaryRendererFlip, playbackSpeed);"));
        Assert.That(editorSource, Does.Contain("Replay VFX"));
        Assert.That(editorSource, Does.Contain("Effect Tuning"));
        Assert.That(editorSource, Does.Contain("Advanced"));
        Assert.That(editorSource, Does.Not.Contain("DrawDefaultInspector"));
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
