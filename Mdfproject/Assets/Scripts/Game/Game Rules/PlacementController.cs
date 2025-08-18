// Assets/Scripts/Managers/PlacementController.cs
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
public class PlacementController : MonoBehaviour
{
    [Header("타일맵 & 카메라 참조")]
    [SerializeField] private Tilemap groundTilemap;
    [SerializeField] private Camera playerCamera;

    [Header("배치 데이터")]
    [SerializeField] private TileBase wallTileToPlace;

    [Header("프리뷰 설정")]
    [SerializeField] private bool showPreview = true;
    [SerializeField] private Color validPreviewColor = new Color(0f, 1f, 0f, 0.5f);
    [SerializeField] private Color invalidPreviewColor = new Color(1f, 0f, 0f, 0.5f);
    
    // --- 내부 상태 및 참조 변수 ---
    private PlacementMode currentMode = PlacementMode.None;
    private GameObject unitPrefabToPlace;
    private GameObject previewObject;
    private SpriteRenderer previewRenderer;
    private Vector3Int currentMouseGridPosition;

    private PlayerManager playerManager;
    private Dictionary<Vector3Int, GameObject> placedUnits;
    private Tilemap obstacleTilemap;

    private readonly Plane gamePlane = new Plane(Vector3.forward, 0);

    /// <summary>
    /// FieldManager로부터 필수 참조들을 주입받습니다.
    /// </summary>
    public void Initialize(PlayerManager pm, Dictionary<Vector3Int, GameObject> units, Tilemap obsTilemap)
    {
        playerManager = pm;
        placedUnits = units;
        obstacleTilemap = obsTilemap;

        // 초기화 실패는 심각한 문제이므로 Error 로그를 유지합니다.
        if (playerManager == null || placedUnits == null || obstacleTilemap == null || groundTilemap == null || playerCamera == null)
        {
            Debug.LogError("PlacementController 초기화 실패: 하나 이상의 필수 참조가 null입니다! 인스펙터를 확인하세요.", gameObject);
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
            // 준비 단계가 아닐 때 시도하는 것은 비정상적인 상황일 수 있으므로 Warning 로그를 유지합니다.
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
            // FIX: LogPlacementFailureReason 함수를 호출하는 대신, 아무것도 하지 않거나
            // 간단한 사운드 효과(삑! 하는 소리 등)를 재생하는 로직으로 대체할 수 있습니다.
            // 여기서는 일단 아무것도 하지 않도록 비워둡니다.
            // LogPlacementFailureReason(currentMouseGridPosition); // 이 줄을 주석 처리
            return;
        }

        switch (currentMode)
        {
            case PlacementMode.Unit:
                if (unitPrefabToPlace != null)
                {
                    Vector3 worldPos = obstacleTilemap.CellToWorld(currentMouseGridPosition) + (obstacleTilemap.cellSize * 0.5f);
                    GameObject newUnit = Instantiate(unitPrefabToPlace, worldPos, Quaternion.identity);
                    placedUnits.Add(currentMouseGridPosition, newUnit);
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
        if (placedUnits.ContainsKey(currentMouseGridPosition))
        {
            return false;
        }

        if (obstacleTilemap.GetTile(currentMouseGridPosition) != null)
        {
            obstacleTilemap.SetTile(currentMouseGridPosition, null);
            playerManager.ReturnWall();
            return true;
        }
        
        return false;
    }
    
    #endregion

    #region Coordinate & Validation

    private void UpdateMousePosition()
    {
        Vector3 mouseWorldPos = GetMouseWorldPosition();
        currentMouseGridPosition = obstacleTilemap.WorldToCell(mouseWorldPos);
    }
    
    private Vector3 GetMouseWorldPosition()
    {
        if (playerCamera.orthographic)
        {
            return playerCamera.ScreenToWorldPoint(Input.mousePosition);
        }
        else
        {
            Ray cameraRay = playerCamera.ScreenPointToRay(Input.mousePosition);
            if (gamePlane.Raycast(cameraRay, out float enter))
            {
                return cameraRay.GetPoint(enter);
            }
        }
        return Vector3.zero;
    }
    
    public bool IsPositionValidForPlacement(Vector3Int gridPosition)
    {
        if (groundTilemap.GetTile(gridPosition) == null) return false;
        if (obstacleTilemap.GetTile(gridPosition) != null) return false;
        if (placedUnits.ContainsKey(gridPosition)) return false;
        return true;
    }


    #endregion

    #region Preview Logic

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
            Texture2D tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            previewSprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), Vector2.one * 0.5f);
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