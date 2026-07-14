#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.SceneManagement;

public class AddressablesLoadingPolicyEditModeTests
{
    private static readonly Dictionary<string, string> AllowedRawAddressablesEntryPoints =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            {
                "Assets/Scripts/RuntimeAssets/AddressableAssetCache.cs|LoadAssetAsync",
                "The lease cache is the sole direct single-asset loader."
            },
            {
                "Assets/Scripts/Managers/LoadManager.cs|LoadAssetsAsync",
                "Boot and match data labels share one LoadManager-owned loader and explicit released handles."
            },
            {
                "Assets/Scripts/Managers/AugmentManager.cs|LoadAssetsAsync",
                "AugmentData is a documented label-owned lifetime with an explicit released handle."
            },
            {
                "Assets/Scripts/Managers/AddressablesManager.cs|InitializeAsync",
                "The bootstrap catalog initialization handle is explicitly released."
            },
            {
                "Assets/Scripts/Managers/AddressablesManager.cs|AssetReference.LoadAssetAsync",
                "Required Fusion prefabs use serialized AssetReference ownership and ReleaseAsset."
            },
            {
                "Assets/Scripts/ComponentRegistrySystem/StaticAssets/AssetRegistry.cs|InitializeAsync",
                "The legacy registry owns and releases its one catalog initialization handle."
            }
        };

    private static readonly Dictionary<string, string> ExpectedGroups = new Dictionary<string, string>
    {
        { "MDF Gameplay Data", "mdf-gameplay-data" },
        { "MDF Runtime World", "mdf-runtime-world" },
        { "MDF Unit Prefabs", "mdf-unit-prefab" },
        { "MDF Monster Prefabs", "mdf-monster-prefab" },
        { "MDF Portraits", "mdf-portrait" },
        { "MDF Projectiles", "mdf-projectile" },
        { "MDF UI", "mdf-ui" },
        { "MDF VFX Effects", "mdf-vfx-effect" },
        { "MDF VFX Projectiles", "mdf-vfx-projectile" }
    };

    private static readonly HashSet<string> PackSeparatelyGroups = new HashSet<string>
    {
        "MDF Unit Prefabs",
        "MDF Monster Prefabs",
        "MDF Portraits",
        "MDF Projectiles"
    };

    [Test]
    public void BootPolicyIsCatalogOnlyAndHasNoFullLocatorCollector()
    {
        Assert.That(AddressablesManager.BootLoadPolicy, Is.EqualTo(AddressablesBootLoadPolicy.CatalogOnly));
        Assert.That(
            typeof(AddressablesManager).GetMethod(
                "CollectAllObjectLocations",
                BindingFlags.Static | BindingFlags.NonPublic),
            Is.Null);
        Assert.That(
            typeof(AddressablesManager).GetMethod("InitializeAsync", BindingFlags.Instance | BindingFlags.Public),
            Is.Not.Null);
    }

    [Test]
    public void RawAddressablesLoadsStayInsideDocumentedLifecycleOwners()
    {
        var discovered = new List<string>();
        var directCallPattern = new Regex(
            @"Addressables\s*\.\s*(?<static>InitializeAsync|LoadAssetAsync|LoadAssetsAsync|InstantiateAsync)\s*(?:<|\()|" +
            @"\bassetReference\s*\.\s*(?<reference>LoadAssetAsync)\s*<",
            RegexOptions.Compiled);

        foreach (string guid in AssetDatabase.FindAssets("t:MonoScript", new[] { "Assets/Scripts" }))
        {
            string path = NormalizePath(AssetDatabase.GUIDToAssetPath(guid));
            if (path.Contains("/Editor/") || path.Contains("/Testing/"))
            {
                continue;
            }

            MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
            if (script == null)
            {
                continue;
            }

            foreach (Match match in directCallPattern.Matches(script.text))
            {
                string operation = match.Groups["static"].Success
                    ? match.Groups["static"].Value
                    : "AssetReference." + match.Groups["reference"].Value;
                discovered.Add(path + "|" + operation);
            }
        }

        Assert.That(discovered, Is.EquivalentTo(AllowedRawAddressablesEntryPoints.Keys),
            "New raw Addressables calls must use AddressableAssetCache/AssetLoader or be added as a reviewed, " +
            "explicitly released label/AssetReference lifecycle exception.");
    }

    [Test]
    public void EveryMdfEntryBelongsToOneUniqueLabeledCategoryGroup()
    {
        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        Assert.That(settings, Is.Not.Null);
        Assert.That(settings.DefaultGroup, Is.Not.Null);
        Assert.That(settings.DefaultGroup.entries, Is.Empty,
            "New content must be assigned to an explicit MDF category instead of the monolithic default bundle.");
        Assert.That(settings.FindGroup("MDF Runtime Core"), Is.Null,
            "The former oversized runtime group must not return after it has been split.");

        var addresses = new HashSet<string>();
        var guids = new HashSet<string>();
        foreach (KeyValuePair<string, string> expected in ExpectedGroups)
        {
            AddressableAssetGroup group = settings.FindGroup(expected.Key);
            Assert.That(group, Is.Not.Null, expected.Key);
            Assert.That(group.entries, Is.Not.Empty, expected.Key);

            BundledAssetGroupSchema schema = group.GetSchema<BundledAssetGroupSchema>();
            Assert.That(schema, Is.Not.Null, expected.Key);
            Assert.That(schema.IncludeInBuild, Is.True, expected.Key);
            BundledAssetGroupSchema.BundlePackingMode expectedMode = PackSeparatelyGroups.Contains(expected.Key)
                ? BundledAssetGroupSchema.BundlePackingMode.PackSeparately
                : BundledAssetGroupSchema.BundlePackingMode.PackTogether;
            Assert.That(schema.BundleMode, Is.EqualTo(expectedMode), expected.Key);

            foreach (AddressableAssetEntry entry in group.entries)
            {
                Assert.That(entry.labels, Does.Contain(expected.Value), entry.address);
                Assert.That(addresses.Add(entry.address), Is.True, $"Duplicate address: {entry.address}");
                Assert.That(guids.Add(entry.guid), Is.True, $"Duplicate GUID: {entry.guid}");
            }
        }

        string[] actualMdfGroups = settings.groups
            .Where(group => group != null && group.Name.StartsWith("MDF ", StringComparison.Ordinal))
            .Select(group => group.Name)
            .OrderBy(name => name)
            .ToArray();
        Assert.That(actualMdfGroups, Is.EquivalentTo(ExpectedGroups.Keys));
    }

    [Test]
    public void RuntimeGroupsFollowAssetChangeBoundaries()
    {
        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;

        AssertGroupPaths(settings, "MDF Unit Prefabs", path =>
            path.Contains("/Prefabs/Character/") && !path.EndsWith("/Hero.prefab"));
        AssertGroupPaths(settings, "MDF Monster Prefabs", path => path.Contains("/Prefabs/Monster/"));
        AssertGroupPaths(settings, "MDF Portraits", path => path.Contains("/Resource/Image/Portrait/"));
        AssertGroupPaths(settings, "MDF Projectiles", path => path.Contains("/Prefabs/Projectiles/"));
        AssertGroupPaths(settings, "MDF Runtime World", path =>
            path.EndsWith("/Hero.prefab") ||
            path.EndsWith("/Player_Root.prefab") ||
            path.EndsWith("/GameManagers.prefab") ||
            path.Contains("/Prefabs/Structure/") ||
            path.EndsWith("/User_Grid3D.prefab"));
    }

    [Test]
    public void SceneAndUiManagerOwnedPrefabsAreNotDuplicatedAsAddressableEntries()
    {
        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        var addressablePaths = new HashSet<string>(
            settings.groups
                .Where(group => group != null)
                .SelectMany(group => group.entries)
                .Select(entry => NormalizePath(AssetDatabase.GUIDToAssetPath(entry.guid))),
            StringComparer.OrdinalIgnoreCase);
        HashSet<string> requiredRuntimeAddressableGuids = CollectRequiredRuntimeAddressableGuids();

        foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes.Where(scene => scene.enabled))
        {
            foreach (string dependency in AssetDatabase.GetDependencies(scene.path, false))
            {
                string normalizedDependency = NormalizePath(dependency);
                if (!addressablePaths.Contains(normalizedDependency))
                {
                    continue;
                }

                string dependencyGuid = AssetDatabase.AssetPathToGUID(normalizedDependency);
                Assert.That(requiredRuntimeAddressableGuids, Does.Contain(dependencyGuid),
                    $"Built scene direct dependency is also Addressable without being a required runtime reference: " +
                    $"{scene.path} -> {dependency}");
            }
        }

        string playerRootGuid = AssetDatabase.AssetPathToGUID("Assets/Prefabs/Player_Root.prefab");
        Assert.That(requiredRuntimeAddressableGuids, Does.Contain(playerRootGuid));
        Assert.That(settings.FindAssetEntry(playerRootGuid), Is.Not.Null,
            "Player_Root is both a Fusion scene reference and a required AddressablesManager AssetReference.");

        string gameManagersGuid = AssetDatabase.AssetPathToGUID("Assets/Prefabs/GameManagers.prefab");
        Assert.That(requiredRuntimeAddressableGuids, Does.Contain(gameManagersGuid));
        Assert.That(settings.FindAssetEntry(gameManagersGuid), Is.Not.Null,
            "GameManagers is serialized in the scene but Fusion SpawnAsync resolves it through its GUID.");

        const string uiManagerPath = "Assets/Prefabs/UI/UIManager.prefab";
        foreach (string dependency in AssetDatabase.GetDependencies(uiManagerPath, false))
        {
            Assert.That(addressablePaths, Does.Not.Contain(NormalizePath(dependency)),
                $"UIManager-owned pool prefab is also Addressable: {dependency}");
        }

        GameObject uiManagerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(uiManagerPath);
        UIManagers manager = uiManagerPrefab.GetComponent<UIManagers>();
        Assert.That(manager, Is.Not.Null);
        Assert.That(manager.UILists.Any(prefab => prefab != null && prefab.name == "UI_Pnl_BattleTransition"), Is.True,
            "BattleTransition must remain resolvable through the scene-owned UI pool after its Addressable entry is removed.");
    }

    [Test]
    public void RemoteCatalogStaysDisabledUntilARealCdnProfileExists()
    {
        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        Assert.That(settings.BuildRemoteCatalog, Is.False);
    }

    [Test]
    public void UnitDataRetainsLegacyLabelAndAddsBootDataPolicyLabel()
    {
        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        AddressableAssetGroup group = settings.FindGroup("MDF Gameplay Data");
        FieldInfo bootLabel = typeof(LoadManager).GetField(
            "BootUnitDataLabel",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(bootLabel, Is.Not.Null);
        Assert.That(bootLabel.GetRawConstantValue(), Is.EqualTo("mdf-boot-data"));
        Assert.That(
            typeof(LoadManager).GetField("inspectorUnitData", BindingFlags.Instance | BindingFlags.NonPublic),
            Is.Null,
            "Boot data must come from the explicit Addressables label, not a scene-specific inspector fallback.");

        int unitDataCount = 0;

        foreach (AddressableAssetEntry entry in group.entries)
        {
            if (!entry.labels.Contains("UnitData"))
            {
                continue;
            }

            unitDataCount++;
            Assert.That(entry.labels, Does.Contain("mdf-gameplay-data"), entry.address);
            Assert.That(entry.labels, Does.Contain("mdf-boot-data"), entry.address);
        }

        Assert.That(unitDataCount, Is.GreaterThan(0));
    }

    private static void AssertGroupPaths(
        AddressableAssetSettings settings,
        string groupName,
        Func<string, bool> predicate)
    {
        AddressableAssetGroup group = settings.FindGroup(groupName);
        Assert.That(group, Is.Not.Null);
        Assert.That(group.entries, Is.Not.Empty);

        foreach (AddressableAssetEntry entry in group.entries)
        {
            string path = NormalizePath(AssetDatabase.GUIDToAssetPath(entry.guid));
            Assert.That(predicate(path), Is.True, $"Unexpected {groupName} entry: {entry.address} -> {path}");
        }
    }

    private static string NormalizePath(string path)
    {
        return string.IsNullOrEmpty(path) ? string.Empty : path.Replace('\\', '/');
    }

    private static HashSet<string> CollectRequiredRuntimeAddressableGuids()
    {
        string[] fieldNames =
        {
            "playerManagerPrefabRef",
            "gridPrefabRef",
            "defaultMonsterPrefabRef",
            "waveDatabaseRef"
        };
        var guids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        SceneSetup[] originalSetup = EditorSceneManager.GetSceneManagerSetup();

        try
        {
            foreach (EditorBuildSettingsScene builtScene in EditorBuildSettings.scenes.Where(scene => scene.enabled))
            {
                Scene scene = EditorSceneManager.OpenScene(builtScene.path, OpenSceneMode.Single);
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (AddressablesManager manager in root.GetComponentsInChildren<AddressablesManager>(true))
                    {
                        foreach (string fieldName in fieldNames)
                        {
                            FieldInfo field = typeof(AddressablesManager).GetField(
                                fieldName,
                                BindingFlags.Instance | BindingFlags.NonPublic);
                            var reference = field?.GetValue(manager) as AssetReference;
                            if (reference != null && !string.IsNullOrEmpty(reference.AssetGUID))
                            {
                                guids.Add(reference.AssetGUID);
                            }
                        }
                    }

                    foreach (GameSceneInitializer initializer in root.GetComponentsInChildren<GameSceneInitializer>(true))
                    {
                        if (initializer.gameManagersPrefab == null)
                        {
                            continue;
                        }

                        string prefabPath = AssetDatabase.GetAssetPath(initializer.gameManagersPrefab);
                        string prefabGuid = AssetDatabase.AssetPathToGUID(prefabPath);
                        if (!string.IsNullOrEmpty(prefabGuid))
                        {
                            guids.Add(prefabGuid);
                        }
                    }
                }
            }
        }
        finally
        {
            EditorSceneManager.RestoreSceneManagerSetup(originalSetup);
        }

        return guids;
    }
}
#endif
