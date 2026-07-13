using System;
using UnityEngine;

/// <summary>
/// Immutable geometry for one field. It owns coordinate math only; occupancy,
/// authority and presentation remain in FieldManager.
/// </summary>
public readonly struct FieldGridGeometry
{
    private readonly Vector3 _origin;
    private readonly float _cellSize;
    private readonly Vector2Int _innerSize;
    private readonly int _outerMargin;

    public FieldGridGeometry(Vector3 origin, float cellSize, Vector2Int innerSize, int outerMargin)
    {
        _origin = origin;
        _cellSize = Mathf.Max(Mathf.Epsilon, cellSize);
        _innerSize = new Vector2Int(Mathf.Max(0, innerSize.x), Mathf.Max(0, innerSize.y));
        _outerMargin = Mathf.Max(0, outerMargin);
    }

    public Vector2Int InnerSize => _innerSize;

    public Vector2Int TotalSize => new Vector2Int(
        _innerSize.x + _outerMargin * 2,
        _innerSize.y + _outerMargin * 2);

    public Vector3 TotalOrigin => new Vector3(
        _origin.x - _outerMargin * _cellSize,
        _origin.y,
        _origin.z - _outerMargin * _cellSize);

    public Vector3 InnerCellToWorld(Vector2Int cell, float worldY)
    {
        return CellToWorld(_origin, cell, worldY);
    }

    public Vector2Int WorldToInnerCell(Vector3 worldPosition)
    {
        return WorldToCellClamped(worldPosition, _origin, _innerSize);
    }

    public bool TryWorldToInnerCell(Vector3 worldPosition, out Vector2Int cell)
    {
        cell = new Vector2Int(
            Mathf.FloorToInt((worldPosition.x - _origin.x) / _cellSize),
            Mathf.FloorToInt((worldPosition.z - _origin.z) / _cellSize));
        return IsValidInner(cell);
    }

    public Vector2Int InnerToNavigation(Vector2Int innerCell)
    {
        return new Vector2Int(innerCell.x + _outerMargin, innerCell.y + _outerMargin);
    }

    public bool TryNavigationToInner(Vector2Int navigationCell, out Vector3Int innerCell)
    {
        innerCell = new Vector3Int(
            navigationCell.x - _outerMargin,
            navigationCell.y - _outerMargin,
            0);
        return IsValidInner(new Vector2Int(innerCell.x, innerCell.y));
    }

    public bool IsValidInner(Vector2Int cell)
    {
        return cell.x >= 0 && cell.x < _innerSize.x && cell.y >= 0 && cell.y < _innerSize.y;
    }

    public bool IsValidNavigation(Vector2Int cell)
    {
        Vector2Int totalSize = TotalSize;
        return cell.x >= 0 && cell.x < totalSize.x && cell.y >= 0 && cell.y < totalSize.y;
    }

    public Vector2Int WorldToNavigation(Vector3 worldPosition)
    {
        return WorldToCellClamped(worldPosition, TotalOrigin, TotalSize);
    }

    public Vector3 NavigationCellToWorld(Vector2Int cell, float worldY)
    {
        return CellToWorld(TotalOrigin, cell, worldY);
    }

    public Vector3 ClampWorldToInnerBounds(Vector3 worldPosition)
    {
        float maxXExclusive = _origin.x + _innerSize.x * _cellSize;
        float maxZExclusive = _origin.z + _innerSize.y * _cellSize;
        float epsilon = Mathf.Max(1e-4f * _cellSize, Mathf.Epsilon);
        float maxX = Mathf.Max(_origin.x, maxXExclusive - epsilon);
        float maxZ = Mathf.Max(_origin.z, maxZExclusive - epsilon);
        return new Vector3(
            Mathf.Clamp(worldPosition.x, _origin.x, maxX),
            worldPosition.y,
            Mathf.Clamp(worldPosition.z, _origin.z, maxZ));
    }

    public Vector3Int[] BuildBorderGapCells()
    {
        if (_innerSize.x <= 0 || _innerSize.y <= 0)
        {
            return Array.Empty<Vector3Int>();
        }

        int centerX = _innerSize.x / 2;
        int centerY = _innerSize.y / 2;
        return new[]
        {
            new Vector3Int(centerX, _innerSize.y - 1, 0),
            new Vector3Int(centerX, 0, 0),
            new Vector3Int(_innerSize.x - 1, centerY, 0),
            new Vector3Int(0, centerY, 0)
        };
    }

    private Vector3 CellToWorld(Vector3 origin, Vector2Int cell, float worldY)
    {
        return new Vector3(
            origin.x + (cell.x + 0.5f) * _cellSize,
            worldY,
            origin.z + (cell.y + 0.5f) * _cellSize);
    }

    private Vector2Int WorldToCellClamped(Vector3 worldPosition, Vector3 origin, Vector2Int size)
    {
        if (size.x <= 0 || size.y <= 0)
        {
            return Vector2Int.zero;
        }

        int x = Mathf.FloorToInt((worldPosition.x - origin.x) / _cellSize);
        int y = Mathf.FloorToInt((worldPosition.z - origin.z) / _cellSize);
        return new Vector2Int(
            Mathf.Clamp(x, 0, size.x - 1),
            Mathf.Clamp(y, 0, size.y - 1));
    }
}
