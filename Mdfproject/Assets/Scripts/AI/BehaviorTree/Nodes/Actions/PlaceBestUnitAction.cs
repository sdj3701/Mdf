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
                if (_playerManager.fieldManager.GetAlliedUnitsOnField().Count == 0)
                {
                    return status = NodeStatus.Success; // 재배치할 유닛이 없으면 즉시 성공
                }

                _rearrangedUnits = new List<Unit>();
                RecalculateMonsterPath(); // 재배치 시작 시 몬스터 경로를 한 번 계산하고 변환합니다.

                // RecalculateMonsterPath()에서 이미 변환된 _idealMonsterPath를 사용하므로
                // 여기서 다시 원본 경로로 덮어쓰지 않습니다.

                // 디버깅을 위해 AI가 사용하는 경로를 AstarGrid에 별도로 저장합니다.
                _playerManager.astarGrid.IdealPathForAIDebug = _idealMonsterPath;
            }

            var allUnitsOnField = _playerManager.fieldManager.GetAlliedUnitsOnField();

            // 모든 유닛의 재배치 검토가 끝났는지 확인합니다.
            if (_rearrangedUnits.Count >= allUnitsOnField.Count)
            {
                _rearrangedUnits = null; // 다음 사이클을 위해 상태를 리셋합니다.
                if (_playerManager.astarGrid != null) _playerManager.astarGrid.IdealPathForAIDebug = null; // 디버그 경로 정리
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

            // 현재 위치를 고려하여 최적의 새 위치를 찾습니다. (변환된 '이상적인 경로'를 전달)
            Vector3Int? bestPos = _playerManager.fieldManager.FindBestSpotForAI(nextUnitToMove.Data, _idealMonsterPath, null, null, originalPos);

            if (bestPos.HasValue && bestPos.Value != originalPos)
            {
                // 위치가 변경되어야 한다면 MoveUnitCommand를 실행합니다.
                _commandProcessor.RequestCommandExecution(new MoveUnitCommand(_playerManager.playerId, originalPos, bestPos.Value));
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

            // [3D Migration] FieldManager/AstarGrid 좌표 변환 사용 (origin/cellSize 반영)
            // 경계 밖일 수 있으므로 먼저 클램프 후 셀 변환
            Vector3 startClamped = grid.ClampToGrid(start.position);
            Vector3 goalClamped = grid.ClampToGrid(goal.position);
            Vector2Int startPos = grid.WorldToCell(startClamped);
            Vector2Int goalPos = grid.WorldToCell(goalClamped);

            Debug.Log($"[AI Path Debug] Player {_playerManager.playerId} - Start: {start.position} -> {startPos}, Goal: {goal.position} -> {goalPos}");

            // 유닛이 없는 상태에서, 벽을 정상적으로 고려한 실제 몬스터 이동 경로를 계산합니다.
            bool pathFound = grid.FindPath(startPos, goalPos, false);

            // 경로 계산이 끝난 후, 모든 유닛의 콜라이더를 다시 활성화합니다.
            foreach (var collider in colliders)
            {
                if (collider != null) collider.enabled = true;
            }

            // 경로 처리 및 좌표계 변환
            if (pathFound && grid.FinalPath != null && grid.FinalPath.Count > 0)
            {
                // 이상적인 경로를 복사하여 저장합니다. (재배치 중에 경로가 변경되지 않도록)
                _idealMonsterPath = new List<AstarNode>(grid.FinalPath);

                // AI 필드 좌표계로 변환
                var fieldManager = _playerManager.fieldManager;
                if (fieldManager != null)
                {
                    // AI 필드의 실제 타일 범위 확인
                    var allValidTiles = fieldManager.GetValidPlacementTiles(UnitType.Melee);
                    if (allValidTiles != null && allValidTiles.Count > 0)
                    {
                        // AI 필드의 y좌표 중심값 계산
                        float fieldCenterY = (allValidTiles.Min(t => t.y) + allValidTiles.Max(t => t.y)) / 2.0f;

                        // 몬스터 경로의 y좌표 중심값 계산
                        float pathCenterY = (_idealMonsterPath.Min(n => n.y) + _idealMonsterPath.Max(n => n.y)) / 2.0f;

                        // 오프셋 계산: 필드 중심 - 경로 중심
                        int yOffset = Mathf.RoundToInt(fieldCenterY - pathCenterY);

                        // Debug.Log($"[AI Path Transform] 필드 중심 Y: {fieldCenterY}, 경로 중심 Y: {pathCenterY}, 오프셋: {yOffset}");

                        // 경로의 모든 노드를 AI 필드 좌표계로 변환
                        for (int i = 0; i < _idealMonsterPath.Count; i++)
                        {
                            var node = _idealMonsterPath[i];
                            _idealMonsterPath[i] = new AstarNode(node.isWall, node.x, node.y + yOffset);
                        }

                        // Debug.Log($"[AI Path Transform] 변환 후 첫 번째 노드: ({_idealMonsterPath[0].x}, {_idealMonsterPath[0].y}), 마지막 노드: ({_idealMonsterPath[_idealMonsterPath.Count-1].x}, {_idealMonsterPath[_idealMonsterPath.Count-1].y})");
                    }
                }

                grid.IdealPathForAIDebug = _idealMonsterPath; // 디버그용 경로 저장
            }
            else
            {
                Debug.LogWarning("[AI] 몬스터 경로를 찾을 수 없습니다. 재배치를 건너뜁니다.");
                _idealMonsterPath = new List<AstarNode>();
            }
        }
    }
}
