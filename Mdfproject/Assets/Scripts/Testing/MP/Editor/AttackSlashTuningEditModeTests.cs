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
        Assert.That(config.ResolvePlaybackSpeedCap(), Is.EqualTo(2f).Within(0.001f));
        Assert.That(config.ResolveMinimumVisibleSeconds(), Is.EqualTo(0.16f).Within(0.001f));
        Assert.That(BasicAttackVfxRuntimeUtility.ResolvePlaybackSpeed(1f, 3f, config.ResolvePlaybackSpeedCap()), Is.EqualTo(2f).Within(0.001f));
        Assert.That(BasicAttackVfxRuntimeUtility.ResolveLifetimeSeconds(0.1f, 2f, config.ResolveMinimumVisibleSeconds()), Is.EqualTo(0.16f).Within(0.001f));
    }

    [Test]
    public void AttackSlashTuningPreviewCopiesSettingsToUnitData()
    {
        UnitData data = ScriptableObject.CreateInstance<UnitData>();
        data.basicAttackVfxConfigsByStarLevel = new BasicAttackVfxConfig[3];

        GameObject root = new GameObject("PreviewRoot");
        var preview = root.AddComponent<AttackSlashTuningPreview>();

        var serializedPreview = new SerializedObject(preview);
        serializedPreview.FindProperty("unitData").objectReferenceValue = data;
        serializedPreview.FindProperty("starLevel").intValue = 2;
        serializedPreview.FindProperty("applyToAllStarLevels").boolValue = false;
        serializedPreview.FindProperty("slashPrefabAddress").stringValue = "VFX_AttackSlash_SwordSlash5";
        serializedPreview.FindProperty("localPositionOffset").vector3Value = new Vector3(0.1f, 0.2f, 0.3f);
        serializedPreview.FindProperty("rotationOffsetEuler").vector3Value = new Vector3(10f, 20f, 30f);
        serializedPreview.FindProperty("rotationMode").enumValueIndex = (int)BasicAttackVfxRotationMode.UnitForward;
        serializedPreview.FindProperty("scaleMultiplier").floatValue = 1.25f;
        serializedPreview.FindProperty("attackSpawnNormalizedTime").floatValue = 0.37f;
        serializedPreview.FindProperty("playbackSpeed").floatValue = 1.8f;
        serializedPreview.FindProperty("vfxPlaybackSpeedCap").floatValue = 1.6f;
        serializedPreview.FindProperty("minimumVisibleSeconds").floatValue = 0.22f;
        serializedPreview.FindProperty("primaryRendererFlip").vector3Value = new Vector3(0f, 1f, 0f);
        serializedPreview.ApplyModifiedPropertiesWithoutUndo();

        preview.CopySettingsToUnitData(false);

        BasicAttackVfxConfig firstStar = data.basicAttackVfxConfigsByStarLevel[0];
        BasicAttackVfxConfig secondStar = data.basicAttackVfxConfigsByStarLevel[1];
        Assert.That(firstStar.calibrationSource, Is.Not.EqualTo("AttackSlashTuningScene"));
        Assert.That(secondStar.prefabKey, Is.EqualTo("VFX_AttackSlash_SwordSlash5"));
        Assert.That(secondStar.localPositionOffset, Is.EqualTo(new Vector3(0.1f, 0.2f, 0.3f)));
        Assert.That(secondStar.rotationOffsetEuler, Is.EqualTo(new Vector3(10f, 20f, 30f)));
        Assert.That(secondStar.rotationMode, Is.EqualTo(BasicAttackVfxRotationMode.UnitForward));
        Assert.That(secondStar.scaleMultiplier, Is.EqualTo(1.25f).Within(0.001f));
        Assert.That(secondStar.spawnNormalizedTime, Is.EqualTo(0.37f).Within(0.001f));
        Assert.That(secondStar.playbackSpeed, Is.EqualTo(1.8f).Within(0.001f));
        Assert.That(secondStar.playbackSpeedCap, Is.EqualTo(1.6f).Within(0.001f));
        Assert.That(secondStar.minimumVisibleSeconds, Is.EqualTo(0.22f).Within(0.001f));
        Assert.That(secondStar.primaryRendererFlip, Is.EqualTo(new Vector3(0f, 1f, 0f)));
        Assert.That(secondStar.calibrationSource, Is.EqualTo("AttackSlashTuningScene"));
        Assert.That(preview.TryResolvePreviewWorldPose(out Transform origin, out _, out _, out _), Is.True);
        Assert.That(origin, Is.EqualTo(root.transform));

        Object.DestroyImmediate(root);
        Object.DestroyImmediate(data);
    }

    [Test]
    public void AttackSlashTuningPreviewPullsTimingAndSpeedFromUnitData()
    {
        UnitData data = ScriptableObject.CreateInstance<UnitData>();
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
                playbackSpeed = 0.75f,
                playbackSpeedCap = 1.7f,
                minimumVisibleSeconds = 0.21f
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
        Assert.That(refreshedPreview.FindProperty("vfxPlaybackSpeedCap").floatValue, Is.EqualTo(1.7f).Within(0.001f));
        Assert.That(refreshedPreview.FindProperty("minimumVisibleSeconds").floatValue, Is.EqualTo(0.21f).Within(0.001f));

        Object.DestroyImmediate(root);
        Object.DestroyImmediate(data);
    }

    [Test]
    public void UnitDataBasicAttackVfxUsesConfigOnly()
    {
        UnitData data = ScriptableObject.CreateInstance<UnitData>();
        data.basicAttackVfxConfigsByStarLevel = new[]
        {
            BasicAttackVfxConfig.CreateDefault("VFX_AttackSlash_SwordSlash1"),
            null,
            new BasicAttackVfxConfig()
        };

        Assert.That(data.GetBasicAttackVfxConfig(1).prefabKey, Is.EqualTo("VFX_AttackSlash_SwordSlash1"));
        Assert.That(data.GetBasicAttackVfxConfig(2), Is.Null);
        Assert.That(data.GetBasicAttackVfxConfig(3), Is.Null);

        string unitDataSource = File.ReadAllText("Assets/Scripts/Game/Units/UnitData.cs");
        string importerSource = File.ReadAllText("Assets/Scripts/Editor/GoogleSheetDataImporter.cs");
        Assert.That(unitDataSource, Does.Not.Contain("basicAttackVfxPrefabsByStarLevel"));
        Assert.That(unitDataSource, Does.Not.Contain("GetBasicAttackVfxKey"));
        Assert.That(importerSource, Does.Not.Contain("basicAttackVfxPrefabsByStarLevel"));

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
        Assert.That(presenterSource, Does.Contain("config.ResolvePlaybackSpeedCap()"));
        Assert.That(presenterSource, Does.Contain("config.ResolveMinimumVisibleSeconds()"));
        Assert.That(presenterSource, Does.Contain("BasicAttackVfxRuntimeUtility.ResolvePlaybackSpeed(configuredPlaybackSpeed, animationPlaybackSpeed, playbackSpeedCap)"));
        Assert.That(presenterSource, Does.Contain("BasicAttackVfxRuntimeUtility.ResolveLifetimeSeconds(lifetimeSeconds, playbackSpeed, minimumVisibleSeconds)"));
        Assert.That(presenterSource, Does.Contain("BasicAttackVfxRuntimeUtility.RestartParticles(instance, primaryRendererFlip, playbackSpeed);"));
        Assert.That(presenterSource, Does.Not.Contain("spawnOriginPath"));
        Assert.That(presenterSource, Does.Not.Contain("private Transform spawnOrigin"));
        Assert.That(unitSource, Does.Contain("public float GetCappedAttackAnimationPlaybackSpeed()"));
        Assert.That(unitSource, Does.Contain("Mathf.Min(currentAttackSpeed, maxAttackAnimationsPerSecond)"));
        Assert.That(unitSource, Does.Contain("CalculateAttackAnimationPlaybackSpeed(animRate)"));
        Assert.That(unitSource, Does.Contain("ScheduleBasicAttackVfxForCurrentAnimation(targetEnemy, attackPresentationId);"));
        Assert.That(unitSource, Does.Contain("config.spawnNormalizedTime"));
        Assert.That(unitSource, Does.Contain("return normalizedTime / animRate;"));
        Assert.That(unitSource, Does.Not.Contain("TryPlayBasicAttackVfxForAttack(_pendingAttack.TargetEnemy"));
        Assert.That(utilitySource, Does.Contain("renderer.flip = primaryRendererFlip;"));
        Assert.That(utilitySource, Does.Contain("main.simulationSpeed = resolvedPlaybackSpeed;"));
        Assert.That(utilitySource, Does.Contain("StopAndClearForReplay(particles[i]);"));
        Assert.That(utilitySource, Does.Contain("ParticleSystemStopBehavior.StopEmittingAndClear"));
        Assert.That(utilitySource, Does.Contain("system.randomSeed = PrimarySlashRandomSeed;"));
    }

    [Test]
    public void CalibratedMeleeSlashUnitDataUsesUnitForwardRotation()
    {
        string[] calibratedMeleeUnitDataPaths =
        {
            "Assets/GameData/Units/UnitData_Warrior.asset",
            "Assets/GameData/Units/UnitData_Guardian.asset",
            "Assets/GameData/Units/UnitData_Assassin.asset"
        };

        for (int pathIndex = 0; pathIndex < calibratedMeleeUnitDataPaths.Length; pathIndex++)
        {
            string path = calibratedMeleeUnitDataPaths[pathIndex];
            UnitData data = AssetDatabase.LoadAssetAtPath<UnitData>(path);
            Assert.That(data, Is.Not.Null, path);
            Assert.That(data.unitType, Is.EqualTo(UnitType.Melee), path);

            data.EnsureBasicAttackVfxConfigArray();
            for (int starIndex = 0; starIndex < data.basicAttackVfxConfigsByStarLevel.Length; starIndex++)
            {
                BasicAttackVfxConfig config = data.basicAttackVfxConfigsByStarLevel[starIndex];
                Assert.That(config, Is.Not.Null, $"{path} star={starIndex + 1}");
                Assert.That(config.rotationMode, Is.EqualTo(BasicAttackVfxRotationMode.UnitForward), $"{path} star={starIndex + 1}");
            }
        }
    }

    [Test]
    public void AttackSlashTuningPreviewSimulatesFinalAttackSpeedWithAnimationCap()
    {
        GameObject root = new GameObject("PreviewRoot");
        var preview = root.AddComponent<AttackSlashTuningPreview>();

        var serializedPreview = new SerializedObject(preview);
        serializedPreview.FindProperty("previewFinalAttackSpeed").floatValue = 8f;
        serializedPreview.FindProperty("previewAnimationSpeedCap").floatValue = 3f;
        serializedPreview.FindProperty("fallbackAttackClipDuration").floatValue = 1f;
        serializedPreview.FindProperty("playbackSpeed").floatValue = 1f;
        serializedPreview.FindProperty("vfxPlaybackSpeedCap").floatValue = 2f;
        serializedPreview.FindProperty("minimumVisibleSeconds").floatValue = 0.16f;
        serializedPreview.FindProperty("useFinalAttackSpeedForLoopInterval").boolValue = true;
        serializedPreview.ApplyModifiedPropertiesWithoutUndo();

        Assert.That(preview.ResolvePreviewDamageIntervalSeconds(), Is.EqualTo(0.125f).Within(0.001f));
        Assert.That(preview.ResolvePreviewAttackIntervalSeconds(), Is.EqualTo(1f / 3f).Within(0.001f));
        Assert.That(preview.ResolvePreviewPresentationIntervalSeconds(), Is.EqualTo(1f / 3f).Within(0.001f));
        Assert.That(preview.ResolvePreviewAnimationPlaybackSpeed(), Is.EqualTo(3f).Within(0.001f));
        Assert.That(preview.ResolvePreviewVfxPlaybackSpeed(), Is.EqualTo(2f).Within(0.001f));
        Assert.That(preview.ResolvePreviewVfxLifetimeSeconds(), Is.EqualTo(1.05f).Within(0.001f));

        serializedPreview.Update();
        serializedPreview.FindProperty("useFinalAttackSpeedForLoopInterval").boolValue = false;
        serializedPreview.FindProperty("loopIntervalSeconds").floatValue = 0.75f;
        serializedPreview.ApplyModifiedPropertiesWithoutUndo();

        Assert.That(preview.ResolvePreviewAttackIntervalSeconds(), Is.EqualTo(0.75f).Within(0.001f));

        Object.DestroyImmediate(root);
    }

    [Test]
    public void OneOffAttackSlashGenerationToolsAreRemoved()
    {
        Assert.That(File.Exists("Assets/Scripts/Editor/AttackSlashCalibratorWindow.cs"), Is.False);
        Assert.That(File.Exists("Assets/Scripts/VFX/AttackSlashCalibrationUtility.cs"), Is.False);
        Assert.That(File.Exists("Assets/Scripts/Editor/AttackSlashVariantPrefabGenerator.cs"), Is.False);
        Assert.That(File.Exists("Assets/Scripts/Editor/AttackSlashTuningSceneInstaller.cs"), Is.False);
        Assert.That(File.Exists("Assets/Scripts/VFX/AttackSlashTuningPreviewInstance.cs"), Is.False);
    }

    [Test]
    public void AttackSlashTuningPreviewSupportsLoopedAttackAndReusableVfx()
    {
        string previewSource = File.ReadAllText("Assets/Scripts/VFX/AttackSlashTuningPreview.cs");
        string editorSource = File.ReadAllText("Assets/Scripts/Editor/AttackSlashTuningPreviewEditor.cs");

        Assert.That(previewSource, Does.Contain("loopAttackAndVfx = true"));
        Assert.That(previewSource, Does.Contain("loopIntervalSeconds"));
        Assert.That(previewSource, Does.Contain("ReplayPreview(false);"));
        Assert.That(previewSource, Does.Contain("TryResolvePreviewWorldPose"));
        Assert.That(previewSource, Does.Contain("PreviewSample"));
        Assert.That(previewSource, Does.Contain("autoDestroyPreviewInstances && Application.isPlaying"));
        Assert.That(previewSource, Does.Contain("attackSpawnNormalizedTime = Mathf.Clamp(config.spawnNormalizedTime"));
        Assert.That(previewSource, Does.Contain("playbackSpeed = config.playbackSpeed"));
        Assert.That(previewSource, Does.Contain("vfxPlaybackSpeedCap = config.ResolvePlaybackSpeedCap()"));
        Assert.That(previewSource, Does.Contain("minimumVisibleSeconds = config.ResolveMinimumVisibleSeconds()"));
        Assert.That(previewSource, Does.Contain("ResolvePreviewVfxPlaybackSpeed()"));
        Assert.That(previewSource, Does.Contain("ResolvePreviewVfxLifetimeSeconds()"));
        Assert.That(previewSource, Does.Not.Contain("spawnOriginPath"));
        Assert.That(previewSource, Does.Not.Contain("AttackSlashTuningPreviewInstance"));
        Assert.That(previewSource, Does.Not.Contain("LastPreviewInstance"));
        Assert.That(previewSource, Does.Not.Contain("EditablePreview"));
        Assert.That(previewSource, Does.Not.Contain("\"LoopPreview\""));
        Assert.That(previewSource, Does.Not.Contain("SuppressEditablePreviewAutoDestroy"));
        Assert.That(previewSource, Does.Not.Contain("ConfigurePreviewPicking"));
        Assert.That(previewSource, Does.Not.Contain("SceneVisibilityManager"));
        Assert.That(previewSource, Does.Not.Contain("ApplyPreviewWorldPose"));
        Assert.That(editorSource, Does.Contain("Effect Tuning"));
        Assert.That(editorSource, Does.Contain("Position Offset"));
        Assert.That(editorSource, Does.Contain("Rotation Offset"));
        Assert.That(editorSource, Does.Contain("VFX Speed Cap"));
        Assert.That(editorSource, Does.Contain("Min Visible Seconds"));
        Assert.That(editorSource, Does.Contain("Final Attack Speed"));
        Assert.That(editorSource, Does.Contain("Save To UnitData"));
        Assert.That(editorSource, Does.Contain("Preview Result"));
        Assert.That(editorSource, Does.Contain("Advanced"));
        Assert.That(editorSource, Does.Contain("Animation Speed Cap"));
        Assert.That(editorSource, Does.Not.Contain("Spawn Origin"));
        Assert.That(editorSource, Does.Not.Contain("Pull From UnitData"));
        Assert.That(editorSource, Does.Not.Contain("Preview VFX"));
        Assert.That(editorSource, Does.Not.Contain("Trigger Attack"));
        Assert.That(editorSource, Does.Not.Contain("Replay VFX"));
        Assert.That(editorSource, Does.Not.Contain("DrawDefaultInspector"));
        Assert.That(editorSource, Does.Not.Contain("Capture To Tuning Component"));
        Assert.That(editorSource, Does.Not.Contain("Capture And Save To UnitData"));
        Assert.That(editorSource, Does.Not.Contain("Spawn Editable Preview"));
        Assert.That(editorSource, Does.Not.Contain("AttackSlashTuningPreviewInstance"));
        Assert.That(editorSource, Does.Not.Contain("OnSceneGUI"));
        Assert.That(editorSource, Does.Not.Contain("Handles.PositionHandle"));
        Assert.That(editorSource, Does.Not.Contain("Handles.RotationHandle"));
        Assert.That(editorSource, Does.Not.Contain("Slash Spawn"));
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
