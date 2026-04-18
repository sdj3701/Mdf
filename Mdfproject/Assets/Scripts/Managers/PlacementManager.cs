// Assets/Scripts/Managers/PlacementManager.cs
using UnityEngine;
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
    private MeshRenderer previewMeshRenderer;  // 3D 모드용
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

    void Awake()
    {
        fieldManager = GetComponent<FieldManager>();
    }

    void Start()
    {
        // FieldManager는 Awake에서 초기화되므로, Start에서 참조를 가져오면 안전합니다.
        this.playerManager = fieldManager.playerManager;
    }

    private bool IsOwnedByLocalPlayer()
    {
        if (playerManager == null && fieldManager != null)
        {
            playerManager = fieldManager.playerManager;
        }

        if (playerManager == null)
        {
            return false;
        }

        if (playerManager.Object != null && playerManager.Object.IsValid)
        {
            return playerManager.Object.HasInputAuthority;
        }

        return GameManagers.Instance != null && GameManagers.Instance.localPlayer == playerManager;
    }

    private bool IsPlacementReady()
    {
        return IsOwnedByLocalPlayer() &&
               playerManager != null &&
               playerManager.IsReadyForPlayerActions &&
               GameManagers.Instance != null &&
               GameManagers.Instance.CommandProcessor != null;
    }
    
    void Update()
    {
        if (!IsPlacementReady())
        {
            if (previewObject != null && previewObject.activeSelf)
            {
                previewObject.SetActive(false);
            }
            return;
        }

        if (currentMode == PlacementMode.None || !showPreview) return;
        
        // [3D] fieldManager가 초기화되었는지 확인
        if (fieldManager == null || fieldManager.ground3D == null)
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
        if (!IsPlacementReady()) return;
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
                var goalCell = fieldManager.WorldToGridInt(playerManager.goalTransform != null ? playerManager.goalTransform.position : Vector3.zero);
                if (gridPosition == goalCell) return false;
            }
            Unit occupant = fieldManager.GetUnitAt(gridPosition);
            if (occupant == null) return true;
            if (occupant.Data.unitType == UnitType.Ranged) return true;
            var alt = fieldManager.FindFirstEmptySlot(occupant.Data);
            return alt.HasValue;
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
        if (!IsPlacementReady()) return;
        if (playerManager.playerId < 0) return;

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
        if (!IsPlacementReady()) return false;
        if (playerManager.playerId < 0) return false;

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
        // 경계 밖 점을 먼저 그리드 경계로 클램프 후 셀로 변환 (경계에서의 -1/size 인덱스 방지)
        Vector3 clamped = fieldManager.ClampToGrid(mouseWorldPos);
        currentMouseGridPosition = fieldManager.WorldToGridInt(clamped);
        // 그리드 범위를 벗어나지 않도록 클램프하여 미묘한 -1/size 인덱스 방지
        currentMouseGridPosition = new Vector3Int(
            Mathf.Clamp(currentMouseGridPosition.x, 0, fieldManager.gridSize.x - 1),
            Mathf.Clamp(currentMouseGridPosition.y, 0, fieldManager.gridSize.y - 1),
            0
        );
    }

    private Vector3 GetMouseWorldPosition()
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

    private void SetupPreviewObject()
    {
        if (previewObject == null)
        {
            previewObject = new GameObject("PlacementPreview");
            
            // 3D 모드: 큰 큐브 프리뷰
            GameObject previewCube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            previewCube.transform.SetParent(previewObject.transform);
            previewCube.transform.localPosition = Vector3.zero;
            previewCube.transform.localScale = new Vector3(fieldManager.cellSize * 0.9f, 0.2f, fieldManager.cellSize * 0.9f);
            
            // Collider 제거 (프리뷰는 충돌 불필요)
            Destroy(previewCube.GetComponent<Collider>());
            
            previewMeshRenderer = previewCube.GetComponent<MeshRenderer>();
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

    private void UpdatePreviewDisplay()
    {
        if (previewObject == null || !previewObject.activeSelf) return;

        // 3D 모드: 유닛 배치 시에만 벽 높이 체크
        bool checkWall = (currentMode == PlacementMode.Unit);
        Vector3 worldPos = fieldManager.GridToWorld(currentMouseGridPosition, checkForWall: checkWall);
        
        previewObject.transform.position = worldPos;
        
        UnitData dataToPlace = (currentMode == PlacementMode.Unit && unitPrefabToPlace != null)
            ? unitPrefabToPlace.GetComponent<Unit>().Data
            : null;

        bool isValid = IsPositionValidForPlacement(currentMouseGridPosition, dataToPlace);
        Color previewColor = isValid ? validPreviewColor : invalidPreviewColor;
        
        // 3D 프리뷰 색상 적용
        if (previewMeshRenderer != null)
        {
            previewMeshRenderer.material.color = previewColor;
        }
    }
    
    #endregion
}
