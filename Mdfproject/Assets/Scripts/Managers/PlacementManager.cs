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

    // --- [수정된 부분] ---
    private PlayerManager playerManager; // 이제 null이 되지 않도록 참조를 받아옵니다.
    private FieldManager fieldManager;

    private Tilemap groundTilemap => GameAssets.TileMaps.GroundTilemap;
    private Tilemap obstacleTilemap => GameAssets.TileMaps.BreakWallTilemap;
    private Camera playerCamera => GameAssets.Cameras.MainCamera;
    private TileBase wallTileToPlace => GameAssets.Tiles.BreakWall;

    private readonly Plane gamePlane = new Plane(Vector3.forward, 0);

    // [수정됨] Awake에서 FieldManager를 통해 PlayerManager 참조를 설정합니다.
    void Awake()
    {
        fieldManager = GetComponent<FieldManager>();
        if (fieldManager == null)
        {
            Debug.LogError("PlacementManager가 FieldManager를 찾을 수 없습니다!", gameObject);
            this.enabled = false;
            return;
        }

        // [핵심 수정] FieldManager로부터 PlayerManager 참조를 받아옵니다.
        // 이렇게 하면 playerManager가 더 이상 null이 아니게 됩니다.
        playerManager = fieldManager.playerManager;
        if (playerManager == null)
        {
            Debug.LogError("PlacementManager가 FieldManager로부터 PlayerManager 참조를 받아오지 못했습니다!", gameObject);
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

    #region Public Methods

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
        if (groundTilemap == null || obstacleTilemap == null || fieldManager == null)
        {
            Debug.LogWarning("[PlacementManager] 타일맵 또는 FieldManager 참조를 찾을 수 없습니다.");
            return false;
        }

        bool hasGroundTile = groundTilemap.GetTile(gridPosition) != null;
        bool hasObstacle = obstacleTilemap.GetTile(gridPosition) != null;
        bool hasUnit = fieldManager.IsUnitAt(gridPosition);

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
            if (!TryRemoveWall())
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
                    fieldManager.CreateAndPlaceUnitFromPlacement(unitPrefabToPlace, currentMouseGridPosition);
                    StopPlacementMode();
                }
                break;
            case PlacementMode.Wall:
                // 이제 playerManager가 null이 아니므로 이 코드는 안전하게 실행됩니다.
                if (playerManager.TryUseWall())
                {
                    obstacleTilemap.SetTile(currentMouseGridPosition, wallTileToPlace);
                    GameEvents.TriggerWallPlaced(playerManager.playerId, currentMouseGridPosition);
                }
                break;
        }
    }

    private bool TryRemoveWall()
    {
        if (currentMode == PlacementMode.Wall && obstacleTilemap.GetTile(currentMouseGridPosition) != null)
        {
            obstacleTilemap.SetTile(currentMouseGridPosition, null);
            playerManager.ReturnWall();
            GameEvents.TriggerWallRemoved(playerManager.playerId, currentMouseGridPosition);
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