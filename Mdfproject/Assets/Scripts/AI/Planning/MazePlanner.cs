using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public static class MazePlanner
{
    private static readonly Vector2Int[] Dir4 = new[]
    {
        new Vector2Int(1,0), new Vector2Int(-1,0), new Vector2Int(0,1), new Vector2Int(0,-1)
    };

    /// <summary>
    /// 미로 설계: 최소한의 벽으로 몬스터 경로를 최대한 길게 만드는 벽 배치 계획을 생성합니다.
    /// 전략: Zigzag Maze Builder - 직선 경로를 지그재그로 만들어 경로 길이를 극대화
    /// </summary>
    public static List<Vector3Int> PlanWalls(FieldManager fm, PlayerManager pm)
    {
        Debug.LogWarning($"[MazePlanner] ===== 미로 계획 시작 (Player {pm.playerId}) =====");

        Vector3Int spawnCell3 = fm.WorldToGridInt(pm.spawnPoint != null ? pm.spawnPoint.position : Vector3.zero);
        Vector3Int goalCell3 = fm.WorldToGridInt(pm.goalTransform != null ? pm.goalTransform.position : Vector3.zero);
        Vector2Int spawn = new Vector2Int(spawnCell3.x, spawnCell3.y);
        Vector2Int goal = new Vector2Int(goalCell3.x, goalCell3.y);

        Debug.LogWarning($"[MazePlanner] 스폰: {spawn}, 골: {goal}");

        int width = fm.gridSize.x;
        int height = fm.gridSize.y;

        Debug.LogWarning($"[MazePlanner] 그리드 크기: {width}x{height}");

        // 초기 벽 수집
        var initialBlocked = new HashSet<Vector2Int>();
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var cell = new Vector3Int(x, y, 0);
                if (fm.HasWallAt(cell)) initialBlocked.Add(new Vector2Int(x, y));
            }
        }

        Debug.LogWarning($"[MazePlanner] 초기 벽 개수: {initialBlocked.Count}");

        // 초기 경로 계산
        var blocked = new HashSet<Vector2Int>(initialBlocked);
        var currentPath = ComputePath(spawn, goal, width, height, blocked);
        if (currentPath == null || currentPath.Count == 0)
        {
            Debug.LogWarning("[MazePlanner] 초기 경로를 찾을 수 없습니다. 미로 계획 실패.");
            return new List<Vector3Int>();
        }

        int initialPathLength = currentPath.Count;
        Debug.LogWarning($"[MazePlanner] 초기 경로 길이: {initialPathLength}");

        // 새로운 전략: Beam Search로 최적 미로 찾기
        var result = BuildOptimalMaze(spawn, goal, width, height, blocked, initialBlocked, initialPathLength);

        Debug.LogWarning($"[MazePlanner] ===== 미로 계획 반환 완료 =====");
        return result;
    }

    /// <summary>
    /// Beam Search로 최적 미로를 찾습니다.
    /// 여러 벽 조합을 시도하여 경로 길이가 가장 긴 조합을 선택합니다.
    /// </summary>
    private static List<Vector3Int> BuildOptimalMaze(Vector2Int spawn, Vector2Int goal, int width, int height,
        HashSet<Vector2Int> blocked, HashSet<Vector2Int> initialBlocked, int initialPathLength)
    {
        // 1단계: 모든 유효한 벽 후보 수집
        var allCandidates = new List<Vector2Int>();
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var pos = new Vector2Int(x, y);
                if (pos == spawn || pos == goal) continue;
                if (initialBlocked.Contains(pos)) continue;
                if (!IsValidPosition(pos, width, height)) continue;
                allCandidates.Add(pos);
            }
        }

        Debug.LogWarning($"[MazePlanner] 벽 후보 개수: {allCandidates.Count}");

        // 2단계: 각 벽의 독립적인 효과 평가 (초기 경로 기준)
        var wallEffects = new List<WallCandidate>();
        foreach (var candidate in allCandidates)
        {
            blocked.Add(candidate);
            var testPath = ComputePath(spawn, goal, width, height, blocked);
            blocked.Remove(candidate);

            if (testPath != null && testPath.Count > 0)
            {
                int pathIncrease = testPath.Count - initialPathLength;
                int distToSpawn = Mathf.Abs(candidate.x - spawn.x) + Mathf.Abs(candidate.y - spawn.y);

                wallEffects.Add(new WallCandidate
                {
                    position = candidate,
                    pathLengthIncrease = pathIncrease,
                    distanceToSpawn = distToSpawn
                });
            }
        }

        // 효과 순으로 정렬
        wallEffects.Sort((a, b) =>
        {
            int effectCmp = b.pathLengthIncrease.CompareTo(a.pathLengthIncrease);
            if (effectCmp != 0) return effectCmp;
            return a.distanceToSpawn.CompareTo(b.distanceToSpawn);
        });

        Debug.LogWarning($"[MazePlanner] 유효한 벽 후보: {wallEffects.Count}");

        // 3단계: Simulated Annealing - 지역 최적해를 탈출하여 더 나은 해 탐색
        int targetWalls = Mathf.Min(width + height, 15);
        Debug.LogWarning($"[MazePlanner] Simulated Annealing 시작: 목표 {targetWalls}개 벽");

        // 초기 해: Greedy로 몇 개 벽 추가
        var currentSolution = new List<Vector2Int>();
        var currentBlocked = new HashSet<Vector2Int>(initialBlocked);

        // Phase 1: Greedy로 초기 해 생성 (목표의 절반)
        for (int i = 0; i < targetWalls / 2; i++)
        {
            Vector2Int? bestWall = null;
            int bestGreedyPathLength = 0;

            foreach (var candidate in allCandidates)
            {
                if (currentBlocked.Contains(candidate)) continue;

                currentBlocked.Add(candidate);
                var testPath = ComputePath(spawn, goal, width, height, currentBlocked);
                currentBlocked.Remove(candidate);

                if (testPath != null && testPath.Count > bestGreedyPathLength)
                {
                    bestGreedyPathLength = testPath.Count;
                    bestWall = candidate;
                }
            }

            if (!bestWall.HasValue) break;

            currentBlocked.Add(bestWall.Value);
            currentSolution.Add(bestWall.Value);
        }

        var currentPath = ComputePath(spawn, goal, width, height, currentBlocked);
        int currentPathLength = currentPath?.Count ?? initialPathLength;

        Debug.LogWarning($"[MazePlanner] Greedy 초기 해: {currentSolution.Count}개 벽, 경로: {currentPathLength}");

        // Phase 2: Simulated Annealing으로 개선 (강화된 파라미터)
        var bestSolution = new List<Vector2Int>(currentSolution);
        var bestBlocked = new HashSet<Vector2Int>(currentBlocked);
        int bestPathLength = currentPathLength;

        float temperature = 200f;  // 초기 온도 증가 (더 많은 탐색)
        float coolingRate = 0.97f;  // 냉각 속도 감소 (더 오래 탐색)
        int iterations = 500;  // 반복 횟수 증가

        var random = new System.Random();

        for (int iter = 0; iter < iterations && temperature > 0.5f; iter++)
        {
            // 이웃 해 생성 전략 개선: 2~3개 벽을 동시에 교체
            if (currentSolution.Count == 0) break;

            int numSwaps = random.Next(1, 4);  // 1~3개 벽 교체
            var removedWalls = new List<Vector2Int>();
            var addedWalls = new List<Vector2Int>();

            // 무작위로 벽 제거
            for (int s = 0; s < numSwaps && currentSolution.Count > 0; s++)
            {
                int removeIdx = random.Next(currentSolution.Count);
                var removedWall = currentSolution[removeIdx];
                currentSolution.RemoveAt(removeIdx);
                currentBlocked.Remove(removedWall);
                removedWalls.Add(removedWall);
            }

            // 무작위로 새 벽 추가
            var availableCandidates = new List<Vector2Int>();
            foreach (var candidate in allCandidates)
            {
                if (!currentBlocked.Contains(candidate))
                {
                    availableCandidates.Add(candidate);
                }
            }

            for (int s = 0; s < numSwaps && availableCandidates.Count > 0; s++)
            {
                int idx = random.Next(availableCandidates.Count);
                var newWall = availableCandidates[idx];
                availableCandidates.RemoveAt(idx);
                currentBlocked.Add(newWall);
                currentSolution.Add(newWall);
                addedWalls.Add(newWall);
            }

            if (addedWalls.Count > 0)
            {

                // 새 해 평가
                var newPath = ComputePath(spawn, goal, width, height, currentBlocked);
                int newPathLength = newPath?.Count ?? 0;

                // 경로가 막히면 거부
                if (newPath == null || newPathLength == 0)
                {
                    // 복구
                    foreach (var wall in addedWalls)
                    {
                        currentBlocked.Remove(wall);
                        currentSolution.Remove(wall);
                    }
                    foreach (var wall in removedWalls)
                    {
                        currentBlocked.Add(wall);
                        currentSolution.Add(wall);
                    }
                }
                else
                {
                    // Acceptance 확률 계산
                    int delta = newPathLength - currentPathLength;
                    bool accept = false;

                    if (delta > 0)
                    {
                        accept = true; // 개선되면 항상 수락
                    }
                    else
                    {
                        // 나빠져도 확률적으로 수락 (지역 최적해 탈출)
                        float probability = Mathf.Exp(delta / temperature);
                        accept = (float)random.NextDouble() < probability;
                    }

                    if (accept)
                    {
                        currentPathLength = newPathLength;
                        currentPath = newPath;

                        // 최선의 해 업데이트
                        if (newPathLength > bestPathLength)
                        {
                            bestPathLength = newPathLength;
                            bestSolution = new List<Vector2Int>(currentSolution);
                            bestBlocked = new HashSet<Vector2Int>(currentBlocked);
                            Debug.LogWarning($"[MazePlanner] 새로운 최선 해: {bestSolution.Count}개 벽, 경로: {bestPathLength} (+{bestPathLength - initialPathLength})");
                        }
                    }
                    else
                    {
                        // 거부: 원래대로 복구
                        foreach (var wall in addedWalls)
                        {
                            currentBlocked.Remove(wall);
                            currentSolution.Remove(wall);
                        }
                        foreach (var wall in removedWalls)
                        {
                            currentBlocked.Add(wall);
                            currentSolution.Add(wall);
                        }
                    }
                }
            }
            else
            {
                // 추가할 후보가 없으면 복구
                foreach (var wall in removedWalls)
                {
                    currentBlocked.Add(wall);
                    currentSolution.Add(wall);
                }
            }

            temperature *= coolingRate;
        }

        currentSolution = bestSolution;
        currentBlocked = new HashSet<Vector2Int>(bestBlocked);
        currentPath = ComputePath(spawn, goal, width, height, currentBlocked);
        currentPathLength = currentPath?.Count ?? bestPathLength;

        Debug.LogWarning($"[MazePlanner] SA 완료: {currentSolution.Count}개 벽, 경로: {currentPathLength}");

        // Phase 3: 남은 슬롯을 채우기 (조건 완화)
        int remainingSlots = targetWalls - currentSolution.Count;
        if (remainingSlots > 0)
        {
            Debug.LogWarning($"[MazePlanner] 남은 {remainingSlots}개 슬롯 채우기 시작");

            // 3-1: 먼저 경로를 길게 만드는 벽 추가
            for (int i = 0; i < remainingSlots; i++)
            {
                Vector2Int? bestWall = null;
                int bestPhase3PathLength = currentPathLength;

                foreach (var candidate in allCandidates)
                {
                    if (currentBlocked.Contains(candidate)) continue;

                    currentBlocked.Add(candidate);
                    var testPath = ComputePath(spawn, goal, width, height, currentBlocked);
                    currentBlocked.Remove(candidate);

                    if (testPath != null && testPath.Count > bestPhase3PathLength)
                    {
                        bestPhase3PathLength = testPath.Count;
                        bestWall = candidate;
                    }
                }

                if (!bestWall.HasValue) break;

                currentBlocked.Add(bestWall.Value);
                currentSolution.Add(bestWall.Value);
                currentPath = ComputePath(spawn, goal, width, height, currentBlocked);
                currentPathLength = currentPath?.Count ?? currentPathLength;

                Debug.LogWarning($"[MazePlanner] 개선 벽 #{currentSolution.Count}: {bestWall.Value}, 경로: {currentPathLength} (+{currentPathLength - initialPathLength})");
            }

            // 3-2: 남은 슬롯을 경로 주변 벽으로 채우기 (경로 길이 유지)
            remainingSlots = targetWalls - currentSolution.Count;
            if (remainingSlots > 0)
            {
                Debug.LogWarning($"[MazePlanner] 남은 {remainingSlots}개 슬롯을 경로 주변 벽으로 채우기");

                // 현재 경로 주변 영역 계산
                var pathNeighborhood = new HashSet<Vector2Int>();
                if (currentPath != null)
                {
                    foreach (var node in currentPath)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            for (int dy = -1; dy <= 1; dy++)
                            {
                                pathNeighborhood.Add(new Vector2Int(node.x + dx, node.y + dy));
                            }
                        }
                    }
                }

                // 경로 주변 후보 수집
                var nearPathCandidates = new List<Vector2Int>();
                foreach (var candidate in allCandidates)
                {
                    if (currentBlocked.Contains(candidate)) continue;
                    if (pathNeighborhood.Contains(candidate))
                    {
                        nearPathCandidates.Add(candidate);
                    }
                }

                // 경로를 막지 않는 벽 추가
                foreach (var candidate in nearPathCandidates)
                {
                    if (currentSolution.Count >= targetWalls) break;

                    currentBlocked.Add(candidate);
                    var testPath = ComputePath(spawn, goal, width, height, currentBlocked);

                    if (testPath != null && testPath.Count >= currentPathLength)
                    {
                        currentSolution.Add(candidate);
                        currentPath = testPath;
                        currentPathLength = testPath.Count;
                        Debug.LogWarning($"[MazePlanner] 밀도 벽 #{currentSolution.Count}: {candidate}, 경로: {currentPathLength}");
                    }
                    else
                    {
                        currentBlocked.Remove(candidate);
                    }
                }
            }
        }

        Debug.LogWarning($"[MazePlanner] ===== 미로 설계 완료 =====");
        Debug.LogWarning($"[MazePlanner] 계획된 벽 개수: {currentSolution.Count}");
        Debug.LogWarning($"[MazePlanner] 경로 길이: {initialPathLength} -> {currentPathLength} (증가: {currentPathLength - initialPathLength})");

        if (currentSolution.Count > 0)
        {
            Debug.LogWarning($"[MazePlanner] 첫 번째 벽: {currentSolution[0]}, 마지막 벽: {currentSolution[currentSolution.Count - 1]}");
        }

        // 스폰에 가까운 순으로 정렬 (건설 순서)
        currentSolution.Sort((a, b) =>
        {
            int distA = Mathf.Abs(a.x - spawn.x) + Mathf.Abs(a.y - spawn.y);
            int distB = Mathf.Abs(b.x - spawn.x) + Mathf.Abs(b.y - spawn.y);
            return distA.CompareTo(distB);
        });

        // Vector3Int로 변환하여 반환
        var result = new List<Vector3Int>(currentSolution.Count);
        foreach (var pos in currentSolution)
        {
            result.Add(new Vector3Int(pos.x, pos.y, 0));
        }

        return result;
    }

    private class WallCandidate
    {
        public Vector2Int position;
        public int pathLengthIncrease;
        public int distanceToSpawn;
    }

    private static bool IsValidPosition(Vector2Int pos, int width, int height)
    {
        return pos.x >= 0 && pos.x < width && pos.y >= 0 && pos.y < height;
    }

    private static int DistanceToPath(Vector2Int c, List<Vector2Int> path)
    {
        int best = int.MaxValue;
        for (int i = 0; i < path.Count; i++)
        {
            var p = path[i];
            int d = Mathf.Abs(c.x - p.x) + Mathf.Abs(c.y - p.y);
            if (d < best) best = d;
            if (best == 0) break;
        }
        return best == int.MaxValue ? 99999 : best;
    }

    private static List<Vector2Int> ComputePath(Vector2Int start, Vector2Int goal, int width, int height, HashSet<Vector2Int> blocked)
    {
        if (start == goal) return new List<Vector2Int> { start };
        var q = new Queue<Vector2Int>();
        var prev = new Dictionary<Vector2Int, Vector2Int>();
        var seen = new HashSet<Vector2Int>();
        q.Enqueue(start);
        seen.Add(start);
        while (q.Count > 0)
        {
            var cur = q.Dequeue();
            for (int i = 0; i < Dir4.Length; i++)
            {
                var nxt = cur + Dir4[i];
                if (nxt.x < 0 || nxt.x >= width || nxt.y < 0 || nxt.y >= height) continue;
                if (blocked.Contains(nxt)) continue;
                if (!seen.Add(nxt)) continue;
                prev[nxt] = cur;
                if (nxt == goal)
                {
                    var path = new List<Vector2Int>();
                    var t = nxt;
                    while (t != start)
                    {
                        path.Add(t);
                        t = prev[t];
                    }
                    path.Add(start);
                    path.Reverse();
                    return path;
                }
                q.Enqueue(nxt);
            }
        }
        return null;
    }
}
