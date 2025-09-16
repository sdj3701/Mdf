using AI.BehaviorTree.Nodes;

namespace AI.BehaviorTree.Nodes.Actions
{
    public class ChooseBestAugmentAction : ActionNode
    {
        private PlayerManager _playerManager;
        private CommandProcessor _commandProcessor;

        public ChooseBestAugmentAction(PlayerManager playerManager, CommandProcessor commandProcessor)
        {
            _playerManager = playerManager;
            _commandProcessor = commandProcessor;
        }

        public override NodeStatus Tick()
        {
            if (_playerManager.augmentManager.GetPresentedAugments().Count > 0)
            {
                // TODO: 유틸리티 시스템으로 최고의 증강을 선택하는 로직
                int bestAugmentIndex = 0; // 임시: 무조건 첫 번째 선택
                _commandProcessor.ExecuteCommand(new SelectAugmentCommand(_playerManager.playerId, bestAugmentIndex));
                return status = NodeStatus.Success;
            }
            return status = NodeStatus.Failure;
        }
    }
}
