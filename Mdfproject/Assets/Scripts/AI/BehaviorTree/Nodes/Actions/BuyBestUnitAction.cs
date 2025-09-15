using System.Collections.Generic;
using AI.BehaviorTree.Nodes;
using AI.UtilitySystem;
using AI.UtilitySystem.Considerations;
using UnityEngine;

namespace AI.BehaviorTree.Nodes.Actions
{
    public class BuyBestUnitAction : ActionNode
    {
        private PlayerManager _playerManager;
        private CommandProcessor _commandProcessor;
        private List<Consideration> _buyConsiderations;

        public BuyBestUnitAction(PlayerManager playerManager, CommandProcessor commandProcessor)
        {
            _playerManager = playerManager;
            _commandProcessor = commandProcessor;

            // 이 액션이 사용할 평가 기준들을 설정
            _buyConsiderations = new List<Consideration>
            {
                new CanCombineConsideration { weight = 2.0f }, // 조합 가능성은 2배 중요!
                new GoldRemainingConsideration { weight = 0.8f },
            };
        }

        public override NodeStatus Tick()
        {
            var availableItems = _playerManager.shopManager.GetAvailableShopItems();
            int bestSlotIndex = -1;
            float highestScore = 0f;

            foreach (var itemPair in availableItems)
            {
                int slotIndex = itemPair.Key;
                ShopItem item = itemPair.Value;

                if (_playerManager.GetGold() < item.CalculatedCost) continue;

                var context = new AIContext(_playerManager, item);
                float currentScore = CalculateScore(context, _buyConsiderations);

                if (currentScore > highestScore)
                {
                    highestScore = currentScore;
                    bestSlotIndex = slotIndex;
                }
            }

            // 0.2점 같은 임계값보다 높은 점수의 아이템이 있다면 구매
            if (bestSlotIndex != -1 && highestScore > 0.2f)
            {
                _commandProcessor.ExecuteCommand(new BuyUnitCommand(_playerManager.playerId, bestSlotIndex));
                return status = NodeStatus.Success;
            }

            return status = NodeStatus.Failure;
        }

        private float CalculateScore(AIContext context, List<Consideration> considerations)
        {
            float totalScore = 0;
            float weightSum = 0;
            foreach (var consideration in considerations)
            {
                totalScore += consideration.Score(context) * consideration.weight;
                weightSum += consideration.weight;
            }
            return (weightSum > 0) ? totalScore / weightSum : 0;
        }
    }
}
