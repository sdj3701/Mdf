namespace AI.BehaviorTree
{
    public abstract class Node
    {
        public NodeStatus status = NodeStatus.Failure;
        public abstract NodeStatus Tick();
    }
}
