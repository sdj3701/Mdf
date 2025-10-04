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

    private Tilemap groundTilemap;
    private Tilemap obstacleTilemap;

    private PlacementMode currentMode = PlacementMode.None;
    private GameObject unitPrefabToPlace;
    private GameObject previewObject;
    private SpriteRenderer previewRenderer;
    private Vector3Int currentMouseGridPosition;

    private PlayerManager playerManager;
    private FieldManager fieldManager;
    
    private Camera _cachedPlayerCamera;
    private Camera playerCamera
    {
        get
        {
            if (_cachedPlayerCamera == null)
            {
                _cachedPlayerCamera = GameAssets.Cameras.MainCamera;
                if (_cachedPlayerCamera == null)
                {
                    // Fallback: Find the main camera directly if not registered in ComponentRegistry
                    _cachedPlayerCamera = Camera.main;
                }
            }
            return _cachedPlayerCamera;
        }
    }
    private readonly Plane gamePlane = new Plane(Vector3.forward, 0);

    void Awake()
    {
        fieldManager = GetComponent<FieldManager>();
    }

    void Start()
    {
        // FieldManager는 Awake에서 초기화되므로, Start에서 참조를 가져오면 안전합니다.
        this.playerManager = fieldManager.playerManager;
        
        // [수정] 아래 타일맵 할당 코드를 제거합니다. 이 시점에서는 아직 FieldManager의 타일맵이 null일 수 있습니다.
        // this.groundTilemap = fieldManager.GroundTilemap;
        // this.obstacleTilemap = fieldManager.ObstacleTilemap;
        //
        // if (groundTilemap == null || obstacleTilemap == null)
        // {
        //     Debug.LogError("PlacementManager가 FieldManager로부터 Tilemap 참조를 받아오지 못했습니다!", gameObject);
        // }
    }
    
    void Update()
    {
        // [추가] 타일맵 참조가 null일 경우 FieldManager로부터 가져옵니다.
        // 이렇게 하면 FieldManager가 초기화된 이후에 안전하게 참조를 얻을 수 있습니다.
        if (groundTilemap == null && fieldManager != null)
        {
            groundTilemap = fieldManager.GroundTilemap;
        }
        if (obstacleTilemap == null && fieldManager != null)
        {
            obstacleTilemap = fieldManager.ObstacleTilemap;
        }

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
                    var command = new PlaceUnitCommand(playerManager.playerId, dataToPlace, currentMouseGridPosition);
                    GameManagers.Instance.CommandProcessor.RequestCommandExecution(command);
                    StopPlacementMode();
                }
                break;
            case PlacementMode.Wall:
                var wallCommand = new PlaceWallCommand(playerManager.playerId, currentMouseGridPosition);
                GameManagers.Instance.CommandProcessor.RequestCommandExecution(wallCommand);
                break;
        }
    }

    private bool TryRemoveWall()
    {
        if (currentMode == PlacementMode.Wall)
        {
            // 실제 벽이 있는지 여부는 PlayerManager에서 확인하므로, 여기서는 요청만 보냅니다.
            var command = new RemoveWallCommand(playerManager.playerId, currentMouseGridPosition);
            GameManagers.Instance.CommandProcessor.RequestCommandExecution(command);
            return true; // 요청을 보냈으므로 true를 반환하여 StopPlacementMode()가 호출되지 않도록 합니다.
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