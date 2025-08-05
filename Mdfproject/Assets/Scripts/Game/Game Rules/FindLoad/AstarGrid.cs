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
    public float detectionRadius = 0.4f; // 감지 반지름 조절 가능하게

    [Header("막힌 목적지 처리")]
    public bool allowWallBreaking = true; // 벽 파괴 허용
    public int maxWallsToBreak = 1; // 최대 파괴할 벽 개수
    public bool useSmartWallSelection = true; // 똑똑한 벽 선택
    public LayerMask breakableWallLayer = -1; // 파괴 가능한 벽 레이어

    int sizeX, sizeY;
    AstarNode[,] NodeArray;
    AstarNode StartNode, TargetNode, CurNode;
    List<AstarNode> OpenList, ClosedList;

    // 벽 파괴 관련 변수
    private List<Vector2Int> wallsToBreak = new List<Vector2Int>();
    private AstarNode[,] OriginalNodeArray; // 원본 그리드 백업


    public GameObject monsterPrefab;  // 테스트할 몬스터 연결

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
                    if (col.gameObject.layer == LayerMask.NameToLayer("Wall"))
                    {
                        isWall = true;
                        break;
                    }
                }

                NodeArray[i, j] = new AstarNode(isWall, i + bottomLeft.x, j + bottomLeft.y);
            }
        }
    }

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

    private bool FindAndBreakWalls()
    {
        BackupOriginalGrid();

        // 직선상의 벽 파괴 시도
        List<Vector2Int> wallsOnPath = GetWallsOnDirectPath(startPos, targetPos);

        foreach (Vector2Int wall in wallsOnPath)
        {
            RestoreOriginalGrid();
            wallsToBreak.Clear();
            wallsToBreak.Add(wall);

            if (CanBreakWall(wall))
            {
                BreakWallInGrid(wall);

                if (IsPathPossible())
                {
                    Debug.Log($"💥 벽 ({wall.x}, {wall.y}) 파괴로 경로 확보!");
                    return true;
                }
            }
        }

        return false;
    }

    private List<Vector2Int> GetWallsOnDirectPath(Vector2Int start, Vector2Int end)
    {
        List<Vector2Int> wallsOnPath = new List<Vector2Int>();
        List<Vector2Int> linePoints = GetLinePoints(start, end);

        foreach (Vector2Int point in linePoints)
        {
            if (IsValidPosition(point) && IsWall(point) && CanBreakWall(point))
            {
                wallsOnPath.Add(point);
            }
        }

        return wallsOnPath;
    }

    private List<Vector2Int> GetLinePoints(Vector2Int start, Vector2Int end)
    {
        List<Vector2Int> points = new List<Vector2Int>();

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
            }
        }

        points.Add(end);
        return points;
    }

    private bool CanBreakWall(Vector2Int pos)
    {
        Vector2 worldPos = new Vector2(pos.x, pos.y);
        Collider2D[] colliders = Physics2D.OverlapCircleAll(worldPos, detectionRadius);

        foreach (Collider2D col in colliders)
        {
            if (col.gameObject.layer == LayerMask.NameToLayer("Wall"))
            {
                DestructibleWall destructible = col.GetComponent<DestructibleWall>();
                return destructible != null;
            }
        }

        return false;
    }

    private void BreakWallInGrid(Vector2Int pos)
    {
        if (IsValidPosition(pos))
        {
            NodeArray[pos.x - bottomLeft.x, pos.y - bottomLeft.y].isWall = false;
            if (showDebugInfo)
                Debug.Log($"🔨 그리드에서 벽 제거: ({pos.x}, {pos.y})");
        }
    }

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

    private bool IsValidPosition(Vector2Int pos)
    {
        return pos.x >= bottomLeft.x && pos.x <= topRight.x &&
               pos.y >= bottomLeft.y && pos.y <= topRight.y;
    }

    private bool IsWall(Vector2Int pos)
    {
        if (!IsValidPosition(pos)) return true;
        return NodeArray[pos.x - bottomLeft.x, pos.y - bottomLeft.y].isWall;
    }

    private void ResetPathfinding()
    {
        StartNode = NodeArray[startPos.x - bottomLeft.x, startPos.y - bottomLeft.y];
        TargetNode = NodeArray[targetPos.x - bottomLeft.x, targetPos.y - bottomLeft.y];
        OpenList = new List<AstarNode>() { StartNode };
        ClosedList = new List<AstarNode>();
        FinalNodeList = new List<AstarNode>();
    }

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