using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using AI.BehaviorTree.Nodes;

namespace AI.BehaviorTree.Nodes.Actions
{
    public class BuildMazeAction : ActionNode
    {
        private readonly PlayerManager _playerManager;
        private readonly CommandProcessor _commandProcessor;

        private float _nextBuildAt;
        private readonly float _minInterval = 0.3f;
        private readonly float _maxInterval = 0.8f;

        private readonly HashSet<Vector3Int> _temporarilySkipped = new HashSet<Vector3Int>();
        private readonly HashSet<Vector3Int> _builtAtLeastOnce = new HashSet<Vector3Int>();
        private float _skipClearTime;
        private Task<MazePlanner.MazePlanResult> _planTask;

        public BuildMazeAction(PlayerManager playerManager, CommandProcessor commandProcessor)
        {
            _playerManager = playerManager;
            _commandProcessor = commandProcessor;
        }

        public override NodeStatus Tick()
        {
            // Basic safety
            if (_playerManager == null || _playerManager.fieldManager == null || _playerManager.astarGrid == null)
            {
                Debug.LogWarning($"<color=red>[BuildMazeAction] Missing refs: PM={_playerManager != null}, FM={_playerManager?.fieldManager != null}, Grid={_playerManager?.astarGrid != null}</color>");
                return status = NodeStatus.Failure;
            }
            if (GameManagers.Instance == null || GameManagers.Instance.GetGameState() != GameManagers.GameState.Prepare)
            {
                return status = NodeStatus.Failure;
            }

            var fm = _playerManager.fieldManager;

            // 1) Plan once per match/owner
            if (!_playerManager.mazePlanned)
            {
                if (_planTask == null)
                {
                    _planTask = MazePlanner.PlanWallsAsync(fm, _playerManager);
                }

                if (!_planTask.IsCompleted)
                {
                    return status = NodeStatus.Running;
                }

                if (_planTask.IsFaulted || _planTask.IsCanceled)
                {
                    Debug.LogWarning($"<color=red>[BuildMazeAction] Maze planning failed: {_planTask.Exception?.GetBaseException().Message}</color>");
                    _planTask = null;
                    return status = NodeStatus.Failure;
                }

                var planResult = _planTask.Result;
                _planTask = null;
                _playerManager.mazePlannedOrder = planResult?.BuildOrder ?? new List<Vector3Int>();
                _playerManager.mazeBuildCursor = 0;
                _playerManager.mazePlanned = true;
                _playerManager.mazeConstructionComplete = false;
                _builtAtLeastOnce.Clear();
                _temporarilySkipped.Clear();
                _nextBuildAt = Time.time + Random.Range(_minInterval, _maxInterval);
                Debug.Log($"<color=magenta>[BuildMazeAction] Player {_playerManager.playerId} planned {_playerManager.mazePlannedOrder.Count} walls</color>");
                return status = NodeStatus.Success;
            }

            if (_playerManager.mazePlannedOrder == null || _playerManager.mazePlannedOrder.Count == 0)
            {
                _playerManager.mazeConstructionComplete = true;
                return status = NodeStatus.Failure;
            }

            if (Time.time >= _skipClearTime)
            {
                _temporarilySkipped.Clear();
                _skipClearTime = Time.time + 1.5f;
            }

            int stock = _playerManager.GetWallCount();
            int reserve = _playerManager.GetWallReserveK();
            bool hasMissing = false;
            bool hasAffordable = false;
            Vector3Int? target = null;

            foreach (var pos in _playerManager.mazePlannedOrder)
            {
                if (fm.HasWallAt(pos)) continue;

                hasMissing = true;
                bool wasBuilt = _builtAtLeastOnce.Contains(pos);
                bool canSpend = wasBuilt ? stock > 0 : stock - reserve > 0;
                if (canSpend) hasAffordable = true;
                if (!canSpend) continue;
                if (_temporarilySkipped.Contains(pos)) continue;

                var occ = fm.GetUnitAt(pos);
                if (occ != null && occ.Data.unitType == UnitType.Melee)
                {
                    var alt = fm.FindFirstEmptySlot(occ.Data);
                    if (!alt.HasValue)
                    {
                        _temporarilySkipped.Add(pos);
                        continue;
                    }
                }

                target = pos;
                break;
            }

            if (!hasMissing)
            {
                if (!_playerManager.mazeConstructionComplete)
                {
                    _playerManager.mazeConstructionComplete = true;
                    Debug.Log($"<color=magenta>[BuildMazeAction] Player {_playerManager.playerId} blueprint complete ({_playerManager.mazePlannedOrder.Count} walls)</color>");
                }
                return status = NodeStatus.Failure;
            }

            if (!hasAffordable)
            {
                if (!_playerManager.mazeConstructionComplete)
                {
                    _playerManager.mazeConstructionComplete = true;
                    Debug.Log($"<color=yellow>[BuildMazeAction] Player {_playerManager.playerId} no stock for new builds this round (stock={stock}, reserve={reserve})</color>");
                }
                return status = NodeStatus.Failure;
            }

            if (!target.HasValue)
            {
                return status = NodeStatus.Failure;
            }

            if (Time.time < _nextBuildAt || !AIPacer.Ready(_playerManager.playerId, AIPacer.CatWall))
            {
                return status = NodeStatus.Failure;
            }

            var placeAt = target.Value;

            // 스폰/골 셀인지 확인 (안전장치)
            Vector3Int spawnCell = fm.WorldToGridInt(_playerManager.spawnPoint != null ? _playerManager.spawnPoint.position : Vector3.zero);
            Vector3Int goalCell = fm.WorldToGridInt(_playerManager.goalTransform != null ? _playerManager.goalTransform.position : Vector3.zero);
            if (placeAt == spawnCell || placeAt == goalCell)
            {
                // 스폰/골 위치는 건너뜀
                _temporarilySkipped.Add(placeAt);
                return status = NodeStatus.Failure;
            }

            var cmd = new PlaceWallCommand(_playerManager.playerId, placeAt);
            _commandProcessor.RequestCommandExecution(cmd);

            _builtAtLeastOnce.Add(placeAt);
            _playerManager.mazeBuildCursor = _playerManager.mazePlannedOrder.IndexOf(placeAt) + 1;
            _nextBuildAt = Time.time + Random.Range(_minInterval, _maxInterval);
            AIPacer.Arm(_playerManager.playerId, AIPacer.CatWall, _minInterval, _maxInterval);

            return status = NodeStatus.Success;
        }
    }
}
