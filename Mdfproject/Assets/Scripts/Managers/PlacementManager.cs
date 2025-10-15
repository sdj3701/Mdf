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
    private SpriteRenderer previewRenderer;  // 2D 모드용
    private MeshRenderer previewMeshRenderer;  // [3D Migration] 3D 모드용
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
        // 3D 모드에서는 Tilemap이 null일 수 있으므로 한 번만 시도
        if (groundTilemap == null && obstacleTilemap == null && fieldManager != null)
        {
            groundTilemap = fieldManager.GroundTilemap;
            obstacleTilemap = fieldManager.ObstacleTilemap;
            // 3D 모드면 둘 다 null일 수 있음
        }

        if (currentMode == PlacementMode.None || !showPreview) return;
        
        // [3D Migration] fieldManager가 초기화되었는지 확인
        if (fieldManager == null || (obstacleTilemap == null && fieldManager.ground3D == null))
        {
            return; // 아직 초기화 안 됨
        }
        
        UpdateMousePosition();
        HandleMouseInput();
        if (showPreview)
            UpdatePreviewDisplay();
    }
    
    #region Public Methods
    
    public PlacementMode GetCurrentMode() => currentMode;

    public void StartPlacementMode(PlacementMode mode, GameObject unitPrefab = null)
    {
        if (GameManagers.Instance != null && GameManagers.Instance.GetGameState() != GameManagers.GameState.Prepare) return;
        currentMode = mode;
        unitPrefabToPlace = unitPrefab;
        SetupPreviewObject();
    }

    public void StopPlacementMode()
    {
        currentMode = PlacementMode.None;
        unitPrefabToPlace = null;
        if (previewObject != null)
        {
            previewObject.SetActive(false);
        }
    }
    
    public bool IsPositionValidForPlacement(Vector3Int gridPosition, UnitData unitData = null)
    {
        // [3D Migration] Tilemap 또는 3D 그리드 사용
        if (groundTilemap != null)
        {
            // 2D Tilemap 모드
            bool hasGroundTile = groundTilemap.GetTile(gridPosition) != null;
            bool hasObstacle = fieldManager.HasWallAt(gridPosition);
            bool hasUnit = fieldManager.IsUnitAt(gridPosition);

            if (unitData != null)
            {
                if (hasUnit || !hasGroundTile) return false;
                if (unitData.unitType == UnitType.Melee && hasObstacle)
                {
                    return false;
                }
                return true;
            }
            else
            {
                if (!hasGroundTile) return false;
                if (hasObstacle) return false;
                // 스폰/골 그리드에는 벽 금지
                if (playerManager != null)
                {
                    Vector3Int spawnCell = playerManager.spawnPoint != null ? groundTilemap.WorldToCell(playerManager.spawnPoint.position) : Vector3Int.zero;
                    Vector3Int goalCell = playerManager.goalTransform != null ? groundTilemap.WorldToCell(playerManager.goalTransform.position) : Vector3Int.zero;
                    if (gridPosition == spawnCell || gridPosition == goalCell) return false;
                }
                Unit occupant = fieldManager.GetUnitAt(gridPosition);
                if (occupant == null) return true;
                if (occupant.Data.unitType == UnitType.Ranged) return true;
                var alt = fieldManager.FindFirstEmptySlot(occupant.Data);
                return alt.HasValue;
            }
        }
        else
        {
            // 3D 모드
            if (!fieldManager.IsValidGridPosition(gridPosition)) return false;
            bool hasObstacle = fieldManager.HasWallAt(gridPosition);
            bool hasUnit = fieldManager.IsUnitAt(gridPosition);

            if (unitData != null)
            {
                if (hasUnit) return false;
                if (unitData.unitType == UnitType.Melee && hasObstacle)
                {
                    return false;
                }
                return true;
            }
            else
            {
                if (hasObstacle) return false;
                // 스폰/골 그리드에는 벽 금지
                if (playerManager != null)
                {
                    var spawnCell = fieldManager.WorldToGridInt(playerManager.spawnPoint != null ? playerManager.spawnPoint.position : Vector3.zero);
                    var goalCell = fieldManager.WorldToGridInt(playerManager.goalTransform != null ? playerManager.goalTransform.position : Vector3.zero);
                    if (gridPosition == spawnCell || gridPosition == goalCell) return false;
                }
                Unit occupant = fieldManager.GetUnitAt(gridPosition);
                if (occupant == null) return true;
                if (occupant.Data.unitType == UnitType.Ranged) return true;
                var alt = fieldManager.FindFirstEmptySlot(occupant.Data);
                return alt.HasValue;
            }
        }
    }
    
    #endregion
    
    #region Input & Placement Logic

    private void HandleMouseInput()
    {
        if (Input.GetMouseButtonDown(0)) TryPlace();
        if (Input.GetMouseButtonDown(1)) StopPlacementMode();
    }

    private void TryPlace()
    {
        UnitData dataToPlace = (currentMode == PlacementMode.Unit && unitPrefabToPlace != null)
            ? unitPrefabToPlace.GetComponent<Unit>().Data
            : null;

        // 안전 클램프 (특히 경계 클릭 시)
        if (fieldManager != null && fieldManager.ground3D != null)
        {
            currentMouseGridPosition = new Vector3Int(
                Mathf.Clamp(currentMouseGridPosition.x, 0, fieldManager.gridSize.x - 1),
                Mathf.Clamp(currentMouseGridPosition.y, 0, fieldManager.gridSize.y - 1),
                0
            );
        }

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
        // [3D Migration] Tilemap 또는 3D 그리드 사용
        if (obstacleTilemap != null)
        {
            currentMouseGridPosition = obstacleTilemap.WorldToCell(mouseWorldPos);
        }
        else
        {
            // 경계 밖 점을 먼저 그리드 경계로 클램프 후 셀로 변환 (경계에서의 -1/size 인덱스 방지)
            Vector3 clamped = fieldManager.ClampToGrid(mouseWorldPos);
            currentMouseGridPosition = fieldManager.WorldToGridInt(clamped);
        }
        // 그리드 범위를 벗어나지 않도록 클램프하여 미묘한 -1/size 인덱스 방지
        if (fieldManager != null && fieldManager.ground3D != null)
        {
            currentMouseGridPosition = new Vector3Int(
                Mathf.Clamp(currentMouseGridPosition.x, 0, fieldManager.gridSize.x - 1),
                Mathf.Clamp(currentMouseGridPosition.y, 0, fieldManager.gridSize.y - 1),
                0
            );
        }
    }

    private Vector3 GetMouseWorldPosition()
    {
        // [3D Migration] Tilemap 또는 3D Raycast 사용
        if (obstacleTilemap != null)
        {
            // 2D Tilemap 모드
            Ray cameraRay = playerCamera.ScreenPointToRay(Input.mousePosition);
            if (gamePlane.Raycast(cameraRay, out float enter))
            {
                return cameraRay.GetPoint(enter);
            }
            return Vector3.zero;
        }
        else
        {
            // 3D 모드: Ground에 Raycast
            Ray ray = playerCamera.ScreenPointToRay(Input.mousePosition);
            Plane groundPlane = new Plane(Vector3.up, fieldManager.gridOrigin);
            
            if (groundPlane.Raycast(ray, out float enter))
            {
                return ray.GetPoint(enter);
            }
            
            return Vector3.zero;
        }
    }

    private void SetupPreviewObject()
    {
        // [3D Migration] Tilemap 또는 3D 모드에 따라 다른 프리뷰 생성
        bool is3DMode = (obstacleTilemap == null);
        
        if (previewObject == null)
        {
            previewObject = new GameObject("PlacementPreview");
            
            if (is3DMode)
            {
                // 3D 모드: 큰 큐브 프리뷰
                GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.transform.SetParent(previewObject.transform);
                cube.transform.localPosition = Vector3.zero;
                cube.transform.localScale = new Vector3(fieldManager.cellSize * 0.9f, 0.2f, fieldManager.cellSize * 0.9f);
                
                // Collider 제거 (프리뷰는 충돌 불필요)
                Destroy(cube.GetComponent<Collider>());
                
                previewMeshRenderer = cube.GetComponent<MeshRenderer>();
                // 투명 Material 생성
                Material previewMaterial = new Material(Shader.Find("Standard"));
                previewMaterial.SetFloat("_Mode", 3); // Transparent
                previewMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                previewMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                previewMaterial.SetInt("_ZWrite", 0);
                previewMaterial.DisableKeyword("_ALPHATEST_ON");
                previewMaterial.EnableKeyword("_ALPHABLEND_ON");
                previewMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                previewMaterial.renderQueue = 3000;
                previewMeshRenderer.material = previewMaterial;
            }
            else
            {
                // 2D 모드: SpriteRenderer
                previewRenderer = previewObject.AddComponent<SpriteRenderer>();
                previewRenderer.sortingOrder = 10;
            }
        }

        if (is3DMode)
        {
            // 3D 모드: 큐브 크기 조정
            Transform cube = previewObject.transform.GetChild(0);
            if (currentMode == PlacementMode.Unit && unitPrefabToPlace != null)
            {
                cube.localScale = new Vector3(fieldManager.cellSize * 0.9f, 0.5f, fieldManager.cellSize * 0.9f);
            }
            else if (currentMode == PlacementMode.Wall)
            {
                cube.localScale = new Vector3(fieldManager.cellSize * 0.9f, fieldManager.cellSize * 0.9f, fieldManager.cellSize * 0.9f);
            }
            previewObject.SetActive(true);
        }
        else
        {
            // 2D 모드: Sprite 할당
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
    }

    private void UpdatePreviewDisplay()
    {
        if (previewObject == null || !previewObject.activeSelf) return;

        // [3D Migration] Tilemap 또는 3D 그리드 사용 (벽 체크 포함)
        Vector3 worldPos;
        if (obstacleTilemap != null)
        {
            worldPos = obstacleTilemap.CellToWorld(currentMouseGridPosition) + (obstacleTilemap.cellSize * 0.5f);
        }
        else
        {
            // 3D 모드: 유닛 배치 시에만 벽 높이 체크
            bool checkWall = (currentMode == PlacementMode.Unit);
            worldPos = fieldManager.GridToWorld(currentMouseGridPosition, checkForWall: checkWall);
        }
        
        previewObject.transform.position = worldPos;
        
        UnitData dataToPlace = (currentMode == PlacementMode.Unit && unitPrefabToPlace != null)
            ? unitPrefabToPlace.GetComponent<Unit>().Data
            : null;

        bool isValid = IsPositionValidForPlacement(currentMouseGridPosition, dataToPlace);
        Color previewColor = isValid ? validPreviewColor : invalidPreviewColor;
        
        // [3D Migration] 3D 또는 2D 모드에 따라 색상 적용
        if (previewMeshRenderer != null)
        {
            previewMeshRenderer.material.color = previewColor;
        }
        else if (previewRenderer != null)
        {
            previewRenderer.color = previewColor;
        }
    }
    
    #endregion
}