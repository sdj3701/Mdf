using System.Collections.Generic;
using System.Linq;
using AI.BehaviorTree.Nodes;
using UnityEngine;

namespace AI.BehaviorTree.Nodes.Actions
{
    /// <summary>
    /// 필드에 있는 모든 유닛의 최적 배치를 다시 계산하고, 초당 한 기씩 이동시킵니다.
    /// </summary>
    public class RearrangeAllUnitsAction : ActionNode
    {
        private readonly PlayerManager _playerManager;
        private readonly CommandProcessor _commandProcessor;

        // 재배치 진행 상태를 저장하기 위한 상태 변수
        private List<Unit> _rearrangedUnits;

        public RearrangeAllUnitsAction(PlayerManager playerManager, CommandProcessor commandProcessor)
        {
            _playerManager = playerManager;
            _commandProcessor = commandProcessor;
        }

        public override NodeStatus Tick()
        {
            // 재배치 사이클의 첫 번째 틱인 경우, 상태를 초기화합니다.
            if (_rearrangedUnits == null)
            {
                if (_playerManager.fieldManager.GetAlliedUnitsOnField().Count == 0)
                {
                    return status = NodeStatus.Success; // 재배치할 유닛이 없으면 즉시 성공
                }
                
                _rearrangedUnits = new List<Unit>();
                RecalculateMonsterPath(); // 재배치 시작 시 몬스터 경로를 한 번 계산합니다.
            }

            var allUnitsOnField = _playerManager.fieldManager.GetAlliedUnitsOnField();

            // 모든 유닛의 재배치 검토가 끝났는지 확인합니다.
            if (_rearrangedUnits.Count >= allUnitsOnField.Count)
            {
                _rearrangedUnits = null; // 다음 사이클을 위해 상태를 리셋합니다.
                _playerManager.fieldManager.CheckForCombination(); // 모든 이동 후 조합을 확인합니다.
                Debug.Log("[AI] 유닛 재배치를 완료했습니다.");
                return status = NodeStatus.Success;
            }

            // 재배치할 다음 유닛을 우선순위(원거리 -> 근접)에 따라 선택합니다.
            var nextUnitToMove = allUnitsOnField
                .Except(_rearrangedUnits)
                .OrderBy(u => u.Data.unitType == UnitType.Ranged ? 0 : 1)
                .FirstOrDefault();

            if (nextUnitToMove == null)
            {
                _rearrangedUnits = null; // 예외 상황: 남은 유닛이 없으면 종료
                return status = NodeStatus.Success;
            }

            // 선택된 유닛의 현재 위치를 찾습니다.
            Vector3Int? originalPosNullable = _playerManager.fieldManager.GetUnitPosition(nextUnitToMove);

            if (!originalPosNullable.HasValue)
            {
                Debug.LogWarning($"[AI] 재배치 중 유닛 {nextUnitToMove.Data.unitName}의 위치를 찾을 수 없어 건너뜁니다.");
                _rearrangedUnits.Add(nextUnitToMove);
                return status = NodeStatus.Running; // 다음 유닛으로 계속 진행
            }
            Vector3Int originalPos = originalPosNullable.Value;

            // 현재 위치를 고려하여 최적의 새 위치를 찾습니다.
            Vector3Int? bestPos = _playerManager.fieldManager.FindBestSpotForAI(nextUnitToMove.Data, null, null, originalPos);

            if (bestPos.HasValue && bestPos.Value != originalPos)
            {
                // 위치가 변경되어야 한다면 MoveUnitCommand를 실행합니다.
                _commandProcessor.ExecuteCommand(new MoveUnitCommand(_playerManager.playerId, originalPos, bestPos.Value));
            }

            // 이 유닛은 처리되었음을 기록합니다.
            _rearrangedUnits.Add(nextUnitToMove);

            // 아직 처리할 유닛이 남았으므로 'Running' 상태를 반환하여 다음 틱에 계속합니다.
            return status = NodeStatus.Running;
        }

        private void RecalculateMonsterPath()
        {
            var grid = _playerManager.astarGrid;
            var start = _playerManager.spawnPoint;
            var goal = _playerManager.goalTransform;

            if (grid == null || start == null || goal == null) return;

            Vector2Int startPos = new Vector2Int(Mathf.RoundToInt(start.position.x), Mathf.RoundToInt(start.position.y));
            Vector2Int goalPos = new Vector2Int(Mathf.RoundToInt(goal.position.x), Mathf.RoundToInt(goal.position.y));
        }
    }
}
