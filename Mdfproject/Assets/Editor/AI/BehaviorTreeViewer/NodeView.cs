using UnityEngine;
using UnityEditor.Experimental.GraphView;
using UnityEngine.UIElements;
using AI.BehaviorTree;
using AI.BehaviorTree.Nodes;

public class NodeView : UnityEditor.Experimental.GraphView.Node
{
    public AI.BehaviorTree.Node RuntimeNode { get; private set; }
    public Port InputPort { get; private set; }
    public Port OutputPort { get; private set; }

    public NodeView(AI.BehaviorTree.Node node)
    {
        this.RuntimeNode = node;
        this.title = node.GetType().Name.Replace("Node", "");
        this.viewDataKey = System.Guid.NewGuid().ToString();

        CreatePorts();
        SetupClasses();
    }

    private void CreatePorts()
    {
        InputPort = InstantiatePort(Orientation.Vertical, Direction.Input, Port.Capacity.Single, typeof(bool));
        InputPort.portName = "In";
        inputContainer.Add(InputPort);
        
        // Action 노드는 자식이 없으므로 출력 포트가 필요 없습니다.
        if (RuntimeNode is ActionNode) return;
        
        var capacity = (RuntimeNode is CompositeNode) ? Port.Capacity.Multi : Port.Capacity.Single;
        OutputPort = InstantiatePort(Orientation.Vertical, Direction.Output, capacity, typeof(bool));
        OutputPort.portName = "Out";
        outputContainer.Add(OutputPort);
    }

    private void SetupClasses()
    {
        if (RuntimeNode is ActionNode) AddToClassList("action");
        else if (RuntimeNode is CompositeNode) AddToClassList("composite");
        else if (RuntimeNode is DecoratorNode) AddToClassList("decorator");
    }

    public void UpdateStatus()
    {
        RemoveFromClassList("running");
        RemoveFromClassList("success");
        RemoveFromClassList("failure");

        if (RuntimeNode == null || !Application.isPlaying) return;
        
        switch (RuntimeNode.status)
        {
            case NodeStatus.Running:
                AddToClassList("running");
                break;
            case NodeStatus.Success:
                AddToClassList("success");
                break;
            case NodeStatus.Failure:
                AddToClassList("failure");
                break;
        }
    }
}
