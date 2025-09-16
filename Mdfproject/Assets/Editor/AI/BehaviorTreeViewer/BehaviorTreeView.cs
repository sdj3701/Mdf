using UnityEditor.Experimental.GraphView;
using UnityEngine.UIElements;
using System.Collections.Generic;
using AI.BehaviorTree;
using AI.BehaviorTree.Nodes;
using UnityEngine;

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
        
        // 루트 노드부터 재귀적으로 뷰 생성 시작, 초기 위치는 (0,0)
        CreateNodeViewRecursive(_displayedTree.GetRootNode(), null, Vector2.zero);
    }
    
    private void CreateNodeViewRecursive(AI.BehaviorTree.Node node, NodeView parentView, Vector2 position)
    {
        var nodeView = new NodeView(node);
        nodeView.SetPosition(new Rect(position, Vector2.zero)); // 노드 위치 설정, 크기는 USS에 따름
        AddElement(nodeView);

        if (parentView != null && parentView.OutputPort != null && nodeView.InputPort != null)
        {
            var edge = parentView.OutputPort.ConnectTo(nodeView.InputPort);
            AddElement(edge);
        }
        
        // 자식 노드들을 위한 위치 계산
        const float horizontalSpacing = 200f;
        const float verticalSpacing = 250f;

        if (node is CompositeNode composite)
        {
            var children = composite.GetChildren();
            float totalWidth = (children.Count - 1) * horizontalSpacing;
            float startX = position.x - totalWidth / 2;

            for (int i = 0; i < children.Count; i++)
            {
                var childPosition = new Vector2(startX + i * horizontalSpacing, position.y + verticalSpacing);
                CreateNodeViewRecursive(children[i], nodeView, childPosition);
            }
        }
        else if (node is DecoratorNode decorator)
        {
            if (decorator.GetChild() != null)
            {
                var childPosition = new Vector2(position.x, position.y + verticalSpacing);
                CreateNodeViewRecursive(decorator.GetChild(), nodeView, childPosition);
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
