#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

public static class TestMatchingUIToolkitSceneInstaller
{
    private const string UxmlPath = "Assets/UI/TestMatching/TestMatching.uxml";
    private const string PanelSettingsPath = "Assets/UI/TestMatching/TestMatchingPanelSettings.asset";
    private const string SceneObjectName = "TestMatching UI Toolkit";

    [MenuItem("MDF/UI/TestMatching/Create In Active Scene")]
    public static void CreateInActiveScene()
    {
        VisualTreeAsset visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
        if (visualTreeAsset == null)
        {
            Debug.LogError($"[TestMatchingUIToolkitSceneInstaller] UXML not found: {UxmlPath}");
            return;
        }

        PanelSettings panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
        if (panelSettings == null)
        {
            Debug.LogError($"[TestMatchingUIToolkitSceneInstaller] PanelSettings not found: {PanelSettingsPath}");
            return;
        }

        GameObject target = GameObject.Find(SceneObjectName);
        if (target == null)
        {
            target = new GameObject(SceneObjectName);
            Undo.RegisterCreatedObjectUndo(target, "Create TestMatching UI Toolkit");
        }

        UIDocument uiDocument = target.GetComponent<UIDocument>();
        if (uiDocument == null)
        {
            uiDocument = Undo.AddComponent<UIDocument>(target);
        }

        TestMatchingUIToolkitController controller = target.GetComponent<TestMatchingUIToolkitController>();
        if (controller == null)
        {
            controller = Undo.AddComponent<TestMatchingUIToolkitController>(target);
        }

        uiDocument.panelSettings = panelSettings;
        uiDocument.visualTreeAsset = visualTreeAsset;

        SerializedObject serializedController = new SerializedObject(controller);
        serializedController.FindProperty("document").objectReferenceValue = uiDocument;
        serializedController.FindProperty("titleSceneName").stringValue = SceneDefine.Title;
        serializedController.FindProperty("joinLobbySceneName").stringValue = SceneDefine.JoinLobby;
        serializedController.FindProperty("createRoomUsesInputText").boolValue = true;
        serializedController.ApplyModifiedProperties();

        EditorUtility.SetDirty(target);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Selection.activeGameObject = target;

        Debug.Log("[TestMatchingUIToolkitSceneInstaller] TestMatching UI Toolkit object is ready in the active scene.");
    }
}
#endif
