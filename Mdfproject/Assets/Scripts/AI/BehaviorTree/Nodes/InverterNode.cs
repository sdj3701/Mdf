namespace AI.BehaviorTree.Nodes
{
    /// <summary>
    /// 자식 노드의 결과를 반전시키는 데코레이터 노드
    /// Success → Failure, Failure → Success
    /// </summary>
    public class InverterNode : DecoratorNode
    {
        public InverterNode(Node childNode) : base(childNode)
        {
        }

        public override NodeStatus Tick()
        {
            var childStatus = child.Tick();

            switch (childStatus)
            {
                case NodeStatus.Success:
                    status = NodeStatus.Failure;
                    return status;
                case NodeStatus.Failure:
                    status = NodeStatus.Success;
                    return status;
                case NodeStatus.Running:
                    status = NodeStatus.Running;
                    return status;
                default:
                    status = NodeStatus.Failure;
                    return status;
            }
        }
    }
}

