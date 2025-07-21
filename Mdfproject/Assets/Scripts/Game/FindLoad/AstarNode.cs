using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

public class AstarNode
{
    public bool walkable;
    public Vector2 worldPosition;
    public int x;
    public int y;
    public int fCost{ get { return gCost + hCost;} }
    public int gCost;
    public int hCost;

    public AstarNode parent;

    public AstarNode(bool walkable, Vector2 worldPos, int x, int y)
    {
        this.walkable = walkable;
        this.worldPosition = worldPos;
        this.x = x;
        this.y = y;
    }
}
