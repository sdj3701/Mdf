// Assets/Scripts/Editor/MissingScriptFinder.cs
#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

/// <summary>
/// 프로젝트 전체에서 Missing Script가 있는 프리팹과 씬 오브젝트를 찾는 에디터 도구입니다.
/// </summary>
public class MissingScriptFinder : EditorWindow
{
    private Vector2 _scrollPosition;
    private List<string> _results = new List<string>();
    private bool _searchComplete = false;

    [MenuItem("Tools/Find Missing Scripts")]
    public static void ShowWindow()
    {
        GetWindow<MissingScriptFinder>("Missing Script Finder");
    }

    private void OnGUI()
    {
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Missing Script 검색 도구", EditorStyles.boldLabel);
        EditorGUILayout.Space(10);

        EditorGUILayout.BeginHorizontal();
        
        if (GUILayout.Button("프리팹에서 검색", GUILayout.Height(30)))
        {
            FindMissingScriptsInPrefabs();
        }
        
        if (GUILayout.Button("현재 씬에서 검색", GUILayout.Height(30)))
        {
            FindMissingScriptsInScene();
        }
        
        if (GUILayout.Button("전체 검색", GUILayout.Height(30)))
        {
            FindMissingScriptsAll();
        }
        
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(10);

        if (_searchComplete)
        {
            EditorGUILayout.LabelField($"검색 결과: {_results.Count}개 발견", EditorStyles.boldLabel);
            
            if (_results.Count == 0)
            {
                EditorGUILayout.HelpBox("Missing Script가 없습니다! ✓", MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox("아래 항목을 클릭하면 해당 에셋으로 이동합니다.", MessageType.Warning);
            }
        }

        EditorGUILayout.Space(5);
        
        _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
        
        foreach (var result in _results)
        {
            if (GUILayout.Button(result, EditorStyles.linkLabel))
            {
                // 프리팹 경로면 해당 에셋 선택
                if (result.StartsWith("Assets/"))
                {
                    var asset = AssetDatabase.LoadAssetAtPath<GameObject>(result);
                    if (asset != null)
                    {
                        Selection.activeObject = asset;
                        EditorGUIUtility.PingObject(asset);
                    }
                }
                else
                {
                    // 씬 오브젝트면 해당 오브젝트 찾기
                    var obj = GameObject.Find(result);
                    if (obj != null)
                    {
                        Selection.activeGameObject = obj;
                        EditorGUIUtility.PingObject(obj);
                    }
                }
            }
        }
        
        EditorGUILayout.EndScrollView();
    }

    private void FindMissingScriptsInPrefabs()
    {
        _results.Clear();
        _searchComplete = false;
        
        string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" });
        int total = prefabGuids.Length;
        int current = 0;
        
        foreach (var guid in prefabGuids)
        {
            current++;
            string path = AssetDatabase.GUIDToAssetPath(guid);
            
            EditorUtility.DisplayProgressBar("프리팹 검색 중...", path, (float)current / total);
            
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab != null && HasMissingScripts(prefab))
            {
                _results.Add(path);
                Debug.LogWarning($"[MissingScriptFinder] Missing Script 발견: {path}");
            }
        }
        
        EditorUtility.ClearProgressBar();
        _searchComplete = true;
        
        Debug.Log($"[MissingScriptFinder] 프리팹 검색 완료. {_results.Count}개의 Missing Script 발견.");
    }

    private void FindMissingScriptsInScene()
    {
        _results.Clear();
        _searchComplete = false;
        
        GameObject[] allObjects = GameObject.FindObjectsOfType<GameObject>(true);
        
        foreach (var obj in allObjects)
        {
            if (HasMissingScripts(obj))
            {
                string path = GetGameObjectPath(obj);
                _results.Add(path);
                Debug.LogWarning($"[MissingScriptFinder] 씬 오브젝트에서 Missing Script 발견: {path}");
            }
        }
        
        _searchComplete = true;
        Debug.Log($"[MissingScriptFinder] 씬 검색 완료. {_results.Count}개의 Missing Script 발견.");
    }

    private void FindMissingScriptsAll()
    {
        _results.Clear();
        
        // 프리팹 검색
        string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" });
        int total = prefabGuids.Length;
        int current = 0;
        
        foreach (var guid in prefabGuids)
        {
            current++;
            string path = AssetDatabase.GUIDToAssetPath(guid);
            
            EditorUtility.DisplayProgressBar("프리팹 검색 중...", path, (float)current / total);
            
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab != null && HasMissingScripts(prefab))
            {
                _results.Add($"[Prefab] {path}");
            }
        }
        
        EditorUtility.ClearProgressBar();
        
        // 씬 오브젝트 검색
        GameObject[] allObjects = GameObject.FindObjectsOfType<GameObject>(true);
        foreach (var obj in allObjects)
        {
            if (HasMissingScripts(obj))
            {
                _results.Add($"[Scene] {GetGameObjectPath(obj)}");
            }
        }
        
        _searchComplete = true;
        Debug.Log($"[MissingScriptFinder] 전체 검색 완료. {_results.Count}개의 Missing Script 발견.");
    }

    /// <summary>
    /// GameObject에 Missing Script가 있는지 확인합니다. (하위 오브젝트 포함)
    /// </summary>
    private bool HasMissingScripts(GameObject obj)
    {
        // 자신 확인
        Component[] components = obj.GetComponents<Component>();
        foreach (var comp in components)
        {
            if (comp == null)
            {
                return true;
            }
        }
        
        // 하위 오브젝트 확인
        foreach (Transform child in obj.transform)
        {
            if (HasMissingScripts(child.gameObject))
            {
                return true;
            }
        }
        
        return false;
    }

    /// <summary>
    /// GameObject의 전체 경로를 반환합니다.
    /// </summary>
    private string GetGameObjectPath(GameObject obj)
    {
        string path = obj.name;
        Transform parent = obj.transform.parent;
        
        while (parent != null)
        {
            path = parent.name + "/" + path;
            parent = parent.parent;
        }
        
        return path;
    }
}
#endif
