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
    private PlayerManager playerManager;
    private FieldManager fieldManager; // FieldManager를 직접 참조하여 유닛 위치 정보를 얻습니다.

    private Tilemap groundTilemap => GameAssets.TileMaps.GroundTilemap;
    private Tilemap obstacleTilemap => GameAssets.TileMaps.BreakWallTilemap;
    private Camera playerCamera => GameAssets.Cameras.MainCamera;
    private TileBase wallTileToPlace => GameAssets.Tiles.BreakWall;

    private readonly Plane gamePlane = new Plane(Vector3.forward, 0);

    // [수정됨] Awake에서 필요한 컴포넌트들을 참조합니다.
    void Awake()
    {
        fieldManager = GetComponent<FieldManager>();
        if (fieldManager == null)
        {
            Debug.LogError("PlacementManager가 FieldManager를 찾을 수 없습니다!", gameObject);
            this.enabled = false;
        }
    }
    
    // [수정됨] Initialize 메서드는 이제 PlayerManager만 받습니다.
    public void Initialize(PlayerManager pm)
    {
        playerManager = pm;
        if (playerManager == null)
        {
            Debug.LogError("PlacementManager 초기화 실패: PlayerManager 참조가 null입니다!", gameObject);
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
    
    /// <summary>
    /// 지정된 그리드 위치에 유닛이나 벽을 배치할 수 있는지 확인합니다.
    /// </summary>
    public bool IsPositionValidForPlacement(Vector3Int gridPosition)
    {
        if (groundTilemap == null || obstacleTilemap == null || fieldManager == null)
        {
            Debug.LogWarning("[PlacementManager] 타일맵 또는 FieldManager 참조를 찾을 수 없습니다.");
            return false;
        }

        bool hasGroundTile = groundTilemap.GetTile(gridPosition) != null;
        bool hasObstacle = obstacleTilemap.GetTile(gridPosition) != null;
        // [수정됨] FieldManager에게 해당 위치에 유닛이 있는지 직접 물어봅니다.
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
                // 벽 제거에 실패했다면(제거할 벽이 없다면) 배치 모드를 취소합니다.
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
                    // FieldManager에게 유닛 생성을 요청합니다.
                    fieldManager.CreateAndPlaceUnitFromPlacement(unitPrefabToPlace, currentMouseGridPosition);
                    StopPlacementMode(); // 유닛은 한 번만 배치합니다.
                }
                break;
            case PlacementMode.Wall:
                if (playerManager.TryUseWall())
                {
                    obstacleTilemap.SetTile(currentMouseGridPosition, wallTileToPlace);
                    // 벽은 여러 개를 연속으로 배치할 수 있으므로 StopPlacementMode()를 호출하지 않습니다.
                }
                break;
        }
    }

    /// <summary>
    /// 현재 마우스 위치의 벽을 제거하려고 시도합니다.
    /// </summary>
    /// <returns>벽 제거에 성공했으면 true, 아니면 false를 반환합니다.</returns>
    private bool TryRemoveWall()
    {
        // 벽 배치 모드일 때만 벽을 제거할 수 있습니다.
        if (currentMode == PlacementMode.Wall && obstacleTilemap.GetTile(currentMouseGridPosition) != null)
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
            previewRenderer.sortingOrder = 10; // 다른 스프라이트보다 위에 보이도록 설정
        }

        Sprite previewSprite = null;
        if (currentMode == PlacementMode.Unit && unitPrefabToPlace != null)
        {
            // 유닛 프리팹의 자식에서 SpriteRenderer를 찾아 이미지를 가져옵니다.
            previewSprite = unitPrefabToPlace.GetComponentInChildren<SpriteRenderer>()?.sprite;
        }
        else if (currentMode == PlacementMode.Wall)
        {
            // 벽 타일(TileBase)을 실제 타일(Tile)로 변환하여 스프라이트를 가져옵니다.
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

        // 프리뷰 오브젝트를 마우스의 그리드 위치에 맞게 이동시킵니다.
        Vector3 worldPos = obstacleTilemap.CellToWorld(currentMouseGridPosition) + (obstacleTilemap.cellSize * 0.5f);
        previewObject.transform.position = worldPos;

        // 배치 가능 여부에 따라 프리뷰 색상을 변경합니다.
        previewRenderer.color = IsPositionValidForPlacement(currentMouseGridPosition) ? validPreviewColor : invalidPreviewColor;
    }

    #endregion
}