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
        Assert.That(config.ResolveProjectileScaleMultiplier(), Is.EqualTo(1f).Within(0.001f));
        Assert.That(config.ResolveImpactScaleMultiplier(), Is.EqualTo(1f).Within(0.001f));
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
    public void RangedUnitDataAssetsHaveProjectileVfxConfigKeys()
    {
        var expected = new Dictionary<string, string>
        {
            { "Assets/GameData/Units/UnitData_Archer.asset", "ArrowProjectile" },
            { "Assets/GameData/Units/UnitData_Cleric.asset", "BaseProjectile" },
            { "Assets/GameData/Units/UnitData_Mage.asset", "BaseProjectile" },
            { "Assets/GameData/Units/UnitData_Pyromancer.asset", "BaseProjectile" }
        };

        foreach (var pair in expected)
        {
            UnitData data = AssetDatabase.LoadAssetAtPath<UnitData>(pair.Key);
            Assert.That(data, Is.Not.Null, pair.Key);
            Assert.That(data.basicAttackVfxProfile, Is.Not.Null, pair.Key);
            Assert.That(data.GetProjectilePrefabKey(), Is.EqualTo(pair.Value), pair.Key);
            Assert.That(data.GetProjectileVfxConfig(), Is.Not.Null, pair.Key);
            Assert.That(data.GetProjectileVfxConfig().ResolveProjectileSpeed(), Is.EqualTo(10f).Within(0.001f), pair.Key);
        }
    }

    [Test]
    public void TestSceneContainsProjectileVfxTuningRows()
    {
        string scene = File.ReadAllText("Assets/Scenes/test.unity");

        Assert.That(scene, Does.Contain("ProjectileVfxTuning_Root"));
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
        serializedPreview.FindProperty("muzzleScaleMultiplier").floatValue = 1.1f;
        serializedPreview.FindProperty("projectileScaleMultiplier").floatValue = 1.2f;
        serializedPreview.FindProperty("impactScaleMultiplier").floatValue = 1.3f;
        serializedPreview.FindProperty("muzzlePlaybackSpeed").floatValue = 0.9f;
        serializedPreview.FindProperty("projectilePlaybackSpeed").floatValue = 1.4f;
        serializedPreview.FindProperty("impactPlaybackSpeed").floatValue = 1.5f;
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
        Assert.That(config.muzzleScaleMultiplier, Is.EqualTo(1.1f).Within(0.001f));
        Assert.That(config.projectileScaleMultiplier, Is.EqualTo(1.2f).Within(0.001f));
        Assert.That(config.impactScaleMultiplier, Is.EqualTo(1.3f).Within(0.001f));
        Assert.That(config.muzzlePlaybackSpeed, Is.EqualTo(0.9f).Within(0.001f));
        Assert.That(config.projectilePlaybackSpeed, Is.EqualTo(1.4f).Within(0.001f));
        Assert.That(config.impactPlaybackSpeed, Is.EqualTo(1.5f).Within(0.001f));
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
    public void ProjectileVfxManagerUsesThreeStageConfigInsteadOfLegacyArray()
    {
        string source = File.ReadAllText("Assets/Scripts/VFX/ProjectileVfxManager.cs");
        string previewSource = File.ReadAllText("Assets/Scripts/VFX/ProjectileVfxTuningPreview.cs");
        string unitDataSource = File.ReadAllText("Assets/Scripts/Game/Units/UnitData.cs");

        Assert.That(source, Does.Contain("TryResolveProjectileVfx"));
        Assert.That(source, Does.Contain("SpawnMuzzleFlashAsync"));
        Assert.That(source, Does.Contain("SpawnImpactFlashAsync"));
        Assert.That(source, Does.Contain("unit.Data.GetProjectileVfxConfig()"));
        Assert.That(source, Does.Not.Contain("projectilePrefabsByStarLevel"));
        Assert.That(source, Does.Contain("ProjectileVfxRuntimeUtility.RestartParticles"));
        Assert.That(previewSource, Does.Contain("CopySettingsToUnitData"));
        Assert.That(previewSource, Does.Contain("loopAttackAndVfx"));
        Assert.That(previewSource, Does.Contain("projectileSpeed"));
        Assert.That(unitDataSource, Does.Not.Contain("public float projectileSpeed"));
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
}
#endif
