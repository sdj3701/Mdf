using AI.BehaviorTree.Nodes;

namespace AI.BehaviorTree.Nodes.Conditions
{
    /// <summary>
    /// 미로 건설이 완료되었는지 확인하는 조건 노드
    /// </summary>
    public class IsMazeConstructionCompleteCondition : DecoratorNode
    {
        private readonly PlayerManager _playerManager;

        public IsMazeConstructionCompleteCondition(PlayerManager playerManager, Node child) : base(child)
        {
            _playerManager = playerManager;
        }

        public override NodeStatus Tick()
        {
            if (_playerManager != null && _playerManager.mazeConstructionComplete)
            {
                status = child.Tick(); // 조건 만족 시 자식 노드 실행
                return status;
            }
            status = NodeStatus.Failure;
            return status; // 조건 불만족
        }
    }
}

