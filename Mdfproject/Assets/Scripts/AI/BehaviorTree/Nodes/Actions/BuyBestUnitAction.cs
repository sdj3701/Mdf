using System.Collections.Generic;
using AI.BehaviorTree; // for AIPacer
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
            Debug.Log($"<color=magenta>[BuyBestUnitAction] Tick 시작 - Player {_playerManager.playerId}, 골드: {_playerManager.GetGold()}, 구매완료: {_playerManager.unitPurchaseComplete}</color>");

            // Pace purchases so they don't look instantaneous
            if (!AIPacer.Ready(_playerManager.playerId, AIPacer.CatBuy))
            {
                Debug.Log($"<color=magenta>[BuyBestUnitAction] AIPacer 대기 중...</color>");
                return status = NodeStatus.Running; // Failure 대신 Running 반환
            }

            var availableItems = _playerManager.shopManager.GetAvailableShopItems();
            Debug.Log($"<color=magenta>[BuyBestUnitAction] 상점 아이템 개수: {availableItems.Count}</color>");

            int bestSlotIndex = -1;
            float highestScore = 0f;

            foreach (var itemPair in availableItems)
            {
                int slotIndex = itemPair.Key;
                ShopItem item = itemPair.Value;

                if (_playerManager.GetGold() < item.CalculatedCost)
                {
                    Debug.Log($"<color=magenta>[BuyBestUnitAction] 슬롯 {slotIndex}: {item.UnitData.unitName} - 골드 부족 (필요: {item.CalculatedCost}, 보유: {_playerManager.GetGold()})</color>");
                    continue;
                }

                var context = new AIContext(_playerManager, item);
                float currentScore = CalculateScore(context, _buyConsiderations);

                Debug.Log($"<color=magenta>[BuyBestUnitAction] 슬롯 {slotIndex}: {item.UnitData.unitName} - 점수: {currentScore:F2}</color>");

                if (currentScore > highestScore)
                {
                    highestScore = currentScore;
                    bestSlotIndex = slotIndex;
                }
            }

            // 0.1점 같은 임계값보다 높은 점수의 아이템이 있다면 구매
            if (bestSlotIndex != -1 && highestScore > 0.1f)
            {
                Debug.Log($"<color=magenta>[BuyBestUnitAction] 유닛 구매: 슬롯 {bestSlotIndex}, 점수: {highestScore:F2}</color>");
                _commandProcessor.RequestCommandExecution(new BuyUnitCommand(_playerManager.playerId, bestSlotIndex));
                // Arm next buy after a short random delay
                AIPacer.Arm(_playerManager.playerId, AIPacer.CatBuy, 0.5f, 1.0f);
                return status = NodeStatus.Success;
            }

            // 더 이상 구매할 유닛이 없으면 구매 완료 플래그 설정
            if (!_playerManager.unitPurchaseComplete)
            {
                _playerManager.unitPurchaseComplete = true;
                Debug.Log($"<color=magenta>[BuyBestUnitAction] Player {_playerManager.playerId} 유닛 구매 완료! (최고 점수: {highestScore:F2}, 골드: {_playerManager.GetGold()})</color>");
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
