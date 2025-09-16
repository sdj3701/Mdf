using AI.BehaviorTree.Nodes;

namespace AI.BehaviorTree.Nodes.Actions
{
    /// <summary>
    /// 필드에 있는 모든 유닛의 최적 배치를 다시 계산하고 실행하도록 명령합니다.
    /// </summary>
    public class RearrangeAllUnitsAction : ActionNode
    {
        private readonly PlayerManager _playerManager;
        private readonly CommandProcessor _commandProcessor;

        public RearrangeAllUnitsAction(PlayerManager playerManager, CommandProcessor commandProcessor)
        {
            _playerManager = playerManager;
            _commandProcessor = commandProcessor;
        }

        public override NodeStatus Tick()
        {
            // 필드에 재배치할 유닛이 있는지 확인합니다.
            if (_playerManager.fieldManager.GetAlliedUnitsOnField().Count == 0)
            {
                return status = NodeStatus.Failure;
            }
            
            _commandProcessor.ExecuteCommand(new RearrangeUnitsCommand(_playerManager.playerId));
            
            // 재배치 명령은 항상 성공으로 간주하여, 리롤로 넘어가지 않도록 합니다.
            // (실제 배치가 성공했는지 여부와 관계없이, 재배치를 '시도'했다는 것이 중요)
            return status = NodeStatus.Success;
        }
    }
}
