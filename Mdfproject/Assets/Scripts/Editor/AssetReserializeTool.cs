#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 선택한 에셋 또는 특정 타입의 에셋을 강제로 리시리얼라이즈하는 에디터 도구
/// </summary>
[InitializeOnLoad]
public class AssetReserializeTool
{
    private static readonly string[] AutoReserializeFolders =
    {
        "Assets/Prefabs",
        "Assets/GameData",
        "Assets/Scenes",
        "Assets/Language",

        // "Assets/Etc"
    };

    static AssetReserializeTool()
    {
        EditorApplication.delayCall += OnEditorStartup;
    }

    private static void OnEditorStartup()
    {
        EditorApplication.delayCall -= OnEditorStartup;
        
        // 플레이 모드에서는 리시리얼라이즈 불가
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            return;
        }
        
        ReserializeTargetFolders();
    }

    /// <summary>
    /// 목록 폴더들을 리시리얼라이즈합니다.
    /// </summary>
    [MenuItem("Tools/Asset/Reserialize Target Folders")]
    private static void ReserializeTargetFolders()
    {
        var paths = new List<string>();

        foreach (var folder in AutoReserializeFolders)
        {
            if (AssetDatabase.IsValidFolder(folder) == false) continue;

            var guids = AssetDatabase.FindAssets("", new[] { folder });
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetDatabase.IsValidFolder(path) == false)
                {
                    paths.Add(path);
                }
            }
        }

        if (paths.Count == 0) return;

        AssetDatabase.ForceReserializeAssets(paths);
        Debug.Log($"[AssetReserializeTool] 에디터 시작 시 자동 리시리얼라이즈 완료: {paths.Count}개");
    }

    /// <summary>
    /// 선택한 에셋을 리시리얼라이즈합니다.
    /// </summary>
    [MenuItem("Assets/Reserialize Selected", false, 50)]
    public static void ReserializeSelected()
    {
        var selectedPaths = GetSelectedAssetPaths();

        if (selectedPaths.Count == 0)
        {
            Debug.LogWarning("[AssetReserializeTool] 선택된 에셋이 없습니다.");
            return;
        }

        AssetDatabase.ForceReserializeAssets(selectedPaths);
        Debug.Log($"[AssetReserializeTool] 리시리얼라이즈 완료: {selectedPaths.Count}개");
    }

    /// <summary>
    /// 선택한 에셋 리시리얼라이즈 메뉴 활성화 여부를 검증합니다.
    /// </summary>
    [MenuItem("Assets/Reserialize Selected", true)]
    public static bool ReserializeSelectedValidate()
    {
        return Selection.assetGUIDs.Length > 0;
    }

    /// <summary>
    /// 선택한 에셋의 경로 목록을 가져옵니다.
    /// </summary>
    private static List<string> GetSelectedAssetPaths()
    {
        var paths = new List<string>();

        foreach (var guid in Selection.assetGUIDs)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);

            if (AssetDatabase.IsValidFolder(path))
            {
                var assetGuids = AssetDatabase.FindAssets("", new[] { path });
                foreach (var assetGuid in assetGuids)
                {
                    var assetPath = AssetDatabase.GUIDToAssetPath(assetGuid);
                    if (AssetDatabase.IsValidFolder(assetPath) == false)
                    {
                        paths.Add(assetPath);
                    }
                }
            }
            else
            {
                paths.Add(path);
            }
        }

        return paths;
    }

    /// <summary>
    /// 모든 에셋을 리시리얼라이즈합니다.
    /// </summary>
    [MenuItem("Tools/Asset/Reserialize All Assets")]
    public static void ReserializeAllAssets()
    {
        if (EditorUtility.DisplayDialog(
            "Reserialize All Assets",
            "모든 에셋을 리시리얼라이즈합니다. 시간이 오래 걸릴 수 있습니다. 계속하시겠습니까?",
            "확인",
            "취소") == false)
        {
            return;
        }

        AssetDatabase.ForceReserializeAssets();
        Debug.Log("[AssetReserializeTool] 모든 에셋 리시리얼라이즈 완료");
    }

    /// <summary>
    /// 모든 프리팹 에셋을 리시리얼라이즈합니다.
    /// </summary>
    [MenuItem("Tools/Asset/Reserialize Prefab Assets")]
    public static void ReserializePrefabAssets()
    {
        string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" });
        string[] prefabPaths = new string[prefabGuids.Length];

        for (int i = 0; i < prefabGuids.Length; i++)
        {
            prefabPaths[i] = AssetDatabase.GUIDToAssetPath(prefabGuids[i]);
        }

        AssetDatabase.ForceReserializeAssets(prefabPaths);
        Debug.Log($"[AssetReserializeTool] 프리팹 리시리얼라이즈 완료: {prefabPaths.Length}개");
    }

    /// <summary>
    /// 모든 ScriptableObject 에셋을 리시리얼라이즈합니다.
    /// </summary>
    [MenuItem("Tools/Asset/Reserialize ScriptableObject Assets")]
    public static void ReserializeScriptableObjectAssets()
    {
        string[] scriptableObjectGuids = AssetDatabase.FindAssets("t:ScriptableObject", new[] { "Assets" });
        string[] scriptableObjectPaths = new string[scriptableObjectGuids.Length];

        for (int i = 0; i < scriptableObjectGuids.Length; i++)
        {
            scriptableObjectPaths[i] = AssetDatabase.GUIDToAssetPath(scriptableObjectGuids[i]);
        }

        AssetDatabase.ForceReserializeAssets(scriptableObjectPaths);
        Debug.Log($"[AssetReserializeTool] ScriptableObject 리시리얼라이즈 완료: {scriptableObjectPaths.Length}개");
    }

    /// <summary>
    /// 모든 Material 에셋을 리시리얼라이즈합니다.
    /// </summary>
    [MenuItem("Tools/Asset/Reserialize Material Assets")]
    public static void ReserializeMaterialAssets()
    {
        string[] materialGuids = AssetDatabase.FindAssets("t:Material", new[] { "Assets" });
        string[] materialPaths = new string[materialGuids.Length];

        for (int i = 0; i < materialGuids.Length; i++)
        {
            materialPaths[i] = AssetDatabase.GUIDToAssetPath(materialGuids[i]);
        }

        AssetDatabase.ForceReserializeAssets(materialPaths);
        Debug.Log($"[AssetReserializeTool] Material 리시리얼라이즈 완료: {materialPaths.Length}개");
    }

    /// <summary>
    /// 모든 Scene 에셋을 리시리얼라이즈합니다.
    /// </summary>
    [MenuItem("Tools/Asset/Reserialize Scene Assets")]
    public static void ReserializeSceneAssets()
    {
        string[] sceneGuids = AssetDatabase.FindAssets("t:Scene", new[] { "Assets" });
        string[] scenePaths = new string[sceneGuids.Length];

        for (int i = 0; i < sceneGuids.Length; i++)
        {
            scenePaths[i] = AssetDatabase.GUIDToAssetPath(sceneGuids[i]);
        }

        AssetDatabase.ForceReserializeAssets(scenePaths);
        Debug.Log($"[AssetReserializeTool] Scene 리시리얼라이즈 완료: {scenePaths.Length}개");
    }
}
#endif