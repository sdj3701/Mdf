#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

public static class ReLoginUIToolkitSceneInstaller
{
    private const string UxmlPath = "Assets/UI/ReLogin/ReLogin.uxml";
    private const string PanelSettingsPath = "Assets/UI/ReLogin/ReLoginPanelSettings.asset";
    private const string SceneObjectName = "ReLogin UI Toolkit";

    [MenuItem("MDF/UI/ReLogin/Create In Active Scene")]
    public static void CreateInActiveScene()
    {
        VisualTreeAsset visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
        if (visualTreeAsset == null)
        {
            Debug.LogError($"[ReLoginUIToolkitSceneInstaller] UXML not found: {UxmlPath}");
            return;
        }

        PanelSettings panelSettings = GetOrCreatePanelSettings();
        GameObject target = GameObject.Find(SceneObjectName);
        if (target == null)
        {
            target = new GameObject(SceneObjectName);
            Undo.RegisterCreatedObjectUndo(target, "Create ReLogin UI Toolkit");
        }

        UIDocument uiDocument = target.GetComponent<UIDocument>();
        if (uiDocument == null)
        {
            uiDocument = Undo.AddComponent<UIDocument>(target);
        }

        uiDocument.panelSettings = panelSettings;
        uiDocument.visualTreeAsset = visualTreeAsset;

        if (target.GetComponent<ReLoginUIToolkitController>() == null)
        {
            Undo.AddComponent<ReLoginUIToolkitController>(target);
        }

        EditorUtility.SetDirty(target);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Selection.activeGameObject = target;

        Debug.Log("[ReLoginUIToolkitSceneInstaller] ReLogin UI Toolkit object is ready in the active scene.");
    }

    private static PanelSettings GetOrCreatePanelSettings()
    {
        PanelSettings panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
        if (panelSettings != null)
        {
            return panelSettings;
        }

        panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
        AssetDatabase.CreateAsset(panelSettings, PanelSettingsPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        return panelSettings;
    }
}
#endif
