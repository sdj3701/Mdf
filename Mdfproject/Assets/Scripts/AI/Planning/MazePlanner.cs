using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Debug = UnityEngine.Debug;

public static class MazePlanner
{
    private enum CellType { Empty, WallAi, WallInitial }

    private class MazeGenerationResult
    {
        public CellType[,] Grid;
        public List<Vector2Int> FinalPath;
        public List<Vector2Int> AiWalls;
        public Vector2Int Start;
        public Vector2Int Goal;
        public List<Vector2Int> DfsPath;
        public bool AiWallsInBuildOrder;
    }

    private struct MazePlanInput
    {
        public int Width;
        public int Height;
        public HashSet<Vector2Int> InitialWalls;
        public int TargetMinLength;
        public int WallBudget;
        public bool UseFixedEndpoints;
        public Vector2Int FixedStart;
        public Vector2Int FixedGoal;
    }

    private static readonly Vector2Int[] Dir4 = new[]
    {
        new Vector2Int(1,0), new Vector2Int(-1,0), new Vector2Int(0,1), new Vector2Int(0,-1)
    };

    private static readonly SemaphoreSlim PlanningSemaphore = new SemaphoreSlim(1, 1);
    private static int RngSeed = System.Environment.TickCount;

    private const float LongestSearchMs = 200f;
    private const int MaxGenerationAttempts = 80;
    private const int StackallocLimitCells = 4096;

    public class MazePlanResult
    {
        public List<Vector3Int> BuildOrder = new List<Vector3Int>();
        public List<Vector2Int> ValidatedPath = new List<Vector2Int>();
        public HashSet<Vector2Int> BlueprintWalls = new HashSet<Vector2Int>();
        public Vector2Int Start;
        public Vector2Int Goal;
    }

    /// <summary>
    /// Generate a maze once and return the blueprint and build order for the AI.
    /// </summary>
    public static MazePlanResult PlanWalls(FieldManager fm, PlayerManager pm)
    {
        if (fm == null || pm == null)
        {
            Debug.LogWarning("[MazePlanner] PlanWalls called with null references.");
            return new MazePlanResult();
        }

        var input = BuildPlanInput(fm, pm);
        var plan = PlanWallsFromInput(input, log: true);

        // 고정 위치만 사용하므로 AlignSpawnAndGoal 호출 불필요

        return plan;
    }

    public static Task<MazePlanResult> PlanWallsAsync(FieldManager fm, PlayerManager pm)
    {
        return PlanWallsAsyncImpl(fm, pm);
    }

    /// <summary>
    /// Plan additional walls on top of the current maze layout (using the current wall stock budget).
    /// Returns an empty plan when no net path-length increase is possible.
    /// </summary>
    public static Task<MazePlanResult> PlanAdditionalWallsAsync(FieldManager fm, PlayerManager pm)
    {
        return PlanAdditionalWallsAsyncImpl(fm, pm);
    }

    private static MazePlanInput BuildPlanInput(FieldManager fm, PlayerManager pm)
    {
        int width = Mathf.Max(1, fm.gridSize.x);
        int height = Mathf.Max(1, fm.gridSize.y);
        var initialWalls = CollectInitialWalls(fm);
        int freeCells = Mathf.Max(1, width * height - initialWalls.Count);
        int maxPossiblePath = Mathf.Max(1, freeCells - 1);
        int minDesired = Mathf.Min(width + height + 2, maxPossiblePath);
        int targetMinLength = Mathf.Clamp((int)(freeCells * 0.4f), minDesired, maxPossiblePath);

        int wallBudget = Mathf.Max(0, pm.GetWallCount() - pm.GetWallReserveK());

        bool useFixedEndpoints = pm.spawnPoint != null && pm.goalTransform != null;
        Vector2Int fixedStart = Vector2Int.zero;
        Vector2Int fixedGoal = Vector2Int.zero;
        if (useFixedEndpoints)
        {
            var spawnCell = fm.WorldToGridInt(pm.spawnPoint.position);
            var goalCell = fm.WorldToGridInt(pm.goalTransform.position);
            fixedStart = new Vector2Int(spawnCell.x, spawnCell.y);
            fixedGoal = new Vector2Int(goalCell.x, goalCell.y);

            if (fixedStart == fixedGoal)
            {
                Debug.LogWarning($"[MazePlanner] Spawn/Goal overlap at {fixedStart}. Maze may fail.");
            }
            if (initialWalls.Contains(fixedStart) || initialWalls.Contains(fixedGoal))
            {
                Debug.LogWarning($"[MazePlanner] Spawn/Goal is on a wall cell. Start={fixedStart}, Goal={fixedGoal}");
            }
        }
        else
        {
            Debug.LogError("[MazePlanner] Spawn/Goal transforms missing. Cannot plan maze without fixed endpoints.");
        }

        return new MazePlanInput
        {
            Width = width,
            Height = height,
            InitialWalls = initialWalls,
            TargetMinLength = targetMinLength,
            WallBudget = wallBudget,
            UseFixedEndpoints = useFixedEndpoints,
            FixedStart = fixedStart,
            FixedGoal = fixedGoal
        };
    }

    private static async Task<MazePlanResult> PlanWallsAsyncImpl(FieldManager fm, PlayerManager pm)
    {
        if (fm == null || pm == null)
        {
            return new MazePlanResult();
        }

        var input = BuildPlanInput(fm, pm);

        await PlanningSemaphore.WaitAsync();
        try
        {
            return await Task.Run(() => PlanWallsFromInput(input, log: false));
        }
        finally
        {
            PlanningSemaphore.Release();
        }
    }

    private static async Task<MazePlanResult> PlanAdditionalWallsAsyncImpl(FieldManager fm, PlayerManager pm)
    {
        if (fm == null || pm == null)
        {
            return new MazePlanResult();
        }

        var input = BuildPlanInput(fm, pm);

        await PlanningSemaphore.WaitAsync();
        try
        {
            return await Task.Run(() => PlanAdditionalWallsFromInput(input, log: false));
        }
        finally
        {
            PlanningSemaphore.Release();
        }
    }

    private static MazePlanResult PlanWallsFromInput(MazePlanInput input, bool log)
    {
        var plan = new MazePlanResult();
        var rng = CreateRng();
        MazeGenerationResult generation = null;

        if (input.WallBudget <= 0)
        {
            // 고정 위치 사용 (랜덤 폴백 제거됨)
            Vector2Int start = input.FixedStart;
            Vector2Int goal = input.FixedGoal;

            var grid = BuildGrid(input.Width, input.Height, input.InitialWalls, null);
            var path = AStarSearch(grid, start, goal);
            plan.Start = start;
            plan.Goal = goal;
            plan.ValidatedPath = path ?? new List<Vector2Int>();

            if (log)
            {
                Debug.Log($"[MazePlanner] Wall budget is 0. No build order generated. Start={plan.Start}, Goal={plan.Goal}, PathLen={plan.ValidatedPath.Count}");
            }

            return plan;
        }

        // 항상 고정 위치 사용 (랜덤 폴백 제거됨)
        generation = GenerateFlawlessMazeWithFixedEndpoints(
            input.Width,
            input.Height,
            input.InitialWalls,
            input.TargetMinLength,
            rng,
            input.FixedStart,
            input.FixedGoal,
            log);

        if (generation == null && log)
        {
            Debug.LogWarning($"[MazePlanner] Fixed endpoint maze generation failed. Start={input.FixedStart}, Goal={input.FixedGoal}");
        }

        if (generation == null)
        {
            generation = GenerateBudgetedMaze(
                input.Width,
                input.Height,
                input.InitialWalls,
                input.FixedStart,
                input.FixedGoal,
                input.WallBudget,
                rng,
                log);
        }

        if (generation == null)
        {
            if (log)
            {
                Debug.LogWarning("[MazePlanner] Failed to build any maze. Returning empty plan.");
            }
            return plan;
        }

        plan.Start = generation.Start;
        plan.Goal = generation.Goal;
        plan.ValidatedPath = generation.FinalPath ?? new List<Vector2Int>();

        var orderedWalls = generation.AiWallsInBuildOrder
            ? generation.AiWalls
            : PrioritizeWalls(generation, input.InitialWalls, rng, input.WallBudget);

        plan.BlueprintWalls = generation.AiWalls != null
            ? new HashSet<Vector2Int>(generation.AiWalls)
            : new HashSet<Vector2Int>();
        foreach (var cell in orderedWalls)
        {
            plan.BuildOrder.Add(new Vector3Int(cell.x, cell.y, 0));
        }

        if (log)
        {
            Debug.Log($"[MazePlanner] Maze planned. Start={plan.Start}, Goal={plan.Goal}, Walls={plan.BuildOrder.Count}, PathLen={plan.ValidatedPath.Count}");
        }

        return plan;
    }

    private static MazePlanResult PlanAdditionalWallsFromInput(MazePlanInput input, bool log)
    {
        var plan = new MazePlanResult();
        var rng = CreateRng();
        int width = input.Width;
        int height = input.Height;

        // 고정 위치 사용 (랜덤 폴백 제거됨)
        Vector2Int start = input.FixedStart;
        Vector2Int goal = input.FixedGoal;

        if (!IsInside(start, width, height) || input.InitialWalls.Contains(start))
        {
            start = FindFirstEmptyCell(width, height, input.InitialWalls, Vector2Int.zero);
        }
        if (!IsInside(goal, width, height) || input.InitialWalls.Contains(goal) || goal == start)
        {
            goal = FindFirstEmptyCell(width, height, input.InitialWalls, start);
        }

        plan.Start = start;
        plan.Goal = goal;

        if (input.WallBudget <= 0)
        {
            var baseGrid = BuildGrid(width, height, input.InitialWalls, null);
            plan.ValidatedPath = AStarSearch(baseGrid, start, goal) ?? new List<Vector2Int>();
            plan.BlueprintWalls = new HashSet<Vector2Int>();
            return plan;
        }

        var additional = GenerateBudgetedMaze(width, height, input.InitialWalls, start, goal, input.WallBudget, rng, log);
        if (additional == null)
        {
            return plan;
        }

        plan.ValidatedPath = additional.FinalPath ?? new List<Vector2Int>();
        plan.BlueprintWalls = additional.AiWalls != null ? new HashSet<Vector2Int>(additional.AiWalls) : new HashSet<Vector2Int>();

        if (additional.AiWalls != null)
        {
            foreach (var cell in additional.AiWalls)
            {
                plan.BuildOrder.Add(new Vector3Int(cell.x, cell.y, 0));
            }
        }

        if (log)
        {
            Debug.Log($"[MazePlanner] Additional plan. NewWalls={plan.BuildOrder.Count}, PathLen={plan.ValidatedPath.Count}");
        }

        return plan;
    }

    #region Generation Core

    private static MazeGenerationResult GenerateFlawlessMaze(int width, int height, HashSet<Vector2Int> initialWalls, int targetMinLength, System.Random rng, bool log)
    {
        int minDistance = Mathf.Max(4, (width + height) / 3);
        int attempt = 0;

        while (attempt < MaxGenerationAttempts)
        {
            attempt++;

            if (!TryPickStartGoal(width, height, initialWalls, rng, minDistance, out var start, out var goal))
            {
                break;
            }

            var dfsPath = FindStrictLongestPath(start, goal, initialWalls, width, height, rng);
            if (dfsPath == null || dfsPath.Count < targetMinLength)
            {
                continue;
            }

            var optimizedGrid = OptimizeWalls(start, goal, initialWalls, dfsPath, width, height, rng);
            if (optimizedGrid == null) continue;

            var finalPath = AStarSearch(optimizedGrid, start, goal);
            if (finalPath == null) continue;
            if (finalPath.Count < dfsPath.Count) continue;

            var aiWalls = ExtractAiWalls(optimizedGrid);

            if (log)
            {
                Debug.Log($"[MazePlanner] Maze found on attempt {attempt}. Path {dfsPath.Count} -> {finalPath.Count}, AI walls {aiWalls.Count}");
            }

            return new MazeGenerationResult
            {
                Grid = optimizedGrid,
                FinalPath = finalPath,
                DfsPath = dfsPath,
                AiWalls = aiWalls,
                Start = start,
                Goal = goal
            };
        }

        return null;
    }

    private static MazeGenerationResult GenerateFlawlessMazeWithFixedEndpoints(
        int width,
        int height,
        HashSet<Vector2Int> initialWalls,
        int targetMinLength,
        System.Random rng,
        Vector2Int start,
        Vector2Int goal,
        bool log)
    {
        int attempt = 0;
        List<Vector2Int> bestPath = null;

        while (attempt < MaxGenerationAttempts)
        {
            attempt++;

            var dfsPath = FindStrictLongestPath(start, goal, initialWalls, width, height, rng);
            if (dfsPath != null && (bestPath == null || dfsPath.Count > bestPath.Count))
            {
                bestPath = dfsPath;
            }

            if (dfsPath == null || dfsPath.Count < targetMinLength)
            {
                continue;
            }

            var optimizedGrid = OptimizeWalls(start, goal, initialWalls, dfsPath, width, height, rng);
            if (optimizedGrid == null) continue;

            var finalPath = AStarSearch(optimizedGrid, start, goal);
            if (finalPath == null) continue;
            if (finalPath.Count < dfsPath.Count) continue;

            var aiWalls = ExtractAiWalls(optimizedGrid);

            if (log)
            {
                Debug.Log($"[MazePlanner] Fixed maze found on attempt {attempt}. Path {dfsPath.Count} -> {finalPath.Count}, AI walls {aiWalls.Count}");
            }

            return new MazeGenerationResult
            {
                Grid = optimizedGrid,
                FinalPath = finalPath,
                DfsPath = dfsPath,
                AiWalls = aiWalls,
                Start = start,
                Goal = goal
            };
        }

        if (bestPath != null && bestPath.Count > 1)
        {
            var optimizedGrid = OptimizeWalls(start, goal, initialWalls, bestPath, width, height, rng);
            if (optimizedGrid != null)
            {
                var finalPath = AStarSearch(optimizedGrid, start, goal);
                if (finalPath != null && finalPath.Count >= bestPath.Count)
                {
                    var aiWalls = ExtractAiWalls(optimizedGrid);
                    if (log)
                    {
                        Debug.Log($"[MazePlanner] Using best fixed path below target. Path {bestPath.Count} -> {finalPath.Count}, AI walls {aiWalls.Count}");
                    }

                    return new MazeGenerationResult
                    {
                        Grid = optimizedGrid,
                        FinalPath = finalPath,
                        DfsPath = bestPath,
                        AiWalls = aiWalls,
                        Start = start,
                        Goal = goal
                    };
                }
            }
        }

        return null;
    }

    private static MazeGenerationResult GenerateBudgetedMaze(
        int width,
        int height,
        HashSet<Vector2Int> initialWalls,
        Vector2Int start,
        Vector2Int goal,
        int wallBudget,
        System.Random rng,
        bool log)
    {
        wallBudget = Mathf.Max(0, wallBudget);

        var grid = BuildGrid(width, height, initialWalls, null);
        var baselinePath = AStarSearch(grid, start, goal);
        if (baselinePath == null || baselinePath.Count < 2 || wallBudget == 0)
        {
            return new MazeGenerationResult
            {
                Grid = grid,
                FinalPath = baselinePath,
                DfsPath = baselinePath,
                AiWalls = new List<Vector2Int>(),
                AiWallsInBuildOrder = true,
                Start = start,
                Goal = goal
            };
        }

        int baselineLength = baselinePath.Count;
        var currentPath = baselinePath;
        int currentLength = baselineLength;
        var aiWalls = new List<Vector2Int>();

        while (aiWalls.Count < wallBudget)
        {
            if (currentPath == null || currentPath.Count < 3)
            {
                break;
            }

            Vector2Int? best = null;
            int bestIncrease = int.MinValue;
            int bestScore = int.MinValue;

            for (int i = 1; i < currentPath.Count - 1; i++)
            {
                var candidate = currentPath[i];
                if (!IsInside(candidate, width, height)) continue;
                if (initialWalls.Contains(candidate)) continue;
                if (grid[candidate.x, candidate.y] != CellType.Empty) continue;

                grid[candidate.x, candidate.y] = CellType.WallAi;
                int pathLen = ShortestPathLength(grid, start, goal);
                grid[candidate.x, candidate.y] = CellType.Empty;

                if (pathLen < 2)
                {
                    continue;
                }

                int increase = pathLen - currentLength;
                int distToGoal = Heuristic(candidate, goal);
                int distToStart = Heuristic(candidate, start);
                int score = increase * 1000 + (distToGoal + distToStart) * 3;

                if (increase > bestIncrease || (increase == bestIncrease && (score > bestScore || (score == bestScore && rng.Next(2) == 0))))
                {
                    best = candidate;
                    bestIncrease = increase;
                    bestScore = score;
                }
            }

            if (!best.HasValue)
            {
                break;
            }

            // If this is the last wall we can place and it doesn't increase length, don't waste resources.
            int remaining = wallBudget - aiWalls.Count;
            if (bestIncrease <= 0 && remaining <= 1)
            {
                break;
            }

            var chosen = best.Value;
            grid[chosen.x, chosen.y] = CellType.WallAi;
            aiWalls.Add(chosen);
            currentPath = AStarSearch(grid, start, goal) ?? currentPath;
            currentLength = currentPath?.Count ?? currentLength;
        }

        var finalPath = AStarSearch(grid, start, goal) ?? baselinePath;
        int finalLength = finalPath?.Count ?? 0;
        if (finalLength <= baselineLength)
        {
            if (log)
            {
                Debug.LogWarning($"[MazePlanner] Budget plan produced no length gain. Baseline={baselineLength}, Final={finalLength}. Returning empty build order.");
            }

            aiWalls.Clear();
            grid = BuildGrid(width, height, initialWalls, null);
            finalPath = baselinePath;
        }

        if (log)
        {
            Debug.Log($"[MazePlanner] Budget plan built {aiWalls.Count}/{wallBudget} walls. Path {baselineLength} -> {(finalPath?.Count ?? 0)}");
        }

        return new MazeGenerationResult
        {
            Grid = grid,
            FinalPath = finalPath,
            DfsPath = finalPath,
            AiWalls = aiWalls,
            AiWallsInBuildOrder = true,
            Start = start,
            Goal = goal
        };
    }

    private static CellType[,] OptimizeWalls(Vector2Int start, Vector2Int goal, HashSet<Vector2Int> initialWalls, List<Vector2Int> targetPath, int width, int height, System.Random rng)
    {
        var grid = BuildGrid(width, height, initialWalls, null);

        // Fill with AI walls except path/initial
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                if (grid[x, y] == CellType.WallInitial) continue;
                grid[x, y] = CellType.WallAi;
            }
        }

        foreach (var p in targetPath)
        {
            grid[p.x, p.y] = CellType.Empty;
        }

        var wallsToCheck = new List<Vector2Int>();
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                if (grid[x, y] == CellType.WallAi)
                {
                    wallsToCheck.Add(new Vector2Int(x, y));
                }
            }
        }

        ShuffleInPlace(wallsToCheck, rng);
        int targetLen = targetPath.Count;

        foreach (var wall in wallsToCheck)
        {
            grid[wall.x, wall.y] = CellType.Empty;
            int checkLen = ShortestPathLength(grid, start, goal);
            if (checkLen > 0 && checkLen < targetLen)
            {
                grid[wall.x, wall.y] = CellType.WallAi;
            }
        }

        return grid;
    }

    private static MazeGenerationResult BuildFallbackMaze(FieldManager fm, PlayerManager pm, HashSet<Vector2Int> initialWalls)
    {
        int width = Mathf.Max(1, fm.gridSize.x);
        int height = Mathf.Max(1, fm.gridSize.y);

        var start = fm.WorldToGridInt(pm.spawnPoint != null ? pm.spawnPoint.position : Vector3.zero);
        var goal = fm.WorldToGridInt(pm.goalTransform != null ? pm.goalTransform.position : Vector3.zero);
        var start2D = new Vector2Int(start.x, start.y);
        var goal2D = new Vector2Int(goal.x, goal.y);

        if (!IsInside(start2D, width, height) || initialWalls.Contains(start2D))
        {
            start2D = FindFirstEmptyCell(width, height, initialWalls, Vector2Int.zero);
        }

        if (!IsInside(goal2D, width, height) || initialWalls.Contains(goal2D) || goal2D == start2D)
        {
            goal2D = FindFirstEmptyCell(width, height, initialWalls, start2D);
        }

        var grid = BuildGrid(width, height, initialWalls, null);
        var path = AStarSearch(grid, start2D, goal2D) ?? new List<Vector2Int> { start2D, goal2D };

        return new MazeGenerationResult
        {
            Grid = grid,
            FinalPath = path,
            DfsPath = path,
            AiWalls = new List<Vector2Int>(),
            Start = start2D,
            Goal = goal2D
        };
    }

    private static bool TryPickStartGoal(int width, int height, HashSet<Vector2Int> initialWalls, System.Random rng, int minDistance, out Vector2Int start, out Vector2Int goal)
    {
        var cells = new List<Vector2Int>();
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                var cell = new Vector2Int(x, y);
                if (initialWalls.Contains(cell)) continue;
                cells.Add(cell);
            }
        }

        start = Vector2Int.zero;
        goal = Vector2Int.zero;
        if (cells.Count < 2) return false;

        for (int i = 0; i < 100; i++)
        {
            start = cells[rng.Next(cells.Count)];
            goal = cells[rng.Next(cells.Count)];
            if (start == goal) continue;
            int dist = Mathf.Abs(start.x - goal.x) + Mathf.Abs(start.y - goal.y);
            if (dist <= minDistance) continue;
            return true;
        }

        // fallback: pick a far pair even if it violates minDistance
        start = cells[0];
        var anchor = start;
        goal = cells.Skip(1)
            .OrderByDescending(c => Mathf.Abs(anchor.x - c.x) + Mathf.Abs(anchor.y - c.y))
            .FirstOrDefault();
        return start != goal;
    }

    private static List<Vector2Int> FindStrictLongestPath(Vector2Int start, Vector2Int goal, HashSet<Vector2Int> initialWalls, int width, int height, System.Random rng)
    {
        var best = new List<Vector2Int>();
        var visited = new HashSet<Vector2Int> { start };
        var path = new List<Vector2Int> { start };
        var sw = Stopwatch.StartNew();

        void Dfs(Vector2Int current)
        {
            if (sw.ElapsedMilliseconds > LongestSearchMs) return;

            if (current == goal)
            {
                if (path.Count > best.Count)
                {
                    best.Clear();
                    best.AddRange(path);
                }
                return;
            }

            Span<Vector2Int> candidates = stackalloc Vector2Int[4];
            int candidateCount = 0;
            foreach (var dir in Dir4)
            {
                var next = current + dir;
                if (!IsInside(next, width, height)) continue;
                if (visited.Contains(next)) continue;
                if (initialWalls.Contains(next)) continue;

                if (next == goal || IsSafeSpacing(next, current, visited))
                {
                    candidates[candidateCount++] = next;
                }
            }

            ShuffleInPlace(candidates, candidateCount, rng);
            // push goal candidate to the end to encourage detours
            int goalIndex = -1;
            for (int i = 0; i < candidateCount; i++)
            {
                if (candidates[i] == goal)
                {
                    goalIndex = i;
                    break;
                }
            }
            if (goalIndex >= 0 && goalIndex != candidateCount - 1)
            {
                var g = candidates[goalIndex];
                for (int i = goalIndex; i < candidateCount - 1; i++)
                {
                    candidates[i] = candidates[i + 1];
                }
                candidates[candidateCount - 1] = g;
            }

            for (int i = 0; i < candidateCount; i++)
            {
                var next = candidates[i];
                visited.Add(next);
                path.Add(next);
                Dfs(next);
                if (sw.ElapsedMilliseconds > LongestSearchMs) break;
                path.RemoveAt(path.Count - 1);
                visited.Remove(next);
            }
        }

        Dfs(start);
        return best;
    }

    private static bool IsSafeSpacing(Vector2Int target, Vector2Int current, HashSet<Vector2Int> visited)
    {
        foreach (var dir in Dir4)
        {
            var neighbor = target + dir;
            if (visited.Contains(neighbor) && neighbor != current)
            {
                return false;
            }
        }
        return true;
    }

    private static CellType[,] BuildGrid(int width, int height, HashSet<Vector2Int> initialWalls, HashSet<Vector2Int> aiWalls)
    {
        var grid = new CellType[width, height];
        if (initialWalls != null)
        {
            foreach (var wall in initialWalls)
            {
                if (IsInside(wall, width, height))
                {
                    grid[wall.x, wall.y] = CellType.WallInitial;
                }
            }
        }

        if (aiWalls != null)
        {
            foreach (var wall in aiWalls)
            {
                if (IsInside(wall, width, height))
                {
                    grid[wall.x, wall.y] = CellType.WallAi;
                }
            }
        }
        return grid;
    }

    private static List<Vector2Int> ExtractAiWalls(CellType[,] grid)
    {
        var list = new List<Vector2Int>();
        int width = grid.GetLength(0);
        int height = grid.GetLength(1);
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                if (grid[x, y] == CellType.WallAi)
                {
                    list.Add(new Vector2Int(x, y));
                }
            }
        }
        return list;
    }

    #endregion

    #region Pathfinding / Ordering

    private static int ShortestPathLength(CellType[,] grid, Vector2Int start, Vector2Int goal)
    {
        int width = grid.GetLength(0);
        int height = grid.GetLength(1);

        if (!IsInside(start, width, height) || !IsInside(goal, width, height)) return -1;
        if (grid[start.x, start.y] != CellType.Empty || grid[goal.x, goal.y] != CellType.Empty) return -1;
        if (start == goal) return 1;

        int cellCount = width * height;
        Span<byte> visited = cellCount <= StackallocLimitCells ? stackalloc byte[cellCount] : new byte[cellCount];
        Span<int> queue = cellCount <= StackallocLimitCells ? stackalloc int[cellCount] : new int[cellCount];

        int startIndex = start.x + start.y * width;
        int goalIndex = goal.x + goal.y * width;

        int head = 0;
        int tail = 0;
        queue[tail++] = startIndex;
        visited[startIndex] = 1;

        int depth = 0;
        int remainingInLayer = 1;
        int nextLayerCount = 0;

        while (head < tail)
        {
            int current = queue[head++];
            remainingInLayer--;

            if (current == goalIndex)
            {
                return depth + 1;
            }

            int cx = current % width;
            int cy = current / width;

            foreach (var dir in Dir4)
            {
                int nx = cx + dir.x;
                int ny = cy + dir.y;
                if (nx < 0 || nx >= width || ny < 0 || ny >= height) continue;
                if (grid[nx, ny] != CellType.Empty) continue;

                int nextIndex = nx + ny * width;
                if (visited[nextIndex] != 0) continue;

                visited[nextIndex] = 1;
                queue[tail++] = nextIndex;
                nextLayerCount++;
            }

            if (remainingInLayer == 0)
            {
                depth++;
                remainingInLayer = nextLayerCount;
                nextLayerCount = 0;
            }
        }

        return -1;
    }

    private static List<Vector2Int> AStarSearch(CellType[,] grid, Vector2Int start, Vector2Int goal)
    {
        int width = grid.GetLength(0);
        int height = grid.GetLength(1);

        if (!IsInside(start, width, height) || !IsInside(goal, width, height)) return null;
        if (grid[start.x, start.y] != CellType.Empty || grid[goal.x, goal.y] != CellType.Empty) return null;
        if (start == goal) return new List<Vector2Int> { start };

        int cellCount = width * height;
        Span<int> parent = cellCount <= StackallocLimitCells ? stackalloc int[cellCount] : new int[cellCount];
        Span<byte> visited = cellCount <= StackallocLimitCells ? stackalloc byte[cellCount] : new byte[cellCount];
        Span<int> queue = cellCount <= StackallocLimitCells ? stackalloc int[cellCount] : new int[cellCount];

        int startIndex = start.x + start.y * width;
        int goalIndex = goal.x + goal.y * width;

        int head = 0;
        int tail = 0;
        queue[tail++] = startIndex;
        visited[startIndex] = 1;
        parent[startIndex] = startIndex;

        while (head < tail)
        {
            int current = queue[head++];
            if (current == goalIndex) break;

            int cx = current % width;
            int cy = current / width;

            foreach (var dir in Dir4)
            {
                int nx = cx + dir.x;
                int ny = cy + dir.y;
                if (nx < 0 || nx >= width || ny < 0 || ny >= height) continue;
                if (grid[nx, ny] != CellType.Empty) continue;

                int nextIndex = nx + ny * width;
                if (visited[nextIndex] != 0) continue;

                visited[nextIndex] = 1;
                parent[nextIndex] = current;
                queue[tail++] = nextIndex;
            }
        }

        if (visited[goalIndex] == 0) return null;

        int length = 1;
        for (int at = goalIndex; at != startIndex; at = parent[at])
        {
            length++;
        }

        var path = new List<Vector2Int>(length);
        for (int at = goalIndex; ; at = parent[at])
        {
            int x = at % width;
            int y = at / width;
            path.Add(new Vector2Int(x, y));
            if (at == startIndex) break;
        }
        path.Reverse();
        return path;
    }

    private static List<Vector2Int> PrioritizeWalls(MazeGenerationResult generation, HashSet<Vector2Int> initialWalls, System.Random rng, int maxWalls)
    {
        var order = new List<Vector2Int>();
        if (generation == null || generation.AiWalls == null || generation.AiWalls.Count == 0) return order;
        if (maxWalls <= 0) return order;

        var remaining = new HashSet<Vector2Int>(generation.AiWalls);

        var workingGrid = BuildGrid(generation.Grid.GetLength(0), generation.Grid.GetLength(1), initialWalls, null);
        var currentPath = AStarSearch(workingGrid, generation.Start, generation.Goal);
        int currentLength = currentPath?.Count ?? 0;

        while (remaining.Count > 0 && order.Count < maxWalls)
        {
            Vector2Int? best = null;
            int bestIncrease = int.MinValue;
            int bestScore = int.MinValue;

            foreach (var candidate in remaining)
            {
                if (workingGrid[candidate.x, candidate.y] != CellType.Empty) continue;

                workingGrid[candidate.x, candidate.y] = CellType.WallAi;
                int pathLen = ShortestPathLength(workingGrid, generation.Start, generation.Goal);
                workingGrid[candidate.x, candidate.y] = CellType.Empty;

                if (pathLen < 0) continue;

                int increase = pathLen - currentLength;
                int distToGoal = Heuristic(candidate, generation.Goal);
                int distToStart = Heuristic(candidate, generation.Start);
                bool onPath = currentPath != null && currentPath.Contains(candidate);

                int score = increase * 1000 + (distToGoal + distToStart) * 3 + (onPath ? 50 : 0);

                if (increase > bestIncrease || (increase == bestIncrease && (score > bestScore || (score == bestScore && rng.Next(2) == 0))))
                {
                    best = candidate;
                    bestIncrease = increase;
                    bestScore = score;
                }
            }

            if (!best.HasValue)
            {
                // no improvement candidate (shouldn't happen often)
                var fallback = remaining.First();
                order.Add(fallback);
                remaining.Remove(fallback);
                workingGrid[fallback.x, fallback.y] = CellType.WallAi;
                currentPath = AStarSearch(workingGrid, generation.Start, generation.Goal);
                currentLength = currentPath?.Count ?? currentLength;
                continue;
            }

            order.Add(best.Value);
            remaining.Remove(best.Value);
            workingGrid[best.Value.x, best.Value.y] = CellType.WallAi;
            currentPath = AStarSearch(workingGrid, generation.Start, generation.Goal) ?? currentPath;
            currentLength = currentPath?.Count ?? currentLength;
        }

        return order;
    }

    #endregion

    #region Utility

    public static bool RandomizeSpawnAndGoal(FieldManager fm, PlayerManager pm)
    {
        if (fm == null || pm == null) return false;

        int width = Mathf.Max(1, fm.gridSize.x);
        int height = Mathf.Max(1, fm.gridSize.y);
        var initialWalls = CollectInitialWalls(fm);
        var rng = CreateRng();
        int minDistance = Mathf.Max(4, (width + height) / 3);

        int freeCells = Mathf.Max(1, width * height - initialWalls.Count);
        int maxPossiblePath = Mathf.Max(1, freeCells - 1);
        int minDesired = Mathf.Min(width + height + 2, maxPossiblePath);
        int targetMinLength = Mathf.Clamp((int)(freeCells * 0.4f), minDesired, maxPossiblePath);

        Vector2Int? fallbackStart = null;
        Vector2Int? fallbackGoal = null;
        const int MaxStrictChecks = 12;
        int strictChecks = 0;

        for (int attempt = 0; attempt < MaxGenerationAttempts; attempt++)
        {
            if (!TryPickStartGoal(width, height, initialWalls, rng, minDistance, out var start, out var goal))
            {
                break;
            }

            var grid = BuildGrid(width, height, initialWalls, null);
            int pathLen = ShortestPathLength(grid, start, goal);
            if (pathLen > 1)
            {
                if (!fallbackStart.HasValue)
                {
                    fallbackStart = start;
                    fallbackGoal = goal;
                }

                if (strictChecks >= MaxStrictChecks)
                {
                    break;
                }
                strictChecks++;

                var strictPath = FindStrictLongestPath(start, goal, initialWalls, width, height, rng);
                if (strictPath != null && strictPath.Count >= targetMinLength)
                {
                    AlignSpawnAndGoal(pm, fm, start, goal);
                    return true;
                }
            }
        }

        if (fallbackStart.HasValue && fallbackGoal.HasValue)
        {
            AlignSpawnAndGoal(pm, fm, fallbackStart.Value, fallbackGoal.Value);
            return true;
        }

        var fallback = BuildFallbackMaze(fm, pm, initialWalls);
        if (fallback != null)
        {
            AlignSpawnAndGoal(pm, fm, fallback.Start, fallback.Goal);
            return true;
        }

        Debug.LogWarning($"[MazePlanner] Failed to randomize spawn/goal for Player {pm.playerId}");
        return false;
    }

    private static System.Random CreateRng()
    {
        return new System.Random(Interlocked.Increment(ref RngSeed));
    }

    private static HashSet<Vector2Int> CollectInitialWalls(FieldManager fm)
    {
        var set = new HashSet<Vector2Int>();
        for (int y = 0; y < fm.gridSize.y; y++)
        {
            for (int x = 0; x < fm.gridSize.x; x++)
            {
                var cell = new Vector3Int(x, y, 0);
                if (fm.HasWallAt(cell))
                {
                    set.Add(new Vector2Int(x, y));
                }
            }
        }
        return set;
    }

    private static void AlignSpawnAndGoal(PlayerManager pm, FieldManager fm, Vector2Int start, Vector2Int goal)
    {
        if (pm.spawnPoint != null)
        {
            var world = fm.GridToWorld(new Vector3Int(start.x, start.y, 0));
            pm.spawnPoint.position = world;
        }

        if (pm.goalTransform != null)
        {
            var world = fm.GridToWorld(new Vector3Int(goal.x, goal.y, 0));
            pm.goalTransform.position = world;
        }
    }

    private static int Heuristic(Vector2Int a, Vector2Int b)
    {
        return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);
    }

    private static bool IsInside(Vector2Int pos, int width, int height)
    {
        return pos.x >= 0 && pos.x < width && pos.y >= 0 && pos.y < height;
    }

    private static Vector2Int FindFirstEmptyCell(int width, int height, HashSet<Vector2Int> blocked, Vector2Int avoid)
    {
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var cell = new Vector2Int(x, y);
                if (blocked.Contains(cell) || cell == avoid) continue;
                return cell;
            }
        }
        return Vector2Int.zero;
    }

    private static void ShuffleInPlace(List<Vector2Int> list, System.Random rng)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    private static void ShuffleInPlace(Span<Vector2Int> list, int count, System.Random rng)
    {
        for (int i = count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            var temp = list[i];
            list[i] = list[j];
            list[j] = temp;
        }
    }

    #endregion
}
