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
    [Header("배치 프리팹 및 타일")]
    public TileBase wallTileToPlace;

    [Header("프리뷰 설정")]
    [SerializeField] private bool showPreview = true;
    [SerializeField] private Color validPreviewColor = new Color(0f, 1f, 0f, 0.5f);
    [SerializeField] private Color invalidPreviewColor = new Color(1f, 0f, 0f, 0.5f);

    private Tilemap groundTilemap;
    private Tilemap obstacleTilemap;

    private PlacementMode currentMode = PlacementMode.None;
    private GameObject unitPrefabToPlace;
    private GameObject previewObject;
    private SpriteRenderer previewRenderer;
    private Vector3Int currentMouseGridPosition;

    private PlayerManager playerManager;
    private FieldManager fieldManager;
    
    private Camera playerCamera => GameAssets.Cameras.MainCamera;
    private readonly Plane gamePlane = new Plane(Vector3.forward, 0);

    void Awake()
    {
        fieldManager = GetComponent<FieldManager>();
        
        // ✅ [수정된 핵심 로직] FieldManager가 먼저 초기화되기를 기다리기 때문에,
        // 참조를 받아오는 부분을 Start로 옮겨 안정성을 높입니다.
    }

    void Start()
    {
        // FieldManager는 Awake에서 초기화되므로, Start에서 참조를 가져오면 안전합니다.
        this.playerManager = fieldManager.playerManager;
        this.groundTilemap = fieldManager.GroundTilemap;
        this.obstacleTilemap = fieldManager.ObstacleTilemap;

        if (groundTilemap == null || obstacleTilemap == null)
        {
            Debug.LogError("PlacementManager가 FieldManager로부터 Tilemap 참조를 받아오지 못했습니다!", gameObject);
        }
    }
    
    // ... (이하 나머지 코드는 이전과 동일) ...
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
        if (groundTilemap == null) return false;

        bool hasGroundTile = groundTilemap.GetTile(gridPosition) != null;
        bool hasObstacle = fieldManager.GetWallAt(gridPosition) != null;
        bool hasUnit = fieldManager.IsUnitAt(gridPosition);

        // 기본 조건: 유닛이 이미 있거나, 땅 타일이 없으면 배치 불가
        if (hasUnit || !hasGroundTile) return false;
        
        // 배치하려는 것이 유닛일 경우, 유닛 타입에 따른 규칙을 적용
        if (unitData != null)
        {
            // 근접 유닛은 장애물(언덕) 위에 배치할 수 없습니다.
            if (unitData.unitType == UnitType.Melee && hasObstacle)
            {
                return false;
            }
        }
        // 배치하려는 것이 유닛이 아닐 경우 (예: 벽), 장애물 위에 놓을 수 없습니다.
        else if (hasObstacle)
        {
            return false;
        }
        
        // 위의 모든 금지 조건에 해당하지 않으면 배치 가능
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
                if (playerManager.TryUseWall())
                {
                    fieldManager.CreateWallAt(currentMouseGridPosition);
                    if (obstacleTilemap != null && wallTileToPlace != null)
                    {
                        obstacleTilemap.SetTile(currentMouseGridPosition, wallTileToPlace);
                    }
                    GameEvents.TriggerWallPlaced(playerManager.playerId, currentMouseGridPosition);
                }
                break;
        }
    }

    private bool TryRemoveWall()
    {
        if (currentMode == PlacementMode.Wall)
        {
            DestructibleWall wallToRemove = fieldManager.GetWallAt(currentMouseGridPosition);
            if (wallToRemove != null)
            {
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
        else if (currentMode == PlacementMode.Wall && fieldManager.destructibleWallPrefab != null)
        {
            previewSprite = fieldManager.destructibleWallPrefab.GetComponent<SpriteRenderer>()?.sprite;
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
