// Assets/Scripts/AI/BehaviorTree/Nodes/Actions/AIAttackStrategy.cs
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AI.BehaviorTree.Nodes.Actions
{
    /// <summary>
    /// AI 공격자가 상대 필드를 분석하여 전략적으로 몬스터를 소환하기 위한 전략 클래스.
    /// 실제 플레이어와 동일한 스폰 영역을 사용합니다.
    /// </summary>
    public class AIAttackStrategy
    {
        #region 방향 정의
        public enum SpawnDirection
        {
            North,
            South,
            East,
            West
        }
        #endregion

        #region 필드
        private readonly FieldManager _targetField;
        private readonly PlayerManager _attackerPlayer;
        private readonly AstarGrid _targetGrid;
        private readonly Transform _goalTransform;
        private readonly LayerMask _spawnAreaLayerMask;

        // 방향별 유효 스폰 지점 캐시
        private Dictionary<SpawnDirection, List<Vector3>> _validSpawnPositions;
        #endregion

        #region 생성자
        public AIAttackStrategy(FieldManager targetField, PlayerManager attackerPlayer, LayerMask spawnAreaLayerMask)
        {
            _targetField = targetField;
            _attackerPlayer = attackerPlayer;
            _targetGrid = targetField?.playerManager?.astarGrid;
            _goalTransform = targetField?.playerManager?.goalTransform;
            _spawnAreaLayerMask = spawnAreaLayerMask;
        }
        #endregion

        #region 소환 계획 생성 (메인 진입점)
        /// <summary>
        /// AttackMonsterPool을 분석하여 전략적 소환 계획을 생성합니다.
        /// </summary>
        public AISpawnPlan BuildSpawnPlan(List<MonsterPoolEntry> pool)
        {
            var plan = new AISpawnPlan();

            if (pool == null || pool.Count == 0 || _targetField == null || _targetGrid == null || _goalTransform == null)
            {
                return plan;
            }

            // 1. 유효 스폰 지점 수집
            CollectValidSpawnPositions();

            if (_validSpawnPositions == null || _validSpawnPositions.Values.All(v => v.Count == 0))
            {
                // 폴백: 기본 스폰 포인트에 전부 소환
                Debug.LogWarning("[AIAttackStrategy] 유효한 스폰 지점을 찾지 못했습니다. 기본 위치에 소환합니다.");
                Vector3 fallbackPos = GetFallbackSpawnPos();
                var fallbackPhase = new AISpawnPhase();
                foreach (var entry in pool)
                {
                    if (entry == null || entry.IsEmpty) continue;
                    fallbackPhase.Orders.Add(new AISpawnOrder(entry, fallbackPos, entry.RemainingCount));
                }
                plan.Phases.Add(fallbackPhase);
                return plan;
            }

            // 2. 몬스터 분류
            ClassifyMonsterPool(pool, out var destroyers, out var tanks, out var normalGround, out var flying);

            // 3. 최적 스폰 지점 계산
            Vector3 groundSpawnPos = EvaluateGroundSpawnPosition();
            Vector3 flyingSpawnPos = EvaluateFlyingSpawnPosition();
            Vector3 destroyerSpawnPos = EvaluateDestroyerSpawnPosition();

            // 4. 전략 흐름에 따라 소환 계획 생성
            bool hasDestroyers = destroyers.Count > 0;

            if (hasDestroyers)
            {
                BuildPlanWithDestroyers(plan, destroyers, tanks, normalGround, flying,
                    groundSpawnPos, flyingSpawnPos, destroyerSpawnPos);
            }
            else
            {
                BuildPlanWithoutDestroyers(plan, tanks, normalGround, flying,
                    groundSpawnPos, flyingSpawnPos);
            }

            // Debug.Log($"[AIAttackStrategy] 소환 계획 생성 완료: {plan.Phases.Count} 페이즈, Destroyer={hasDestroyers}");
            return plan;
        }
        #endregion

        #region 스폰 지점 수집 (실제 플레이어와 동일한 검증)
        /// <summary>
        /// 아우터 그리드 영역에서 유효한 스폰 지점을 수집하고 4방향으로 그룹핑합니다.
        /// 실제 플레이어가 클릭 소환하는 영역과 동일한 검증 절차를 적용합니다.
        /// </summary>
        private void CollectValidSpawnPositions()
        {
            _validSpawnPositions = new Dictionary<SpawnDirection, List<Vector3>>
            {
                { SpawnDirection.North, new List<Vector3>() },
                { SpawnDirection.South, new List<Vector3>() },
                { SpawnDirection.East, new List<Vector3>() },
                { SpawnDirection.West, new List<Vector3>() }
            };

            if (_targetField == null)
            {
                return;
            }

            var fieldPositions = _targetField.GetOuterSpawnWorldPositionsByDirection(_spawnAreaLayerMask);
            foreach (var pair in fieldPositions)
            {
                _validSpawnPositions[ConvertDirection(pair.Key)].AddRange(pair.Value);
            }
        }

        private static SpawnDirection ConvertDirection(FieldManager.BorderDirection direction)
        {
            switch (direction)
            {
                case FieldManager.BorderDirection.North:
                    return SpawnDirection.North;
                case FieldManager.BorderDirection.South:
                    return SpawnDirection.South;
                case FieldManager.BorderDirection.East:
                    return SpawnDirection.East;
                default:
                    return SpawnDirection.West;
            }
        }

        #endregion

        #region 지상 몬스터 최적 스폰 지점
        /// <summary>
        /// 모든 유효 스폰 포인트에서 골 지점까지 A* 경로 길이를 비교하여 가장 짧은 경로의 스폰 지점을 반환합니다.
        /// 성능 최적화: 각 방향에서 골에 가장 가까운 후보를 먼저 선별한 뒤 A* 비교합니다.
        /// </summary>
        private Vector3 EvaluateGroundSpawnPosition()
        {
            Vector3 bestSpawnPos = GetFallbackSpawnPos();
            int shortestPath = int.MaxValue;

            foreach (var kvp in _validSpawnPositions)
            {
                if (kvp.Value.Count == 0) continue;

                // 해당 방향의 스폰 포인트들을 골까지 직선거리(sqrMagnitude) 기준 정렬 후 상위 N개만 A* 계산
                var candidates = kvp.Value;

                foreach (var candidate in candidates)
                {
                    int pathLength = CalculatePathLength(candidate, false);
                    if (pathLength > 0 && pathLength < shortestPath)
                    {
                        shortestPath = pathLength;
                        bestSpawnPos = candidate;
                    }
                }
            }

            Debug.Log($"[AIAttackStrategy] 지상 몬스터 최적 스폰 지점: {bestSpawnPos}, 경로 길이: {shortestPath}");
            return bestSpawnPos;
        }

        /// <summary>
        /// 후보 스폰 포인트 목록에서 목표 지점에 직선거리가 가장 가까운 상위 N개를 반환합니다.
        /// A* 계산 횟수를 줄이기 위한 사전 필터링 용도입니다.
        /// </summary>
        private List<Vector3> GetClosestCandidates(List<Vector3> points, Vector3 targetPos, int maxCount)
        {
            if (points.Count <= maxCount) return points;

            // 직선거리(sqrMagnitude) 기준으로 정렬하여 상위 maxCount개 반환
            var sorted = new List<Vector3>(points);
            sorted.Sort((a, b) =>
            {
                float distA = (a - targetPos).sqrMagnitude;
                float distB = (b - targetPos).sqrMagnitude;
                return distA.CompareTo(distB);
            });

            return sorted.GetRange(0, maxCount);
        }
        #endregion

        #region 공중 몬스터 최적 스폰 지점 (게릴라)
        /// <summary>
        /// 상대 원거리 유닛(벽 위 유닛)이 가장 적게 밀집된 방향에서 스폰 지점을 반환합니다.
        /// </summary>
        private Vector3 EvaluateFlyingSpawnPosition()
        {
            // 방향별 원거리 유닛 수 카운트
            var rangedUnitCounts = new Dictionary<SpawnDirection, int>
            {
                { SpawnDirection.North, 0 },
                { SpawnDirection.South, 0 },
                { SpawnDirection.East, 0 },
                { SpawnDirection.West, 0 }
            };

            Vector2Int gridSize = _targetField.gridSize;
            float midX = gridSize.x * 0.5f;
            float midY = gridSize.y * 0.5f;

            // placedUnits에서 벽 위에 배치된 유닛(원거리)의 위치를 카운트
            for (int x = 0; x < gridSize.x; x++)
            {
                for (int y = 0; y < gridSize.y; y++)
                {
                    var pos = new Vector3Int(x, y, 0);
                    if (_targetField.HasWallAt(pos) && _targetField.GetUnitAt(pos) != null)
                    {
                        // 유닛이 필드의 어느 쪽에 있는지 판단
                        SpawnDirection closestDir = GetClosestDirectionFromGrid(x, y, midX, midY, gridSize);
                        rangedUnitCounts[closestDir]++;
                    }
                }
            }

            // 유닛이 가장 적은 방향에서 유효 스폰 포인트가 있는 방향 선택
            SpawnDirection bestDir = SpawnDirection.North;
            int minCount = int.MaxValue;

            foreach (var kvp in rangedUnitCounts)
            {
                if (_validSpawnPositions.ContainsKey(kvp.Key) && _validSpawnPositions[kvp.Key].Count > 0)
                {
                    if (kvp.Value < minCount)
                    {
                        minCount = kvp.Value;
                        bestDir = kvp.Key;
                    }
                }
            }

            var candidates = _validSpawnPositions.ContainsKey(bestDir) ? _validSpawnPositions[bestDir] : null;
            if (candidates != null && candidates.Count > 0)
            {
                return candidates[candidates.Count / 2];
            }

            return GetFallbackSpawnPos();
        }

        /// <summary>
        /// 그리드 내 좌표가 어느 방향(벽/테두리)에 가장 가까운지 판단합니다.
        /// </summary>
        private SpawnDirection GetClosestDirectionFromGrid(int x, int y, float midX, float midY, Vector2Int gridSize)
        {
            float distNorth = gridSize.y - 1 - y;
            float distSouth = y;
            float distEast = gridSize.x - 1 - x;
            float distWest = x;

            float minDist = Mathf.Min(distNorth, Mathf.Min(distSouth, Mathf.Min(distEast, distWest)));

            if (Mathf.Approximately(minDist, distNorth)) return SpawnDirection.North;
            if (Mathf.Approximately(minDist, distSouth)) return SpawnDirection.South;
            if (Mathf.Approximately(minDist, distEast)) return SpawnDirection.East;
            return SpawnDirection.West;
        }
        #endregion

        #region 벽 파괴 몬스터 최적 스폰 지점
        /// <summary>
        /// ignoreBreakableWalls 경로로 벽을 부수며 진행할 때 가장 빠르고,
        /// 수비 유닛이 적은 방향의 스폰 지점을 반환합니다.
        /// 모든 방향의 유효 스폰 포인트에서 골에 가장 가까운 후보를 비교합니다.
        /// </summary>
        private Vector3 EvaluateDestroyerSpawnPosition()
        {
            Vector3 bestSpawnPos = GetFallbackSpawnPos();
            float bestScore = float.MinValue;

            foreach (var kvp in _validSpawnPositions)
            {
                if (kvp.Value.Count == 0) continue;

                // 각 방향에서 골에 가장 가까운 후보 포인트를 선별
                var candidates = kvp.Value;

                foreach (var candidate in candidates)
                {
                    // Destroyer 경로: ignoreBreakableWalls=true
                    int pathLength = CalculatePathLength(candidate, true);
                    if (pathLength <= 0) continue;

                    // 경로의 그리드 진입점 주변 수비 유닛 수 계산 (감점)
                    Vector3 entryPoint = GetPathEntryPoint(candidate, true);
                    int nearbyUnitCount = CountNearbyDefenders(entryPoint, 3);

                    // 점수 = 짧은 경로(높을수록 좋음) - 수비 유닛(많을수록 나쁨)
                    float score = 1000f / pathLength - nearbyUnitCount * 50f;

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestSpawnPos = candidate;
                    }
                }
            }

            Debug.Log($"[AIAttackStrategy] Destroyer 최적 스폰 지점: {bestSpawnPos}, 스코어: {bestScore:F1}");
            return bestSpawnPos;
        }

        /// <summary>
        /// 특정 위치 주변(반경 칸 수 이내) 수비 유닛 수를 카운트합니다.
        /// </summary>
        private int CountNearbyDefenders(Vector3 worldPos, int radius)
        {
            Vector2Int gridPos = _targetField.WorldToGrid(worldPos);
            int count = 0;

            for (int dx = -radius; dx <= radius; dx++)
            {
                for (int dy = -radius; dy <= radius; dy++)
                {
                    var checkPos = new Vector3Int(gridPos.x + dx, gridPos.y + dy, 0);
                    if (_targetField.IsValidGridPosition(checkPos) && _targetField.GetUnitAt(checkPos) != null)
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        /// <summary>
        /// 특정 스폰 위치에서 골까지의 A* 경로 중,
        /// 그리드 내부에 최초 진입하는 셀의 월드 좌표를 반환합니다.
        /// 아우터 좌표의 WorldToGrid 클램핑 문제를 회피합니다.
        /// </summary>
        private Vector3 GetPathEntryPoint(Vector3 spawnWorldPos, bool ignoreBreakableWalls)
        {
            if (_targetGrid == null || _goalTransform == null)
                return spawnWorldPos;

            Vector2Int startPos = _targetField.WorldToNavigationCell(spawnWorldPos);
            Vector2Int endPos = _targetField.WorldToNavigationCell(_goalTransform.position);

            if (!_targetGrid.FindPath(startPos, endPos, ignoreWalls: false, ignoreBreakableWalls: ignoreBreakableWalls))
                return spawnWorldPos;

            var path = _targetGrid.FinalPath;
            if (path == null || path.Count == 0) return spawnWorldPos;

            // 경로에서 내부 그리드에 해당하는 첫 번째 노드를 찾음
            foreach (var node in path)
            {
                if (_targetField.TryNavigationCellToInnerCell(new Vector2Int(node.x, node.y), out var innerCell))
                {
                    return _targetField.GridToWorld(innerCell);
                }
            }

            // 내부 진입점을 못 찾으면 경로의 중간 지점 사용
            var midNode = path[path.Count / 2];
            return _targetField.NavigationCellToWorld(new Vector2Int(midNode.x, midNode.y));
        }
        #endregion

        #region 몬스터 분류
        /// <summary>
        /// AttackMonsterPool을 Destroyer/탱커/일반지상/공중 4그룹으로 분류합니다.
        /// </summary>
        private void ClassifyMonsterPool(
            List<MonsterPoolEntry> pool,
            out List<MonsterPoolEntry> destroyers,
            out List<MonsterPoolEntry> tanks,
            out List<MonsterPoolEntry> normalGround,
            out List<MonsterPoolEntry> flying)
        {
            destroyers = new List<MonsterPoolEntry>();
            tanks = new List<MonsterPoolEntry>();
            normalGround = new List<MonsterPoolEntry>();
            flying = new List<MonsterPoolEntry>();

            // 먼저 Destroyer와 공중 분리
            var groundEntries = new List<MonsterPoolEntry>();

            foreach (var entry in pool)
            {
                if (entry == null || entry.IsEmpty || entry.MonsterData == null) continue;

                if ((entry.MonsterData.traits & MonsterTraits.Destroyer) != 0)
                {
                    destroyers.Add(entry);
                }
                else if (entry.MonsterData.monsterType == MonsterType.Flying)
                {
                    flying.Add(entry);
                }
                else
                {
                    groundEntries.Add(entry);
                }
            }

            if (groundEntries.Count == 0) return;

            // 지상 몬스터 중 체력 상위 30%를 탱커로 분류
            var sortedByHealth = groundEntries
                .OrderByDescending(e => e.MonsterData.maxHealth)
                .ToList();

            int tankThresholdIndex = Mathf.Max(1, Mathf.CeilToInt(sortedByHealth.Count * 0.3f));

            for (int i = 0; i < sortedByHealth.Count; i++)
            {
                if (i < tankThresholdIndex)
                {
                    tanks.Add(sortedByHealth[i]);
                }
                else
                {
                    normalGround.Add(sortedByHealth[i]);
                }
            }
        }
        #endregion

        #region 전략별 소환 계획 생성
        /// <summary>
        /// Destroyer 보유 시 소환 계획을 생성합니다.
        /// Phase 0: 벽 파괴 지점에 탱커 1마리 선소환
        /// Phase 1: Destroyer 몬스터 소환
        /// Phase 2: 기본전략 - 탱커 소환
        /// Phase 3: 대기 후 나머지 지상
        /// Phase 4: 공중 게릴라
        /// </summary>
        private void BuildPlanWithDestroyers(
            AISpawnPlan plan,
            List<MonsterPoolEntry> destroyers,
            List<MonsterPoolEntry> tanks,
            List<MonsterPoolEntry> normalGround,
            List<MonsterPoolEntry> flying,
            Vector3 groundSpawnPos,
            Vector3 flyingSpawnPos,
            Vector3 destroyerSpawnPos)
        {
            // Phase 0: 벽 파괴 스폰 지점에 탱커 1마리 선소환 (적 주의 분산)
            int phase0TankReserved = 0; // Phase 0에서 예약한 탱커 수
            var phase0 = new AISpawnPhase();
            if (tanks.Count > 0)
            {
                var firstTank = tanks[0];
                phase0TankReserved = Mathf.Min(1, firstTank.RemainingCount);
                if (phase0TankReserved > 0)
                {
                    phase0.Orders.Add(new AISpawnOrder(firstTank, groundSpawnPos, phase0TankReserved));
                }
            }
            if (phase0.Orders.Count > 0)
            {
                plan.Phases.Add(phase0);
            }

            // Phase 1: Destroyer 몬스터 소환 (벽 파괴)
            var phase1 = new AISpawnPhase { DelayBeforePhase = 0.5f };
            foreach (var entry in destroyers)
            {
                if (entry.IsEmpty) continue;
                phase1.Orders.Add(new AISpawnOrder(entry, destroyerSpawnPos, entry.RemainingCount));
            }
            if (phase1.Orders.Count > 0)
            {
                plan.Phases.Add(phase1);
            }

            // Phase 2: 기본전략 - 나머지 탱커 소환
            // ★ Phase 0에서 예약한 수량을 차감
            var phase2 = new AISpawnPhase { DelayBeforePhase = 0.5f };
            for (int i = 0; i < tanks.Count; i++)
            {
                var entry = tanks[i];
                if (entry.IsEmpty) continue;
                int totalAvailable = entry.RemainingCount;
                // 첫 번째 탱커에서는 Phase 0에서 예약한 수만큼 차감
                int reserved = (i == 0) ? phase0TankReserved : 0;
                int remaining = totalAvailable - reserved;
                if (remaining > 0)
                {
                    phase2.Orders.Add(new AISpawnOrder(entry, groundSpawnPos, remaining));
                }
            }
            if (phase2.Orders.Count > 0)
            {
                plan.Phases.Add(phase2);
            }

            // Phase 3: 어그로 대기 후 나머지 지상 몬스터
            var phase3 = new AISpawnPhase { DelayBeforePhase = Random.Range(1.5f, 2.0f) };
            foreach (var entry in normalGround)
            {
                if (entry.IsEmpty) continue;
                phase3.Orders.Add(new AISpawnOrder(entry, groundSpawnPos, entry.RemainingCount));
            }
            if (phase3.Orders.Count > 0)
            {
                plan.Phases.Add(phase3);
            }

            // Phase 4: 공중 게릴라
            var phase4 = new AISpawnPhase { DelayBeforePhase = Random.Range(0.5f, 1.0f) };
            foreach (var entry in flying)
            {
                if (entry.IsEmpty) continue;
                phase4.Orders.Add(new AISpawnOrder(entry, flyingSpawnPos, entry.RemainingCount));
            }
            if (phase4.Orders.Count > 0)
            {
                plan.Phases.Add(phase4);
            }
        }

        /// <summary>
        /// Destroyer 미보유 시 소환 계획을 생성합니다.
        /// Phase 0: 탱커 소환
        /// Phase 1: 대기 후 나머지 지상
        /// Phase 2: 공중 게릴라
        /// </summary>
        private void BuildPlanWithoutDestroyers(
            AISpawnPlan plan,
            List<MonsterPoolEntry> tanks,
            List<MonsterPoolEntry> normalGround,
            List<MonsterPoolEntry> flying,
            Vector3 groundSpawnPos,
            Vector3 flyingSpawnPos)
        {
            // Phase 0: 탱커 선봉 소환
            var phase0 = new AISpawnPhase();
            foreach (var entry in tanks)
            {
                if (entry.IsEmpty) continue;
                phase0.Orders.Add(new AISpawnOrder(entry, groundSpawnPos, entry.RemainingCount));
            }
            if (phase0.Orders.Count > 0)
            {
                plan.Phases.Add(phase0);
            }

            // Phase 1: 어그로 대기 후 나머지 지상
            var phase1 = new AISpawnPhase { DelayBeforePhase = Random.Range(1.5f, 2.0f) };
            foreach (var entry in normalGround)
            {
                if (entry.IsEmpty) continue;
                phase1.Orders.Add(new AISpawnOrder(entry, groundSpawnPos, entry.RemainingCount));
            }
            if (phase1.Orders.Count > 0)
            {
                plan.Phases.Add(phase1);
            }

            // Phase 2: 공중 게릴라
            var phase2 = new AISpawnPhase { DelayBeforePhase = Random.Range(0.5f, 1.5f) };
            foreach (var entry in flying)
            {
                if (entry.IsEmpty) continue;
                phase2.Orders.Add(new AISpawnOrder(entry, flyingSpawnPos, entry.RemainingCount));
            }
            if (phase2.Orders.Count > 0)
            {
                plan.Phases.Add(phase2);
            }
        }
        #endregion

        #region 유틸리티
        /// <summary>
        /// 특정 위치에서 골 지점까지의 A* 경로 길이를 계산합니다.
        /// </summary>
        private int CalculatePathLength(Vector3 spawnWorldPos, bool ignoreBreakableWalls)
        {
            if (_targetGrid == null || _goalTransform == null) return -1;

            Vector2Int startPos = _targetField.WorldToNavigationCell(spawnWorldPos);
            Vector2Int endPos = _targetField.WorldToNavigationCell(_goalTransform.position);

            if (_targetGrid.FindPath(startPos, endPos, ignoreWalls: false, ignoreBreakableWalls: ignoreBreakableWalls))
            {
                return _targetGrid.FinalPath?.Count ?? -1;
            }

            return -1;
        }

        /// <summary>
        /// 폴백 스폰 위치를 반환합니다.
        /// </summary>
        private Vector3 GetFallbackSpawnPos()
        {
            if (_targetField != null)
            {
                return _targetField.GetFallbackOuterSpawnWorldPosition();
            }

            return Vector3.zero;
        }

        /// <summary>
        /// 아무 방향에서든 유효한 스폰 지점 하나를 가져옵니다.
        /// </summary>
        public Vector3 GetAnyValidSpawnPosition()
        {
            if (_validSpawnPositions == null) return GetFallbackSpawnPos();

            foreach (var kvp in _validSpawnPositions)
            {
                if (kvp.Value.Count > 0)
                {
                    return kvp.Value[kvp.Value.Count / 2];
                }
            }

            return GetFallbackSpawnPos();
        }
        #endregion
    }

    #region 소환 계획 데이터 클래스
    /// <summary>
    /// AI의 전체 소환 계획. 여러 페이즈로 구성됩니다.
    /// </summary>
    public class AISpawnPlan
    {
        public List<AISpawnPhase> Phases = new List<AISpawnPhase>();
    }

    /// <summary>
    /// 소환 계획의 한 단계. 여러 소환 명령과 대기 시간으로 구성됩니다.
    /// </summary>
    public class AISpawnPhase
    {
        /// <summary>이 페이즈의 소환 명령 목록</summary>
        public List<AISpawnOrder> Orders = new List<AISpawnOrder>();

        /// <summary>이 페이즈 시작 전 대기 시간(초)</summary>
        public float DelayBeforePhase;
    }

    /// <summary>
    /// 개별 몬스터 소환 명령. 어떤 몬스터를 어디에 몇 마리 소환할지 정의합니다.
    /// </summary>
    public class AISpawnOrder
    {
        /// <summary>소환할 몬스터의 풀 엔트리</summary>
        public MonsterPoolEntry PoolEntry;

        /// <summary>소환 위치 (월드 좌표)</summary>
        public Vector3 SpawnPosition;

        /// <summary>소환할 수량</summary>
        public int Count;

        public AISpawnOrder(MonsterPoolEntry poolEntry, Vector3 spawnPosition, int count)
        {
            PoolEntry = poolEntry;
            SpawnPosition = spawnPosition;
            Count = count;
        }
    }
    #endregion
}
