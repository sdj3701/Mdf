// Assets/Scripts/Game/Game Rules/FindLoad/AstarGrid.cs

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
    public bool showDebugInfo = true;
    public bool detailedWallDebugging = false;
    public float detectionRadius = 0.4f;

    [Header("막힌 목적지 처리")]
    public bool allowWallBreaking = true;
    public int maxWallsToBreak = 1;
    public bool useSmartWallSelection = true;
    public LayerMask wallLayers = -1;

    int sizeX, sizeY;
    AstarNode[,] NodeArray;
    AstarNode StartNode, TargetNode, CurNode;
    List<AstarNode> OpenList, ClosedList;

    private List<Vector2Int> wallsToBreak = new List<Vector2Int>();
    private AstarNode[,] OriginalNodeArray;

    // 역할 분리를 위해 몬스터 생성 관련 코드(monsterPrefab, StartMonsterMovement)를 모두 제거했습니다.
    // 이 클래스는 이제 순수하게 경로 계산만 담당합니다.

    /// <summary>
    /// [핵심 메서드] 지정된 시작점과 끝점 사이의 경로를 계산하여 노드 리스트로 반환합니다.
    /// MonsterSpawner 등 외부 클래스에서 이 함수를 호출하여 사용합니다.
    /// </summary>
    /// <param name="start">경로 탐색을 시작할 좌표</param>
    /// <param name="end">경로 탐색의 목표 좌표</param>
    /// <returns>계산된 경로 리스트. 경로를 찾지 못하면 null을 반환합니다.</returns>
    public List<AstarNode> FindPath(Vector2Int start, Vector2Int end)
    {
        // 1. 이 길찾기를 위한 전용 변수 설정
        startPos = start;
        targetPos = end;
        wallsToBreak.Clear(); // 새로운 경로 탐색을 위해 파괴할 벽 리스트 초기화

        // 2. 그리드 초기화 및 경로 탐색
        InitializeGrid();

        // 3. 벽 파괴 필요성 체크 및 실행
        if (allowWallBreaking && !IsPathPossible())
        {
            if (!FindAndBreakWalls())
            {
                Debug.LogError($"[FindPath] 벽 파괴 시도 후에도 경로를 찾을 수 없습니다: {start} -> {end}");
                return null; // 경로 찾기 실패
            }
        }

        // 4. 최종 A* 알고리즘 실행
        ResetPathfinding();
        if (ExecuteAStarAlgorithm())
        {
            return FinalNodeList; // 성공 시 계산된 경로 반환
        }

        Debug.LogError($"[FindPath] 최종 경로를 찾을 수 없습니다: {start} -> {end}");
        return null; // 최종 실패
    }
    
    /// <summary>
    /// 인스펙터의 startPos, targetPos를 이용해 길찾기를 테스트하기 위한 레거시 함수입니다.
    /// </summary>
    public void PathFinding()
    {
        FindPath(startPos, targetPos);
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
                // ✅ [수정] 타일 중앙에서 충돌을 감지하도록 0.5f씩 더해줍니다.
                Vector2 checkPos = new Vector2(i + bottomLeft.x + 0.5f, j + bottomLeft.y + 0.5f);
                Collider2D[] colliders = Physics2D.OverlapCircleAll(checkPos, detectionRadius);

                foreach (Collider2D col in colliders)
                {
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
        
        List<Vector2Int> wallsOnPath = GetWallsOnDirectPath(startPos, targetPos);

        if (wallsOnPath.Count == 0)
        {
            return false;
        }

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
        if (start == end)
        {
            points.Add(start);
            return points;
        }
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
    
    private bool CanBreakWall(Vector2Int pos)
    {
        Vector2 worldPos = new Vector2(pos.x, pos.y);
        Collider2D[] colliders = Physics2D.OverlapCircleAll(worldPos, detectionRadius);
        foreach (Collider2D col in colliders)
        {
            if (col.gameObject.layer == LayerMask.NameToLayer("BreakWall"))
                if ((wallLayers.value & (1 << col.gameObject.layer)) != 0)
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
                // StartMonsterMovement() 호출을 제거하여 몬스터 생성 책임을 분리.
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

        // 그리드 노드 그리기
        for (int i = 0; i < sizeX; i++)
        {
            for (int j = 0; j < sizeY; j++)
            {
                // ✅ [수정] 기즈모도 타일 중앙에 그려지도록 0.5f씩 더해줍니다.
                Vector3 pos = new Vector3(i + bottomLeft.x + 0.5f, j + bottomLeft.y + 0.5f, 0);
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

        // 최종 경로 그리기
        if (FinalNodeList != null && FinalNodeList.Count > 0)
        {
            Gizmos.color = Color.green;
            for (int i = 0; i < FinalNodeList.Count - 1; i++)
            {
                // ✅ [수정] 경로 라인도 타일 중앙을 지나도록 0.5f씩 더해줍니다.
                Vector3 from = new Vector3(FinalNodeList[i].x + 0.5f, FinalNodeList[i].y + 0.5f, 0);
                Vector3 to = new Vector3(FinalNodeList[i + 1].x + 0.5f, FinalNodeList[i + 1].y + 0.5f, 0);
                Gizmos.DrawLine(from, to);
            }
        }

        // 시작점과 도착점 그리기
        Gizmos.color = Color.blue;
        // ✅ [수정] 시작/도착점 구체도 타일 중앙에 그려지도록 0.5f씩 더해줍니다.
        Gizmos.DrawSphere(new Vector3(startPos.x + 0.5f, startPos.y + 0.5f, 0), 0.5f);
        Gizmos.color = Color.yellow;
        Gizmos.DrawSphere(new Vector3(targetPos.x + 0.5f, targetPos.y + 0.5f, 0), 0.5f);
    }
}