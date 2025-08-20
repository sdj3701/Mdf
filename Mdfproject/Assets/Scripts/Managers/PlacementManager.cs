// Assets/Scripts/Managers/PlacementManager.cs
using UnityEngine;
using UnityEngine.Tilemaps;
using System.Collections.Generic;

public enum PlacementMode
{
    None,
    Unit,
    Wall
}

[RequireComponent(typeof(FieldManager))]
public class PlacementManager : MonoBehaviour
{
    [Header("프리뷰 설정")]
    [SerializeField] private bool showPreview = true;
    [SerializeField] private Color validPreviewColor = new Color(0f, 1f, 0f, 0.5f);
    [SerializeField] private Color invalidPreviewColor = new Color(1f, 0f, 0f, 0.5f);

    private PlacementMode currentMode = PlacementMode.None;
    private GameObject unitPrefabToPlace;
    private GameObject previewObject;
    private SpriteRenderer previewRenderer;
    private Vector3Int currentMouseGridPosition;

    private PlayerManager playerManager;
    private Dictionary<Vector3Int, GameObject> placedUnits;

    private Tilemap groundTilemap => GameAssets.TileMaps.GroundTilemap;
    private Tilemap obstacleTilemap => GameAssets.TileMaps.BreakWallTilemap;
    private Camera playerCamera => GameAssets.Cameras.MainCamera;
    private TileBase wallTileToPlace => GameAssets.Tiles.BreakWall;

    private readonly Plane gamePlane = new Plane(Vector3.forward, 0);

    public void Initialize(PlayerManager pm, Dictionary<Vector3Int, GameObject> units)
    {
        playerManager = pm;
        placedUnits = units;

        if (playerManager == null || placedUnits == null)
        {
            Debug.LogError("PlacementManager 초기화 실패: PlayerManager 또는 placedUnits 참조가 null입니다!", gameObject);
            this.enabled = false;
        }
    }

    void Update()
    {
        if (currentMode == PlacementMode.None)
        {
            if (previewObject != null && previewObject.activeSelf)
                previewObject.SetActive(false);
            return;
        }
        
        if (obstacleTilemap == null || playerCamera == null) return;

        UpdateMousePosition();
        HandleMouseInput();
        if (showPreview)
            UpdatePreviewDisplay();
    }

    #region Public Methods (FieldManager가 호출)

    public PlacementMode GetCurrentMode()
    {
        return currentMode;
    }

    public void StartPlacementMode(PlacementMode mode, GameObject unitPrefab = null)
    {
        if (GameManagers.Instance.GetGameState() != GameManagers.GameState.Prepare)
        {
            Debug.LogWarning("준비 단계에서만 배치할 수 있습니다.");
            return;
        }

        currentMode = mode;
        unitPrefabToPlace = unitPrefab;
        SetupPreviewObject();
    }

    public void StopPlacementMode()
    {
        currentMode = PlacementMode.None;
    }
    
    public bool IsPositionValidForPlacement(Vector3Int gridPosition)
    {
        // [안정성 강화 및 디버그 로그 추가]
        if (groundTilemap == null)
        {
            // 이 경고가 계속 보인다면 GameAssets 또는 ComponentRegistry 시스템 점검이 필요합니다.
            // Debug.LogWarningOnce()는 최신 Unity 버전에만 있으므로 LogWarning으로 대체합니다.
            Debug.LogWarning("[PlacementManager] GroundTilemap 참조를 찾을 수 없습니다. GameAssets 설정을 확인하세요.");
            return false;
        }
        if (obstacleTilemap == null)
        {
            Debug.LogWarning("[PlacementManager] ObstacleTilemap(BreakWall) 참조를 찾을 수 없습니다. GameAssets 설정을 확인하세요.");
            return false;
        }

        bool hasGroundTile = groundTilemap.GetTile(gridPosition) != null;
        bool hasObstacle = obstacleTilemap.GetTile(gridPosition) != null;
        bool hasUnit = placedUnits.ContainsKey(gridPosition);

        return hasGroundTile && !hasObstacle && !hasUnit;
    }

    #endregion

    #region Input & Placement Logic

    private void HandleMouseInput()
    {
        if (Input.GetMouseButtonDown(0))
        {
            TryPlace();
        }
        if (Input.GetMouseButtonDown(1))
        {
            if (!TryRemove())
            {
                StopPlacementMode();
            }
        }
    }

    private void TryPlace()
    {
        if (!IsPositionValidForPlacement(currentMouseGridPosition))
        {
            return;
        }

        switch (currentMode)
        {
            case PlacementMode.Unit:
                if (unitPrefabToPlace != null)
                {
                    playerManager.fieldManager.CreateAndPlaceUnitFromPlacement(unitPrefabToPlace, currentMouseGridPosition);
                    StopPlacementMode();
                }
                break;
            case PlacementMode.Wall:
                if (playerManager.TryUseWall())
                {
                    obstacleTilemap.SetTile(currentMouseGridPosition, wallTileToPlace);
                }
                break;
        }
    }

    private bool TryRemove()
    {
        if (obstacleTilemap.GetTile(currentMouseGridPosition) != null)
        {
            obstacleTilemap.SetTile(currentMouseGridPosition, null);
            playerManager.ReturnWall();
            return true;
        }
        
        return false;
    }

    #endregion

    #region Coordinate & Preview Logic

    private void UpdateMousePosition()
    {
        Vector3 mouseWorldPos = GetMouseWorldPosition();
        currentMouseGridPosition = obstacleTilemap.WorldToCell(mouseWorldPos);
    }

    private Vector3 GetMouseWorldPosition()
    {
        Ray cameraRay = playerCamera.ScreenPointToRay(Input.mousePosition);
        if (gamePlane.Raycast(cameraRay, out float enter))
        {
            return cameraRay.GetPoint(enter);
        }
        return Vector3.zero;
    }

    private void SetupPreviewObject()
    {
        if (previewObject == null)
        {
            previewObject = new GameObject("PlacementPreview");
            previewRenderer = previewObject.AddComponent<SpriteRenderer>();
            previewRenderer.sortingOrder = 10;
        }

        Sprite previewSprite = null;
        if (currentMode == PlacementMode.Unit && unitPrefabToPlace != null)
        {
            previewSprite = unitPrefabToPlace.GetComponentInChildren<SpriteRenderer>()?.sprite;
        }
        else if (currentMode == PlacementMode.Wall)
        {
            if (wallTileToPlace is Tile tileWithSprite)
            {
                previewSprite = tileWithSprite.sprite;
            }
        }
        
        previewRenderer.sprite = previewSprite;
        previewObject.SetActive(previewSprite != null);
    }

    private void UpdatePreviewDisplay()
    {
        if (previewObject == null || !previewObject.activeSelf) return;

        Vector3 worldPos = obstacleTilemap.CellToWorld(currentMouseGridPosition) + (obstacleTilemap.cellSize * 0.5f);
        previewObject.transform.position = worldPos;

        previewRenderer.color = IsPositionValidForPlacement(currentMouseGridPosition) ? validPreviewColor : invalidPreviewColor;
    }

    #endregion
}