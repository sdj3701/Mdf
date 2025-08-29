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
    // [추가됨] 타일 대신 생성할 벽 Prefab을 인스펙터에서 연결해줍니다.
    [Header("배치 프리팹")]
    public GameObject destructibleWallPrefab;

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
    private FieldManager fieldManager;

    private Tilemap groundTilemap => GameAssets.TileMaps.GroundTilemap;
    private Tilemap obstacleTilemap => GameAssets.TileMaps.BreakWallTilemap;
    private Camera playerCamera => GameAssets.Cameras.MainCamera;
    // [제거됨] 이제 TileBase는 프리뷰에만 사용됩니다.
    // private TileBase wallTileToPlace => GameAssets.Tiles.BreakWall;

    private readonly Plane gamePlane = new Plane(Vector3.forward, 0);

    void Awake()
    {
        fieldManager = GetComponent<FieldManager>();
        playerManager = fieldManager.playerManager;
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
    
    public PlacementMode GetCurrentMode() => currentMode;

    public void StartPlacementMode(PlacementMode mode, GameObject unitPrefab = null)
    {
        if (GameManagers.Instance.GetGameState() != GameManagers.GameState.Prepare) return;
        currentMode = mode;
        unitPrefabToPlace = unitPrefab;
        SetupPreviewObject();
    }

    public void StopPlacementMode()
    {
        currentMode = PlacementMode.None;
    }
    
    public bool IsPositionValidForPlacement(Vector3Int gridPosition, UnitData unitData = null)
    {
        bool hasGroundTile = groundTilemap.GetTile(gridPosition) != null;
        // [수정됨] 이제 타일맵이 아닌, 해당 위치에 벽 오브젝트가 있는지 확인합니다.
        bool hasObstacle = fieldManager.GetWallAt(gridPosition) != null;
        bool hasUnit = fieldManager.IsUnitAt(gridPosition);

        if (hasUnit || !hasGroundTile) return false;
        
        if (hasObstacle)
        {
            return unitData != null && unitData.unitType == UnitType.Ranged;
        }
        
        return true;
    }
    
    #endregion
    
    #region Input & Placement Logic

    private void HandleMouseInput()
    {
        if (Input.GetMouseButtonDown(0)) TryPlace();
        if (Input.GetMouseButtonDown(1))
        {
            if (!TryRemoveWall()) StopPlacementMode();
        }
    }

    private void TryPlace()
    {
        UnitData dataToPlace = (currentMode == PlacementMode.Unit && unitPrefabToPlace != null)
            ? unitPrefabToPlace.GetComponent<Unit>().Data
            : null;

        if (!IsPositionValidForPlacement(currentMouseGridPosition, dataToPlace)) return;

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
                // [핵심 수정] 타일을 그리는 대신, Prefab을 생성합니다.
                if (playerManager.TryUseWall())
                {
                    // FieldManager에게 벽 생성을 요청합니다.
                    fieldManager.CreateWallAt(destructibleWallPrefab, currentMouseGridPosition);
                    GameEvents.TriggerWallPlaced(playerManager.playerId, currentMouseGridPosition);
                }
                break;
        }
    }

    private bool TryRemoveWall()
    {
        // [핵심 수정] 타일을 지우는 대신, 해당 위치의 벽 오브젝트를 제거합니다.
        if (currentMode == PlacementMode.Wall)
        {
            DestructibleWall wallToRemove = fieldManager.GetWallAt(currentMouseGridPosition);
            if (wallToRemove != null)
            {
                // FieldManager에게 벽 제거를 요청합니다.
                fieldManager.RemoveWallAt(currentMouseGridPosition);
                playerManager.ReturnWall();
                GameEvents.TriggerWallRemoved(playerManager.playerId, currentMouseGridPosition);
                return true;
            }
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
        else if (currentMode == PlacementMode.Wall && destructibleWallPrefab != null)
        {
            // [수정됨] 프리팹에서 스프라이트를 가져옵니다.
            previewSprite = destructibleWallPrefab.GetComponent<SpriteRenderer>()?.sprite;
        }
        
        previewRenderer.sprite = previewSprite;
        previewObject.SetActive(previewSprite != null);
    }

    private void UpdatePreviewDisplay()
    {
        if (previewObject == null || !previewObject.activeSelf) return;

        Vector3 worldPos = obstacleTilemap.CellToWorld(currentMouseGridPosition) + (obstacleTilemap.cellSize * 0.5f);
        previewObject.transform.position = worldPos;
        
        UnitData dataToPlace = (currentMode == PlacementMode.Unit && unitPrefabToPlace != null)
            ? unitPrefabToPlace.GetComponent<Unit>().Data
            : null;

        previewRenderer.color = IsPositionValidForPlacement(currentMouseGridPosition, dataToPlace) ? validPreviewColor : invalidPreviewColor;
    }
    
    #endregion
}