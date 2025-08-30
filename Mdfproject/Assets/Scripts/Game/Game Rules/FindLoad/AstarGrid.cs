// Assets/Scripts/Game/Game Rules/FindLoad/AstarGrid.cs
using System.Collections.Generic;
using UnityEngine;

public class AstarGrid : MonoBehaviour
{
    [Header("그리드 설정 (로컬 오프셋)")]
    [Tooltip("그리드 오브젝트의 위치(Pivot)를 기준으로 한 왼쪽 아래 경계입니다.")]
    public Vector2Int bottomLeft;
    [Tooltip("그리드 오브젝트의 위치(Pivot)를 기준으로 한 오른쪽 위 경계입니다.")]
    public Vector2Int topRight;
    
    [Header("레이어 및 비용 설정")]
    public LayerMask wallLayers = -1;
    [Tooltip("파괴 가능한 벽 오브젝트들이 속한 레이어를 지정합니다. (예: BreakWall 레이어)")]
    public LayerMask breakableWallLayer;
    public float detectionRadius = 0.4f;
    public int wallBreakCost = 10000;

    [Header("경로 탐색 옵션")]
    public bool allowDiagonal = true;
    public bool dontCrossCorner = false;

    [Header("디버깅")]
    public bool showDebugInfo = true;
    [SerializeField] private Vector2Int debugStartPos, debugTargetPos;

    public List<AstarNode> FinalPath { get; private set; }
    public List<Vector2Int> WallsToBreakInPath { get; private set; }

    private int sizeX, sizeY;
    private AstarNode[,] NodeArray;
    
    // ✅ [추가된 핵심 로직] 런타임에 계산될 실제 월드 좌표 경계
    private Vector2Int worldBottomLeft;
    private Vector2Int worldTopRight;

    private void Awake()
    {
        // ✅ [추가된 핵심 로직]
        // 이 컴포넌트가 깨어날 때, 자신의 월드 위치를 기준으로 실제 경계를 계산합니다.
        // 이렇게 하면 GameManagers가 이 그리드를 어디에 생성하든 항상 올바른 경계를 갖게 됩니다.
        Vector2Int gridOrigin = new Vector2Int(
            Mathf.RoundToInt(transform.position.x),
            Mathf.RoundToInt(transform.position.y)
        );
        worldBottomLeft = gridOrigin + bottomLeft;
        worldTopRight = gridOrigin + topRight;
        
        // 그리드 노드 배열을 처음 생성합니다.
        InitializeGrid();
    }

    public bool FindPath(Vector2Int start, Vector2Int end)
    {
        // 런타임에 벽 정보가 바뀔 수 있으므로, 경로 탐색 시마다 벽 상태를 다시 확인합니다.
        UpdateGridWallStatus();

        if (!IsValidPosition(start) || !IsValidPosition(end))
        {
            Debug.LogError($"[AstarGrid] 시작점({start}) 또는 끝점({end})이 그리드 범위를 벗어났습니다. 그리드 경계: {worldBottomLeft} ~ {worldTopRight}");
            return false;
        }

        AstarNode StartNode = GetNode(start);
        AstarNode TargetNode = GetNode(end);

        List<AstarNode> OpenList = new List<AstarNode>();
        HashSet<AstarNode> ClosedList = new HashSet<AstarNode>();
        
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

    private void InitializeGrid()
    {
        // ✅ [수정] 월드 좌표 경계를 기준으로 크기를 계산합니다.
        sizeX = worldTopRight.x - worldBottomLeft.x + 1;
        sizeY = worldTopRight.y - worldBottomLeft.y + 1;
        NodeArray = new AstarNode[sizeX, sizeY];

        for (int i = 0; i < sizeX; i++)
        {
            for (int j = 0; j < sizeY; j++)
            {
                int x = i + worldBottomLeft.x;
                int y = j + worldBottomLeft.y;
                NodeArray[i, j] = new AstarNode(false, x, y);
            }
        }
    }

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

                if (isWall)
                {
                    // [수정] 벽 중에서, breakableWallLayer에 속한 것만 파괴 가능으로 설정합니다.
                    // 이렇게 하면 'Wall' 레이어는 파괴 불가능 장애물로, 'BreakWall' 레이어는 파괴 가능 장애물로 정확히 구분됩니다.
                    bool isBreakable = Physics2D.OverlapCircle(checkWorldPos, detectionRadius, breakableWallLayer);
                    NodeArray[i, j].isBreakable = isBreakable;
                }
                else
                {
                    NodeArray[i, j].isBreakable = false;
                }
            }
        }
    }


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

                if (NeighborNode.isWall && !NeighborNode.isBreakable) continue;

                if (dontCrossCorner && x != 0 && y != 0)
                {
                    if (GetNode(new Vector2Int(CurNode.x + x, CurNode.y)).isWall || GetNode(new Vector2Int(CurNode.x, CurNode.y + y)).isWall)
                        continue;
                }

                int distanceCost = (x == 0 || y == 0) ? 10 : 14;
                int tentativeGCost = CurNode.G + distanceCost;
                
                if (NeighborNode.isWall)
                {
                    tentativeGCost += wallBreakCost;
                }

                if (tentativeGCost < NeighborNode.G)
                {
                    NeighborNode.ParentNode = CurNode;
                    NeighborNode.G = tentativeGCost;
                    NeighborNode.H = GetManhattanDistance(new Vector2Int(NeighborNode.x, NeighborNode.y), new Vector2Int(TargetNode.x, TargetNode.y));

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
        // ✅ [수정] 월드 좌표 경계와 비교합니다.
        return pos.x >= worldBottomLeft.x && pos.x <= worldTopRight.x &&
               pos.y >= worldBottomLeft.y && pos.y <= worldTopRight.y;
    }

    private AstarNode GetNode(Vector2Int pos)
    {
        if (!IsValidPosition(pos)) return null;
        // ✅ [수정] 월드 좌표를 배열 인덱스로 변환합니다.
        return NodeArray[pos.x - worldBottomLeft.x, pos.y - worldBottomLeft.y];
    }
    
    [ContextMenu("디버그 경로 탐색 실행")]
    private void PathFindingForDebug()
    {
        // 디버깅 시에는 Awake가 호출된 후의 월드 좌표를 사용해야 합니다.
        if (NodeArray == null) Awake(); // 에디터에서 바로 실행 시 Awake 호출
        FindPath(debugStartPos, debugTargetPos);
    }

    void OnDrawGizmos()
    {
        if (!showDebugInfo) return;
        
        // ✅ [수정] 월드 좌표 경계를 기준으로 기즈모를 그립니다.
        Vector2Int bottomLeftGizmo = Application.isPlaying ? worldBottomLeft : new Vector2Int(Mathf.RoundToInt(transform.position.x), Mathf.RoundToInt(transform.position.y)) + bottomLeft;
        Vector2Int topRightGizmo = Application.isPlaying ? worldTopRight : new Vector2Int(Mathf.RoundToInt(transform.position.x), Mathf.RoundToInt(transform.position.y)) + topRight;

        Gizmos.color = Color.cyan;
        Vector3 center = new Vector3(bottomLeftGizmo.x + (topRightGizmo.x - bottomLeftGizmo.x) / 2f + 0.5f, bottomLeftGizmo.y + (topRightGizmo.y - bottomLeftGizmo.y) / 2f + 0.5f, 0);
        Vector3 size = new Vector3(topRightGizmo.x - bottomLeftGizmo.x + 1, topRightGizmo.y - bottomLeftGizmo.y + 1, 0);
        Gizmos.DrawWireCube(center, size);

        if (NodeArray == null) return;

        for (int i = 0; i < sizeX; i++)
        {
            for (int j = 0; j < sizeY; j++)
            {
                if (NodeArray[i, j].isWall)
                {
                    Vector3 pos = new Vector3(NodeArray[i,j].x + 0.5f, NodeArray[i,j].y + 0.5f, 0);
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
