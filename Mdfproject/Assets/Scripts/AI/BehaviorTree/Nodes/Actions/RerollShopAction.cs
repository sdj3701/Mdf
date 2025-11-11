using AI.BehaviorTree.Nodes;
using UnityEngine;

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
            int rerollCost = _playerManager.shopManager.GetRerollCost();
            int currentGold = _playerManager.GetGold();

            if (currentGold >= rerollCost)
            {
                Debug.Log($"<color=yellow>[RerollShopAction] Player {_playerManager.playerId} 리롤 실행 (골드: {currentGold} → {currentGold - rerollCost})</color>");
                _commandProcessor.RequestCommandExecution(new RerollShopCommand(_playerManager.playerId));
                return status = NodeStatus.Success;
            }
            return status = NodeStatus.Failure;
        }
    }
}
