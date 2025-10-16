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
                return status = NodeStatus.Failure;
            }
            if (GameManagers.Instance == null || GameManagers.Instance.GetGameState() != GameManagers.GameState.Prepare)
            {
                return status = NodeStatus.Failure;
            }

            var fm = _playerManager.fieldManager;

            // 1) 최초 1회 미로 계획 수립 (초기 벽 포함, 무제한 가정)
            if (!_playerManager.mazePlanned)
            {
                var planned = MazePlanner.PlanWalls(fm, _playerManager);
                _playerManager.mazePlannedOrder = planned ?? new List<Vector3Int>();
                _playerManager.mazeBuildCursor = 0;
                _playerManager.mazePlanned = true;
                _nextBuildAt = Time.time + Random.Range(_minInterval, _maxInterval);
                return status = NodeStatus.Failure;
            }

            // 2) 유지/보수: 계획된 순서에서 아직 없는 벽을 찾는다
            int nextIndex = -1;
            for (int i = 0; i < _playerManager.mazePlannedOrder.Count; i++)
            {
                var pos = _playerManager.mazePlannedOrder[i];
                if (_temporarilySkipped.Contains(pos)) continue;
                if (!fm.HasWallAt(pos)) { nextIndex = i; break; }
            }

            // 3) 더 지을 벽이 없다면 성공 반환 (다음 행동으로 넘어감)
            if (nextIndex == -1)
            {
                return status = NodeStatus.Failure;
            }

            // 4) 재고 확인: k개는 항상 남긴다
            int reserve = _playerManager.GetWallReserveK();
            int available = _playerManager.GetWallCount() - reserve;
            if (available <= 0)
            {
                return status = NodeStatus.Failure;
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
                        }
                        return status = NodeStatus.Failure;
                    }
                }

                // 안전: Spawn/Goal 보호는 PlaceWallCommand에서 재확인됨
                var cmd = new PlaceWallCommand(_playerManager.playerId, placeAt);
                _commandProcessor.RequestCommandExecution(cmd);

                _playerManager.mazeBuildCursor = nextIndex + 1;
                _nextBuildAt = Time.time + Random.Range(_minInterval, _maxInterval);
                AIPacer.Arm(_playerManager.playerId, AIPacer.CatWall, _minInterval, _maxInterval);
                return status = NodeStatus.Failure;
            }

            return status = NodeStatus.Failure;
        }
    }
}
