#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityCliConnector;
using UnityCliConnector.Tools;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class AttackSlashTuningSceneInstaller
{
    public const string DefaultScenePath = "Assets/Scenes/test.unity";
    private const string RootName = "AttackSlashTuning_Root";
    private const float UnitSpacing = 3.0f;
    private const float TargetDistance = 2.4f;

    [MenuItem("Tools/MDF/VFX/Rebuild Attack Slash Test Scene")]
    public static void RebuildDefaultTestSceneMenu()
    {
        RebuildTestScene(DefaultScenePath, true);
    }

    public static AttackSlashTuningSceneBuildResult RebuildTestScene(string scenePath, bool selectRoot)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            throw new InvalidOperationException("Stop Play Mode before rebuilding the attack slash tuning scene.");
        }

        if (string.IsNullOrWhiteSpace(scenePath))
        {
            scenePath = DefaultScenePath;
        }

        Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        GameObject oldRoot = GameObject.Find(RootName);
        if (oldRoot != null)
        {
            UnityEngine.Object.DestroyImmediate(oldRoot);
        }

        EnsureCameraAndLight();

        GameObject root = new GameObject(RootName);
        int created = PopulateUnits(root.transform);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();

        if (selectRoot)
        {
            Selection.activeGameObject = root;
        }

        Debug.Log($"[AttackSlashTuningSceneInstaller] Rebuilt {scenePath}. meleeUnits={created}");
        return new AttackSlashTuningSceneBuildResult
        {
            success = true,
            scenePath = scenePath,
            createdUnitCount = created,
            rootName = RootName
        };
    }

    private static int PopulateUnits(Transform root)
    {
        List<UnitData> units = LoadMeleeUnitsWithSlashVfx();
        int created = 0;
        for (int i = 0; i < units.Count; i++)
        {
            UnitData unitData = units[i];
            BasicAttackVfxConfig config = unitData.GetBasicAttackVfxConfig(1);
            string unitPrefabAddress = ResolveUnitPrefabAddress(unitData, 1);
            GameObject unitPrefab = ResolveAddressableGameObject(unitPrefabAddress);
            GameObject slashPrefab = ResolveAddressableGameObject(config.prefabKey);
            if (unitPrefab == null || slashPrefab == null)
            {
                Debug.LogWarning($"[AttackSlashTuningSceneInstaller] Skipped {unitData.name}. unitPrefab={unitPrefabAddress}, slashPrefab={config.prefabKey}");
                continue;
            }

            Vector3 position = new Vector3((i - (units.Count - 1) * 0.5f) * UnitSpacing, 0f, 0f);
            GameObject unitObject = PrefabUtility.InstantiatePrefab(unitPrefab) as GameObject;
            if (unitObject == null)
            {
                continue;
            }

            unitObject.name = $"Tuning_{unitData.unitName}_{unitPrefab.name}";
            unitObject.transform.SetParent(root, false);
            unitObject.transform.SetPositionAndRotation(position, Quaternion.identity);
            AssignUnitReferences(unitObject, unitData);

            GameObject target = new GameObject($"{unitObject.name}_Target");
            target.transform.SetParent(root, false);
            target.transform.position = position + Vector3.forward * TargetDistance + Vector3.up * 0.6f;

            AttackSlashTuningPreview preview = unitObject.GetComponent<AttackSlashTuningPreview>();
            if (preview == null)
            {
                preview = unitObject.AddComponent<AttackSlashTuningPreview>();
            }

            preview.Configure(unitData, 1, slashPrefab, config.prefabKey, target.transform);
            EditorUtility.SetDirty(preview);
            EditorUtility.SetDirty(unitObject);
            created++;
        }

        return created;
    }

    private static List<UnitData> LoadMeleeUnitsWithSlashVfx()
    {
        string[] guids = AssetDatabase.FindAssets("t:UnitData", new[] { "Assets/GameData/Units" });
        return guids
            .Select(guid => AssetDatabase.LoadAssetAtPath<UnitData>(AssetDatabase.GUIDToAssetPath(guid)))
            .Where(data => data != null && data.unitType == UnitType.Melee)
            .Where(data =>
            {
                BasicAttackVfxConfig config = data.GetBasicAttackVfxConfig(1);
                return config != null && config.HasPrefabKey;
            })
            .OrderBy(data => data.cost)
            .ThenBy(data => data.unitName, StringComparer.Ordinal)
            .ToList();
    }

    private static string ResolveUnitPrefabAddress(UnitData unitData, int starLevel)
    {
        if (unitData == null || unitData.prefabsByStarLevel == null || unitData.prefabsByStarLevel.Length == 0)
        {
            return string.Empty;
        }

        int index = Mathf.Clamp(starLevel - 1, 0, unitData.prefabsByStarLevel.Length - 1);
        return unitData.prefabsByStarLevel[index] ?? string.Empty;
    }

    private static void AssignUnitReferences(GameObject unitObject, UnitData unitData)
    {
        Unit unit = unitObject.GetComponent<Unit>();
        if (unit == null)
        {
            return;
        }

        var serializedUnit = new SerializedObject(unit);
        SerializedProperty unitDataProperty = serializedUnit.FindProperty("unitData");
        if (unitDataProperty != null)
        {
            unitDataProperty.objectReferenceValue = unitData;
        }

        SerializedProperty animatorProperty = serializedUnit.FindProperty("animator");
        if (animatorProperty != null && animatorProperty.objectReferenceValue == null)
        {
            animatorProperty.objectReferenceValue = unitObject.GetComponent<Animator>();
        }

        serializedUnit.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(unit);
    }

    private static void EnsureCameraAndLight()
    {
        Camera camera = UnityEngine.Object.FindObjectOfType<Camera>();
        if (camera == null)
        {
            GameObject cameraObject = new GameObject("Main Camera");
            camera = cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<AudioListener>();
        }

        camera.name = "Main Camera";
        camera.tag = "MainCamera";
        camera.transform.SetPositionAndRotation(new Vector3(0f, 4.0f, -8.0f), Quaternion.Euler(24f, 0f, 0f));

        Light light = UnityEngine.Object.FindObjectOfType<Light>();
        if (light == null)
        {
            GameObject lightObject = new GameObject("Directional Light");
            light = lightObject.AddComponent<Light>();
        }

        light.type = LightType.Directional;
        light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
    }

    private static GameObject ResolveAddressableGameObject(string address)
    {
        if (string.IsNullOrWhiteSpace(address) || AddressableAssetSettingsDefaultObject.Settings == null)
        {
            return null;
        }

        AddressableAssetEntry entry = AddressableAssetSettingsDefaultObject.Settings.groups
            .Where(group => group != null)
            .SelectMany(group => group.entries)
            .FirstOrDefault(candidate => candidate != null && candidate.address == address);

        if (entry == null)
        {
            return null;
        }

        string assetPath = AssetDatabase.GUIDToAssetPath(entry.guid);
        return string.IsNullOrWhiteSpace(assetPath) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
    }
}

public sealed class AttackSlashTuningSceneBuildResult
{
    public bool success;
    public string scenePath;
    public int createdUnitCount;
    public string rootName;
}

[UnityCliTool(Name = "attack_slash_rebuild_test_scene", Description = "Rebuild Assets/Scenes/test.unity with latest in-game melee unit prefabs and slash tuning components.")]
public static class AttackSlashRebuildTestSceneTool
{
    public static object HandleCommand(JObject parameters)
    {
        string scenePath = GetString(parameters, "scene_path", AttackSlashTuningSceneInstaller.DefaultScenePath);
        return AttackSlashTuningSceneInstaller.RebuildTestScene(scenePath, false);
    }

    private static string GetString(JObject body, string key, string fallback)
    {
        if (body != null && body.TryGetValue(key, StringComparison.OrdinalIgnoreCase, out JToken token))
        {
            string value = token.Type == JTokenType.Null ? null : token.ToString();
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }

        return fallback;
    }
}
#endif
