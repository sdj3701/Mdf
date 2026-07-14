// Assets/Scripts/Managers/FieldManager.cs
using UnityEngine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using Fusion;
using MDF.Runtime.Assets;
[RequireComponent(typeof(PlacementManager))]
public partial class FieldManager : MonoBehaviour
{
    private readonly AddressableAssetOwner _assetOwner = new AddressableAssetOwner();

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
    [Tooltip("Status bars created during field loading so the first unit/wall interaction does not instantiate a world-space canvas.")]
    [SerializeField, Min(0)] private int statusBarPrewarmCount = 4;
    private readonly List<StatusBarUI> _prewarmedStatusBars = new List<StatusBarUI>();
    private Transform _statusBarReserveRoot;

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
    public Vector2Int TotalGridSize => GridGeometry.TotalSize;

    /// <summary>
    /// 전체 그리드의 원점 (외곽 확장 포함)
    /// </summary>
    public Vector3 TotalGridOrigin => GridGeometry.TotalOrigin;

    /// <summary>
    /// 외곽 확장 마진 (셀 단위)
    /// </summary>
    public int OuterGridMargin => outerGridMargin;

    private FieldGridGeometry GridGeometry =>
        new FieldGridGeometry(gridOrigin, cellSize, gridSize, outerGridMargin);

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
    private readonly HashSet<Unit> _combinationReservedUnits = new HashSet<Unit>();
    private bool _combinationInProgress;
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

    public struct UnitPlacementResult
    {
        public bool Succeeded;
        public Unit Unit;
        public Vector3Int Position;
        public string FailureReason;

        public static UnitPlacementResult Success(Unit unit, Vector3Int position)
        {
            return new UnitPlacementResult
            {
                Succeeded = true,
                Unit = unit,
                Position = position,
                FailureReason = null
            };
        }

        public static UnitPlacementResult Failure(Vector3Int position, string reason)
        {
            return new UnitPlacementResult
            {
                Succeeded = false,
                Unit = null,
                Position = position,
                FailureReason = reason
            };
        }
    }
    private readonly List<PendingNetworkMove> pendingNetworkMoves = new List<PendingNetworkMove>();
    private readonly HashSet<uint> retiredNetworkUnitIds = new HashSet<uint>();
    private Dictionary<Vector3Int, DestructibleWall> placedWalls = new Dictionary<Vector3Int, DestructibleWall>();
    // 영구(파괴 불가) 벽 관리
    private Dictionary<Vector3Int, GameObject> placedPermanentWalls = new Dictionary<Vector3Int, GameObject>();
    private readonly HashSet<Vector3Int> authoritativePermanentWallCells = new HashSet<Vector3Int>();
    private readonly HashSet<Vector3Int> playerPlacedPermanentWallCells = new HashSet<Vector3Int>();
    private int _lastAppliedPermanentWallLayoutRevision = -1;
    private bool HasExactPermanentWallLayout =>
        _lastAppliedPermanentWallLayoutRevision >= 0 || permanentWallsGenerated || authoritativePermanentWallCells.Count > 0;
    private int wallLayer = -1;
    private bool permanentWallsGenerated = false;
    private int _lastWallMapRebuildFrame = -1;
    private string _lastWallMapRebuildSummary = "wallMap:notBuilt";
    public bool IsWallMapReady => _lastWallMapRebuildFrame >= 0;
    private int _lastUnitMapRebuildFrame = -1;
    private string _lastUnitMapRebuildSummary = "unitMap:notBuilt";
    private bool _lastUnitMapRebuildSucceeded = true;
    public bool IsUnitMapReady => _lastUnitMapRebuildFrame >= 0;
    private float _lastClientUnitMapReconcileTime = -999f;
    private const float ClientUnitMapReconcileIntervalSeconds = 0.25f;
    private string _hostMigrationUnitRestoreInProgressSignature = string.Empty;
    private int _hostMigrationUnitRestoreGeneration;
    private bool _hostMigrationUnitRestoreRunning;
    private bool _hostMigrationUnitRestoreSucceeded = true;
    private string _hostMigrationUnitRestoreFailureReason = string.Empty;
    private MigrationRestoreReport _hostMigrationUnitRestoreReport = MigrationRestoreReport.Empty("field_units");
    private readonly Dictionary<uint, NetworkId> _hostMigrationUnitNetworkIdRemap =
        new Dictionary<uint, NetworkId>();
    private int _hostMigrationUnitNetworkIdRemapGeneration = -1;
    private bool _hostMigrationUnitNetworkIdRemapCommitted;
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

    private void SetAuthoritativePermanentWallCells(
        IEnumerable<Vector3Int> cells,
        IEnumerable<Vector3Int> playerPlacedCells = null)
    {
        authoritativePermanentWallCells.Clear();
        playerPlacedPermanentWallCells.Clear();
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

        if (playerPlacedCells == null)
        {
            return;
        }

        foreach (var cell in playerPlacedCells)
        {
            if (authoritativePermanentWallCells.Contains(cell))
            {
                playerPlacedPermanentWallCells.Add(cell);
            }
        }
    }

    public int[] BuildPermanentWallSyncPayload(string context)
    {
        RebuildWallMapsAfterMigration(context, false, out _);

        IEnumerable<Vector3Int> sourceCells = HasExactPermanentWallLayout
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

        int[] flat = new int[orderedCells.Count * 3];
        for (int i = 0; i < orderedCells.Count; i++)
        {
            flat[i * 3] = orderedCells[i].x;
            flat[i * 3 + 1] = orderedCells[i].y;
            flat[i * 3 + 2] = playerPlacedPermanentWallCells.Contains(orderedCells[i]) ? 1 : 0;
        }

        return flat;
    }

    private Unit selectedUnit;
    private Vector3Int originalUnitPosition;
    private Vector3 offset;
    private Fusion.NetworkTransform selectedUnitNetworkTransform; // 드래그 중 NetworkTransform 참조

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
    private bool isDragStarted = false;
    private GameObject unitDetailPanelInstance;
    private Unit unitDisplayedInPanel;
    private GameObject unitSellPanelInstance;
    private Unit unitDisplayedInSellPanel;
    private GameObject wallRemovePanelInstance;
    private GameObject wallDisplayedInRemovePanel;

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
        PrewarmStatusBarReserve();
    }

    // ✅ [추가된 핵심 로직] PlayerManager가 호출하여 초기화
    // [3D Migration] Tilemap 대신 3D Ground를 받도록 오버로드 추가
    public void Initialize(PlayerManager owner, GameObject ground3DObject)
    {
        this.playerManager = owner;
        this.ground3D = ground3DObject;
        MarkWallTopologyChanged();
        PrewarmStatusBarReserve();
        InvalidateCombatTargetRegistry();
        _battleOccupancy?.Clear();
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

        return GridGeometry.InnerCellToWorld(gridPos, yOffset);
    }

    /// <summary>
    /// 3D 월드 좌표를 논리 그리드 좌표로 변환합니다.
    /// </summary>
    /// <param name="worldPos">3D 월드 좌표</param>
    /// <returns>그리드 좌표</returns>
    public Vector2Int WorldToGrid(Vector3 worldPos)
    {
        // 3D 월드 좌표를 그리드 좌표로 변환 (X, Z 사용)
        return GridGeometry.WorldToInnerCell(worldPos);
    }

    /// <summary>
    /// Vector3Int를 3D 월드 좌표로 변환합니다. (호환성용)
    /// </summary>
    public Vector3 GridToWorld(Vector3Int gridPos, bool checkForWall = false)
    {
        return GridToWorld(new Vector2Int(gridPos.x, gridPos.y), checkForWall);
    }

    public Vector3 GetWallRootWorldPosition(Vector3Int gridPosition)
    {
        Vector3 worldPos = GridToWorld(gridPosition, checkForWall: false);
        worldPos.y += GetWallRootYOffset();
        return worldPos;
    }

    private float GetWallRootYOffset()
    {
        if (destructibleWallPrefab != null)
        {
            return GetWallPrefabHeight() * 0.5f;
        }

        return wallYOffset * 0.5f;
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
        return GridGeometry.InnerToNavigation(innerCell);
    }

    public Vector2Int InnerCellToNavigationCell(Vector3Int innerCell)
    {
        return InnerCellToNavigationCell(new Vector2Int(innerCell.x, innerCell.y));
    }

    public bool TryNavigationCellToInnerCell(Vector2Int navigationCell, out Vector3Int innerCell)
    {
        return GridGeometry.TryNavigationToInner(navigationCell, out innerCell);
    }

    public bool IsValidNavigationCell(Vector2Int navigationCell)
    {
        return GridGeometry.IsValidNavigation(navigationCell);
    }

    public Vector2Int WorldToNavigationCell(Vector3 worldPos)
    {
        return GridGeometry.WorldToNavigation(worldPos);
    }

    public Vector3 NavigationCellToWorld(Vector2Int navigationCell, bool checkForWall = false)
    {
        float yOffset = gridOrigin.y + groundYOffset;
        if (checkForWall && TryNavigationCellToInnerCell(navigationCell, out var innerCell) && HasWallAt(innerCell))
        {
            yOffset = gridOrigin.y + wallYOffset;
        }

        return GridGeometry.NavigationCellToWorld(navigationCell, yOffset);
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
        return new List<Vector3Int>(GridGeometry.BuildBorderGapCells());
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
        return GridGeometry.ClampWorldToInnerBounds(worldPos);
    }

    /// <summary>
    /// 그리드 좌표가 유효한 범위 내에 있는지 확인합니다.
    /// </summary>
    public bool IsValidGridPosition(Vector2Int gridPos)
    {
        return GridGeometry.IsValidInner(gridPos);
    }

    public bool IsValidGridPosition(Vector3Int gridPos)
    {
        return IsValidGridPosition(new Vector2Int(gridPos.x, gridPos.y));
    }

    public Vector3Int GetGoalGridPosition()
    {
        if (playerManager != null &&
            playerManager.goalTransform != null &&
            GridGeometry.TryWorldToInnerCell(playerManager.goalTransform.position, out Vector2Int resolved))
        {
            return new Vector3Int(resolved.x, resolved.y, 0);
        }

        return new Vector3Int(gridSize.x / 2, gridSize.y / 2, 0);
    }

    public bool IsGoalCell(Vector3Int gridPosition)
    {
        if (!IsValidGridPosition(gridPosition))
        {
            return false;
        }

        Vector3Int goalCell = GetGoalGridPosition();
        return gridPosition.x == goalCell.x && gridPosition.y == goalCell.y;
    }

    public bool IsRegularUnitPlacementCell(Vector3Int gridPosition)
    {
        return IsValidGridPosition(gridPosition) && !IsGoalCell(gridPosition);
    }

    internal int GetRegularUnitGoalViolationCount()
    {
        Vector3Int goalCell = GetGoalGridPosition();
        return placedUnits.Count(kvp =>
            kvp.Value != null &&
            kvp.Key.x == goalCell.x &&
            kvp.Key.y == goalCell.y);
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
            // Unit placement is frozen for battle. Rebuild the local cell index once from
            // authoritative scene objects; moving monsters update only when their cell changes.
            InvalidateCombatTargetRegistry();
            EnsureCombatTargetRegistryReady();

            // 활성화된 배치 모드(유닛, 벽 등)가 있다면 강제로 종료합니다.
            if (placementManager.GetCurrentMode() != PlacementMode.None)
            {
                placementManager.StopPlacementMode();
            }

            // 유닛을 드래그하는 중이었다면 취소하고 원위치시킵니다.
            if (selectedUnit != null)
            {
                Vector3 originalWorldPos = GridToWorld(originalUnitPosition, checkForWall: true);
                RestoreSelectedUnitNetworkTransform();
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
            if (!UnitBelongsToFieldOwnerDurable(occupant))
            {
                Debug.LogWarning($"[WallFlow-Create] abort: occupant is not owned by this field. owner={BuildWallOwnerTag()}, pos={gridPosition}");
                return;
            }

            if (occupant.Data == null)
            {
                Debug.LogWarning($"[WallFlow-Create] abort: occupant unit data is unresolved. owner={BuildWallOwnerTag()}, pos={gridPosition}");
                return;
            }

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

        Vector3 worldPos = GetWallRootWorldPosition(gridPosition);

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
            SnapSpawnedWallTransform(wallGO, worldPos, Quaternion.identity);
        }
        else
        {
            wallGO = Instantiate(destructibleWallPrefab, worldPos, Quaternion.identity, wallParent);
            SnapSpawnedWallTransform(wallGO, worldPos, Quaternion.identity);
        }
        DestructibleWall wallComponent = wallGO.GetComponent<DestructibleWall>();

        if (wallComponent != null)
        {
            AttachStatusBar(wallGO, wallComponent.SetStatusBar);
            wallComponent.Initialize(this, gridPosition);
            placedWalls.Add(gridPosition, wallComponent);
            MarkWallTopologyChanged();
            PublishDestructibleWallHealthMigrationState("create_wall");
            SchedulePathRefresh();

            RepairUnitPresentationAt(gridPosition, "CreateWallAt");

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
            MarkWallTopologyChanged();
            PublishDestructibleWallHealthMigrationState("remove_wall");
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
            .Where(wall => wall != null && TryResolveOwnedDestructibleWallCell(wall, out _));

        foreach (var wall in globalDestructibleCandidates)
        {
            destructibleWalls.Add(wall);
        }

        foreach (var wall in destructibleWalls.Where(IsLiveDestructibleWallCandidate).Distinct())
        {
            if (!TryResolveOwnedDestructibleWallCell(wall, out Vector3Int cell))
            {
                continue;
            }

            destructibleCandidates++;
            if (!IsValidGridPosition(cell))
            {
                outOfBounds++;
                continue;
            }

            if (HasExactPermanentWallLayout && authoritativePermanentWallCells.Contains(cell))
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

            if (HasExactPermanentWallLayout && !authoritativePermanentWallCells.Contains(cell))
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
            MarkWallTopologyChanged();
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
    public bool RebuildUnitMapAfterMigration(string context, bool verboseLog, out string summary, bool repairPresentation = false)
    {
        if (_lastUnitMapRebuildFrame == Time.frameCount)
        {
            if (repairPresentation)
            {
                RepairPlacedUnitPresentation($"RebuildUnitMapAfterMigration.{context}", allowMissingGameManagers: true);
            }

            summary = _lastUnitMapRebuildSummary;
            return _lastUnitMapRebuildSucceeded;
        }

        var oldCells = new HashSet<Vector3Int>(placedUnits.Keys);
        var preservedPendingUnitPositions = new HashSet<Vector3Int>(pendingUnitPositions);
        var preservedPendingUnitDataByPosition = new Dictionary<Vector3Int, UnitData>(pendingUnitDataByPosition);
        var preservedPendingNetworkMoves = pendingNetworkMoves.ToList();
        var rebuiltUnits = new Dictionary<Vector3Int, Unit>();
        var plannedGoalRelocations = new List<(Unit Unit, Vector3Int Cell)>();

        int candidates = 0;
        int registered = 0;
        int duplicates = 0;
        int outOfBounds = 0;
        int missingData = 0;
        int goalRelocations = 0;
        int unresolvedGoalConflicts = 0;

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
        var eligibleRebuildCandidates = new List<(Unit Unit, bool WasRegistered, Vector3Int RegisteredCell)>();

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
            bool belongsToPlayer = UnitBelongsToFieldOwnerDurable(unit);
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

            eligibleRebuildCandidates.Add((unit, wasAlreadyRegistered, registeredCell));
        }

        var reservedRebuildCells = new HashSet<Vector3Int>();
        foreach (var candidate in eligibleRebuildCandidates)
        {
            Vector3Int candidateCell = candidate.WasRegistered
                ? candidate.RegisteredCell
                : WorldToGridInt(candidate.Unit.transform.position);
            if (IsRegularUnitPlacementCell(candidateCell))
            {
                reservedRebuildCells.Add(candidateCell);
            }
        }

        foreach (var candidate in eligibleRebuildCandidates)
        {
            Unit unit = candidate.Unit;
            Vector3Int cell = candidate.WasRegistered
                ? candidate.RegisteredCell
                : WorldToGridInt(unit.transform.position);
            if (!IsValidGridPosition(cell))
            {
                outOfBounds++;
                continue;
            }

            if (IsGoalCell(cell))
            {
                Vector3Int? relocatedCell = FindFirstRegularUnitRebuildCell(unit.Data, reservedRebuildCells);
                if (relocatedCell.HasValue)
                {
                    cell = relocatedCell.Value;
                    reservedRebuildCells.Add(cell);
                    plannedGoalRelocations.Add((unit, cell));
                    goalRelocations++;
                }
                else
                {
                    unresolvedGoalConflicts++;
                    continue;
                }
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
            registered++;
        }

        _lastUnitMapRebuildSucceeded = unresolvedGoalConflicts == 0;
        if (_lastUnitMapRebuildSucceeded)
        {
            placedUnits = rebuiltUnits;
            foreach (var relocation in plannedGoalRelocations)
            {
                MoveUnitImmediate(relocation.Unit, GridToWorld(relocation.Cell, checkForWall: true));
                Debug.LogWarning($"[UnitFlow-Migration] Relocated legacy regular unit away from Goal. context={context}, unit={relocation.Unit.name}, to={relocation.Cell}");
            }

            foreach (var kvp in placedUnits)
            {
                if (kvp.Value != null)
                {
                    SyncUnitPlacementIdentity(kvp.Value, kvp.Key);
                }
            }

            RefreshCombatTargetRegistryAfterRosterRebuild();
            RestorePendingStateAfterUnitMapRebuild(
                preservedPendingUnitPositions,
                preservedPendingUnitDataByPosition,
                preservedPendingNetworkMoves);
            if (repairPresentation)
            {
                RepairPlacedUnitPresentation($"RebuildUnitMapAfterMigration.{context}", allowMissingGameManagers: true);
            }
        }

        bool unitMapChanged = !oldCells.SetEquals(placedUnits.Keys);
        _lastUnitMapRebuildFrame = Time.frameCount;
        _lastUnitMapRebuildSummary =
            $"ctx={context},units={placedUnits.Count}/{candidates},registered={registered},missingData={missingData},outOfBounds={outOfBounds},duplicates={duplicates},goalRelocations={goalRelocations},unresolvedGoalConflicts={unresolvedGoalConflicts},changed={unitMapChanged}";
        summary = _lastUnitMapRebuildSummary;

        if (verboseLog)
        {
            Debug.Log($"[UnitFlow-Migration] RebuildUnitMapAfterMigration {_lastUnitMapRebuildSummary}");
        }

        return _lastUnitMapRebuildSucceeded;
    }

    private void RestorePendingStateAfterUnitMapRebuild(
        HashSet<Vector3Int> preservedPendingUnitPositions,
        Dictionary<Vector3Int, UnitData> preservedPendingUnitDataByPosition,
        List<PendingNetworkMove> preservedPendingNetworkMoves)
    {
        pendingUnitPositions.Clear();
        pendingUnitDataByPosition.Clear();

        foreach (var position in preservedPendingUnitPositions)
        {
            if (!IsRegularUnitPlacementCell(position) || placedUnits.ContainsKey(position))
            {
                continue;
            }

            pendingUnitPositions.Add(position);
            if (preservedPendingUnitDataByPosition.TryGetValue(position, out UnitData unitData) && unitData != null)
            {
                pendingUnitDataByPosition[position] = unitData;
            }
        }

        pendingNetworkMoves.Clear();
        int oldestAllowedFrame = Time.frameCount - PendingNetworkMoveLifetimeFrames;
        foreach (var move in preservedPendingNetworkMoves)
        {
            if (move.CreatedFrame < oldestAllowedFrame)
            {
                continue;
            }

            if (!IsValidGridPosition(move.From) || !IsRegularUnitPlacementCell(move.To))
            {
                continue;
            }

            if (pendingNetworkMoves.Any(existing => existing.From == move.From && existing.To == move.To))
            {
                continue;
            }

            pendingNetworkMoves.Add(move);
        }

        ProcessPendingNetworkMoves();
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

    public int[] GetPlayerPlacedPermanentWallFlatPositions()
    {
        var cells = playerPlacedPermanentWallCells
            .Where(IsValidGridPosition)
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

    public bool IsPlayerPlacedPermanentWallAt(Vector3Int gridPosition)
    {
        return playerPlacedPermanentWallCells.Contains(gridPosition) &&
               placedPermanentWalls.TryGetValue(gridPosition, out GameObject wallObject) &&
               wallObject != null;
    }

    public GameObject GetRemovableWallObjectAt(Vector3Int gridPosition)
    {
        if (placedWalls.TryGetValue(gridPosition, out DestructibleWall destructibleWall) &&
            destructibleWall != null)
        {
            return destructibleWall.gameObject;
        }

        if (IsPlayerPlacedPermanentWallAt(gridPosition) &&
            placedPermanentWalls.TryGetValue(gridPosition, out GameObject permanentWall))
        {
            return permanentWall;
        }

        return null;
    }

    public bool HasRemovableWallAt(Vector3Int gridPosition)
    {
        return GetRemovableWallObjectAt(gridPosition) != null;
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
        bool captured = TryGetFieldUnitMigrationSnapshot(out FieldUnitMigrationSnapshot[] snapshots);
        unitDataRefs = snapshots.Select(entry => entry.UnitDataRef).ToArray();
        unitDataKeys = snapshots.Select(entry => entry.UnitDataKey).ToArray();
        starLevels = snapshots.Select(entry => entry.StarLevel).ToArray();
        flatPositions = new int[snapshots.Length * 3];
        for (int i = 0; i < snapshots.Length; i++)
        {
            flatPositions[(i * 3) + 0] = snapshots[i].Position.x;
            flatPositions[(i * 3) + 1] = snapshots[i].Position.y;
            flatPositions[(i * 3) + 2] = snapshots[i].Position.z;
        }

        return captured;
    }

    public bool TryGetFieldUnitMigrationSnapshot(out FieldUnitMigrationSnapshot[] snapshots)
    {
        _lastUnitMapRebuildFrame = -1;
        if (!RebuildUnitMapAfterMigration(
                "TryGetFieldUnitMigrationSnapshot.AuthoritativeRebuild",
                false,
                out string rebuildSummary))
        {
            snapshots = Array.Empty<FieldUnitMigrationSnapshot>();
            Debug.LogError($"[UnitFlow-Migration] Refusing to capture field units because the authoritative rebuild failed: {rebuildSummary}");
            return false;
        }

        List<Unit> rosterCandidates = CollectFieldUnitMigrationCandidates();
        var registeredCellsByUnit = placedUnits
            .Where(pair => pair.Value != null && IsFieldUnitSnapshotCandidate(pair.Value))
            .GroupBy(pair => pair.Value)
            .ToDictionary(group => group.Key, group => group.Select(pair => pair.Key).ToList());
        var resolvedCandidates = new List<(Unit Unit, Vector3Int Position)>(rosterCandidates.Count);
        var seenCells = new HashSet<Vector3Int>();
        foreach (Unit unit in rosterCandidates)
        {
            if (registeredCellsByUnit.TryGetValue(unit, out List<Vector3Int> registeredCells) &&
                registeredCells.Count > 1)
            {
                snapshots = Array.Empty<FieldUnitMigrationSnapshot>();
                Debug.LogError($"[UnitFlow-Migration] Refusing to capture a field unit registered in multiple cells. unit={unit.name}, cells={string.Join("|", registeredCells)}");
                return false;
            }

            Vector3Int position = registeredCells != null && registeredCells.Count == 1
                ? registeredCells[0]
                : WorldToGridInt(unit.transform.position);
            if (!IsValidGridPosition(position))
            {
                snapshots = Array.Empty<FieldUnitMigrationSnapshot>();
                Debug.LogError($"[UnitFlow-Migration] Refusing to capture an out-of-grid field unit. cell={position}, unit={unit.name}");
                return false;
            }

            if (IsGoalCell(position))
            {
                snapshots = Array.Empty<FieldUnitMigrationSnapshot>();
                Debug.LogError($"[UnitFlow-Migration] Refusing to capture a regular unit on the Goal cell. cell={position}, unit={unit.name}");
                return false;
            }

            if (!seenCells.Add(position))
            {
                snapshots = Array.Empty<FieldUnitMigrationSnapshot>();
                Debug.LogError($"[UnitFlow-Migration] Refusing to capture duplicate field-unit cells. cell={position}, unit={unit.name}");
                return false;
            }

            resolvedCandidates.Add((unit, position));
        }

        var captured = new List<FieldUnitMigrationSnapshot>(resolvedCandidates.Count);
        foreach (var candidate in resolvedCandidates
                     .OrderBy(candidate => candidate.Position.x)
                     .ThenBy(candidate => candidate.Position.y)
                     .ThenBy(candidate => candidate.Position.z))
        {
            Unit unit = candidate.Unit;
            var snapshot = new FieldUnitMigrationSnapshot
            {
                HasRuntimeState = true,
                NetworkIdRaw = TryGetUnitNetworkIdRaw(unit, out uint networkIdRaw) ? networkIdRaw : 0,
                UnitDataRef = unit.Data,
                UnitDataKey = NormalizeMigrationUnitDataKey(unit.UnitDataKeyForRoster),
                StarLevel = Mathf.Max(1, unit.StarLevelForRoster),
                Position = candidate.Position
            };
            unit.CaptureMigrationRuntimeState(ref snapshot);
            if (snapshot.UnitDataRef == null && string.IsNullOrWhiteSpace(snapshot.UnitDataKey))
            {
                snapshots = Array.Empty<FieldUnitMigrationSnapshot>();
                Debug.LogError($"[UnitFlow-Migration] Refusing to capture a field unit without stable data identity. cell={candidate.Position}, unit={unit.name}");
                return false;
            }

            captured.Add(snapshot);
        }

        snapshots = captured.ToArray();
        if (snapshots.Length != rosterCandidates.Count)
        {
            Debug.LogError($"[UnitFlow-Migration] Field roster capture count mismatch. candidates={rosterCandidates.Count},captured={snapshots.Length}");
            snapshots = Array.Empty<FieldUnitMigrationSnapshot>();
            return false;
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
        List<FieldUnitMigrationEntry> legacyEntries = BuildFieldUnitMigrationEntries(
            unitDataRefs,
            unitDataKeys,
            starLevels,
            flatPositions);
        FieldUnitMigrationSnapshot[] snapshots = legacyEntries.Select(entry => new FieldUnitMigrationSnapshot
        {
            HasRuntimeState = false,
            UnitDataRef = entry.UnitDataRef,
            UnitDataKey = entry.UnitDataKey,
            StarLevel = entry.StarLevel,
            Position = entry.Position
        }).ToArray();
        return RestoreFieldUnitsAfterHostMigration(snapshots, context);
    }

    public bool RestoreFieldUnitsAfterHostMigration(
        FieldUnitMigrationSnapshot[] snapshots,
        string context)
    {
        snapshots ??= Array.Empty<FieldUnitMigrationSnapshot>();
        int capturedCount = snapshots.Length;
        FieldUnitMigrationSnapshot? invalidPosition = snapshots
            .Cast<FieldUnitMigrationSnapshot?>()
            .FirstOrDefault(entry => entry.HasValue && !IsValidGridPosition(entry.Value.Position));
        if (invalidPosition.HasValue)
        {
            return RejectFieldUnitMigrationSnapshot(
                capturedCount,
                $"unit_position_out_of_grid:{invalidPosition.Value.Position}",
                context);
        }

        Vector3Int? duplicatePosition = snapshots
            .GroupBy(entry => entry.Position)
            .Where(group => group.Count() > 1)
            .Select(group => (Vector3Int?)group.Key)
            .FirstOrDefault();
        if (duplicatePosition.HasValue)
        {
            return RejectFieldUnitMigrationSnapshot(
                capturedCount,
                $"unit_position_duplicate:{duplicatePosition.Value}",
                context);
        }

        uint duplicateNetworkId = snapshots
            .Where(entry => entry.NetworkIdRaw != 0)
            .GroupBy(entry => entry.NetworkIdRaw)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .FirstOrDefault();
        if (duplicateNetworkId != 0)
        {
            return RejectFieldUnitMigrationSnapshot(
                capturedCount,
                $"unit_network_id_duplicate:{duplicateNetworkId}",
                context);
        }

        var desiredEntries = snapshots
            .OrderBy(entry => entry.Position.x)
            .ThenBy(entry => entry.Position.y)
            .ThenBy(entry => entry.Position.z)
            .ToList();
        if (desiredEntries.Any(entry => IsGoalCell(entry.Position)))
        {
            return RejectFieldUnitMigrationSnapshot(
                capturedCount,
                "unit_goal_cell_blocked_in_snapshot",
                context);
        }

        var signatureEntries = desiredEntries.Select(entry => new FieldUnitMigrationEntry
        {
            UnitDataRef = entry.UnitDataRef,
            UnitDataKey = entry.UnitDataKey,
            StarLevel = entry.StarLevel,
            Position = entry.Position
        }).ToList();
        string desiredSignature = BuildFieldUnitMigrationSignature(signatureEntries);
        if (BuildCurrentFieldUnitMigrationSignature() == desiredSignature &&
            CurrentRosterMatchesSnapshotNetworkIdentities(desiredEntries))
        {
            int exactGeneration = ++_hostMigrationUnitRestoreGeneration;
            BeginHostMigrationUnitNetworkIdRemapGeneration(exactGeneration);
            var exactRemap = new Dictionary<uint, NetworkId>();
            string remapFailure = string.Empty;
            foreach (FieldUnitMigrationSnapshot snapshot in desiredEntries)
            {
                Unit actualUnit = GetUnitAt(snapshot.Position);
                if (!TryStageHostMigrationUnitNetworkIdRemap(
                        snapshot,
                        actualUnit,
                        exactRemap,
                        out remapFailure))
                {
                    break;
                }
            }

            if (string.IsNullOrEmpty(remapFailure) &&
                !CommitHostMigrationUnitNetworkIdRemap(
                    exactGeneration,
                    exactRemap,
                    desiredEntries,
                    out remapFailure))
            {
                // The failure is handled atomically below.
            }

            if (!string.IsNullOrEmpty(remapFailure))
            {
                InvalidateHostMigrationUnitNetworkIdRemap(exactGeneration);
                _hostMigrationUnitRestoreInProgressSignature = string.Empty;
                _hostMigrationUnitRestoreRunning = false;
                _hostMigrationUnitRestoreSucceeded = false;
                _hostMigrationUnitRestoreFailureReason = remapFailure;
                _hostMigrationUnitRestoreReport = MigrationRestoreReport.FailedScope(
                    "field_units",
                    Mathf.Max(1, desiredEntries.Count),
                    remapFailure);
                return false;
            }

            _hostMigrationUnitRestoreInProgressSignature = string.Empty;
            _hostMigrationUnitRestoreRunning = false;
            _hostMigrationUnitRestoreSucceeded = true;
            _hostMigrationUnitRestoreFailureReason = string.Empty;
            _hostMigrationUnitRestoreReport = new MigrationRestoreReport(
                "field_units",
                desiredEntries.Count,
                0,
                desiredEntries.Count,
                0);
            return true;
        }

        if (_hostMigrationUnitRestoreInProgressSignature == desiredSignature)
        {
            return true;
        }

        var resolvedEntries = new List<(UnitData Data, FieldUnitMigrationSnapshot Snapshot)>();
        foreach (var entry in desiredEntries)
        {
            UnitData data = ResolveMigrationUnitData(entry.UnitDataRef, entry.UnitDataKey);
            if (data == null)
            {
                int failedGeneration = ++_hostMigrationUnitRestoreGeneration;
                InvalidateHostMigrationUnitNetworkIdRemap(failedGeneration);
                _hostMigrationUnitRestoreRunning = false;
                _hostMigrationUnitRestoreSucceeded = false;
                _hostMigrationUnitRestoreFailureReason = $"unit_data_missing:{entry.UnitDataKey}";
                _hostMigrationUnitRestoreReport = MigrationRestoreReport.FailedScope(
                    "field_units",
                    desiredEntries.Count,
                    _hostMigrationUnitRestoreFailureReason);
                Debug.LogError($"[UnitFlow-Migration] RestoreFieldUnitsAfterHostMigration rejected before mutation. context={context}, reason={_hostMigrationUnitRestoreFailureReason}");
                return false;
            }

            FieldUnitMigrationSnapshot normalized = entry;
            normalized.UnitDataRef = data;
            normalized.UnitDataKey = NormalizeMigrationUnitDataKey(entry.UnitDataKey);
            normalized.StarLevel = Mathf.Max(1, entry.StarLevel);
            resolvedEntries.Add((data, normalized));
        }

        int generation = ++_hostMigrationUnitRestoreGeneration;
        BeginHostMigrationUnitNetworkIdRemapGeneration(generation);
        _hostMigrationUnitRestoreInProgressSignature = desiredSignature;
        _hostMigrationUnitRestoreRunning = true;
        _hostMigrationUnitRestoreSucceeded = false;
        _hostMigrationUnitRestoreFailureReason = string.Empty;
        _hostMigrationUnitRestoreReport = new MigrationRestoreReport(
            "field_units",
            desiredEntries.Count,
            0,
            0,
            0);
        RestoreFieldUnitsAfterHostMigrationAsync(resolvedEntries, desiredSignature, context, generation).Forget();
        return true;
    }

    private bool RejectFieldUnitMigrationSnapshot(int capturedCount, string reason, string context)
    {
        int generation = ++_hostMigrationUnitRestoreGeneration;
        InvalidateHostMigrationUnitNetworkIdRemap(generation);
        _hostMigrationUnitRestoreInProgressSignature = string.Empty;
        _hostMigrationUnitRestoreRunning = false;
        _hostMigrationUnitRestoreSucceeded = false;
        _hostMigrationUnitRestoreFailureReason = reason;
        _hostMigrationUnitRestoreReport = MigrationRestoreReport.FailedScope(
            "field_units",
            Mathf.Max(1, capturedCount),
            reason);
        Debug.LogError($"[UnitFlow-Migration] RestoreFieldUnitsAfterHostMigration rejected before mutation. context={context}, captured={capturedCount}, reason={reason}");
        return false;
    }

    private Vector3Int? FindFirstRegularUnitRebuildCell(UnitData unitData, HashSet<Vector3Int> reservedCells)
    {
        for (int y = 0; y < gridSize.y; y++)
        {
            for (int x = 0; x < gridSize.x; x++)
            {
                var candidate = new Vector3Int(x, y, 0);
                if (!IsRegularUnitPlacementCell(candidate) ||
                    (reservedCells != null && reservedCells.Contains(candidate)) ||
                    pendingUnitPositions.Contains(candidate))
                {
                    continue;
                }

                if (HasWallAt(candidate) && (unitData == null || unitData.unitType == UnitType.Melee))
                {
                    continue;
                }

                return candidate;
            }
        }

        return null;
    }

    public WallPlacementKind GetWallPlacementKind()
    {
        return placementManager != null
            ? placementManager.GetCurrentWallKind()
            : WallPlacementKind.Destructible;
    }

    public WallPlacementKind ToggleWallPlacementKind()
    {
        return placementManager != null
            ? placementManager.ToggleCurrentWallKind()
            : WallPlacementKind.Destructible;
    }

    public bool IsHostMigrationUnitRestoreTerminal(out bool succeeded, out string reason)
    {
        succeeded = !_hostMigrationUnitRestoreRunning && _hostMigrationUnitRestoreSucceeded;
        reason = _hostMigrationUnitRestoreRunning
            ? "field_unit_restore_running"
            : _hostMigrationUnitRestoreFailureReason;
        return !_hostMigrationUnitRestoreRunning;
    }

    public MigrationRestoreReport HostMigrationUnitRestoreReport => _hostMigrationUnitRestoreReport;

    public int HostMigrationUnitNetworkIdRemapGeneration =>
        _hostMigrationUnitNetworkIdRemapCommitted
            ? _hostMigrationUnitNetworkIdRemapGeneration
            : -1;

    /// <summary>
    /// Resolves an identity captured before host migration to the NetworkId owned by the
    /// successfully reconciled unit. The table is published atomically only after the matching
    /// restore generation reaches a successful terminal state; callers therefore cannot observe
    /// a partial remap while Addressables/unit creation is still in flight.
    /// </summary>
    public bool TryResolveHostMigrationUnitNetworkId(
        NetworkId capturedNetworkId,
        out NetworkId actualNetworkId)
    {
        actualNetworkId = default;
        return capturedNetworkId.Raw != 0 &&
               TryResolveHostMigrationUnitNetworkId(capturedNetworkId.Raw, out actualNetworkId);
    }

    public bool TryResolveHostMigrationUnitNetworkId(
        uint capturedNetworkIdRaw,
        out NetworkId actualNetworkId)
    {
        actualNetworkId = default;
        if (capturedNetworkIdRaw == 0 ||
            !_hostMigrationUnitNetworkIdRemapCommitted ||
            _hostMigrationUnitRestoreRunning ||
            !_hostMigrationUnitRestoreSucceeded ||
            _hostMigrationUnitNetworkIdRemapGeneration != _hostMigrationUnitRestoreGeneration)
        {
            return false;
        }

        return _hostMigrationUnitNetworkIdRemap.TryGetValue(
                   capturedNetworkIdRaw,
                   out actualNetworkId) &&
               actualNetworkId.Raw != 0;
    }

    private void BeginHostMigrationUnitNetworkIdRemapGeneration(int generation)
    {
        _hostMigrationUnitNetworkIdRemap.Clear();
        _hostMigrationUnitNetworkIdRemapGeneration = generation;
        _hostMigrationUnitNetworkIdRemapCommitted = false;
    }

    private void InvalidateHostMigrationUnitNetworkIdRemap(int generation)
    {
        _hostMigrationUnitNetworkIdRemap.Clear();
        _hostMigrationUnitNetworkIdRemapGeneration = generation;
        _hostMigrationUnitNetworkIdRemapCommitted = false;
    }

    private bool CommitHostMigrationUnitNetworkIdRemap(
        int generation,
        IReadOnlyDictionary<uint, NetworkId> stagedRemap,
        IReadOnlyList<FieldUnitMigrationSnapshot> snapshots,
        out string failureReason)
    {
        failureReason = string.Empty;
        if (generation != _hostMigrationUnitRestoreGeneration ||
            stagedRemap == null ||
            snapshots == null)
        {
            failureReason = "unit_network_id_remap_generation_mismatch";
            return false;
        }

        int expectedMappings = snapshots.Count(snapshot => snapshot.NetworkIdRaw != 0);
        if (stagedRemap.Count != expectedMappings)
        {
            failureReason =
                $"unit_network_id_remap_count_mismatch:expected={expectedMappings},actual={stagedRemap.Count}";
            return false;
        }

        foreach (FieldUnitMigrationSnapshot snapshot in snapshots)
        {
            if (snapshot.NetworkIdRaw == 0)
            {
                continue;
            }

            if (!stagedRemap.TryGetValue(snapshot.NetworkIdRaw, out NetworkId actualNetworkId) ||
                actualNetworkId.Raw == 0)
            {
                failureReason = $"unit_network_id_remap_missing:{snapshot.NetworkIdRaw}";
                return false;
            }
        }

        _hostMigrationUnitNetworkIdRemap.Clear();
        foreach (KeyValuePair<uint, NetworkId> pair in stagedRemap)
        {
            _hostMigrationUnitNetworkIdRemap.Add(pair.Key, pair.Value);
        }

        _hostMigrationUnitNetworkIdRemapGeneration = generation;
        _hostMigrationUnitNetworkIdRemapCommitted = true;
        return true;
    }

    private static bool TryStageHostMigrationUnitNetworkIdRemap(
        FieldUnitMigrationSnapshot snapshot,
        Unit actualUnit,
        IDictionary<uint, NetworkId> stagedRemap,
        out string failureReason)
    {
        failureReason = string.Empty;
        if (snapshot.NetworkIdRaw == 0)
        {
            return true;
        }

        if (actualUnit == null ||
            !actualUnit.TryGetComponent<NetworkObject>(out NetworkObject networkObject) ||
            networkObject == null ||
            !networkObject.IsValid ||
            networkObject.Id.Raw == 0)
        {
            failureReason =
                $"unit_network_id_remap_actual_invalid:captured={snapshot.NetworkIdRaw},cell={snapshot.Position}";
            return false;
        }

        if (stagedRemap.TryGetValue(snapshot.NetworkIdRaw, out NetworkId existing))
        {
            if (existing.Raw == networkObject.Id.Raw)
            {
                return true;
            }

            failureReason =
                $"unit_network_id_remap_conflict:captured={snapshot.NetworkIdRaw},first={existing.Raw},second={networkObject.Id.Raw}";
            return false;
        }

        stagedRemap.Add(snapshot.NetworkIdRaw, networkObject.Id);
        return true;
    }

    private async UniTaskVoid RestoreFieldUnitsAfterHostMigrationAsync(
        List<(UnitData Data, FieldUnitMigrationSnapshot Snapshot)> entries,
        string desiredSignature,
        string context,
        int generation)
    {
        int restored = 0;
        int preserved = 0;
        int failed = 0;
        var stagedNetworkIdRemap = new Dictionary<uint, NetworkId>();
        try
        {
            RebuildUnitMapAfterMigration($"{context}.IncrementalReconcile", false, out _);
            List<Unit> candidates = CollectFieldUnitMigrationCandidates();
            var usedUnits = new HashSet<Unit>();
            var matchedUnits = new Dictionary<int, (Unit Unit, bool ExactNetworkMatch)>();

            for (int i = 0; i < entries.Count; i++)
            {
                Unit matched = FindPreservedMigrationUnit(
                    entries[i].Snapshot,
                    candidates,
                    usedUnits,
                    out bool exactNetworkMatch);
                if (matched == null)
                {
                    continue;
                }

                usedUnits.Add(matched);
                matchedUnits[i] = (matched, exactNetworkMatch);
            }

            foreach (var pair in matchedUnits.OrderBy(pair => pair.Key))
            {
                if (generation != _hostMigrationUnitRestoreGeneration)
                {
                    return;
                }

                FieldUnitMigrationSnapshot snapshot = entries[pair.Key].Snapshot;
                RegisterUnitAt(pair.Value.Unit, snapshot.Position);
                if (snapshot.HasRuntimeState && !pair.Value.ExactNetworkMatch)
                {
                    if (!await pair.Value.Unit.RestoreMigrationRuntimeStateAsync(snapshot))
                    {
                        failed++;
                        _hostMigrationUnitRestoreFailureReason =
                            $"matched_unit_runtime_state_restore_failed:{snapshot.Position}";
                        continue;
                    }

                }

                if (!TryStageHostMigrationUnitNetworkIdRemap(
                        snapshot,
                        pair.Value.Unit,
                        stagedNetworkIdRemap,
                        out string remapFailure))
                {
                    failed++;
                    _hostMigrationUnitRestoreFailureReason = remapFailure;
                    continue;
                }

                if (snapshot.HasRuntimeState && !pair.Value.ExactNetworkMatch)
                {
                    restored++;
                }
                else
                {
                    preserved++;
                }
            }

            for (int i = 0; i < entries.Count; i++)
            {
                if (matchedUnits.ContainsKey(i))
                {
                    continue;
                }

                var entry = entries[i];
                FieldUnitMigrationSnapshot snapshot = entry.Snapshot;
                if (generation != _hostMigrationUnitRestoreGeneration)
                {
                    return;
                }

                Unit blockingUnit = GetUnitAt(snapshot.Position);
                if (blockingUnit != null && !usedUnits.Contains(blockingUnit))
                {
                    RemovePlacedUnitEntries(blockingUnit);
                }

                UnitPlacementResult result = await TryCreateUnitAtAsync(
                    entry.Data,
                    snapshot.Position,
                    snapshot.StarLevel,
                    false,
                    suppressCombination: true);
                if (!result.Succeeded)
                {
                    failed++;
                    _hostMigrationUnitRestoreFailureReason =
                        $"unit_restore_failed:{snapshot.Position}:{result.FailureReason}";
                    if (blockingUnit != null)
                    {
                        RegisterUnitAt(blockingUnit, snapshot.Position);
                    }
                    continue;
                }

                usedUnits.Add(result.Unit);
                if (snapshot.HasRuntimeState && !await result.Unit.RestoreMigrationRuntimeStateAsync(snapshot))
                {
                    failed++;
                    _hostMigrationUnitRestoreFailureReason =
                        $"unit_runtime_state_restore_failed:{snapshot.Position}";
                    usedUnits.Remove(result.Unit);
                    RemovePlacedUnitEntries(result.Unit);
                    RemoveOwnedUnitReference(result.Unit);
                    DespawnOrDestroyUnitForMigrationRestore(result.Unit, $"{context}.RuntimeStateRollback");
                    if (blockingUnit != null)
                    {
                        RegisterUnitAt(blockingUnit, snapshot.Position);
                    }
                    continue;
                }

                if (!TryStageHostMigrationUnitNetworkIdRemap(
                        snapshot,
                        result.Unit,
                        stagedNetworkIdRemap,
                        out string remapFailure))
                {
                    failed++;
                    _hostMigrationUnitRestoreFailureReason = remapFailure;
                    usedUnits.Remove(result.Unit);
                    RemovePlacedUnitEntries(result.Unit);
                    RemoveOwnedUnitReference(result.Unit);
                    DespawnOrDestroyUnitForMigrationRestore(result.Unit, $"{context}.NetworkIdRemapRollback");
                    if (blockingUnit != null)
                    {
                        RegisterUnitAt(blockingUnit, snapshot.Position);
                    }
                    continue;
                }

                restored++;
            }

            if (generation != _hostMigrationUnitRestoreGeneration)
            {
                return;
            }

            if (failed == 0)
            {
                foreach (Unit extra in candidates.Where(unit => unit != null && !usedUnits.Contains(unit)))
                {
                    RemovePlacedUnitEntries(extra);
                    RemoveOwnedUnitReference(extra);
                    DespawnOrDestroyUnitForMigrationRestore(extra, $"{context}.IncrementalExtra");
                }
            }

            bool signatureMatches = string.Equals(
                BuildCurrentFieldUnitMigrationSignature(),
                desiredSignature,
                StringComparison.Ordinal);
            bool remapCommitted = false;
            if (failed == 0 && signatureMatches)
            {
                remapCommitted = CommitHostMigrationUnitNetworkIdRemap(
                    generation,
                    stagedNetworkIdRemap,
                    entries.Select(entry => entry.Snapshot).ToList(),
                    out string remapCommitFailure);
                if (!remapCommitted)
                {
                    failed++;
                    _hostMigrationUnitRestoreFailureReason = remapCommitFailure;
                }
            }

            _hostMigrationUnitRestoreSucceeded = failed == 0 && signatureMatches && remapCommitted;
            if (!_hostMigrationUnitRestoreSucceeded)
            {
                if (string.IsNullOrEmpty(_hostMigrationUnitRestoreFailureReason))
                {
                    _hostMigrationUnitRestoreFailureReason = "field_unit_signature_mismatch";
                }
                failed = Mathf.Max(1, failed);
            }
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, this);
            _hostMigrationUnitRestoreFailureReason = "field_unit_restore_exception";
            failed = Mathf.Max(1, failed);
        }
        finally
        {
            if (generation == _hostMigrationUnitRestoreGeneration)
            {
                _hostMigrationUnitRestoreRunning = false;
                if (_hostMigrationUnitRestoreSucceeded)
                {
                    _hostMigrationUnitRestoreInProgressSignature = string.Empty;
                    _hostMigrationUnitRestoreFailureReason = string.Empty;
                }
                else
                {
                    InvalidateHostMigrationUnitNetworkIdRemap(generation);
                }

                if (!_hostMigrationUnitRestoreSucceeded && failed > 0 && restored + preserved + failed > entries.Count)
                {
                    int overflow = restored + preserved + failed - entries.Count;
                    int reducePreserved = Mathf.Min(preserved, overflow);
                    preserved -= reducePreserved;
                    overflow -= reducePreserved;
                    restored = Mathf.Max(0, restored - overflow);
                }

                int unaccounted = Mathf.Max(0, entries.Count - restored - preserved - failed);
                failed += unaccounted;
                int reportCaptured = entries.Count == 0 && !_hostMigrationUnitRestoreSucceeded ? 1 : entries.Count;
                _hostMigrationUnitRestoreReport = new MigrationRestoreReport(
                    "field_units",
                    reportCaptured,
                    restored,
                    preserved,
                    failed,
                    _hostMigrationUnitRestoreFailureReason);

                _lastUnitMapRebuildFrame = Time.frameCount;
                _lastUnitMapRebuildSummary =
                    $"ctx={context},restored={restored},preserved={preserved},failed={failed},captured={entries.Count},success={_hostMigrationUnitRestoreSucceeded},reason={_hostMigrationUnitRestoreFailureReason}";
                Debug.Log($"[UnitFlow-Migration] RestoreFieldUnitsAfterHostMigration {_lastUnitMapRebuildSummary}");
            }
        }
    }

    private List<Unit> CollectFieldUnitMigrationCandidates()
    {
        var candidates = new HashSet<Unit>();
        foreach (Unit unit in placedUnits.Values)
        {
            if (unit != null && IsFieldUnitSnapshotCandidate(unit))
            {
                candidates.Add(unit);
            }
        }

        if (playerManager != null && playerManager.ownedUnits != null)
        {
            foreach (Unit unit in playerManager.ownedUnits)
            {
                if (unit != null && IsFieldUnitSnapshotCandidate(unit))
                {
                    candidates.Add(unit);
                }
            }
        }

        if (unitParent != null)
        {
            foreach (Unit unit in unitParent.GetComponentsInChildren<Unit>(true))
            {
                if (unit != null && IsFieldUnitSnapshotCandidate(unit))
                {
                    candidates.Add(unit);
                }
            }
        }

        return candidates.ToList();
    }

    private bool CurrentRosterMatchesSnapshotNetworkIdentities(
        IReadOnlyList<FieldUnitMigrationSnapshot> snapshots)
    {
        if (snapshots == null)
        {
            return false;
        }

        foreach (FieldUnitMigrationSnapshot snapshot in snapshots)
        {
            if (snapshot.NetworkIdRaw == 0)
            {
                return false;
            }

            Unit unit = GetUnitAt(snapshot.Position);
            if (unit == null ||
                !TryGetUnitNetworkIdRaw(unit, out uint currentNetworkIdRaw) ||
                currentNetworkIdRaw != snapshot.NetworkIdRaw ||
                !IsMigrationUnitCompatible(unit, snapshot))
            {
                return false;
            }
        }

        return true;
    }

    private Unit FindPreservedMigrationUnit(
        FieldUnitMigrationSnapshot snapshot,
        IReadOnlyList<Unit> candidates,
        HashSet<Unit> usedUnits,
        out bool exactNetworkMatch)
    {
        exactNetworkMatch = false;
        if (snapshot.NetworkIdRaw != 0)
        {
            Unit networkMatch = candidates.FirstOrDefault(unit =>
                unit != null &&
                !usedUnits.Contains(unit) &&
                TryGetUnitNetworkIdRaw(unit, out uint idRaw) &&
                idRaw == snapshot.NetworkIdRaw &&
                IsMigrationUnitCompatible(unit, snapshot));
            if (networkMatch != null)
            {
                exactNetworkMatch = true;
                return networkMatch;
            }
        }

        Unit cellMatch = GetUnitAt(snapshot.Position);
        if (cellMatch != null &&
            !usedUnits.Contains(cellMatch) &&
            IsMigrationUnitCompatible(cellMatch, snapshot))
        {
            return cellMatch;
        }

        return candidates.FirstOrDefault(unit =>
            unit != null &&
            !usedUnits.Contains(unit) &&
            WorldToGridInt(unit.transform.position) == snapshot.Position &&
            IsMigrationUnitCompatible(unit, snapshot));
    }

    private static bool IsMigrationUnitCompatible(Unit unit, FieldUnitMigrationSnapshot snapshot)
    {
        if (unit == null || Mathf.Max(1, unit.StarLevelForRoster) != Mathf.Max(1, snapshot.StarLevel))
        {
            return false;
        }

        string expectedKey = NormalizeMigrationUnitDataKey(snapshot.UnitDataKey);
        if (string.IsNullOrEmpty(expectedKey) && snapshot.UnitDataRef != null)
        {
            expectedKey = NormalizeMigrationUnitDataKey(snapshot.UnitDataRef.name);
        }

        string actualKey = NormalizeMigrationUnitDataKey(unit.UnitDataKeyForRoster);
        return !string.IsNullOrEmpty(expectedKey) &&
               string.Equals(actualKey, expectedKey, StringComparison.OrdinalIgnoreCase);
    }

    private bool IsFieldUnitSnapshotCandidate(Unit unit)
    {
        if (unit == null)
        {
            return false;
        }

        return playerManager != null &&
               playerManager.ownedUnits != null &&
               playerManager.ownedUnits.Contains(unit) ||
               UnitBelongsToFieldOwnerDurable(unit);
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

    private bool TryResolveOwnedDestructibleWallCell(DestructibleWall wall, out Vector3Int cell)
    {
        cell = default;
        if (wall == null)
        {
            return false;
        }

        // Networked placement metadata is the durable source of truth. A replicated wall can
        // temporarily have an unsnapped transform or a stale parent on non-authority peers, so
        // world-space bounds must not override a known owner/cell pair.
        if (wall.TryGetReplicatedPlacement(out int ownerPlayerId, out Vector3Int replicatedCell))
        {
            if (playerManager == null || ownerPlayerId != playerManager.playerId)
            {
                return false;
            }

            cell = replicatedCell;
            return true;
        }

        // Scene-authored and legacy non-network walls have no replicated placement metadata.
        // Keep the previous world-space fallback only for those objects.
        if (!IsWorldPositionInsideOwnedGrid(wall.transform.position))
        {
            return false;
        }

        cell = WorldToGridInt(wall.transform.position);
        return true;
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
            prefab = await AssetLoader.LoadAssetAsync<GameObject>(permanentWallAddressKey, _assetOwner);
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
            SetAuthoritativePermanentWallCells(selected);
            playerManager.NotifyPermanentWallLayoutChanged("initial_structural_walls");
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
    public async void ApplyPermanentWallsFromServer(int layoutRevision, int[] packedPositions)
    {
        if (layoutRevision < _lastAppliedPermanentWallLayoutRevision)
        {
            return;
        }

        _lastAppliedPermanentWallLayoutRevision = layoutRevision;
        if (_awaitNetworkPermanentWallsCoroutine != null)
        {
            StopCoroutine(_awaitNetworkPermanentWallsCoroutine);
            _awaitNetworkPermanentWallsCoroutine = null;
        }

        packedPositions ??= Array.Empty<int>();
        var requestedCells = new List<Vector3Int>(packedPositions.Length / 3);
        var requestedPlayerPlacedCells = new HashSet<Vector3Int>();
        int count = packedPositions.Length / 3;
        for (int i = 0; i < count; i++)
        {
            var pos = new Vector3Int(packedPositions[i * 3], packedPositions[i * 3 + 1], 0);
            if (!IsValidGridPosition(pos))
            {
                continue;
            }
            requestedCells.Add(pos);
            if (packedPositions[i * 3 + 2] != 0)
            {
                requestedPlayerPlacedCells.Add(pos);
            }
        }

        SetAuthoritativePermanentWallCells(requestedCells, requestedPlayerPlacedCells);
        RemoveObsoleteClientPermanentWallFallbacks(requestedCells);
        if (requestedCells.Count == 0)
        {
            permanentWallsGenerated = true;
            return;
        }

        // 프리팹 확보 (Inspector 우선, 없으면 Addressables)
        GameObject prefab = permanentWallPrefab;
        if (prefab == null && !string.IsNullOrEmpty(permanentWallAddressKey))
        {
            prefab = await AssetLoader.LoadAssetAsync<GameObject>(permanentWallAddressKey, _assetOwner);
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
            _awaitNetworkPermanentWallsCoroutine = StartCoroutine(
                WaitForNetworkPermanentWallsAndRebuildCoroutine(
                    layoutRevision,
                    requestedCells,
                    requestedPlayerPlacedCells,
                    prefab));
            return;
        }

        foreach (var pos in requestedCells)
        {
            if (HasWallAt(pos)) continue;
            CreatePermanentWallAt(pos, prefab, requestedPlayerPlacedCells.Contains(pos));
        }

        permanentWallsGenerated = true;
    }

    public MigrationRestoreReport RestorePermanentWallsAfterHostMigration(
        int[] flatPositions,
        int[] playerPlacedFlatPositions,
        string context)
    {
        string scope = $"permanent_walls:P{(playerManager != null ? playerManager.playerId : -1)}";
        int captured = flatPositions?.Length / 2 ?? 0;
        if (!TryParsePermanentWallMigrationSnapshot(
                flatPositions,
                playerPlacedFlatPositions,
                out HashSet<Vector3Int> requestedCells,
                out HashSet<Vector3Int> requestedPlayerPlacedCells,
                out string validationError))
        {
            return MigrationRestoreReport.FailedScope(
                scope,
                Mathf.Max(1, captured),
                $"permanent_wall_snapshot_invalid:{validationError}");
        }

        RebuildWallMapsAfterMigration($"FieldManager.RestorePermanentWallsAfterHostMigration.Pre.{context}", false, out _);

        GameObject prefab = permanentWallPrefab;
        if (requestedCells.Count > 0 && prefab == null)
        {
            return MigrationRestoreReport.FailedScope(
                scope,
                Mathf.Max(1, captured),
                "permanent_wall_prefab_unavailable");
        }

        foreach (Vector3Int pos in requestedCells)
        {
            bool hasExistingPermanentWall = placedPermanentWalls.TryGetValue(pos, out GameObject existingPermanentWall) &&
                                            existingPermanentWall != null;
            if (!hasExistingPermanentWall && HasWallAt(pos))
            {
                return MigrationRestoreReport.FailedScope(
                    scope,
                    Mathf.Max(1, captured),
                    $"permanent_wall_collision:{pos.x},{pos.y}");
            }

            Unit occupant = GetUnitAt(pos);
            if (!hasExistingPermanentWall &&
                occupant != null &&
                (occupant.Data == null || occupant.Data.unitType != UnitType.Ranged))
            {
                return MigrationRestoreReport.FailedScope(
                    scope,
                    Mathf.Max(1, captured),
                    $"permanent_wall_unit_collision:{pos.x},{pos.y}");
            }
        }

        SetAuthoritativePermanentWallCells(requestedCells, requestedPlayerPlacedCells);
        int created = 0;
        foreach (Vector3Int pos in requestedCells)
        {
            if (placedPermanentWalls.TryGetValue(pos, out GameObject existingPermanentWall) &&
                existingPermanentWall != null)
            {
                continue;
            }

            bool createdSuccessfully;
            try
            {
                createdSuccessfully = CreatePermanentWallAt(
                    pos,
                    prefab,
                    requestedPlayerPlacedCells.Contains(pos));
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                return MigrationRestoreReport.FailedScope(
                    scope,
                    Mathf.Max(1, captured),
                    $"permanent_wall_create_exception:{pos.x},{pos.y}:{exception.GetType().Name}");
            }

            if (!createdSuccessfully)
            {
                return MigrationRestoreReport.FailedScope(
                    scope,
                    Mathf.Max(1, captured),
                    $"permanent_wall_create_failed:{pos.x},{pos.y}");
            }

            created++;
        }

        RebuildWallMapsAfterMigration($"FieldManager.RestorePermanentWallsAfterHostMigration.Post.{context}", false, out string summary, true);
        var actualCells = new HashSet<Vector3Int>(placedPermanentWalls
            .Where(pair => pair.Value != null)
            .Select(pair => pair.Key));
        var actualPlayerPlacedCells = new HashSet<Vector3Int>(playerPlacedPermanentWallCells
            .Where(cell => actualCells.Contains(cell)));
        if (!actualCells.SetEquals(requestedCells) ||
            !actualPlayerPlacedCells.SetEquals(requestedPlayerPlacedCells) ||
            !authoritativePermanentWallCells.SetEquals(requestedCells))
        {
            return MigrationRestoreReport.FailedScope(
                scope,
                Mathf.Max(1, captured),
                $"permanent_wall_layout_mismatch:requested={requestedCells.Count},actual={actualCells.Count},requestedPlayer={requestedPlayerPlacedCells.Count},actualPlayer={actualPlayerPlacedCells.Count}");
        }

        permanentWallsGenerated = true;
        Debug.Log($"[WallFlow-Migration] durable permanent wall restore complete. owner={BuildWallOwnerTag()}, context={context}, requested={requestedCells.Count}, created={created}, summary={summary}");
        return new MigrationRestoreReport(scope, captured, captured, 0, 0);
    }

    private bool TryParsePermanentWallMigrationSnapshot(
        int[] flatPositions,
        int[] playerPlacedFlatPositions,
        out HashSet<Vector3Int> requestedCells,
        out HashSet<Vector3Int> requestedPlayerPlacedCells,
        out string error)
    {
        requestedCells = new HashSet<Vector3Int>();
        requestedPlayerPlacedCells = new HashSet<Vector3Int>();
        error = string.Empty;
        if (flatPositions == null || playerPlacedFlatPositions == null)
        {
            error = "null_array";
            return false;
        }

        if ((flatPositions.Length & 1) != 0 || (playerPlacedFlatPositions.Length & 1) != 0)
        {
            error = $"shape_mismatch:permanent={flatPositions.Length},playerPlaced={playerPlacedFlatPositions.Length}";
            return false;
        }

        for (int i = 0; i < flatPositions.Length; i += 2)
        {
            var pos = new Vector3Int(flatPositions[i], flatPositions[i + 1], 0);
            if (!IsValidGridPosition(pos))
            {
                error = $"cell_out_of_grid:index={i / 2},cell={pos.x},{pos.y}";
                return false;
            }

            if (!requestedCells.Add(pos))
            {
                error = $"duplicate_cell:index={i / 2},cell={pos.x},{pos.y}";
                return false;
            }
        }

        for (int i = 0; i < playerPlacedFlatPositions.Length; i += 2)
        {
            var pos = new Vector3Int(playerPlacedFlatPositions[i], playerPlacedFlatPositions[i + 1], 0);
            if (!IsValidGridPosition(pos))
            {
                error = $"player_cell_out_of_grid:index={i / 2},cell={pos.x},{pos.y}";
                return false;
            }

            if (!requestedPlayerPlacedCells.Add(pos))
            {
                error = $"duplicate_player_cell:index={i / 2},cell={pos.x},{pos.y}";
                return false;
            }

            if (!requestedCells.Contains(pos))
            {
                error = $"player_cell_not_permanent:index={i / 2},cell={pos.x},{pos.y}";
                return false;
            }
        }

        return true;
    }

    private void RemoveObsoleteClientPermanentWallFallbacks(IEnumerable<Vector3Int> requestedCells)
    {
        if (!IsRunningClientPeer())
        {
            return;
        }

        var requested = requestedCells != null
            ? new HashSet<Vector3Int>(requestedCells)
            : new HashSet<Vector3Int>();
        bool topologyChanged = false;
        foreach (var pair in placedPermanentWalls.ToArray())
        {
            if (requested.Contains(pair.Key))
            {
                continue;
            }

            GameObject wallObject = pair.Value;
            bool isNetworkObject = wallObject != null && wallObject.GetComponent<NetworkObject>() != null;
            if (wallObject != null)
            {
                wallObject.SetActive(false);
            }
            if (!isNetworkObject && wallObject != null)
            {
                Destroy(wallObject);
            }

            placedPermanentWalls.Remove(pair.Key);
            playerPlacedPermanentWallCells.Remove(pair.Key);
            topologyChanged = true;
        }

        _lastWallMapRebuildFrame = -1;
        if (topologyChanged)
        {
            MarkWallTopologyChanged();
        }
        SchedulePathRefresh();
    }

    private System.Collections.IEnumerator WaitForNetworkPermanentWallsAndRebuildCoroutine(
        int layoutRevision,
        List<Vector3Int> expectedCells,
        HashSet<Vector3Int> expectedPlayerPlacedCells,
        GameObject fallbackPrefab)
    {
        const float timeout = 5f;
        float waited = 0f;
        int expectedCount = expectedCells != null ? expectedCells.Count : 0;

        while (waited < timeout)
        {
            if (layoutRevision != _lastAppliedPermanentWallLayoutRevision)
            {
                _awaitNetworkPermanentWallsCoroutine = null;
                yield break;
            }

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
            TryCreateClientPermanentWallFallbacks(
                layoutRevision,
                expectedCells,
                expectedPlayerPlacedCells,
                fallbackPrefab,
                "NetworkSyncTimeout");
        }

        permanentWallsGenerated = permanentWallsGenerated || placedPermanentWalls.Count > 0;
        _awaitNetworkPermanentWallsCoroutine = null;
    }

    private void TryCreateClientPermanentWallFallbacks(
        int layoutRevision,
        List<Vector3Int> expectedCells,
        HashSet<Vector3Int> expectedPlayerPlacedCells,
        GameObject fallbackPrefab,
        string context)
    {
        var runner = playerManager != null ? playerManager.Runner : null;
        if (layoutRevision != _lastAppliedPermanentWallLayoutRevision ||
            runner == null || !runner.IsRunning || runner.IsServer || fallbackPrefab == null || expectedCells == null)
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
            CreateLocalPermanentWallAt(
                cell,
                fallbackPrefab,
                expectedPlayerPlacedCells != null && expectedPlayerPlacedCells.Contains(cell));
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

    public bool TryCreatePlayerPlacedPermanentWallAt(Vector3Int gridPosition)
    {
        if (!IsValidGridPosition(gridPosition) || HasWallAt(gridPosition) || permanentWallPrefab == null)
        {
            return false;
        }

        Unit occupant = GetUnitAt(gridPosition);
        if (occupant != null)
        {
            if (!UnitBelongsToFieldOwnerDurable(occupant) || occupant.Data == null)
            {
                return false;
            }

            if (occupant.Data.unitType == UnitType.Melee)
            {
                Vector3Int? alternate = FindFirstEmptySlot(occupant.Data);
                if (!alternate.HasValue)
                {
                    return false;
                }

                MoveUnit(gridPosition, alternate.Value);
            }
        }

        return CreatePermanentWallAt(gridPosition, permanentWallPrefab, true);
    }

    public bool TryRemovePlayerPlacedPermanentWallAt(Vector3Int gridPosition)
    {
        if (!playerPlacedPermanentWallCells.Contains(gridPosition) ||
            !placedPermanentWalls.TryGetValue(gridPosition, out GameObject wallObject))
        {
            return false;
        }

        Unit unitOnTop = GetUnitAt(gridPosition);
        if (unitOnTop != null)
        {
            Vector3 newPosition = GridToWorld(gridPosition, checkForWall: false);
            MoveUnitImmediate(unitOnTop, newPosition);
        }

        var runner = playerManager != null ? playerManager.Runner : null;
        if (wallObject != null)
        {
            // Destroy is deferred until end-of-frame; disable the collider before advancing
            // wallRevision so an immediate path refresh cannot cache the removed wall.
            wallObject.SetActive(false);
        }
        if (wallObject != null && runner != null && runner.IsRunning &&
            wallObject.TryGetComponent<NetworkObject>(out var networkObject) &&
            playerManager.Object != null && playerManager.Object.HasStateAuthority)
        {
            runner.Despawn(networkObject);
        }
        else if (wallObject != null)
        {
            Destroy(wallObject);
        }

        placedPermanentWalls.Remove(gridPosition);
        authoritativePermanentWallCells.Remove(gridPosition);
        playerPlacedPermanentWallCells.Remove(gridPosition);
        MarkWallTopologyChanged();
        _lastWallMapRebuildFrame = -1;
        SchedulePathRefresh();
        return true;
    }

    private bool CreatePermanentWallAt(Vector3Int gridPosition, GameObject prefab, bool isPlayerPlaced = false)
    {
        if (!IsValidGridPosition(gridPosition)) return false;
        if (HasWallAt(gridPosition)) return false;
        if (prefab == null) return false;

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
                return false;
            }

            var spawned = runner.Spawn(networkPrefab, worldPos, Quaternion.identity, playerManager.Object.InputAuthority);
            if (spawned == null)
            {
                Debug.LogError($"[WallFlow-Auto] abort: Runner.Spawn failed for permanent wall. owner={BuildWallOwnerTag()}, pos={gridPosition}");
                return false;
            }

            wallGO = spawned.gameObject;
            if (wallParent != null)
            {
                wallGO.transform.SetParent(wallParent, true);
            }
            SnapSpawnedWallTransform(wallGO, worldPos, Quaternion.identity);
        }
        else
        {
            wallGO = Instantiate(prefab, worldPos, Quaternion.identity, wallParent);
            SnapSpawnedWallTransform(wallGO, worldPos, Quaternion.identity);
        }

        // 레이어 지정 (자식 포함)
        if (wallLayer >= 0) SetLayerRecursively(wallGO, wallLayer);

        placedPermanentWalls[gridPosition] = wallGO;
        authoritativePermanentWallCells.Add(gridPosition);
        MarkWallTopologyChanged();
        if (isPlayerPlaced)
        {
            playerPlacedPermanentWallCells.Add(gridPosition);
        }

        // 벽 위에 원거리 유닛이 있었다면 올려놓기
        Unit unitOnCell = GetUnitAt(gridPosition);
        if (unitOnCell != null && unitOnCell.Data.unitType == UnitType.Ranged)
        {
            Vector3 atopPos = GridToWorld(gridPosition, checkForWall: true);
            MoveUnitImmediate(unitOnCell, atopPos);
        }

        _lastWallMapRebuildFrame = -1;
        SchedulePathRefresh();
        return true;
    }

    private void CreateLocalPermanentWallAt(Vector3Int gridPosition, GameObject prefab, bool isPlayerPlaced = false)
    {
        if (!IsValidGridPosition(gridPosition)) return;
        if (HasWallAt(gridPosition)) return;
        if (prefab == null) return;

        Vector3 worldPos = GridToWorld(gridPosition);
        float halfH = GetPrefabHeight(prefab) * 0.5f;
        worldPos.y += halfH;

        GameObject wallGO = Instantiate(prefab, worldPos, Quaternion.identity, wallParent);
        SnapSpawnedWallTransform(wallGO, worldPos, Quaternion.identity);
        foreach (var networkObject in wallGO.GetComponentsInChildren<NetworkObject>(true))
        {
            Destroy(networkObject);
        }

        if (wallLayer >= 0) SetLayerRecursively(wallGO, wallLayer);
        placedPermanentWalls[gridPosition] = wallGO;
        MarkWallTopologyChanged();
        if (isPlayerPlaced)
        {
            playerPlacedPermanentWallCells.Add(gridPosition);
        }

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

    /// <summary>
    /// Binds one status bar to a unit/wall lifecycle. Fusion pooled objects keep their child
    /// hierarchy, so always reuse the existing inactive child before creating a new instance.
    /// Resetting before initialization also removes subscriptions and skill-button listeners from
    /// the object's previous pooled lifetime.
    /// </summary>
    internal StatusBarUI AttachStatusBar(GameObject host, Action<StatusBarUI> setter)
    {
        if (host == null)
        {
            return null;
        }

        StatusBarUI statusBarUI = host.GetComponentInChildren<StatusBarUI>(includeInactive: true);
        if (statusBarUI == null && statusBarPrefab != null)
        {
            GameObject statusBarGO = TakePrewarmedStatusBar(host.transform);
            if (statusBarGO == null)
            {
                statusBarGO = Instantiate(statusBarPrefab, host.transform);
            }
            statusBarUI = statusBarGO != null ? statusBarGO.GetComponent<StatusBarUI>() : null;
        }

        if (statusBarUI == null)
        {
            return null;
        }

        setter?.Invoke(statusBarUI);
        bool needsEnable = !statusBarUI.gameObject.activeSelf;
        statusBarUI.ResetForReuse(
            initializeImmediately: !needsEnable && host.activeInHierarchy && statusBarUI.isActiveAndEnabled);
        if (needsEnable)
        {
            statusBarUI.gameObject.SetActive(true);
        }
        return statusBarUI;
    }

    internal int AvailablePrewarmedStatusBarCount
    {
        get
        {
            _prewarmedStatusBars.RemoveAll(statusBar => statusBar == null);
            return _prewarmedStatusBars.Count;
        }
    }

    private void PrewarmStatusBarReserve()
    {
        if (statusBarPrefab == null || statusBarPrewarmCount <= 0)
        {
            return;
        }

        _prewarmedStatusBars.RemoveAll(statusBar => statusBar == null);
        if (_statusBarReserveRoot == null)
        {
            var reserveObject = new GameObject("[StatusBarReserve]");
            reserveObject.transform.SetParent(transform, false);
            reserveObject.SetActive(false);
            _statusBarReserveRoot = reserveObject.transform;
        }

        int targetCount = Mathf.Max(0, statusBarPrewarmCount);
        while (_prewarmedStatusBars.Count < targetCount)
        {
            GameObject instance = Instantiate(statusBarPrefab, _statusBarReserveRoot);
            if (instance == null)
            {
                break;
            }

            instance.SetActive(false);
            StatusBarUI statusBar = instance.GetComponent<StatusBarUI>();
            if (statusBar == null)
            {
                Destroy(instance);
                break;
            }

            statusBar.ResetForReuse(initializeImmediately: false);
            _prewarmedStatusBars.Add(statusBar);
        }
    }

    private GameObject TakePrewarmedStatusBar(Transform host)
    {
        while (_prewarmedStatusBars.Count > 0)
        {
            int lastIndex = _prewarmedStatusBars.Count - 1;
            StatusBarUI statusBar = _prewarmedStatusBars[lastIndex];
            _prewarmedStatusBars.RemoveAt(lastIndex);
            if (statusBar == null)
            {
                continue;
            }

            statusBar.transform.SetParent(host, false);
            return statusBar.gameObject;
        }

        return null;
    }

    private void MoveUnitImmediate(Unit unit, Vector3 targetWorldPos)
    {
        if (unit != null && !unit.IsDead)
        {
            unit.EnsureAlivePresentationActive();
        }

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

        UpdateCombatUnitOccupancy(unit);
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

    private void RemovePlacedUnitEntriesFromOtherFields(Unit unit)
    {
        if (unit == null)
        {
            return;
        }

        var gm = GameManagers.Instance;
        if (gm == null)
        {
            return;
        }

        foreach (var player in gm.AllPlayers)
        {
            var otherField = player != null ? player.fieldManager : null;
            if (otherField == null || otherField == this)
            {
                continue;
            }

            otherField.RemovePlacedUnitEntries(unit);
        }
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
        if (!IsRegularUnitPlacementCell(gridPosition))
        {
            return false;
        }

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
        TryCreateAndPlaceUnitOnFieldAsync(unitData, starLevel).Forget();
    }

    /// <summary>
    /// [AI용] 유닛 타입에 따라 첫 번째 빈 공간에 유닛을 생성하고 배치합니다. (초기 'Dumb' 배치)
    /// </summary>
    public void CreateAndPlaceUnitOnFieldForAI(UnitData unitData, int starLevel)
    {
        TryCreateAndPlaceUnitOnFieldAsync(unitData, starLevel, true).Forget();
    }

    public async UniTask<UnitPlacementResult> TryCreateAndPlaceUnitOnFieldAsync(
        UnitData unitData,
        int starLevel,
        bool markAsAIPurchased = false,
        bool suppressCombination = false,
        CancellationToken cancellationToken = default)
    {
        if (unitData == null)
        {
            return UnitPlacementResult.Failure(default, "unit_data_missing");
        }

        Vector3Int? placementPosition = FindFirstEmptySlot(unitData);
        if (!placementPosition.HasValue)
        {
            return UnitPlacementResult.Failure(default, "field_full");
        }

        return await TryCreateUnitAtAsync(
            unitData,
            placementPosition.Value,
            starLevel,
            markAsAIPurchased,
            suppressCombination,
            cancellationToken);
    }

    public async UniTask<UnitPlacementResult> TryCreateUnitAtAsync(
        UnitData data,
        Vector3Int gridPosition,
        int starLevel,
        bool markAsAIPurchased = false,
        bool suppressCombination = false,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidGridPosition(gridPosition))
        {
            return UnitPlacementResult.Failure(gridPosition, "grid_position_invalid");
        }
        if (IsGoalCell(gridPosition))
        {
            return UnitPlacementResult.Failure(gridPosition, "unit_goal_cell_blocked");
        }
        if (data == null)
        {
            return UnitPlacementResult.Failure(gridPosition, "unit_data_missing");
        }
        if (IsUnitAt(gridPosition))
        {
            return UnitPlacementResult.Failure(gridPosition, "grid_position_occupied");
        }
        if (data.prefabsByStarLevel == null || data.prefabsByStarLevel.Length == 0)
        {
            return UnitPlacementResult.Failure(gridPosition, "unit_prefab_table_missing");
        }
        if (starLevel < 1 || starLevel > data.prefabsByStarLevel.Length)
        {
            return UnitPlacementResult.Failure(gridPosition, "unit_star_level_invalid");
        }

        string prefabKey = data.prefabsByStarLevel[starLevel - 1];
        if (string.IsNullOrEmpty(prefabKey))
        {
            return UnitPlacementResult.Failure(gridPosition, "unit_prefab_key_missing");
        }
        if (!TryReserveUnitPosition(gridPosition, data))
        {
            return UnitPlacementResult.Failure(gridPosition, "grid_position_reserved");
        }

        GameObject newUnitGO = null;
        NetworkObject spawnedNetworkObject = null;
        var runner = playerManager != null ? playerManager.Runner : null;
        GameManagers expectedGameManagers = GameManagers.Instance;
        bool networkSessionExpected =
            (expectedGameManagers != null && expectedGameManagers.Runner != null && expectedGameManagers.Runner.IsRunning) ||
            (playerManager != null && playerManager.Object != null && playerManager.Object.IsValid);
        NetworkRunner expectedRunner = runner != null && runner.IsRunning
            ? runner
            : expectedGameManagers != null && expectedGameManagers.Runner != null && expectedGameManagers.Runner.IsRunning
                ? expectedGameManagers.Runner
                : null;
        if (networkSessionExpected &&
            (expectedRunner == null || playerManager == null || playerManager.Object == null ||
             !playerManager.Object.IsValid || !playerManager.Object.HasStateAuthority))
        {
            ReleaseReservedUnitPosition(gridPosition);
            return UnitPlacementResult.Failure(gridPosition, "network_session_player_not_authoritative");
        }
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            GameObject prefabToCreate = await AssetLoader.LoadAssetAsync<GameObject>(prefabKey, _assetOwner);
            cancellationToken.ThrowIfCancellationRequested();
            if (prefabToCreate == null)
            {
                return UnitPlacementResult.Failure(gridPosition, "unit_prefab_load_failed");
            }

            if (IsGoalCell(gridPosition))
            {
                return UnitPlacementResult.Failure(gridPosition, "unit_goal_cell_blocked_after_load");
            }

            runner = playerManager != null ? playerManager.Runner : null;
            if (networkSessionExpected &&
                (expectedGameManagers != GameManagers.Instance || runner != expectedRunner || expectedRunner == null ||
                 !expectedRunner.IsRunning || playerManager.Object == null ||
                 !playerManager.Object.IsValid || !playerManager.Object.HasStateAuthority))
            {
                return UnitPlacementResult.Failure(gridPosition, "runner_or_authority_changed_during_load");
            }

            Vector3 worldPos = GridToWorld(gridPosition, checkForWall: true);
            bool hasNetPrefab = prefabToCreate.TryGetComponent<NetworkObject>(out var networkPrefab);
            if (runner != null && runner.IsRunning && hasNetPrefab)
            {
                if (playerManager == null || playerManager.Object == null || !playerManager.Object.HasStateAuthority)
                {
                    return UnitPlacementResult.Failure(gridPosition, "state_authority_required");
                }

                spawnedNetworkObject = runner.Spawn(
                    networkPrefab,
                    worldPos,
                    Quaternion.identity,
                    playerManager.Object.InputAuthority);
                if (spawnedNetworkObject == null)
                {
                    return UnitPlacementResult.Failure(gridPosition, "network_spawn_failed");
                }

                retiredNetworkUnitIds.Remove(spawnedNetworkObject.Id.Raw);
                newUnitGO = spawnedNetworkObject.gameObject;
                if (unitParent != null)
                {
                    newUnitGO.transform.SetParent(unitParent, true);
                }
            }
            else if (!networkSessionExpected)
            {
                newUnitGO = Instantiate(prefabToCreate, worldPos, Quaternion.identity, unitParent);
            }
            else
            {
                return UnitPlacementResult.Failure(gridPosition, "network_unit_prefab_required");
            }

            Unit newUnitComponent = newUnitGO != null ? newUnitGO.GetComponent<Unit>() : null;
            if (newUnitComponent == null)
            {
                CleanupUncommittedUnit(newUnitGO, spawnedNetworkObject, runner);
                newUnitGO = null;
                spawnedNetworkObject = null;
                return UnitPlacementResult.Failure(gridPosition, "unit_component_missing");
            }

            var orientationFixer = newUnitGO.GetComponent<UnitOrientationFixer>();
            if (orientationFixer == null)
            {
                orientationFixer = newUnitGO.AddComponent<UnitOrientationFixer>();
                orientationFixer.rigRootName = "Armature";
                orientationFixer.rigLocalEulerTarget = new Vector3(-90f, 180f, 0f);
                orientationFixer.faceCameraOnSpawn = true;
                orientationFixer.enforceEveryLateUpdate = true;
                orientationFixer.targetCamera = playerCamera;
                orientationFixer.yawOffsetDeg = 180f;
            }

            if (markAsAIPurchased)
            {
                newUnitComponent.SetForceSkillAutoUse(true);
            }
            AttachStatusBar(newUnitGO, newUnitComponent.SetStatusBar);
            await newUnitComponent.Initialize(data, starLevel, playerManager);
            cancellationToken.ThrowIfCancellationRequested();
            runner = playerManager != null ? playerManager.Runner : null;
            if (networkSessionExpected &&
                (expectedGameManagers != GameManagers.Instance || runner != expectedRunner || expectedRunner == null ||
                 !expectedRunner.IsRunning || playerManager.Object == null ||
                 !playerManager.Object.IsValid || !playerManager.Object.HasStateAuthority))
            {
                CleanupUncommittedUnit(newUnitGO, spawnedNetworkObject, expectedRunner);
                newUnitGO = null;
                spawnedNetworkObject = null;
                return UnitPlacementResult.Failure(gridPosition, "runner_or_authority_changed_during_initialize");
            }
            if (newUnitComponent == null || newUnitComponent.Data != data || newUnitComponent.starLevel != starLevel)
            {
                CleanupUncommittedUnit(newUnitGO, spawnedNetworkObject, runner);
                newUnitGO = null;
                spawnedNetworkObject = null;
                return UnitPlacementResult.Failure(gridPosition, "unit_initialization_incomplete");
            }

            if (IsGoalCell(gridPosition))
            {
                CleanupUncommittedUnit(newUnitGO, spawnedNetworkObject, runner);
                newUnitGO = null;
                spawnedNetworkObject = null;
                return UnitPlacementResult.Failure(gridPosition, "unit_goal_cell_blocked_before_commit");
            }

            if (placedUnits.ContainsKey(gridPosition))
            {
                CleanupUncommittedUnit(newUnitGO, spawnedNetworkObject, runner);
                newUnitGO = null;
                spawnedNetworkObject = null;
                return UnitPlacementResult.Failure(gridPosition, "grid_position_occupied_after_spawn");
            }

            placedUnits.Add(gridPosition, newUnitComponent);
            SyncUnitPlacementIdentity(newUnitComponent, gridPosition);
            ProcessPendingNetworkMoves();
            BroadcastAuthoritativeUnitRoster("TryCreateUnitAtAsync");
            if (!suppressCombination)
            {
                CheckForCombination();
            }

            return UnitPlacementResult.Success(newUnitComponent, gridPosition);
        }
        catch (OperationCanceledException)
        {
            CleanupUncommittedUnit(newUnitGO, spawnedNetworkObject, runner);
            throw;
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, this);
            CleanupUncommittedUnit(newUnitGO, spawnedNetworkObject, runner);
            return UnitPlacementResult.Failure(gridPosition, "unit_initialization_failed");
        }
        finally
        {
            ReleaseReservedUnitPosition(gridPosition);
        }
    }

    private void CleanupUncommittedUnit(GameObject unitObject, NetworkObject networkObject, NetworkRunner runner)
    {
        if (unitObject == null)
        {
            return;
        }

        Unit uncommittedUnit = unitObject.GetComponent<Unit>();
        if (uncommittedUnit != null)
        {
            RemoveOwnedUnitReference(uncommittedUnit);
        }

        if (networkObject != null && runner != null && runner.IsRunning && playerManager != null &&
            playerManager.Object != null && playerManager.Object.HasStateAuthority)
        {
            runner.Despawn(networkObject);
            return;
        }

        Destroy(unitObject);
    }

    public bool TryRollbackPlacedUnit(Unit unit, string context)
    {
        if (unit == null)
        {
            return false;
        }

        Vector3Int? position = GetUnitPosition(unit);
        if (!position.HasValue)
        {
            return false;
        }

        var runner = playerManager != null ? playerManager.Runner : null;
        if (runner != null && runner.IsRunning &&
            (playerManager == null || playerManager.Object == null || !playerManager.Object.HasStateAuthority))
        {
            return false;
        }

        RetireUnitRegistrationLocally(unit);
        UnitDied(unit);
        if (runner != null && runner.IsRunning && unit.TryGetComponent<NetworkObject>(out var networkObject))
        {
            runner.Despawn(networkObject);
        }
        else
        {
            Destroy(unit.gameObject);
        }

        BroadcastAuthoritativeUnitRoster($"RollbackPlacedUnit:{context}");
        return true;
    }

     private async UniTask LegacyCreateUnitAtUnsafe(UnitData data, Vector3Int gridPosition, int starLevel, bool markAsAIPurchased = false, bool suppressCombination = false)
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
            GameObject prefabToCreate = await AssetLoader.LoadAssetAsync<GameObject>(prefabKey, _assetOwner);

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

        if (IsGoalCell(to))
        {
            return;
        }

        if (placedUnits.TryGetValue(from, out Unit unit))
        {
            if (unit == null || !UnitBelongsToFieldOwnerDurable(unit))
            {
                return;
            }
            if (_combinationReservedUnits.Contains(unit))
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

        if (unit.Owner != null &&
            unit.Owner.playerId >= 0 &&
            unit.Owner.playerId != playerManager.playerId)
        {
            return false;
        }

        int rosterOwnerId = unit.OwnerPlayerIdForRoster;
        if (rosterOwnerId >= 0 && rosterOwnerId != playerManager.playerId)
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

        if (unit.OwnerPlayerIdForRoster == playerManager.playerId)
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

    private bool UnitBelongsToFieldOwnerDurable(Unit unit)
    {
        return UnitBelongsToFieldOwner(unit) || UnitHasReplicatedFieldOwner(unit);
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

        if (IsGoalCell(to))
        {
            reason = "unit_goal_cell_blocked";
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
        if (!IsValidGridPosition(from) || !IsRegularUnitPlacementCell(to) || !ShouldQueuePendingNetworkMove(from))
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

                if (!UnitBelongsToFieldOwnerDurable(unit))
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
        if (IsGoalCell(a) || IsGoalCell(b))
        {
            return;
        }
        if (!placedUnits.TryGetValue(a, out Unit unitA) || !placedUnits.TryGetValue(b, out Unit unitB))
        {
            // Debug.LogWarning($"[FieldManager] SwapUnits 실패: 대상 유닛을 찾을 수 없음 {a} <-> {b}");
            return;
        }

        if (unitA == null || unitB == null ||
            !UnitBelongsToFieldOwnerDurable(unitA) ||
            !UnitBelongsToFieldOwnerDurable(unitB))
        {
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
        var runner = playerManager != null ? playerManager.Runner : null;
        bool networkRunning = runner != null && runner.IsRunning;
        foreach (var entry in placedUnits.ToArray())
        {
            Unit unit = entry.Value;
            if (unit == null)
            {
                EnsurePlacedUnitPresentation(entry.Key, unit, "RespawnAllUnits", allowMissingGameManagers: false);
                continue;
            }

            if (networkRunning && !unit.HasValidNetworkObject)
            {
                continue;
            }

            EnsurePlacedUnitPresentation(entry.Key, unit, "RespawnAllUnits", allowMissingGameManagers: false);
        }
    }

    private static void SnapSpawnedWallTransform(GameObject wallGO, Vector3 position, Quaternion rotation)
    {
        if (wallGO == null)
        {
            return;
        }

        wallGO.transform.SetPositionAndRotation(position, rotation);

        var networkTransform = wallGO.GetComponent<Fusion.NetworkTransform>();
        if (networkTransform != null && networkTransform.enabled)
        {
            networkTransform.Teleport(position, rotation);
        }
    }

    public void RepairPlacedUnitPresentation(string context = "manual")
    {
        RepairPlacedUnitPresentation(context, allowMissingGameManagers: false);
    }

    private void RepairPlacedUnitPresentation(string context, bool allowMissingGameManagers)
    {
        foreach (var entry in placedUnits.ToArray())
        {
            EnsurePlacedUnitPresentation(entry.Key, entry.Value, context, allowMissingGameManagers);
        }
    }

    private void RepairUnitPresentationAt(Vector3Int gridPosition, string context)
    {
        if (placedUnits.TryGetValue(gridPosition, out var unit))
        {
            EnsurePlacedUnitPresentation(gridPosition, unit, context, allowMissingGameManagers: false);
        }
    }

    private void EnsurePlacedUnitPresentation(Vector3Int gridPosition, Unit unit, string context, bool allowMissingGameManagers)
    {
        if (unit == null)
        {
            placedUnits.Remove(gridPosition);
            pendingUnitPositions.Remove(gridPosition);
            pendingUnitDataByPosition.Remove(gridPosition);
            return;
        }

        if (!IsValidGridPosition(gridPosition))
        {
            return;
        }

        bool belongsToField = playerManager == null || UnitBelongsToFieldOwnerDurable(unit);
        if (!belongsToField)
        {
            return;
        }

        if (!CanRepairPrepareUnitPresentation(allowMissingGameManagers))
        {
            return;
        }

        if (unit.IsDead || !unit.gameObject.activeSelf || !unit.gameObject.activeInHierarchy)
        {
            unit.Respawn();
        }
        else
        {
            unit.EnsureAlivePresentationActive();
        }

        Vector3 expectedWorldPos = GridToWorld(gridPosition, checkForWall: true);
        if ((unit.transform.position - expectedWorldPos).sqrMagnitude > 0.0001f)
        {
            MoveUnitImmediate(unit, expectedWorldPos);
        }

        SyncUnitPlacementIdentity(unit, gridPosition);
    }

    private bool CanRepairPrepareUnitPresentation(bool allowMissingGameManagers)
    {
        if (!Application.isPlaying)
        {
            return true;
        }

        bool networkRunning = playerManager != null &&
                              playerManager.Runner != null &&
                              playerManager.Runner.IsRunning;
        if (networkRunning &&
            (playerManager.Object == null ||
             !playerManager.Object.IsValid ||
             !playerManager.Object.HasStateAuthority))
        {
            return false;
        }

        var gm = GameManagers.Instance;
        if (gm == null)
        {
            return allowMissingGameManagers;
        }

        return gm.GetGameState() == GameManagers.GameState.Prepare;
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
        int rosterRevision = playerManager.AdvanceUnitRosterRevisionForAuthority();
        int[] unitIdRaws = new int[entries.Count];
        int[] flatPositions = new int[entries.Count * 3];
        int[] unitDataKeyHashes = new int[entries.Count];
        int[] starLevels = new int[entries.Count];
        int[] compactRoster = new int[4 + (entries.Count * 6)];
        compactRoster[0] = -4;
        compactRoster[1] = rosterRevision;
        compactRoster[3] = entries.Count;
        for (int i = 0; i < entries.Count; i++)
        {
            UnitRosterBroadcastEntry entry = entries[i];
            unitIdRaws[i] = entry.UnitIdRaw;
            flatPositions[(i * 3) + 0] = entry.Position.x;
            flatPositions[(i * 3) + 1] = entry.Position.y;
            flatPositions[(i * 3) + 2] = entry.Position.z;
            unitDataKeyHashes[i] = entry.UnitDataKeyHash;
            starLevels[i] = entry.StarLevel;

            int offset = 4 + (i * 6);
            compactRoster[offset + 0] = entry.UnitIdRaw;
            compactRoster[offset + 1] = entry.Position.x;
            compactRoster[offset + 2] = entry.Position.y;
            compactRoster[offset + 3] = entry.Position.z;
            compactRoster[offset + 4] = entry.StarLevel;
            compactRoster[offset + 5] = entry.UnitDataKeyHash;
        }
        compactRoster[2] = PlayerManager.ComputeUnitRosterFingerprint(
            unitIdRaws,
            flatPositions,
            unitDataKeyHashes,
            starLevels);

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
                playerManager.RPC_RegisterUnitAt(
                    entry.UnitId,
                    entry.Position.x,
                    entry.Position.y,
                    entry.UnitDataKey,
                    entry.StarLevel,
                    rosterRevision);
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

    private void RetireUnitRegistrationLocally(Unit unit)
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
        if (_combinationReservedUnits.Contains(unit))
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
            ++detailPanelRequestRevision;
            if (UIManagers.Instance != null)
            {
                UIManagers.Instance.ReturnUIElement("UI_Pnl_UnitDetail");
            }
            unitDetailPanelInstance = null;
            unitDisplayedInPanel = null;
            kingDisplayedInPanel = false;
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
                    if (!IsGoalCell(pos) && !HasWallAt(pos))
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
                if (!IsGoalCell(wallPos))
                {
                    validTiles.Add(wallPos);
                }
            }
            foreach (var permPos in placedPermanentWalls.Keys)
            {
                if (!IsGoalCell(permPos))
                {
                    validTiles.Add(permPos);
                }
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
        if (IsGoalCell(gridPosition))
        {
            return;
        }
        RemovePlacedUnitEntriesFromOtherFields(unit);
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

    private FieldAiPlacementService _aiPlacementService;

    private FieldAiPlacementService AiPlacementService =>
        _aiPlacementService ?? (_aiPlacementService = new FieldAiPlacementService(this));

    public Vector3Int? FindBestSpotForAI(
        UnitData unitData,
        List<AstarNode> monsterPathContext,
        List<Unit> alliedUnitsContext = null,
        HashSet<Vector3Int> occupiedTiles = null,
        Vector3Int? movingUnitOriginalPos = null)
    {
        return AiPlacementService.FindBestSpot(
            unitData,
            monsterPathContext,
            alliedUnitsContext,
            occupiedTiles,
            movingUnitOriginalPos);
    }

    internal void ScheduleAiPlacementDebugClear()
    {
        CancelInvoke(nameof(ClearDebugScores));
        Invoke(nameof(ClearDebugScores), 3.0f);
    }

    #endregion

    public void CheckForCombination()
    {
        ProcessCombinationsAsync().Forget();
    }

    private async UniTask ProcessCombinationsAsync()
    {
        if (_combinationInProgress)
        {
            return;
        }

        var runner = playerManager != null ? playerManager.Runner : null;
        if (runner != null && runner.IsRunning &&
            (playerManager == null || playerManager.Object == null || !playerManager.Object.HasStateAuthority))
        {
            return;
        }

        _combinationInProgress = true;
        try
        {
            while (TryFindCombinableUnits(out List<Unit> unitsToCombine))
            {
                bool allReserved = true;
                foreach (Unit unit in unitsToCombine)
                {
                    if (unit == null || !_combinationReservedUnits.Add(unit))
                    {
                        allReserved = false;
                        break;
                    }
                }

                if (!allReserved)
                {
                    foreach (Unit unit in unitsToCombine)
                    {
                        if (unit != null)
                        {
                            _combinationReservedUnits.Remove(unit);
                        }
                    }
                    break;
                }

                bool combined;
                try
                {
                    combined = await TryCombineUnitsTransactionAsync(unitsToCombine);
                }
                finally
                {
                    foreach (Unit unit in unitsToCombine)
                    {
                        if (unit != null)
                        {
                            _combinationReservedUnits.Remove(unit);
                        }
                    }
                }

                if (!combined)
                {
                    break;
                }
            }
        }
        finally
        {
            _combinationInProgress = false;
        }
    }

    private bool TryFindCombinableUnits(out List<Unit> unitsToCombine)
    {
        unitsToCombine = null;
        var group = placedUnits
            .Where(kvp => kvp.Value != null && kvp.Value.Data != null && kvp.Value.starLevel < 3)
            .GroupBy(kvp => kvp.Value)
            .Select(unitGroup => new
            {
                Unit = unitGroup.Key,
                Position = unitGroup.Select(kvp => kvp.Key)
                    .OrderBy(cell => cell.x)
                    .ThenBy(cell => cell.y)
                    .ThenBy(cell => cell.z)
                    .First()
            })
            .OrderBy(entry => entry.Position.x)
            .ThenBy(entry => entry.Position.y)
            .ThenBy(entry => entry.Position.z)
            .GroupBy(entry => new { entry.Unit.Data, entry.Unit.starLevel })
            .FirstOrDefault(candidate => candidate.Count() >= 3);

        if (group == null)
        {
            return false;
        }

        unitsToCombine = group.Select(entry => entry.Unit).Take(3).ToList();
        return unitsToCombine.Count == 3;
    }

    private async UniTask<bool> TryCombineUnitsTransactionAsync(List<Unit> unitsToCombine)
    {
        if (unitsToCombine == null || unitsToCombine.Count != 3 || unitsToCombine.Any(unit => unit == null))
        {
            return false;
        }

        Unit baseUnit = unitsToCombine[2];
        UnitData unitData = baseUnit.Data;
        int oldStarLevel = baseUnit.starLevel;
        int newStarLevel = oldStarLevel + 1;
        GameManagers gameManagers = GameManagers.Instance;
        if (gameManagers == null || gameManagers.currentState != GameManagers.GameState.Prepare || gameManagers.IsSequenceTransitioning)
        {
            return false;
        }
        if (unitData == null || unitData.prefabsByStarLevel == null ||
            oldStarLevel < 1 || newStarLevel > unitData.prefabsByStarLevel.Length)
        {
            return false;
        }

        var positions = new Vector3Int[3];
        for (int i = 0; i < unitsToCombine.Count; i++)
        {
            Vector3Int? position = GetUnitPosition(unitsToCombine[i]);
            if (!position.HasValue || GetUnitAt(position.Value) != unitsToCombine[i] ||
                unitsToCombine[i].Data != unitData || unitsToCombine[i].starLevel != oldStarLevel)
            {
                return false;
            }
            positions[i] = position.Value;
        }

        string oldPrefabKey = unitData.prefabsByStarLevel[oldStarLevel - 1];
        string newPrefabKey = unitData.prefabsByStarLevel[newStarLevel - 1];
        if (string.Equals(oldPrefabKey, newPrefabKey, StringComparison.Ordinal))
        {
            try
            {
                await baseUnit.Initialize(unitData, newStarLevel, playerManager);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, baseUnit);
                try
                {
                    await baseUnit.Initialize(unitData, oldStarLevel, playerManager);
                }
                catch (Exception rollbackException)
                {
                    Debug.LogException(rollbackException, baseUnit);
                }
                return false;
            }

            if (!AreCombinationInputsCurrent(unitsToCombine, positions, unitData, oldStarLevel, baseUnit, newStarLevel))
            {
                if (baseUnit != null)
                {
                    try
                    {
                        await baseUnit.Initialize(unitData, oldStarLevel, playerManager);
                    }
                    catch (Exception rollbackException)
                    {
                        Debug.LogException(rollbackException, baseUnit);
                    }
                }
                return false;
            }

            RemoveCombinedUnit(unitsToCombine[0], positions[0]);
            RemoveCombinedUnit(unitsToCombine[1], positions[1]);
            BroadcastAuthoritativeUnitRoster("CheckForCombination.InPlacePromotion");
            return true;
        }

        Unit stagedReplacement = await TryStageCombinedReplacementAsync(unitData, newStarLevel, positions[2]);
        if (stagedReplacement == null)
        {
            return false;
        }

        if (!AreCombinationInputsCurrent(unitsToCombine, positions, unitData, oldStarLevel, null, 0))
        {
            var currentRunner = playerManager != null ? playerManager.Runner : null;
            NetworkObject stagedNetworkObject = stagedReplacement.Object != null && stagedReplacement.Object.IsValid
                ? stagedReplacement.Object
                : stagedReplacement.GetComponent<NetworkObject>();
            CleanupUncommittedUnit(stagedReplacement.gameObject, stagedNetworkObject, currentRunner);
            return false;
        }

        for (int i = 0; i < unitsToCombine.Count; i++)
        {
            RemoveCombinedUnit(unitsToCombine[i], positions[i]);
        }

        placedUnits[positions[2]] = stagedReplacement;
        SyncUnitPlacementIdentity(stagedReplacement, positions[2]);
        AttachStatusBar(stagedReplacement.gameObject, stagedReplacement.SetStatusBar);
        BroadcastAuthoritativeUnitRoster("CheckForCombination.StagedReplacement");
        return true;
    }

    private bool AreCombinationInputsCurrent(
        IReadOnlyList<Unit> units,
        IReadOnlyList<Vector3Int> positions,
        UnitData expectedData,
        int expectedStarLevel,
        Unit promotedUnit,
        int promotedStarLevel)
    {
        GameManagers gm = GameManagers.Instance;
        if (gm == null || gm.currentState != GameManagers.GameState.Prepare || gm.IsSequenceTransitioning ||
            units == null || positions == null || units.Count != 3 || positions.Count != 3)
        {
            return false;
        }

        var runner = playerManager != null ? playerManager.Runner : null;
        if (runner != null && runner.IsRunning &&
            (playerManager.Object == null || !playerManager.Object.IsValid || !playerManager.Object.HasStateAuthority))
        {
            return false;
        }

        for (int i = 0; i < units.Count; i++)
        {
            Unit unit = units[i];
            int requiredStar = unit == promotedUnit ? promotedStarLevel : expectedStarLevel;
            if (unit == null || GetUnitAt(positions[i]) != unit || unit.Data != expectedData ||
                unit.starLevel != requiredStar || !_combinationReservedUnits.Contains(unit))
            {
                return false;
            }
        }

        return true;
    }

    private async UniTask<Unit> TryStageCombinedReplacementAsync(UnitData unitData, int starLevel, Vector3Int position)
    {
        string prefabKey = unitData.prefabsByStarLevel[starLevel - 1];
        if (string.IsNullOrWhiteSpace(prefabKey))
        {
            return null;
        }

        GameManagers expectedGameManagers = GameManagers.Instance;
        bool networkSessionExpected =
            (expectedGameManagers != null && expectedGameManagers.Runner != null && expectedGameManagers.Runner.IsRunning) ||
            (playerManager != null && playerManager.Object != null && playerManager.Object.IsValid);
        NetworkRunner expectedRunner = playerManager != null && playerManager.Runner != null && playerManager.Runner.IsRunning
            ? playerManager.Runner
            : expectedGameManagers != null && expectedGameManagers.Runner != null && expectedGameManagers.Runner.IsRunning
                ? expectedGameManagers.Runner
                : null;
        if (networkSessionExpected &&
            (expectedRunner == null || playerManager == null || playerManager.Object == null ||
             !playerManager.Object.IsValid || !playerManager.Object.HasStateAuthority))
        {
            return null;
        }
        GameObject prefab = await AssetLoader.LoadAssetAsync<GameObject>(prefabKey, _assetOwner);
        if (prefab == null)
        {
            return null;
        }

        var runner = playerManager != null ? playerManager.Runner : null;
        if (networkSessionExpected &&
            (expectedGameManagers != GameManagers.Instance || runner != expectedRunner || expectedRunner == null ||
             !expectedRunner.IsRunning || playerManager.Object == null ||
             !playerManager.Object.IsValid || !playerManager.Object.HasStateAuthority))
        {
            return null;
        }
        GameObject stagedObject;
        NetworkObject stagedNetworkObject = null;
        Vector3 worldPosition = GridToWorld(position, checkForWall: true);
        if (runner != null && runner.IsRunning && prefab.TryGetComponent<NetworkObject>(out var networkPrefab))
        {
            if (playerManager == null || playerManager.Object == null || !playerManager.Object.HasStateAuthority)
            {
                return null;
            }

            stagedNetworkObject = runner.Spawn(
                networkPrefab,
                worldPosition,
                Quaternion.identity,
                playerManager.Object.InputAuthority);
            if (stagedNetworkObject == null)
            {
                return null;
            }
            retiredNetworkUnitIds.Remove(stagedNetworkObject.Id.Raw);
            stagedObject = stagedNetworkObject.gameObject;
            if (unitParent != null)
            {
                stagedObject.transform.SetParent(unitParent, true);
            }
        }
        else if (!networkSessionExpected)
        {
            stagedObject = Instantiate(prefab, worldPosition, Quaternion.identity, unitParent);
        }
        else
        {
            return null;
        }

        Unit stagedUnit = stagedObject != null ? stagedObject.GetComponent<Unit>() : null;
        if (stagedUnit == null)
        {
            CleanupUncommittedUnit(stagedObject, stagedNetworkObject, runner);
            return null;
        }

        try
        {
            await stagedUnit.Initialize(unitData, starLevel, playerManager);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, stagedUnit);
            CleanupUncommittedUnit(stagedObject, stagedNetworkObject, runner);
            return null;
        }

        runner = playerManager != null ? playerManager.Runner : null;
        if (networkSessionExpected &&
            (expectedGameManagers != GameManagers.Instance || runner != expectedRunner || expectedRunner == null ||
             !expectedRunner.IsRunning || playerManager.Object == null ||
             !playerManager.Object.IsValid || !playerManager.Object.HasStateAuthority))
        {
            CleanupUncommittedUnit(stagedObject, stagedNetworkObject, expectedRunner);
            return null;
        }

        if (stagedUnit == null || stagedUnit.Data != unitData || stagedUnit.starLevel != starLevel)
        {
            CleanupUncommittedUnit(stagedObject, stagedNetworkObject, runner);
            return null;
        }
        return stagedUnit;
    }

    private void RemoveCombinedUnit(Unit unit, Vector3Int position)
    {
        if (unit == null)
        {
            return;
        }

        RetireUnitRegistrationLocally(unit);
        UnitDied(unit);
        var runner = playerManager != null ? playerManager.Runner : null;
        if (runner != null && runner.IsRunning && unit.TryGetComponent<NetworkObject>(out var networkObject))
        {
            runner.Despawn(networkObject);
        }
        else
        {
            Destroy(unit.gameObject);
        }
    }

    private async UniTask LegacyCheckForCombinationUnsafe()
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
                    RetireUnitRegistrationLocally(unit);
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
            await ReplaceUnitPrefab(baseUnit);
            CheckForCombination();
        }
    }

    private async UniTask ReplaceUnitPrefab(Unit unitToReplace)
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
        var runner = playerManager != null ? playerManager.Runner : null;
        if (runner != null
            && runner.IsRunning
            && (playerManager == null || playerManager.Object == null || !playerManager.Object.HasStateAuthority))
        {
            // Debug.LogWarning("[FieldManager] ReplaceUnitPrefab ignored: no state authority.");
            return;
        }

        // 기존 유닛 제거
        RetireUnitRegistrationLocally(unitToReplace);
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

        var prefab = await AssetLoader.LoadAssetAsync<GameObject>(prefabKey, _assetOwner);
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
                SkillData currentSkill = await AssetLoader.LoadAssetAsync<SkillData>(skillKey, _assetOwner);
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
        _aiPlacementService?.ClearDebugScores();
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

    void OnDestroy()
    {
        _assetOwner.Dispose();
        _prewarmedStatusBars.Clear();
        _statusBarReserveRoot = null;
        DisposeCombatTargetRegistry();
        DisposeGridDebugVisualization();
    }
}


