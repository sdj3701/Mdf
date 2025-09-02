using UnityEngine;
using GameCore.Enums;

public interface IPlacementHandler
{
    bool CanHandle(PlacementMode mode);
    void HandleInput();
    void OnMousePositionChanged(Vector3Int gridPosition);
    void OnModeChanged(PlacementMode mode);
}