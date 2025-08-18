using GameCore;
using GameCore.Enums;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public partial class PlacementManager : RegisteredComponent
{
    public static PlacementManager Instance { get; private set; }

    [Header("게임로직 설정")]
    public float PlacementTime = 10.0f;
    public static int CreateWallCount = 5;
    public float CharacterSortingOrder = 10f;

    [Header("프리뷰 설정")]
    public bool ShowPreview = true;
    public Color PreviewColor = Color.green;

    // 현재 마우스 위치
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



    [Header("타일맵 타입")]
    [SerializeField] private TilemapType tilemapType;

    protected override void Awake()
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

        componentId = "PlacementManager";
        base.Awake();
    }

    protected override void RegisterSelf()
    {
        ComponentRegistry.Register("PlacementManager", this);
    }

    protected override void UnregisterSelf()
    {
        ComponentRegistry.Unregister<PlacementManager>("PlacementManager");
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (!string.IsNullOrEmpty(componentId))
        {
            componentId = tilemapType.ToString() + "Tilemap";
        }
    }
#endif

    private void Start()
    {
        // 간단한 초기화
        RegisterPlacementHandlers();
    }

    private void Update()
    {
        // 게임 상태 확인
        if (GameManagers.Instance != null &&
            GameManagers.Instance.GetGameState() != GameManagers.GameState.Prepare)
        {
            return;
        }

        UpdateMousePosition();
        UpdateWallCountUI();
        HandleCurrentModeInput();
    }

    private void RegisterPlacementHandlers()
    {

        placementHandlers.Clear();

        var wallHandler = GetComponent<WallPlacementHandler>();
        if (wallHandler != null)
        {
            placementHandlers.Add(wallHandler);
        }

        var characterHandler = GetComponent<CharacterPlacementHandler>();
        if (characterHandler != null)
        {
            placementHandlers.Add(characterHandler);
        }

        Debug.Log($"🔧 총 {placementHandlers.Count}개 핸들러 등록");
    }

    private void UpdateMousePosition()
    {
        var playerCamera = GameAssets.Cameras.MainCamera;
        if (GameAssets.TileMaps.BreakWallTilemap == null || playerCamera == null) return;

        Vector3 mouseWorldPosition = GetMouseWorldPosition();
        Vector3Int newGridPosition = GameAssets.TileMaps.BreakWallTilemap.WorldToCell(mouseWorldPosition);

        if (newGridPosition != CurrentMouseGridPosition)
        {
            CurrentMouseGridPosition = newGridPosition;
            OnMousePositionChanged?.Invoke(CurrentMouseGridPosition);
        }
    }

    private void UpdateWallCountUI()
    {
        // GameAssets를 통해 접근
        var wallCountText = GameAssets.UI.CurrentBreakWall;
        if (wallCountText != null)
            wallCountText.text = CreateWallCount.ToString();
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

    // ===== Public 메서드들 =====

    public Vector3 GetMouseWorldPosition()
    {
        var camera = GameAssets.Cameras.MainCamera;
        if (camera == null) return Vector3.zero;

        Vector3 mouseScreenPosition = Input.mousePosition;

        if (camera.orthographic)
        {
            mouseScreenPosition.z = camera.nearClipPlane;
            return camera.ScreenToWorldPoint(mouseScreenPosition);
        }
        else
        {
            Ray ray = camera.ScreenPointToRay(mouseScreenPosition);
            float distance = -camera.transform.position.z / ray.direction.z;
            return ray.origin + ray.direction * distance;
        }
    }

    public bool IsGroundLayer(Vector3Int? gridPosition = null)
    {
        if (GameAssets.TileMaps.GroundTilemap == null) return false;

        Vector3Int checkPosition = gridPosition ?? CurrentMouseGridPosition;

        bool isGroundTilemap = GameAssets.TileMaps.GroundTilemap.gameObject.layer == LayerMask.NameToLayer("Ground");
        var tile = GameAssets.TileMaps.GroundTilemap.GetTile(checkPosition);
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

    // ===== AssetRegistry 접근 메서드들 =====

    /// <summary>
    /// 캐릭터 스프라이트를 AssetRegistry에서 가져옵니다.
    /// </summary>
    public Sprite GetCharacterSprite()
    {
        return AssetRegistry.GetSprite("DefaultCharacter");
    }

    /// <summary>
    /// 캐릭터 프리팹을 AssetRegistry에서 가져옵니다.
    /// </summary>
    public GameObject GetCharacterPrefab()
    {
        return AssetRegistry.GetPrefab("CharacterPrefab");
    }

    // ===== 디버그 메서드들 =====

    [ContextMenu("현재 상태 출력")]
    public void PrintCurrentState()
    {
        Debug.Log("=== PlacementManager 현재 상태 ===");
        Debug.Log($"Ground Tilemap: {GameAssets.TileMaps.GroundTilemap?.name ?? "null"}");
        Debug.Log($"BreakWall Tilemap: {GameAssets.TileMaps.BreakWallTilemap?.name ?? "null"}");
        Debug.Log($"Main Camera: {GameAssets.Cameras.MainCamera?.name ?? "null"}");
        //Debug.Log($"Wall Count Text: {GameAssets.UI.WallCountText?.name ?? "null"}");
        Debug.Log($"현재 배치 모드: {CurrentPlacementMode}");
        Debug.Log($"등록된 핸들러 수: {placementHandlers.Count}");


        // AssetRegistry 상태도 출력
        Debug.Log("=== AssetRegistry 상태 ===");
        AssetRegistry.PrintAssetStats();
    }

    [ContextMenu("AssetRegistry 에셋 확인")]
    public void CheckAssetRegistryAssets()
    {
        Debug.Log("=== AssetRegistry 에셋 확인 ===");

        var breakWall = AssetRegistry.GetTile("BreakWall");
        Debug.Log($"BreakWall 타일: {(breakWall != null ? "✅ 로드됨" : "❌ 없음")}");

        var characterSprite = AssetRegistry.GetSprite("DefaultCharacter");
        Debug.Log($"캐릭터 스프라이트: {(characterSprite != null ? "✅ 로드됨" : "❌ 없음")}");

        var characterPrefab = AssetRegistry.GetPrefab("CharacterPrefab");
        Debug.Log($"캐릭터 프리팹: {(characterPrefab != null ? "✅ 로드됨" : "❌ 없음")}");
    }
}