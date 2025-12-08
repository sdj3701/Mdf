using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public class BehaviorTreeViewer : EditorWindow
{
    private BehaviorTreeView _treeView;
    private AIPlayerController _selectedController;

    [MenuItem("AI/Behavior Tree Viewer")]
    public static void OpenWindow()
    {
        BehaviorTreeViewer window = GetWindow<BehaviorTreeViewer>();
        window.titleContent = new GUIContent("Behavior Tree Viewer");
    }

    public void CreateGUI()
    {
        _treeView = new BehaviorTreeView { style = { flexGrow = 1 } };
        rootVisualElement.Add(_treeView);

        var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/Editor/AI/BehaviorTreeViewer/BehaviorTreeEditor.uss");
        if (styleSheet != null)
            rootVisualElement.styleSheets.Add(styleSheet);
        
        EditorApplication.update += OnEditorUpdate;
        OnSelectionChange();
    }
    
    private void OnDisable()
    {
        EditorApplication.update -= OnEditorUpdate;
    }

    private void OnSelectionChange()
    {
        if (EditorApplication.isPlaying)
        {
            _selectedController = Selection.activeGameObject?.GetComponent<AIPlayerController>();
            if (_treeView != null)
            {
                 _treeView.PopulateView(_selectedController);
            }
        }
    }
    
    private void OnEditorUpdate()
    {
        if (EditorApplication.isPlaying && _selectedController != null && _treeView != null)
        {
            _treeView.PopulateView(_selectedController);
            _treeView.UpdateNodeStatuses();
        }
    }
}
