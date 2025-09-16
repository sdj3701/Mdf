using System.Collections.Generic;
using System.Linq;
using AI.BehaviorTree.Nodes;
using AI.UtilitySystem;
using AI.UtilitySystem.Considerations.Placement;
using UnityEngine;

namespace AI.BehaviorTree.Nodes.Actions
{
    public class PlaceBestUnitAction : ActionNode
    {
        private PlayerManager _playerManager;
        private CommandProcessor _commandProcessor;
        private List<Consideration> _placementConsiderations;
        
        // 임시로 미배치 유닛을 추적하기 위한 정적 변수
        private static Dictionary<UnitData, int> _tempUnplacedUnits = new Dictionary<UnitData, int>();

        public PlaceBestUnitAction(PlayerManager playerManager, CommandProcessor commandProcessor)
        {
            _playerManager = playerManager;
            _commandProcessor = commandProcessor;

            _placementConsiderations = new List<Consideration>
            {
                new ProximityToAlliesConsideration { weight = 1.2f },
                new AttackRangeCoverageConsideration { weight = 1.0f },
                new RangedUnitSynergyConsideration { weight = 1.5f },
            };
        }

        public override NodeStatus Tick()
        {
            // TODO: PlayerManager에 배치되지 않은 유닛을 가져오는 기능 필요. 현재 임시 로직 유지.
            var unitToPlaceData = GetTempUnplacedUnit();
            if (unitToPlaceData == null)
            {
                return status = NodeStatus.Failure; // 배치할 유닛 없음
            }

            // FieldManager에서 유닛 타입에 따라 배치 가능한 타일 목록을 가져옵니다.
            var validTiles = _playerManager.fieldManager.GetValidPlacementTiles(unitToPlaceData.unitType);
            if (validTiles == null || validTiles.Count == 0)
            {
                Debug.LogWarning($"AI가 {unitToPlaceData.unitType} 타입의 유닛을 배치할 유효한 타일을 찾지 못했습니다.");
                return status = NodeStatus.Failure;
            }
            
            // FieldManager에서 현재 필드에 있는 모든 아군 유닛 목록을 가져옵니다.
            var alliedUnits = _playerManager.fieldManager.GetAlliedUnitsOnField();
            
            Vector3Int bestPosition = Vector3Int.zero;
            float highestScore = -1f;

            foreach (var tilePos in validTiles)
            {
                // 해당 위치에 다른 유닛이 이미 있는지 확인합니다.
                if (_playerManager.fieldManager.IsUnitAt(tilePos))
                {
                    continue;
                }
                
                var context = new AIContext(_playerManager, unitToPlaceData, tilePos, alliedUnits);
                float currentScore = CalculateScore(context, _placementConsiderations);

                if (currentScore > highestScore)
                {
                    highestScore = currentScore;
                    bestPosition = tilePos;
                }
            }

            if (highestScore > -1f)
            {
                _commandProcessor.ExecuteCommand(new PlaceUnitCommand(_playerManager.playerId, unitToPlaceData, bestPosition));
                RemoveTempUnplacedUnit(unitToPlaceData);

                Debug.Log($"[AI] {unitToPlaceData.unitName}을(를) {bestPosition}에 배치 (점수: {highestScore:F2})");
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
        
        #region FieldManager/PlayerManager 구현 전 임시 코드
        
        private UnitData GetTempUnplacedUnit()
        {
            return _tempUnplacedUnits.Keys.FirstOrDefault();
        }

        public static void AddTempUnplacedUnit(UnitData data)
        {
             if (_tempUnplacedUnits.ContainsKey(data)) _tempUnplacedUnits[data]++;
             else _tempUnplacedUnits[data] = 1;
        }

        private void RemoveTempUnplacedUnit(UnitData data)
        {
            if (_tempUnplacedUnits.ContainsKey(data))
            {
                _tempUnplacedUnits[data]--;
                if (_tempUnplacedUnits[data] <= 0)
                {
                    _tempUnplacedUnits.Remove(data);
                }
            }
        }
        
        #endregion
    }
}
