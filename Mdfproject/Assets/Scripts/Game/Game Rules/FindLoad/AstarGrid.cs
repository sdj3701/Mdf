using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.XR;

public class AstarGrid : MonoBehaviour
{
    public Vector2Int bottomLeft, topRight, startPos, targetPos;
    public List<AstarNode> FinalNodeList;
    public bool allowDiagonal, dontCrossCorner;

    [Header("디버깅")]
    public bool showDebugInfo = true; // 디버그 정보 표시 여부
    public bool detailedWallDebugging = false; // 벽 파괴 상세 디버깅
    public float detectionRadius = 0.4f; // 감지 반지름 조절 가능하게

    [Header("막힌 목적지 처리")]
    public bool allowWallBreaking = true; // 벽 파괴 허용
    public int maxWallsToBreak = 1; // 최대 파괴할 벽 개수
    public bool useSmartWallSelection = true; // 똑똑한 벽 선택
    public LayerMask wallLayers = -1; // 벽으로 인식할 레이어들 (Wall + BreakWall)

    int sizeX, sizeY;
    AstarNode[,] NodeArray;
    AstarNode StartNode, TargetNode, CurNode;
    List<AstarNode> OpenList, ClosedList;

    // 벽 파괴 관련 변수
    private List<Vector2Int> wallsToBreak = new List<Vector2Int>();
    private AstarNode[,] OriginalNodeArray; // 원본 그리드 백업

    public GameObject monsterPrefab;  // 테스트할 몬스터 연결

    /// <summary>
    /// 메인 패스파인딩 함수 - 그리드 초기화, 벽 파괴 체크, A* 알고리즘 실행을 순차적으로 처리
    /// </summary>
    public void PathFinding()
    {
        // 1단계: 그리드 초기화
        InitializeGrid();

        // 2단계: 벽 파괴 필요성 체크 및 실행 (전처리)
        if (allowWallBreaking)
        {
            Debug.Log("🔨 벽 파괴 필요성 체크 중...");

            if (!IsPathPossible())
            {
                Debug.Log("💥 벽 파괴가 필요합니다. 벽 파괴 시작...");

                if (FindAndBreakWalls())
                {
                    Debug.Log($"✅ 벽 {wallsToBreak.Count}개 파괴 완료!");
                }
                else
                {
                    Debug.LogError("❌ 적절한 벽을 찾을 수 없습니다!");
                    return;
                }
            }
            else
            {
                Debug.Log("✅ 벽 파괴 없이도 경로 가능!");
            }
        }

        // 3단계: 패스파인딩 초기화 및 실행
        ResetPathfinding();

        // 4단계: A* 알고리즘 실행
        if (ExecuteAStarAlgorithm())
        {
            Debug.Log("🎯 경로 찾기 성공!");
        }
        else
        {
            Debug.LogError("❌ 경로를 찾을 수 없습니다!");
        }
    }

    /// <summary>
    /// 그리드 초기화 - 지정된 범위의 각 셀에 대해 벽 여부를 체크하고 AstarNode 배열을 생성
    /// </summary>
    private void InitializeGrid()
    {
        sizeX = topRight.x - bottomLeft.x + 1;
        sizeY = topRight.y - bottomLeft.y + 1;
        NodeArray = new AstarNode[sizeX, sizeY];

        for (int i = 0; i < sizeX; i++)
        {
            for (int j = 0; j < sizeY; j++)
            {
                bool isWall = false;
                Vector2 checkPos = new Vector2(i + bottomLeft.x, j + bottomLeft.y);
                Collider2D[] colliders = Physics2D.OverlapCircleAll(checkPos, detectionRadius);

                foreach (Collider2D col in colliders)
                {
                    // LayerMask를 사용해 지정된 레이어들을 벽으로 인식
                    if ((wallLayers.value & (1 << col.gameObject.layer)) != 0)
                    {
                        isWall = true;
                        break;
                    }
                }

                NodeArray[i, j] = new AstarNode(isWall, i + bottomLeft.x, j + bottomLeft.y);
            }
        }
    }

    /// <summary>
    /// 경로 가능 여부를 빠르게 체크 - 최대 50번 반복으로 제한하여 성능 최적화
    /// </summary>
    private bool IsPathPossible()
    {
        ResetPathfinding();

        int maxIterations = 50;
        int iterations = 0;

        while (OpenList.Count > 0 && iterations < maxIterations)
        {
            CurNode = OpenList[0];
            for (int i = 1; i < OpenList.Count; i++)
                if (OpenList[i].F <= CurNode.F && OpenList[i].H < CurNode.H)
                    CurNode = OpenList[i];

            OpenList.Remove(CurNode);
            ClosedList.Add(CurNode);

            if (CurNode == TargetNode)
                return true;

            ExploreNeighbors();
            iterations++;
        }

        return false;
    }

    /// <summary>
    /// 벽 파괴 로직 - 시작점과 목적지를 잇는 직선상의 벽들을 찾아서 하나씩 파괴 시도
    /// </summary>
    private bool FindAndBreakWalls()
    {
        Debug.Log($"🔍 시작점: ({startPos.x}, {startPos.y}), 목적지: ({targetPos.x}, {targetPos.y})");

        BackupOriginalGrid();

        // 직선상의 벽 파괴 시도
        List<Vector2Int> wallsOnPath = GetWallsOnDirectPath(startPos, targetPos);

        Debug.Log($"💣 직선상에서 파괴 가능한 벽 {wallsOnPath.Count}개 발견");

        if (wallsOnPath.Count == 0)
        {
            Debug.LogWarning("⚠️ 직선상에 파괴 가능한 벽이 없습니다!");
            return false;
        }

        foreach (Vector2Int wall in wallsOnPath)
        {
            RestoreOriginalGrid();
            wallsToBreak.Clear();
            wallsToBreak.Add(wall);

            if (detailedWallDebugging)
                Debug.Log($"🔨 벽 ({wall.x}, {wall.y}) 파괴 시도...");

            if (CanBreakWall(wall))
            {
                BreakWallInGrid(wall);

                if (IsPathPossible())
                {
                    Debug.Log($"💥 벽 ({wall.x}, {wall.y}) 파괴로 경로 확보!");
                    return true;
                }
                else
                {
                    if (detailedWallDebugging)
                        Debug.Log($"❌ 벽 ({wall.x}, {wall.y}) 파괴해도 경로 없음");
                }
            }
            else
            {
                if (detailedWallDebugging)
                    Debug.LogWarning($"⚠️ 벽 ({wall.x}, {wall.y})를 파괴할 수 없음");
            }
        }

        Debug.LogError("❌ 모든 벽을 시도했지만 경로를 찾을 수 없음");
        return false;
    }

    /// <summary>
    /// 시작점과 목적지를 잇는 직선상에 위치한 모든 파괴 가능한 벽들을 찾아서 리스트로 반환
    /// </summary>
    private List<Vector2Int> GetWallsOnDirectPath(Vector2Int start, Vector2Int end)
    {
        List<Vector2Int> wallsOnPath = new List<Vector2Int>();
        List<Vector2Int> linePoints = GetLinePoints(start, end);
        // 디버깅: 직선상의 모든 점들을 출력
        if (detailedWallDebugging)
        {
            Debug.Log($"🔍 직선상의 점들 ({linePoints.Count}개):");
            foreach (Vector2Int point in linePoints)
            {
                Debug.Log($"  점: ({point.x}, {point.y}) - Valid: {IsValidPosition(point)}, Wall: {(IsValidPosition(point) ? IsWall(point) : false)}, CanBreak: {(IsValidPosition(point) && IsWall(point) ? CanBreakWall(point) : false)}");
            }
        }

        foreach (Vector2Int point in linePoints)
        {
            if (IsValidPosition(point) && IsWall(point) && CanBreakWall(point))
            {
                wallsOnPath.Add(point);
                if (detailedWallDebugging)
                    Debug.Log($"✅ 파괴 가능한 벽 발견: ({point.x}, {point.y})");
            }
        }
        return wallsOnPath;
    }

    /// <summary>
    /// 브레젠햄 직선 알고리즘 - 두 점을 잇는 직선상의 모든 격자점들을 계산하여 반환
    /// </summary>
    /// <summary>
    /// 브레젠햄 직선 알고리즘 - 두 점을 잇는 직선상의 모든 격자점들을 계산하여 반환
    /// </summary>
    private List<Vector2Int> GetLinePoints(Vector2Int start, Vector2Int end)
    {
        List<Vector2Int> points = new List<Vector2Int>();

        // 시작점과 끝점이 같은 경우 처리
        if (start == end)
        {
            points.Add(start);
            return points;
        }

        // 시작점을 먼저 추가
        points.Add(start);

        int dx = Mathf.Abs(end.x - start.x);
        int dy = Mathf.Abs(end.y - start.y);
        int x = start.x;
        int y = start.y;
        int sx = start.x < end.x ? 1 : -1;
        int sy = start.y < end.y ? 1 : -1;

        if (dx > dy)
        {
            int err = dx / 2;
            while (x != end.x)
            {
                points.Add(new Vector2Int(x, y));
                err -= dy;
                if (err < 0)
                {
                    y += sy;
                    err += dx;
                }
                x += sx;
                points.Add(new Vector2Int(x, y));
            }
        }
        else
        {
            int err = dy / 2;
            while (y != end.y)
            {
                points.Add(new Vector2Int(x, y));
                err -= dx;
                if (err < 0)
                {
                    x += sx;
                    err += dy;
                }
                y += sy;
                points.Add(new Vector2Int(x, y));
            }
        }

        points.Add(end);
        return points;
    }

    /// <summary>
    /// 해당 위치의 벽이 파괴 가능한지 확인 - wallLayers에 포함된 레이어이면서 DestructibleWall 컴포넌트가 있는지 체크
    /// </summary>
    private bool CanBreakWall(Vector2Int pos)
    {
        Vector2 worldPos = new Vector2(pos.x, pos.y);
        Collider2D[] colliders = Physics2D.OverlapCircleAll(worldPos, detectionRadius);
        foreach (Collider2D col in colliders)
        {
            // BreakWall 레이어인 경우에만 파괴 가능
            if (col.gameObject.layer == LayerMask.NameToLayer("BreakWall"))
                // wallLayers에 포함된 레이어인지 확인
                if ((wallLayers.value & (1 << col.gameObject.layer)) != 0)
                {
                    // DestructibleWall 컴포넌트가 있으면 파괴 가능
                    DestructibleWall destructible = col.GetComponent<DestructibleWall>();
                    return destructible != null;
                    // BreakWall 레이어인 경우에만 파괴 가능
                    /*if (col.gameObject.layer == LayerMask.NameToLayer("BreakWall"))
                    {
                        // DestructibleWall 컴포넌트가 있으면 파괴 가능
                        DestructibleWall destructible = col.GetComponent<DestructibleWall>();
                        if (destructible != null)
                            return true;
                    }*/
                }
        }


        return false;
    }

    /// <summary>
    /// 그리드에서 지정된 위치의 벽을 제거 - NodeArray의 isWall 속성을 false로 변경
    /// </summary>
    private void BreakWallInGrid(Vector2Int pos)
    {
        if (IsValidPosition(pos))
        {
            NodeArray[pos.x - bottomLeft.x, pos.y - bottomLeft.y].isWall = false;
            if (showDebugInfo)
                Debug.Log($"🔨 그리드에서 벽 제거: ({pos.x}, {pos.y})");
        }
    }

    /// <summary>
    /// 현재 그리드 상태를 백업 - 벽 파괴 시도 전 원본 상태 보존용
    /// </summary>
    private void BackupOriginalGrid()
    {
        OriginalNodeArray = new AstarNode[sizeX, sizeY];
        for (int i = 0; i < sizeX; i++)
        {
            for (int j = 0; j < sizeY; j++)
            {
                OriginalNodeArray[i, j] = new AstarNode(
                    NodeArray[i, j].isWall,
                    NodeArray[i, j].x,
                    NodeArray[i, j].y
                );
            }
        }
    }

    /// <summary>
    /// 백업된 원본 그리드로 복원 - 벽 파괴 시도 실패 시 원래 상태로 되돌리기
    /// </summary>
    private void RestoreOriginalGrid()
    {
        if (OriginalNodeArray != null)
        {
            for (int i = 0; i < sizeX; i++)
            {
                for (int j = 0; j < sizeY; j++)
                {
                    NodeArray[i, j].isWall = OriginalNodeArray[i, j].isWall;
                }
            }
        }
    }

    /// <summary>
    /// 주어진 위치가 그리드 범위 내에 있는지 확인
    /// </summary>
    private bool IsValidPosition(Vector2Int pos)
    {
        return pos.x >= bottomLeft.x && pos.x <= topRight.x &&
               pos.y >= bottomLeft.y && pos.y <= topRight.y;
    }

    /// <summary>
    /// 해당 위치가 벽인지 확인 - 유효하지 않은 위치는 벽으로 처리
    /// </summary>
    private bool IsWall(Vector2Int pos)
    {
        if (!IsValidPosition(pos)) return true;
        return NodeArray[pos.x - bottomLeft.x, pos.y - bottomLeft.y].isWall;
    }

    /// <summary>
    /// 패스파인딩을 위한 초기화 - 시작/목적지 노드 설정, OpenList/ClosedList 초기화
    /// </summary>
    private void ResetPathfinding()
    {
        StartNode = NodeArray[startPos.x - bottomLeft.x, startPos.y - bottomLeft.y];
        TargetNode = NodeArray[targetPos.x - bottomLeft.x, targetPos.y - bottomLeft.y];
        OpenList = new List<AstarNode>() { StartNode };
        ClosedList = new List<AstarNode>();
        FinalNodeList = new List<AstarNode>();
    }

    /// <summary>
    /// A* 알고리즘 실행 - F값이 가장 낮은 노드를 선택하며 목적지까지의 최적 경로 탐색
    /// </summary>
    private bool ExecuteAStarAlgorithm()
    {
        while (OpenList.Count > 0)
        {
            CurNode = OpenList[0];
            for (int i = 1; i < OpenList.Count; i++)
                if (OpenList[i].F <= CurNode.F && OpenList[i].H < CurNode.H)
                    CurNode = OpenList[i];

            OpenList.Remove(CurNode);
            ClosedList.Add(CurNode);

            if (CurNode == TargetNode)
            {
                BuildFinalPath();
                StartMonsterMovement();
                return true;
            }

            ExploreNeighbors();
        }

        return false;
    }

    /// <summary>
    /// 목적지에서 시작점까지 역추적하여 최종 경로 구성 - ParentNode를 따라가며 경로 생성
    /// </summary>
    private void BuildFinalPath()
    {
        AstarNode currentNode = TargetNode;
        while (currentNode != StartNode)
        {
            FinalNodeList.Add(currentNode);
            currentNode = currentNode.ParentNode;
        }
        FinalNodeList.Add(StartNode);
        FinalNodeList.Reverse();

        if (showDebugInfo)
        {
            for (int i = 0; i < FinalNodeList.Count; i++)
                Debug.Log($"{i}번째: ({FinalNodeList[i].x}, {FinalNodeList[i].y})");
        }
    }

    /// <summary>
    /// 몬스터 이동 시작 - 계산된 경로와 파괴할 벽 정보를 몬스터에게 전달하고 이동 시작
    /// </summary>
    private void StartMonsterMovement()
    {
        if (monsterPrefab == null)
            monsterPrefab = GameObject.Find("Monster");

        if (monsterPrefab != null && FinalNodeList.Count > 0)
        {
            GameObject instance = Instantiate(monsterPrefab);
            Monster monster = instance.GetComponent<Monster>();

            if (wallsToBreak.Count > 0)
            {
                monster.SetWallsToBreak(wallsToBreak);
            }

            monster.StartFollowingPath(FinalNodeList);
        }
    }

    /// <summary>
    /// 현재 노드의 상하좌우 및 대각선 방향 인접 노드들을 탐색하여 OpenList에 추가
    /// </summary>
    private void ExploreNeighbors()
    {
        if (allowDiagonal)
        {
            OpenListAdd(CurNode.x + 1, CurNode.y + 1);
            OpenListAdd(CurNode.x - 1, CurNode.y + 1);
            OpenListAdd(CurNode.x - 1, CurNode.y - 1);
            OpenListAdd(CurNode.x + 1, CurNode.y - 1);
        }

        OpenListAdd(CurNode.x, CurNode.y + 1);
        OpenListAdd(CurNode.x + 1, CurNode.y);
        OpenListAdd(CurNode.x, CurNode.y - 1);
        OpenListAdd(CurNode.x - 1, CurNode.y);
    }

    /// <summary>
    /// 지정된 좌표의 노드를 OpenList에 추가 - 유효성 검사, 이동 비용 계산, G/H값 설정 포함
    /// </summary>
    void OpenListAdd(int checkX, int checkY)
    {
        if (checkX >= bottomLeft.x && checkX < topRight.x + 1 && checkY >= bottomLeft.y && checkY < topRight.y + 1 &&
            !NodeArray[checkX - bottomLeft.x, checkY - bottomLeft.y].isWall &&
            !ClosedList.Contains(NodeArray[checkX - bottomLeft.x, checkY - bottomLeft.y]))
        {
            if (allowDiagonal)
                if (NodeArray[CurNode.x - bottomLeft.x, checkY - bottomLeft.y].isWall &&
                    NodeArray[checkX - bottomLeft.x, CurNode.y - bottomLeft.y].isWall) return;

            if (dontCrossCorner)
                if (NodeArray[CurNode.x - bottomLeft.x, checkY - bottomLeft.y].isWall ||
                    NodeArray[checkX - bottomLeft.x, CurNode.y - bottomLeft.y].isWall) return;

            AstarNode NeighborNode = NodeArray[checkX - bottomLeft.x, checkY - bottomLeft.y];
            int MoveCost = CurNode.G + (CurNode.x - checkX == 0 || CurNode.y - checkY == 0 ? 10 : 14);

            if (MoveCost < NeighborNode.G || !OpenList.Contains(NeighborNode))
            {
                NeighborNode.G = MoveCost;
                NeighborNode.H = (Mathf.Abs(NeighborNode.x - TargetNode.x) + Mathf.Abs(NeighborNode.y - TargetNode.y)) * 10;
                NeighborNode.ParentNode = CurNode;

                OpenList.Add(NeighborNode);
            }
        }
    }

    /// <summary>
    /// Scene 뷰에서 그리드, 경로, 파괴할 벽들을 시각적으로 표시하는 기즈모 그리기
    /// </summary>
    void OnDrawGizmos()
    {
        if (NodeArray == null) return;

        for (int i = 0; i < sizeX; i++)
        {
            for (int j = 0; j < sizeY; j++)
            {
                Vector3 pos = new Vector3(i + bottomLeft.x, j + bottomLeft.y, 0);
                Vector2Int gridPos = new Vector2Int(i + bottomLeft.x, j + bottomLeft.y);

                if (wallsToBreak.Contains(gridPos))
                {
                    Gizmos.color = Color.black;
                    Gizmos.DrawCube(pos, Vector3.one * 0.9f);
                }
                else if (NodeArray[i, j].isWall)
                {
                    Gizmos.color = Color.red;
                    Gizmos.DrawCube(pos, Vector3.one * 0.8f);
                }
                else
                {
                    Gizmos.color = Color.white;
                    Gizmos.DrawWireCube(pos, Vector3.one);
                }
            }
        }

        if (FinalNodeList != null && FinalNodeList.Count > 0)
        {
            Gizmos.color = Color.green;
            for (int i = 0; i < FinalNodeList.Count - 1; i++)
            {
                Vector3 from = new Vector3(FinalNodeList[i].x, FinalNodeList[i].y, 0);
                Vector3 to = new Vector3(FinalNodeList[i + 1].x, FinalNodeList[i + 1].y, 0);
                Gizmos.DrawLine(from, to);
            }
        }

        Gizmos.color = Color.blue;
        Gizmos.DrawSphere(new Vector3(startPos.x, startPos.y, 0), 0.5f);
        Gizmos.color = Color.yellow;
        Gizmos.DrawSphere(new Vector3(targetPos.x, targetPos.y, 0), 0.5f);
    }
}