// Assets/Scripts/Game/Game Rules/FindLoad/AstarGrid.cs
using System.Collections.Generic;
using UnityEngine;

public class AstarGrid : MonoBehaviour
{
    [Header("그리드 설정 (로컬 오프셋)")]
    [Tooltip("그리드 오브젝트의 위치(Pivot)를 기준으로 한 왼쪽 아래 경계입니다. (X, Z 좌표)")]
    public Vector2Int bottomLeft;
    [Tooltip("그리드 오브젝트의 위치(Pivot)를 기준으로 한 오른쪽 위 경계입니다. (X, Z 좌표)")]
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
    public List<AstarNode> IdealPathForAIDebug { get; set; } // AI 디버깅용 경로
    public List<Vector2Int> WallsToBreakInPath { get; private set; }

    private int sizeX, sizeY;
    private AstarNode[,] NodeArray;

    // ✅ [추가된 핵심 로직] 런타임에 계산될 실제 월드 좌표 경계
    private Vector2Int worldBottomLeft;
    private Vector2Int worldTopRight;

    // ✅ [수정] Awake에서 public Initialize로 변경
    // [3D Migration] transform.position.z를 사용하여 3D 공간의 x, z 좌표로 초기화
    public void Initialize()
    {
        // 이 컴포넌트가 깨어날 때, 자신의 월드 위치를 기준으로 실제 경계를 계산합니다.
        // Vector2Int의 x는 3D의 x, y는 3D의 z를 의미합니다.
        Vector2Int gridOrigin = new Vector2Int(
            Mathf.FloorToInt(transform.position.x),
            Mathf.FloorToInt(transform.position.z)  // [3D Migration] y → z
        );
        worldBottomLeft = gridOrigin + bottomLeft;
        worldTopRight = gridOrigin + topRight;

        // 그리드 노드 배열을 처음 생성합니다.
        InitializeGrid();
    }

    public bool FindPath(Vector2Int start, Vector2Int end, bool ignoreWalls = false)
    {
        // 런타임에 벽 정보가 바뀔 수 있으므로, 경로 탐색 시마다 벽 상태를 다시 확인합니다.
        // 벽을 무시하는 경우, 이 업데이트를 건너뛰어 성능을 최적화하고 그리드를 '깨끗한' 상태로 둡니다.
        if (!ignoreWalls)
        {
            UpdateGridWallStatus();
        }

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

            ExploreNeighbors(CurNode, TargetNode, OpenList, ClosedList, ignoreWalls);
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
                // [3D Migration] NodeArray[i,j].x는 3D의 x, .y는 3D의 z를 의미
                // Physics2D는 유닛이 2D를 사용하므로 그대로 유지 (x, z를 x, y로 매핑)
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


    private void ExploreNeighbors(AstarNode CurNode, AstarNode TargetNode, List<AstarNode> OpenList, HashSet<AstarNode> ClosedList, bool ignoreWalls)
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

                if (!ignoreWalls && NeighborNode.isWall && !NeighborNode.isBreakable) continue;

                if (!ignoreWalls && dontCrossCorner && x != 0 && y != 0)
                {
                    if (GetNode(new Vector2Int(CurNode.x + x, CurNode.y)).isWall || GetNode(new Vector2Int(CurNode.x, CurNode.y + y)).isWall)
                        continue;
                }

                int distanceCost = (x == 0 || y == 0) ? 10 : 14;
                int tentativeGCost = CurNode.G + distanceCost;

                if (!ignoreWalls && NeighborNode.isWall)
                {
                    tentativeGCost += wallBreakCost;
                }

                // 이웃 노드까지의 새로운 G 비용이 기존보다 저렴하거나, OpenList에 아직 없다면 정보를 갱신합니다.
                bool inOpenList = OpenList.Contains(NeighborNode);
                if (tentativeGCost < NeighborNode.G || !inOpenList)
                {
                    NeighborNode.ParentNode = CurNode;
                    NeighborNode.G = tentativeGCost;
                    NeighborNode.H = GetManhattanDistance(new Vector2Int(NeighborNode.x, NeighborNode.y), new Vector2Int(TargetNode.x, TargetNode.y));

                    if (!inOpenList)
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
        if (NodeArray == null) Initialize(); // 에디터에서 바로 실행 시 Initialize 호출
        FindPath(debugStartPos, debugTargetPos, false);
    }

    void OnDrawGizmos()
    {
        if (!showDebugInfo) return;

        // ✅ [수정] 월드 좌표 경계를 기준으로 기즈모를 그립니다.
        // [3D Migration] Vector2Int의 x는 3D의 x, y는 3D의 z
        Vector2Int bottomLeftGizmo = Application.isPlaying ? worldBottomLeft : new Vector2Int(Mathf.RoundToInt(transform.position.x), Mathf.RoundToInt(transform.position.z)) + bottomLeft;
        Vector2Int topRightGizmo = Application.isPlaying ? worldTopRight : new Vector2Int(Mathf.RoundToInt(transform.position.x), Mathf.RoundToInt(transform.position.z)) + topRight;

        Gizmos.color = Color.cyan;
        // [3D Migration] Y축은 0으로 고정, Z축 사용
        Vector3 center = new Vector3(
            bottomLeftGizmo.x + (topRightGizmo.x - bottomLeftGizmo.x) / 2f + 0.5f,
            0,
            bottomLeftGizmo.y + (topRightGizmo.y - bottomLeftGizmo.y) / 2f + 0.5f
        );
        Vector3 size = new Vector3(
            topRightGizmo.x - bottomLeftGizmo.x + 1,
            0.1f,  // Y축 두께
            topRightGizmo.y - bottomLeftGizmo.y + 1
        );
        Gizmos.DrawWireCube(center, size);

        if (NodeArray == null) return;

        for (int i = 0; i < sizeX; i++)
        {
            for (int j = 0; j < sizeY; j++)
            {
                if (NodeArray[i, j].isWall)
                {
                    // [3D Migration] x는 그대로, y는 z로
                    Vector3 pos = new Vector3(NodeArray[i,j].x + 0.5f, 0, NodeArray[i,j].y + 0.5f);
                    Gizmos.color = NodeArray[i, j].isBreakable ? new Color(1f, 0.5f, 0f, 0.7f) : new Color(1f, 0f, 0f, 0.7f);
                    Gizmos.DrawCube(pos, new Vector3(0.8f, 0.1f, 0.8f));
                }
            }
        }

        if (FinalPath != null && FinalPath.Count > 0)
        {
            Gizmos.color = Color.green; // 몬스터의 현재 실제 경로
            for (int i = 0; i < FinalPath.Count - 1; i++)
            {
                // [3D Migration] x는 그대로, y는 z로, Y축은 0.1로 살짝 띄움
                Vector3 from = new Vector3(FinalPath[i].x + 0.5f, 0.1f, FinalPath[i].y + 0.5f);
                Vector3 to = new Vector3(FinalPath[i + 1].x + 0.5f, 0.1f, FinalPath[i + 1].y + 0.5f);
                Gizmos.DrawLine(from, to);
            }
        }

        // AI가 계획 중인 이상적인 경로를 별도의 색상으로 표시합니다.
        if (IdealPathForAIDebug != null && IdealPathForAIDebug.Count > 0)
        {
            Gizmos.color = Color.magenta; // AI가 참고하는 이상적인 경로
            for (int i = 0; i < IdealPathForAIDebug.Count - 1; i++)
            {
                // [3D Migration] x는 그대로, y는 z로, Y축은 0.2로 더 띄움
                Vector3 from = new Vector3(IdealPathForAIDebug[i].x + 0.5f, 0.2f, IdealPathForAIDebug[i].y + 0.5f);
                Vector3 to = new Vector3(IdealPathForAIDebug[i + 1].x + 0.5f, 0.2f, IdealPathForAIDebug[i + 1].y + 0.5f);
                Gizmos.DrawLine(from, to);
            }
        }
    }


    #endregion
}
