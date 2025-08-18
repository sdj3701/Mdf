using UnityEngine;
using UnityEngine.Tilemaps;
using GameCore.Enums;

public class WallPlacementHandler : MonoBehaviour, IPlacementHandler
{
    private PlacementManager placementManager;
    private GameObject previewObject;

    private void Start()
    {
        placementManager = PlacementManager.Instance;

        // 이벤트 구독
        placementManager.OnMousePositionChanged += OnMousePositionChanged;
        placementManager.OnPlacementModeChanged += OnModeChanged;

        CreatePreviewObject();
    }

    private void OnDestroy()
    {
        if (placementManager != null)
        {
            placementManager.OnMousePositionChanged -= OnMousePositionChanged;
            placementManager.OnPlacementModeChanged -= OnModeChanged;
        }
    }

    public bool CanHandle(PlacementMode mode) => mode == PlacementMode.Wall;

    public void HandleInput()
    {
        if (Input.GetMouseButtonDown(0)) PlaceWall();
        if (Input.GetMouseButtonDown(1)) RemoveWall();
        if (Input.GetMouseButtonDown(2)) CheckWallInfo();
    }

    public void OnMousePositionChanged(Vector3Int gridPosition)
    {
        if (placementManager.CurrentPlacementMode == PlacementMode.Wall)
        {
            UpdatePreview();
        }
    }

    public void OnModeChanged(PlacementMode mode)
    {
        if (previewObject != null)
        {
            previewObject.SetActive(mode == PlacementMode.Wall && placementManager.ShowPreview);
        }
    }

    private void PlaceWall()
    {
        if (PlacementManager.CreateWallCount <= 0)
        {
            Debug.LogWarning("더 이상 벽을 배치할 수 없습니다!");
            return;
        }

        if (!placementManager.IsGroundLayer())
        {
            Debug.LogError("❌ Ground가 아닌 곳에는 벽을 설치할 수 없습니다!");
            return;
        }

        Vector3Int currentPos = placementManager.CurrentMouseGridPosition;
        TileBase existingTile = GameAssets.TileMaps.BreakWallTilemap.GetTile(currentPos);

        if (existingTile == null)
        {
            GameAssets.TileMaps.BreakWallTilemap.SetTile(currentPos, GameAssets.Tiles.BreakWall);
            PlacementManager.CreateWallCount--;
        }
        else
        {
            Debug.LogWarning($"⚠️ 이미 벽이 존재합니다: {currentPos}");
        }
    }

    private void RemoveWall()
    {
        if (PlacementManager.CreateWallCount >= 5)
        {
            Debug.LogWarning("벽을 제거할 수 없습니다! 최대 벽 개수에 도달했습니다.");
            return;
        }

        Vector3Int currentPos = placementManager.CurrentMouseGridPosition;
        TileBase existingTile = GameAssets.TileMaps.BreakWallTilemap.GetTile(currentPos);

        if (existingTile != null)
        {
            GameAssets.TileMaps.BreakWallTilemap.SetTile(currentPos, null);
            PlacementManager.CreateWallCount++;
        }
        else
        {
            Debug.Log($"제거할 벽이 없습니다: {currentPos}");
        }
    }

    private void CheckWallInfo()
    {
        Vector3Int currentPos = placementManager.CurrentMouseGridPosition;
        TileBase currentTile = GameAssets.TileMaps.BreakWallTilemap.GetTile(currentPos);
        Vector3 worldPosition = GameAssets.TileMaps.BreakWallTilemap.CellToWorld(currentPos);

        if (currentTile != null)
        {
            Debug.Log($"🧱 벽 타일 정보 - 그리드: {currentPos}, 월드: {worldPosition}, 타일명: {currentTile.name}");
        }
        else
        {
            Debug.Log($"⬜ 빈 공간 (벽 없음) - 그리드: {currentPos}, 월드: {worldPosition}");
        }
    }

    private void CreatePreviewObject()
    {
        previewObject = new GameObject("WallPreview");

        SpriteRenderer spriteRenderer = previewObject.AddComponent<SpriteRenderer>();

        Texture2D texture = new Texture2D(1, 1);
        texture.SetPixel(0, 0, Color.white);
        texture.Apply();

        Sprite previewSprite = Sprite.Create(texture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1);
        spriteRenderer.sprite = previewSprite;
        spriteRenderer.color = placementManager.PreviewColor;
        spriteRenderer.sortingOrder = 10;

        previewObject.SetActive(false);
    }

    private void UpdatePreview()
    {
        if (previewObject == null) return;

        Vector3Int currentPos = placementManager.CurrentMouseGridPosition;
        Vector3 previewWorldPosition = GameAssets.TileMaps.BreakWallTilemap.CellToWorld(currentPos);
        previewWorldPosition += GameAssets.TileMaps.BreakWallTilemap.cellSize * 0.5f;
        previewObject.transform.position = previewWorldPosition;

        SpriteRenderer spriteRenderer = previewObject.GetComponent<SpriteRenderer>();
        TileBase existingTile = GameAssets.TileMaps.BreakWallTilemap.GetTile(currentPos);

        if (existingTile != null)
        {
            spriteRenderer.color = Color.red;
        }
        else
        {
            spriteRenderer.color = placementManager.PreviewColor;
        }

        previewObject.SetActive(true);
    }
}