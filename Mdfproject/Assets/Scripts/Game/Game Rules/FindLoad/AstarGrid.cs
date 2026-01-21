// Assets/Scripts/Game/Game Rules/FindLoad/AstarGrid.cs
using System.Collections.Generic;
using UnityEngine;

public class AstarGrid : MonoBehaviour
{
    [Header("3D 그리드 바인딩")]
    [Tooltip("FieldManager의 그리드 설정(Origin, CellSize, Size)을 사용합니다.")]
    public bool useFieldManagerGrid = true;
    [Tooltip("동일한 Grid 루트 아래의 FieldManager를 자동으로 찾습니다.")]
    public FieldManager fieldManager;
    [Tooltip("FieldManager를 사용하지 않을 때, 직접 지정할 그리드 원점(월드, XZ)")]
    public Vector3 gridOrigin = Vector3.zero;
    [Tooltip("FieldManager를 사용하지 않을 때, 직접 지정할 셀 크기")]
    public float cellSize = 1f;
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
    public List<Vector2Int> WallsToBreakInPath { get; private set; }

    private int sizeX, sizeY;
    private AstarNode[,] NodeArray;
    // ✅ [변경] 노드 인덱스 경계(셀 좌표). FieldManager 그리드(0,0)~(size-1,size-1)를 기본으로 사용
    private Vector2Int worldBottomLeft; // 셀 좌표 기준 최소값
    private Vector2Int worldTopRight;   // 셀 좌표 기준 최대값
    private bool initialized = false;

    // 플레이 중에는 PlayerManager가 FieldManager 초기화 이후 호출하도록 두고,
    // 에디터 미플레이 상태에서는 미리 초기화하여 프리뷰를 보이게 합니다.
    private void Awake()
    {
        if (!Application.isPlaying && !initialized)
        {
            Initialize();
        }
    }

    private void OnValidate()
    {
        if (gridSize.x < 1) gridSize.x = 1;
        if (gridSize.y < 1) gridSize.y = 1;
    }
    // [3D Migration] FieldManager 그리드를 우선 바인딩, 없으면 기존 방식 사용
    public void Initialize()
    {
        // FieldManager 자동 바인딩 시도
        if (useFieldManagerGrid && fieldManager == null)
        {
            fieldManager = GetComponentInParent<FieldManager>();
        }

        if (useFieldManagerGrid && fieldManager != null && fieldManager.ground3D != null)
        {
            // FieldManager의 확장된 그리드 정보를 사용 (외곽 스폰 영역 포함)
            gridOrigin = fieldManager.TotalGridOrigin;
            cellSize = Mathf.Max(0.0001f, fieldManager.cellSize);
            gridSize = fieldManager.TotalGridSize;

            // 확장된 좌표계를 사용: 외곽 마진을 포함한 0..TotalSize-1 범위
            worldBottomLeft = Vector2Int.zero; // 셀 좌표 기준
            worldTopRight = new Vector2Int(gridSize.x - 1, gridSize.y - 1);
        }
        else
        {
            // 자신의 월드 위치를 기준으로 기존 방식 유지 (셀 크기 1 가정)
            Vector2Int origin = new Vector2Int(
                Mathf.FloorToInt(transform.position.x),
                Mathf.FloorToInt(transform.position.z)
            ) + startOffset;

            worldBottomLeft = origin;
            worldTopRight = origin + new Vector2Int(gridSize.x - 1, gridSize.y - 1);
            gridOrigin = new Vector3(worldBottomLeft.x, 0f, worldBottomLeft.y);
            cellSize = 1f;
        }

        // 그리드 노드 배열을 처음 생성합니다.
        InitializeGrid();
        initialized = true;
    }

    public bool FindPath(Vector2Int start, Vector2Int end, bool ignoreWalls = false, bool ignoreBreakableWalls = false)
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

            ExploreNeighbors(CurNode, TargetNode, OpenList, ClosedList, ignoreWalls, ignoreBreakableWalls);
        }

        Debug.LogWarning($"[AstarGrid] 경로를 찾을 수 없습니다: {start} -> {end}");
        FinalPath = null;
        WallsToBreakInPath = null;
        return false;
    }

    private void InitializeGrid()
    {
        // ✅ [수정] 셀 좌표 경계를 기준으로 크기를 계산합니다.
        sizeX = worldTopRight.x - worldBottomLeft.x + 1;
        sizeY = worldTopRight.y - worldBottomLeft.y + 1;
        NodeArray = new AstarNode[sizeX, sizeY];

        for (int i = 0; i < sizeX; i++)
        {
            for (int j = 0; j < sizeY; j++)
            {
                int cellX = i + worldBottomLeft.x; // 셀 좌표
                int cellY = j + worldBottomLeft.y; // 셀 좌표
                NodeArray[i, j] = new AstarNode(false, cellX, cellY);
            }
        }
    }

    private void UpdateGridWallStatus()
    {
        if (NodeArray == null) InitializeGrid();

        int breakableWallCount = 0;
        int permanentWallCount = 0;
        
        for (int i = 0; i < sizeX; i++)
        {
            for (int j = 0; j < sizeY; j++)
            {
                // ✅ 셀 좌표를 월드 좌표로 변환하여 감지
                Vector3 cellCenter = CellToWorldCenter(new Vector2Int(NodeArray[i, j].x, NodeArray[i, j].y), sampleY);
                Vector3 checkWorldPos = cellCenter;
                Vector3 halfExtents = new Vector3(detectionRadius * cellSize, Mathf.Max(0.01f, sampleHeight * 0.5f), detectionRadius * cellSize);
                bool isWall = Physics.CheckBox(checkWorldPos, halfExtents, Quaternion.identity, wallLayers);
                NodeArray[i, j].isWall = isWall;

                if (isWall)
                {
                    // 벽 중에서 breakableWallLayer에 속한 것만 파괴 가능으로 설정
                    bool isBreakable = Physics.CheckBox(checkWorldPos, halfExtents, Quaternion.identity, breakableWallLayer);
                    NodeArray[i, j].isBreakable = isBreakable;
                    
                    if (isBreakable)
                    {
                        breakableWallCount++;
                    }
                    else
                    {
                        permanentWallCount++;
                    }
                }
                else
                {
                    NodeArray[i, j].isBreakable = false;
                }
            }
        }
        
        if (showDebugInfo)
        {
            Debug.Log($"[AstarGrid] 벽 인식 완료 - 파괴 가능: {breakableWallCount}, 파괴 불가: {permanentWallCount}");
        }
    }


    private void ExploreNeighbors(AstarNode CurNode, AstarNode TargetNode, List<AstarNode> OpenList, HashSet<AstarNode> ClosedList, bool ignoreWalls, bool ignoreBreakableWalls)
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

                // ignoreWalls가 true이면 모든 벽 무시
                // ignoreBreakableWalls가 true이면 파괴 가능한 벽만 무시
                if (!ignoreWalls)
                {
                    if (NeighborNode.isWall)
                    {
                        // 파괴 가능한 벽이고 ignoreBreakableWalls가 true면 통과
                        if (NeighborNode.isBreakable && ignoreBreakableWalls)
                        {
                            // 파괴 가능 벽을 무시 - 일반 타일로 취급
                        }
                        // 파괴 불가능한 벽이면 항상 차단
                        else if (!NeighborNode.isBreakable)
                        {
                            continue;
                        }
                        // 파괴 가능한 벽이지만 ignoreBreakableWalls가 false인 경우
                        // 아래에서 높은 비용으로 처리됨
                    }
                }

                if (!ignoreWalls && dontCrossCorner && x != 0 && y != 0)
                {
                    if (GetNode(new Vector2Int(CurNode.x + x, CurNode.y)).isWall || GetNode(new Vector2Int(CurNode.x, CurNode.y + y)).isWall)
                        continue;
                }

                int distanceCost = (x == 0 || y == 0) ? 10 : 14;
                int tentativeGCost = CurNode.G + distanceCost;

                // 파괴 가능 벽 비용 처리
                if (!ignoreWalls && NeighborNode.isWall)
                {
                    // ignoreBreakableWalls가 true이고 파괴 가능한 벽이면 비용 추가 안함
                    if (!(ignoreBreakableWalls && NeighborNode.isBreakable))
                    {
                        tentativeGCost += wallBreakCost;
                    }
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
    /// 최대 경계는 셀 경계 바로 안쪽까지만 허용하여 최댓값에서의 올림/내림 오차로 인한 유령 셀을 방지합니다.
    /// </summary>
    public Vector3 ClampToGrid(Vector3 worldPos)
    {
        // 하한은 포함, 상한은 셀 경계 바로 안쪽(배타적)으로 클램프
        float minX = gridOrigin.x + (worldBottomLeft.x) * cellSize;
        float maxXExclusive = gridOrigin.x + (worldTopRight.x + 1) * cellSize;
        float minZ = gridOrigin.z + (worldBottomLeft.y) * cellSize;
        float maxZExclusive = gridOrigin.z + (worldTopRight.y + 1) * cellSize;

        float epsilon = Mathf.Max(1e-4f * cellSize, Mathf.Epsilon);
        float clampedX = Mathf.Clamp(worldPos.x, minX, maxXExclusive - epsilon);
        float clampedZ = Mathf.Clamp(worldPos.z, minZ, maxZExclusive - epsilon);
        return new Vector3(clampedX, worldPos.y, clampedZ);
    }
    /// <summary>
    /// 월드 좌표를 포함하는 셀의 정수 그리드 좌표(X/Z)를 반환합니다. (바닥 내림)
    /// </summary>
    public Vector2Int WorldToCell(Vector3 worldPos)
    {
        // FieldManager 그리드 기준으로 변환
        int cx = Mathf.FloorToInt((worldPos.x - gridOrigin.x) / cellSize);
        int cz = Mathf.FloorToInt((worldPos.z - gridOrigin.z) / cellSize);
        // 최댓값 경계에서 생길 수 있는 유령 셀(= size 인덱스)을 방지하기 위해 유효 범위로 클램프
        cx = Mathf.Clamp(cx, worldBottomLeft.x, worldTopRight.x);
        cz = Mathf.Clamp(cz, worldBottomLeft.y, worldTopRight.y);
        return new Vector2Int(cx, cz);
    }

    /// <summary>
    /// 셀 좌표의 중심 월드 좌표를 반환합니다. (기본 Y=0)
    /// </summary>
    public Vector3 CellToWorldCenter(Vector2Int cell, float y = 0f)
    {
        float wx = gridOrigin.x + (cell.x + 0.5f) * cellSize;
        float wz = gridOrigin.z + (cell.y + 0.5f) * cellSize;
        return new Vector3(wx, y, wz);
    }

    /// <summary>
    /// 임의 월드 좌표를 가장 가까운 셀 중심으로 스냅합니다. (Y 유지 또는 지정)
    /// </summary>
    public Vector3 SnapToCellCenter(Vector3 worldPos, float yOverride = float.NaN)
    {
        // 먼저 그리드 경계로 클램프하여 항상 유효 셀을 선택
        Vector3 clamped = ClampToGrid(worldPos);
        Vector2Int cell = WorldToCell(clamped);
        float y = float.IsNaN(yOverride) ? worldPos.y : yOverride;
        return CellToWorldCenter(cell, y);
    }
    private bool IsValidPosition(Vector2Int pos)
    {
        // ✅ [수정] 셀 좌표 경계와 비교합니다.
        return pos.x >= worldBottomLeft.x && pos.x <= worldTopRight.x &&
               pos.y >= worldBottomLeft.y && pos.y <= worldTopRight.y;
    }

    public AstarNode GetNode(Vector2Int pos)
    {
        if (!IsValidPosition(pos)) return null;
        // ✅ [수정] 셀 좌표를 배열 인덱스로 변환합니다.
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

        // ✅ FieldManager/Origin/CellSize 기반으로 그리드 박스를 그립니다.
        Vector3 gizmoOrigin;
        Vector2Int gizmoSize;
        if (Application.isPlaying && useFieldManagerGrid && fieldManager != null && fieldManager.ground3D != null)
        {
            gizmoOrigin = gridOrigin;
            gizmoSize = gridSize;
        }
        else
        {
            // 에디터에서도 최대한 FieldManager와 일치하도록 시도
            gizmoOrigin = gridOrigin;
            gizmoSize = gridSize;
        }

        Gizmos.color = Color.cyan;
        Vector3 center = new Vector3(
            gizmoOrigin.x + gizmoSize.x * cellSize * 0.5f,
            0,
            gizmoOrigin.z + gizmoSize.y * cellSize * 0.5f
        );
        Vector3 size = new Vector3(
            gizmoSize.x * cellSize,
            0.1f,
            gizmoSize.y * cellSize
        );
        Gizmos.DrawWireCube(center, size);

        if (NodeArray == null) return;

        for (int i = 0; i < sizeX; i++)
        {
            for (int j = 0; j < sizeY; j++)
            {
                Vector3 pos = CellToWorldCenter(new Vector2Int(NodeArray[i, j].x, NodeArray[i, j].y), 0f);
                if (NodeArray[i, j].isWall)
                {
                    // 벽 셀: 빨강(고정) / 주황(파괴 가능)
                    Gizmos.color = NodeArray[i, j].isBreakable ? new Color(1f, 0.5f, 0f, 0.7f) : new Color(1f, 0f, 0f, 0.7f);
                    Gizmos.DrawCube(pos, new Vector3(0.8f * cellSize, 0.1f, 0.8f * cellSize));
                }
                else
                {
                    // 이동 가능 셀: 반투명 초록
                    Gizmos.color = new Color(0f, 1f, 0f, 0.15f);
                    Gizmos.DrawCube(pos, new Vector3(0.8f * cellSize, 0.02f, 0.8f * cellSize));
                }
            }
        }

        if (FinalPath != null && FinalPath.Count > 0)
        {
            Gizmos.color = Color.green; // 몬스터의 현재 실제 경로
            for (int i = 0; i < FinalPath.Count - 1; i++)
            {
                // [3D Migration] 셀 -> 월드 변환 사용
                Vector3 from = CellToWorldCenter(new Vector2Int(FinalPath[i].x, FinalPath[i].y), 0.1f);
                Vector3 to = CellToWorldCenter(new Vector2Int(FinalPath[i + 1].x, FinalPath[i + 1].y), 0.1f);
                Gizmos.DrawLine(from, to);
            }
        }
    }


    #endregion
}
