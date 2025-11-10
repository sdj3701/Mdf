namespace AI.BehaviorTree.Nodes
{
    /// <summary>
    /// 항상 Success를 반환하는 노드
    /// 조건 노드를 단순 체크용으로 사용할 때 더미 자식으로 사용
    /// </summary>
    public class AlwaysSuccessNode : ActionNode
    {
        public override NodeStatus Tick()
        {
            status = NodeStatus.Success;
            return status;
        }
    }
}

