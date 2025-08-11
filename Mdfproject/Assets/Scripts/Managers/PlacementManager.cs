using GameCore;
using GameCore.Enums;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Tilemaps;

public class PlacementManager : MonoBehaviour
{
    public static PlacementManager Instance { get; private set; }

    [Header("UI 설정")]
    public TMP_Text wallCountText;

    [Header("게임로직 설정")]
    public float PlacementTime = 10.0f;
    public static int CreateWallCount = 5;

    [Header("타일맵 설정")]
    public Tilemap Groundtilemap;
    public Tilemap BreakWalltilemap;
    public TileBase BreakWalltileToPlace;
    public Camera PlayerCamera;

    [Header("캐릭터 생성 설정")]
    public Sprite CharacterSprite;
    public GameObject CharacterPrefab;
    public float CharacterSortingOrder = 5f;

    [Header("프리뷰 설정")]
    public bool ShowPreview = true;
    public Color PreviewColor = Color.green;

    // 현재 마우스 위치 (중앙 관리)
    public Vector3Int CurrentMouseGridPosition { get; private set; }

    // 현재 배치 모드
    public PlacementMode CurrentPlacementMode { get; private set; } = PlacementMode.None;

    // 생성된 캐릭터 관리
    public static Dictionary<Vector3Int, GameObject> SpawnedCharacters = new Dictionary<Vector3Int, GameObject>();

    // 배치 핸들러들
    private List<IPlacementHandler> placementHandlers = new List<IPlacementHandler>();

    // 이벤트 시스템
    public System.Action<Vector3Int> OnMousePositionChanged;
    public System.Action<PlacementMode> OnPlacementModeChanged;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Start()
    {
        // 컴포넌트 자동 할당
        if (BreakWalltilemap == null)
            BreakWalltilemap = FindObjectOfType<Tilemap>();

        if (PlayerCamera == null)
            PlayerCamera = Camera.main;

        // 배치 핸들러 등록
        RegisterPlacementHandlers();
    }

    private void Update()
    {
        // 게임 상태가 준비 단계일 때만 배치 가능
        if (GameManagers.Instance != null && GameManagers.Instance.GetGameState() != GameManagers.GameState.Prepare)
        {
            return;
        }

        // 마우스 위치 업데이트 (중앙 관리)
        UpdateMousePosition();

        // 벽 개수 UI 업데이트
        if (wallCountText != null)
            wallCountText.text = CreateWallCount.ToString();

        // 현재 모드에 따른 입력 처리
        HandleCurrentModeInput();
    }

    private void RegisterPlacementHandlers()
    {
        Debug.Log("🔧 RegisterPlacementHandlers 시작");

        placementHandlers.Clear();

        var wallHandler = GetComponent<WallPlacementHandler>();
        if (wallHandler != null)
        {
            placementHandlers.Add(wallHandler);
            Debug.Log("✅ WallPlacementHandler 등록 성공");
        }

        var characterHandler = GetComponent<CharacterPlacementHandler>();
        if (characterHandler != null)
        {
            placementHandlers.Add(characterHandler);
            Debug.Log("✅ CharacterPlacementHandler 등록 성공");
        }

        Debug.Log($"🔧 총 {placementHandlers.Count}개 핸들러 등록");
    }

    private void UpdateMousePosition()
    {
        Vector3 mouseWorldPosition = GetMouseWorldPosition();
        Vector3Int newGridPosition = BreakWalltilemap.WorldToCell(mouseWorldPosition);

        if (newGridPosition != CurrentMouseGridPosition)
        {
            CurrentMouseGridPosition = newGridPosition;
            OnMousePositionChanged?.Invoke(CurrentMouseGridPosition);
        }
    }

    private void HandleCurrentModeInput()
    {
        if (CurrentPlacementMode == PlacementMode.None) return;

        foreach (var handler in placementHandlers)
        {
            if (handler.CanHandle(CurrentPlacementMode))
            {
                handler.HandleInput();
                break;
            }
        }
    }

    public Vector3 GetMouseWorldPosition()
    {
        Vector3 mouseScreenPosition = Input.mousePosition;

        if (PlayerCamera.orthographic)
        {
            mouseScreenPosition.z = PlayerCamera.nearClipPlane;
            return PlayerCamera.ScreenToWorldPoint(mouseScreenPosition);
        }
        else
        {
            Ray ray = PlayerCamera.ScreenPointToRay(mouseScreenPosition);
            float distance = -PlayerCamera.transform.position.z / ray.direction.z;
            return ray.origin + ray.direction * distance;
        }
    }

    public bool IsGroundLayer(Vector3Int? gridPosition = null)
    {
        Vector3Int checkPosition = gridPosition ?? CurrentMouseGridPosition;

        bool isGroundTilemap = Groundtilemap.gameObject.layer == LayerMask.NameToLayer("Ground");
        TileBase tile = Groundtilemap.GetTile(checkPosition);
        bool hasTile = tile != null;

        return isGroundTilemap && hasTile;
    }

    public void SetPlacementMode(PlacementMode mode)
    {
        if (CurrentPlacementMode != mode)
        {
            Debug.Log($"🔄 모드 변경: {CurrentPlacementMode} → {mode}");
            CurrentPlacementMode = mode;
            OnPlacementModeChanged?.Invoke(mode);
        }
    }

    public void ToggleWallPlacement()
    {
        Debug.Log("🧱 ToggleWallPlacement 호출");
        SetPlacementMode(CurrentPlacementMode == PlacementMode.Wall ? PlacementMode.None : PlacementMode.Wall);
    }

    public void ToggleCharacterPlacement()
    {
        Debug.Log("👤 ToggleCharacterPlacement 호출");
        SetPlacementMode(CurrentPlacementMode == PlacementMode.Character ? PlacementMode.None : PlacementMode.Character);
    }

    public static void ClearAllCharacters()
    {
        foreach (var character in SpawnedCharacters.Values)
        {
            if (character != null)
                DestroyImmediate(character);
        }
        SpawnedCharacters.Clear();
        Debug.Log("모든 캐릭터가 제거되었습니다!");
    }
}