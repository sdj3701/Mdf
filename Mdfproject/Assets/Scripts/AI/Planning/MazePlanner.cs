using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;
using Debug = UnityEngine.Debug;

public static class MazePlanner
{
    private enum CellType { Empty, WallAi, WallInitial }

    private class PathNode
    {
        public Vector2Int Position;
        public PathNode Parent;
        public int G;
        public int H;
        public int F => G + H;
    }

    private class MazeGenerationResult
    {
        public CellType[,] Grid;
        public List<Vector2Int> FinalPath;
        public List<Vector2Int> AiWalls;
        public Vector2Int Start;
        public Vector2Int Goal;
        public List<Vector2Int> DfsPath;
    }

    private static readonly Vector2Int[] Dir4 = new[]
    {
        new Vector2Int(1,0), new Vector2Int(-1,0), new Vector2Int(0,1), new Vector2Int(0,-1)
    };

    private const float LongestSearchMs = 200f;
    private const int MaxGenerationAttempts = 80;

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
        var plan = new MazePlanResult();
        if (fm == null || pm == null)
        {
            Debug.LogWarning("[MazePlanner] PlanWalls called with null references.");
            return plan;
        }

        int width = Mathf.Max(1, fm.gridSize.x);
        int height = Mathf.Max(1, fm.gridSize.y);
        var rng = new System.Random();

        var initialWalls = CollectInitialWalls(fm);
        int freeCells = Mathf.Max(1, width * height - initialWalls.Count);
        int maxPossiblePath = Mathf.Max(1, freeCells - 1);
        int minDesired = Mathf.Min(width + height + 2, maxPossiblePath);
        int targetMinLength = Mathf.Clamp((int)(freeCells * 0.4f), minDesired, maxPossiblePath);

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
            Debug.LogWarning("[MazePlanner] Spawn/Goal transforms missing. Falling back to random endpoints.");
        }

        MazeGenerationResult generation = null;
        if (useFixedEndpoints)
        {
            generation = GenerateFlawlessMazeWithFixedEndpoints(width, height, initialWalls, targetMinLength, rng, fixedStart, fixedGoal);
            if (generation == null)
            {
                Debug.LogWarning($"[MazePlanner] Fixed endpoint maze generation failed. Start={fixedStart}, Goal={fixedGoal}");
                generation = BuildFallbackMazeFixed(width, height, initialWalls, fixedStart, fixedGoal);
            }
        }
        else
        {
            generation = GenerateFlawlessMaze(width, height, initialWalls, targetMinLength, rng);
            if (generation == null)
            {
                Debug.LogWarning("[MazePlanner] Strict maze generation failed, using fallback path.");
                generation = BuildFallbackMaze(fm, pm, initialWalls);
            }
        }

        if (generation == null)
        {
            Debug.LogWarning("[MazePlanner] Failed to build any maze. Returning empty plan.");
            return plan;
        }

        if (!useFixedEndpoints)
        {
            AlignSpawnAndGoal(pm, fm, generation.Start, generation.Goal);
        }

        plan.Start = generation.Start;
        plan.Goal = generation.Goal;
        plan.ValidatedPath = generation.FinalPath ?? new List<Vector2Int>();
        plan.BlueprintWalls = new HashSet<Vector2Int>(generation.AiWalls);

        var orderedWalls = PrioritizeWalls(generation, initialWalls, rng);
        foreach (var cell in orderedWalls)
        {
            plan.BuildOrder.Add(new Vector3Int(cell.x, cell.y, 0));
        }

        Debug.Log($"[MazePlanner] Maze planned. Start={plan.Start}, Goal={plan.Goal}, Walls={plan.BuildOrder.Count}, PathLen={plan.ValidatedPath.Count}");
        return plan;
    }

    #region Generation Core

    private static MazeGenerationResult GenerateFlawlessMaze(int width, int height, HashSet<Vector2Int> initialWalls, int targetMinLength, System.Random rng)
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

            Debug.Log($"[MazePlanner] Maze found on attempt {attempt}. Path {dfsPath.Count} -> {finalPath.Count}, AI walls {aiWalls.Count}");

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
        Vector2Int goal)
    {
        int attempt = 0;

        while (attempt < MaxGenerationAttempts)
        {
            attempt++;

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

            Debug.Log($"[MazePlanner] Fixed maze found on attempt {attempt}. Path {dfsPath.Count} -> {finalPath.Count}, AI walls {aiWalls.Count}");

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
            var checkPath = AStarSearch(grid, start, goal);
            if (checkPath != null && checkPath.Count < targetLen)
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

    private static MazeGenerationResult BuildFallbackMazeFixed(int width, int height, HashSet<Vector2Int> initialWalls, Vector2Int start, Vector2Int goal)
    {
        var grid = BuildGrid(width, height, initialWalls, null);
        var path = AStarSearch(grid, start, goal) ?? new List<Vector2Int> { start, goal };

        return new MazeGenerationResult
        {
            Grid = grid,
            FinalPath = path,
            DfsPath = path,
            AiWalls = new List<Vector2Int>(),
            Start = start,
            Goal = goal
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

        return false;
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
                    best = new List<Vector2Int>(path);
                }
                return;
            }

            var candidates = new List<Vector2Int>();
            foreach (var dir in Dir4)
            {
                var next = current + dir;
                if (!IsInside(next, width, height)) continue;
                if (visited.Contains(next)) continue;
                if (initialWalls.Contains(next)) continue;

                if (next == goal || IsSafeSpacing(next, current, visited))
                {
                    candidates.Add(next);
                }
            }

            ShuffleInPlace(candidates, rng);
            // push goal candidate to the end to encourage detours
            int goalIndex = candidates.FindIndex(c => c == goal);
            if (goalIndex >= 0)
            {
                var g = candidates[goalIndex];
                candidates.RemoveAt(goalIndex);
                candidates.Add(g);
            }

            foreach (var next in candidates)
            {
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

    private static List<Vector2Int> AStarSearch(CellType[,] grid, Vector2Int start, Vector2Int goal)
    {
        int width = grid.GetLength(0);
        int height = grid.GetLength(1);

        if (!IsInside(start, width, height) || !IsInside(goal, width, height)) return null;

        var open = new List<PathNode>();
        var closed = new HashSet<Vector2Int>();
        var startNode = new PathNode
        {
            Position = start,
            G = 0,
            H = Heuristic(start, goal)
        };
        open.Add(startNode);

        while (open.Count > 0)
        {
            var current = open[0];
            for (int i = 1; i < open.Count; i++)
            {
                if (open[i].F < current.F || (open[i].F == current.F && open[i].H < current.H))
                {
                    current = open[i];
                }
            }

            open.Remove(current);
            closed.Add(current.Position);

            if (current.Position == goal)
            {
                var path = new List<Vector2Int>();
                var node = current;
                while (node != null)
                {
                    path.Add(node.Position);
                    node = node.Parent;
                }
                path.Reverse();
                return path;
            }

            foreach (var dir in Dir4)
            {
                var nextPos = current.Position + dir;
                if (!IsInside(nextPos, width, height)) continue;
                if (grid[nextPos.x, nextPos.y] != CellType.Empty) continue;
                if (closed.Contains(nextPos)) continue;

                int tentativeG = current.G + 1;
                var existing = open.FirstOrDefault(n => n.Position == nextPos);
                if (existing == null)
                {
                    open.Add(new PathNode
                    {
                        Position = nextPos,
                        Parent = current,
                        G = tentativeG,
                        H = Heuristic(nextPos, goal)
                    });
                }
                else if (tentativeG < existing.G)
                {
                    existing.G = tentativeG;
                    existing.Parent = current;
                }
            }
        }

        return null;
    }

    private static List<Vector2Int> PrioritizeWalls(MazeGenerationResult generation, HashSet<Vector2Int> initialWalls, System.Random rng)
    {
        var order = new List<Vector2Int>();
        var remaining = new HashSet<Vector2Int>(generation.AiWalls);

        var workingGrid = BuildGrid(generation.Grid.GetLength(0), generation.Grid.GetLength(1), initialWalls, null);
        var currentPath = AStarSearch(workingGrid, generation.Start, generation.Goal);
        int currentLength = currentPath?.Count ?? 0;

        while (remaining.Count > 0)
        {
            Vector2Int? best = null;
            int bestIncrease = int.MinValue;
            int bestScore = int.MinValue;
            List<Vector2Int> bestPath = null;

            foreach (var candidate in remaining)
            {
                if (workingGrid[candidate.x, candidate.y] != CellType.Empty) continue;

                workingGrid[candidate.x, candidate.y] = CellType.WallAi;
                var path = AStarSearch(workingGrid, generation.Start, generation.Goal);
                workingGrid[candidate.x, candidate.y] = CellType.Empty;

                if (path == null) continue;

                int increase = path.Count - currentLength;
                int distToGoal = Heuristic(candidate, generation.Goal);
                int distToStart = Heuristic(candidate, generation.Start);
                bool onPath = currentPath != null && currentPath.Contains(candidate);

                int score = increase * 1000 + (distToGoal + distToStart) * 3 + (onPath ? 50 : 0);

                if (increase > bestIncrease || (increase == bestIncrease && (score > bestScore || (score == bestScore && rng.Next(2) == 0))))
                {
                    best = candidate;
                    bestIncrease = increase;
                    bestScore = score;
                    bestPath = path;
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
            currentPath = bestPath ?? currentPath;
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
        var rng = new System.Random();
        int minDistance = Mathf.Max(3, (width + height) / 4);

        for (int attempt = 0; attempt < MaxGenerationAttempts; attempt++)
        {
            if (!TryPickStartGoal(width, height, initialWalls, rng, minDistance, out var start, out var goal))
            {
                break;
            }

            var grid = BuildGrid(width, height, initialWalls, null);
            var path = AStarSearch(grid, start, goal);
            if (path != null && path.Count > 1)
            {
                AlignSpawnAndGoal(pm, fm, start, goal);
                return true;
            }
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

    #endregion
}
