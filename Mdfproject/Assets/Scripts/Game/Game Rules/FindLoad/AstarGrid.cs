// Assets/Scripts/Game/Game Rules/FindLoad/AstarGrid.cs
using System.Collections.Generic;
using UnityEngine;

public class AstarGrid : MonoBehaviour
{
    [Header("그리드 설정")]
    public Vector2Int bottomLeft;
    public Vector2Int topRight;
    public LayerMask wallLayers = -1;
    public float detectionRadius = 0.4f;

    [Header("경로 탐색 옵션")]
    public bool allowDiagonal = true;
    public bool dontCrossCorner = false;
    [Tooltip("부술 수 있는 벽을 통과할 때 추가되는 비용. 일반적인 이동 비용(10 또는 14)보다 훨씬 높아야 합니다.")]
    public int wallBreakCost = 10000;

    [Header("디버깅")]
    public bool showDebugInfo = true;
    [SerializeField] private Vector2Int debugStartPos, debugTargetPos;

    public List<AstarNode> FinalPath { get; private set; }
    public List<Vector2Int> WallsToBreakInPath { get; private set; }

    private int sizeX, sizeY;
    private AstarNode[,] NodeArray;

    private void Awake()
    {
        // 게임 시작 시 그리드를 한 번 생성하여 NodeArray를 초기화합니다.
        InitializeGrid();
    }

    public bool FindPath(Vector2Int start, Vector2Int end)
    {
        // 런타임에 벽 정보가 바뀔 수 있으므로, 경로 탐색 시마다 벽 상태를 다시 확인합니다.
        UpdateGridWallStatus();

        if (!IsValidPosition(start) || !IsValidPosition(end))
        {
            Debug.LogError($"[AstarGrid] 시작점({start}) 또는 끝점({end})이 그리드 범위를 벗어났습니다.");
            return false;
        }

        AstarNode StartNode = GetNode(start);
        AstarNode TargetNode = GetNode(end);

        List<AstarNode> OpenList = new List<AstarNode>();
        HashSet<AstarNode> ClosedList = new HashSet<AstarNode>();
        
        // G-cost 및 부모 노드 초기화
        for (int i = 0; i < sizeX; i++)
        {
            for (int j = 0; j < sizeY; j++)
            {
                NodeArray[i, j].G = int.MaxValue;
                NodeArray[i, j].ParentNode = null;
            }
        }
        
        StartNode.G = 0;
        StartNode.H = GetManhattanDistance(start, end);
        OpenList.Add(StartNode);
        
        while (OpenList.Count > 0)
        {
            AstarNode CurNode = OpenList[0];
            for (int i = 1; i < OpenList.Count; i++)
            {
                if (OpenList[i].F < CurNode.F || (OpenList[i].F == CurNode.F && OpenList[i].H < CurNode.H))
                {
                    CurNode = OpenList[i];
                }
            }

            OpenList.Remove(CurNode);
            ClosedList.Add(CurNode);

            if (CurNode == TargetNode)
            {
                BuildFinalPath(StartNode, TargetNode);
                return true;
            }
            
            ExploreNeighbors(CurNode, TargetNode, OpenList, ClosedList);
        }

        Debug.LogWarning($"[AstarGrid] 경로를 찾을 수 없습니다: {start} -> {end}");
        FinalPath = null;
        WallsToBreakInPath = null;
        return false;
    }

    // 그리드 노드 배열을 처음 생성합니다.
    private void InitializeGrid()
    {
        sizeX = topRight.x - bottomLeft.x + 1;
        sizeY = topRight.y - bottomLeft.y + 1;
        NodeArray = new AstarNode[sizeX, sizeY];

        for (int i = 0; i < sizeX; i++)
        {
            for (int j = 0; j < sizeY; j++)
            {
                int x = i + bottomLeft.x;
                int y = j + bottomLeft.y;
                NodeArray[i, j] = new AstarNode(false, x, y);
            }
        }
    }

    // 경로 탐색 직전에 호출되어, 현재 씬의 벽 상태를 그리드에 업데이트합니다.
    private void UpdateGridWallStatus()
    {
        if (NodeArray == null) InitializeGrid();

        for (int i = 0; i < sizeX; i++)
        {
            for (int j = 0; j < sizeY; j++)
            {
                Vector2 checkWorldPos = new Vector2(NodeArray[i, j].x + 0.5f, NodeArray[i, j].y + 0.5f);
                bool isWall = Physics2D.OverlapCircle(checkWorldPos, detectionRadius, wallLayers);
                NodeArray[i, j].isWall = isWall;
                NodeArray[i, j].isBreakable = isWall && IsBreakableWall(checkWorldPos);
            }
        }
    }

    private bool IsBreakableWall(Vector2 worldPos)
    {
        Collider2D[] colliders = Physics2D.OverlapCircleAll(worldPos, detectionRadius, wallLayers);
        foreach (var col in colliders)
        {
            if (col.GetComponent<DestructibleWall>() != null) return true;
        }
        return false;
    }

    // ✅ [수정된 최종 로직] 이웃 노드를 탐색하고 비용을 정확하게 계산하여 업데이트합니다.
    private void ExploreNeighbors(AstarNode CurNode, AstarNode TargetNode, List<AstarNode> OpenList, HashSet<AstarNode> ClosedList)
    {
        for (int x = -1; x <= 1; x++)
        {
            for (int y = -1; y <= 1; y++)
            {
                if (x == 0 && y == 0) continue;
                if (!allowDiagonal && x != 0 && y != 0) continue;

                Vector2Int neighborPos = new Vector2Int(CurNode.x + x, CurNode.y + y);
                if (!IsValidPosition(neighborPos)) continue;

                AstarNode NeighborNode = GetNode(neighborPos);
                if (ClosedList.Contains(NeighborNode)) continue;

                // 부술 수 없는 벽이면 완전히 무시합니다.
                if (NeighborNode.isWall && !NeighborNode.isBreakable) continue;

                // 코너를 가로지르는 것을 방지하는 로직
                if (dontCrossCorner && x != 0 && y != 0)
                {
                    if (GetNode(new Vector2Int(CurNode.x + x, CurNode.y)).isWall || GetNode(new Vector2Int(CurNode.x, CurNode.y + y)).isWall)
                        continue;
                }

                // 1. 현재 노드를 거쳐 이웃 노드로 가는 G-cost를 계산합니다.
                int distanceCost = (x == 0 || y == 0) ? 10 : 14;
                int tentativeGCost = CurNode.G + distanceCost;
                
                // 만약 이웃이 부숴야 하는 벽이라면, 막대한 패널티 비용을 추가합니다.
                if (NeighborNode.isWall)
                {
                    tentativeGCost += wallBreakCost;
                }

                // 2. 이 경로가 기존에 알려진 경로보다 더 효율적인지 확인합니다.
                if (tentativeGCost < NeighborNode.G)
                {
                    // 3. 더 효율적이라면, 이웃 노드의 정보를 업데이트합니다.
                    NeighborNode.ParentNode = CurNode;
                    NeighborNode.G = tentativeGCost;
                    NeighborNode.H = GetManhattanDistance(new Vector2Int(NeighborNode.x, NeighborNode.y), new Vector2Int(TargetNode.x, TargetNode.y));

                    // 4. 이웃 노드가 OpenList에 없다면 추가합니다.
                    if (!OpenList.Contains(NeighborNode))
                    {
                        OpenList.Add(NeighborNode);
                    }
                }
            }
        }
    }

    private void BuildFinalPath(AstarNode startNode, AstarNode endNode)
    {
        FinalPath = new List<AstarNode>();
        WallsToBreakInPath = new List<Vector2Int>();
        AstarNode currentNode = endNode;

        while (currentNode != startNode)
        {
            FinalPath.Add(currentNode);
            if (currentNode.isWall)
            {
                WallsToBreakInPath.Add(new Vector2Int(currentNode.x, currentNode.y));
            }
            currentNode = currentNode.ParentNode;
        }
        FinalPath.Add(startNode);
        
        FinalPath.Reverse();
        WallsToBreakInPath.Reverse();
    }

    private int GetManhattanDistance(Vector2Int a, Vector2Int b)
    {
        return (Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y)) * 10;
    }

    #region 유틸리티 메서드
    private bool IsValidPosition(Vector2Int pos)
    {
        return pos.x >= bottomLeft.x && pos.x <= topRight.x &&
               pos.y >= bottomLeft.y && pos.y <= topRight.y;
    }

    private AstarNode GetNode(Vector2Int pos)
    {
        if (!IsValidPosition(pos)) return null;
        return NodeArray[pos.x - bottomLeft.x, pos.y - bottomLeft.y];
    }
    
    [ContextMenu("디버그 경로 탐색 실행")]
    private void PathFindingForDebug()
    {
        FindPath(debugStartPos, debugTargetPos);
    }

    void OnDrawGizmos()
    {
        if (!showDebugInfo) return;
        
        Gizmos.color = Color.cyan;
        Vector3 center = new Vector3(bottomLeft.x + (topRight.x - bottomLeft.x) / 2f + 0.5f, bottomLeft.y + (topRight.y - bottomLeft.y) / 2f + 0.5f, 0);
        Vector3 size = new Vector3(topRight.x - bottomLeft.x + 1, topRight.y - bottomLeft.y + 1, 0);
        Gizmos.DrawWireCube(center, size);

        if (NodeArray == null) return;

        for (int i = 0; i < sizeX; i++)
        {
            for (int j = 0; j < sizeY; j++)
            {
                if (NodeArray[i, j].isWall)
                {
                    Vector3 pos = new Vector3(i + bottomLeft.x + 0.5f, j + bottomLeft.y + 0.5f, 0);
                    Gizmos.color = NodeArray[i, j].isBreakable ? new Color(1f, 0.5f, 0f, 0.7f) : new Color(1f, 0f, 0f, 0.7f); 
                    Gizmos.DrawCube(pos, Vector3.one * 0.8f);
                }
            }
        }

        if (FinalPath != null && FinalPath.Count > 0)
        {
            Gizmos.color = Color.green;
            for (int i = 0; i < FinalPath.Count - 1; i++)
            {
                Vector3 from = new Vector3(FinalPath[i].x + 0.5f, FinalPath[i].y + 0.5f, 0);
                Vector3 to = new Vector3(FinalPath[i + 1].x + 0.5f, FinalPath[i + 1].y + 0.5f, 0);
                Gizmos.DrawLine(from, to);
            }
        }
    }
    #endregion
}