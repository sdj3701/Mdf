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
    private Material previewMaterial;
    private MeshRenderer previewMeshRenderer;  // 3D 모드용
    private Vector3Int currentMouseGridPosition;

    private PlayerManager playerManager;
    private FieldManager fieldManager;
    private static readonly int BaseColorPropertyId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorPropertyId = Shader.PropertyToID("_Color");
    
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

    private void OnDestroy()
    {
        if (previewMaterial == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(previewMaterial);
        }
        else
        {
            DestroyImmediate(previewMaterial);
        }
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

        if (currentMode == PlacementMode.None)
        {
            if (previewObject != null && previewObject.activeSelf)
            {
                previewObject.SetActive(false);
            }

            return;
        }
        
        // [3D] fieldManager가 초기화되었는지 확인
        if (fieldManager == null || fieldManager.ground3D == null)
        {
            return; // 아직 초기화 안 됨
        }
        
        UpdateMousePosition();
        HandleMouseInput();
        if (showPreview)
        {
            if (previewObject == null)
            {
                SetupPreviewObject();
            }

            if (!previewObject.activeSelf)
            {
                previewObject.SetActive(true);
            }

            UpdatePreviewDisplay();
        }
        else if (previewObject != null && previewObject.activeSelf)
        {
            previewObject.SetActive(false);
        }
    }
    
    #region Public Methods
    
    public PlacementMode GetCurrentMode() => currentMode;

    public void StartPlacementMode(PlacementMode mode, GameObject unitPrefab = null)
    {
        if (!IsPlacementReady())
        {
            if (mode == PlacementMode.Wall)
            {
                LogManualWall("mode", $"blocked reason=placement_not_ready {DescribePlacementReadiness()}");
            }

            return;
        }

        if (GameManagers.Instance != null &&
            (GameManagers.Instance.GetGameState() != GameManagers.GameState.Prepare || GameManagers.Instance.IsSequenceTransitioning))
        {
            if (mode == PlacementMode.Wall)
            {
                LogManualWall("mode", $"blocked reason=phase state={GameManagers.Instance.GetGameState()} transitioning={GameManagers.Instance.IsSequenceTransitioning}");
            }

            return;
        }

        currentMode = mode;
        unitPrefabToPlace = unitPrefab;
        if (showPreview)
        {
            SetupPreviewObject();
        }
        else if (previewObject != null && previewObject.activeSelf)
        {
            previewObject.SetActive(false);
        }

        if (mode == PlacementMode.Wall)
        {
            LogManualWall("mode", $"started mode={mode} {DescribePlacementReadiness()} {DescribeCameraState()}");
        }
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
        bool pointerOverUI = MdfInput.IsPointerOverFieldBlockingUI();
        bool primaryPressed = MdfInput.PrimaryPointerWasPressedThisFrame();
        bool secondaryPressed = MdfInput.SecondaryPointerWasPressedThisFrame();
        if (primaryPressed && pointerOverUI && currentMode == PlacementMode.Wall)
        {
            LogManualWall("ui-block", MdfInput.DescribeFieldBlockingUiHits());
        }

        if (primaryPressed && !pointerOverUI)
        {
            if (currentMode == PlacementMode.Wall && TryRemoveWall())
            {
                return;
            }

            TryPlace();
        }

        if (secondaryPressed)
        {
            if (!pointerOverUI && currentMode == PlacementMode.Wall && TryRemoveWall())
            {
                return;
            }

            StopPlacementMode();
        }
    }

    private void TryPlace()
    {
        if (!IsPlacementReady())
        {
            LogManualWall("click", $"blocked reason=placement_not_ready {DescribePlacementReadiness()}");
            return;
        }

        if (playerManager.playerId < 0)
        {
            LogManualWall("click", "blocked reason=player_id_unassigned");
            return;
        }

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

        if (!IsPositionValidForPlacement(currentMouseGridPosition, dataToPlace))
        {
            if (currentMode == PlacementMode.Wall)
            {
                LogManualWall("validity", BuildWallPlacementInvalidReason(currentMouseGridPosition));
            }

            return;
        }

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
                LogManualWall("command", $"queue player={playerManager.playerId} pos={currentMouseGridPosition} {DescribeCameraState()}");
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
            if (fieldManager == null || fieldManager.GetWallAt(currentMouseGridPosition) == null)
            {
                return false;
            }

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
        Ray ray = playerCamera.ScreenPointToRay(MdfInput.PointerPosition);
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
            previewMaterial = CreatePreviewMaterial();
            previewMeshRenderer.sharedMaterial = previewMaterial;
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

    private string BuildWallPlacementInvalidReason(Vector3Int gridPosition)
    {
        if (fieldManager == null)
        {
            return "blocked reason=field_manager_null";
        }

        bool validGrid = fieldManager.IsValidGridPosition(gridPosition);
        bool hasWall = validGrid && fieldManager.HasWallAt(gridPosition);
        Unit occupant = validGrid ? fieldManager.GetUnitAt(gridPosition) : null;
        string occupantType = occupant != null && occupant.Data != null ? occupant.Data.unitType.ToString() : "none";
        Vector3Int goalCell = playerManager != null
            ? fieldManager.WorldToGridInt(playerManager.goalTransform != null ? playerManager.goalTransform.position : Vector3.zero)
            : new Vector3Int(int.MinValue, int.MinValue, 0);
        bool isGoal = validGrid && gridPosition == goalCell;
        bool meleeBlocked = occupant != null &&
                            occupant.Data != null &&
                            occupant.Data.unitType == UnitType.Melee &&
                            !fieldManager.FindFirstEmptySlot(occupant.Data).HasValue;
        int wallStock = playerManager != null ? playerManager.GetWallCount() : -1;

        return $"blocked pos={gridPosition} validGrid={validGrid} hasWall={hasWall} isGoal={isGoal} occupant={occupantType} meleeBlocked={meleeBlocked} wallStock={wallStock} {DescribeCameraState()}";
    }

    private void LogManualWall(string stage, string message)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (currentMode == PlacementMode.Wall || stage == "mode")
        {
            Debug.Log($"[ManualWall] {stage} {message}");
        }
#endif
    }

    private string DescribePlacementReadiness()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        bool hasPlayer = playerManager != null;
        bool hasField = fieldManager != null;
        bool owned = IsOwnedByLocalPlayer();
        bool ready = hasPlayer && playerManager.IsReadyForPlayerActions;
        bool hasCommandProcessor = GameManagers.Instance != null && GameManagers.Instance.CommandProcessor != null;
        int playerId = hasPlayer ? SafeGetPlayerId(playerManager) : -1;
        bool hasInputAuthority = hasPlayer &&
                                 playerManager.Object != null &&
                                 playerManager.Object.IsValid &&
                                 playerManager.Object.HasInputAuthority;
        return $"player={playerId} hasField={hasField} owned={owned} inputAuthority={hasInputAuthority} ready={ready} commandProcessor={hasCommandProcessor}";
#else
        return string.Empty;
#endif
    }

    private string DescribeCameraState()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        var cameraManager = CameraManager.Instance;
        if (cameraManager == null)
        {
            return "camera=null";
        }

        int viewingPlayerId = -1;
        var viewing = cameraManager.CurrentViewingField;
        if (viewing != null)
        {
            viewingPlayerId = SafeGetPlayerId(viewing);
        }

        return $"cameraOwn={cameraManager.IsViewingOwnField} viewingPlayer={viewingPlayerId}";
#else
        return string.Empty;
#endif
    }

    private static int SafeGetPlayerId(PlayerManager player)
    {
        if (player == null)
        {
            return -1;
        }

        try
        {
            return player.playerId;
        }
        catch (System.InvalidOperationException)
        {
            return -1;
        }
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
            ApplyPreviewColor(previewColor);
        }
    }

    private static Material CreatePreviewMaterial()
    {
        Shader shader = FindPlacementPreviewShader();
        Material material = new Material(shader)
        {
            name = "PlacementPreviewMaterial",
            hideFlags = HideFlags.DontSave
        };
        ConfigureTransparentPreviewMaterial(material);
        return material;
    }

    private static Shader FindPlacementPreviewShader()
    {
        return Shader.Find("Universal Render Pipeline/Unlit")
               ?? Shader.Find("Universal Render Pipeline/Lit")
               ?? Shader.Find("Sprites/Default")
               ?? Shader.Find("Legacy Shaders/Transparent/Diffuse")
               ?? Shader.Find("Hidden/Internal-Colored")
               ?? Shader.Find("Standard");
    }

    private static void ConfigureTransparentPreviewMaterial(Material material)
    {
        if (material == null)
        {
            return;
        }

        material.SetOverrideTag("RenderType", "Transparent");

        if (material.HasProperty("_Mode"))
        {
            material.SetFloat("_Mode", 3f);
        }

        if (material.HasProperty("_Surface"))
        {
            material.SetFloat("_Surface", 1f);
        }

        if (material.HasProperty("_Blend"))
        {
            material.SetFloat("_Blend", 0f);
        }

        if (material.HasProperty("_SrcBlend"))
        {
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        }

        if (material.HasProperty("_DstBlend"))
        {
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        }

        if (material.HasProperty("_ZWrite"))
        {
            material.SetInt("_ZWrite", 0);
        }

        material.DisableKeyword("_ALPHATEST_ON");
        material.EnableKeyword("_ALPHABLEND_ON");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.SetShaderPassEnabled("ShadowCaster", false);
        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
    }

    private void ApplyPreviewColor(Color previewColor)
    {
        if (previewMaterial == null)
        {
            previewMaterial = previewMeshRenderer != null ? previewMeshRenderer.sharedMaterial : null;
        }

        if (previewMaterial == null)
        {
            return;
        }

        if (previewMaterial.HasProperty(BaseColorPropertyId))
        {
            previewMaterial.SetColor(BaseColorPropertyId, previewColor);
        }

        if (previewMaterial.HasProperty(ColorPropertyId))
        {
            previewMaterial.SetColor(ColorPropertyId, previewColor);
        }
    }
    
    #endregion
}
