// Assets/Scripts/Game/Game Rules/FindLoad/AstarGrid.cs
using System.Collections.Generic;
using UnityEngine;

public class AstarGrid : MonoBehaviour
{
    [Header("그리드 설정 (원점 + 크기, X/Z)")]
    [Tooltip("그리드 오브젝트의 위치(Pivot)에서 시작하여 +X(오른쪽), +Z(위)로 전개됩니다.")]
    public Vector2Int gridSize = new Vector2Int(10, 10);
    [Tooltip("Pivot로부터의 로컬 오프셋 (시작 셀). 보통 (0,0).")]
    public Vector2Int startOffset = Vector2Int.zero;

    [Header("레이어 및 비용 설정")]
    public LayerMask wallLayers = -1;
    [Tooltip("파괴 가능한 벽 오브젝트들이 속한 레이어를 지정합니다. (예: BreakWall 레이어)")]
    public LayerMask breakableWallLayer;
    public float detectionRadius = 0.4f;
    public int wallBreakCost = 10000;
    [Tooltip("벽 감지 샘플의 월드 Y 높이 (그리드 셀 중심의 Y)")]
    public float sampleY = 0.5f;
    [Tooltip("벽 감지 시 사용할 세로(높이) 두께. CheckBox의 전체 높이 값입니다.")]
    public float sampleHeight = 2f;

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
    private bool initialized = false;

    // ✅ [수정] Awake에서 public Initialize로 변경 (필요 시 외부에서도 재초기화 가능)
    private void Awake()
    {
        if (!initialized)
            Initialize();
    }

    private void OnValidate()
    {
        if (gridSize.x < 1) gridSize.x = 1;
        if (gridSize.y < 1) gridSize.y = 1;
    }
    // [3D Migration] Pivot 기준 원점에서 +X/+Z로 전개
    public void Initialize()
    {
        // 자신의 월드 위치를 기준으로 실제 경계를 계산합니다. (x=월드 X, y=월드 Z)
        Vector2Int origin = new Vector2Int(
            Mathf.FloorToInt(transform.position.x),
            Mathf.FloorToInt(transform.position.z)
        ) + startOffset;

        // 오른쪽(+X), 위(+Z)로 전개
        worldBottomLeft = origin;
        worldTopRight = origin + new Vector2Int(gridSize.x - 1, gridSize.y - 1);

        // 그리드 노드 배열을 처음 생성합니다.
        InitializeGrid();
        initialized = true;
    }

    public bool FindPath(Vector2Int start, Vector2Int end, bool ignoreWalls = false)
    {
        // 런타임에 벽 정보가 바뀔 수 있으므로, 경로 탐색 시마다 벽 상태를 다시 확인합니다.
        // 벽을 무시하는 경우, 이 업데이斯特를 건너뛰어 성능을 최적화하고 그리드를 '깨끗한' 상태로 둡니다.
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
                // [3D Migration] NodeArray[i,j].x -> world X, NodeArray[i,j].y -> world Z
                // 세로 두께를 가진 박스 콜리전으로 감지 (Y 고정이더라도 벽이 떠있을 수 있으므로)
                Vector3 checkWorldPos = new Vector3(NodeArray[i, j].x + 0.5f, sampleY, NodeArray[i, j].y + 0.5f);
                Vector3 halfExtents = new Vector3(detectionRadius, Mathf.Max(0.01f, sampleHeight * 0.5f), detectionRadius);
                bool isWall = Physics.CheckBox(checkWorldPos, halfExtents, Quaternion.identity, wallLayers);
                NodeArray[i, j].isWall = isWall;

                if (isWall)
                {
                    // 벽 중에서 breakableWallLayer에 속한 것만 파괴 가능으로 설정
                    bool isBreakable = Physics.CheckBox(checkWorldPos, halfExtents, Quaternion.identity, breakableWallLayer);
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
    /// <summary>
    /// 주어진 월드 좌표를 그리드의 월드 경계 내로 클램프합니다. (X/Z만 제한, Y는 그대로)
    /// </summary>
    public Vector3 ClampToGrid(Vector3 worldPos)
    {
        // 경계는 셀 경계까지 허용: [worldBottomLeft.x, worldTopRight.x + 1)
        float minX = worldBottomLeft.x;
        float maxX = worldTopRight.x + 1f; // 셀의 오른쪽 경계
        float minZ = worldBottomLeft.y;
        float maxZ = worldTopRight.y + 1f; // 셀의 위쪽 경계

        float clampedX = Mathf.Clamp(worldPos.x, minX, maxX);
        float clampedZ = Mathf.Clamp(worldPos.z, minZ, maxZ);
        return new Vector3(clampedX, worldPos.y, clampedZ);
    }
    /// <summary>
    /// 월드 좌표를 포함하는 셀의 정수 그리드 좌표(X/Z)를 반환합니다. (바닥 내림)
    /// </summary>
    public Vector2Int WorldToCell(Vector3 worldPos)
    {
        return new Vector2Int(
            Mathf.FloorToInt(worldPos.x),
            Mathf.FloorToInt(worldPos.z)
        );
    }

    /// <summary>
    /// 셀 좌표의 중심 월드 좌표를 반환합니다. (기본 Y=0)
    /// </summary>
    public Vector3 CellToWorldCenter(Vector2Int cell, float y = 0f)
    {
        return new Vector3(cell.x + 0.5f, y, cell.y + 0.5f);
    }

    /// <summary>
    /// 임의 월드 좌표를 가장 가까운 셀 중심으로 스냅합니다. (Y 유지 또는 지정)
    /// </summary>
    public Vector3 SnapToCellCenter(Vector3 worldPos, float yOverride = float.NaN)
    {
        Vector2Int cell = WorldToCell(worldPos);
        float y = float.IsNaN(yOverride) ? worldPos.y : yOverride;
        return new Vector3(cell.x + 0.5f, y, cell.y + 0.5f);
    }
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

        // ✅ 월드 좌표 경계를 기준으로 기즈모를 그립니다. (x=월드 X, y=월드 Z)
        Vector2Int bottomLeftGizmo;
        Vector2Int topRightGizmo;
        if (Application.isPlaying)
        {
            bottomLeftGizmo = worldBottomLeft;
            topRightGizmo = worldTopRight;
        }
        else
        {
            Vector2Int editorOrigin = new Vector2Int(
                Mathf.RoundToInt(transform.position.x),
                Mathf.RoundToInt(transform.position.z)
            ) + startOffset;
            bottomLeftGizmo = editorOrigin;
            topRightGizmo = editorOrigin + new Vector2Int(gridSize.x - 1, gridSize.y - 1);
        }

        Gizmos.color = Color.cyan;
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
                Vector3 pos = new Vector3(NodeArray[i,j].x + 0.5f, 0, NodeArray[i,j].y + 0.5f);
                if (NodeArray[i, j].isWall)
                {
                    // 벽 셀: 빨강(고정) / 주황(파괴 가능)
                    Gizmos.color = NodeArray[i, j].isBreakable ? new Color(1f, 0.5f, 0f, 0.7f) : new Color(1f, 0f, 0f, 0.7f);
                    Gizmos.DrawCube(pos, new Vector3(0.8f, 0.1f, 0.8f));
                }
                else
                {
                    // 이동 가능 셀: 반투명 초록
                    Gizmos.color = new Color(0f, 1f, 0f, 0.15f);
                    Gizmos.DrawCube(pos, new Vector3(0.8f, 0.02f, 0.8f));
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
