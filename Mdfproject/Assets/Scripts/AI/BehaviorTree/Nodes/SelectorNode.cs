namespace AI.BehaviorTree.Nodes
{
    // 자식 노드 중 하나라도 성공하면 즉시 성공을 반환 (OR)
    public class SelectorNode : CompositeNode
    {
        public SelectorNode(params Node[] childNodes) : base(childNodes) { }

        public override NodeStatus Tick()
        {
            foreach (var child in children)
            {
                switch (child.Tick())
                {
                    case NodeStatus.Success:
                        status = NodeStatus.Success;
                        return status;
                    case NodeStatus.Running:
                        status = NodeStatus.Running;
                        return status;
                    case NodeStatus.Failure:
                        continue;
                }
            }
            status = NodeStatus.Failure;
            return status;
        }
    }
}
