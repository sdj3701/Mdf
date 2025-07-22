using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

[System.Serializable]
public class AstarNode
{
    public bool walkable;
    public int x;
    public int y;
    public int fCost { get { return gCost + hCost; } }
    public int gCost;
    public int hCost;

    public AstarNode parent;
    public AstarNode(bool walkable, int x, int y)
    {
        this.walkable = walkable;
        this.x = x;
        this.y = y;
    }
}
/*
    G는 이동한 거리
    H는 목표 까지의 거리    |가로| + |세로| 장애물 무시하여
    F는 G + H
*/