namespace AI.BehaviorTree.Nodes
{
    // 자식 노드를 하나만 가지며, 실행 여부를 제어
    public abstract class DecoratorNode : Node
    {
        protected Node child;

        public DecoratorNode(Node childNode)
        {
            child = childNode;
        }
    }
}
