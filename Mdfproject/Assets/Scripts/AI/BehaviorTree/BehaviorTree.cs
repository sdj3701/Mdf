namespace AI.BehaviorTree
{
    public class BehaviorTree
    {
        private Node _root;

        public BehaviorTree(Node rootNode)
        {
            _root = rootNode;
        }

        public NodeStatus Tick()
        {
            return _root.Tick();
        }

        public Node GetRootNode()
        {
            return _root;
        }
    }
}
