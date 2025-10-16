using System.Collections.Generic;
using UnityEngine;

public static class MazePlanner
{
    private static readonly Vector2Int[] Dir4 = new[]
    {
        new Vector2Int(1,0), new Vector2Int(-1,0), new Vector2Int(0,1), new Vector2Int(0,-1)
    };

    public static List<Vector3Int> PlanWalls(FieldManager fm, PlayerManager pm)
    {
        Vector3Int spawnCell3 = fm.WorldToGridInt(pm.spawnPoint != null ? pm.spawnPoint.position : Vector3.zero);
        Vector3Int goalCell3 = fm.WorldToGridInt(pm.goalTransform != null ? pm.goalTransform.position : Vector3.zero);
        Vector2Int spawn = new Vector2Int(spawnCell3.x, spawnCell3.y);
        Vector2Int goal = new Vector2Int(goalCell3.x, goalCell3.y);

        int width = fm.gridSize.x;
        int height = fm.gridSize.y;

        var initialBlocked = new HashSet<Vector2Int>();
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var cell = new Vector3Int(x, y, 0);
                if (fm.HasWallAt(cell)) initialBlocked.Add(new Vector2Int(x, y));
            }
        }

        var blocked = new HashSet<Vector2Int>(initialBlocked);
        var path = ComputePath(spawn, goal, width, height, blocked);
        if (path == null || path.Count == 0) return new List<Vector3Int>();

        var pathSet = new HashSet<Vector2Int>(path);
        var plannedOrder = new List<Vector2Int>();

        // Helper to try a placement while keeping connectivity
        bool AddIfSafe(Vector2Int c)
        {
            if (c == spawn || c == goal) return false;
            if (c.x < 0 || c.x >= width || c.y < 0 || c.y >= height) return false;
            if (blocked.Contains(c)) return false;
            blocked.Add(c);
            var testPath = ComputePath(spawn, goal, width, height, blocked);
            if (testPath == null || testPath.Count == 0)
            {
                blocked.Remove(c);
                return false;
            }
            plannedOrder.Add(c);
            // Optionally refresh pathSet to keep padding near the new route
            path = testPath;
            pathSet = new HashSet<Vector2Int>(path);
            return true;
        }

        // 1) Pad the current path by placing walls adjacent to it (funneling)
        foreach (var p in path)
        {
            for (int i = 0; i < Dir4.Length; i++)
            {
                var n = p + Dir4[i];
                if (pathSet.Contains(n)) continue;
                AddIfSafe(n);
            }
        }

        // 2) Add more walls near the path (by distance to path), still preserving connectivity
        var candidates = new List<Vector2Int>();
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var c = new Vector2Int(x, y);
                if (c == spawn || c == goal) continue;
                if (blocked.Contains(c)) continue;
                candidates.Add(c);
            }
        }

        candidates.Sort((a, b) =>
        {
            int da = DistanceToPath(a, path);
            int db = DistanceToPath(b, path);
            int dcmp = da.CompareTo(db); // closer to path first
            if (dcmp != 0) return dcmp;
            int sa = Mathf.Abs(a.x - spawn.x) + Mathf.Abs(a.y - spawn.y);
            int sb = Mathf.Abs(b.x - spawn.x) + Mathf.Abs(b.y - spawn.y);
            return sa.CompareTo(sb);
        });

        foreach (var c in candidates)
        {
            AddIfSafe(c);
        }

        var result = new List<Vector3Int>(plannedOrder.Count);
        foreach (var c in plannedOrder)
        {
            result.Add(new Vector3Int(c.x, c.y, 0));
        }
        return result;
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
