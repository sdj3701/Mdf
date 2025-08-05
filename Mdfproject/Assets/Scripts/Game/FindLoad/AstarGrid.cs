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

    int sizeX, sizeY;
    AstarNode[,] NodeArray;
    AstarNode StartNode, TargetNode, CurNode;
    List<AstarNode> OpenList, ClosedList;

    public GameObject monsterPrefab;  // ✅ 테스트할 몬스터 연결


    public void PathFinding()
    {
        // NodeArray의 크기 정해주고, isWall, x, y 대입
        sizeX = topRight.x - bottomLeft.x + 1;
        sizeY = topRight.y - bottomLeft.y + 1;
        NodeArray = new AstarNode[sizeX, sizeY];

        int wallCount = 0; // 벽 개수 카운트

        for (int i = 0; i < sizeX; i++)
        {
            for (int j = 0; j < sizeY; j++)
            {
                bool isWall = false;
                Vector2 checkPos = new Vector2(i + bottomLeft.x, j + bottomLeft.y);

                // 모든 콜라이더 검사
                Collider2D[] colliders = Physics2D.OverlapCircleAll(checkPos, detectionRadius);

                // if (showDebugInfo && colliders.Length > 0)
                // {
                //     Debug.Log($"위치 ({checkPos.x}, {checkPos.y})에서 {colliders.Length}개의 콜라이더 발견:");
                // }

                foreach (Collider2D col in colliders)
                {
                    // if (showDebugInfo)
                    // {
                    //     Debug.Log($"- 오브젝트: {col.gameObject.name}, 레이어: {LayerMask.LayerToName(col.gameObject.layer)}, 타입: {col.GetType().Name}");
                    // }

                    if (col.gameObject.layer == LayerMask.NameToLayer("Wall"))
                    {
                        isWall = true;
                        wallCount++;
                        // if (showDebugInfo)
                        // {
                        //     Debug.Log($"★ 벽 발견! 위치: ({checkPos.x}, {checkPos.y})");
                        // }
                    }
                }

                NodeArray[i, j] = new AstarNode(isWall, i + bottomLeft.x, j + bottomLeft.y);
            }
        }

        //Debug.Log($"총 {wallCount}개의 벽이 인식되었습니다.");

        // 인스펙터에서 시작 위치랑 
        StartNode = NodeArray[startPos.x - bottomLeft.x, startPos.y - bottomLeft.y];
        TargetNode = NodeArray[targetPos.x - bottomLeft.x, targetPos.y - bottomLeft.y];

        OpenList = new List<AstarNode>() { StartNode };
        ClosedList = new List<AstarNode>();
        FinalNodeList = new List<AstarNode>();

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
                AstarNode TargetCurNode = TargetNode;
                while (TargetCurNode != StartNode)
                {
                    FinalNodeList.Add(TargetCurNode);
                    TargetCurNode = TargetCurNode.ParentNode;
                }
                FinalNodeList.Add(StartNode);
                FinalNodeList.Reverse();

                for (int i = 0; i < FinalNodeList.Count; i++)
                    print(i + "번째는 " + FinalNodeList[i].x + ", " + FinalNodeList[i].y);

                if (monsterPrefab == null)
                    monsterPrefab = GameObject.Find("Monster");


                if (monsterPrefab != null && FinalNodeList.Count > 0)
                {
                    Debug.Log("🎯 경로 계산 완료! 몬스터 자동 이동 시작");
                    GameObject instance = Instantiate(monsterPrefab); // 인스턴스 생성
                    Monster monster = instance.GetComponent<Monster>();
                    monster.StartFollowingPath(FinalNodeList);
                }

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

    void OpenListAdd(int checkX, int checkY)
    {
        if (checkX >= bottomLeft.x && checkX < topRight.x + 1 && checkY >= bottomLeft.y && checkY < topRight.y + 1 && !NodeArray[checkX - bottomLeft.x, checkY - bottomLeft.y].isWall && !ClosedList.Contains(NodeArray[checkX - bottomLeft.x, checkY - bottomLeft.y]))
        {
            if (allowDiagonal) if (NodeArray[CurNode.x - bottomLeft.x, checkY - bottomLeft.y].isWall && NodeArray[checkX - bottomLeft.x, CurNode.y - bottomLeft.y].isWall) return;
            if (dontCrossCorner) if (NodeArray[CurNode.x - bottomLeft.x, checkY - bottomLeft.y].isWall || NodeArray[checkX - bottomLeft.x, CurNode.y - bottomLeft.y].isWall) return;

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
}