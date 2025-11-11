using System.Collections.Generic;
using UnityEngine;
using AI.BehaviorTree.Nodes;

namespace AI.BehaviorTree.Nodes.Actions
{
    public class BuildMazeAction : ActionNode
    {
        private readonly PlayerManager _playerManager;
        private readonly CommandProcessor _commandProcessor;

        private float _nextBuildAt = 0f;
        private readonly float _minInterval = 0.3f;
        private readonly float _maxInterval = 0.8f;

        private readonly System.Collections.Generic.HashSet<Vector3Int> _temporarilySkipped = new System.Collections.Generic.HashSet<Vector3Int>();
        private float _skipClearTime = 0f;

        public BuildMazeAction(PlayerManager playerManager, CommandProcessor commandProcessor)
        {
            _playerManager = playerManager;
            _commandProcessor = commandProcessor;
        }

        public override NodeStatus Tick()
        {
            // 안전 검사
            if (_playerManager == null || _playerManager.fieldManager == null || _playerManager.astarGrid == null)
            {
                Debug.LogWarning($"<color=red>[BuildMazeAction] 안전 검사 실패: PlayerManager={_playerManager != null}, FieldManager={_playerManager?.fieldManager != null}, AstarGrid={_playerManager?.astarGrid != null}</color>");
                return status = NodeStatus.Failure;
            }
            if (GameManagers.Instance == null || GameManagers.Instance.GetGameState() != GameManagers.GameState.Prepare)
            {
                Debug.LogWarning($"<color=red>[BuildMazeAction] 게임 상태 확인 실패: GameState={GameManagers.Instance?.GetGameState()}</color>");
                return status = NodeStatus.Failure;
            }

            var fm = _playerManager.fieldManager;

            // 1) 최초 1회 미로 계획 수립 (초기 벽 포함, 무제한 가정)
            if (!_playerManager.mazePlanned)
            {
                Debug.Log($"<color=magenta>[BuildMazeAction] Player {_playerManager.playerId} 미로 계획 시작... (현재 골드: {_playerManager.GetGold()})</color>");
                var planned = MazePlanner.PlanWalls(fm, _playerManager);
                _playerManager.mazePlannedOrder = planned ?? new List<Vector3Int>();
                _playerManager.mazeBuildCursor = 0;
                _playerManager.mazePlanned = true;
                _nextBuildAt = Time.time + Random.Range(_minInterval, _maxInterval);
                Debug.Log($"<color=magenta>[BuildMazeAction] Player {_playerManager.playerId} 미로 계획 완료: {_playerManager.mazePlannedOrder.Count}개 벽, 다음 건설 시간: {_nextBuildAt} (현재 골드: {_playerManager.GetGold()})</color>");
                // 계획 수립 후 Success 반환 (다음 틱에 건설 시작)
                return status = NodeStatus.Success;
            }

            // 2) 유지/보수: 계획된 순서에서 아직 없는 벽을 찾는다
            int nextIndex = -1;
            int wallsAlreadyBuilt = 0;
            int wallsSkipped = 0;
            for (int i = 0; i < _playerManager.mazePlannedOrder.Count; i++)
            {
                var pos = _playerManager.mazePlannedOrder[i];
                if (_temporarilySkipped.Contains(pos))
                {
                    wallsSkipped++;
                    continue;
                }
                if (fm.HasWallAt(pos))
                {
                    wallsAlreadyBuilt++;
                }
                else
                {
                    nextIndex = i;
                    break;
                }
            }

            // 3) 더 지을 벽이 없다면 건설 완료 플래그 설정 후 Failure 반환
            if (nextIndex == -1)
            {
                if (!_playerManager.mazeConstructionComplete)
                {
                    _playerManager.mazeConstructionComplete = true;
                    Debug.Log($"<color=magenta>[BuildMazeAction] Player {_playerManager.playerId} 미로 건설 완료! (계획: {_playerManager.mazePlannedOrder.Count}, 건설: {wallsAlreadyBuilt}, 스킵: {wallsSkipped})</color>");
                }
                return status = NodeStatus.Failure;
            }
            
            //Debug.Log($"<color=cyan>[BuildMazeAction] 다음 벽 찾음: 인덱스 {nextIndex}/{_playerManager.mazePlannedOrder.Count}, 위치: {_playerManager.mazePlannedOrder[nextIndex]} (이미 건설: {wallsAlreadyBuilt}, 스킵: {wallsSkipped})</color>");

            // 4) 재고 확인: k개는 항상 남긴다
            int reserve = _playerManager.GetWallReserveK();
            int available = _playerManager.GetWallCount() - reserve;
            if (available <= 0)
            {
                Debug.LogWarning($"<color=yellow>[BuildMazeAction] Player {_playerManager.playerId} 벽 재고 부족: 현재={_playerManager.GetWallCount()}, 예약={reserve}, 사용가능={available}</color>");
                // 재고 부족 시 Running 반환 (대기 중)
                return status = NodeStatus.Running;
            }

            // 5) 랜덤 인터벌에 맞춰 한 개씩 건설 (AIPacer와 함께 사용)
            if (Time.time >= _nextBuildAt && AIPacer.Ready(_playerManager.playerId, AIPacer.CatWall))
            {
                var placeAt = _playerManager.mazePlannedOrder[nextIndex];

                // 주기적으로 스킵 목록 정리
                if (Time.time >= _skipClearTime)
                {
                    _temporarilySkipped.Clear();
                    _skipClearTime = Time.time + 1.5f;
                }

                // 배치 가능성 사전 점검: 근접 유닛이 있고 대체 슬롯이 없다면 잠시 스킵
                var occ = fm.GetUnitAt(placeAt);
                if (occ != null && occ.Data.unitType == UnitType.Melee)
                {
                    var alt = fm.FindFirstEmptySlot(occ.Data);
                    if (!alt.HasValue)
                    {
                        if (!_temporarilySkipped.Contains(placeAt))
                        {
                            _temporarilySkipped.Add(placeAt);
                            Debug.Log($"<color=magenta>[BuildMazeAction] Player {_playerManager.playerId} 벽 위치 {placeAt}에 근접 유닛이 있어 스킵</color>");
                        }
                        // 이 벽은 스킵하고 대기 (Running 반환)
                        return status = NodeStatus.Running;
                    }
                }

                // 안전: Spawn/Goal 보호는 PlaceWallCommand에서 재확인됨
                var cmd = new PlaceWallCommand(_playerManager.playerId, placeAt);
                _commandProcessor.RequestCommandExecution(cmd);

                _playerManager.mazeBuildCursor = nextIndex + 1;
                _nextBuildAt = Time.time + Random.Range(_minInterval, _maxInterval);
                AIPacer.Arm(_playerManager.playerId, AIPacer.CatWall, _minInterval, _maxInterval);

                // 벽을 건설했으므로 Success 반환 (이번 틱에서 성공적으로 행동 완료)
                return status = NodeStatus.Success;
            }

            // 아직 건설 타이밍이 아니면 Running 반환 (대기 중)
            return status = NodeStatus.Running;
        }
    }
}
