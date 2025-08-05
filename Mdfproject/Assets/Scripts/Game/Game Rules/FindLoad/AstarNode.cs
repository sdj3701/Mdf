using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

[System.Serializable]
public class AstarNode
{
    public AstarNode(bool _isWall, int _x, int _y) { isWall = _isWall; x = _x; y = _y; }

    public bool isWall;
    public AstarNode ParentNode;
    public int x, y, G, H;
    public int F { get { return G + H; } }
}
/*
    G는 이동한 거리
    H는 목표 까지의 거리    |가로| + |세로| 장애물 무시하여
    F는 G + H
*/