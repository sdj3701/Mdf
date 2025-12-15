using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

public class AstarGrid : MonoBehaviour
{
    public bool displayGridGizmos;
    public Transform monster;
    
    // ⭐ LayerMask 분리
    public LayerMask GROUND;     // 이동 가능한 지면
    public LayerMask OBSTACLE;   // 장애물
    
    public Vector2Int BottomLeft, TopRight;
    public Vector2Int StartPosition;
    public Vector2Int EndPosition;
    public List<AstarNode> FinalNodeList;
    
    public float nodeRadius = 0.4f;
    
    // 전체 맵
    AstarNode[,] grid;
    AstarNode startNode, endNode, curNode;
    List<AstarNode> openList, closedList;
    
    public bool allowDiagonal, dontCrossCorner;
    public bool useLayerBasedRange = false; // 레이어 기반 범위 체크 사용 여부
    
    int gridSizeX, gridSizeY;

    void Awake()
    {
        Debug.Log("=== AStar 초기화 시작 ===");
        
        gridSizeX = TopRight.x - BottomLeft.x + 1;
        gridSizeY = TopRight.y - BottomLeft.y + 1;
        grid = new AstarNode[gridSizeX, gridSizeY];
        
        Debug.Log($"📋 설정 정보:");
        Debug.Log($"   BottomLeft: {BottomLeft}");
        Debug.Log($"   TopRight: {TopRight}");
        Debug.Log($"   GridSize: {gridSizeX} x {gridSizeY}");
        Debug.Log($"   StartPosition: {StartPosition}");
        Debug.Log($"   EndPosition: {EndPosition}");
        Debug.Log($"   useLayerBasedRange: {useLayerBasedRange}");
        
        // ⭐ 좌표 기반 사용 권장
        if (useLayerBasedRange)
        {
            Debug.LogWarning("⚠️ LayerMask 방식은 Ground에 콜라이더가 필요합니다!");
            Debug.LogWarning("💡 간단한 게임이라면 useLayerBasedRange = false 권장");
        }
        
        CheckObstacle();
        
        // 📋 상세한 위치 검증
        Debug.Log("\n🔍 위치 검증:");
        Debug.Log($"시작점 ({StartPosition.x}, {StartPosition.y}) 검증:");
        bool startValid = IsValidPositionDetailed(StartPosition, "시작점");
        
        Debug.Log($"\n끝점 ({EndPosition.x}, {EndPosition.y}) 검증:");
        bool endValid = IsValidPositionDetailed(EndPosition, "끝점");
        
        if (!startValid || !endValid)
        {
            Debug.LogError($"❌ 위치 오류! 시작: {StartPosition} (유효: {startValid}), 끝: {EndPosition} (유효: {endValid})");
            Debug.LogError("💡 해결 방법:");
            Debug.LogError("   1. TopRight 값을 늘리거나");
            Debug.LogError("   2. useLayerBasedRange = false로 설정하거나");
            Debug.LogError("   3. 시작/끝점을 범위 안으로 이동");
            return;
        }
        
        startNode = grid[StartPosition.x - BottomLeft.x, StartPosition.y - BottomLeft.y];
        endNode = grid[EndPosition.x - BottomLeft.x, EndPosition.y - BottomLeft.y];
        
        FinalNodeList = new List<AstarNode>();
        
        Debug.Log($"\n✅ 초기화 완료!");
        Debug.Log($"   시작점: ({StartPosition.x}, {StartPosition.y}) - Walkable: {startNode.walkable}");
        Debug.Log($"   끝점: ({EndPosition.x}, {EndPosition.y}) - Walkable: {endNode.walkable}");
    }

    // ⭐ 개선된 범위 체크 (LayerMask 기반)
    bool IsValidPosition(Vector2Int pos)
    {
        // 기본 좌표 범위 체크
        if (pos.x < BottomLeft.x || pos.x > TopRight.x || 
            pos.y < BottomLeft.y || pos.y > TopRight.y)
            return false;
            
        // LayerMask 기반 범위 체크 (옵션)
        if (useLayerBasedRange)
        {
            return HasGroundAt(pos);
        }
        
        return true;
    }
    
    // 🔍 상세한 위치 검증 (디버깅용)
    bool IsValidPositionDetailed(Vector2Int pos, string label)
    {
        Debug.Log($"  📍 {label} 상세 검증:");
        
        // 1. 좌표 범위 체크
        bool coordValid = !(pos.x < BottomLeft.x || pos.x > TopRight.x || 
                           pos.y < BottomLeft.y || pos.y > TopRight.y);
        
        Debug.Log($"     좌표 범위: {pos} vs 범위({BottomLeft}~{TopRight}) → {(coordValid ? "✅" : "❌")}");
        
        if (!coordValid)
        {
            if (pos.x < BottomLeft.x) Debug.Log($"       X가 너무 작음: {pos.x} < {BottomLeft.x}");
            if (pos.x > TopRight.x) Debug.Log($"       X가 너무 큼: {pos.x} > {TopRight.x}");
            if (pos.y < BottomLeft.y) Debug.Log($"       Y가 너무 작음: {pos.y} < {BottomLeft.y}");
            if (pos.y > TopRight.y) Debug.Log($"       Y가 너무 큼: {pos.y} > {TopRight.y}");
            return false;
        }
        
        // 2. LayerMask 기반 체크
        if (useLayerBasedRange)
        {
            bool hasGround = HasGroundAtDetailed(pos);
            Debug.Log($"     Ground 체크: {(hasGround ? "✅" : "❌")}");
            return hasGround;
        }
        
        Debug.Log($"     LayerMask 체크 스킵 (useLayerBasedRange = false)");
        return true;
    }
    
    // Ground 레이어 체크 함수
    bool HasGroundAt(Vector2Int pos)
    {
        Vector2 worldPos = new Vector2(pos.x, pos.y);
        Collider2D[] colliders = Physics2D.OverlapCircleAll(worldPos, nodeRadius);
        
        foreach (Collider2D col in colliders)
        {
            if (((1 << col.gameObject.layer) & GROUND) != 0)
            {
                return true;
            }
        }
        return false;
    }
    
    // 🔍 상세한 Ground 체크 (디버깅용)
    bool HasGroundAtDetailed(Vector2Int pos)
    {
        Vector2 worldPos = new Vector2(pos.x, pos.y);
        Collider2D[] colliders = Physics2D.OverlapCircleAll(worldPos, nodeRadius);
        
        Debug.Log($"       위치 ({pos.x}, {pos.y})에서 {colliders.Length}개 콜라이더 발견");
        
        if (colliders.Length == 0)
        {
            Debug.Log($"       ❌ 콜라이더 없음");
            return false;
        }
        
        foreach (Collider2D col in colliders)
        {
            int layer = col.gameObject.layer;
            string layerName = LayerMask.LayerToName(layer);
            bool isGround = ((1 << layer) & GROUND) != 0;
            
            Debug.Log($"       - {col.name}: 레이어 {layer}({layerName}) → Ground: {(isGround ? "✅" : "❌")}");
            
            if (isGround)
                return true;
        }
        
        Debug.Log($"       ❌ Ground 레이어 없음");
        return false;
    }

    // Unity Tilemap 지원 추가
    public Tilemap groundTilemap;    // Inspector에서 할당
    public Tilemap obstacleTilemap;  // Inspector에서 할당
    public bool useTilemapDetection = false;

    // 장애물 체크 (최적화된 버전)
    public void CheckObstacle()
    {
        for (int i = 0; i < gridSizeX; i++)
        {
            for (int j = 0; j < gridSizeY; j++)
            {
                Vector2 worldPos = new Vector2(i + BottomLeft.x, j + BottomLeft.y);
                bool isWalkable = false;
                
                if (useTilemapDetection && groundTilemap != null)
                {
                    // 🎯 Tilemap 방식 (콜라이더 불필요!)
                    Vector3Int cellPos = groundTilemap.WorldToCell(worldPos);
                    
                    bool hasGroundTile = groundTilemap.GetTile(cellPos) != null;
                    bool hasObstacleTile = false;
                    
                    if (obstacleTilemap != null)
                        hasObstacleTile = obstacleTilemap.GetTile(cellPos) != null;
                    
                    isWalkable = hasGroundTile && !hasObstacleTile;
                    
                    // 디버그 (첫 번째 줄만)
                    Debug.Log($"Tilemap[{i},{j}]: Ground={hasGroundTile}, Obstacle={hasObstacleTile}, Walkable={isWalkable}");
                }
                else
                {
                    // 🔸 기존 콜라이더 방식
                    Collider2D[] colliders = Physics2D.OverlapCircleAll(worldPos, nodeRadius);
                    
                    bool hasGround = !useLayerBasedRange; // LayerMask 사용 안하면 기본적으로 true
                    bool hasObstacle = false;
                    
                    foreach (Collider2D col in colliders)
                    {
                        int layer = col.gameObject.layer;
                        
                        if (useLayerBasedRange && ((1 << layer) & GROUND) != 0)
                            hasGround = true;
                            
                        if (((1 << layer) & OBSTACLE) != 0)
                            hasObstacle = true;
                    }
                    
                    isWalkable = hasGround && !hasObstacle;
                    
                    // 디버그 (첫 번째 줄만)
                    Debug.Log($"Collider[{i},{j}]: Ground={hasGround}, Obstacle={hasObstacle}, Walkable={isWalkable}");
                }
                
                grid[i, j] = new AstarNode(isWalkable, i + BottomLeft.x, j + BottomLeft.y);
            }
        }
    }

    // PathFinding 메서드
    public void PathFinding()
    {
        Debug.Log("🚀 PathFinding() 시작!");
        
        CheckObstacle();
        
        // startNode, endNode 재참조
        startNode = grid[StartPosition.x - BottomLeft.x, StartPosition.y - BottomLeft.y];
        endNode = grid[EndPosition.x - BottomLeft.x, EndPosition.y - BottomLeft.y];
        
        if (!startNode.walkable)
        {
            Debug.LogError("❌ 시작점이 이동 불가능한 곳입니다!");
            return;
        }
        if (!endNode.walkable)
        {
            Debug.LogError("❌ 끝점이 이동 불가능한 곳입니다!");
            return;
        }
        
        // 초기화
        openList = new List<AstarNode>() { startNode };
        closedList = new List<AstarNode>();
        FinalNodeList.Clear();
        
        // gCost 초기화
        for (int i = 0; i < gridSizeX; i++)
        {
            for (int j = 0; j < gridSizeY; j++)
            {
                grid[i, j].gCost = int.MaxValue;
                grid[i, j].parent = null;
            }
        }
        
        startNode.gCost = 0;
        startNode.hCost = (Mathf.Abs(startNode.x - endNode.x) + Mathf.Abs(startNode.y - endNode.y)) * 10;
        
        while (openList.Count > 0)
        {
            // 최적 노드 선택
            curNode = openList[0];
            for (int i = 1; i < openList.Count; i++)
                if (openList[i].fCost < curNode.fCost || 
                   (openList[i].fCost == curNode.fCost && openList[i].hCost < curNode.hCost))
                    curNode = openList[i];
                    
            openList.Remove(curNode);
            closedList.Add(curNode);
            
            // 목적지 도달
            if (curNode == endNode)
            {
                Debug.Log("🎉 경로 찾기 성공!");
                
                // 경로 역추적
                AstarNode targetNode = endNode;
                while (targetNode != startNode)
                {
                    FinalNodeList.Add(targetNode);
                    targetNode = targetNode.parent;
                }
                FinalNodeList.Add(startNode);
                FinalNodeList.Reverse();
                
                Debug.Log($"경로 길이: {FinalNodeList.Count}");
                return;
            }
            
            // 이웃 노드 탐색
            OpenListAdd(curNode.x, curNode.y + 1);
            OpenListAdd(curNode.x + 1, curNode.y);
            OpenListAdd(curNode.x, curNode.y - 1);
            OpenListAdd(curNode.x - 1, curNode.y);
            
            if (allowDiagonal)
            {
                OpenListAdd(curNode.x + 1, curNode.y + 1);
                OpenListAdd(curNode.x + 1, curNode.y - 1);
                OpenListAdd(curNode.x - 1, curNode.y + 1);
                OpenListAdd(curNode.x - 1, curNode.y - 1);
            }
        }
        
        Debug.LogWarning("❌ 경로를 찾을 수 없습니다!");
    }

    // ⭐ 개선된 OpenListAdd (LayerMask 기반 범위 체크)
    void OpenListAdd(int checkX, int checkY)
    {
        Vector2Int checkPos = new Vector2Int(checkX, checkY);
        
        // 범위 체크 (LayerMask 기반 또는 좌표 기반)
        if (!IsValidPosition(checkPos))
        {
            Debug.Log($"범위 벗어남: ({checkX}, {checkY}) - {(useLayerBasedRange ? "Ground 없음" : "좌표 범위 초과")}");
            return;
        }
        
        AstarNode neighborNode = grid[checkX - BottomLeft.x, checkY - BottomLeft.y];
        
        // walkable 체크
        if (!neighborNode.walkable)
        {
            Debug.Log($"이동 불가: ({checkX}, {checkY}) - 장애물");
            return;
        }
        
        if (closedList.Contains(neighborNode))
        {
            Debug.Log($"이미 방문: ({checkX}, {checkY})");
            return;
        }
        
        // 대각선 코너 체크
        if (allowDiagonal && dontCrossCorner)
        {
            bool isDiagonal = (checkX != curNode.x && checkY != curNode.y);
            if (isDiagonal)
            {
                AstarNode horizontal = grid[checkX - BottomLeft.x, curNode.y - BottomLeft.y];
                AstarNode vertical = grid[curNode.x - BottomLeft.x, checkY - BottomLeft.y];
                
                if (!horizontal.walkable || !vertical.walkable)
                    return;
            }
        }
        
        // 비용 계산
        bool isDiagonalMove = (checkX != curNode.x && checkY != curNode.y);
        int moveCost = curNode.gCost + (isDiagonalMove ? 14 : 10);
        
        if (moveCost < neighborNode.gCost)
        {
            neighborNode.gCost = moveCost;
            neighborNode.hCost = (Mathf.Abs(neighborNode.x - endNode.x) + Mathf.Abs(neighborNode.y - endNode.y)) * 10;
            neighborNode.parent = curNode;
            
            if (!openList.Contains(neighborNode))
            {
                openList.Add(neighborNode);
                Debug.Log($"✅ OpenList 추가: ({checkX}, {checkY}), Count: {openList.Count}");
            }
        }
    }

    // 기즈모 표시
    void OnDrawGizmos()
    {
        // 시작점과 끝점
        if (startNode != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawSphere(new Vector2(startNode.x, startNode.y), 0.3f);
        }
        
        if (endNode != null)
        {
            Gizmos.color = Color.blue;
            Gizmos.DrawSphere(new Vector2(endNode.x, endNode.y), 0.3f);
        }
        
        // 그리드 표시
        if (displayGridGizmos && grid != null)
        {
            for (int i = 0; i < gridSizeX; i++)
            {
                for (int j = 0; j < gridSizeY; j++)
                {
                    Vector2 worldPos = new Vector2(i + BottomLeft.x, j + BottomLeft.y);
                    
                    if (grid[i, j].walkable)
                        Gizmos.color = Color.white;
                    else
                        Gizmos.color = Color.black;
                    
                    Gizmos.DrawWireCube(worldPos, Vector2.one * 0.9f);
                }
            }
        }
        
        // 경로 표시
        if (FinalNodeList != null && FinalNodeList.Count > 0)
        {
            Gizmos.color = Color.red;
            for (int i = 0; i < FinalNodeList.Count - 1; i++)
            {
                if (FinalNodeList[i] != null && FinalNodeList[i + 1] != null)
                {
                    Vector2 from = new Vector2(FinalNodeList[i].x, FinalNodeList[i].y);
                    Vector2 to = new Vector2(FinalNodeList[i + 1].x, FinalNodeList[i + 1].y);
                    Gizmos.DrawLine(from, to);
                }
            }
        }
    }

    [ContextMenu("🚀 PathFinding 테스트")]
    public void TestPathFinding()
    {
        PathFinding();
    }
}