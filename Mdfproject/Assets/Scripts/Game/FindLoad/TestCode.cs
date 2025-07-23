using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TestCode : MonoBehaviour
{
    public Vector2Int bottomLeft, topRight, startPos, targetPos;
    public List<Node> FinalNodeList;
    public bool allowDiagonal, dontCrossCorner;
    [Header("디버깅")]
    public bool showDebugInfo = true; // 디버그 정보 표시 여부
    public float detectionRadius = 0.4f; // 감지 반지름 조절 가능하게

    int sizeX, sizeY;
    Node[,] NodeArray;
    Node StartNode, TargetNode, CurNode;
    List<Node> OpenList, ClosedList;

    public void PathFinding()
    {
        // NodeArray의 크기 정해주고, isWall, x, y 대입
        sizeX = topRight.x - bottomLeft.x + 1;
        sizeY = topRight.y - bottomLeft.y + 1;
        NodeArray = new Node[sizeX, sizeY];

        int wallCount = 0; // 벽 개수 카운트

        for (int i = 0; i < sizeX; i++)
        {
            for (int j = 0; j < sizeY; j++)
            {
                bool isWall = false;
                Vector2 checkPos = new Vector2(i + bottomLeft.x, j + bottomLeft.y);
                
                // 모든 콜라이더 검사
                Collider2D[] colliders = Physics2D.OverlapCircleAll(checkPos, detectionRadius);
                
                if (showDebugInfo && colliders.Length > 0)
                {
                    Debug.Log($"위치 ({checkPos.x}, {checkPos.y})에서 {colliders.Length}개의 콜라이더 발견:");
                }

                foreach (Collider2D col in colliders)
                {
                    if (showDebugInfo)
                    {
                        Debug.Log($"- 오브젝트: {col.gameObject.name}, 레이어: {LayerMask.LayerToName(col.gameObject.layer)}, 타입: {col.GetType().Name}");
                    }

                    if (col.gameObject.layer == LayerMask.NameToLayer("Wall"))
                    {
                        isWall = true;
                        wallCount++;
                        if (showDebugInfo)
                        {
                            Debug.Log($"★ 벽 발견! 위치: ({checkPos.x}, {checkPos.y})");
                        }
                    }
                }

                NodeArray[i, j] = new Node(isWall, i + bottomLeft.x, j + bottomLeft.y);
            }
        }

        Debug.Log($"총 {wallCount}개의 벽이 인식되었습니다.");

        // 기존 pathfinding 로직...
        StartNode = NodeArray[startPos.x - Mathf.Abs(bottomLeft.x), startPos.y - Mathf.Abs(bottomLeft.y)];
        TargetNode = NodeArray[targetPos.x - bottomLeft.x, targetPos.y - Mathf.Abs(bottomLeft.y)];

        OpenList = new List<Node>() { StartNode };
        ClosedList = new List<Node>();
        FinalNodeList = new List<Node>();

        while (OpenList.Count > 0)
        {
            CurNode = OpenList[0];
            for (int i = 1; i < OpenList.Count; i++)
                if (OpenList[i].F <= CurNode.F && OpenList[i].H < CurNode.H) CurNode = OpenList[i];

            OpenList.Remove(CurNode);
            ClosedList.Add(CurNode);

            if (CurNode == TargetNode)
            {
                Node TargetCurNode = TargetNode;
                while (TargetCurNode != StartNode)
                {
                    FinalNodeList.Add(TargetCurNode);
                    TargetCurNode = TargetCurNode.ParentNode;
                }
                FinalNodeList.Add(StartNode);
                FinalNodeList.Reverse();

                for (int i = 0; i < FinalNodeList.Count; i++) print(i + "번째는 " + FinalNodeList[i].x + ", " + FinalNodeList[i].y);
                return;
            }

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
    }

    // 수동으로 특정 위치의 콜라이더 검사하는 함수
    [ContextMenu("현재 위치 콜라이더 검사")]
    public void CheckCollidersAtPosition()
    {
        Vector2 testPos = new Vector2(0, 0); // 테스트할 위치
        Collider2D[] colliders = Physics2D.OverlapCircleAll(testPos, detectionRadius);
        
        Debug.Log($"위치 ({testPos.x}, {testPos.y})에서 콜라이더 검사 결과:");
        Debug.Log($"발견된 콜라이더 수: {colliders.Length}");
        
        foreach (Collider2D col in colliders)
        {
            Debug.Log($"오브젝트명: {col.gameObject.name}");
            Debug.Log($"레이어: {LayerMask.LayerToName(col.gameObject.layer)} (인덱스: {col.gameObject.layer})");
            Debug.Log($"콜라이더 타입: {col.GetType().Name}");
            Debug.Log($"Wall 레이어인가?: {col.gameObject.layer == LayerMask.NameToLayer("Wall")}");
            Debug.Log("---");
        }
    }

    void OnDrawGizmos()
    {
        // 기존 경로 그리기
        Gizmos.color = Color.red;  
        if (FinalNodeList != null && FinalNodeList.Count > 1) 
        {
            for (int i = 0; i < FinalNodeList.Count - 1; i++)
                Gizmos.DrawLine(new Vector2(FinalNodeList[i].x, FinalNodeList[i].y), new Vector2(FinalNodeList[i + 1].x, FinalNodeList[i + 1].y));
        }

        // 감지 범위 그리기 (디버깅용)
        if (showDebugInfo && NodeArray != null)
        {
            Gizmos.color = Color.yellow;
            for (int i = 0; i < sizeX; i++)
            {
                for (int j = 0; j < sizeY; j++)
                {
                    Vector3 pos = new Vector3(i + bottomLeft.x, j + bottomLeft.y, 0);
                    Gizmos.DrawWireSphere(pos, detectionRadius);
                    
                    // 벽인 경우 빨간색 사각형 그리기
                    if (NodeArray[i, j].isWall)
                    {
                        Gizmos.color = Color.red;
                        Gizmos.DrawWireCube(pos, Vector3.one * 0.8f);
                        Gizmos.color = Color.yellow;
                    }
                }
            }
        }
    }

    // 나머지 함수들은 기존과 동일...
    void OpenListAdd(int checkX, int checkY)
    {
        if (checkX >= bottomLeft.x && checkX < topRight.x + 1 && checkY >= bottomLeft.y && checkY < topRight.y + 1 && !NodeArray[checkX - bottomLeft.x, checkY - bottomLeft.y].isWall && !ClosedList.Contains(NodeArray[checkX - bottomLeft.x, checkY - bottomLeft.y]))
        {
            if (allowDiagonal) if (NodeArray[CurNode.x - bottomLeft.x, checkY - bottomLeft.y].isWall && NodeArray[checkX - bottomLeft.x, CurNode.y - bottomLeft.y].isWall) return;
            if (dontCrossCorner) if (NodeArray[CurNode.x - bottomLeft.x, checkY - bottomLeft.y].isWall || NodeArray[checkX - bottomLeft.x, CurNode.y - bottomLeft.y].isWall) return;

            Node NeighborNode = NodeArray[checkX - bottomLeft.x, checkY - bottomLeft.y];
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
}

[System.Serializable]
public class Node
{
    public Node(bool _isWall, int _x, int _y) { isWall = _isWall; x = _x; y = _y; }

    public bool isWall;
    public Node ParentNode;
    public int x, y, G, H;
    public int F { get { return G + H; } }
}