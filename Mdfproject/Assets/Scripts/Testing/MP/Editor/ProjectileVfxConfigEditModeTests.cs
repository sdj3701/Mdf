#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

public sealed class ProjectileVfxConfigEditModeTests
{
    [Test]
    public void ProjectileVfxConfigDefaultsToSingleProjectileKey()
    {
        ProjectileVfxConfig config = ProjectileVfxConfig.CreateDefault("ArrowProjectile");

        Assert.That(config.projectileKey, Is.EqualTo("ArrowProjectile"));
        Assert.That(config.HasProjectileKey, Is.True);
        Assert.That(config.HasMuzzleFlashKey, Is.False);
        Assert.That(config.HasImpactFlashKey, Is.False);
        Assert.That(config.ResolveMuzzleScaleMultiplier(), Is.EqualTo(1f).Within(0.001f));
        Assert.That(config.ResolveMuzzleScaleMultiplierVector(), Is.EqualTo(Vector3.one));
        Assert.That(config.ResolveProjectileScaleMultiplier(), Is.EqualTo(1f).Within(0.001f));
        Assert.That(config.ResolveProjectileScaleMultiplierVector(), Is.EqualTo(Vector3.one));
        Assert.That(config.ResolveProjectileSpawnNormalizedTime(), Is.EqualTo(ProjectileVfxConfig.DefaultProjectileSpawnNormalizedTime).Within(0.001f));
        Assert.That(config.ResolveProjectileVisualHeightOffset(), Is.EqualTo(ProjectileVfxConfig.DefaultProjectileVisualHeightOffset).Within(0.001f));
        Assert.That(config.ResolveProjectileDynamicLightIntensity(), Is.EqualTo(ProjectileVfxConfig.DefaultProjectileDynamicLightIntensity).Within(0.001f));
        Assert.That(config.ResolveProjectileDynamicLightRange(), Is.EqualTo(ProjectileVfxConfig.DefaultProjectileDynamicLightRange).Within(0.001f));
        Assert.That(config.ResolveImpactScaleMultiplier(), Is.EqualTo(1f).Within(0.001f));
        Assert.That(config.ResolveImpactScaleMultiplierVector(), Is.EqualTo(Vector3.one));
        Assert.That(config.ResolveProjectileSpeed(), Is.EqualTo(ProjectileVfxConfig.DefaultProjectileSpeed).Within(0.001f));
        Assert.That(config.ResolveMuzzlePlaybackSpeed(), Is.EqualTo(1f).Within(0.001f));
        Assert.That(config.ResolveProjectilePlaybackSpeed(), Is.EqualTo(1f).Within(0.001f));
        Assert.That(config.ResolveImpactPlaybackSpeed(), Is.EqualTo(1f).Within(0.001f));
        Assert.That(config.ResolveMuzzleLifetimeSeconds(), Is.EqualTo(1f).Within(0.001f));
        Assert.That(config.ResolveImpactLifetimeSeconds(), Is.EqualTo(1f).Within(0.001f));
    }

    [Test]
    public void UnitDataProjectileLookupUsesSingleConfig()
    {
        UnitData data = ScriptableObject.CreateInstance<UnitData>();
        BasicAttackVfxProfile profile = ScriptableObject.CreateInstance<BasicAttackVfxProfile>();
        profile.projectileVfxConfig = ProjectileVfxConfig.CreateDefault("BaseProjectile");
        data.basicAttackVfxProfile = profile;

        Assert.That(data.GetProjectilePrefabKey(), Is.EqualTo("BaseProjectile"));
        Assert.That(data.GetProjectileVfxConfig(), Is.SameAs(profile.projectileVfxConfig));
        Assert.That(data.ResolveProjectileSpeed(), Is.EqualTo(ProjectileVfxConfig.DefaultProjectileSpeed).Within(0.001f));

        Object.DestroyImmediate(profile);
        Object.DestroyImmediate(data);
    }

    [Test]
    public void ProjectileVfxConfigKeepsLegacyUniformScaleCompatibility()
    {
        ProjectileVfxConfig config = ProjectileVfxConfig.CreateDefault("BaseProjectile");
        config.projectileScaleMultiplier = 0.5f;

        Assert.That(config.ResolveProjectileScaleMultiplierVector(), Is.EqualTo(Vector3.one * 0.5f));

        config.projectileScaleMultiplierVector = new Vector3(1f, 1.25f, 1.5f);

        Assert.That(config.ResolveProjectileScaleMultiplierVector(), Is.EqualTo(new Vector3(1f, 1.25f, 1.5f)));
    }

    [Test]
    public void RangedUnitDataAssetsHaveProjectileVfxConfigKeys()
    {
        var expected = new Dictionary<string, (string Muzzle, string Projectile, string Impact)>
        {
            { "Assets/GameData/Units/UnitData_Archer.asset", (string.Empty, "ArrowProjectile", string.Empty) },
            { "Assets/GameData/Units/UnitData_Cleric.asset", ("VFX_Projectile_Wind7_Flash", "VFX_Projectile_Wind7_Projectile", "VFX_Projectile_Wind7_Hit") },
            { "Assets/GameData/Units/UnitData_Mage.asset", ("VFX_Projectile_Ice5_Flash", "VFX_Projectile_Ice5_Projectile", "VFX_Projectile_Ice5_Hit") },
            { "Assets/GameData/Units/UnitData_Pyromancer.asset", ("VFX_Projectile_Fire4_Flash", "VFX_Projectile_Fire4_Projectile", "VFX_Projectile_Fire4_Hit") }
        };

        foreach (var pair in expected)
        {
            UnitData data = AssetDatabase.LoadAssetAtPath<UnitData>(pair.Key);
            Assert.That(data, Is.Not.Null, pair.Key);
            Assert.That(data.basicAttackVfxProfile, Is.Not.Null, pair.Key);
            ProjectileVfxConfig config = data.GetProjectileVfxConfig();
            Assert.That(config, Is.Not.Null, pair.Key);
            Assert.That(config.muzzleFlashKey, Is.EqualTo(pair.Value.Muzzle), pair.Key);
            Assert.That(data.GetProjectilePrefabKey(), Is.EqualTo(pair.Value.Projectile), pair.Key);
            Assert.That(config.impactFlashKey, Is.EqualTo(pair.Value.Impact), pair.Key);
            Assert.That(config.ResolveProjectileSpeed(), Is.EqualTo(10f).Within(0.001f), pair.Key);
            if (pair.Key.Contains("Cleric"))
            {
                Assert.That(config.ResolveProjectileDynamicLightIntensity(), Is.GreaterThan(0f), pair.Key);
                Assert.That(config.ResolveProjectileDynamicLightRange(), Is.GreaterThan(0f), pair.Key);
            }
        }
    }

    [Test]
    public void TestSceneContainsProjectileVfxTuningRows()
    {
        string scene = File.ReadAllText("Assets/Scenes/test.unity");

        Assert.That(scene, Does.Contain("ProjectileVfxTuning_Root"));
        Assert.That(scene, Does.Contain("ProjectileVfxEffectRoot"));
        Assert.That(scene, Does.Contain("ProjectileTuning_Archer"));
        Assert.That(scene, Does.Contain("ProjectileTuning_Mage"));
        Assert.That(scene, Does.Contain("ProjectileTuning_Cleric"));
        Assert.That(scene, Does.Contain("ProjectileTuning_Pyromancer"));
        Assert.That(scene, Does.Contain("ProjectileTarget_Archer_Slime"));
        Assert.That(scene, Does.Contain("ProjectileTarget_Mage_Slime"));
        Assert.That(scene, Does.Contain("ProjectileTarget_Cleric_Slime"));
        Assert.That(scene, Does.Contain("ProjectileTarget_Pyromancer_Slime"));
        Assert.That(scene, Does.Contain("projectileAddress: ArrowProjectile"));
        Assert.That(scene, Does.Contain("projectileAddress: BaseProjectile"));
        Assert.That(scene, Does.Contain("projectileSpeed: 10"));
    }

    [Test]
    public void ProjectileVfxTuningPreviewCopiesSettingsToUnitData()
    {
        UnitData data = ScriptableObject.CreateInstance<UnitData>();
        BasicAttackVfxProfile profile = ScriptableObject.CreateInstance<BasicAttackVfxProfile>();
        profile.EnsureConfigs();
        data.basicAttackVfxProfile = profile;
        GameObject root = new GameObject("ProjectilePreviewRoot");
        var preview = root.AddComponent<ProjectileVfxTuningPreview>();

        var serializedPreview = new SerializedObject(preview);
        serializedPreview.FindProperty("unitData").objectReferenceValue = data;
        serializedPreview.FindProperty("muzzleFlashAddress").stringValue = "MuzzleKey";
        serializedPreview.FindProperty("projectileAddress").stringValue = "ProjectileKey";
        serializedPreview.FindProperty("impactFlashAddress").stringValue = "ImpactKey";
        serializedPreview.FindProperty("muzzleLocalPositionOffset").vector3Value = new Vector3(0.1f, 0.2f, 0.3f);
        serializedPreview.FindProperty("projectileLocalPositionOffset").vector3Value = new Vector3(0.4f, 0.5f, 0.6f);
        serializedPreview.FindProperty("impactLocalPositionOffset").vector3Value = new Vector3(0.7f, 0.8f, 0.9f);
        serializedPreview.FindProperty("muzzleRotationOffsetEuler").vector3Value = new Vector3(1f, 2f, 3f);
        serializedPreview.FindProperty("projectileRotationOffsetEuler").vector3Value = new Vector3(4f, 5f, 6f);
        serializedPreview.FindProperty("impactRotationOffsetEuler").vector3Value = new Vector3(7f, 8f, 9f);
        serializedPreview.FindProperty("muzzleScaleMultiplierVector").vector3Value = new Vector3(1.1f, 1.2f, 1.3f);
        serializedPreview.FindProperty("projectileScaleMultiplierVector").vector3Value = new Vector3(1.4f, 1.5f, 1.6f);
        serializedPreview.FindProperty("impactScaleMultiplierVector").vector3Value = new Vector3(1.7f, 1.8f, 1.9f);
        serializedPreview.FindProperty("muzzlePlaybackSpeed").floatValue = 0.9f;
        serializedPreview.FindProperty("projectilePlaybackSpeed").floatValue = 1.4f;
        serializedPreview.FindProperty("impactPlaybackSpeed").floatValue = 1.5f;
        serializedPreview.FindProperty("attackSpawnNormalizedTime").floatValue = 0.42f;
        serializedPreview.FindProperty("projectileVisualHeightOffset").floatValue = 0.75f;
        serializedPreview.FindProperty("projectileDynamicLightIntensity").floatValue = 0.2f;
        serializedPreview.FindProperty("projectileDynamicLightRange").floatValue = 0.8f;
        serializedPreview.FindProperty("projectileSpeed").floatValue = 22f;
        serializedPreview.FindProperty("muzzleLifetimeSeconds").floatValue = 0.6f;
        serializedPreview.FindProperty("impactLifetimeSeconds").floatValue = 0.7f;
        serializedPreview.FindProperty("alignProjectileToDirection").boolValue = false;
        serializedPreview.FindProperty("alignImpactToDirection").boolValue = false;
        serializedPreview.ApplyModifiedPropertiesWithoutUndo();

        preview.CopySettingsToUnitData(false);

        ProjectileVfxConfig config = data.GetProjectileVfxConfig();
        Assert.That(config.muzzleFlashKey, Is.EqualTo("MuzzleKey"));
        Assert.That(config.projectileKey, Is.EqualTo("ProjectileKey"));
        Assert.That(config.impactFlashKey, Is.EqualTo("ImpactKey"));
        Assert.That(config.muzzleLocalPositionOffset, Is.EqualTo(new Vector3(0.1f, 0.2f, 0.3f)));
        Assert.That(config.projectileLocalPositionOffset, Is.EqualTo(new Vector3(0.4f, 0.5f, 0.6f)));
        Assert.That(config.impactLocalPositionOffset, Is.EqualTo(new Vector3(0.7f, 0.8f, 0.9f)));
        Assert.That(config.muzzleRotationOffsetEuler, Is.EqualTo(new Vector3(1f, 2f, 3f)));
        Assert.That(config.projectileRotationOffsetEuler, Is.EqualTo(new Vector3(4f, 5f, 6f)));
        Assert.That(config.impactRotationOffsetEuler, Is.EqualTo(new Vector3(7f, 8f, 9f)));
        Assert.That(config.ResolveMuzzleScaleMultiplierVector(), Is.EqualTo(new Vector3(1.1f, 1.2f, 1.3f)));
        Assert.That(config.ResolveProjectileScaleMultiplierVector(), Is.EqualTo(new Vector3(1.4f, 1.5f, 1.6f)));
        Assert.That(config.ResolveImpactScaleMultiplierVector(), Is.EqualTo(new Vector3(1.7f, 1.8f, 1.9f)));
        Assert.That(config.muzzlePlaybackSpeed, Is.EqualTo(0.9f).Within(0.001f));
        Assert.That(config.projectilePlaybackSpeed, Is.EqualTo(1.4f).Within(0.001f));
        Assert.That(config.impactPlaybackSpeed, Is.EqualTo(1.5f).Within(0.001f));
        Assert.That(config.projectileSpawnNormalizedTime, Is.EqualTo(0.42f).Within(0.001f));
        Assert.That(config.projectileVisualHeightOffset, Is.EqualTo(0.75f).Within(0.001f));
        Assert.That(config.projectileDynamicLightIntensity, Is.EqualTo(0.2f).Within(0.001f));
        Assert.That(config.projectileDynamicLightRange, Is.EqualTo(0.8f).Within(0.001f));
        Assert.That(config.projectileSpeed, Is.EqualTo(22f).Within(0.001f));
        Assert.That(config.muzzleLifetimeSeconds, Is.EqualTo(0.6f).Within(0.001f));
        Assert.That(config.impactLifetimeSeconds, Is.EqualTo(0.7f).Within(0.001f));
        Assert.That(config.alignProjectileToDirection, Is.False);
        Assert.That(config.alignImpactToDirection, Is.False);

        Object.DestroyImmediate(root);
        Object.DestroyImmediate(profile);
        Object.DestroyImmediate(data);
    }

    [Test]
    public void ProjectileVfxTuningPreviewPullsSpawnTimingFromUnitData()
    {
        UnitData data = ScriptableObject.CreateInstance<UnitData>();
        BasicAttackVfxProfile profile = ScriptableObject.CreateInstance<BasicAttackVfxProfile>();
        profile.projectileVfxConfig = ProjectileVfxConfig.CreateDefault("ProjectileKey");
        profile.projectileVfxConfig.projectileSpawnNormalizedTime = 0.63f;
        data.basicAttackVfxProfile = profile;

        GameObject root = new GameObject("ProjectilePreviewRoot");
        var preview = root.AddComponent<ProjectileVfxTuningPreview>();

        var serializedPreview = new SerializedObject(preview);
        serializedPreview.FindProperty("unitData").objectReferenceValue = data;
        serializedPreview.FindProperty("attackSpawnNormalizedTime").floatValue = 0.1f;
        serializedPreview.ApplyModifiedPropertiesWithoutUndo();

        preview.PullFromUnitData();

        serializedPreview.Update();
        Assert.That(serializedPreview.FindProperty("attackSpawnNormalizedTime").floatValue, Is.EqualTo(0.63f).Within(0.001f));

        Object.DestroyImmediate(root);
        Object.DestroyImmediate(profile);
        Object.DestroyImmediate(data);
    }

    [Test]
    public void ProjectileVfxTuningPreviewSavesPrefabSelectionAsAddressKey()
    {
        UnitData data = ScriptableObject.CreateInstance<UnitData>();
        BasicAttackVfxProfile profile = ScriptableObject.CreateInstance<BasicAttackVfxProfile>();
        profile.EnsureConfigs();
        data.basicAttackVfxProfile = profile;
        GameObject root = new GameObject("ProjectilePreviewRoot");
        var preview = root.AddComponent<ProjectileVfxTuningPreview>();

        GameObject muzzle = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/VFX/Projectile/VFX_Projectile_Fire4_Flash.prefab");
        GameObject projectile = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/VFX/Projectile/VFX_Projectile_Fire4_Projectile.prefab");
        GameObject impact = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/VFX/Projectile/VFX_Projectile_Fire4_Hit.prefab");
        Assert.That(muzzle, Is.Not.Null);
        Assert.That(projectile, Is.Not.Null);
        Assert.That(impact, Is.Not.Null);

        var serializedPreview = new SerializedObject(preview);
        serializedPreview.FindProperty("unitData").objectReferenceValue = data;
        serializedPreview.FindProperty("muzzleFlashPrefab").objectReferenceValue = muzzle;
        serializedPreview.FindProperty("projectilePrefab").objectReferenceValue = projectile;
        serializedPreview.FindProperty("impactFlashPrefab").objectReferenceValue = impact;
        serializedPreview.FindProperty("muzzleFlashAddress").stringValue = string.Empty;
        serializedPreview.FindProperty("projectileAddress").stringValue = string.Empty;
        serializedPreview.FindProperty("impactFlashAddress").stringValue = string.Empty;
        serializedPreview.ApplyModifiedPropertiesWithoutUndo();

        preview.CopySettingsToUnitData(false);

        ProjectileVfxConfig config = data.GetProjectileVfxConfig();
        Assert.That(config.muzzleFlashKey, Is.EqualTo("VFX_Projectile_Fire4_Flash"));
        Assert.That(config.projectileKey, Is.EqualTo("VFX_Projectile_Fire4_Projectile"));
        Assert.That(config.impactFlashKey, Is.EqualTo("VFX_Projectile_Fire4_Hit"));

        Object.DestroyImmediate(root);
        Object.DestroyImmediate(profile);
        Object.DestroyImmediate(data);
    }

    [Test]
    public void ProjectileVfxManagerUsesThreeStageConfigInsteadOfLegacyArray()
    {
        string source = File.ReadAllText("Assets/Scripts/VFX/ProjectileVfxManager.cs");
        string previewSource = File.ReadAllText("Assets/Scripts/VFX/ProjectileVfxTuningPreview.cs");
        string previewEditorSource = File.ReadAllText("Assets/Scripts/Editor/ProjectileVfxTuningPreviewEditor.cs");
        string runtimeUtilitySource = File.ReadAllText("Assets/Scripts/VFX/ProjectileVfxRuntimeUtility.cs");
        string unitDataSource = File.ReadAllText("Assets/Scripts/Game/Units/UnitData.cs");
        string unitSource = File.ReadAllText("Assets/Scripts/Game/Units/Unit.cs");
        string schedulerSource = File.ReadAllText("Assets/Scripts/Managers/CombatScheduler.cs");

        Assert.That(source, Does.Contain("TryResolveProjectileVfx"));
        Assert.That(source, Does.Contain("SpawnMuzzleFlashAsync"));
        Assert.That(source, Does.Contain("SpawnImpactFlashAsync"));
        Assert.That(source, Does.Contain("unit.Data.GetProjectileVfxConfig()"));
        Assert.That(source, Does.Contain("ResolveProjectileScaleMultiplierVector()"));
        Assert.That(source, Does.Not.Contain("projectilePrefabsByStarLevel"));
        Assert.That(source, Does.Contain("ProjectileVfxRuntimeUtility.RestartParticles"));
        Assert.That(source, Does.Contain("ApplyProjectileVisualHeight"));
        Assert.That(source, Does.Contain("ResolveProjectileVisualHeightOffset()"));
        Assert.That(runtimeUtilitySource, Does.Contain("GetComponentsInChildren<Light>"));
        Assert.That(runtimeUtilitySource, Does.Contain("ApplyDynamicLighting"));
        Assert.That(runtimeUtilitySource, Does.Contain("dynamicLightIntensity"));
        Assert.That(runtimeUtilitySource, Does.Contain("lightsModule.enabled = enableDynamicLighting"));
        Assert.That(runtimeUtilitySource, Does.Contain("PrepareRenderersForVfxVisibility"));
        Assert.That(runtimeUtilitySource, Does.Contain("allowOcclusionWhenDynamic = false"));
        Assert.That(runtimeUtilitySource, Does.Contain("BasicAttackVfxSortingOrder"));
        Assert.That(runtimeUtilitySource, Does.Contain("renderer.sortingOrder"));
        Assert.That(previewSource, Does.Contain("ProjectileVfxEffectRoot"));
        Assert.That(previewSource, Does.Contain("ResolvePreviewRoot()"));
        Assert.That(previewSource, Does.Contain("projectileVisualHeightOffset"));
        Assert.That(previewSource, Does.Contain("projectileDynamicLightIntensity"));
        Assert.That(previewSource, Does.Contain("attackSpawnNormalizedTime = config.ResolveProjectileSpawnNormalizedTime()"));
        Assert.That(previewSource, Does.Contain("config.projectileSpawnNormalizedTime"));
        Assert.That(previewSource, Does.Contain("now - _nextLoopTime >= interval"));
        Assert.That(previewSource, Does.Contain("TryPlayProjectileSequenceFromAnimatorState"));
        Assert.That(previewSource, Does.Contain("MatchesAttackState"));
        Assert.That(previewSource, Does.Contain("_spawnedProjectileForTrackedAttack"));
        Assert.That(previewSource, Does.Not.Contain("Mathf.FloorToInt(Mathf.Max(attackState.normalizedTime"));
        Assert.That(previewSource, Does.Contain("IndexOf(\"atk\", System.StringComparison.OrdinalIgnoreCase)"));
        Assert.That(previewEditorSource, Does.Contain("Visual Height Offset"));
        Assert.That(previewEditorSource, Does.Contain("Dynamic Light Intensity"));
        Assert.That(previewEditorSource, Does.Contain("projectileVisualHeightOffset"));
        Assert.That(previewSource, Does.Contain("muzzleScaleMultiplierVector"));
        Assert.That(previewSource, Does.Contain("projectileScaleMultiplierVector"));
        Assert.That(previewSource, Does.Contain("impactScaleMultiplierVector"));
        Assert.That(previewSource, Does.Contain("CopySettingsToUnitData"));
        Assert.That(previewSource, Does.Contain("loopAttackAndVfx"));
        Assert.That(previewSource, Does.Contain("projectileSpeed"));
        Assert.That(unitSource, Does.Contain("ResolveProjectileFireDelaySeconds"));
        Assert.That(unitSource, Does.Contain("config.ResolveProjectileSpawnNormalizedTime()"));
        Assert.That(unitSource, Does.Contain("fireDelaySeconds"));
        Assert.That(unitSource, Does.Contain("IsAttackClipName"));
        Assert.That(unitSource, Does.Contain("IndexOf(\"atk\", System.StringComparison.OrdinalIgnoreCase)"));
        Assert.That(unitSource, Does.Not.Contain("SchedulePendingRangedAttackForCurrentAnimation"));
        Assert.That(unitSource, Does.Not.Contain("UseConfiguredProjectileTiming"));
        Assert.That(schedulerSource, Does.Contain("PendingFire"));
        Assert.That(schedulerSource, Does.Contain("ProcessDueFires"));
        Assert.That(schedulerSource, Does.Contain("fireDelaySeconds"));
        Assert.That(schedulerSource, Does.Contain("PendingFireSnapshot : INetworkStruct"));
        Assert.That(schedulerSource, Does.Contain("PendingHitSnapshot : INetworkStruct"));
        Assert.That(schedulerSource, Does.Contain("PendingFireSnapshots"));
        Assert.That(schedulerSource, Does.Contain("PendingHitSnapshots"));
        Assert.That(schedulerSource, Does.Contain("RebuildPendingBucketsFromNetworkSnapshots"));
        Assert.That(schedulerSource, Does.Contain("ClearPendingFireSnapshot"));
        Assert.That(schedulerSource, Does.Contain("ClearPendingHitSnapshot"));
        Assert.That(schedulerSource, Does.Contain("PublishProjectileVfxEvent(attacker, target, fireTick, hitTick)"));
        Assert.That(schedulerSource, Does.Contain("RPC_PlayProjectileVfx"));
        Assert.That(schedulerSource, Does.Contain("ProjectileVfxManager.PlayFromCombatEvent"));
        Assert.That(schedulerSource, Does.Not.Contain("EventSeqs"));
        Assert.That(schedulerSource, Does.Not.Contain("TryGetEvent"));
        Assert.That(schedulerSource, Does.Not.Contain("GetInFlightEvents"));
        Assert.That(source, Does.Contain("PlayFromCombatEvent"));
        Assert.That(source, Does.Not.Contain("ProcessNewEvents"));
        Assert.That(source, Does.Not.Contain("CatchupInFlight"));
        Assert.That(unitDataSource, Does.Not.Contain("public float projectileSpeed"));
    }

    [Test]
    public void ProjectileVfxManagerFreezesDeadOrPooledTargets()
    {
        string source = File.ReadAllText("Assets/Scripts/VFX/ProjectileVfxManager.cs");

        Assert.That(source, Does.Contain("TargetNetworkIdRaw"));
        Assert.That(source, Does.Contain("TryRefreshTrackedTargetPosition"));
        Assert.That(source, Does.Contain("FreezeTrackedTarget(active)"));
        Assert.That(source, Does.Contain("!targetGameObject.activeInHierarchy"));
        Assert.That(source, Does.Contain("targetObject.Id.Raw != targetNetworkIdRaw"));
        Assert.That(source, Does.Contain("targetUnit.IsDead"));
        Assert.That(source, Does.Contain("targetUnit.NetworkedIsDead"));
        Assert.That(source, Does.Contain("targetMonster.currentHP <= 0f"));
    }

    [Test]
    public void VfxPoolManagerPrewarmsBasicAttackProfileKeysFromUnitData()
    {
        string source = File.ReadAllText("Assets/Scripts/VFX/VfxPoolManager.cs");

        Assert.That(source, Does.Contain("prewarmBasicAttackProfilesOnStart = true"));
        Assert.That(source, Does.Contain("basicAttackProfilePrewarmCount = 20"));
        Assert.That(source, Does.Contain("LoadManager.Instance.GetAllUnitData()"));
        Assert.That(source, Does.Contain("CollectBasicAttackVfxKeys"));
        Assert.That(source, Does.Contain("unitData.GetBasicAttackVfxConfig(starLevel)"));
        Assert.That(source, Does.Contain("slashConfig.prefabKey"));
        Assert.That(source, Does.Contain("projectileConfig.muzzleFlashKey"));
        Assert.That(source, Does.Contain("projectileConfig.projectileKey"));
        Assert.That(source, Does.Contain("projectileConfig.impactFlashKey"));
        Assert.That(source, Does.Contain("AssetLoader.LoadAssetAsync<GameObject>(key)"));
        Assert.That(source, Does.Contain("Prewarm(prefab, basicAttackProfilePrewarmCount"));
    }

    [Test]
    public void RequestedHovlProjectileWrapperPrefabsExistAsAddressables()
    {
        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        Assert.That(settings, Is.Not.Null);

        var expected = new HashSet<string>
        {
            "VFX_Projectile_Fire4_Projectile",
            "VFX_Projectile_Fire4_Hit",
            "VFX_Projectile_Fire4_Flash",
            "VFX_Projectile_Ice5_Projectile",
            "VFX_Projectile_Ice5_Hit",
            "VFX_Projectile_Ice5_Flash",
            "VFX_Projectile_Wind7_Projectile",
            "VFX_Projectile_Wind7_Hit",
            "VFX_Projectile_Wind7_Flash"
        };

        foreach (string address in expected)
        {
            string path = $"Assets/Prefabs/VFX/Projectile/{address}.prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

            Assert.That(prefab, Is.Not.Null, path);
            Assert.That(prefab.GetComponent<VFXAutoDestroy>(), Is.Not.Null, path);
            Assert.That(prefab.transform.childCount, Is.EqualTo(1), path);
            Assert.That(prefab.GetComponentInChildren<Projectile>(true), Is.Null, path);
            Assert.That(prefab.GetComponentsInChildren<Collider>(true), Is.Empty, path);
            Assert.That(prefab.GetComponentsInChildren<Rigidbody>(true), Is.Empty, path);

            if (address.EndsWith("_Projectile", System.StringComparison.Ordinal))
            {
                AssertProjectileWrapperHasNoEmbeddedOneShots(prefab, path);
            }

            var behaviours = prefab.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour behaviour = behaviours[i];
                if (behaviour == null || !behaviour.GetType().Name.StartsWith("HS_", System.StringComparison.Ordinal))
                {
                    continue;
                }

                Assert.Fail($"{path} still contains Hovl runtime behaviour {behaviour.GetType().Name}");
            }

            string guid = AssetDatabase.AssetPathToGUID(path);
            AddressableAssetEntry entry = settings.FindAssetEntry(guid);
            Assert.That(entry, Is.Not.Null, path);
            Assert.That(entry.address, Is.EqualTo(address), path);
        }

        Assert.That(File.Exists("Assets/Scripts/Editor/TempProjectileVfxWrapperGenerator.cs"), Is.False);
        Assert.That(File.Exists("Assets/Scripts/Editor/TempProjectileVfxPurifyMigration.cs"), Is.False);
    }

    private static void AssertProjectileWrapperHasNoEmbeddedOneShots(GameObject prefab, string path)
    {
        Transform[] transforms = prefab.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            Transform transform = transforms[i];
            bool isOneShot = transform.name.StartsWith("Flash", System.StringComparison.Ordinal)
                || transform.name.StartsWith("Hit", System.StringComparison.Ordinal);

            if (!isOneShot)
            {
                continue;
            }

            Assert.Fail($"{path} contains an embedded one-shot VFX object: {GetTransformPath(transform)}");
        }
    }

    private static string GetTransformPath(Transform transform)
    {
        var names = new List<string>();
        while (transform != null)
        {
            names.Add(transform.name);
            transform = transform.parent;
        }

        names.Reverse();
        return string.Join("/", names);
    }
}
#endif
