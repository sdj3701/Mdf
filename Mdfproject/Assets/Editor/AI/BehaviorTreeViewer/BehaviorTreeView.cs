using UnityEditor.Experimental.GraphView;
using UnityEngine.UIElements;
using System.Collections.Generic;
using AI.BehaviorTree;
using AI.BehaviorTree.Nodes;

public class BehaviorTreeView : GraphView
{
    public AIPlayerController AiController { get; private set; }
    private BehaviorTree _displayedTree;

    public BehaviorTreeView()
    {
        this.AddManipulator(new ContentZoomer());
        this.AddManipulator(new ContentDragger());
        this.AddManipulator(new SelectionDragger());
        this.AddManipulator(new RectangleSelector());

        var grid = new GridBackground();
        Insert(0, grid);
        grid.StretchToParentSize();
    }

    public void PopulateView(AIPlayerController aiController)
    {
        this.AiController = aiController;
        var activeTree = AiController?.GetActiveTree();

        // 표시된 트리와 현재 활성 트리가 동일하면 다시 그리지 않고 최적화합니다.
        if (activeTree == _displayedTree && _displayedTree != null)
        {
            return;
        }

        _displayedTree = activeTree;
        graphElements.ForEach(RemoveElement);

        if (AiController == null || _displayedTree == null || _displayedTree.GetRootNode() == null) return;
        
        CreateNodeViewRecursive(_displayedTree.GetRootNode(), null);
    }
    
    private void CreateNodeViewRecursive(AI.BehaviorTree.Node node, NodeView parentView)
    {
        NodeView nodeView = new NodeView(node);
        AddElement(nodeView);

        if (parentView != null && parentView.OutputPort != null && nodeView.InputPort != null)
        {
            var edge = parentView.OutputPort.ConnectTo(nodeView.InputPort);
            AddElement(edge);
        }

        if (node is CompositeNode composite)
        {
            composite.GetChildren().ForEach(child => CreateNodeViewRecursive(child, nodeView));
        }
        else if (node is DecoratorNode decorator)
        {
            if (decorator.GetChild() != null)
            {
                CreateNodeViewRecursive(decorator.GetChild(), nodeView);
            }
        }
    }
    
    public void UpdateNodeStatuses()
    {
        nodes.ForEach(n => {
            var view = n as NodeView;
            view?.UpdateStatus();
        });
    }
}
