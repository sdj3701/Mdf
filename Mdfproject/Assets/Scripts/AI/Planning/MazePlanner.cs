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
    /// 전략: 출발 지점 근처부터 시작하여 경로를 최대한 길게 만드는 전략적 벽 배치
    /// </summary>
    public static List<Vector3Int> PlanWalls(FieldManager fm, PlayerManager pm)
    {
        Debug.Log($"[MazePlanner] ===== 미로 계획 시작 (Player {pm.playerId}) =====");

        Vector3Int spawnCell3 = fm.WorldToGridInt(pm.spawnPoint != null ? pm.spawnPoint.position : Vector3.zero);
        Vector3Int goalCell3 = fm.WorldToGridInt(pm.goalTransform != null ? pm.goalTransform.position : Vector3.zero);
        Vector2Int spawn = new Vector2Int(spawnCell3.x, spawnCell3.y);
        Vector2Int goal = new Vector2Int(goalCell3.x, goalCell3.y);

        Debug.Log($"[MazePlanner] 스폰: {spawn}, 골: {goal}");

        int width = fm.gridSize.x;
        int height = fm.gridSize.y;

        // 플레이어가 보유한 벽 개수에서 비상용 예비 벽을 뺀 개수만큼 건설
        int totalWallCount = pm.GetWallCount(); // 플레이어가 보유한 총 벽 개수
        int reserveWalls = pm.GetWallReserveK(); // 비상용으로 남겨둘 벽 개수
        int maxWallsToBuild = totalWallCount - reserveWalls; // 실제로 건설할 벽 개수

        Debug.Log($"[MazePlanner] 그리드 크기: {width}x{height}, 보유 벽: {totalWallCount}개, 예비: {reserveWalls}개, 건설 목표: {maxWallsToBuild}개");

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

        Debug.Log($"[MazePlanner] 초기 벽 개수: {initialBlocked.Count}");

        // 초기 경로 계산
        var blocked = new HashSet<Vector2Int>(initialBlocked);
        var currentPath = ComputePath(spawn, goal, width, height, blocked);
        if (currentPath == null || currentPath.Count == 0)
        {
            Debug.LogWarning("[MazePlanner] 초기 경로를 찾을 수 없습니다. 미로 계획 실패.");
            return new List<Vector3Int>();
        }

        int initialPathLength = currentPath.Count;
        Debug.Log($"[MazePlanner] 초기 경로 길이: {initialPathLength}");

        // 새로운 전략: 최소 벽으로 최대 효율
        var result = BuildOptimalMaze(spawn, goal, width, height, blocked, initialBlocked, initialPathLength, maxWallsToBuild);

        Debug.Log($"[MazePlanner] ===== 미로 계획 반환 완료 =====");
        return result;
    }

    /// <summary>
    /// 최소한의 벽으로 경로를 최대한 길게 만드는 전략적 미로를 생성합니다.
    /// Greedy 방식으로 각 벽이 경로 길이를 최대한 증가시키는 위치를 선택합니다.
    /// 출발 지점 근처부터 시작하여 벽을 배치합니다.
    /// </summary>
    private static List<Vector3Int> BuildOptimalMaze(Vector2Int spawn, Vector2Int goal, int width, int height,
        HashSet<Vector2Int> blocked, HashSet<Vector2Int> initialBlocked, int initialPathLength, int maxWallsToBuild)
    {
        var solution = new List<Vector2Int>();
        var currentBlocked = new HashSet<Vector2Int>(initialBlocked);

        // 현재 경로
        var currentPath = ComputePath(spawn, goal, width, height, currentBlocked);
        int currentPathLength = currentPath?.Count ?? initialPathLength;

        Debug.Log($"[MazePlanner] 초기 경로 길이: {currentPathLength}");
        Debug.Log($"[MazePlanner] 초기 벽 개수: {initialBlocked.Count}개");

        // 모든 가능한 벽 후보 수집
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

        Debug.Log($"[MazePlanner] 벽 후보 개수: {allCandidates.Count}, 신규 건설 목표: {maxWallsToBuild}개");

        // Greedy 전략: 매번 경로를 가장 많이 늘리는 벽을 선택
        // 목표 개수만큼 건설하되, 필드 벽이 15개 넘으면 효율성 체크
        int noImprovementCount = 0;
        const int EFFICIENCY_CHECK_THRESHOLD = 15; // 이 개수 이상부터 효율성 체크
        const int MAX_NO_IMPROVEMENT = 3; // 3번 연속 개선 없으면 중단

        while (solution.Count < maxWallsToBuild)
        {
            Vector2Int? bestWall = null;
            int bestPathLength = currentPathLength;
            float bestScore = float.MinValue;

            // 모든 후보 중에서 최선의 벽 찾기
            foreach (var candidate in allCandidates)
            {
                if (currentBlocked.Contains(candidate)) continue;

                // 테스트: 이 위치에 벽을 세웠을 때
                currentBlocked.Add(candidate);
                var testPath = ComputePath(spawn, goal, width, height, currentBlocked);
                currentBlocked.Remove(candidate);

                // 경로가 막히면 스킵
                if (testPath == null || testPath.Count == 0)
                    continue;

                int pathIncrease = testPath.Count - currentPathLength;

                // 점수 계산: 경로 증가량이 주요 기준
                float score = pathIncrease * 1000f;

                // 보조 기준 1: 출발 지점과의 거리 (가까울수록 높은 점수)
                int distToSpawn = Mathf.Abs(candidate.x - spawn.x) + Mathf.Abs(candidate.y - spawn.y);
                score -= distToSpawn * 5f; // 출발지에서 멀수록 페널티

                // 보조 기준 2: 인접성 보너스 (가급적 이어붙이기)
                if (solution.Count > 0)
                {
                    int minDistToExisting = int.MaxValue;
                    foreach (var existingWall in solution)
                    {
                        int dist = Mathf.Abs(candidate.x - existingWall.x) + Mathf.Abs(candidate.y - existingWall.y);
                        if (dist < minDistToExisting)
                            minDistToExisting = dist;
                    }

                    // 인접(거리 1)이면 보너스, 멀어질수록 페널티
                    if (minDistToExisting == 1)
                        score += 100f; // 인접 보너스
                    else if (minDistToExisting == 2)
                        score += 50f; // 대각선 인접
                    else
                        score -= minDistToExisting * 10f; // 거리 페널티 (하지만 경로 증가가 크면 상쇄됨)
                }

                // 보조 기준 3: 경로 상에 있으면 추가 보너스
                if (currentPath != null && currentPath.Contains(candidate))
                {
                    score += 200f;
                }

                if (testPath.Count > bestPathLength ||
                    (testPath.Count == bestPathLength && score > bestScore))
                {
                    bestPathLength = testPath.Count;
                    bestScore = score;
                    bestWall = candidate;
                }
            }

            // 최선의 벽을 찾았는지 확인
            if (!bestWall.HasValue)
            {
                Debug.LogWarning($"[MazePlanner] 더 이상 유효한 벽 후보가 없습니다. (현재: {solution.Count}/{maxWallsToBuild})");
                break;
            }

            int improvement = bestPathLength - currentPathLength;

            // 벽 배치
            solution.Add(bestWall.Value);
            currentBlocked.Add(bestWall.Value);
            currentPath = ComputePath(spawn, goal, width, height, currentBlocked);
            currentPathLength = currentPath?.Count ?? currentPathLength;

            int distFromSpawn = Mathf.Abs(bestWall.Value.x - spawn.x) + Mathf.Abs(bestWall.Value.y - spawn.y);
            int totalWallsNow = initialBlocked.Count + solution.Count;

            // 효율성 체크: 필드 벽이 15개 이상일 때만
            if (totalWallsNow >= EFFICIENCY_CHECK_THRESHOLD)
            {
                if (improvement <= 0)
                {
                    noImprovementCount++;
                    Debug.Log($"[MazePlanner] 벽 #{solution.Count}/{maxWallsToBuild}: {bestWall.Value} (출발지 거리: {distFromSpawn}), 경로: {currentPathLength} (개선없음 {noImprovementCount}/{MAX_NO_IMPROVEMENT}), 점수: {bestScore:F1}");

                    if (noImprovementCount >= MAX_NO_IMPROVEMENT)
                    {
                        Debug.LogWarning($"[MazePlanner] 필드 벽 {totalWallsNow}개 상태에서 {MAX_NO_IMPROVEMENT}번 연속 개선 없음. 효율성을 위해 중단합니다.");
                        break;
                    }
                }
                else
                {
                    noImprovementCount = 0; // 개선되면 리셋
                    Debug.Log($"[MazePlanner] 벽 #{solution.Count}/{maxWallsToBuild}: {bestWall.Value} (출발지 거리: {distFromSpawn}), 경로: {currentPathLength} (+{improvement}), 점수: {bestScore:F1}");
                }
            }
            else
            {
                // 15개 미만일 때는 효율성 체크 없이 계속 건설
                if (improvement > 0)
                {
                    Debug.Log($"[MazePlanner] 벽 #{solution.Count}/{maxWallsToBuild}: {bestWall.Value} (출발지 거리: {distFromSpawn}), 경로: {currentPathLength} (+{improvement}), 점수: {bestScore:F1}");
                }
                else
                {
                    Debug.Log($"[MazePlanner] 벽 #{solution.Count}/{maxWallsToBuild}: {bestWall.Value} (출발지 거리: {distFromSpawn}), 경로: {currentPathLength} (개선없음), 점수: {bestScore:F1}");
                }
            }
        }

        // 최종 경로 확인
        var finalPath = ComputePath(spawn, goal, width, height, currentBlocked);
        int finalPathLength = finalPath?.Count ?? 0;
        int totalWallsInField = initialBlocked.Count + solution.Count;

        Debug.Log($"[MazePlanner] ===== 미로 설계 완료 =====");
        Debug.Log($"[MazePlanner] 초기 벽: {initialBlocked.Count}개, 신규 건설: {solution.Count}개, 필드 총 벽: {totalWallsInField}개");
        Debug.Log($"[MazePlanner] 경로 길이: {initialPathLength} -> {finalPathLength} (증가: {finalPathLength - initialPathLength})");
        Debug.Log($"[MazePlanner] 효율성: {(finalPathLength - initialPathLength) / (float)Mathf.Max(1, solution.Count):F2} (경로증가/신규벽개수)");

        // Vector3Int로 변환하여 반환
        var result = new List<Vector3Int>(solution.Count);
        foreach (var pos in solution)
        {
            result.Add(new Vector3Int(pos.x, pos.y, 0));
        }

        return result;
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
