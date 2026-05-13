// Assets/Scripts/Managers/FieldManager.cs
using UnityEngine;
using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using Fusion;
using AI.UtilitySystem;
using AI.UtilitySystem.Considerations.Placement;
[RequireComponent(typeof(PlacementManager))]
public class FieldManager : MonoBehaviour
{
    public enum BorderDirection
    {
        North,
        South,
        East,
        West
    }

    // ✅ [수정] public 필드 제거, 이제 PlayerManager로부터 주입받음
    public PlayerManager playerManager;

    [Header("정리용 부모 오브젝트")]
    public Transform unitParent;
    public Transform wallParent;

    [Header("생성할 프리팹")]
    [Tooltip("몬스터가 공격하거나 플레이어가 설치할 때 사용되는 파괴 가능한 벽 프리팹입니다.")]
    public GameObject destructibleWallPrefab;
    public GameObject statusBarPrefab;

    [Header("영구 벽 설정")]
    [Tooltip("게임 시작 시 자동 배치할 파괴 불가(영구) 벽 프리팹입니다. 미지정 시 Addressables 키 'PermanentWallPrefab'로 로드합니다.")]
    public GameObject permanentWallPrefab;
    [Tooltip("Addressables에서 영구 벽 프리팹을 로드할 키 (미지정 프리팹 폴백)")]
    public string permanentWallAddressKey = "PermanentWallPrefab";
    [Tooltip("게임 시작 시 각 플레이어 필드에 배치할 영구 벽 개수")]
    public int initialPermanentWallCount = 0;
    [Tooltip("영구/파괴가능 벽이 속할 레이어명 (AstarGrid.wallLayers와 일치해야 함)")]
    public string wallLayerName = "Wall";

    [Header("범위 표시")]
    public GameObject attackRangeIndicatorPrefab;
    public GameObject skillRangeIndicatorPrefab;
    [SerializeField] private float rangeIndicatorYOffset = 0.15f;
    [SerializeField] private int rangeIndicatorSortingOrder = 200;
    private GameObject attackRangeIndicatorInstance;
    private GameObject skillRangeIndicatorInstance;

    [Header("Path Visualization")]
    [Tooltip("경로 표시: 라인 대신 정사각형 마커를 움직여서 표시")]
    public bool useMovingPathMarkers = true;
    [Tooltip("경로 위를 이동할 네모 마커 프리팹 (정사각형 Quad/Sprite)")]
    public GameObject pathMarkerPrefab;
    [Tooltip("마커 높이 보정")]
    public float pathMarkerYOffset = 0.05f;
    [Tooltip("네모 마커 이동 속도")]
    public float pathMarkerSpeed = 4f;
    [Tooltip("네모 마커 생성 간격(초)")]
    public float pathMarkerSpawnInterval = 0.25f;
    [Tooltip("한 번에 준비해둘 마커 개수(풀 사이즈)")]
    public int pathMarkerPoolSize = 32;
    [Tooltip("마커 한 변의 스케일(정사각형 유지)")]
    public float pathMarkerSize = 1f;
    [Tooltip("준비 단계에서만 경로를 표시")]
    public bool showPathInPrepare = true;
    [Header("Economy")]
    [Tooltip("Sell price penalty applied to 2+ star units.")]
    [SerializeField] private int sellPenalty = 1;
    private readonly List<Vector3> _pathWorldPoints = new List<Vector3>();
    private readonly List<GameObject> _pathMarkerPool = new List<GameObject>();
    private Coroutine _markerSpawnRoutine;
    private Coroutine _pathRefreshRoutine;
    private readonly List<GameObject> _activeMarkers = new List<GameObject>();
    private readonly Dictionary<GameObject, Coroutine> _markerRoutines = new Dictionary<GameObject, Coroutine>();
    private float _lastInteractionGateBlockLogRealtime = -10f;

    // [3D Migration] 논리 그리드 설정
    [Header("3D 그리드 설정")]
    [Tooltip("3D 공간에서 논리 그리드의 시작점 (보통 Ground 오브젝트의 위치)")]
    public Vector3 gridOrigin = Vector3.zero;

    [Tooltip("그리드 한 칸의 크기 (미터 단위)")]
    public float cellSize = 1f;

    [Tooltip("그리드 크기 (X, Z 칸 수) - x는 3D의 X, y는 3D의 Z를 의미. 이것이 수비자의 배치 가능 영역입니다.")]
    public Vector2Int gridSize = new Vector2Int(10, 9);

    [Tooltip("동서남북으로 확장할 외곽 셀 수 (몬스터 스폰 영역, 배치 불가)")]
    [SerializeField] private int outerGridMargin = 2;

    /// <summary>
    /// 외곽 확장을 포함한 전체 그리드 크기 (A* 경로 탐색에 사용)
    /// </summary>
    public Vector2Int TotalGridSize => new Vector2Int(gridSize.x + outerGridMargin * 2, gridSize.y + outerGridMargin * 2);

    /// <summary>
    /// 전체 그리드의 원점 (외곽 확장 포함)
    /// </summary>
    public Vector3 TotalGridOrigin => new Vector3(gridOrigin.x - outerGridMargin * cellSize, gridOrigin.y, gridOrigin.z - outerGridMargin * cellSize);

    /// <summary>
    /// 외곽 확장 마진 (셀 단위)
    /// </summary>
    public int OuterGridMargin => outerGridMargin;

    [Tooltip("Ground Renderer의 Bounds로부터 그리드 Origin/Size를 자동 유도합니다. 끄면 인스펙터 설정값을 그대로 사용합니다.")]
    public bool deriveGridFromGroundBounds = false;

    [Header("유닛 배치 높이 설정")]
    [Tooltip("일반 Ground에 배치될 때 Y축 오프셋")]
    public float groundYOffset = 0f;

    [Tooltip("벽(BreakWall) 위에 배치될 때 Y축 오프셋")]
    [SerializeField]
    private float wallYOffset = 1f;

    // 3D Ground 오브젝트 참조 (Raycast 대상)
    public GameObject ground3D { get; private set; }

    // [Deprecated] Tilemap은 호환성을 위해 유지하되, 3D 전환 시 null이 될 수 있음



    private PlacementManager placementManager;
    private Dictionary<Vector3Int, Unit> placedUnits = new Dictionary<Vector3Int, Unit>();
    private readonly HashSet<Vector3Int> pendingUnitPositions = new HashSet<Vector3Int>();
    private readonly Dictionary<Vector3Int, UnitData> pendingUnitDataByPosition = new Dictionary<Vector3Int, UnitData>();
    private const int PendingNetworkMoveLifetimeFrames = 300;
    private struct PendingNetworkMove
    {
        public Vector3Int From;
        public Vector3Int To;
        public int CreatedFrame;
    }

    public struct PendingUnitPlacement
    {
        public Vector3Int Position;
        public UnitData UnitData;
    }
    private readonly List<PendingNetworkMove> pendingNetworkMoves = new List<PendingNetworkMove>();
    private readonly HashSet<uint> retiredNetworkUnitIds = new HashSet<uint>();
    private Dictionary<Vector3Int, DestructibleWall> placedWalls = new Dictionary<Vector3Int, DestructibleWall>();
    // 영구(파괴 불가) 벽 관리
    private Dictionary<Vector3Int, GameObject> placedPermanentWalls = new Dictionary<Vector3Int, GameObject>();
    private readonly HashSet<Vector3Int> authoritativePermanentWallCells = new HashSet<Vector3Int>();
    private int wallLayer = -1;
    private bool permanentWallsGenerated = false;
    private int _lastWallMapRebuildFrame = -1;
    private string _lastWallMapRebuildSummary = "wallMap:notBuilt";
    public bool IsWallMapReady => _lastWallMapRebuildFrame >= 0;
    private int _lastUnitMapRebuildFrame = -1;
    private string _lastUnitMapRebuildSummary = "unitMap:notBuilt";
    public bool IsUnitMapReady => _lastUnitMapRebuildFrame >= 0;
    private float _lastClientUnitMapReconcileTime = -999f;
    private const float ClientUnitMapReconcileIntervalSeconds = 0.25f;
    private string _hostMigrationUnitRestoreInProgressSignature = string.Empty;
    private Coroutine _awaitNetworkPermanentWallsCoroutine;

    private string BuildWallOwnerTag()
    {
        if (playerManager == null)
        {
            return "P?";
        }

        try
        {
            int ownerId = playerManager.playerId;
            return ownerId >= 0 ? $"P{ownerId}" : "P?";
        }
        catch (InvalidOperationException)
        {
            return "P?";
        }
    }

    private string BuildRunnerTag()
    {
        if (playerManager == null || playerManager.Runner == null || !playerManager.Runner.IsRunning)
        {
            return "runner=offline";
        }

        return $"runner={playerManager.Runner.name},isServer={playerManager.Runner.IsServer},isClient={playerManager.Runner.IsClient}";
    }

    private bool IsRunningClientPeer()
    {
        return playerManager != null
               && playerManager.Runner != null
               && playerManager.Runner.IsRunning
               && !playerManager.Runner.IsServer;
    }

    private void SetAuthoritativePermanentWallCells(IEnumerable<Vector3Int> cells)
    {
        authoritativePermanentWallCells.Clear();
        if (cells == null)
        {
            return;
        }

        foreach (var cell in cells)
        {
            if (IsValidGridPosition(cell))
            {
                authoritativePermanentWallCells.Add(cell);
            }
        }
    }

    public int[] BuildPermanentWallSyncPayload(string context)
    {
        RebuildWallMapsAfterMigration(context, false, out _);

        IEnumerable<Vector3Int> sourceCells = authoritativePermanentWallCells.Count > 0
            ? authoritativePermanentWallCells
            : placedPermanentWalls.Keys;

        var orderedCells = sourceCells
            .Where(IsValidGridPosition)
            .Distinct()
            .OrderBy(cell => cell.x)
            .ThenBy(cell => cell.y)
            .ToList();

        if (orderedCells.Count == 0)
        {
            return Array.Empty<int>();
        }

        int[] flat = new int[orderedCells.Count * 2];
        for (int i = 0; i < orderedCells.Count; i++)
        {
            flat[i * 2] = orderedCells[i].x;
            flat[i * 2 + 1] = orderedCells[i].y;
        }

        return flat;
    }

    private Unit selectedUnit;
    private Vector3Int originalUnitPosition;
    private Vector3 offset;
    private Fusion.NetworkTransform selectedUnitNetworkTransform; // 드래그 중 NetworkTransform 참조

    private readonly List<Consideration> _placementConsiderations = new List<Consideration>
    {
        // --- 근접 유닛 우선순위 (요청사항에 따라 가중치 조정) ---
        new MeleePlacementConsideration { weight = 3.0f },      // 0. Ground 타일 (가장 중요)
        new OnMonsterPathConsideration { weight = 10.0f },      // 1. 몬스터 경로 위 (최우선)
        new MeleeProtectsRangedConsideration { weight = 1.6f }, // 2. 경로 위에서 원거리 유닛 보호

        // --- 공통 / 원거리 유닛 우선순위 ---
        new ProximityToAlliesConsideration { weight = 1.2f },   // 유닛끼리 뭉치기
        new AttackRangeCoverageConsideration { weight = 1.0f }, // 공격 범위 효율
        new RangedUnitSynergyConsideration { weight = 1.5f },   // 원거리 유닛이 근접 유닛 근처에
    };

    // 유닛 클릭/드래그 및 상세 정보 패널 관련 변수
    private float mouseDownTimer;
    private const float dragDelay = 0.2f; // 0.2초 이상 누르면 드래그 시작
    [Header("드래그 설정")]
    [Tooltip("드래그 중 유닛이 떠오르는 높이(Y)")]
    public float dragLiftHeight = 3f;
    [Tooltip("드래그 중 마우스를 따라가는 보간 속도")]
    public float dragFollowSpeed = 20f;
    [Tooltip("유닛을 드래그 시작으로 인식할 최대 스크린 거리(픽셀)")]
    public float dragPickMaxScreenDistance = 80f;
    [Header("Selection")]
    [Tooltip("Enable world-distance fallback selection when raycast/screen bounds miss.")]
    public bool useWorldDistanceFallback = false;
    [Tooltip("Max world-space distance used for fallback unit selection.")]
    public float clickPickMaxWorldDistance = 0.6f;
    // 드래그 상태 값 (3D용)
    private float dragBaseY;           // 드래그 시작 시의 기준 Y 값
    private Vector2 offsetXZ;          // 마우스 대비 유닛의 XZ 평면 오프셋

    // AI 배치 디버그용 변수들
    private Dictionary<Vector3Int, float> _debugTileScores = new Dictionary<Vector3Int, float>();
    private Dictionary<Vector3Int, DebugScoreBreakdown> _debugScoreBreakdowns = new Dictionary<Vector3Int, DebugScoreBreakdown>();
#pragma warning disable CS0414 // 디버그 시각화용 변수 (추후 사용 예정)
    private bool _showDebugScores = false;
#pragma warning restore CS0414
    private UnitData _debugUnitData;

    // 디버그용 점수 세부사항 구조체
    public struct DebugScoreBreakdown
    {
        public float groundScore;
        public float pathScore;
        public float allyScore;
        public float totalScore;
    }
    private bool isDragStarted = false;
    private GameObject unitDetailPanelInstance;
    private Unit unitDisplayedInPanel;
    private GameObject unitSellPanelInstance;
    private Unit unitDisplayedInSellPanel;
    private GameObject wallRemovePanelInstance;
    private DestructibleWall wallDisplayedInRemovePanel;

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

    public Camera PlayerCamera => playerCamera;

    /// <summary>
    /// 쿼터뷰 등 기울어진 카메라에서, 화면상의 마우스와 가장 겹쳐 보이는 그리드 셀을 찾습니다.
    /// 기준은 각 셀 중심의 스크린 좌표와 현재 마우스 스크린 좌표 간의 거리입니다.
    /// </summary>
    private Vector3Int GetBestGridUnderMouse(int searchRadius = 2)
    {
        Vector3 mouseWorld = GetMouseWorldPosition();
        Vector2 mouseScreen = MdfInput.PointerPosition;
        Vector3Int guess = WorldToGridInt(mouseWorld);

        float bestDist = float.MaxValue;
        Vector3Int best = guess;

        for (int dy = -searchRadius; dy <= searchRadius; dy++)
        {
            for (int dx = -searchRadius; dx <= searchRadius; dx++)
            {
                int gx = guess.x + dx;
                int gy = guess.y + dy;
                if (gx < 0 || gy < 0 || gx >= gridSize.x || gy >= gridSize.y) continue;

                var cell = new Vector3Int(gx, gy, 0);
                Vector3 center = GridToWorld(cell, checkForWall: true);

                Vector3 centerScreen = playerCamera.WorldToScreenPoint(center);
                float dist = Vector2.SqrMagnitude((Vector2)centerScreen - mouseScreen);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = cell;
                }
            }
        }

        return best;
    }

    void Awake()
    {
        placementManager = GetComponent<PlacementManager>();
        UpdateWallYOffsetFromPrefab();
        wallLayer = LayerMask.NameToLayer(wallLayerName);
    }

    // ✅ [추가된 핵심 로직] PlayerManager가 호출하여 초기화
    // [3D Migration] Tilemap 대신 3D Ground를 받도록 오버로드 추가
    public void Initialize(PlayerManager owner, GameObject ground3DObject)
    {
        this.playerManager = owner;
        this.ground3D = ground3DObject;
        pendingUnitPositions.Clear();
        pendingUnitDataByPosition.Clear();
        pendingNetworkMoves.Clear();
        retiredNetworkUnitIds.Clear();

        // 3D Ground 기준으로 항상 그리드 원점을 정렬하고, 필요 시에만 사이즈를 유도합니다.
        if (ground3D != null)
        {
            Renderer renderer = ground3D.GetComponent<Renderer>();
            if (renderer != null)
            {
                Bounds bounds = renderer.bounds;
                float epsilon = Mathf.Max(1e-4f * cellSize, Mathf.Epsilon);
                int minIndexX = Mathf.FloorToInt(bounds.min.x / cellSize);
                int minIndexZ = Mathf.FloorToInt(bounds.min.z / cellSize);

                // 항상 per-player ground에 맞게 원점을 정렬 (겹침 방지)
                gridOrigin = new Vector3(minIndexX * cellSize, 0, minIndexZ * cellSize);

                if (deriveGridFromGroundBounds)
                {
                    int maxIndexExclusiveX = Mathf.FloorToInt((bounds.max.x - epsilon) / cellSize) + 1;
                    int maxIndexExclusiveZ = Mathf.FloorToInt((bounds.max.z - epsilon) / cellSize) + 1;
                    gridSize = new Vector2Int(
                        Mathf.Max(1, maxIndexExclusiveX - minIndexX),
                        Mathf.Max(1, maxIndexExclusiveZ - minIndexZ)
                    );
                    
                }
                else
                {
                    
                }
            }
            else
            {
                // Renderer가 없으면 Transform 위치를 기준으로 최소 인덱스 정렬
                int minIndexX = Mathf.FloorToInt(ground3D.transform.position.x / cellSize);
                int minIndexZ = Mathf.FloorToInt(ground3D.transform.position.z / cellSize);
                gridOrigin = new Vector3(minIndexX * cellSize, 0, minIndexZ * cellSize);
                
            }
        }

        if (unitParent == null)
        {
            GameObject parentObject = new GameObject($"[{playerManager.name} Units]");
            parentObject.transform.SetParent(transform.parent);
            unitParent = parentObject.transform;
        }

        if (wallParent == null)
        {
            GameObject parentObject = new GameObject($"[{playerManager.name} Walls]");
            parentObject.transform.SetParent(transform.parent);
            wallParent = parentObject.transform;
        }

        // 먼저 현재 씬/복원 오브젝트 기준으로 벽 맵을 재구성해 기존 영구벽 존재 여부를 반영합니다.
        RebuildWallMapsAfterMigration("FieldManager.Initialize.PreGenerate", false, out _);

        int ownerId = playerManager != null ? playerManager.playerId : -1;
        if (ownerId >= 0)
        {
            GeneratePermanentWallsIfNeeded();
        }

        // 그리드 디버그 라인 생성 (showGridDebug가 true일 때만)
        CreateGridLines();
        RebuildWallMapsAfterMigration("FieldManager.Initialize.PostGenerate", false, out _);
    }



    #region 3D Grid Coordinate Conversion

    /// <summary>
    /// 논리 그리드 좌표를 3D 월드 좌표로 변환합니다.
    /// </summary>
    /// <param name="gridPos">그리드 좌표 (x, y)</param>
    /// <param name="checkForWall">벽 존재 여부를 확인하여 Y 오프셋 적용 여부</param>
    /// <returns>3D 월드 좌표 (셀 중심점)</returns>
    public Vector3 GridToWorld(Vector2Int gridPos, bool checkForWall = false)
    {
        // 그리드 좌표를 3D 월드 좌표로 변환
        float worldX = gridOrigin.x + (gridPos.x + 0.5f) * cellSize;
        float worldZ = gridOrigin.z + (gridPos.y + 0.5f) * cellSize;

        // Y 오프셋 계산 (벽 위인지 확인)
        float yOffset = gridOrigin.y + groundYOffset;
        if (checkForWall && ground3D != null)
        {
            Vector3Int gridPos3D = new Vector3Int(gridPos.x, gridPos.y, 0);
            if (HasWallAt(gridPos3D))
            {
                yOffset = gridOrigin.y + wallYOffset;
            }
        }

        return new Vector3(worldX, yOffset, worldZ);
    }

    /// <summary>
    /// 3D 월드 좌표를 논리 그리드 좌표로 변환합니다.
    /// </summary>
    /// <param name="worldPos">3D 월드 좌표</param>
    /// <returns>그리드 좌표</returns>
    public Vector2Int WorldToGrid(Vector3 worldPos)
    {
        // 3D 월드 좌표를 그리드 좌표로 변환 (X, Z 사용)
        int gridX = Mathf.FloorToInt((worldPos.x - gridOrigin.x) / cellSize);
        int gridY = Mathf.FloorToInt((worldPos.z - gridOrigin.z) / cellSize);
        // 경계에서의 미세한 오차로 인해 size 인덱스가 되는 것을 방지하기 위해 유효 범위로 클램프
        gridX = Mathf.Clamp(gridX, 0, Mathf.Max(0, gridSize.x - 1));
        gridY = Mathf.Clamp(gridY, 0, Mathf.Max(0, gridSize.y - 1));
        return new Vector2Int(gridX, gridY);
    }

    /// <summary>
    /// Vector3Int를 3D 월드 좌표로 변환합니다. (호환성용)
    /// </summary>
    public Vector3 GridToWorld(Vector3Int gridPos, bool checkForWall = false)
    {
        return GridToWorld(new Vector2Int(gridPos.x, gridPos.y), checkForWall);
    }

    /// <summary>
    /// 3D 월드 좌표를 Vector3Int 그리드 좌표로 변환합니다. (호환성용)
    /// </summary>
    public Vector3Int WorldToGridInt(Vector3 worldPos)
    {
        Vector2Int grid2D = WorldToGrid(worldPos);
        return new Vector3Int(grid2D.x, grid2D.y, 0);
    }

    public Vector2Int InnerCellToNavigationCell(Vector2Int innerCell)
    {
        return new Vector2Int(innerCell.x + outerGridMargin, innerCell.y + outerGridMargin);
    }

    public Vector2Int InnerCellToNavigationCell(Vector3Int innerCell)
    {
        return InnerCellToNavigationCell(new Vector2Int(innerCell.x, innerCell.y));
    }

    public bool TryNavigationCellToInnerCell(Vector2Int navigationCell, out Vector3Int innerCell)
    {
        int innerX = navigationCell.x - outerGridMargin;
        int innerY = navigationCell.y - outerGridMargin;
        innerCell = new Vector3Int(innerX, innerY, 0);
        return IsValidGridPosition(innerCell);
    }

    public bool IsValidNavigationCell(Vector2Int navigationCell)
    {
        Vector2Int totalGridSize = TotalGridSize;
        return navigationCell.x >= 0 && navigationCell.x < totalGridSize.x &&
               navigationCell.y >= 0 && navigationCell.y < totalGridSize.y;
    }

    public Vector2Int WorldToNavigationCell(Vector3 worldPos)
    {
        Vector3 totalGridOrigin = TotalGridOrigin;
        Vector2Int totalGridSize = TotalGridSize;
        int gridX = Mathf.FloorToInt((worldPos.x - totalGridOrigin.x) / cellSize);
        int gridY = Mathf.FloorToInt((worldPos.z - totalGridOrigin.z) / cellSize);
        gridX = Mathf.Clamp(gridX, 0, Mathf.Max(0, totalGridSize.x - 1));
        gridY = Mathf.Clamp(gridY, 0, Mathf.Max(0, totalGridSize.y - 1));
        return new Vector2Int(gridX, gridY);
    }

    public Vector3 NavigationCellToWorld(Vector2Int navigationCell, bool checkForWall = false)
    {
        Vector3 totalGridOrigin = TotalGridOrigin;
        float worldX = totalGridOrigin.x + (navigationCell.x + 0.5f) * cellSize;
        float worldZ = totalGridOrigin.z + (navigationCell.y + 0.5f) * cellSize;

        float yOffset = gridOrigin.y + groundYOffset;
        if (checkForWall && TryNavigationCellToInnerCell(navigationCell, out var innerCell) && HasWallAt(innerCell))
        {
            yOffset = gridOrigin.y + wallYOffset;
        }

        return new Vector3(worldX, yOffset, worldZ);
    }

    public List<AstarNode> ConvertNavigationPathToInnerField(List<AstarNode> navigationPath)
    {
        var innerPath = new List<AstarNode>();
        if (navigationPath == null || navigationPath.Count == 0)
        {
            return innerPath;
        }

        foreach (var node in navigationPath)
        {
            if (node == null)
            {
                continue;
            }

            if (TryNavigationCellToInnerCell(new Vector2Int(node.x, node.y), out var innerCell))
            {
                innerPath.Add(new AstarNode(node.isWall, innerCell.x, innerCell.y));
            }
        }

        return innerPath;
    }

    public List<Vector3Int> GetBorderGapCells()
    {
        int centerX = gridSize.x / 2;
        int centerY = gridSize.y / 2;

        return new List<Vector3Int>(4)
        {
            new Vector3Int(centerX, gridSize.y - 1, 0),
            new Vector3Int(centerX, 0, 0),
            new Vector3Int(gridSize.x - 1, centerY, 0),
            new Vector3Int(0, centerY, 0)
        };
    }

    public List<Vector3Int> GetOpenBorderGaps()
    {
        var gaps = new List<Vector3Int>(4);
        foreach (var gapCell in GetBorderGapCells())
        {
            AddOpenBorderGap(gaps, gapCell);
        }

        return gaps;
    }

    public bool TryGetSingleOpenEntryCell(out Vector3Int entryCell)
    {
        var gaps = GetOpenBorderGaps();
        if (gaps.Count == 1)
        {
            entryCell = gaps[0];
            return true;
        }

        entryCell = default;
        return false;
    }

    public bool TryGetSingleOpenEntryNavigationCell(out Vector2Int entryCell)
    {
        if (TryGetSingleOpenEntryCell(out var innerEntryCell))
        {
            entryCell = InnerCellToNavigationCell(innerEntryCell);
            return true;
        }

        entryCell = default;
        return false;
    }

    public Dictionary<BorderDirection, List<Vector3>> GetOuterSpawnWorldPositionsByDirection(LayerMask spawnAreaLayerMask = default)
    {
        var positionsByDirection = new Dictionary<BorderDirection, List<Vector3>>
        {
            { BorderDirection.North, new List<Vector3>() },
            { BorderDirection.South, new List<Vector3>() },
            { BorderDirection.East, new List<Vector3>() },
            { BorderDirection.West, new List<Vector3>() }
        };

        int margin = OuterGridMargin;
        for (int y = gridSize.y; y < gridSize.y + margin; y++)
        {
            for (int x = -margin; x < gridSize.x + margin; x++)
            {
                AddOuterSpawnCandidate(positionsByDirection, spawnAreaLayerMask, new Vector2Int(x, y));
            }
        }

        for (int y = -margin; y < 0; y++)
        {
            for (int x = -margin; x < gridSize.x + margin; x++)
            {
                AddOuterSpawnCandidate(positionsByDirection, spawnAreaLayerMask, new Vector2Int(x, y));
            }
        }

        for (int y = 0; y < gridSize.y; y++)
        {
            for (int x = -margin; x < 0; x++)
            {
                AddOuterSpawnCandidate(positionsByDirection, spawnAreaLayerMask, new Vector2Int(x, y));
            }
        }

        for (int y = 0; y < gridSize.y; y++)
        {
            for (int x = gridSize.x; x < gridSize.x + margin; x++)
            {
                AddOuterSpawnCandidate(positionsByDirection, spawnAreaLayerMask, new Vector2Int(x, y));
            }
        }

        return positionsByDirection;
    }

    public Vector3 GetRandomOuterSpawnWorldPosition(LayerMask spawnAreaLayerMask = default)
    {
        var positionsByDirection = GetOuterSpawnWorldPositionsByDirection(spawnAreaLayerMask);
        var allPositions = new List<Vector3>();
        foreach (var pair in positionsByDirection)
        {
            allPositions.AddRange(pair.Value);
        }

        if (allPositions.Count == 0)
        {
            return GetFallbackOuterSpawnWorldPosition();
        }

        return allPositions[UnityEngine.Random.Range(0, allPositions.Count)];
    }

    public Vector3 GetFallbackOuterSpawnWorldPosition()
    {
        if (TryGetSingleOpenEntryCell(out var entryCell))
        {
            return OuterGridCellToWorld(GetAdjacentOuterCellForGap(entryCell));
        }

        return OuterGridCellToWorld(new Vector2Int(gridSize.x / 2, gridSize.y));
    }

    private void AddOpenBorderGap(List<Vector3Int> gaps, Vector3Int cell)
    {
        if (!IsValidGridPosition(cell) || HasWallAt(cell) || gaps.Contains(cell))
        {
            return;
        }

        gaps.Add(cell);
    }

    private void AddOuterSpawnCandidate(
        Dictionary<BorderDirection, List<Vector3>> positionsByDirection,
        LayerMask spawnAreaLayerMask,
        Vector2Int outerCell)
    {
        Vector3 spawnPosition = OuterGridCellToWorld(outerCell);

        if (spawnAreaLayerMask.value != 0)
        {
            Vector3 rayOrigin = new Vector3(spawnPosition.x, 50f, spawnPosition.z);
            if (!Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, 100f, spawnAreaLayerMask))
            {
                return;
            }

            spawnPosition = hit.point;
        }

        if (!IsOuterFieldWorldPosition(spawnPosition))
        {
            return;
        }

        positionsByDirection[ClassifyBorderDirection(outerCell)].Add(spawnPosition);
    }

    private BorderDirection ClassifyBorderDirection(Vector2Int outerCell)
    {
        if (outerCell.y >= gridSize.y) return BorderDirection.North;
        if (outerCell.y < 0) return BorderDirection.South;
        if (outerCell.x >= gridSize.x) return BorderDirection.East;
        return BorderDirection.West;
    }

    private bool IsOuterFieldWorldPosition(Vector3 worldPosition)
    {
        int rawGridX = Mathf.FloorToInt((worldPosition.x - gridOrigin.x) / cellSize);
        int rawGridY = Mathf.FloorToInt((worldPosition.z - gridOrigin.z) / cellSize);

        bool isInsideGrid = rawGridX >= 0 && rawGridX < gridSize.x &&
                            rawGridY >= 0 && rawGridY < gridSize.y;

        return !isInsideGrid;
    }

    private Vector3 OuterGridCellToWorld(Vector2Int outerCell)
    {
        float worldX = gridOrigin.x + (outerCell.x + 0.5f) * cellSize;
        float worldZ = gridOrigin.z + (outerCell.y + 0.5f) * cellSize;
        return new Vector3(worldX, gridOrigin.y, worldZ);
    }

    private Vector2Int GetAdjacentOuterCellForGap(Vector3Int entryCell)
    {
        if (entryCell.y >= gridSize.y - 1) return new Vector2Int(entryCell.x, gridSize.y);
        if (entryCell.y <= 0) return new Vector2Int(entryCell.x, -1);
        if (entryCell.x >= gridSize.x - 1) return new Vector2Int(gridSize.x, entryCell.y);
        return new Vector2Int(-1, entryCell.y);
    }

    /// <summary>
    /// 주어진 월드 좌표를 논리 그리드의 월드 경계 내로 클램프합니다. (X/Z만 제한, Y는 그대로)
    /// 최대 경계는 배타적으로 취급하여 최댓값에서의 내림으로 인한 유령 셀을 방지합니다.
    /// </summary>
    public Vector3 ClampToGrid(Vector3 worldPos)
    {
        float minX = gridOrigin.x;
        float minZ = gridOrigin.z;
        float maxXExclusive = gridOrigin.x + gridSize.x * cellSize;
        float maxZExclusive = gridOrigin.z + gridSize.y * cellSize;
        float epsilon = Mathf.Max(1e-4f * cellSize, Mathf.Epsilon);

        float clampedX = Mathf.Clamp(worldPos.x, minX, maxXExclusive - epsilon);
        float clampedZ = Mathf.Clamp(worldPos.z, minZ, maxZExclusive - epsilon);
        return new Vector3(clampedX, worldPos.y, clampedZ);
    }

    /// <summary>
    /// 그리드 좌표가 유효한 범위 내에 있는지 확인합니다.
    /// </summary>
    public bool IsValidGridPosition(Vector2Int gridPos)
    {
        return gridPos.x >= 0 && gridPos.x < gridSize.x &&
               gridPos.y >= 0 && gridPos.y < gridSize.y;
    }

    public bool IsValidGridPosition(Vector3Int gridPos)
    {
        return IsValidGridPosition(new Vector2Int(gridPos.x, gridPos.y));
    }

    #endregion

    #region Path Line Helpers

    public bool IsLocalControlledField()
    {
        var gm = GameManagers.Instance;
        if (playerManager == null) return false;
        if (playerManager.Object == null) return true; // fallback in non-networked testing
        if (playerManager.Object.HasInputAuthority) return true;
        return gm != null && gm.localPlayer == playerManager;
    }

    private bool CanProcessLocalFieldInput()
    {
        if (!IsLocalControlledField())
        {
            return false;
        }

        var gm = GameManagers.Instance;
        if (gm == null)
        {
            return false;
        }

        if (!gm.IsPrepareInteractionReadyForField(playerManager, out string reason))
        {
            if (Time.realtimeSinceStartup - _lastInteractionGateBlockLogRealtime > 1f)
            {
                _lastInteractionGateBlockLogRealtime = Time.realtimeSinceStartup;
                Debug.Log($"[HM-INPUT-GATE] blocked owner={BuildWallOwnerTag()} reason={reason}");
            }

            return false;
        }

        return true;
    }

    private void SchedulePathRefresh()
    {
        if (!showPathInPrepare) return;
        if (_pathRefreshRoutine != null)
        {
            StopCoroutine(_pathRefreshRoutine);
        }
        _pathRefreshRoutine = StartCoroutine(RefreshPathLineNextFrame());
    }

    private System.Collections.IEnumerator RefreshPathLineNextFrame()
    {
        // 물리/콜라이더 업데이트가 반영된 뒤 실행
        yield return new WaitForFixedUpdate();
        yield return new WaitForEndOfFrame();
        RefreshPathLine();
        _pathRefreshRoutine = null;
    }

    private void RefreshPathLine()
    {
        if (!showPathInPrepare || !IsLocalControlledField())
        {
            return;
        }

        var grid = playerManager != null ? playerManager.astarGrid : null;
        if (grid == null || playerManager.goalTransform == null || !TryGetSingleOpenEntryNavigationCell(out var startPos))
        {
            return;
        }

        Vector2Int endPos = WorldToNavigationCell(playerManager.goalTransform.position);

        if (!grid.FindPath(startPos, endPos))
        {
            return;
        }

        var path = grid.FinalPath;
        if (path == null || path.Count < 2)
        {
            return;
        }

        // 경로를 월드 좌표로 변환해 캐시
        _pathWorldPoints.Clear();
        for (int i = 0; i < path.Count; i++)
        {
            var node = path[i];
            Vector3 pos = NavigationCellToWorld(new Vector2Int(node.x, node.y));
            pos.y += pathMarkerYOffset;
            _pathWorldPoints.Add(pos);
        }

        // 마커 표시
        // 경로가 바뀌면 기존 마커를 모두 회수하고 새 경로로 다시 흘려보냅니다.
        StopMarkerFlow();
        RestartMarkerFlow();
    }

    private void HidePathLine()
    {
        StopMarkerFlow();
        if (_pathRefreshRoutine != null)
        {
            StopCoroutine(_pathRefreshRoutine);
            _pathRefreshRoutine = null;
        }
    }

    private void RestartMarkerFlow()
    {
        StopMarkerSpawn();
        if (_pathWorldPoints.Count < 2) return;
        WarmupMarkerPool();
        _markerSpawnRoutine = StartCoroutine(SpawnMarkersRoutine());
    }

    private void StopMarkerFlow()
    {
        StopMarkerSpawn();
        foreach (var kv in _markerRoutines)
        {
            if (kv.Value != null) StopCoroutine(kv.Value);
        }
        _markerRoutines.Clear();
        foreach (var marker in _activeMarkers)
        {
            if (marker != null) marker.SetActive(false);
        }
        _activeMarkers.Clear();
    }

    private void StopMarkerSpawn()
    {
        if (_markerSpawnRoutine != null)
        {
            StopCoroutine(_markerSpawnRoutine);
            _markerSpawnRoutine = null;
        }
    }

    private void WarmupMarkerPool()
    {
        if (pathMarkerPrefab == null) return;
        while (_pathMarkerPool.Count < pathMarkerPoolSize)
        {
            var go = Instantiate(pathMarkerPrefab, transform);
            SetupMarkerTransform(go.transform);
            go.SetActive(false);
            _pathMarkerPool.Add(go);
        }
    }

    private GameObject GetMarkerFromPool()
    {
        foreach (var marker in _pathMarkerPool)
        {
            if (marker != null && !marker.activeSelf)
            {
                SetupMarkerTransform(marker.transform);
                return marker;
            }
        }
        if (_pathMarkerPool.Count < pathMarkerPoolSize && pathMarkerPrefab != null)
        {
            var go = Instantiate(pathMarkerPrefab, transform);
            SetupMarkerTransform(go.transform);
            go.SetActive(false);
            _pathMarkerPool.Add(go);
            return go;
        }
        return null;
    }

    private void SetupMarkerTransform(Transform t)
    {
        if (t == null) return;
        // 정사각형 유지: 한 변 스케일을 통일
        t.localScale = Vector3.one * pathMarkerSize;
        // 지형과 수평: 노멀을 위쪽으로
        t.rotation = Quaternion.LookRotation(Vector3.up, Vector3.forward);
    }

    private System.Collections.IEnumerator SpawnMarkersRoutine()
    {
        while (showPathInPrepare && IsLocalControlledField() && GameManagers.Instance != null &&
               GameManagers.Instance.GetGameState() == GameManagers.GameState.Prepare &&
               !GameManagers.Instance.IsSequenceTransitioning)
        {
            var marker = GetMarkerFromPool();
            if (marker != null)
            {
                marker.SetActive(true);
                var snapshot = new List<Vector3>(_pathWorldPoints);
                _activeMarkers.Add(marker);
                var routine = StartCoroutine(MoveMarkerAlongPath(marker, snapshot));
                _markerRoutines[marker] = routine;
            }
            yield return new WaitForSeconds(pathMarkerSpawnInterval);
        }
    }

    private System.Collections.IEnumerator MoveMarkerAlongPath(GameObject marker, List<Vector3> pathSnapshot)
    {
        if (marker == null || pathSnapshot == null || pathSnapshot.Count < 2)
        {
            if (marker != null) marker.SetActive(false);
            yield break;
        }

        int seg = 0;
        Vector3 pos = pathSnapshot[0];
        marker.transform.position = pos;

        while (seg < pathSnapshot.Count - 1 && GameManagers.Instance != null &&
               GameManagers.Instance.GetGameState() == GameManagers.GameState.Prepare &&
               !GameManagers.Instance.IsSequenceTransitioning)
        {
            Vector3 a = pathSnapshot[seg];
            Vector3 b = pathSnapshot[seg + 1];
            float dist = Vector3.Distance(a, b);
            float travelled = 0f;
            while (travelled < dist &&
                   GameManagers.Instance.GetGameState() == GameManagers.GameState.Prepare &&
                   !GameManagers.Instance.IsSequenceTransitioning)
            {
                float step = pathMarkerSpeed * Time.deltaTime;
                travelled += step;
                float t = Mathf.Clamp01(travelled / Mathf.Max(0.001f, dist));
                marker.transform.position = Vector3.Lerp(a, b, t);
                yield return null;
            }
            seg++;
        }

        marker.SetActive(false);
        _activeMarkers.Remove(marker);
        _markerRoutines.Remove(marker);
    }

    #endregion

    // ... (이하 나머지 코드는 이전과 동일) ...
    // OnEnable, OnDisable, Update, Event Handlers, 벽/유닛 관리, 드래그앤드롭 로직 등
    void OnEnable()
    {
        GameEvents.OnGameStateChanged += HandleGameStateChange;
    }

    void OnDisable()
    {
        GameEvents.OnGameStateChanged -= HandleGameStateChange;
    }

    void Update()
    {
        if (placementManager.GetCurrentMode() == PlacementMode.None && CanProcessLocalFieldInput())
        {
            HandleUnitDragAndDrop();
        }
        // 즉시 갱신이 필요한 경우(벽/유닛 배치 변경)에는 _pathRefreshRoutine에서 처리

        // 런타임 그리드 디버그 표시 (Game 뷰에서 Gizmos 버튼 ON 필요)
        DrawGridDebugLines();
    }

    #region Public Methods for UI

    public void StartPlacementMode(PlacementMode mode, GameObject unitPrefab = null)
    {
        placementManager.StartPlacementMode(mode, unitPrefab);
    }

    public void StopPlacementMode()
    {
        placementManager.StopPlacementMode();
    }

    public PlacementMode GetPlacementMode()
    {
        return placementManager != null ? placementManager.GetCurrentMode() : PlacementMode.None;
    }

    public void TogglePlacementMode(PlacementMode mode, GameObject unitPrefab = null)
    {
        if (placementManager.GetCurrentMode() == mode)
        {
            placementManager.StopPlacementMode();
        }
        else
        {
            placementManager.StartPlacementMode(mode, unitPrefab);
        }
    }

    #endregion

    #region Event Handlers

    private void HandleGameStateChange(GameManagers.GameState newState)
    {
        if (newState == GameManagers.GameState.Prepare)
        {
            RespawnAllUnits();
            if (showPathInPrepare)
            {
                RefreshPathLine();
            }
        }
        // [수정] 게임 상태가 전투로 변경될 때의 처리
        else if (newState == GameManagers.GameState.Battle1 || newState == GameManagers.GameState.Battle2)
        {
            // 활성화된 배치 모드(유닛, 벽 등)가 있다면 강제로 종료합니다.
            if (placementManager.GetCurrentMode() != PlacementMode.None)
            {
                placementManager.StopPlacementMode();
            }

            // 유닛을 드래그하는 중이었다면 취소하고 원위치시킵니다.
            if (selectedUnit != null)
            {
                Vector3 originalWorldPos = GridToWorld(originalUnitPosition, checkForWall: true);
                SnapbackSelectedUnit(originalWorldPos);
                // 드래그 중에는 placedUnits에서 제거되지 않으므로, 다시 Add할 필요가 없습니다.
                

                // 드래그 상태를 초기화합니다.
                selectedUnit = null;
            }
            HidePathLine();
            HideUnitSellPanel();
        }
    }

    #endregion

    #region 벽 생성 및 관리

    public void CreateWallAt(Vector3Int gridPosition)
    {
        Debug.Log($"[WallFlow-Create] CreateWallAt ENTER owner={BuildWallOwnerTag()}, pos={gridPosition}, hasStateAuth={(playerManager != null && playerManager.Object != null && playerManager.Object.IsValid && playerManager.Object.HasStateAuthority)}, {BuildRunnerTag()}");

        if (destructibleWallPrefab == null)
        {
            Debug.LogWarning($"[WallFlow-Create] abort: destructibleWallPrefab is null. owner={BuildWallOwnerTag()}, pos={gridPosition}");
            // Debug.LogError($"[FieldManager] CreateWallAt failed: destructibleWallPrefab is null (Player={playerManager?.playerId}) at {gridPosition}");
            return;
        }
        if (HasWallAt(gridPosition))
        {
            Debug.LogWarning($"[WallFlow-Create] abort: wall already exists. owner={BuildWallOwnerTag()}, pos={gridPosition}");
            // Debug.LogWarning($"[FieldManager] CreateWallAt ignored: wall already exists at {gridPosition} (Player={playerManager?.playerId})");
            return;
        }
        if (!IsValidGridPosition(gridPosition))
        {
            Debug.LogWarning($"[WallFlow-Create] abort: invalid grid position. owner={BuildWallOwnerTag()}, pos={gridPosition}, gridSize={gridSize}");
            // Debug.LogWarning($"[FieldManager] CreateWallAt 무시: 유효 범위 밖 위치 {gridPosition} (GridSize={gridSize})");
            return;
        }

        Unit occupant = GetUnitAt(gridPosition);
        if (occupant != null)
        {
            if (occupant.Data.unitType == UnitType.Melee)
            {
                Vector3Int? alt = FindFirstEmptySlot(occupant.Data);
                if (!alt.HasValue)
                {
                    Debug.LogWarning($"[WallFlow-Create] abort: no empty slot to relocate melee unit. owner={BuildWallOwnerTag()}, pos={gridPosition}");
                    // Debug.LogWarning($"[FieldManager] CreateWallAt aborted: no empty slot to relocate melee unit at {gridPosition} (Player={playerManager?.playerId})");
                    return;
                }
                MoveUnit(gridPosition, alt.Value);
            }
        }

        Vector3 worldPos = GridToWorld(gridPosition);
        float halfWallHeight = GetWallPrefabHeight() * 0.5f;
        worldPos.y += halfWallHeight;

        GameObject wallGO = null;
        var runner = playerManager != null ? playerManager.Runner : null;
        if (runner != null && destructibleWallPrefab.TryGetComponent<NetworkObject>(out var netPrefab))
        {
            if (!playerManager.Object.HasStateAuthority)
            {
                Debug.LogWarning($"[WallFlow-Create] abort: no state authority for network wall spawn. owner={BuildWallOwnerTag()}, pos={gridPosition}");
                return;
            }
            var spawned = runner.Spawn(netPrefab, worldPos, Quaternion.identity, playerManager.Object.InputAuthority);
            if (spawned == null)
            {
                Debug.LogError($"[WallFlow-Create] abort: Runner.Spawn failed. owner={BuildWallOwnerTag()}, pos={gridPosition}");
                // Debug.LogError($"[FieldManager] Runner.Spawn 실패: {destructibleWallPrefab.name} (Player={playerManager?.playerId})");
                return;
            }
            wallGO = spawned.gameObject;
            if (wallParent != null)
            {
                wallGO.transform.SetParent(wallParent, true);
            }
        }
        else
        {
            wallGO = Instantiate(destructibleWallPrefab, worldPos, Quaternion.identity, wallParent);
        }
        DestructibleWall wallComponent = wallGO.GetComponent<DestructibleWall>();

        if (wallComponent != null)
        {
            AttachStatusBar(wallGO, wallComponent.SetStatusBar);
            wallComponent.Initialize(this, gridPosition);
            placedWalls.Add(gridPosition, wallComponent);
            SchedulePathRefresh();

            Unit unitOnCell = GetUnitAt(gridPosition);
            if (unitOnCell != null && unitOnCell.Data.unitType == UnitType.Ranged)
            {
                Vector3 atopPos = GridToWorld(gridPosition, checkForWall: true);
                MoveUnitImmediate(unitOnCell, atopPos);
            }

            Debug.Log($"[WallFlow-Create] CreateWallAt SUCCESS owner={BuildWallOwnerTag()}, pos={gridPosition}, worldPos={worldPos}, totalWalls={placedWalls.Count}");
        }
        else
        {
            Debug.LogError($"[WallFlow-Create] abort: spawned wall has no DestructibleWall component. owner={BuildWallOwnerTag()}, pos={gridPosition}");
            // Debug.LogError($"{destructibleWallPrefab.name} 프리팹에 DestructibleWall 컴포넌트가 없습니다!", wallGO);
            Destroy(wallGO);
        }
    }

    public void RemoveWallAt(Vector3Int gridPosition)
    {
        if (placedWalls.TryGetValue(gridPosition, out DestructibleWall wall))
        {
            Unit unitOnTop = GetUnitAt(gridPosition);
            if (unitOnTop != null)
            {
                var gm = GameManagers.Instance;
                bool isPreparePhase = gm != null &&
                                      gm.GetGameState() == GameManagers.GameState.Prepare &&
                                      !gm.IsSequenceTransitioning;

                if (isPreparePhase)
                {
                    // 준비 단계: 유닛을 벽 아래 높이로 재배치 (죽이지 않음)
                    Vector3 newPos = GridToWorld(gridPosition, checkForWall: false);
                    MoveUnitImmediate(unitOnTop, newPos);
                }
                else
                {
                    // 전투 단계: 벽 파괴 시 유닛도 사망
                    unitOnTop.TakeDamage(99999, DamageType.Physical);
                }
            }

            var runner = playerManager != null ? playerManager.Runner : null;
            if (runner != null && wall.TryGetComponent<NetworkObject>(out var no) && playerManager.Object.HasStateAuthority)
            {
                runner.Despawn(no);
            }
            else
            {
                Destroy(wall.gameObject);
            }
            placedWalls.Remove(gridPosition);
            SchedulePathRefresh();
        }
    }

    public DestructibleWall GetWallAt(Vector3Int gridPosition)
    {
        placedWalls.TryGetValue(gridPosition, out DestructibleWall wall);
        return wall;
    }

    // 파괴 가능/불가를 포함한 모든 벽 존재 여부
    public bool HasWallAt(Vector3Int gridPosition)
    {
        return placedWalls.ContainsKey(gridPosition) || placedPermanentWalls.ContainsKey(gridPosition);
    }

    /// <summary>
    /// Host Migration 이후 런타임 벽 딕셔너리를 월드 오브젝트 기준으로 재구성합니다.
    /// </summary>
    public bool RebuildWallMapsAfterMigration(string context, bool verboseLog, out string summary, bool forceRebuild = false)
    {
        if (!forceRebuild && _lastWallMapRebuildFrame == Time.frameCount)
        {
            summary = _lastWallMapRebuildSummary;
            return true;
        }

        var oldDestructibleCells = new HashSet<Vector3Int>(placedWalls.Keys);
        var oldPermanentCells = new HashSet<Vector3Int>(placedPermanentWalls.Keys);
        var rebuiltDestructible = new Dictionary<Vector3Int, DestructibleWall>();
        var rebuiltPermanent = new Dictionary<Vector3Int, GameObject>();

        int destructibleCandidates = 0;
        int permanentCandidates = 0;
        int duplicates = 0;
        int outOfBounds = 0;

        var destructibleWalls = new List<DestructibleWall>();
        if (wallParent != null)
        {
            destructibleWalls.AddRange(wallParent.GetComponentsInChildren<DestructibleWall>(true));
        }

        foreach (var wall in placedWalls.Values)
        {
            if (IsLiveDestructibleWallCandidate(wall))
            {
                destructibleWalls.Add(wall);
            }
        }

        // Network Spawn된 파괴 가능 벽은 migration 이후 wallParent에 속하지 않을 수 있으므로
        // 전역 후보도 함께 훑어 현재 필드 범위에 속한 벽을 다시 수집합니다.
        var globalDestructibleCandidates = UnityEngine.Object.FindObjectsOfType<DestructibleWall>(true)
            .Where(wall => wall != null && IsWorldPositionInsideOwnedGrid(wall.transform.position));

        foreach (var wall in globalDestructibleCandidates)
        {
            destructibleWalls.Add(wall);
        }

        foreach (var wall in destructibleWalls.Where(IsLiveDestructibleWallCandidate).Distinct())
        {
            destructibleCandidates++;
            Vector3Int cell = WorldToGridInt(wall.transform.position);
            if (!IsValidGridPosition(cell))
            {
                outOfBounds++;
                continue;
            }

            if (authoritativePermanentWallCells.Count > 0 && authoritativePermanentWallCells.Contains(cell))
            {
                continue;
            }

            wall.RebindAfterMigration(this, cell);
            if (!rebuiltDestructible.TryAdd(cell, wall))
            {
                duplicates++;
            }
        }

        var permanentObjects = new List<GameObject>();
        if (wallParent != null)
        {
            foreach (Transform child in wallParent)
            {
                if (child != null)
                {
                    permanentObjects.Add(child.gameObject);
                }
            }
        }

        foreach (var kv in placedPermanentWalls)
        {
            if (kv.Value != null)
            {
                permanentObjects.Add(kv.Value);
            }
        }

        // Network Spawn된 영구벽은 wallParent에 속하지 않을 수 있으므로 전역 후보도 포함합니다.
        var globalPermanentCandidates = UnityEngine.Object.FindObjectsOfType<NetworkObject>(true)
            .Where(no => no != null && no.gameObject != null)
            .Select(no => no.gameObject)
            .Where(IsLikelyPermanentWallObject)
            .Where(obj => IsWorldPositionInsideOwnedGrid(obj.transform.position));

        foreach (var candidate in globalPermanentCandidates)
        {
            permanentObjects.Add(candidate);
        }

        foreach (var wallObj in permanentObjects.Where(obj => obj != null).Distinct())
        {
            if (wallObj.GetComponent<DestructibleWall>() != null)
            {
                continue;
            }

            permanentCandidates++;
            Vector3Int cell = WorldToGridInt(wallObj.transform.position);
            if (!IsValidGridPosition(cell))
            {
                outOfBounds++;
                continue;
            }

            if (authoritativePermanentWallCells.Count > 0 && !authoritativePermanentWallCells.Contains(cell))
            {
                continue;
            }

            if (rebuiltDestructible.ContainsKey(cell))
            {
                continue;
            }

            if (!rebuiltPermanent.TryAdd(cell, wallObj))
            {
                duplicates++;
            }
        }

        placedWalls = rebuiltDestructible;
        placedPermanentWalls = rebuiltPermanent;

        // 로컬 플래그가 초기값(false)인 상태로 복원될 수 있으므로,
        // 재구성 결과에 영구벽이 있으면 즉시 생성 완료 상태로 승격합니다.
        bool migrationInProgress = HostMigrationHandler.Instance != null && HostMigrationHandler.Instance.IsMigrating;
        bool canPromoteGeneratedFromRebuild = migrationInProgress || !IsRunningClientPeer();
        if (canPromoteGeneratedFromRebuild && !permanentWallsGenerated && placedPermanentWalls.Count > 0)
        {
            permanentWallsGenerated = true;
            if (verboseLog)
            {
                Debug.Log($"[WallFlow-Migration] promoted permanentWallsGenerated=true from rebuilt map. owner={BuildWallOwnerTag()}, permanentWalls={placedPermanentWalls.Count}, ctx={context}");
            }
        }

        bool destructibleChanged = !oldDestructibleCells.SetEquals(placedWalls.Keys);
        bool permanentChanged = !oldPermanentCells.SetEquals(placedPermanentWalls.Keys);
        bool wallMapChanged = destructibleChanged || permanentChanged;
        if (wallMapChanged)
        {
            SchedulePathRefresh();
        }

        _lastWallMapRebuildFrame = Time.frameCount;
        _lastWallMapRebuildSummary =
            $"ctx={context},destructible={placedWalls.Count}/{destructibleCandidates},permanent={placedPermanentWalls.Count}/{permanentCandidates},outOfBounds={outOfBounds},duplicates={duplicates},changed={wallMapChanged}";
        summary = _lastWallMapRebuildSummary;

        if (verboseLog)
        {
            Debug.Log($"[WallFlow-Migration] RebuildWallMapsAfterMigration {_lastWallMapRebuildSummary}");
        }

        return true;
    }

    private static bool IsLiveDestructibleWallCandidate(DestructibleWall wall)
    {
        return wall != null
            && wall.gameObject != null
            && wall.gameObject.activeSelf;
    }

    /// <summary>
    /// Host Migration 이후 런타임 유닛 맵(placedUnits)을 월드 오브젝트 기준으로 재구성합니다.
    /// </summary>
    public bool RebuildUnitMapAfterMigration(string context, bool verboseLog, out string summary)
    {
        if (_lastUnitMapRebuildFrame == Time.frameCount)
        {
            summary = _lastUnitMapRebuildSummary;
            return true;
        }

        var oldCells = new HashSet<Vector3Int>(placedUnits.Keys);
        var rebuiltUnits = new Dictionary<Vector3Int, Unit>();

        int candidates = 0;
        int registered = 0;
        int duplicates = 0;
        int outOfBounds = 0;
        int missingData = 0;

        var unitCandidates = new List<Unit>();

        foreach (var unit in placedUnits.Values)
        {
            if (unit != null)
            {
                unitCandidates.Add(unit);
            }
        }

        if (playerManager != null && playerManager.ownedUnits != null)
        {
            foreach (var unit in playerManager.ownedUnits)
            {
                if (unit != null)
                {
                    unitCandidates.Add(unit);
                }
            }
        }

        if (unitParent != null)
        {
            unitCandidates.AddRange(unitParent.GetComponentsInChildren<Unit>(true));
        }

        var globalCandidates = UnityEngine.Object.FindObjectsOfType<Unit>(true)
            .Where(unit => unit != null)
            .Where(unit =>
                playerManager == null ||
                playerManager.Runner == null ||
                !playerManager.Runner.IsRunning ||
                unit.Runner == playerManager.Runner)
            .Where(unit =>
                IsWorldPositionInsideOwnedGrid(unit.transform.position) ||
                UnitHasReplicatedFieldOwner(unit));
        unitCandidates.AddRange(globalCandidates);

        var existingCellsByUnit = placedUnits
            .Where(kvp => kvp.Value != null)
            .GroupBy(kvp => kvp.Value)
            .ToDictionary(group => group.Key, group => group.First().Key);
        var ownedUnits = playerManager != null && playerManager.ownedUnits != null
            ? new HashSet<Unit>(playerManager.ownedUnits.Where(unit => unit != null))
            : new HashSet<Unit>();

        foreach (var unit in unitCandidates.Where(u => u != null).Distinct())
        {
            bool networkRunning = playerManager != null
                && playerManager.Runner != null
                && playerManager.Runner.IsRunning;
            bool hasUnitNetworkId = TryGetUnitNetworkIdRaw(unit, out uint unitIdRaw);
            if (networkRunning && !hasUnitNetworkId)
            {
                RemoveOwnedUnitReference(unit);
                continue;
            }

            if (hasUnitNetworkId && retiredNetworkUnitIds.Contains(unitIdRaw))
            {
                RemoveOwnedUnitReference(unit);
                continue;
            }

            bool wasAlreadyRegistered = existingCellsByUnit.TryGetValue(unit, out Vector3Int registeredCell);
            bool belongsToPlayer = UnitBelongsToFieldOwner(unit) || UnitHasReplicatedFieldOwner(unit) || ownedUnits.Contains(unit);
            if (!belongsToPlayer && playerManager != null && networkRunning)
            {
                continue;
            }

            if (!belongsToPlayer && unit.Owner != null && playerManager != null)
            {
                continue;
            }

            bool inactiveOrDead = !unit.gameObject.activeInHierarchy || unit.IsDead;
            if (inactiveOrDead && !wasAlreadyRegistered && !belongsToPlayer)
            {
                continue;
            }

            candidates++;
            unit.RebindAfterMigration(playerManager, $"FieldManager.{context}", verboseLog);

            if (unit.Data == null)
            {
                missingData++;
            }

            Vector3Int cell = wasAlreadyRegistered ? registeredCell : WorldToGridInt(unit.transform.position);
            if (!IsValidGridPosition(cell))
            {
                outOfBounds++;
                continue;
            }

            if (rebuiltUnits.TryGetValue(cell, out Unit existing))
            {
                bool replace = existing == null || (existing.Data == null && unit.Data != null);
                if (replace)
                {
                    rebuiltUnits[cell] = unit;
                }

                duplicates++;
                continue;
            }

            rebuiltUnits[cell] = unit;
            SyncUnitPlacementIdentity(unit, cell);
            registered++;
        }

        placedUnits = rebuiltUnits;
        pendingUnitPositions.Clear();
        pendingUnitDataByPosition.Clear();
        pendingNetworkMoves.Clear();

        bool unitMapChanged = !oldCells.SetEquals(placedUnits.Keys);
        _lastUnitMapRebuildFrame = Time.frameCount;
        _lastUnitMapRebuildSummary =
            $"ctx={context},units={placedUnits.Count}/{candidates},registered={registered},missingData={missingData},outOfBounds={outOfBounds},duplicates={duplicates},changed={unitMapChanged}";
        summary = _lastUnitMapRebuildSummary;

        if (verboseLog)
        {
            Debug.Log($"[UnitFlow-Migration] RebuildUnitMapAfterMigration {_lastUnitMapRebuildSummary}");
        }

        return true;
    }

    public string BuildWallCellHash()
    {
        RefreshWallMapsFromSceneIfPlaying("BuildWallCellHash");

        var destructible = placedWalls.Keys
            .OrderBy(cell => cell.x)
            .ThenBy(cell => cell.y)
            .ThenBy(cell => cell.z)
            .Select(cell => $"D{cell.x},{cell.y},{cell.z}");
        var permanent = placedPermanentWalls.Keys
            .OrderBy(cell => cell.x)
            .ThenBy(cell => cell.y)
            .ThenBy(cell => cell.z)
            .Select(cell => $"P{cell.x},{cell.y},{cell.z}");
        return string.Join("|", destructible.Concat(permanent));
    }

    public int[] GetPermanentWallFlatPositions()
    {
        var cells = placedPermanentWalls.Keys
            .OrderBy(cell => cell.x)
            .ThenBy(cell => cell.y)
            .ThenBy(cell => cell.z)
            .ToArray();

        int[] flat = new int[cells.Length * 2];
        for (int i = 0; i < cells.Length; i++)
        {
            flat[i * 2] = cells[i].x;
            flat[i * 2 + 1] = cells[i].y;
        }

        return flat;
    }

    private struct FieldUnitMigrationEntry
    {
        public UnitData UnitDataRef;
        public string UnitDataKey;
        public int StarLevel;
        public Vector3Int Position;
    }

    public bool TryGetFieldUnitSnapshot(
        out UnitData[] unitDataRefs,
        out string[] unitDataKeys,
        out int[] starLevels,
        out int[] flatPositions)
    {
        var entries = placedUnits
            .Where(kvp => kvp.Value != null)
            .Where(kvp => IsValidGridPosition(kvp.Key))
            .Where(kvp => IsFieldUnitSnapshotCandidate(kvp.Value))
            .OrderBy(kvp => kvp.Key.x)
            .ThenBy(kvp => kvp.Key.y)
            .ThenBy(kvp => kvp.Key.z)
            .Select(kvp => new FieldUnitMigrationEntry
            {
                UnitDataRef = kvp.Value.Data,
                UnitDataKey = kvp.Value.UnitDataKeyForRoster,
                StarLevel = Mathf.Max(1, kvp.Value.StarLevelForRoster),
                Position = kvp.Key
            })
            .Where(entry => entry.UnitDataRef != null || !string.IsNullOrWhiteSpace(entry.UnitDataKey))
            .ToList();

        unitDataRefs = entries.Select(entry => entry.UnitDataRef).ToArray();
        unitDataKeys = entries.Select(entry => NormalizeMigrationUnitDataKey(entry.UnitDataKey)).ToArray();
        starLevels = entries.Select(entry => entry.StarLevel).ToArray();
        flatPositions = new int[entries.Count * 3];
        for (int i = 0; i < entries.Count; i++)
        {
            flatPositions[(i * 3) + 0] = entries[i].Position.x;
            flatPositions[(i * 3) + 1] = entries[i].Position.y;
            flatPositions[(i * 3) + 2] = entries[i].Position.z;
        }

        return true;
    }

    public bool RestoreFieldUnitsAfterHostMigration(
        UnitData[] unitDataRefs,
        string[] unitDataKeys,
        int[] starLevels,
        int[] flatPositions,
        string context)
    {
        var desiredEntries = BuildFieldUnitMigrationEntries(unitDataRefs, unitDataKeys, starLevels, flatPositions);
        string desiredSignature = BuildFieldUnitMigrationSignature(desiredEntries);
        if (BuildCurrentFieldUnitMigrationSignature() == desiredSignature)
        {
            _hostMigrationUnitRestoreInProgressSignature = string.Empty;
            return true;
        }

        if (_hostMigrationUnitRestoreInProgressSignature == desiredSignature)
        {
            return true;
        }

        _hostMigrationUnitRestoreInProgressSignature = desiredSignature;
        ClearCurrentUnitsForHostMigrationRestore(context);

        int requested = 0;
        int missingData = 0;
        foreach (var entry in desiredEntries)
        {
            UnitData data = ResolveMigrationUnitData(entry.UnitDataRef, entry.UnitDataKey);
            if (data == null)
            {
                missingData++;
                continue;
            }

            CreateUnitAt(data, entry.Position, Mathf.Max(1, entry.StarLevel), false, suppressCombination: true);
            requested++;
        }

        _lastUnitMapRebuildFrame = Time.frameCount;
        _lastUnitMapRebuildSummary =
            $"ctx={context},restoreRequested={requested},missingData={missingData},snapshotUnits={desiredEntries.Count}";
        Debug.Log($"[UnitFlow-Migration] RestoreFieldUnitsAfterHostMigration {_lastUnitMapRebuildSummary}");
        return missingData == 0;
    }

    private bool IsFieldUnitSnapshotCandidate(Unit unit)
    {
        if (unit == null)
        {
            return false;
        }

        return UnitBelongsToFieldOwner(unit) ||
               UnitHasReplicatedFieldOwner(unit) ||
               playerManager != null &&
               playerManager.ownedUnits != null &&
               playerManager.ownedUnits.Contains(unit);
    }

    private List<FieldUnitMigrationEntry> BuildFieldUnitMigrationEntries(
        UnitData[] unitDataRefs,
        string[] unitDataKeys,
        int[] starLevels,
        int[] flatPositions)
    {
        var entries = new List<FieldUnitMigrationEntry>();
        int count = flatPositions != null ? flatPositions.Length / 3 : 0;
        for (int i = 0; i < count; i++)
        {
            var position = new Vector3Int(
                flatPositions[(i * 3) + 0],
                flatPositions[(i * 3) + 1],
                flatPositions[(i * 3) + 2]);
            if (!IsValidGridPosition(position))
            {
                continue;
            }

            string key = unitDataKeys != null && i < unitDataKeys.Length
                ? NormalizeMigrationUnitDataKey(unitDataKeys[i])
                : string.Empty;
            UnitData dataRef = unitDataRefs != null && i < unitDataRefs.Length ? unitDataRefs[i] : null;
            if (dataRef != null && string.IsNullOrWhiteSpace(key))
            {
                key = NormalizeMigrationUnitDataKey(dataRef.name);
            }

            if (dataRef == null && string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            entries.Add(new FieldUnitMigrationEntry
            {
                UnitDataRef = dataRef,
                UnitDataKey = key,
                StarLevel = starLevels != null && i < starLevels.Length ? Mathf.Max(1, starLevels[i]) : 1,
                Position = position
            });
        }

        return entries
            .OrderBy(entry => entry.Position.x)
            .ThenBy(entry => entry.Position.y)
            .ThenBy(entry => entry.Position.z)
            .ToList();
    }

    private string BuildCurrentFieldUnitMigrationSignature()
    {
        var entries = placedUnits
            .Where(kvp => kvp.Value != null)
            .Where(kvp => IsValidGridPosition(kvp.Key))
            .Where(kvp => IsFieldUnitSnapshotCandidate(kvp.Value))
            .Select(kvp => new FieldUnitMigrationEntry
            {
                UnitDataRef = kvp.Value.Data,
                UnitDataKey = kvp.Value.UnitDataKeyForRoster,
                StarLevel = Mathf.Max(1, kvp.Value.StarLevelForRoster),
                Position = kvp.Key
            })
            .OrderBy(entry => entry.Position.x)
            .ThenBy(entry => entry.Position.y)
            .ThenBy(entry => entry.Position.z)
            .ToList();
        return BuildFieldUnitMigrationSignature(entries);
    }

    private static string BuildFieldUnitMigrationSignature(List<FieldUnitMigrationEntry> entries)
    {
        if (entries == null || entries.Count == 0)
        {
            return "empty";
        }

        return string.Join("|", entries.Select(entry =>
            $"{entry.Position.x},{entry.Position.y},{entry.Position.z}:{NormalizeMigrationUnitDataKey(entry.UnitDataKey)}:star={Mathf.Max(1, entry.StarLevel)}"));
    }

    private void ClearCurrentUnitsForHostMigrationRestore(string context)
    {
        var unitsToRemove = new HashSet<Unit>();
        foreach (var unit in placedUnits.Values)
        {
            if (unit != null)
            {
                unitsToRemove.Add(unit);
            }
        }

        if (playerManager != null && playerManager.ownedUnits != null)
        {
            foreach (var unit in playerManager.ownedUnits)
            {
                if (unit != null && IsFieldUnitSnapshotCandidate(unit))
                {
                    unitsToRemove.Add(unit);
                }
            }
        }

        if (unitParent != null)
        {
            foreach (var unit in unitParent.GetComponentsInChildren<Unit>(true))
            {
                if (unit != null)
                {
                    unitsToRemove.Add(unit);
                }
            }
        }

        placedUnits.Clear();
        pendingUnitPositions.Clear();
        pendingUnitDataByPosition.Clear();
        pendingNetworkMoves.Clear();

        foreach (var unit in unitsToRemove)
        {
            RemoveOwnedUnitReference(unit);
            DespawnOrDestroyUnitForMigrationRestore(unit, context);
        }
    }

    private void DespawnOrDestroyUnitForMigrationRestore(Unit unit, string context)
    {
        if (unit == null || unit.gameObject == null)
        {
            return;
        }

        var runner = playerManager != null ? playerManager.Runner : null;
        if (runner != null
            && runner.IsRunning
            && runner.IsServer
            && unit.TryGetComponent<NetworkObject>(out var networkObject)
            && networkObject != null
            && networkObject.IsValid)
        {
            try
            {
                retiredNetworkUnitIds.Add(networkObject.Id.Raw);
                runner.Despawn(networkObject);
                return;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FieldManager] Restore unit despawn failed. context={context}, unit={unit.name}, error={ex.GetType().Name}");
            }
        }

        unit.gameObject.SetActive(false);
        Destroy(unit.gameObject);
    }

    private static UnitData ResolveMigrationUnitData(UnitData dataRef, string unitDataKey)
    {
        if (dataRef != null)
        {
            return dataRef;
        }

        string key = NormalizeMigrationUnitDataKey(unitDataKey);
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        var loadManager = LoadManager.Instance;
        if (loadManager == null || !loadManager.IsReady)
        {
            return null;
        }

        UnitData resolved = loadManager.GetUnitData(key);
        if (resolved != null)
        {
            return resolved;
        }

        string stripped = RemoveMigrationUnitDataPrefix(key);
        resolved = loadManager.GetUnitData(stripped);
        if (resolved != null)
        {
            return resolved;
        }

        var all = loadManager.GetAllUnitData();
        if (all == null)
        {
            return null;
        }

        return all.FirstOrDefault(data =>
            data != null &&
            (string.Equals(NormalizeMigrationUnitDataKey(data.name), key, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(NormalizeMigrationUnitDataKey(data.unitName), key, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(NormalizeMigrationUnitDataKey(data.name), stripped, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(NormalizeMigrationUnitDataKey(data.unitName), stripped, StringComparison.OrdinalIgnoreCase)));
    }

    private static string NormalizeMigrationUnitDataKey(string key)
    {
        return string.IsNullOrWhiteSpace(key) ? string.Empty : key.Replace("(Clone)", string.Empty).Trim();
    }

    private static string RemoveMigrationUnitDataPrefix(string value)
    {
        const string prefix = "UnitData_";
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        return value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? value.Substring(prefix.Length)
            : value;
    }

    private bool IsWorldPositionInsideOwnedGrid(Vector3 worldPos)
    {
        float epsilon = Mathf.Max(1e-3f, cellSize * 0.1f);
        float minX = gridOrigin.x - epsilon;
        float minZ = gridOrigin.z - epsilon;
        float maxX = gridOrigin.x + (gridSize.x * cellSize) + epsilon;
        float maxZ = gridOrigin.z + (gridSize.y * cellSize) + epsilon;
        return worldPos.x >= minX && worldPos.x <= maxX && worldPos.z >= minZ && worldPos.z <= maxZ;
    }

    private bool IsLikelyPermanentWallObject(GameObject obj)
    {
        if (obj == null)
        {
            return false;
        }

        if (obj.GetComponent<DestructibleWall>() != null)
        {
            return false;
        }

        if (obj.GetComponent<Unit>() != null)
        {
            return false;
        }

        if (obj.name.IndexOf("PermanentWall", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        // 이름 기반 필터가 실패할 때를 대비한 보수적 폴백
        return wallLayer >= 0
               && obj.layer == wallLayer
               && obj.GetComponent<NetworkObject>() != null
               && obj.GetComponent<BoxCollider>() != null
               && obj.GetComponent<MeshRenderer>() != null;
    }

    private void UpdateWallYOffsetFromPrefab()
    {
        if (destructibleWallPrefab != null)
        {
            wallYOffset = GetWallPrefabHeight();
        }
    }

    private float GetWallPrefabHeight()
    {
        return destructibleWallPrefab.transform.localScale.y;
    }

    private float GetPrefabHeight(GameObject prefab)
    {
        return prefab != null ? prefab.transform.localScale.y : 1f;
    }



    // 영구(파괴 불가) 벽 생성 - 테두리 + 필드 내부 랜덤
    private async void GeneratePermanentWallsIfNeeded()
    {
        bool migrationInProgress = HostMigrationHandler.Instance != null && HostMigrationHandler.Instance.IsMigrating;
        var currentRunner = playerManager != null ? playerManager.Runner : null;
        bool clientPeerWaitingForServer = currentRunner != null && currentRunner.IsRunning && !currentRunner.IsServer;
        bool adoptedExistingWalls = false;
        int existingPermanentWalls = placedPermanentWalls.Count;

        if (migrationInProgress || !clientPeerWaitingForServer)
        {
            adoptedExistingWalls = TryAdoptExistingPermanentWallsBeforeGeneration(
                "GeneratePermanentWallsIfNeeded.Precheck",
                out existingPermanentWalls);
        }

        Debug.Log($"[WallFlow-Auto] GeneratePermanentWallsIfNeeded ENTER owner={BuildWallOwnerTag()}, generated={permanentWallsGenerated}, existingPermanentWalls={existingPermanentWalls}, migrationInProgress={migrationInProgress}, {BuildRunnerTag()}, initialPermanentWallCount={initialPermanentWallCount}");

        // Host Migration 복원 경로에서는 랜덤 생성을 금지합니다.
        // 기존 영구벽(복원/동기화 결과)이 있으면 그것을 채택하고, 없으면 동기화 도착을 기다립니다.
        if (migrationInProgress)
        {
            if (adoptedExistingWalls)
            {
                Debug.Log($"[WallFlow-Auto] skip: migration in progress and existing permanent walls adopted. owner={BuildWallOwnerTag()}, count={existingPermanentWalls}");
            }
            else
            {
                Debug.Log($"[WallFlow-Auto] skip: migration in progress. wait for restored/synced permanent walls. owner={BuildWallOwnerTag()}");
            }
            return;
        }

        if (clientPeerWaitingForServer)
        {
            Debug.Log($"[WallFlow-Auto] skip: client peer waits for server sync. owner={BuildWallOwnerTag()}, runner={currentRunner.name}");
            return;
        }

        if (permanentWallsGenerated)
        {
            Debug.Log($"[WallFlow-Auto] skip: already generated. owner={BuildWallOwnerTag()}");
            return;
        }
        if (playerManager == null)
        {
            Debug.LogWarning("[WallFlow-Auto] skip: playerManager is null (Initialize not ready).");
            return; // Initialize 미완료
        }

        int ownerId = playerManager.playerId;
        if (ownerId < 0)
        {
            Debug.Log($"[WallFlow-Auto] skip: unresolved playerId ({ownerId}). owner={BuildWallOwnerTag()}");
            return;
        }

        // 네트워크 환경에서는 서버(호스트)만 초기 랜덤 생성 수행
        var runner = playerManager != null ? playerManager.Runner : null;
        if (runner != null && runner.IsRunning && !runner.IsServer)
        {
            Debug.Log($"[WallFlow-Auto] skip: client peer waits for server sync. owner={BuildWallOwnerTag()}, runner={runner.name}");
            // 클라이언트는 서버의 RPC를 통해 동기화 대기
            return;
        }

        Debug.Log($"[WallFlow-Auto] server path confirmed. owner={BuildWallOwnerTag()}, runner={(runner != null ? runner.name : "offline")}");

        // 프리팹 확보 (Inspector 우선, 없으면 Addressables)
        GameObject prefab = permanentWallPrefab;
        if (prefab == null && !string.IsNullOrEmpty(permanentWallAddressKey))
        {
            prefab = await AssetLoader.LoadAssetAsync<GameObject>(permanentWallAddressKey);
            Debug.Log($"[WallFlow-Auto] prefab loaded from addressables. key={permanentWallAddressKey}, success={prefab != null}");
        }

        if (prefab == null)
        {
            Debug.LogError($"[WallFlow-Auto] abort: permanent wall prefab unavailable. key={permanentWallAddressKey}");
            // Debug.LogError($"[FieldManager] Permanent wall prefab not set and failed to load '{permanentWallAddressKey}'. Skipping generation.");
            permanentWallsGenerated = true;
            return;
        }

        // 스폰/골 목표 셀 계산 (3D)
        
        // goalTransform이 null이면 필드 중앙 사용
        Vector3Int goalCell;
        if (playerManager.goalTransform != null)
        {
            goalCell = WorldToGridInt(playerManager.goalTransform.position);
        }
        else
        {
            // 폴백: 필드 중앙
            goalCell = new Vector3Int(gridSize.x / 2, gridSize.y / 2, 0);
            // Debug.LogWarning($"[FieldManager] goalTransform이 null입니다. 필드 중앙 {goalCell}을 사용합니다.");
        }

        Debug.Log($"[WallFlow-Auto] goal resolved. owner={BuildWallOwnerTag()}, goalCell={goalCell}, gridSize={gridSize}");

        List<Vector3Int> selected = new List<Vector3Int>();
        int centerX = gridSize.x / 2;
        int centerY = gridSize.y / 2;

        // === 1. 테두리 벽 생성 (동서남북 가운데는 뚫려있음) ===
        for (int x = 0; x < gridSize.x; x++)
        {
            for (int y = 0; y < gridSize.y; y++)
            {
                bool isLeftEdge = (x == 0);
                bool isRightEdge = (x == gridSize.x - 1);
                bool isBottomEdge = (y == 0);
                bool isTopEdge = (y == gridSize.y - 1);

                if (!isLeftEdge && !isRightEdge && !isBottomEdge && !isTopEdge)
                    continue; // 테두리가 아니면 스킵

                // 동서남북 가운데 뚫린 부분 확인 (스폰 포인트 입구)
                bool isNorthGap = isTopEdge && (x == centerX);
                bool isSouthGap = isBottomEdge && (x == centerX);
                bool isEastGap = isRightEdge && (y == centerY);
                bool isWestGap = isLeftEdge && (y == centerY);

                if (isNorthGap || isSouthGap || isEastGap || isWestGap)
                    continue; // 동서남북 가운데는 뚫려있음 (몬스터 입구)

                var cell = new Vector3Int(x, y, 0);
                if (!IsValidGridPosition(cell)) continue;
                // 테두리는 spawnCell/goalCell 체크 없이 무조건 생성 (gap으로 이미 처리됨)
                if (HasWallAt(cell)) continue;
                if (IsUnitAt(cell)) continue;

                selected.Add(cell);
                CreatePermanentWallAt(cell, prefab);
            }
        }

        // === 2. 필드 내부 랜덤 고정벽 생성 ===
        if (initialPermanentWallCount > 0)
        {
            // 골 주변 상하좌우 4칸 (진입 경로로 반드시 1개 이상 열려있어야 함)
            Vector3Int[] goalAdjacentCells = new Vector3Int[]
            {
                new Vector3Int(goalCell.x - 1, goalCell.y, 0),
                new Vector3Int(goalCell.x + 1, goalCell.y, 0),
                new Vector3Int(goalCell.x, goalCell.y - 1, 0),
                new Vector3Int(goalCell.x, goalCell.y + 1, 0)
            };
            HashSet<Vector3Int> goalAdjacentSet = new HashSet<Vector3Int>(goalAdjacentCells);

            // 배치 가능한 내부 셀 수집 (테두리 + 바깥 한 겹 제외)
            List<Vector3Int> interiorCandidates = new List<Vector3Int>();
            for (int x = 2; x < gridSize.x - 2; x++)
            {
                for (int y = 2; y < gridSize.y - 2; y++)
                {
                    var cell = new Vector3Int(x, y, 0);
                    if (!IsValidGridPosition(cell)) continue;
                    // 스폰과 골 위치는 절대 배치 불가
                    if (cell == goalCell) continue;
                    if (HasWallAt(cell)) continue;
                    if (IsUnitAt(cell)) continue;
                    interiorCandidates.Add(cell);
                }
            }

            // 랜덤 셔플
            ShuffleList(interiorCandidates);

            // 벽 배치 (상하좌우 완전 차단 방지 로직 포함)
            int placedCount = 0;
            foreach (var cell in interiorCandidates)
            {
                if (placedCount >= initialPermanentWallCount) break;

                // 이 셀이 골 인접 4칸 중 하나라면, 배치 후 남은 열린 인접 칸이 1개 이상인지 확인
                if (goalAdjacentSet.Contains(cell))
                {
                    // 현재 열린 인접 칸 수 계산 (이미 selected에 추가된 것도 벽으로 간주)
                    int openAdjacentCount = 0;
                    foreach (var adj in goalAdjacentCells)
                    {
                        if (!IsValidGridPosition(adj)) continue;
                        bool isBlocked = HasWallAt(adj) || selected.Contains(adj) || adj == cell;
                        if (!isBlocked) openAdjacentCount++;
                    }

                    // 배치하면 열린 인접 칸이 0개가 되는 경우 스킵
                    if (openAdjacentCount < 1)
                    {
                        // Debug.Log($"[FieldManager] 골 인접 셀 {cell} 스킵 - 완전 차단 방지");
                        continue;
                    }
                }

                selected.Add(cell);
                CreatePermanentWallAt(cell, prefab);
                placedCount++;
            }

            // Debug.Log($"[FieldManager] 필드 내부 랜덤 고정벽 {placedCount}개 생성 완료");
        }

        // 네트워크 게임이라면, 선택된 좌표를 클라이언트에 브로드캐스트합니다.
        // 영구벽을 NetworkObject로 운용할 때는 생성이 아니라 "복원/검증 힌트"로 사용됩니다.
        if (runner != null && runner.IsRunning && runner.IsServer && playerManager != null && selected.Count > 0)
        {
            int[] flat = new int[selected.Count * 2];
            for (int i = 0; i < selected.Count; i++)
            {
                flat[i * 2] = selected[i].x;
                flat[i * 2 + 1] = selected[i].y;
            }
            SetAuthoritativePermanentWallCells(selected);
            playerManager.RPC_ApplyPermanentWalls(flat);
            Debug.Log($"[WallFlow-Auto] broadcast permanent walls to clients. owner={BuildWallOwnerTag()}, count={selected.Count}");
        }

        permanentWallsGenerated = true;
        Debug.Log($"[WallFlow-Auto] GeneratePermanentWallsIfNeeded SUCCESS owner={BuildWallOwnerTag()}, totalSelected={selected.Count}, permanentWalls={placedPermanentWalls.Count}");
    }

    private bool TryAdoptExistingPermanentWallsBeforeGeneration(string context, out int existingPermanentWalls)
    {
        RebuildWallMapsAfterMigration($"FieldManager.{context}", false, out _);
        existingPermanentWalls = placedPermanentWalls.Count;
        if (existingPermanentWalls <= 0)
        {
            return false;
        }

        if (!permanentWallsGenerated)
        {
            permanentWallsGenerated = true;
            Debug.Log($"[WallFlow-Migration] adopt existing permanent walls. owner={BuildWallOwnerTag()}, count={existingPermanentWalls}, ctx={context}");
        }

        return true;
    }

    /// <summary>
    /// 리스트를 랜덤하게 섞습니다.
    /// </summary>
    private void ShuffleList<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = UnityEngine.Random.Range(0, i + 1);
            T temp = list[i];
            list[i] = list[j];
            list[j] = temp;
        }
    }

    /// <summary>
    /// 서버가 선택한 영구 벽 좌표 목록을 받아 클라이언트에 동기화합니다.
    /// </summary>
    public async void ApplyPermanentWallsFromServer(int[] flatPositions)
    {
        if (flatPositions == null || flatPositions.Length == 0) return;

        var requestedCells = new List<Vector3Int>(flatPositions.Length / 2);
        int count = flatPositions.Length / 2;
        for (int i = 0; i < count; i++)
        {
            var pos = new Vector3Int(flatPositions[i * 2], flatPositions[i * 2 + 1], 0);
            if (!IsValidGridPosition(pos))
            {
                continue;
            }
            requestedCells.Add(pos);
        }

        if (requestedCells.Count == 0)
        {
            return;
        }

        SetAuthoritativePermanentWallCells(requestedCells);

        // 프리팹 확보 (Inspector 우선, 없으면 Addressables)
        GameObject prefab = permanentWallPrefab;
        if (prefab == null && !string.IsNullOrEmpty(permanentWallAddressKey))
        {
            prefab = await AssetLoader.LoadAssetAsync<GameObject>(permanentWallAddressKey);
        }

        if (prefab == null)
        {
            // Debug.LogError($"[FieldManager] Permanent wall prefab not available on client for ApplyPermanentWallsFromServer. Key='{permanentWallAddressKey}'");
            return;
        }

        var runner = playerManager != null ? playerManager.Runner : null;
        bool usesNetworkPermanentWall = runner != null
                                        && runner.IsRunning
                                        && prefab.TryGetComponent<NetworkObject>(out _);

        if (usesNetworkPermanentWall)
        {
            // 영구벽을 NetworkObject로 운용하는 경우, RPC는 "셀 목록 힌트"로만 사용합니다.
            // 실제 오브젝트 생성은 서버 Spawn 결과를 기다리고, 클라이언트는 재구성 루틴으로 딕셔너리를 복원합니다.
            if (_awaitNetworkPermanentWallsCoroutine != null)
            {
                StopCoroutine(_awaitNetworkPermanentWallsCoroutine);
                _awaitNetworkPermanentWallsCoroutine = null;
            }

            _awaitNetworkPermanentWallsCoroutine = StartCoroutine(
                WaitForNetworkPermanentWallsAndRebuildCoroutine(requestedCells, prefab));
            return;
        }

        foreach (var pos in requestedCells)
        {
            if (HasWallAt(pos)) continue;
            CreatePermanentWallAt(pos, prefab);
        }

        permanentWallsGenerated = true;
    }

    public void RestorePermanentWallsAfterHostMigration(int[] flatPositions, string context)
    {
        if (flatPositions == null || flatPositions.Length == 0)
        {
            return;
        }

        var requestedCells = new List<Vector3Int>(flatPositions.Length / 2);
        int count = flatPositions.Length / 2;
        for (int i = 0; i < count; i++)
        {
            var pos = new Vector3Int(flatPositions[i * 2], flatPositions[i * 2 + 1], 0);
            if (IsValidGridPosition(pos) && !requestedCells.Contains(pos))
            {
                requestedCells.Add(pos);
            }
        }

        if (requestedCells.Count == 0)
        {
            return;
        }

        SetAuthoritativePermanentWallCells(requestedCells);
        RebuildWallMapsAfterMigration($"FieldManager.RestorePermanentWallsAfterHostMigration.Pre.{context}", false, out _);

        GameObject prefab = permanentWallPrefab;
        if (prefab == null)
        {
            Debug.LogWarning($"[WallFlow-Migration] permanent wall restore skipped: prefab unavailable. owner={BuildWallOwnerTag()}, context={context}, requested={requestedCells.Count}");
            return;
        }

        int created = 0;
        foreach (var pos in requestedCells)
        {
            if (placedPermanentWalls.ContainsKey(pos))
            {
                continue;
            }

            if (HasWallAt(pos))
            {
                continue;
            }

            int beforeCount = placedPermanentWalls.Count;
            CreatePermanentWallAt(pos, prefab);
            if (placedPermanentWalls.Count > beforeCount)
            {
                created++;
            }
        }

        permanentWallsGenerated = true;
        RebuildWallMapsAfterMigration($"FieldManager.RestorePermanentWallsAfterHostMigration.Post.{context}", false, out string summary, true);
        Debug.Log($"[WallFlow-Migration] durable permanent wall restore complete. owner={BuildWallOwnerTag()}, context={context}, requested={requestedCells.Count}, created={created}, summary={summary}");
    }

    private System.Collections.IEnumerator WaitForNetworkPermanentWallsAndRebuildCoroutine(List<Vector3Int> expectedCells, GameObject fallbackPrefab)
    {
        const float timeout = 5f;
        float waited = 0f;
        int expectedCount = expectedCells != null ? expectedCells.Count : 0;

        while (waited < timeout)
        {
            RebuildWallMapsAfterMigration("FieldManager.ApplyPermanentWallsFromServer.NetworkSync", false, out _);

            int matched = 0;
            if (expectedCells != null)
            {
                foreach (var cell in expectedCells)
                {
                    if (placedPermanentWalls.ContainsKey(cell))
                    {
                        matched++;
                    }
                }
            }

            if (expectedCount == 0 || matched >= expectedCount)
            {
                permanentWallsGenerated = true;
                _awaitNetworkPermanentWallsCoroutine = null;
                yield break;
            }

            yield return new WaitForSeconds(0.1f);
            waited += 0.1f;
        }

        RebuildWallMapsAfterMigration("FieldManager.ApplyPermanentWallsFromServer.NetworkSyncTimeout", true, out string summary);

        int finalMatched = 0;
        if (expectedCells != null)
        {
            foreach (var cell in expectedCells)
            {
                if (placedPermanentWalls.ContainsKey(cell))
                {
                    finalMatched++;
                }
            }
        }

        if (expectedCount > 0 && finalMatched < expectedCount)
        {
            Debug.LogWarning($"[WallFlow-Migration] network permanent wall sync timeout. owner={BuildWallOwnerTag()}, matched={finalMatched}/{expectedCount}, summary={summary}");
            TryCreateClientPermanentWallFallbacks(expectedCells, fallbackPrefab, "NetworkSyncTimeout");
        }

        permanentWallsGenerated = permanentWallsGenerated || placedPermanentWalls.Count > 0;
        _awaitNetworkPermanentWallsCoroutine = null;
    }

    private void TryCreateClientPermanentWallFallbacks(List<Vector3Int> expectedCells, GameObject fallbackPrefab, string context)
    {
        var runner = playerManager != null ? playerManager.Runner : null;
        if (runner == null || !runner.IsRunning || runner.IsServer || fallbackPrefab == null || expectedCells == null)
        {
            return;
        }

        int created = 0;
        foreach (var cell in expectedCells)
        {
            if (placedPermanentWalls.ContainsKey(cell) || HasWallAt(cell))
            {
                continue;
            }

            int before = placedPermanentWalls.Count;
            CreateLocalPermanentWallAt(cell, fallbackPrefab);
            if (placedPermanentWalls.Count > before)
            {
                created++;
            }
        }

        if (created <= 0)
        {
            return;
        }

        permanentWallsGenerated = true;
        RebuildWallMapsAfterMigration($"FieldManager.ClientPermanentWallFallback.{context}", true, out string fallbackSummary, true);
        Debug.LogWarning($"[WallFlow-Migration] client permanent wall fallback created. owner={BuildWallOwnerTag()}, created={created}, expected={expectedCells.Count}, summary={fallbackSummary}");
    }

    private void CreatePermanentWallAt(Vector3Int gridPosition, GameObject prefab)
    {
        if (!IsValidGridPosition(gridPosition)) return;
        if (HasWallAt(gridPosition)) return;
        if (prefab == null) return;

        Vector3 worldPos = GridToWorld(gridPosition);
        float halfH = GetPrefabHeight(prefab) * 0.5f;
        worldPos.y += halfH;

        GameObject wallGO = null;
        var runner = playerManager != null ? playerManager.Runner : null;
        if (runner != null
            && runner.IsRunning
            && prefab.TryGetComponent<NetworkObject>(out var networkPrefab))
        {
            if (playerManager == null || playerManager.Object == null || !playerManager.Object.HasStateAuthority)
            {
                Debug.LogWarning($"[WallFlow-Auto] skip network permanent wall spawn without state authority. owner={BuildWallOwnerTag()}, pos={gridPosition}");
                return;
            }

            var spawned = runner.Spawn(networkPrefab, worldPos, Quaternion.identity, playerManager.Object.InputAuthority);
            if (spawned == null)
            {
                Debug.LogError($"[WallFlow-Auto] abort: Runner.Spawn failed for permanent wall. owner={BuildWallOwnerTag()}, pos={gridPosition}");
                return;
            }

            wallGO = spawned.gameObject;
            if (wallParent != null)
            {
                wallGO.transform.SetParent(wallParent, true);
            }
        }
        else
        {
            wallGO = Instantiate(prefab, worldPos, Quaternion.identity, wallParent);
        }

        // 레이어 지정 (자식 포함)
        if (wallLayer >= 0) SetLayerRecursively(wallGO, wallLayer);

        placedPermanentWalls[gridPosition] = wallGO;

        // 벽 위에 원거리 유닛이 있었다면 올려놓기
        Unit unitOnCell = GetUnitAt(gridPosition);
        if (unitOnCell != null && unitOnCell.Data.unitType == UnitType.Ranged)
        {
            Vector3 atopPos = GridToWorld(gridPosition, checkForWall: true);
            MoveUnitImmediate(unitOnCell, atopPos);
        }
    }

    private void CreateLocalPermanentWallAt(Vector3Int gridPosition, GameObject prefab)
    {
        if (!IsValidGridPosition(gridPosition)) return;
        if (HasWallAt(gridPosition)) return;
        if (prefab == null) return;

        Vector3 worldPos = GridToWorld(gridPosition);
        float halfH = GetPrefabHeight(prefab) * 0.5f;
        worldPos.y += halfH;

        GameObject wallGO = Instantiate(prefab, worldPos, Quaternion.identity, wallParent);
        foreach (var networkObject in wallGO.GetComponentsInChildren<NetworkObject>(true))
        {
            Destroy(networkObject);
        }

        if (wallLayer >= 0) SetLayerRecursively(wallGO, wallLayer);
        placedPermanentWalls[gridPosition] = wallGO;

        Unit unitOnCell = GetUnitAt(gridPosition);
        if (unitOnCell != null && unitOnCell.Data.unitType == UnitType.Ranged)
        {
            Vector3 atopPos = GridToWorld(gridPosition, checkForWall: true);
            MoveUnitImmediate(unitOnCell, atopPos);
        }
    }

    private void SetLayerRecursively(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform child in go.transform)
        {
            if (child != null) SetLayerRecursively(child.gameObject, layer);
        }
    }

    private bool IsNetworkReadyAndHasInputAuthority()
    {
        var gmInst = GameManagers.Instance;
        if (gmInst != null && gmInst.Runner != null && gmInst.Runner.IsRunning)
        {
            if (playerManager != null &&
                playerManager.Object != null &&
                playerManager.Object.IsValid &&
                playerManager.Object.HasInputAuthority)
            {
                return true;
            }

            var lp = gmInst.localPlayer;
            if (lp != null &&
                lp.Object != null &&
                lp.Object.IsValid &&
                lp.Object.HasInputAuthority)
            {
                return true;
            }

            if (Time.realtimeSinceStartup - _lastInteractionGateBlockLogRealtime > 1f)
            {
                _lastInteractionGateBlockLogRealtime = Time.realtimeSinceStartup;
                Debug.Log($"[HM-INPUT-GATE] command blocked owner={BuildWallOwnerTag()} reason=networkInputAuthorityMissing");
            }

            return false;
        }
        return true;
    }

    private void SnapbackSelectedUnit(Vector3 targetWorldPos)
    {
        if (selectedUnitNetworkTransform != null)
        {
            selectedUnitNetworkTransform.enabled = true;
            var networkObject = selectedUnit.GetComponent<Fusion.NetworkObject>();
            if (networkObject != null && networkObject.HasStateAuthority)
            {
                selectedUnitNetworkTransform.Teleport(targetWorldPos, selectedUnit.transform.rotation);
            }
            else
            {
                selectedUnit.transform.position = targetWorldPos;
            }
        }
        else
        {
            selectedUnit.transform.position = targetWorldPos;
        }
    }

    private void AttachStatusBar(GameObject host, Action<StatusBarUI> setter)
    {
        if (statusBarPrefab != null)
        {
            GameObject statusBarGO = Instantiate(statusBarPrefab, host.transform);
            var statusBarUI = statusBarGO.GetComponent<StatusBarUI>();
            if (statusBarUI != null)
            {
                setter?.Invoke(statusBarUI);
            }
        }
    }

    private void MoveUnitImmediate(Unit unit, Vector3 targetWorldPos)
    {
        var networkTransform = unit.GetComponent<Fusion.NetworkTransform>();
        if (networkTransform != null)
        {
            if (!networkTransform.enabled)
            {
                networkTransform.enabled = true;
            }

            var networkObject = unit.GetComponent<Fusion.NetworkObject>();
            if (networkObject != null && networkObject.HasStateAuthority)
            {
                networkTransform.Teleport(targetWorldPos, unit.transform.rotation);
            }
            else
            {
                unit.transform.position = targetWorldPos;
            }
        }
        else
        {
            unit.transform.position = targetWorldPos;
        }
    }

    #endregion

    #region 유닛 생성 및 관리

    public Unit GetUnitAt(Vector3Int gridPosition)
    {
        placedUnits.TryGetValue(gridPosition, out Unit unit);
        return unit;
    }

    private List<Vector3Int> GetPlacedCellsForUnit(Unit unit)
    {
        if (unit == null)
        {
            return new List<Vector3Int>();
        }

        return placedUnits
            .Where(kvp => kvp.Value == unit)
            .Select(kvp => kvp.Key)
            .ToList();
    }

    private int RemovePlacedUnitEntries(Unit unit)
    {
        var cells = GetPlacedCellsForUnit(unit);
        foreach (var cell in cells)
        {
            placedUnits.Remove(cell);
            pendingUnitPositions.Remove(cell);
            pendingUnitDataByPosition.Remove(cell);
        }

        return cells.Count;
    }

    private void RemoveOwnedUnitReference(Unit unit)
    {
        if (unit == null || playerManager == null || playerManager.ownedUnits == null)
        {
            return;
        }

        playerManager.ownedUnits.RemoveAll(owned => owned == null || owned == unit);
    }

    public bool IsUnitAt(Vector3Int gridPosition)
    {
        // 유닛을 드래그하는 중이고, 그 유닛의 원래 위치를 확인하는 경우
        // 배치 목적상 해당 위치는 비어있는 것으로 간주합니다.
        // 이렇게 하면 유닛을 원래 위치에 다시 놓을 수 있습니다.
        if (selectedUnit != null && originalUnitPosition == gridPosition)
        {
            return false;
        }

        if (placedUnits.TryGetValue(gridPosition, out Unit unit))
        {
            // NetworkObject가 Despawn/Destroy 된 경우 Dictionary에 null 레퍼런스가 남을 수 있어 정리합니다.
            if (unit == null)
            {
                placedUnits.Remove(gridPosition);
            }
            else
            {
                return true;
            }
        }

        return pendingUnitPositions.Contains(gridPosition);
    }

    public bool HasPendingUnitAt(Vector3Int gridPosition)
    {
        return pendingUnitPositions.Contains(gridPosition);
    }

    public bool TryGetPendingUnitDataAt(Vector3Int gridPosition, out UnitData unitData)
    {
        return pendingUnitDataByPosition.TryGetValue(gridPosition, out unitData);
    }

    public List<PendingUnitPlacement> GetPendingUnitPlacements()
    {
        return pendingUnitDataByPosition
            .Where(kvp => pendingUnitPositions.Contains(kvp.Key) && kvp.Value != null)
            .Select(kvp => new PendingUnitPlacement
            {
                Position = kvp.Key,
                UnitData = kvp.Value
            })
            .ToList();
    }

    public bool HasPendingNetworkMoveFrom(Vector3Int from)
    {
        return pendingNetworkMoves.Any(move => move.From == from);
    }

    private bool TryReserveUnitPosition(Vector3Int gridPosition, UnitData unitData)
    {
        if (!pendingUnitPositions.Add(gridPosition))
        {
            return false;
        }

        if (unitData != null)
        {
            pendingUnitDataByPosition[gridPosition] = unitData;
        }

        return true;
    }

    private void ReleaseReservedUnitPosition(Vector3Int gridPosition)
    {
        pendingUnitPositions.Remove(gridPosition);
        pendingUnitDataByPosition.Remove(gridPosition);
    }

    public void CreateAndPlaceUnitOnField(UnitData unitData, int starLevel)
    {
        // AI 플레이어인지 ComponentRegistry를 통해 확인합니다. AIPlayerController가 자신의 ID로 등록한다고 가정합니다.
        if (ComponentRegistry.Has<AIPlayerController>(playerManager.playerId.ToString()))
        {
            CreateAndPlaceUnitOnFieldForAI(unitData, starLevel);
            return; // AI 로직을 수행했으면 여기서 종료
        }

        Vector3Int? emptySlot = FindFirstEmptySlot(unitData);
        if (emptySlot.HasValue)
        {
            CreateUnitAt(unitData, emptySlot.Value, starLevel);
        }
        else
        {
            // Debug.LogWarning("[FieldManager] 필드에 빈 공간이 없어 유닛을 배치할 수 없습니다! 골드를 환불합니다.");
            int refundCost = (starLevel == 2) ? unitData.cost * 4 : unitData.cost;
            playerManager.AddGold(refundCost);
        }
    }

    /// <summary>
    /// [AI용] 유닛 타입에 따라 첫 번째 빈 공간에 유닛을 생성하고 배치합니다. (초기 'Dumb' 배치)
    /// </summary>
    public void CreateAndPlaceUnitOnFieldForAI(UnitData unitData, int starLevel)
    {
        Vector3Int? placementPos = FindFirstEmptySlot(unitData);

        if (placementPos.HasValue)
        {
            CreateUnitAt(unitData, placementPos.Value, starLevel, true);
        }
        else
        {
            // Debug.LogWarning($"[FieldManager (AI)] {unitData.unitName}을(를) 배치할 유효한 위치를 찾지 못했습니다. 골드를 환불합니다.");
            int refundCost = (starLevel == 2) ? unitData.cost * 4 : unitData.cost;
            playerManager.AddGold(refundCost);
        }
    }

     public async void CreateUnitAt(UnitData data, Vector3Int gridPosition, int starLevel, bool markAsAIPurchased = false, bool suppressCombination = false)
    {
        if (!IsValidGridPosition(gridPosition))
        {
            // Debug.LogWarning($"[FieldManager] CreateUnitAt 무시: 유효 범위 밖 위치 {gridPosition} (GridSize={gridSize})");
            return;
        }
        if (data == null)
        {
            // Debug.LogError("[FieldManager] CreateUnitAt 실패: UnitData가 null입니다.");
            return;
        }
        if (IsUnitAt(gridPosition))
        {
            // Debug.LogWarning($"[FieldManager] CreateUnitAt 무시: 해당 위치에 이미 유닛이 존재합니다. pos={gridPosition}");
            return;
        }
        if (data.prefabsByStarLevel == null || data.prefabsByStarLevel.Length == 0)
        {
            // Debug.LogError($"[FieldManager] CreateUnitAt 실패: UnitData '{data.unitName}'의 prefabsByStarLevel이 비어있습니다.");
            return;
        }
        if (starLevel < 1 || starLevel > data.prefabsByStarLevel.Length)
        {
            // Debug.LogError($"[FieldManager] CreateUnitAt 실패: 잘못된 성급({starLevel}). 허용 범위: 1~{data.prefabsByStarLevel.Length}");
            return;
        }
        string prefabKey = data.prefabsByStarLevel[starLevel - 1];
        if (string.IsNullOrEmpty(prefabKey))
        {
            // Debug.LogError($"[FieldManager] CreateUnitAt 실패: UnitData '{data.unitName}'의 성급 {starLevel} 프리팹 키가 비어있습니다.");
            return;
        }
        if (!TryReserveUnitPosition(gridPosition, data))
        {
            // Debug.LogWarning($"[FieldManager] CreateUnitAt ignored: position already reserved. pos={gridPosition}");
            return;
        }

        try
        {
            GameObject prefabToCreate = await AssetLoader.LoadAssetAsync<GameObject>(prefabKey);

            if (prefabToCreate == null)
            {
                // Debug.LogError($"{data.unitName}의 {starLevel}성에 해당하는 프리팹({prefabKey})을 로드할 수 없습니다!");
                return;
            }

            // 3D 그리드 사용 (벽 체크 포함)
            Vector3 worldPos = GridToWorld(gridPosition, checkForWall: true);

            GameObject newUnitGO = null;
            var runner = playerManager != null ? playerManager.Runner : null;
            bool hasNetPrefab = prefabToCreate.TryGetComponent<NetworkObject>(out var networkPrefab);
            if (runner != null && hasNetPrefab)
            {
                if (!playerManager.Object.HasStateAuthority)
                {
                    return;
                }

                var spawned = runner.Spawn(networkPrefab, worldPos, Quaternion.identity, playerManager.Object.InputAuthority);
                if (spawned == null)
                {
                    // Debug.LogError($"[FieldManager] Runner.Spawn 실패: {prefabToCreate.name} (Player={playerManager?.playerId})");
                    return;
                }
                retiredNetworkUnitIds.Remove(spawned.Id.Raw);
                newUnitGO = spawned.gameObject;
                if (unitParent != null)
                {
                    newUnitGO.transform.SetParent(unitParent, true);
                }
                // 클라이언트들의 placedUnits 등록을 위해 브로드캐스트
                if (playerManager != null)
                {
                    playerManager.RPC_RegisterUnitAt(spawned.Id, gridPosition.x, gridPosition.y, data.name, starLevel);
                }
            }
            else
            {
                newUnitGO = Instantiate(prefabToCreate, worldPos, Quaternion.identity, unitParent);
            }
            // Attach orientation fixer to ensure rig local rotation and face camera on spawn (이미 존재하면 생성하지 않음)
            var orientationFixer = newUnitGO.GetComponent<UnitOrientationFixer>();
            if (orientationFixer == null)
            {
                orientationFixer = newUnitGO.AddComponent<UnitOrientationFixer>();
                orientationFixer.rigRootName = "Armature"; // adjust if your rig root name differs
                orientationFixer.rigLocalEulerTarget = new Vector3(-90f, 180f, 0f);
                orientationFixer.faceCameraOnSpawn = true;
                orientationFixer.enforceEveryLateUpdate = true;
                orientationFixer.targetCamera = playerCamera; // avoid ComponentRegistry lookup warnings
                orientationFixer.yawOffsetDeg = 180f; // compensate if model's visual forward is flipped
            }
            Unit newUnitComponent = newUnitGO.GetComponent<Unit>();

            if (newUnitComponent != null)
            {
                AttachStatusBar(newUnitGO, newUnitComponent.SetStatusBar);
                // Initialize가 비동기이므로 완료를 기다린 후 등록합니다.
                await newUnitComponent.Initialize(data, starLevel, playerManager);
                if (placedUnits.ContainsKey(gridPosition))
                {
                    // Debug.LogWarning($"[FieldManager] CreateUnitAt ignored: position already occupied after spawn. pos={gridPosition}");
                    var netObj = newUnitGO.GetComponent<NetworkObject>();
                    if (runner != null && runner.IsRunning && netObj != null && (playerManager?.Object == null || playerManager.Object.HasStateAuthority))
                    {
                        runner.Despawn(netObj);
                    }
                    else
                    {
                        Destroy(newUnitGO);
                    }
                    return;
                }
                placedUnits.Add(gridPosition, newUnitComponent);
                SyncUnitPlacementIdentity(newUnitComponent, gridPosition);
                ProcessPendingNetworkMoves();
                if (!suppressCombination)
                {
                    CheckForCombination();
                }
                BroadcastAuthoritativeUnitRoster("CreateUnitAt");
            }
            else
            {
                // Debug.LogError($"{prefabToCreate.name} 프리팹에 Unit 컴포넌트가 없습니다!", newUnitGO);
                Destroy(newUnitGO);
            }
        }
        finally
        {
            ReleaseReservedUnitPosition(gridPosition);
        }
    }

    public void UnitDied(Unit deadUnit)
    {
        RemovePlacedUnitEntries(deadUnit);
    }

    public void ApplyPermanentBonusesToAllUnits()
    {
        foreach (var unit in placedUnits.Values)
        {
            if (unit != null)
            {
                unit.RefreshPermanentBonuses();
            }
        }
    }

    public void MoveUnit(Vector3Int from, Vector3Int to)
    {
        PruneExpiredPendingNetworkMoves();
        if (!IsValidGridPosition(from) || !IsValidGridPosition(to))
        {
            // Debug.LogWarning($"[FieldManager] MoveUnit 무시: 범위를 벗어난 이동 {from} -> {to} (GridSize={gridSize})");
            return;
        }

        if (placedUnits.TryGetValue(from, out Unit unit))
        {
            if (unit == null || !UnitBelongsToFieldOwner(unit))
            {
                return;
            }

            if (placedUnits.TryGetValue(to, out var targetUnit) && targetUnit != unit)
            {
                // Debug.LogWarning($"[FieldManager] MoveUnit 무시: 목표 위치 {to}에 이미 유닛이 있음 (from={from})");
                return;
            }

            if (!IsLegalMoveDestinationForUnit(unit.Data, to, out _))
            {
                return;
            }

            string uName = (unit != null && unit.Data != null) ? unit.Data.unitName : (unit != null ? unit.name : "Unit");
            RemovePlacedUnitEntries(unit);

            Vector3 finalWorldPos = GridToWorld(to, checkForWall: true);
            MoveUnitImmediate(unit, finalWorldPos);

            placedUnits[to] = unit;
            SyncUnitPlacementIdentity(unit, to);
            CheckForCombination();
            BroadcastAuthoritativeUnitRoster("MoveUnit");
        }
        else
        {
            if (TryGetPendingUnitDataAt(from, out var pendingUnitData) &&
                !IsLegalMoveDestinationForUnit(pendingUnitData, to, out _))
            {
                return;
            }

            QueuePendingNetworkMove(from, to);
            // Debug.LogWarning($"<color=red>[FieldManager] MoveUnit: '{from}' 위치에서 유닛을 찾을 수 없습니다.</color>");
        }
    }

    private bool UnitBelongsToFieldOwner(Unit unit)
    {
        if (unit == null || playerManager == null)
        {
            return false;
        }

        if (unit.Owner == playerManager)
        {
            return true;
        }

        if (unit.Owner != null && unit.Owner.playerId == playerManager.playerId)
        {
            return true;
        }

        return playerManager.ownedUnits != null && playerManager.ownedUnits.Contains(unit);
    }

    private bool UnitHasReplicatedFieldOwner(Unit unit)
    {
        return unit != null &&
               playerManager != null &&
               unit.OwnerPlayerIdForRoster == playerManager.playerId;
    }

    private void SyncUnitPlacementIdentity(Unit unit, Vector3Int gridPosition)
    {
        if (unit == null)
        {
            return;
        }

        unit.SyncFieldPlacementIdentity(playerManager, gridPosition);
    }

    private bool IsLegalMoveDestinationForUnit(UnitData unitData, Vector3Int to, out string reason)
    {
        if (!IsValidGridPosition(to))
        {
            reason = "move_destination_out_of_range";
            return false;
        }

        if (unitData == null)
        {
            if (HasWallAt(to))
            {
                reason = "move_pending_unit_type_unknown_for_wall";
                return false;
            }

            reason = null;
            return true;
        }

        if (unitData.unitType == UnitType.Melee && HasWallAt(to))
        {
            reason = "melee_unit_cannot_move_to_wall";
            return false;
        }

        reason = null;
        return true;
    }

    private bool ShouldQueuePendingNetworkMove(Vector3Int from)
    {
        if (HasPendingUnitAt(from))
        {
            return true;
        }

        return playerManager != null
            && playerManager.Runner != null
            && playerManager.Runner.IsRunning
            && playerManager.Object != null
            && playerManager.Object.IsValid
            && !playerManager.Object.HasStateAuthority;
    }

    private void QueuePendingNetworkMove(Vector3Int from, Vector3Int to)
    {
        if (!ShouldQueuePendingNetworkMove(from))
        {
            return;
        }

        if (pendingNetworkMoves.Any(move => move.From == from && move.To == to))
        {
            return;
        }

        pendingNetworkMoves.Add(new PendingNetworkMove
        {
            From = from,
            To = to,
            CreatedFrame = Time.frameCount
        });
    }

    private void ProcessPendingNetworkMoves()
    {
        if (pendingNetworkMoves.Count == 0)
        {
            return;
        }

        PruneExpiredPendingNetworkMoves();

        bool progressed;
        do
        {
            progressed = false;
            for (int i = pendingNetworkMoves.Count - 1; i >= 0; i--)
            {
                var move = pendingNetworkMoves[i];
                if (!placedUnits.TryGetValue(move.From, out var unit) || unit == null)
                {
                    continue;
                }

                if (!UnitBelongsToFieldOwner(unit))
                {
                    pendingNetworkMoves.RemoveAt(i);
                    continue;
                }

                if (!IsLegalMoveDestinationForUnit(unit.Data, move.To, out _))
                {
                    pendingNetworkMoves.RemoveAt(i);
                    continue;
                }

                if (placedUnits.TryGetValue(move.To, out var targetUnit) && targetUnit != unit)
                {
                    continue;
                }

                RemovePlacedUnitEntries(unit);
                MoveUnitImmediate(unit, GridToWorld(move.To, checkForWall: true));
                placedUnits[move.To] = unit;
                SyncUnitPlacementIdentity(unit, move.To);
                pendingNetworkMoves.RemoveAt(i);
                progressed = true;
            }
        } while (progressed);
    }

    private void PruneExpiredPendingNetworkMoves()
    {
        if (pendingNetworkMoves.Count == 0)
        {
            return;
        }

        int oldestAllowedFrame = Time.frameCount - PendingNetworkMoveLifetimeFrames;
        pendingNetworkMoves.RemoveAll(move => move.CreatedFrame < oldestAllowedFrame);
    }

    public void SwapUnits(Vector3Int a, Vector3Int b)
    {
        if (!IsValidGridPosition(a) || !IsValidGridPosition(b))
        {
            // Debug.LogWarning($"[FieldManager] SwapUnits 무시: 범위를 벗어남 {a} <-> {b} (GridSize={gridSize})");
            return;
        }
        if (!placedUnits.TryGetValue(a, out Unit unitA) || !placedUnits.TryGetValue(b, out Unit unitB))
        {
            // Debug.LogWarning($"[FieldManager] SwapUnits 실패: 대상 유닛을 찾을 수 없음 {a} <-> {b}");
            return;
        }

        string uNameA = (unitA != null && unitA.Data != null) ? unitA.Data.unitName : (unitA != null ? unitA.name : "UnitA");
        string uNameB = (unitB != null && unitB.Data != null) ? unitB.Data.unitName : (unitB != null ? unitB.name : "UnitB");
        

        Vector3 worldForA = GridToWorld(b, checkForWall: true);
        Vector3 worldForB = GridToWorld(a, checkForWall: true);

        // ✅ NetworkTransform 처리
        var networkTransformA = unitA.GetComponent<Fusion.NetworkTransform>();
        var networkTransformB = unitB.GetComponent<Fusion.NetworkTransform>();

        MoveUnitImmediate(unitA, worldForA);

        MoveUnitImmediate(unitB, worldForB);

        RemovePlacedUnitEntries(unitA);
        RemovePlacedUnitEntries(unitB);
        placedUnits[a] = unitB;
        placedUnits[b] = unitA;
        SyncUnitPlacementIdentity(unitB, a);
        SyncUnitPlacementIdentity(unitA, b);
        CheckForCombination();
        BroadcastAuthoritativeUnitRoster("SwapUnits");
    }

    public void RespawnAllUnits()
    {
        foreach (Unit unit in placedUnits.Values)
        {
            if (unit == null || !unit.HasValidNetworkObject)
            {
                continue;
            }

            if (unit.IsDead || !unit.gameObject.activeSelf || !unit.gameObject.activeInHierarchy)
            {
                unit.Respawn();
            }
        }
    }

    public Vector3Int? FindFirstEmptySlot(UnitData unitData)
    {
        // 3D 모드: 논리 그리드 전체 스캔
        for (int y = 0; y < gridSize.y; y++)
        {
            for (int x = 0; x < gridSize.x; x++)
            {
                Vector3Int pos = new Vector3Int(x, y, 0);
                if (placementManager.IsPositionValidForPlacement(pos, unitData))
                {
                    return pos;
                }
            }
        }
        return null;
    }

    #region AI-specific Public Methods

    /// <summary>
    /// AI가 맵의 경계를 인지할 수 있도록 전체 맵의 범위를 반환합니다.
    /// </summary>
    public BoundsInt GetMapBounds()
    {
        // 3D 모드: 논리 그리드 범위 반환
        return new BoundsInt(0, 0, 0, gridSize.x, gridSize.y, 1);
    }

    /// <summary>
    /// 현재 필드에 배치된 모든 아군 유닛의 리스트를 반환합니다.
    /// </summary>
    public List<Unit> GetAlliedUnitsOnField()
    {
        ReconcileClientUnitMapFromWorldIfNeeded("GetAlliedUnitsOnField");
        // placedUnits 딕셔너리의 값들(Unit)을 리스트로 변환하여 반환합니다.
        return placedUnits.Values.ToList();
    }

    /// <summary>
    /// AI 재배치 로직을 위해 특정 유닛의 현재 그리드 위치를 반환합니다.
    /// </summary>
    private void ReconcileClientUnitMapFromWorldIfNeeded(string context)
    {
        if (!Application.isPlaying || playerManager == null)
        {
            return;
        }

        var runner = playerManager.Runner;
        if (runner == null || !runner.IsRunning)
        {
            return;
        }

        if (playerManager.Object != null && playerManager.Object.HasStateAuthority)
        {
            return;
        }

        if (Time.unscaledTime - _lastClientUnitMapReconcileTime < ClientUnitMapReconcileIntervalSeconds)
        {
            return;
        }

        _lastClientUnitMapReconcileTime = Time.unscaledTime;
        RebuildUnitMapAfterMigration($"ClientRoster.{context}", false, out _);
    }

    public Vector3Int? GetUnitPosition(Unit unit)
    {
        var entry = placedUnits.FirstOrDefault(kvp => kvp.Value == unit);
        if (entry.Value != null) // 유닛을 찾았는지 확인합니다.
        {
            return entry.Key;
        }
        return null;
    }

    private struct UnitRosterBroadcastEntry
    {
        public NetworkId UnitId;
        public int UnitIdRaw;
        public Vector3Int Position;
        public string UnitDataKey;
        public int UnitDataKeyHash;
        public int StarLevel;
    }

    public void BroadcastAuthoritativeUnitRoster(string context)
    {
        var runner = playerManager != null ? playerManager.Runner : null;
        if (runner == null
            || !runner.IsRunning
            || playerManager == null
            || playerManager.Object == null
            || !playerManager.Object.HasStateAuthority)
        {
            return;
        }

        var entries = BuildAuthoritativeUnitRosterEntries();
        if (entries.Count == 0)
        {
            return;
        }

        int[] compactRoster = new int[2 + (entries.Count * 6)];
        compactRoster[0] = -2;
        compactRoster[1] = entries.Count;
        for (int i = 0; i < entries.Count; i++)
        {
            UnitRosterBroadcastEntry entry = entries[i];
            int offset = 2 + (i * 6);
            compactRoster[offset + 0] = entry.UnitIdRaw;
            compactRoster[offset + 1] = entry.Position.x;
            compactRoster[offset + 2] = entry.Position.y;
            compactRoster[offset + 3] = entry.Position.z;
            compactRoster[offset + 4] = entry.StarLevel;
            compactRoster[offset + 5] = entry.UnitDataKeyHash;
        }

        try
        {
            playerManager.RPC_ReconcileUnitRosterCompact(compactRoster);
            GameManagers.Instance?.RPC_ReconcilePlayerUnitRosterCompact(playerManager.playerId, compactRoster);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[FieldManager] Compact unit roster broadcast failed. context={context}, count={entries.Count}, error={ex.GetType().Name}");
            return;
        }

        foreach (UnitRosterBroadcastEntry entry in entries)
        {
            try
            {
                playerManager.RPC_RegisterUnitAt(entry.UnitId, entry.Position.x, entry.Position.y, entry.UnitDataKey, entry.StarLevel);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FieldManager] Unit roster entry broadcast failed. context={context}, unit={entry.UnitIdRaw}, error={ex.GetType().Name}");
            }
        }
    }

    private List<UnitRosterBroadcastEntry> BuildAuthoritativeUnitRosterEntries()
    {
        var entries = new List<UnitRosterBroadcastEntry>();
        foreach (var entry in placedUnits
            .Where(kvp => kvp.Value != null)
            .OrderBy(kvp => kvp.Key.x)
            .ThenBy(kvp => kvp.Key.y)
            .ThenBy(kvp => kvp.Key.z))
        {
            if (!entry.Value.TryGetComponent<NetworkObject>(out var networkObject)
                || networkObject == null
                || !networkObject.IsValid)
            {
                continue;
            }

            entries.Add(new UnitRosterBroadcastEntry
            {
                UnitId = networkObject.Id,
                UnitIdRaw = unchecked((int)networkObject.Id.Raw),
                Position = entry.Key,
                UnitDataKey = GetUnitDataRegistrationKey(entry.Value),
                UnitDataKeyHash = StableUnitDataKeyHash(GetUnitDataRegistrationKey(entry.Value)),
                StarLevel = entry.Value.starLevel
            });
        }

        return entries;
    }

    private void BuildAuthoritativeUnitRoster(out int[] unitIdRaws, out int[] flatPositions, out string[] unitDataKeys, out int[] starLevels)
    {
        var entries = placedUnits
            .Where(kvp => kvp.Value != null
                && TryGetUnitNetworkIdRaw(kvp.Value, out uint unitIdRaw))
            .OrderBy(kvp => kvp.Key.x)
            .ThenBy(kvp => kvp.Key.y)
            .ThenBy(kvp => kvp.Key.z)
            .ToList();

        unitIdRaws = new int[entries.Count];
        flatPositions = new int[entries.Count * 3];
        unitDataKeys = new string[entries.Count];
        starLevels = new int[entries.Count];

        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            TryGetUnitNetworkIdRaw(entry.Value, out uint unitIdRaw);
            unitIdRaws[i] = unchecked((int)unitIdRaw);
            flatPositions[(i * 3) + 0] = entry.Key.x;
            flatPositions[(i * 3) + 1] = entry.Key.y;
            flatPositions[(i * 3) + 2] = entry.Key.z;
            unitDataKeys[i] = GetUnitDataRegistrationKey(entry.Value);
            starLevels[i] = entry.Value != null ? entry.Value.starLevel : 0;
        }
    }

    private static bool TryGetUnitNetworkIdRaw(Unit unit, out uint unitIdRaw)
    {
        unitIdRaw = 0;
        if (unit == null || !unit.TryGetComponent<NetworkObject>(out var networkObject) || networkObject == null || !networkObject.IsValid)
        {
            return false;
        }

        unitIdRaw = networkObject.Id.Raw;
        return true;
    }

    private void BroadcastUnitUnregistered(Unit unit, Vector3Int position, string unitDataKey, int starLevel)
    {
        var runner = playerManager != null ? playerManager.Runner : null;
        if (runner == null
            || !runner.IsRunning
            || playerManager == null
            || playerManager.Object == null
            || !playerManager.Object.HasStateAuthority
            || unit == null
            || !unit.TryGetComponent<NetworkObject>(out var networkObject))
        {
            return;
        }

        retiredNetworkUnitIds.Add(networkObject.Id.Raw);
        RemoveOwnedUnitReference(unit);
        playerManager.RPC_UnregisterUnitAt(networkObject.Id, position.x, position.y, unitDataKey, starLevel);
    }

    private static string GetUnitDataRegistrationKey(Unit unit)
    {
        return unit != null && unit.Data != null ? unit.Data.name : string.Empty;
    }

    private static int StableUnitDataKeyHash(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return 0;
        }

        unchecked
        {
            uint hash = 2166136261u;
            for (int i = 0; i < value.Length; i++)
            {
                hash ^= value[i];
                hash *= 16777619u;
            }

            return (int)hash;
        }
    }

    public int GetSellPrice(Unit unit)
    {
        if (unit == null || unit.Data == null) return 0;
        int baseCost = GetCombinedUnitCost(unit.Data.cost, unit.starLevel);
        if (unit.starLevel >= 2)
        {
            baseCost = Mathf.Max(0, baseCost - sellPenalty);
        }
        return baseCost;
    }

    public bool TrySellUnitAt(Vector3Int gridPosition)
    {
        if (playerManager == null) return false;
        var gm = GameManagers.Instance;
        if (gm != null && (gm.GetGameState() != GameManagers.GameState.Prepare || gm.IsSequenceTransitioning))
        {
            return false;
        }

        if (!placedUnits.TryGetValue(gridPosition, out Unit unit) || unit == null)
        {
            return false;
        }

        int sellPrice = GetSellPrice(unit);
        playerManager.AddGold(sellPrice);

        if (selectedUnit == unit)
        {
            selectedUnit = null;
            selectedUnitNetworkTransform = null;
            isDragStarted = false;
        }

        if (unitDisplayedInPanel == unit && unitDetailPanelInstance != null)
        {
            if (UIManagers.Instance != null)
            {
                UIManagers.Instance.ReturnUIElement("UI_Pnl_UnitDetail");
            }
            unitDetailPanelInstance = null;
            unitDisplayedInPanel = null;
            HideUnitSellPanel();
        }
        else if (unitDisplayedInSellPanel == unit)
        {
            HideUnitSellPanel();
        }

        placedUnits.Remove(gridPosition);

        var runner = playerManager.Runner;
        var networkObject = unit.GetComponent<NetworkObject>();
        if (runner != null && runner.IsRunning && networkObject != null)
        {
            if (playerManager.Object == null || playerManager.Object.HasStateAuthority)
            {
                runner.Despawn(networkObject);
            }
        }
        else
        {
            Destroy(unit.gameObject);
        }

        return true;
    }

    private int GetCombinedUnitCost(int unitCost, int starLevel)
    {
        if (unitCost <= 0 || starLevel <= 0) return 0;
        int multiplier = 1;
        for (int i = 1; i < starLevel; i++)
        {
            multiplier *= 3;
        }
        return unitCost * multiplier;
    }

    /// <summary>
    /// 유닛 타입에 따라 AI가 배치할 수 있는 모든 유효한 타일 위치 목록을 반환합니다.
    /// 이 메서드는 타일의 존재 여부와 타입만 확인하며, 해당 위치에 다른 유닛이 있는지는 확인하지 않습니다.
    /// </summary>
    public List<Vector3Int> GetValidPlacementTiles(UnitType unitType)
    {
        RefreshWallMapsFromSceneIfPlaying("GetValidPlacementTiles");

        var validTiles = new List<Vector3Int>();
        // 3D 모드: 논리 그리드의 모든 셀이 배치 가능 (근접 유닛의 경우)
        if (unitType == UnitType.Melee)
        {
            for (int y = 0; y < gridSize.y; y++)
            {
                for (int x = 0; x < gridSize.x; x++)
                {
                    var pos = new Vector3Int(x, y, 0);
                    if (!HasWallAt(pos))
                    {
                        validTiles.Add(pos);
                    }
                }
            }
        }
        else // Ranged
        {
            // 원거리 유닛은 파괴 가능/불가 벽 위에 배치 가능
            foreach (var wallPos in placedWalls.Keys)
            {
                validTiles.Add(wallPos);
            }
            foreach (var permPos in placedPermanentWalls.Keys)
            {
                validTiles.Add(permPos);
            }
        }
        return validTiles;
    }

    private void RefreshWallMapsFromSceneIfPlaying(string context)
    {
        if (!Application.isPlaying)
        {
            return;
        }

        RebuildWallMapsAfterMigration($"FieldManager.{context}", false, out _);
    }

    #endregion

    #region AI-specific Public Methods

    /// <summary>
    /// 재배치를 위해 필드에 있는 모든 유닛의 등록을 해제합니다. (오브젝트는 파괴하지 않음)
    /// </summary>
    public void UnregisterAllUnits()
    {
        placedUnits.Clear();
        pendingUnitPositions.Clear();
        pendingUnitDataByPosition.Clear();
        pendingNetworkMoves.Clear();
        retiredNetworkUnitIds.Clear();
    }

    /// <summary>
    /// 이미 존재하는 유닛 게임 오브젝트를 특정 위치에 등록하고 위치를 이동시킵니다.
    /// </summary>
    public void RegisterUnitAt(Unit unit, Vector3Int gridPosition)
    {
        if (unit == null)
        {
            // Debug.LogWarning($"[FieldManager] RegisterUnitAt 무시: unit이 null입니다. pos={gridPosition}");
            return;
        }
        if (!IsValidGridPosition(gridPosition))
        {
            // Debug.LogWarning($"[FieldManager] RegisterUnitAt 무시: 유효 범위 밖 위치 {gridPosition} (GridSize={gridSize})");
            return;
        }
        ReleaseReservedUnitPosition(gridPosition);

        // Despawn/Destroy 된 유닛 레퍼런스가 남아있을 수 있어 정리합니다.
        if (placedUnits.TryGetValue(gridPosition, out Unit existingUnit) && existingUnit == null)
        {
            placedUnits.Remove(gridPosition);
        }

        // 같은 유닛이 다른 위치에 이미 등록되어 있으면 기존 엔트리를 제거합니다.
        var previousCells = GetPlacedCellsForUnit(unit);
        if (previousCells.Count == 1 && previousCells[0] == gridPosition)
        {
            Vector3 targetWorldPos = GridToWorld(gridPosition, checkForWall: true);
            if ((unit.transform.position - targetWorldPos).sqrMagnitude > 0.0001f)
            {
                MoveUnitImmediate(unit, targetWorldPos);
            }

            placedUnits[gridPosition] = unit;
            SyncUnitPlacementIdentity(unit, gridPosition);
            ProcessPendingNetworkMoves();
            return;
        }

        foreach (var previousCell in previousCells)
        {
            placedUnits.Remove(previousCell);
            pendingUnitPositions.Remove(previousCell);
            pendingUnitDataByPosition.Remove(previousCell);
        }

        Vector3 worldPos = GridToWorld(gridPosition, checkForWall: true);
        MoveUnitImmediate(unit, worldPos);
        placedUnits[gridPosition] = unit;
        SyncUnitPlacementIdentity(unit, gridPosition);
        ProcessPendingNetworkMoves();
    }

    public void ReconcileUnitsToAuthoritativeRoster(Dictionary<uint, Vector3Int> authoritativePositions)
    {
        if (authoritativePositions == null)
        {
            return;
        }

        foreach (var unitIdRaw in authoritativePositions.Keys)
        {
            retiredNetworkUnitIds.Remove(unitIdRaw);
        }

        var seen = new HashSet<uint>();
        var cellsToRemove = new List<Vector3Int>();
        foreach (var entry in placedUnits.ToArray())
        {
            var unit = entry.Value;
            if (unit == null || !TryGetUnitNetworkIdRaw(unit, out uint unitIdRaw))
            {
                cellsToRemove.Add(entry.Key);
                continue;
            }

            if (!authoritativePositions.TryGetValue(unitIdRaw, out var authoritativePosition))
            {
                retiredNetworkUnitIds.Add(unitIdRaw);
                RemoveOwnedUnitReference(unit);
                cellsToRemove.Add(entry.Key);
                continue;
            }

            if (entry.Key != authoritativePosition || !seen.Add(unitIdRaw))
            {
                cellsToRemove.Add(entry.Key);
            }
        }

        foreach (var cell in cellsToRemove)
        {
            placedUnits.Remove(cell);
            pendingUnitPositions.Remove(cell);
        }

        ProcessPendingNetworkMoves();
    }

    public bool UnregisterUnitAt(Unit unit, Vector3Int expectedPosition, string unitDataKey, int starLevel)
    {
        bool removed = false;

        if (unit != null)
        {
            if (TryGetUnitNetworkIdRaw(unit, out uint unitIdRaw))
            {
                retiredNetworkUnitIds.Add(unitIdRaw);
            }

            RemoveOwnedUnitReference(unit);
            var keys = placedUnits
                .Where(kvp => kvp.Value == unit)
                .Select(kvp => kvp.Key)
                .ToArray();
            foreach (var key in keys)
            {
                placedUnits.Remove(key);
                removed = true;
            }
        }

        if (IsValidGridPosition(expectedPosition) && placedUnits.TryGetValue(expectedPosition, out var existing))
        {
            if (existing == null || existing == unit)
            {
                placedUnits.Remove(expectedPosition);
                removed = true;
            }
        }

        if (removed)
        {
            pendingUnitPositions.Remove(expectedPosition);
        }

        return removed;
    }

    #endregion

    #region AI 배치 Helper

    public Vector3Int? FindBestSpotForAI(UnitData unitData, List<AstarNode> monsterPathContext, List<Unit> alliedUnitsContext = null, HashSet<Vector3Int> occupiedTiles = null, Vector3Int? movingUnitOriginalPos = null)
    {

        // 디버그 정보 초기화
        _debugTileScores.Clear();
        _debugScoreBreakdowns.Clear();
        _showDebugScores = true;
        _debugUnitData = unitData;

        var allValidTiles = GetValidPlacementTiles(unitData.unitType);
        if (allValidTiles == null || allValidTiles.Count == 0)
        {
            // Debug.LogWarning($"AI가 {unitData.unitType} 타입의 유닛을 배치할 유효한 타일을 찾지 못했습니다.");
            return null;
        }

        List<Vector3Int> candidateTiles = allValidTiles;
        HashSet<Vector3Int> rangedPathTiles = null;
        HashSet<Vector3Int> meleePathTiles = null;

        // [핵심 수정] 근접 유닛의 경우, 배치 후보지를 몬스터 경로 위로 먼저 한정합니다.
        if (unitData.unitType == UnitType.Melee && monsterPathContext != null && monsterPathContext.Count > 0)
        {
            // 디버그: AI 필드 타일 범위와 몬스터 경로 범위 확인 (필요시 주석 해제)
            // var fieldTileRange = $"필드 타일 범위: ({allValidTiles.Min(t => t.x)}, {allValidTiles.Min(t => t.y)}) ~ ({allValidTiles.Max(t => t.x)}, {allValidTiles.Max(t => t.y)})";
            // var pathRange = $"받은 몬스터 경로 범위: ({monsterPathContext.Min(n => n.x)}, {monsterPathContext.Min(n => n.y)}) ~ ({monsterPathContext.Max(n => n.x)}, {monsterPathContext.Max(n => n.y)})";
            // Debug.Log($"[AI Placement Debug] {fieldTileRange}");
            // Debug.Log($"[AI Placement Debug] {pathRange}");
            // Debug.Log($"[AI Placement Debug] 받은 경로 첫 번째 노드: ({monsterPathContext[0].x}, {monsterPathContext[0].y}), 마지막 노드: ({monsterPathContext[monsterPathContext.Count-1].x}, {monsterPathContext[monsterPathContext.Count-1].y})");

            meleePathTiles = BuildMonsterPathTileSet(monsterPathContext);
            var onPathTiles = allValidTiles.Where(tile => meleePathTiles.Contains(tile)).ToList();

            // 경로 위에 배치 가능한 타일이 있다면, 후보지를 그 타일들로 제한합니다.
            if (onPathTiles.Count > 0)
            {
                candidateTiles = onPathTiles;
                // Debug.Log($"[AI Placement] 근접 유닛 {unitData.unitName} 배치: 몬스터 경로 위 {onPathTiles.Count}개 타일로 후보지 제한");
            }
            else
            {
                // Debug.Log($"[AI Placement] 근접 유닛 {unitData.unitName} 배치: 몬스터 경로 위에 배치 가능한 타일이 없어 전체 {allValidTiles.Count}개 타일 대상");
            }
            // 경로 위에 배치할 곳이 없다면, 원래의 모든 유효 타일을 대상으로 점수를 계산합니다(폴백).
        }

        if (unitData.unitType == UnitType.Ranged && monsterPathContext != null && monsterPathContext.Count > 0)
        {
            rangedPathTiles = BuildMonsterPathTileSet(monsterPathContext);
            candidateTiles = FilterRangedCandidatesForMonsterPath(allValidTiles, monsterPathContext, unitData.attackRange);
            if (candidateTiles == null || candidateTiles.Count == 0)
            {
                candidateTiles = allValidTiles;
            }
        }

        if (unitData.unitType == UnitType.Ranged)
        {
            candidateTiles = SelectPreferredRangedCandidateTier(candidateTiles, occupiedTiles, movingUnitOriginalPos);
            if ((candidateTiles == null || candidateTiles.Count == 0) && !ReferenceEquals(candidateTiles, allValidTiles))
            {
                candidateTiles = SelectPreferredRangedCandidateTier(allValidTiles, occupiedTiles, movingUnitOriginalPos);
            }
        }

        var alliedUnits = alliedUnitsContext ?? GetAlliedUnitsOnField();

        Vector3Int bestPosition = Vector3Int.zero;
        float highestScore = -1f;

        // 후보 타일들('candidateTiles')을 순회하며 최고 점수 위치를 찾습니다.
        foreach (var tilePos in candidateTiles)
        {
            if (occupiedTiles != null)
            {
                if (occupiedTiles.Contains(tilePos)) continue;
            }
            else
            {
                // 현재 이동시키려는 유닛의 원래 위치가 아니라면, 점유된 타일은 건너뜁니다.
                bool isSpotOfMovingUnit = movingUnitOriginalPos.HasValue && tilePos == movingUnitOriginalPos.Value;
                if (IsUnitAt(tilePos) && !isSpotOfMovingUnit)
                {
                    continue;
                }
            }

            var context = new AIContext(playerManager, unitData, tilePos, alliedUnits, monsterPathContext);
            float currentScore = CalculateScore(context, _placementConsiderations, tilePos);
            if (unitData.unitType == UnitType.Ranged && rangedPathTiles != null && rangedPathTiles.Count > 0)
            {
                currentScore += CalculateRangedPathPriorityBonus(tilePos, rangedPathTiles, unitData.attackRange);
            }
            else if (unitData.unitType == UnitType.Melee && meleePathTiles != null && meleePathTiles.Contains(tilePos))
            {
                currentScore += CalculateNearbyRangedAllyBonus(tilePos, alliedUnits);
            }

            // 디버그용 점수 저장
            _debugTileScores[tilePos] = currentScore;

            if (currentScore > highestScore)
            {
                highestScore = currentScore;
                bestPosition = tilePos;
            }
        }

        if (highestScore > -1f)
        {
            if (movingUnitOriginalPos.HasValue &&
                bestPosition == movingUnitOriginalPos.Value &&
                ShouldForceMoveAwayFromOriginal(movingUnitOriginalPos))
            {
                var fallback = unitData.unitType == UnitType.Ranged
                    ? FindBestRangedFallbackAwayFromOriginal(
                        unitData,
                        allValidTiles,
                        alliedUnits,
                        monsterPathContext,
                        rangedPathTiles,
                        occupiedTiles,
                        movingUnitOriginalPos)
                    : FindBestMeleeFallbackAwayFromOriginal(
                        unitData,
                        allValidTiles,
                        alliedUnits,
                        monsterPathContext,
                        occupiedTiles,
                        movingUnitOriginalPos);
                if (fallback.HasValue)
                {
                    ClearDebugScores();
                    return fallback.Value;
                }
            }
            // Debug.Log($"[AI Placement] {unitData.unitName}을(를) {bestPosition}에 배치 (점수: {highestScore:F2})");

            // 3초 후 디버그 표시 끄기
            Invoke(nameof(ClearDebugScores), 3.0f);

            return bestPosition;
        }

        // 점수 계산에 실패했더라도, 배치 가능한 첫 번째 위치라도 반환합니다.
        if (unitData.unitType == UnitType.Ranged)
        {
            var fallback = FindBestRangedFallbackAwayFromOriginal(
                unitData,
                allValidTiles,
                alliedUnits,
                monsterPathContext,
                rangedPathTiles,
                occupiedTiles,
                movingUnitOriginalPos);
            if (fallback.HasValue)
            {
                ClearDebugScores();
                return fallback.Value;
            }

            if (rangedPathTiles != null && rangedPathTiles.Count > 0)
            {
                ClearDebugScores();
                return movingUnitOriginalPos;
            }
        }

        if (unitData.unitType == UnitType.Melee)
        {
            var fallback = FindBestMeleeFallbackAwayFromOriginal(
                unitData,
                allValidTiles,
                alliedUnits,
                monsterPathContext,
                occupiedTiles,
                movingUnitOriginalPos);
            if (fallback.HasValue)
            {
                ClearDebugScores();
                return fallback.Value;
            }
        }

        ClearDebugScores();
        return FindFirstEmptySlot(unitData);
    }

    private Vector3Int? FindBestRangedFallbackAwayFromOriginal(
        UnitData unitData,
        List<Vector3Int> allValidTiles,
        List<Unit> alliedUnits,
        List<AstarNode> monsterPathContext,
        HashSet<Vector3Int> rangedPathTiles,
        HashSet<Vector3Int> occupiedTiles,
        Vector3Int? movingUnitOriginalPos)
    {
        if (unitData == null || allValidTiles == null || allValidTiles.Count == 0)
        {
            return null;
        }

        var fallbackTiles = SelectPreferredRangedCandidateTier(allValidTiles, occupiedTiles, movingUnitOriginalPos);
        if (fallbackTiles == null || fallbackTiles.Count == 0)
        {
            return null;
        }

        Vector3Int bestPosition = Vector3Int.zero;
        float highestScore = -1f;
        foreach (var tilePos in fallbackTiles)
        {
            if (movingUnitOriginalPos.HasValue && tilePos == movingUnitOriginalPos.Value)
            {
                continue;
            }

            if (occupiedTiles != null && occupiedTiles.Contains(tilePos))
            {
                continue;
            }

            if (IsUnitAt(tilePos))
            {
                continue;
            }

            var context = new AIContext(playerManager, unitData, tilePos, alliedUnits, monsterPathContext);
            float currentScore = CalculateScore(context, _placementConsiderations, tilePos);
            currentScore += CalculateRangedPathPriorityBonus(tilePos, rangedPathTiles, unitData.attackRange);
            currentScore += CalculateFieldCenterScore(tilePos) * 3.0f;

            if (IsOuterRingCell(tilePos) || IsBorderGapCell(tilePos))
            {
                currentScore -= 20.0f;
            }
            else if (IsNearFieldEdgeCell(tilePos))
            {
                currentScore -= 8.0f;
            }

            if (currentScore > highestScore)
            {
                highestScore = currentScore;
                bestPosition = tilePos;
            }
        }

        return highestScore > -1f ? bestPosition : (Vector3Int?)null;
    }

    private Vector3Int? FindBestMeleeFallbackAwayFromOriginal(
        UnitData unitData,
        List<Vector3Int> allValidTiles,
        List<Unit> alliedUnits,
        List<AstarNode> monsterPathContext,
        HashSet<Vector3Int> occupiedTiles,
        Vector3Int? movingUnitOriginalPos)
    {
        if (unitData == null || allValidTiles == null || allValidTiles.Count == 0)
        {
            return null;
        }

        var pathTiles = BuildMonsterPathTileSet(monsterPathContext);
        var pathCandidates = pathTiles.Count > 0
            ? allValidTiles.Where(tile => pathTiles.Contains(tile)).ToList()
            : new List<Vector3Int>();
        var fallbackTiles = pathCandidates.Count > 0 ? pathCandidates : allValidTiles;

        Vector3Int bestPosition = Vector3Int.zero;
        float highestScore = -1f;
        foreach (var tilePos in fallbackTiles)
        {
            if (movingUnitOriginalPos.HasValue && tilePos == movingUnitOriginalPos.Value)
            {
                continue;
            }

            if (occupiedTiles != null && occupiedTiles.Contains(tilePos))
            {
                continue;
            }

            if (IsUnitAt(tilePos) || HasWallAt(tilePos))
            {
                continue;
            }

            var context = new AIContext(playerManager, unitData, tilePos, alliedUnits, monsterPathContext);
            float currentScore = CalculateScore(context, _placementConsiderations, tilePos);
            if (pathTiles.Contains(tilePos))
            {
                currentScore += 20.0f;
                currentScore += CalculateNearbyRangedAllyBonus(tilePos, alliedUnits);
            }

            currentScore += CalculateFieldCenterScore(tilePos) * 2.0f;
            if (IsOuterRingCell(tilePos) || IsBorderGapCell(tilePos))
            {
                currentScore -= 20.0f;
            }
            else if (IsNearFieldEdgeCell(tilePos))
            {
                currentScore -= 8.0f;
            }

            if (currentScore > highestScore)
            {
                highestScore = currentScore;
                bestPosition = tilePos;
            }
        }

        return highestScore > -1f ? bestPosition : (Vector3Int?)null;
    }

    private bool ShouldForceMoveAwayFromOriginal(Vector3Int? movingUnitOriginalPos)
    {
        if (!movingUnitOriginalPos.HasValue)
        {
            return false;
        }

        var original = movingUnitOriginalPos.Value;
        return IsNearFieldEdgeCell(original) || IsBorderGapCell(original);
    }

    private List<Vector3Int> FilterRangedCandidatesForMonsterPath(
        List<Vector3Int> allValidTiles,
        List<AstarNode> monsterPathContext,
        float attackRange)
    {
        if (allValidTiles == null || allValidTiles.Count == 0 || monsterPathContext == null || monsterPathContext.Count == 0)
        {
            return allValidTiles;
        }

        var pathTiles = BuildMonsterPathTileSet(monsterPathContext);

        if (pathTiles.Count == 0)
        {
            return new List<Vector3Int>();
        }

        var pathCoveringTiles = allValidTiles
            .Where(tile => CountCoveredMonsterPathTiles(tile, pathTiles, attackRange) > 0)
            .ToList();
        if (pathCoveringTiles.Count == 0)
        {
            return new List<Vector3Int>();
        }

        var coverageByTile = pathCoveringTiles
            .ToDictionary(tile => tile, tile => CountCoveredMonsterPathTiles(tile, pathTiles, attackRange));
        int maxCovered = coverageByTile.Values.Max();
        int minStrongCovered = Mathf.Max(1, Mathf.CeilToInt(maxCovered * 0.85f));
        var strongCoverageTiles = coverageByTile
            .Where(kvp => kvp.Value >= minStrongCovered)
            .Select(kvp => kvp.Key)
            .ToList();
        var maxCoverageTiles = coverageByTile
            .Where(kvp => kvp.Value == maxCovered)
            .Select(kvp => kvp.Key)
            .ToList();

        var centralStrongCoverageTiles = strongCoverageTiles
            .Where(tile => !IsOuterRingCell(tile) &&
                           !IsNearFieldEdgeCell(tile) &&
                           !IsBorderGapCell(tile) &&
                           CalculateFieldCenterScore(tile) >= 0.55f)
            .ToList();
        if (centralStrongCoverageTiles.Count > 0)
        {
            return centralStrongCoverageTiles;
        }

        var centralMaxCoverageTiles = maxCoverageTiles
            .Where(tile => !IsOuterRingCell(tile) &&
                           !IsNearFieldEdgeCell(tile) &&
                           !IsBorderGapCell(tile) &&
                           CalculateFieldCenterScore(tile) >= 0.55f)
            .ToList();
        if (centralMaxCoverageTiles.Count > 0)
        {
            return centralMaxCoverageTiles;
        }

        var interiorPathCoveringTiles = strongCoverageTiles
            .Where(tile => !IsOuterRingCell(tile) &&
                           !IsNearFieldEdgeCell(tile) &&
                           !IsBorderGapCell(tile))
            .ToList();
        if (interiorPathCoveringTiles.Count > 0)
        {
            var centralInteriorTiles = interiorPathCoveringTiles
                .Where(tile => CalculateFieldCenterScore(tile) >= 0.55f)
                .ToList();
            return centralInteriorTiles.Count > 0
                ? centralInteriorTiles
                : interiorPathCoveringTiles;
        }

        var centralAnyCoverageTiles = pathCoveringTiles
            .Where(tile => !IsOuterRingCell(tile) &&
                           !IsNearFieldEdgeCell(tile) &&
                           !IsBorderGapCell(tile) &&
                           CalculateFieldCenterScore(tile) >= 0.45f)
            .ToList();
        if (centralAnyCoverageTiles.Count > 0)
        {
            int centralMaxCovered = centralAnyCoverageTiles.Max(tile => coverageByTile[tile]);
            int minCentralCovered = Mathf.Max(1, Mathf.CeilToInt(centralMaxCovered * 0.70f));
            var centralCoverageBand = centralAnyCoverageTiles
                .Where(tile => coverageByTile[tile] >= minCentralCovered)
                .ToList();
            return centralCoverageBand.Count > 0 ? centralCoverageBand : centralAnyCoverageTiles;
        }

        return new List<Vector3Int>();
    }

    private List<Vector3Int> SelectPreferredRangedCandidateTier(
        List<Vector3Int> candidateTiles,
        HashSet<Vector3Int> occupiedTiles,
        Vector3Int? movingUnitOriginalPos)
    {
        if (candidateTiles == null || candidateTiles.Count == 0)
        {
            return candidateTiles;
        }

        var strictInterior = candidateTiles
            .Where(tile => IsStrictInteriorRangedCell(tile))
            .ToList();
        if (HasAvailablePlacementTile(strictInterior, occupiedTiles, movingUnitOriginalPos))
        {
            return strictInterior;
        }

        var innerRing = candidateTiles
            .Where(tile => !IsOuterRingCell(tile) && !IsBorderGapCell(tile))
            .ToList();
        if (HasAvailablePlacementTile(innerRing, occupiedTiles, movingUnitOriginalPos))
        {
            return innerRing;
        }

        var nonGap = candidateTiles
            .Where(tile => !IsBorderGapCell(tile))
            .ToList();
        if (HasAvailablePlacementTile(nonGap, occupiedTiles, movingUnitOriginalPos))
        {
            return nonGap;
        }

        return candidateTiles;
    }

    private bool HasAvailablePlacementTile(
        List<Vector3Int> candidateTiles,
        HashSet<Vector3Int> occupiedTiles,
        Vector3Int? movingUnitOriginalPos)
    {
        if (candidateTiles == null || candidateTiles.Count == 0)
        {
            return false;
        }

        foreach (var tile in candidateTiles)
        {
            if (occupiedTiles != null && occupiedTiles.Contains(tile))
            {
                continue;
            }

            bool isSpotOfMovingUnit = movingUnitOriginalPos.HasValue && tile == movingUnitOriginalPos.Value;
            if (IsUnitAt(tile) && !isSpotOfMovingUnit)
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private HashSet<Vector3Int> BuildMonsterPathTileSet(List<AstarNode> monsterPathContext)
    {
        var pathTiles = new HashSet<Vector3Int>();
        if (monsterPathContext == null)
        {
            return pathTiles;
        }

        foreach (var node in monsterPathContext)
        {
            if (node == null)
            {
                continue;
            }

            var tile = new Vector3Int(node.x, node.y, 0);
            if (IsValidGridPosition(tile))
            {
                pathTiles.Add(tile);
            }
        }

        return pathTiles;
    }

    private float CalculateRangedPathPriorityBonus(Vector3Int position, HashSet<Vector3Int> pathTiles, float attackRange)
    {
        if (pathTiles == null || pathTiles.Count == 0)
        {
            return 0f;
        }

        int covered = CountCoveredMonsterPathTiles(position, pathTiles, attackRange);
        if (covered <= 0)
        {
            return 0f;
        }

        int usefulTargetCount = Mathf.Max(1, Mathf.Min(pathTiles.Count, CountTilesInAttackCircle(attackRange)));
        float coverageScore = Mathf.Clamp01((float)covered / usefulTargetCount);
        float centerScore = CalculateFieldCenterScore(position);
        float edgePenalty = IsNearFieldEdgeCell(position) ? -3.5f : 0f;
        float borderPenalty = IsOuterRingCell(position) || IsBorderGapCell(position) ? -6.0f : 0f;
        return covered * 4.0f + coverageScore * 6.0f + centerScore * 8.0f + edgePenalty + borderPenalty;
    }

    private float CalculateNearbyRangedAllyBonus(Vector3Int position, List<Unit> alliedUnits)
    {
        if (alliedUnits == null || alliedUnits.Count == 0)
        {
            return 0f;
        }

        float bestBonus = 0f;
        foreach (var ally in alliedUnits)
        {
            if (ally == null || ally.Data == null || ally.Data.unitType != UnitType.Ranged)
            {
                continue;
            }

            if (!TryGetUnitGridPosition(ally, out var allyCell) || allyCell == position)
            {
                continue;
            }

            int distance = Mathf.Max(Mathf.Abs(allyCell.x - position.x), Mathf.Abs(allyCell.y - position.y));
            if (distance <= 1)
            {
                bestBonus = Mathf.Max(bestBonus, 34f);
            }
            else if (distance == 2)
            {
                bestBonus = Mathf.Max(bestBonus, 18f);
            }
            else if (distance == 3)
            {
                bestBonus = Mathf.Max(bestBonus, 8f);
            }
        }

        return bestBonus;
    }

    private bool TryGetUnitGridPosition(Unit unit, out Vector3Int position)
    {
        position = default(Vector3Int);
        if (unit == null)
        {
            return false;
        }

        var registeredPosition = GetUnitPosition(unit);
        if (registeredPosition.HasValue)
        {
            position = registeredPosition.Value;
            return true;
        }

        position = WorldToGridInt(unit.transform.position);
        return IsValidGridPosition(position);
    }

    private int CountTilesInAttackCircle(float attackRange)
    {
        int count = 0;
        int intRange = Mathf.CeilToInt(Mathf.Max(0f, attackRange));
        float sqrRange = attackRange * attackRange;
        for (int x = -intRange; x <= intRange; x++)
        {
            for (int y = -intRange; y <= intRange; y++)
            {
                if (x * x + y * y <= sqrRange)
                {
                    count++;
                }
            }
        }

        return Mathf.Max(1, count);
    }

    private float CalculateFieldCenterScore(Vector3Int position)
    {
        float centerX = (gridSize.x - 1) * 0.5f;
        float centerY = (gridSize.y - 1) * 0.5f;
        float maxDistance = Mathf.Sqrt(centerX * centerX + centerY * centerY);
        if (maxDistance <= 0f)
        {
            return 1f;
        }

        float distance = Vector2.Distance(new Vector2(position.x, position.y), new Vector2(centerX, centerY));
        return 1f - Mathf.Clamp01(distance / maxDistance);
    }

    private int CountCoveredMonsterPathTiles(Vector3Int position, HashSet<Vector3Int> pathTiles, float attackRange)
    {
        if (pathTiles == null || pathTiles.Count == 0)
        {
            return 0;
        }

        int covered = 0;
        int intRange = Mathf.CeilToInt(Mathf.Max(0f, attackRange));
        float sqrRange = attackRange * attackRange;
        for (int x = -intRange; x <= intRange; x++)
        {
            for (int y = -intRange; y <= intRange; y++)
            {
                if (x * x + y * y > sqrRange)
                {
                    continue;
                }

                if (pathTiles.Contains(position + new Vector3Int(x, y, 0)))
                {
                    covered++;
                }
            }
        }

        return covered;
    }

    private bool IsOuterRingCell(Vector3Int cell)
    {
        return cell.x <= 0 ||
               cell.y <= 0 ||
               cell.x >= gridSize.x - 1 ||
               cell.y >= gridSize.y - 1;
    }

    private bool IsNearFieldEdgeCell(Vector3Int cell)
    {
        return cell.x <= 1 ||
               cell.y <= 1 ||
               cell.x >= gridSize.x - 2 ||
               cell.y >= gridSize.y - 2;
    }

    private bool IsStrictInteriorRangedCell(Vector3Int cell)
    {
        return !IsNearFieldEdgeCell(cell) && !IsBorderGapCell(cell);
    }

    private bool IsBorderGapCell(Vector3Int cell)
    {
        foreach (var gap in GetBorderGapCells())
        {
            if (gap.x == cell.x && gap.y == cell.y)
            {
                return true;
            }
        }

        return false;
    }

    private float CalculateScore(AIContext context, List<Consideration> considerations, Vector3Int tilePos)
    {
        float totalScore = 0;
        float weightSum = 0;

        // 디버그용 세부 점수 저장
        var breakdown = new DebugScoreBreakdown();

        foreach (var consideration in considerations)
        {
            float score = consideration.Score(context);
            float weightedScore = score * consideration.weight;
            totalScore += weightedScore;
            weightSum += consideration.weight;

            // 주요 고려사항들의 점수를 별도로 저장
            if (consideration is MeleePlacementConsideration)
            {
                breakdown.groundScore = score;
            }
            else if (consideration is OnMonsterPathConsideration)
            {
                breakdown.pathScore = score;
            }
            else if (consideration is MeleeProtectsRangedConsideration)
            {
                breakdown.allyScore = score;
            }
        }

        float finalScore = (weightSum > 0) ? totalScore / weightSum : 0;
        breakdown.totalScore = finalScore;

        // 디버그 정보 저장
        _debugScoreBreakdowns[tilePos] = breakdown;



        return finalScore;
    }

    #endregion

    public async void CheckForCombination()
    {
        var runner = playerManager != null ? playerManager.Runner : null;
        if (runner != null
            && runner.IsRunning
            && (playerManager == null || playerManager.Object == null || !playerManager.Object.HasStateAuthority))
        {
            return;
        }

        var combinableGroup = placedUnits
            .Where(kvp => kvp.Value != null && kvp.Value.Data != null && kvp.Value.starLevel < 3)
            .GroupBy(kvp => kvp.Value)
            .Select(group => new
            {
                Unit = group.Key,
                Position = group
                    .Select(kvp => kvp.Key)
                    .OrderBy(cell => cell.x)
                    .ThenBy(cell => cell.y)
                    .ThenBy(cell => cell.z)
                    .First()
            })
            .OrderBy(entry => entry.Position.x)
            .ThenBy(entry => entry.Position.y)
            .ThenBy(entry => entry.Position.z)
            .GroupBy(entry => new { entry.Unit.Data.unitName, entry.Unit.starLevel })
            .Where(g => g.Count() >= 3)
            .FirstOrDefault();

        if (combinableGroup != null)
        {
            List<Unit> unitsToCombine = combinableGroup.Select(entry => entry.Unit).Take(3).ToList();
            bool canDespawn = runner != null
                && runner.IsRunning
                && playerManager != null
                && playerManager.Object != null
                && playerManager.Object.HasStateAuthority;

            for (int i = 0; i < 2; i++)
            {
                var unit = unitsToCombine[i];
                var unitPosition = GetUnitPosition(unit);
                if (unitPosition.HasValue)
                {
                    BroadcastUnitUnregistered(unit, unitPosition.Value, GetUnitDataRegistrationKey(unit), unit != null ? unit.starLevel : 0);
                }

                UnitDied(unit);

                if (canDespawn && unit != null && unit.TryGetComponent<NetworkObject>(out var no))
                {
                    runner.Despawn(no);
                }
                else if (unit != null)
                {
                    Destroy(unit.gameObject);
                }
            }
            BroadcastAuthoritativeUnitRoster("CheckForCombination.RemoveConsumed");

            Unit baseUnit = unitsToCombine[2];
            await baseUnit.Upgrade();
            ReplaceUnitPrefab(baseUnit);
            CheckForCombination();
        }
    }

    private async void ReplaceUnitPrefab(Unit unitToReplace)
    {
        // 현재 위치를 먼저 저장 (UnitDied 전에)
        if (!placedUnits.ContainsValue(unitToReplace))
        {
            // Debug.LogError("[FieldManager] ReplaceUnitPrefab: 유닛이 placedUnits에 없습니다.");
            return;
        }
        Vector3Int currentPos = placedUnits.First(kvp => kvp.Value == unitToReplace).Key;
        UnitData unitData = unitToReplace.Data;
        int newStarLevel = unitToReplace.starLevel;
        int retiredStarLevel = Mathf.Max(1, newStarLevel - 1);
        var runner = playerManager != null ? playerManager.Runner : null;
        if (runner != null
            && runner.IsRunning
            && (playerManager == null || playerManager.Object == null || !playerManager.Object.HasStateAuthority))
        {
            // Debug.LogWarning("[FieldManager] ReplaceUnitPrefab ignored: no state authority.");
            return;
        }

        // 기존 유닛 제거
        BroadcastUnitUnregistered(unitToReplace, currentPos, GetUnitDataRegistrationKey(unitToReplace), retiredStarLevel);
        UnitDied(unitToReplace);
        if (runner != null
            && runner.IsRunning
            && playerManager != null
            && playerManager.Object != null
            && playerManager.Object.HasStateAuthority
            && unitToReplace != null
            && unitToReplace.TryGetComponent<NetworkObject>(out var oldNO))
        {
            runner.Despawn(oldNO);
        }
        else
        {
            Destroy(unitToReplace.gameObject);
        }

        // 새 유닛 강제 배치 (IsUnitAt 체크 없이 직접 배치)
        if (unitData == null || unitData.prefabsByStarLevel == null || unitData.prefabsByStarLevel.Length == 0)
        {
            // Debug.LogError("[FieldManager] ReplaceUnitPrefab: UnitData 또는 프리팹이 없습니다.");
            return;
        }
        if (newStarLevel < 1 || newStarLevel > unitData.prefabsByStarLevel.Length)
        {
            // Debug.LogError($"[FieldManager] ReplaceUnitPrefab: 잘못된 성급({newStarLevel})");
            return;
        }

        string prefabKey = unitData.prefabsByStarLevel[newStarLevel - 1];
        if (string.IsNullOrEmpty(prefabKey))
        {
            // Debug.LogError($"[FieldManager] ReplaceUnitPrefab: 프리팹 키가 비어있습니다.");
            return;
        }

        var prefab = await AssetLoader.LoadAssetAsync<GameObject>(prefabKey);
        if (prefab == null)
        {
            // Debug.LogError($"[FieldManager] ReplaceUnitPrefab: 프리팹 로드 실패 ({prefabKey})");
            return;
        }

        Vector3 worldPos = GridToWorld(currentPos, checkForWall: true);
        GameObject unitGO = null;
        NetworkObject spawnedNO = null;
        if (runner != null && runner.IsRunning && prefab.TryGetComponent<NetworkObject>(out var networkPrefab))
        {
            if (playerManager == null || playerManager.Object == null || !playerManager.Object.HasStateAuthority)
            {
                return;
            }

            spawnedNO = runner.Spawn(networkPrefab, worldPos, Quaternion.identity, playerManager.Object.InputAuthority);
            if (spawnedNO == null)
            {
                // Debug.LogError($"[FieldManager] Runner.Spawn failed: {prefab.name} (Player={playerManager?.playerId})");
                return;
            }

            retiredNetworkUnitIds.Remove(spawnedNO.Id.Raw);
            unitGO = spawnedNO.gameObject;
            if (unitParent != null)
            {
                unitGO.transform.SetParent(unitParent, true);
            }

            playerManager.RPC_RegisterUnitAt(spawnedNO.Id, currentPos.x, currentPos.y, unitData.name, newStarLevel);
        }
        else
        {
            unitGO = Instantiate(prefab, worldPos, Quaternion.identity, unitParent);
        }

        // CreateUnitAt과 동일한 초기 스폰 보정 컴포넌트 부착 (이미 존재하면 생성하지 않음)
        var orientationFixer = unitGO.GetComponent<UnitOrientationFixer>();
        if (orientationFixer == null)
        {
            orientationFixer = unitGO.AddComponent<UnitOrientationFixer>();
            orientationFixer.rigRootName = "Armature";
            orientationFixer.rigLocalEulerTarget = new Vector3(-90f, 180f, 0f);
            orientationFixer.faceCameraOnSpawn = true;
            orientationFixer.enforceEveryLateUpdate = true;
            orientationFixer.targetCamera = playerCamera;
            orientationFixer.yawOffsetDeg = 180f;
        }

        Unit newUnit = unitGO.GetComponent<Unit>();
        if (newUnit == null)
        {
            // Debug.LogError($"[FieldManager] ReplaceUnitPrefab: 생성된 프리팹에 Unit 컴포넌트 없음");
            if (spawnedNO != null
                && runner != null
                && runner.IsRunning
                && playerManager != null
                && playerManager.Object != null
                && playerManager.Object.HasStateAuthority)
            {
                runner.Despawn(spawnedNO);
            }
            else
            {
                Destroy(unitGO);
            }
            return;
        }

        // 강제로 위치에 배치 (중복 체크 없이)
        AttachStatusBar(unitGO, newUnit.SetStatusBar);
        await newUnit.Initialize(unitData, newStarLevel, playerManager);
        placedUnits[currentPos] = newUnit;
        SyncUnitPlacementIdentity(newUnit, currentPos);
        BroadcastAuthoritativeUnitRoster("ReplaceUnitPrefab");
    }

    #endregion

    #region 유닛 상세 정보 패널 및 드래그 앤 드롭

    /// <summary>
    /// [3D] 마우스 위치를 3D 월드 좌표로 변환합니다. Ground 평면과의 교차점을 사용합니다.
    /// </summary>
    private Vector3 GetMouseWorldPosition()
    {
        if (ground3D == null) return Vector3.zero;
        Ray ray = playerCamera.ScreenPointToRay(MdfInput.PointerPosition);
        Plane groundPlane = new Plane(Vector3.up, gridOrigin);
        if (groundPlane.Raycast(ray, out float enter))
        {
            return ray.GetPoint(enter);
        }
        return Vector3.zero;
    }

    /// <summary>
    /// 마우스 아래의 유닛을 찾습니다. 3D Raycast를 사용하고, 실패 시 스크린 거리 근사치를 사용합니다.
    /// </summary>
    private Unit GetUnitUnderMouse()
    {
        if (playerCamera == null) return null;

        Vector2 mouseScreen = MdfInput.PointerPosition;
        Ray ray = playerCamera.ScreenPointToRay(mouseScreen);
        RaycastHit[] hits = Physics.RaycastAll(ray, 1000f);
        if (hits != null && hits.Length > 0)
        {
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var hit in hits)
            {
                var unit3D = hit.collider.GetComponentInParent<Unit>();
                if (unit3D == null || !placedUnits.ContainsValue(unit3D)) continue;

                if (TryGetUnitScreenRect(unit3D, out Rect rect, out _))
                {
                    if (rect.Contains(mouseScreen))
                    {
                        return unit3D;
                    }
                }
                else
                {
                    return unit3D;
                }
            }
        }

        Unit screenHit = GetUnitFromScreenBounds(mouseScreen);
        if (screenHit != null)
        {
            return screenHit;
        }

        if (useWorldDistanceFallback && clickPickMaxWorldDistance > 0f && placedUnits.Count > 0 && ground3D != null)
        {
            Vector3 mouseWorld = GetMouseWorldPosition();
            float thresholdSq = clickPickMaxWorldDistance * clickPickMaxWorldDistance;
            Unit bestUnit = null;
            float bestDistSq = thresholdSq;

            foreach (var unit in placedUnits.Values)
            {
                if (unit == null) continue;
                Vector3 unitPos = unit.transform.position;
                Vector2 delta = new Vector2(unitPos.x - mouseWorld.x, unitPos.z - mouseWorld.z);
                float distSq = delta.sqrMagnitude;
                if (distSq <= bestDistSq)
                {
                    bestDistSq = distSq;
                    bestUnit = unit;
                }
            }

            if (bestUnit != null)
            {
                return bestUnit;
            }
        }

        return null;
    }

    private Unit GetUnitFromScreenBounds(Vector2 mouseScreen)
    {
        if (placedUnits.Count == 0) return null;

        Unit bestUnit = null;
        float bestDepth = float.MaxValue;

        foreach (var unit in placedUnits.Values)
        {
            if (unit == null) continue;
            if (!TryGetUnitScreenRect(unit, out Rect rect, out float depth)) continue;
            if (!rect.Contains(mouseScreen)) continue;

            if (depth < bestDepth)
            {
                bestDepth = depth;
                bestUnit = unit;
            }
        }

        return bestUnit;
    }

    private bool TryGetUnitScreenRect(Unit unit, out Rect rect, out float depth)
    {
        rect = default;
        depth = float.MaxValue;
        if (playerCamera == null) return false;
        if (!TryGetUnitBounds(unit, out Bounds bounds)) return false;

        Vector3 center = bounds.center;
        Vector3 extents = bounds.extents;
        float minX = float.PositiveInfinity;
        float maxX = float.NegativeInfinity;
        float minY = float.PositiveInfinity;
        float maxY = float.NegativeInfinity;
        bool anyInFront = false;

        anyInFront |= AccumulateScreenRect(center + new Vector3(-extents.x, -extents.y, -extents.z), ref minX, ref maxX, ref minY, ref maxY, ref depth);
        anyInFront |= AccumulateScreenRect(center + new Vector3(-extents.x, -extents.y, extents.z), ref minX, ref maxX, ref minY, ref maxY, ref depth);
        anyInFront |= AccumulateScreenRect(center + new Vector3(-extents.x, extents.y, -extents.z), ref minX, ref maxX, ref minY, ref maxY, ref depth);
        anyInFront |= AccumulateScreenRect(center + new Vector3(-extents.x, extents.y, extents.z), ref minX, ref maxX, ref minY, ref maxY, ref depth);
        anyInFront |= AccumulateScreenRect(center + new Vector3(extents.x, -extents.y, -extents.z), ref minX, ref maxX, ref minY, ref maxY, ref depth);
        anyInFront |= AccumulateScreenRect(center + new Vector3(extents.x, -extents.y, extents.z), ref minX, ref maxX, ref minY, ref maxY, ref depth);
        anyInFront |= AccumulateScreenRect(center + new Vector3(extents.x, extents.y, -extents.z), ref minX, ref maxX, ref minY, ref maxY, ref depth);
        anyInFront |= AccumulateScreenRect(center + new Vector3(extents.x, extents.y, extents.z), ref minX, ref maxX, ref minY, ref maxY, ref depth);

        if (!anyInFront) return false;

        rect = Rect.MinMaxRect(minX, minY, maxX, maxY);
        return true;
    }

    private bool AccumulateScreenRect(
        Vector3 worldPoint,
        ref float minX,
        ref float maxX,
        ref float minY,
        ref float maxY,
        ref float minDepth)
    {
        Vector3 screen = playerCamera.WorldToScreenPoint(worldPoint);
        if (screen.z <= 0f) return false;

        if (screen.x < minX) minX = screen.x;
        if (screen.x > maxX) maxX = screen.x;
        if (screen.y < minY) minY = screen.y;
        if (screen.y > maxY) maxY = screen.y;
        if (screen.z < minDepth) minDepth = screen.z;
        return true;
    }

    private bool TryGetUnitBounds(Unit unit, out Bounds bounds)
    {
        bounds = default;
        if (unit == null) return false;

        bool hasBounds = false;
        Renderer[] renderers = unit.GetComponentsInChildren<Renderer>();
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || !renderer.enabled) continue;
            if (!hasBounds)
            {
                bounds = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        if (hasBounds) return true;

        Collider[] colliders = unit.GetComponentsInChildren<Collider>();
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];
            if (collider == null || !collider.enabled) continue;
            if (!hasBounds)
            {
                bounds = collider.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(collider.bounds);
            }
        }

        return hasBounds;
    }

    private void HandleUnitDragAndDrop()
    {
        if (GameManagers.Instance == null)
        {
            // 아직 GameManagers가 준비되지 않았으면 아무것도 하지 않고 함수를 종료합니다.
            return;
        }
        var gameState = GameManagers.Instance.GetGameState();
        bool isActiveGameState = gameState == GameManagers.GameState.Prepare 
            || gameState == GameManagers.GameState.Battle1 
            || gameState == GameManagers.GameState.Battle2;
        if (!isActiveGameState) return;

        if (playerCamera == null)
        {
            // Debug.LogWarning("[FieldManager] playerCamera is null!");
            return;
        }

        // [3D] 초기화 확인 - Host Migration 후 재할당 필요할 수 있음
        if (ground3D == null)
        {
            // Debug.Log($"[FieldManager] ground3D null 감지, fallback 시도... playerManager={playerManager?.name}");
            
            // 방법 1: 부모 계층에서 Ground 찾기
            var parentTransform = transform.parent;
            // Debug.Log($"[FieldManager] parentTransform={parentTransform?.name}");
            
            if (parentTransform != null)
            {
                var groundTransform = parentTransform.Find("Ground") ?? parentTransform.Find("Field");
                if (groundTransform != null)
                {
                    ground3D = groundTransform.gameObject;
                    // Debug.Log($"[FieldManager] ground3D 재할당 완료: {ground3D.name} (방법1: parent.Find)");
                }
                else
                {
                    var meshRenderer = parentTransform.GetComponentInChildren<MeshRenderer>(true);
                    if (meshRenderer != null)
                    {
                        ground3D = meshRenderer.gameObject;
                        // Debug.Log($"[FieldManager] ground3D 재할당 완료: {ground3D.name} (방법2: MeshRenderer)");
                    }
                }
            }
            
            // 방법 2: playerManager.astarGrid에서 찾기
            if (ground3D == null && playerManager != null && playerManager.astarGrid != null)
            {
                var gridParent = playerManager.astarGrid.transform.parent;
                // Debug.Log($"[FieldManager] astarGrid.parent={gridParent?.name}");
                
                if (gridParent != null)
                {
                    var groundTransform = gridParent.Find("Ground") ?? gridParent.Find("Field");
                    if (groundTransform != null)
                    {
                        ground3D = groundTransform.gameObject;
                        // Debug.Log($"[FieldManager] ground3D 재할당 완료: {ground3D.name} (방법3: astarGrid.parent)");
                    }
                    else
                    {
                        // 자식 전체 순회
                        foreach (Transform child in gridParent)
                        {
                            if (child.name.Contains("Ground") || child.name.Contains("Field"))
                            {
                                ground3D = child.gameObject;
                                // Debug.Log($"[FieldManager] ground3D 재할당 완료: {ground3D.name} (방법4: 자식 순회)");
                                break;
                            }
                        }
                    }
                }
            }
            
            // 방법 3: GameManagers.localPlayer에서 찾기
            if (ground3D == null && GameManagers.Instance != null)
            {
                var localPlayer = GameManagers.Instance.localPlayer;
                // Debug.Log($"[FieldManager] GameManagers.localPlayer={localPlayer?.name}");
                
                if (localPlayer != null && localPlayer.astarGrid != null)
                {
                    var gridParent = localPlayer.astarGrid.transform.parent;
                    if (gridParent != null)
                    {
                        var groundTransform = gridParent.Find("Ground") ?? gridParent.Find("Field");
                        if (groundTransform != null)
                        {
                            ground3D = groundTransform.gameObject;
                            // Debug.Log($"[FieldManager] ground3D 재할당 완료: {ground3D.name} (방법5: GameManagers)");
                        }
                    }
                }
            }
            
            // 방법 4: FindObjectOfType으로 AstarGrid 찾아서 parent에서 Ground 찾기
            if (ground3D == null)
            {
                // Debug.Log("[FieldManager] 방법6 시도: FindObjectOfType<AstarGrid>");
                var allGrids = UnityEngine.Object.FindObjectsOfType<AstarGrid>(true);
                // Debug.Log($"[FieldManager] 발견된 AstarGrid 수: {allGrids.Length}");
                
                foreach (var grid in allGrids)
                {
                    var gridParent = grid.transform.parent;
                    if (gridParent != null)
                    {
                        var groundTransform = gridParent.Find("Ground") ?? gridParent.Find("Field");
                        if (groundTransform != null)
                        {
                            ground3D = groundTransform.gameObject;
                            
                            // astarGrid도 복구
                            if (playerManager != null && playerManager.astarGrid == null)
                            {
                                playerManager.astarGrid = grid;
                                // Debug.Log($"[FieldManager] astarGrid도 재할당: {grid.name}");
                            }
                            
                            // Debug.Log($"[FieldManager] ground3D 재할당 완료: {ground3D.name} (방법6: FindObjectOfType)");
                            break;
                        }
                    }
                }
            }
            
            // 방법 5: 마지막으로 Ground 이름이 포함된 모든 오브젝트 찾기
            if (ground3D == null)
            {
                // Debug.Log("[FieldManager] 방법7 시도: GameObject.Find");
                var foundGround = GameObject.Find("Ground");
                if (foundGround != null)
                {
                    ground3D = foundGround;
                    // Debug.Log($"[FieldManager] ground3D 재할당 완료: {ground3D.name} (방법7: GameObject.Find)");
                }
            }
            
            // 그래도 못 찾으면 에러
            if (ground3D == null)
            {
                // Debug.LogWarning("[FieldManager] ground3D is null - 모든 fallback 실패!");
                return;
            }
        }

        // [3D Migration] 마우스 월드 좌표 및 그리드 좌표 계산
        Vector3 mouseWorldPos = GetMouseWorldPosition();
        Vector3Int gridPos = WorldToGridInt(mouseWorldPos);

        // 마우스 버튼을 눌렀을 때
        if (MdfInput.PrimaryPointerWasPressedThisFrame())
        {
            // 셀 기반이 아니라 실제 유닛 콜라이더를 클릭해야 드래그 시작
            Unit clickedUnit = GetUnitUnderMouse();
            bool pointerOverUI = MdfInput.IsPointerOverUI();
            if (pointerOverUI && clickedUnit != null && ShouldAllowUnitDragThroughPrepareToolkit())
            {
                pointerOverUI = false;
            }

            // 패널이 열려있는 상태에서
            if (unitDetailPanelInstance != null && unitDetailPanelInstance.activeSelf)
            {
                // 표시된 유닛을 다시 클릭한 경우 -> 패널 닫고 아무것도 안 함
                if (!pointerOverUI && clickedUnit != null && clickedUnit == unitDisplayedInPanel)
                {
                    UIManagers.Instance.ReturnUIElement("UI_Pnl_UnitDetail");
                    unitDetailPanelInstance = null;
                    unitDisplayedInPanel = null;
                    HideUnitSellPanel();
                    HideWallRemovePanel();
                    selectedUnit = null; // 모든 상태 초기화
                    return;
                }

                // UI가 아닌 다른 곳을 클릭한 경우 -> 패널 닫고 클릭한 대상에 대한 처리 계속
                if (!pointerOverUI)
                {
                    UIManagers.Instance.ReturnUIElement("UI_Pnl_UnitDetail");
                    unitDetailPanelInstance = null;
                    unitDisplayedInPanel = null;
                    HideUnitSellPanel();
                    HideWallRemovePanel();
                }
            }

            // 이제 클릭한 대상에 대한 처리 (드래그 시작 또는 새 패널 열기 준비)
            // UI 위를 클릭한 경우에는 새 선택/드래그를 시작하지 않습니다.
            if (pointerOverUI)
            {
                return;
            }
            if (clickedUnit != null)
            {
                selectedUnit = clickedUnit;
                mouseDownTimer = 0f;
                isDragStarted = false;
                // [3D Migration] 유닛의 현재 위치를 그리드 좌표로 변환
                originalUnitPosition = WorldToGridInt(selectedUnit.transform.position);
                // 3D 드래그를 위한 XZ 오프셋 및 기준 Y 저장
                dragBaseY = selectedUnit.transform.position.y;
                offsetXZ = new Vector2(
                    selectedUnit.transform.position.x - mouseWorldPos.x,
                    selectedUnit.transform.position.z - mouseWorldPos.z
                );

                // NetworkTransform 참조 저장 (드래그 시작 시 비활성화할 예정)
                selectedUnitNetworkTransform = selectedUnit.GetComponent<Fusion.NetworkTransform>();
            }
            else
            {
                // 유닛이 없는 곳을 클릭함 -> 벽만 있는지 확인
                Vector3Int clickedGridPos = WorldToGridInt(mouseWorldPos);
                var clickedWall = GetWallAt(clickedGridPos);
                if (clickedWall != null)
                {
                    // 벽만 있는 경우: 벽 제거 패널만 표시
                    ShowWallRemovePanel(clickedWall, clickedGridPos);
                }
                else
                {
                    // 유닛도 벽도 없는 빈 공간: 벽 제거 패널 숨김
                    HideWallRemovePanel();
                }
            }
        }

        // 마우스 버튼을 누르고 있을 때
        if (MdfInput.PrimaryPointerIsPressed() && selectedUnit != null)
        {
            // 아직 드래그가 시작되지 않았다면, 타이머를 확인하여 드래그 상태로 전환할지 결정합니다.
            if (!isDragStarted)
            {
                mouseDownTimer += Time.deltaTime;
                // 준비 단계일 때만 드래그를 시작할 수 있습니다.
                if (mouseDownTimer >= dragDelay && gameState == GameManagers.GameState.Prepare)
                {
                    // 드래그 시작
                    isDragStarted = true;
                    // offset과 originalUnitPosition은 이미 GetMouseButtonDown에서 설정되었습니다.

                    // ✅ NetworkTransform 비활성화 (로컬 드래그를 위해)
                    if (selectedUnitNetworkTransform != null)
                    {
                        selectedUnitNetworkTransform.enabled = false;
                        string selName = (selectedUnit != null && selectedUnit.Data != null) ? selectedUnit.Data.unitName : (selectedUnit != null ? selectedUnit.name : "Unit");
                    }

                    // 드래그가 시작되면 열려있던 상세 정보 패널을 닫음
                    if (unitDetailPanelInstance != null && unitDetailPanelInstance.activeSelf)
                    {
                        UIManagers.Instance.ReturnUIElement("UI_Pnl_UnitDetail");
                        unitDetailPanelInstance = null;
                        unitDisplayedInPanel = null;
                        HideUnitSellPanel();
                    }
                }
            }
            else
            {
                // 드래그 중: 유닛이 마우스를 부드럽게 따라감 (XZ는 마우스, Y는 들어올림)
                float targetX = mouseWorldPos.x + offsetXZ.x;
                float targetZ = mouseWorldPos.z + offsetXZ.y;
                float targetY = dragBaseY + dragLiftHeight;
                Vector3 targetPos = new Vector3(targetX, targetY, targetZ);

                // NetworkTransform이 비활성화되어 있으므로 직접 transform.position 변경 가능
                selectedUnit.transform.position = Vector3.MoveTowards(
                    selectedUnit.transform.position,
                    targetPos,
                    dragFollowSpeed * Time.deltaTime
                );
            }
        }

        // 마우스 버튼을 뗐을 때
        if (MdfInput.PrimaryPointerWasReleasedThisFrame() && selectedUnit != null)
        {
            if (isDragStarted)
            {
                // 드래그 종료: 화면상 마우스와 가장 겹쳐 보이는 셀을 최종 선택
                Vector3Int bestGrid = GetBestGridUnderMouse();
                bestGrid.x = Mathf.Clamp(bestGrid.x, 0, gridSize.x - 1);
                bestGrid.y = Mathf.Clamp(bestGrid.y, 0, gridSize.y - 1);

                if (placementManager.IsPositionValidForPlacement(bestGrid, selectedUnit.Data))
                {
                    // 네트워크 준비 상태 확인 (Runner가 실행 중이면 localPlayer와 InputAuthority 확인)
                    bool canSend = IsNetworkReadyAndHasInputAuthority();

                    // 드래그 중 비활성화한 NetworkTransform을 성공 드랍 시 항상 복구
                    if (selectedUnitNetworkTransform != null && !selectedUnitNetworkTransform.enabled)
                    {
                        selectedUnitNetworkTransform.enabled = true;
                    }

                    if (canSend)
                    {
                        var command = new MoveUnitCommand(playerManager.playerId, originalUnitPosition, bestGrid);
                        GameManagers.Instance.CommandProcessor.RequestCommandExecution(command);
                    }
                    else
                    {
                        // 네트워크 준비가 안 되었으면 원위치 복귀
                        Vector3 originalWorldPos = GridToWorld(originalUnitPosition, checkForWall: true);
                        SnapbackSelectedUnit(originalWorldPos);
                    }
                }
                else
                {
                    Unit target = GetUnitAt(bestGrid);
                    if (target != null)
                    {
                        bool destWallForSelected = HasWallAt(bestGrid);
                        bool destWallForTarget = HasWallAt(originalUnitPosition);
                        bool invalidForSelected = selectedUnit.Data != null && selectedUnit.Data.unitType == UnitType.Melee && destWallForSelected;
                        bool invalidForTarget = target.Data != null && target.Data.unitType == UnitType.Melee && destWallForTarget;
                        if (!invalidForSelected && !invalidForTarget)
                        {
                            // 네트워크 준비 상태 확인
                            bool canSend = IsNetworkReadyAndHasInputAuthority();

                            // 성공 드랍 경로에서도 NetworkTransform 복구
                            if (selectedUnitNetworkTransform != null && !selectedUnitNetworkTransform.enabled)
                            {
                                selectedUnitNetworkTransform.enabled = true;
                            }

                            if (canSend)
                            {
                                var swapCmd = new SwapUnitCommand(playerManager.playerId, originalUnitPosition, bestGrid);
                                GameManagers.Instance.CommandProcessor.RequestCommandExecution(swapCmd);
                            }
                            else
                            {
                                
                                // 네트워크 준비가 안 되었으면 원위치 복귀
                                Vector3 originalWorldPos = GridToWorld(originalUnitPosition, checkForWall: true);
                                SnapbackSelectedUnit(originalWorldPos);
                            }
                        }
                        else
                        {
                            
                            Vector3 originalWorldPos = GridToWorld(originalUnitPosition, checkForWall: true);

                            // ✅ NetworkTransform 처리
                            SnapbackSelectedUnit(originalWorldPos);
                        }
                    }
                    else
                    {
                        
                        Vector3 originalWorldPos = GridToWorld(originalUnitPosition, checkForWall: true);

                        // ✅ NetworkTransform 처리
                        SnapbackSelectedUnit(originalWorldPos);
                    }
                }
            }
            else
            {
                // 짧은 클릭: 이 FieldManager 소유 유닛만 스냅백. (교차 플레이어 유닛 보호)
                if (placedUnits.ContainsValue(selectedUnit))
                {
                    Vector3 originalWorldPos = GridToWorld(originalUnitPosition, checkForWall: true);
                    SnapbackSelectedUnit(originalWorldPos);
                }
                // 죽은 유닛인 경우 UI 표시 건너뛰기
                if (selectedUnit.IsDead)
                {
                    selectedUnit = null;
                    selectedUnitNetworkTransform = null;
                    isDragStarted = false;
                    return;
                }
                ShowUnitDetailPanel(selectedUnit);
                ShowUnitSellPanel(selectedUnit);
                // 유닛이 서 있는 그리드에 벽이 있으면 벽 제거 패널도 표시
                var wallAtUnitPos = GetWallAt(originalUnitPosition);
                if (wallAtUnitPos != null)
                {
                    ShowWallRemovePanel(wallAtUnitPos, originalUnitPosition);
                }
            }

            // 상태 초기화
            selectedUnit = null;
            selectedUnitNetworkTransform = null;
            isDragStarted = false;
        }
    }

    private bool ShouldAllowUnitDragThroughPrepareToolkit()
    {
        var gm = GameManagers.Instance;
        if (gm == null || gm.GetGameState() != GameManagers.GameState.Prepare || gm.IsSequenceTransitioning)
        {
            return false;
        }

        if (!GamePrepareUIToolkitController.IsToolkitActive)
        {
            return false;
        }

        return !GamePrepareUIToolkitController.IsPointerOverBlockingElement(MdfInput.PointerPosition);
    }

    private async void ShowUnitDetailPanel(Unit unit)
    {
        // 죽은 유닛인 경우 패널을 표시하지 않음
        if (unit == null || unit.IsDead) return;

        // 패널 인스턴스가 없으면 UIManagers를 통해 가져옵니다.
        // 이는 씬에 미리 배치된 패널을 찾거나, 없을 경우 새로 생성하는 역할을 합니다.
        if (unitDetailPanelInstance == null)
        {
            unitDetailPanelInstance = await UIManagers.Instance.GetUIElement("UI_Pnl_UnitDetail");
        }

        if (unitDetailPanelInstance != null)
        {
            var controller = unitDetailPanelInstance.GetComponent<UnitDetailPanelController>();
            if (controller != null)
            {
                controller.DisplayUnitInfo(unit);
                // 패널의 위치는 프리팹/씬에 설정된 고정 위치를 사용하므로, 여기서 위치를 변경하지 않습니다.
                unitDetailPanelInstance.SetActive(true);
                unitDisplayedInPanel = unit;
            }
        }
    }

    private async void ShowUnitSellPanel(Unit unit)
    {
        if (unit == null || UIManagers.Instance == null) return;

        // 전투 시퀀스에서는 판매 패널을 표시하지 않음
        var gm = GameManagers.Instance;
        if (gm != null && (gm.GetGameState() != GameManagers.GameState.Prepare || gm.IsSequenceTransitioning))
        {
            return;
        }

        if (unitSellPanelInstance == null)
        {
            unitSellPanelInstance = await UIManagers.Instance.GetUIElement("UI_Can_UnitSell");
        }

        if (unitSellPanelInstance != null)
        {
            unitSellPanelInstance.transform.SetParent(unit.transform, false);

            var rootCanvas = unitSellPanelInstance.GetComponent<Canvas>();
            if (rootCanvas != null)
            {
                rootCanvas.renderMode = RenderMode.WorldSpace;
                rootCanvas.worldCamera = playerCamera;
            }

            var controller = unitSellPanelInstance.GetComponentInChildren<UnitSellPanelController>(true);
            if (controller != null)
            {
                var controllerCanvas = controller.GetComponent<Canvas>();
                if (controllerCanvas != null && controllerCanvas != rootCanvas)
                {
                    controllerCanvas.renderMode = RenderMode.WorldSpace;
                    controllerCanvas.worldCamera = playerCamera;
                }
                controller.Bind(unit, this);
                unitSellPanelInstance.SetActive(true);
                unitDisplayedInSellPanel = unit;
            }
        }
    }

    private void HideUnitSellPanel()
    {
        if (unitSellPanelInstance != null && UIManagers.Instance != null)
        {
            UIManagers.Instance.ReturnUIElement("UI_Can_UnitSell");
        }
        unitSellPanelInstance = null;
        unitDisplayedInSellPanel = null;
    }

    /// <summary>
    /// 벽 제거 패널을 표시합니다.
    /// </summary>
    private async void ShowWallRemovePanel(DestructibleWall wall, Vector3Int gridPosition)
    {
        if (wall == null || UIManagers.Instance == null) return;

        // 전투 시퀀스에서는 벽 제거 패널을 표시하지 않음
        var gm = GameManagers.Instance;
        if (gm != null && (gm.GetGameState() != GameManagers.GameState.Prepare || gm.IsSequenceTransitioning))
        {
            return;
        }

        if (wallRemovePanelInstance == null)
        {
            wallRemovePanelInstance = await UIManagers.Instance.GetUIElement("UI_Can_WallRemove");
        }

        if (wallRemovePanelInstance != null)
        {
            // 벽의 자식이 아닌 FieldManager의 자식으로 설정하여 렌더링 순서 문제 해결
            wallRemovePanelInstance.transform.SetParent(transform, false);

            var rootCanvas = wallRemovePanelInstance.GetComponent<Canvas>();
            if (rootCanvas != null)
            {
                rootCanvas.renderMode = RenderMode.WorldSpace;
                rootCanvas.worldCamera = playerCamera;
                rootCanvas.overrideSorting = true;
                rootCanvas.sortingOrder = 300;
            }

            var controller = wallRemovePanelInstance.GetComponentInChildren<WallRemovePanelController>(true);
            if (controller != null)
            {
                var controllerCanvas = controller.GetComponent<Canvas>();
                if (controllerCanvas != null && controllerCanvas != rootCanvas)
                {
                    controllerCanvas.renderMode = RenderMode.WorldSpace;
                    controllerCanvas.worldCamera = playerCamera;
                    controllerCanvas.overrideSorting = true;
                    controllerCanvas.sortingOrder = 300;
                }
                controller.Bind(wall, gridPosition, this);
                wallRemovePanelInstance.SetActive(true);
                wallDisplayedInRemovePanel = wall;
            }
        }
    }

    /// <summary>
    /// 모든 선택 관련 UI 패널을 숨깁니다. (유닛 디테일, 유닛 판매, 벽 제거)
    /// </summary>
    public void HideAllSelectionPanels()
    {
        // 유닛 디테일 패널 숨기기
        if (unitDetailPanelInstance != null && UIManagers.Instance != null)
        {
            UIManagers.Instance.ReturnUIElement("UI_Pnl_UnitDetail");
            unitDetailPanelInstance = null;
            unitDisplayedInPanel = null;
        }

        // 유닛 판매 패널 숨기기
        HideUnitSellPanel();

        // 벽 제거 패널 숨기기
        HideWallRemovePanel();
    }

    /// <summary>
    /// 벽 제거 패널을 숨깁니다.
    /// </summary>
    private void HideWallRemovePanel()
    {
        if (wallRemovePanelInstance != null && UIManagers.Instance != null)
        {
            UIManagers.Instance.ReturnUIElement("UI_Can_WallRemove");
        }
        wallRemovePanelInstance = null;
        wallDisplayedInRemovePanel = null;
    }
    #endregion

    #region 범위 표시

    /// <summary>
    /// 지정된 유닛의 공격 및 스킬 범위를 원형으로 표시하고, 겹치는 경우 렌더링 순서를 조정합니다.
    /// </summary>
    public async void ShowRanges(Unit unit)
    {
        // 유닛이 없거나 죽은 경우 범위 표시하지 않음
        if (unit == null || unit.IsDead) return;

        ClearRanges();

        // 1. 공격 범위 값은 그대로 가져옵니다.
        float attackRange = unit.currentAttackRange;
        float skillRange = 0f;

        // --- [핵심 수정 부분] ---
        // 2. 스킬 데이터의 주소가 있는지 확인하고, AssetLoader를 통해 실제 SkillData를 로드합니다.
        if (unit.Data.skillsByStarLevel.Length >= unit.starLevel)
        {
            string skillKey = unit.Data.skillsByStarLevel[unit.starLevel - 1];
            if (!string.IsNullOrEmpty(skillKey))
            {
                SkillData currentSkill = await AssetLoader.LoadAssetAsync<SkillData>(skillKey);
                if (currentSkill != null)
                {
                    skillRange = currentSkill.range;
                }
            }
        }
        // --- [수정 끝] ---

        bool showAttack = attackRangeIndicatorPrefab != null && attackRange > 0;
        bool showSkill = skillRangeIndicatorPrefab != null && skillRange > 0;

        if (!showAttack && !showSkill) return;

        if (!showAttack && !showSkill) return;

        // 2. 범위가 동일할 경우 시각적 조정을 위해 공격 범위 약간 축소
        float attackDiameter = attackRange * 2f;
        float skillDiameter = skillRange * 2f;

        if (showAttack && showSkill && Mathf.Approximately(attackRange, skillRange))
        {
            attackDiameter *= 0.95f; // 공격 범위를 약간 줄여서 둘 다 보이게 함
        }

        // 3. 범위 인디케이터 생성 및 크기 설정
        Vector3 indicatorPos = unit.transform.position;
        indicatorPos.y = unit.transform.position.y + rangeIndicatorYOffset;
        SpriteRenderer attackRenderer = null;
        SpriteRenderer skillRenderer = null;
        if (showAttack)
        {
            attackRangeIndicatorInstance = Instantiate(attackRangeIndicatorPrefab, indicatorPos, Quaternion.Euler(90f, 0f, 0f), transform);
            attackRangeIndicatorInstance.transform.localScale = new Vector3(attackDiameter, attackDiameter, 1f);
            attackRenderer = attackRangeIndicatorInstance.GetComponent<SpriteRenderer>();
        }
        if (showSkill)
        {
            skillRangeIndicatorInstance = Instantiate(skillRangeIndicatorPrefab, indicatorPos, Quaternion.Euler(90f, 0f, 0f), transform);
            skillRangeIndicatorInstance.transform.localScale = new Vector3(skillDiameter, skillDiameter, 1f);
            skillRenderer = skillRangeIndicatorInstance.GetComponent<SpriteRenderer>();
        }

        // 4. 두 범위가 모두 표시될 때 렌더링 순서(Sorting Order) 조정
        if (attackRenderer != null)
        {
            attackRenderer.sortingOrder = rangeIndicatorSortingOrder;
        }
        if (skillRenderer != null)
        {
            skillRenderer.sortingOrder = rangeIndicatorSortingOrder;
        }

        if (attackRenderer != null && skillRenderer != null)
        {
            // 더 큰 범위를 뒤에, 작은 범위를 앞에 렌더링
            if (attackDiameter > skillDiameter)
            {
                attackRenderer.sortingOrder = rangeIndicatorSortingOrder;
                skillRenderer.sortingOrder = rangeIndicatorSortingOrder + 1;
            }
            else
            {
                skillRenderer.sortingOrder = rangeIndicatorSortingOrder;
                attackRenderer.sortingOrder = rangeIndicatorSortingOrder + 1;
            }
        }
    }

    /// <summary>
    /// 표시된 모든 범위를 제거합니다.
    /// </summary>
    public void ClearRanges()
    {
        if (attackRangeIndicatorInstance != null)
        {
            Destroy(attackRangeIndicatorInstance);
            attackRangeIndicatorInstance = null;
        }
        if (skillRangeIndicatorInstance != null)
        {
            Destroy(skillRangeIndicatorInstance);
            skillRangeIndicatorInstance = null;
        }
    }

    #endregion

    #region AI 디버그 시각화



    // 디버그 표시를 끄는 메서드 (배치 완료 후 호출)
    public void ClearDebugScores()
    {
        _showDebugScores = false;
        _debugTileScores.Clear();
        _debugScoreBreakdowns.Clear();
        _debugUnitData = null;
    }

    #endregion

    #region 폭주 모드

    /// <summary>
    /// 필드의 모든 유닛에 폭주 모드를 적용합니다.
    /// </summary>
    public void ApplyBerserkModeToAllUnits()
    {
        foreach (var unit in placedUnits.Values)
        {
            if (unit != null && !unit.IsDead)
            {
                unit.ApplyBerserkMode();
            }
        }
    }

    #endregion

    #region 그리드 시각화 (LineRenderer 기반 - 빌드에서도 보임)

    [Header("그리드 디버그 시각화")]
    [Tooltip("런타임에서 그리드 격자를 표시합니다.")]
    public bool showGridDebug = true;
    [Tooltip("그리드 라인 색상")]
    public Color gridLineColor = new Color(0.5f, 0.5f, 0.5f, 0.8f);
    [Tooltip("테두리 라인 색상 (영구 벽 위치)")]
    public Color borderLineColor = new Color(1f, 0.5f, 0f, 1f);
    [Tooltip("스폰/골 위치 표시 색상")]
    public Color spawnGoalColor = new Color(0f, 1f, 0f, 1f);
    [Tooltip("골 위치 표시 색상")]
    public Color goalColor = Color.red;
    [Tooltip("그리드 라인 Y 오프셋")]
    public float gridLineYOffset = 0.05f;
    [Tooltip("그리드 라인 두께")]
    public float gridLineWidth = 0.02f;

    private GameObject _gridLinesParent;
    private bool _gridLinesCreated = false;

    /// <summary>
    /// 그리드 라인을 생성합니다. Initialize 후에 호출됩니다.
    /// </summary>
    public void CreateGridLines()
    {
        if (_gridLinesCreated) return;
        if (!showGridDebug) return;

        // 기존 라인 삭제
        DestroyGridLines();

        // 부모 오브젝트 생성
        _gridLinesParent = new GameObject("GridLines_Debug");
        _gridLinesParent.transform.SetParent(transform);
        _gridLinesParent.transform.localPosition = Vector3.zero;

        float y = gridOrigin.y + gridLineYOffset;
        int centerX = gridSize.x / 2;
        int centerY = gridSize.y / 2;

        // 세로 라인 (X축)
        for (int x = 0; x <= gridSize.x; x++)
        {
            Vector3 start = new Vector3(gridOrigin.x + x * cellSize, y, gridOrigin.z);
            Vector3 end = new Vector3(gridOrigin.x + x * cellSize, y, gridOrigin.z + gridSize.y * cellSize);
            CreateLine($"VertLine_{x}", start, end, gridLineColor);
        }

        // 가로 라인 (Z축)
        for (int z = 0; z <= gridSize.y; z++)
        {
            Vector3 start = new Vector3(gridOrigin.x, y, gridOrigin.z + z * cellSize);
            Vector3 end = new Vector3(gridOrigin.x + gridSize.x * cellSize, y, gridOrigin.z + z * cellSize);
            CreateLine($"HorizLine_{z}", start, end, gridLineColor);
        }

        // 테두리 셀 표시
        for (int x = 0; x < gridSize.x; x++)
        {
            for (int z = 0; z < gridSize.y; z++)
            {
                bool isLeftEdge = (x == 0);
                bool isRightEdge = (x == gridSize.x - 1);
                bool isBottomEdge = (z == 0);
                bool isTopEdge = (z == gridSize.y - 1);

                if (!isLeftEdge && !isRightEdge && !isBottomEdge && !isTopEdge)
                    continue;

                bool isNorthGap = isTopEdge && (x == centerX);
                bool isSouthGap = isBottomEdge && (x == centerX);
                bool isEastGap = isRightEdge && (z == centerY);
                bool isWestGap = isLeftEdge && (z == centerY);

                Color cellColor = (isNorthGap || isSouthGap || isEastGap || isWestGap)
                    ? spawnGoalColor
                    : borderLineColor;

                CreateCellOutline(x, z, y, cellColor);
            }
        }

        // 골 위치 표시 (X 마크)
        float goalX = gridOrigin.x + (centerX + 0.5f) * cellSize;
        float goalZ = gridOrigin.z + (centerY + 0.5f) * cellSize;
        float goalSize = cellSize * 0.4f;
        CreateLine("Goal_X1",
            new Vector3(goalX - goalSize, y + 0.1f, goalZ - goalSize),
            new Vector3(goalX + goalSize, y + 0.1f, goalZ + goalSize),
            goalColor);
        CreateLine("Goal_X2",
            new Vector3(goalX - goalSize, y + 0.1f, goalZ + goalSize),
            new Vector3(goalX + goalSize, y + 0.1f, goalZ - goalSize),
            goalColor);

        _gridLinesCreated = true;
        // Debug.Log($"[FieldManager] 그리드 라인 생성 완료: {gridSize.x}x{gridSize.y}");
    }

    private void CreateLine(string name, Vector3 start, Vector3 end, Color color)
    {
        GameObject lineObj = new GameObject(name);
        lineObj.transform.SetParent(_gridLinesParent.transform);

        LineRenderer lr = lineObj.AddComponent<LineRenderer>();
        lr.positionCount = 2;
        lr.SetPosition(0, start);
        lr.SetPosition(1, end);
        lr.startWidth = gridLineWidth;
        lr.endWidth = gridLineWidth;
        lr.material = GetLineMaterial();
        lr.startColor = color;
        lr.endColor = color;
        lr.useWorldSpace = true;
    }

    private void CreateCellOutline(int x, int z, float y, Color color)
    {
        float padding = cellSize * 0.05f;
        float size = cellSize * 0.9f;

        Vector3 p0 = new Vector3(gridOrigin.x + x * cellSize + padding, y, gridOrigin.z + z * cellSize + padding);
        Vector3 p1 = new Vector3(p0.x + size, y, p0.z);
        Vector3 p2 = new Vector3(p0.x + size, y, p0.z + size);
        Vector3 p3 = new Vector3(p0.x, y, p0.z + size);

        GameObject lineObj = new GameObject($"Cell_{x}_{z}");
        lineObj.transform.SetParent(_gridLinesParent.transform);

        LineRenderer lr = lineObj.AddComponent<LineRenderer>();
        lr.positionCount = 5;
        lr.SetPosition(0, p0);
        lr.SetPosition(1, p1);
        lr.SetPosition(2, p2);
        lr.SetPosition(3, p3);
        lr.SetPosition(4, p0); // 닫힌 사각형
        lr.startWidth = gridLineWidth * 1.5f;
        lr.endWidth = gridLineWidth * 1.5f;
        lr.material = GetLineMaterial();
        lr.startColor = color;
        lr.endColor = color;
        lr.useWorldSpace = true;
        lr.loop = false;
    }

    private Material _lineMaterial;
    private Material GetLineMaterial()
    {
        if (_lineMaterial == null)
        {
            // 기본 Unlit 머티리얼 생성
            _lineMaterial = new Material(Shader.Find("Sprites/Default"));
            _lineMaterial.hideFlags = HideFlags.HideAndDontSave;
        }
        return _lineMaterial;
    }

    /// <summary>
    /// 그리드 라인을 삭제합니다.
    /// </summary>
    public void DestroyGridLines()
    {
        if (_gridLinesParent != null)
        {
            DestroyImmediate(_gridLinesParent);
            _gridLinesParent = null;
        }
        _gridLinesCreated = false;
    }

    /// <summary>
    /// 그리드 라인 표시/숨김을 토글합니다.
    /// </summary>
    public void ToggleGridLines(bool show)
    {
        showGridDebug = show;
        if (_gridLinesParent != null)
        {
            _gridLinesParent.SetActive(show);
        }
        else if (show && !_gridLinesCreated)
        {
            CreateGridLines();
        }
    }

    void OnDestroy()
    {
        DestroyGridLines();
        if (_lineMaterial != null)
        {
            DestroyImmediate(_lineMaterial);
            _lineMaterial = null;
        }
    }

    // 호환성을 위한 빈 메서드
    public void DrawGridDebugLines() { }

    #endregion
}


