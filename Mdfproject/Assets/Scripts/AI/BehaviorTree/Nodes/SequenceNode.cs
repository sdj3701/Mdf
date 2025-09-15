namespace AI.BehaviorTree.Nodes
{
    // 자식 노드 중 하나라도 실패하면 즉시 실패를 반환 (AND)
    public class SequenceNode : CompositeNode
    {
        public SequenceNode(params Node[] childNodes) : base(childNodes) { }

        public override NodeStatus Tick()
        {
            foreach (var child in children)
            {
                switch (child.Tick())
                {
                    case NodeStatus.Failure:
                        return NodeStatus.Failure;
                    case NodeStatus.Running:
                        return NodeStatus.Running;
                    case NodeStatus.Success:
                        continue;
                }
            }
            return NodeStatus.Success;
        }
    }
}
