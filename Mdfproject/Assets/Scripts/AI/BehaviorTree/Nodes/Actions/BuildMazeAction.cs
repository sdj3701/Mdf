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
        private Task<MazePlanner.MazePlanResult> _extendPlanTask;
        private int _lastExtendBudgetTried = -1;
        private int _activeExtendBudget = -1;
        private float _nextPlanRetryAt;
        private int _planRetryCount;
        private const int MaxPlanRetries = 3;
        private const float PlanRetryDelay = 0.5f;

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
                // Debug.LogWarning($"<color=red>[BuildMazeAction] Missing refs: PM={_playerManager != null}, FM={_playerManager?.fieldManager != null}, Grid={_playerManager?.astarGrid != null}</color>");
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
                if (Time.time < _nextPlanRetryAt)
                {
                    return status = NodeStatus.Running;
                }

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
                    // Debug.LogWarning($"<color=red>[BuildMazeAction] Maze planning failed: {_planTask.Exception?.GetBaseException().Message}</color>");
                    _planTask = null;
                    return status = NodeStatus.Failure;
                }

                var planResult = _planTask.Result;
                _planTask = null;
                var plannedOrder = planResult?.BuildOrder ?? new List<Vector3Int>();
                if (plannedOrder.Count == 0 && _planRetryCount < MaxPlanRetries)
                {
                    _planRetryCount++;
                    _nextPlanRetryAt = Time.time + PlanRetryDelay;
                    int planningStock = _playerManager.GetWallCount();
                    int planningReserve = _playerManager.GetWallReserveK();
                    int budget = Mathf.Max(0, planningStock - planningReserve);
                    int pathLen = planResult?.ValidatedPath?.Count ?? 0;
                    int blueprintWalls = planResult?.BlueprintWalls?.Count ?? 0;
                    // Debug.LogWarning(
                        // $"<color=yellow>[BuildMazeAction] Empty maze plan; retrying ({_planRetryCount}/{MaxPlanRetries}) " +
                        // $"Start={planResult?.Start}, Goal={planResult?.Goal}, PathLen={pathLen}, BlueprintWalls={blueprintWalls}, Budget={budget}</color>");
                    return status = NodeStatus.Running;
                }

                if (plannedOrder.Count == 0)
                {
                    int planningStock = _playerManager.GetWallCount();
                    int planningReserve = _playerManager.GetWallReserveK();
                    int budget = Mathf.Max(0, planningStock - planningReserve);
                    int pathLen = planResult?.ValidatedPath?.Count ?? 0;
                    int blueprintWalls = planResult?.BlueprintWalls?.Count ?? 0;
                    // Debug.LogWarning(
                        // $"<color=yellow>[BuildMazeAction] Empty maze plan after retries; proceeding with 0 walls. " +
                        // $"Start={planResult?.Start}, Goal={planResult?.Goal}, PathLen={pathLen}, BlueprintWalls={blueprintWalls}, Budget={budget}</color>");
                }

                _planRetryCount = 0;
                _playerManager.mazePlannedOrder = plannedOrder;
                _playerManager.mazeBuildCursor = 0;
                _playerManager.mazePlanned = true;
                _playerManager.mazeConstructionComplete = false;
                _builtAtLeastOnce.Clear();
                _temporarilySkipped.Clear();
                _nextBuildAt = Time.time + Random.Range(_minInterval, _maxInterval);
                _extendPlanTask = null;
                _lastExtendBudgetTried = -1;
                _activeExtendBudget = -1;

                // === 스폰 포인트를 선택된 진입 구멍으로 재설정 ===
                if (planResult != null && _playerManager.spawnPoint != null)
                {
                    var newSpawnWorld = fm.GridToWorld(new Vector3Int(planResult.Start.x, planResult.Start.y, 0));
                    _playerManager.spawnPoint.position = newSpawnWorld;

                    // MonsterSpawner 재초기화 (새 스폰 위치 반영)
                    if (_playerManager.monsterSpawner != null && _playerManager.astarGrid != null && _playerManager.goalTransform != null)
                    {
                        var waveDatabase = AddressablesManager.Instance?.WaveDatabase;
                        _playerManager.monsterSpawner.Initialize(
                            _playerManager,
                            _playerManager.astarGrid,
                            waveDatabase,
                            _playerManager.spawnPoint,
                            _playerManager.goalTransform);
                    }

                    Debug.Log($"<color=magenta>[BuildMazeAction] Player {_playerManager.playerId} spawn relocated to gap {planResult.Start}, gapWalls={planResult.GapWalls?.Count ?? 0}</color>");
                }

                // Debug.Log($"<color=magenta>[BuildMazeAction] Player {_playerManager.playerId} planned {_playerManager.mazePlannedOrder.Count} walls</color>");
                return status = NodeStatus.Success;
            }

            // 1.5) Plan extension (when extra wall resources become available later)
            if (_extendPlanTask != null)
            {
                if (!_extendPlanTask.IsCompleted)
                {
                    return status = NodeStatus.Running;
                }

                if (_extendPlanTask.IsFaulted || _extendPlanTask.IsCanceled)
                {
                    // Debug.LogWarning($"<color=red>[BuildMazeAction] Maze extension planning failed: {_extendPlanTask.Exception?.GetBaseException().Message}</color>");
                    _extendPlanTask = null;
                    _activeExtendBudget = -1;
                    _playerManager.mazeConstructionComplete = true;
                    return status = NodeStatus.Failure;
                }

                var extendResult = _extendPlanTask.Result;
                _extendPlanTask = null;

                var extraOrder = extendResult?.BuildOrder ?? new List<Vector3Int>();
                if (extraOrder.Count == 0)
                {
                    if (_activeExtendBudget >= 0)
                    {
                        _lastExtendBudgetTried = Mathf.Max(_lastExtendBudgetTried, _activeExtendBudget);
                    }
                    _activeExtendBudget = -1;
                    _playerManager.mazeConstructionComplete = true;
                    // Debug.Log($"<color=magenta>[BuildMazeAction] Player {_playerManager.playerId} maze fully optimized; no extension walls</color>");
                    return status = NodeStatus.Failure;
                }

                if (_playerManager.mazePlannedOrder == null)
                {
                    _playerManager.mazePlannedOrder = new List<Vector3Int>();
                }

                var existing = new HashSet<Vector3Int>(_playerManager.mazePlannedOrder);
                int added = 0;
                foreach (var pos in extraOrder)
                {
                    if (existing.Add(pos))
                    {
                        _playerManager.mazePlannedOrder.Add(pos);
                        added++;
                    }
                }

                _temporarilySkipped.Clear();
                _nextBuildAt = Time.time + Random.Range(_minInterval, _maxInterval);
                _lastExtendBudgetTried = -1;
                _activeExtendBudget = -1;
                // Debug.Log($"<color=magenta>[BuildMazeAction] Player {_playerManager.playerId} planned maze extension (+{added} walls)</color>");
                return status = NodeStatus.Success;
            }

            if (_playerManager.mazePlannedOrder == null || _playerManager.mazePlannedOrder.Count == 0)
            {
                int currentStock = _playerManager.GetWallCount();
                int currentReserve = _playerManager.GetWallReserveK();
                int budget = currentStock - currentReserve;
                if (budget > 0 && budget > _lastExtendBudgetTried)
                {
                    _activeExtendBudget = budget;
                    _extendPlanTask = MazePlanner.PlanAdditionalWallsAsync(fm, _playerManager);
                    return status = NodeStatus.Running;
                }

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
                if (_extendPlanTask != null)
                {
                    return status = NodeStatus.Running;
                }

                int budget = stock - reserve;
                if (budget > 0 && budget > _lastExtendBudgetTried)
                {
                    _activeExtendBudget = budget;
                    _extendPlanTask = MazePlanner.PlanAdditionalWallsAsync(fm, _playerManager);
                    return status = NodeStatus.Running;
                }

                if (!_playerManager.mazeConstructionComplete)
                {
                    _playerManager.mazeConstructionComplete = true;
                    // Debug.Log($"<color=magenta>[BuildMazeAction] Player {_playerManager.playerId} blueprint complete ({_playerManager.mazePlannedOrder.Count} walls)</color>");
                }
                return status = NodeStatus.Failure;
            }

            if (!hasAffordable)
            {
                if (!_playerManager.mazeConstructionComplete)
                {
                    _playerManager.mazeConstructionComplete = true;
                    // Debug.Log($"<color=yellow>[BuildMazeAction] Player {_playerManager.playerId} no stock for new builds this round (stock={stock}, reserve={reserve})</color>");
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
            _lastExtendBudgetTried = -1;
            _playerManager.mazeBuildCursor = _playerManager.mazePlannedOrder.IndexOf(placeAt) + 1;
            _nextBuildAt = Time.time + Random.Range(_minInterval, _maxInterval);
            AIPacer.Arm(_playerManager.playerId, AIPacer.CatWall, _minInterval, _maxInterval);

            return status = NodeStatus.Success;
        }
    }
}
