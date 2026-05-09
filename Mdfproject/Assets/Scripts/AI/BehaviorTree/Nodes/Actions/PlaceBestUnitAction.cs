using System.Collections.Generic;
using System.Linq;
using AI.BehaviorTree; // for AIPacer pacing
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
        private List<Unit> _unitsToRearrange; // 재배치 시작 시점의 유닛 스냅샷
        private List<AstarNode> _idealMonsterPath;

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
                var currentUnits = _playerManager.fieldManager.GetAlliedUnitsOnField();
                if (currentUnits.Count == 0)
                {
                    return status = NodeStatus.Failure; // 재배치할 유닛이 없으면 다음 행동으로
                }

                _rearrangedUnits = new List<Unit>();
                // 재배치 시작 시점의 유닛 리스트를 스냅샷으로 저장
                _unitsToRearrange = new List<Unit>(currentUnits);

                RecalculateMonsterPath(); // 재배치 시작 시 몬스터 경로를 한 번 계산하고 변환합니다.

                Debug.Log($"[AI] 유닛 재배치 시작: {_unitsToRearrange.Count}개 유닛 대상");
            }

            // 모든 유닛의 재배치 검토가 끝났는지 확인합니다.
            if (_rearrangedUnits.Count >= _unitsToRearrange.Count)
            {
                Debug.Log($"[AI] 유닛 재배치 완료: {_rearrangedUnits.Count}/{_unitsToRearrange.Count}개 처리됨");
                _rearrangedUnits = null; // 다음 사이클을 위해 상태를 리셋합니다.
                _unitsToRearrange = null;
                _playerManager.fieldManager.CheckForCombination(); // 모든 이동 후 조합을 확인합니다.
                // Failure 반환: 재배치는 보조 행동이므로 완료 후 다른 행동 시도
                return status = NodeStatus.Failure;
            }

            // 재배치할 다음 유닛을 우선순위(원거리 -> 근접)에 따라 선택합니다.
            // 스냅샷 리스트에서 선택하되, 이미 처리된 유닛은 제외
            var nextUnitToMove = _unitsToRearrange
                .Except(_rearrangedUnits)
                .OrderBy(u => u.Data.unitType == UnitType.Ranged ? 0 : 1)
                .FirstOrDefault();

            if (nextUnitToMove == null)
            {
                Debug.LogWarning($"[AI] 재배치할 다음 유닛을 찾을 수 없습니다. 재배치 종료. (처리됨: {_rearrangedUnits.Count}/{_unitsToRearrange.Count})");
                _rearrangedUnits = null; // 예외 상황: 남은 유닛이 없으면 종료
                _unitsToRearrange = null;
                return status = NodeStatus.Failure;
            }

            // 선택된 유닛의 현재 위치를 찾습니다.
            Vector3Int? originalPosNullable = _playerManager.fieldManager.GetUnitPosition(nextUnitToMove);

            if (!originalPosNullable.HasValue)
            {
                Debug.LogWarning($"[AI] 재배치 중 유닛 {nextUnitToMove.Data.unitName}의 위치를 찾을 수 없어 건너뜁니다. (처리됨: {_rearrangedUnits.Count + 1}/{_unitsToRearrange.Count})");
                _rearrangedUnits.Add(nextUnitToMove);
                return status = NodeStatus.Running; // 다음 유닛으로 계속 진행
            }
            Vector3Int originalPos = originalPosNullable.Value;

            // 유닛의 실제 물리적 위치가 논리적 그리드 위치와 일치하는지 확인합니다.
            Vector3 expectedWorldPos = _playerManager.fieldManager.GridToWorld(originalPos, checkForWall: true);
            float distanceFromExpected = Vector3.Distance(nextUnitToMove.transform.position, expectedWorldPos);
            bool isPhysicallyAtPosition = distanceFromExpected < 0.1f; // 0.1 유닛 이내면 같은 위치로 간주

            // 현재 위치를 고려하여 최적의 새 위치를 찾습니다. (변환된 '이상적인 경로'를 전달)
            Vector3Int? bestPos = _playerManager.fieldManager.FindBestSpotForAI(nextUnitToMove.Data, _idealMonsterPath, null, null, originalPos);

            // 최적 위치로 이동해야 하는 경우:
            // 1. 최적 위치가 현재 논리적 위치와 다른 경우
            // 2. 또는 최적 위치가 같더라도 유닛이 물리적으로 그 위치에 없는 경우 (async 생성 지연 대응)
            if (bestPos.HasValue && (bestPos.Value != originalPos || !isPhysicallyAtPosition))
            {
                // Pace unit moves
                if (!AIPacer.Ready(_playerManager.playerId, AIPacer.CatMove))
                {
                    return status = NodeStatus.Running;
                }

                if (bestPos.Value != originalPos)
                {
                    // 위치가 변경되어야 한다면 MoveUnitCommand를 실행합니다.
                    //Debug.Log($"[AI] 유닛 재배치: {nextUnitToMove.Data.unitName} {originalPos} → {bestPos.Value} (진행: {_rearrangedUnits.Count + 1}/{_unitsToRearrange.Count})");
                    _commandProcessor.RequestCommandExecution(new MoveUnitCommand(_playerManager.playerId, originalPos, bestPos.Value));
                }
                else
                {
                    // 논리적 위치는 같지만 물리적으로 이동이 필요한 경우 (같은 위치로 "이동"하여 물리적 위치 동기화)
                    //Debug.Log($"[AI] 유닛 위치 동기화: {nextUnitToMove.Data.unitName} {originalPos} (물리적 거리: {distanceFromExpected:F2}) (진행: {_rearrangedUnits.Count + 1}/{_unitsToRearrange.Count})");
                    _commandProcessor.RequestCommandExecution(new MoveUnitCommand(_playerManager.playerId, originalPos, bestPos.Value));
                }
                AIPacer.Arm(_playerManager.playerId, AIPacer.CatMove, 0.4f, 0.9f);
            }
            else
            {
                //Debug.Log($"[AI] 유닛 유지: {nextUnitToMove.Data.unitName} {originalPos} (최적 위치) (진행: {_rearrangedUnits.Count + 1}/{_unitsToRearrange.Count})");
            }

            // 이 유닛은 처리되었음을 기록합니다.
            _rearrangedUnits.Add(nextUnitToMove);

            // 아직 처리할 유닛이 남았으므로 'Running' 상태를 반환하여 다음 틱에 계속합니다.
            return status = NodeStatus.Running;
        }

        private void RecalculateMonsterPath()
        {
            var grid = _playerManager.astarGrid;
            var goal = _playerManager.goalTransform;
            var fieldManager = _playerManager.fieldManager;

            if (grid == null || goal == null || fieldManager == null || !fieldManager.TryGetSingleOpenEntryNavigationCell(out var startPos)) return;

            // 재배치 계획을 위해 현재 필드에 있는 모든 유닛의 3D 콜라이더를 일시적으로 비활성화합니다.
            var allUnits = _playerManager.fieldManager.GetAlliedUnitsOnField();
            List<Collider> colliders = new List<Collider>();
            foreach (var unit in allUnits)
            {
                var collider = unit.GetComponentInChildren<Collider>();
                if (collider != null)
                {
                    colliders.Add(collider);
                    collider.enabled = false;
                }
            }

            Vector2Int goalPos = fieldManager.WorldToNavigationCell(goal.position);


            Debug.Log($"[AI Path Debug] Player {_playerManager.playerId} - EntryNav: {startPos}, Goal: {goal.position} -> {goalPos}");

            // 유닛이 없는 상태에서, 벽을 정상적으로 고려한 실제 몬스터 이동 경로를 계산합니다.
            bool pathFound = grid.FindPath(startPos, goalPos, ignoreWalls: false);

            foreach (var collider in colliders)
            {
                if (collider != null) collider.enabled = true;
            }

            if (pathFound && grid.FinalPath != null && grid.FinalPath.Count > 0)
            {
                Debug.Log($"[AI Path Debug] 경로 찾기 성공! 경로 길이: {grid.FinalPath.Count}");
                _idealMonsterPath = fieldManager.ConvertNavigationPathToInnerField(grid.FinalPath);
            }
            else
            {
                Debug.LogWarning("[AI] 몬스터 경로를 찾을 수 없습니다. 재배치를 건너뜁니다.");
                _idealMonsterPath = new List<AstarNode>();
            }
        }
    }
}
