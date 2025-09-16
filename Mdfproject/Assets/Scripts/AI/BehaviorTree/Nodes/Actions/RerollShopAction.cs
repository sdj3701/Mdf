using AI.BehaviorTree.Nodes;

namespace AI.BehaviorTree.Nodes.Actions
{
    public class RerollShopAction : ActionNode
    {
        private PlayerManager _playerManager;
        private CommandProcessor _commandProcessor;

        public RerollShopAction(PlayerManager playerManager, CommandProcessor commandProcessor)
        {
            _playerManager = playerManager;
            _commandProcessor = commandProcessor;
        }

        public override NodeStatus Tick()
        {
            if (_playerManager.GetGold() >= _playerManager.shopManager.GetRerollCost())
            {
                _commandProcessor.ExecuteCommand(new RerollShopCommand(_playerManager.playerId));
                return status = NodeStatus.Success;
            }
            return status = NodeStatus.Failure;
        }
    }
}
