// Assets/Scripts/Managers/FieldManager.cs
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using Fusion;
using AI.UtilitySystem;
using AI.UtilitySystem.Considerations.Placement;
[RequireComponent(typeof(PlacementManager))]
public class FieldManager : MonoBehaviour
{
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
    private GameObject attackRangeIndicatorInstance;
    private GameObject skillRangeIndicatorInstance;

    // [3D Migration] 논리 그리드 설정
    [Header("3D 그리드 설정")]
    [Tooltip("3D 공간에서 논리 그리드의 시작점 (보통 Ground 오브젝트의 위치)")]
    public Vector3 gridOrigin = Vector3.zero;

    [Tooltip("그리드 한 칸의 크기 (미터 단위)")]
    public float cellSize = 1f;

    [Tooltip("그리드 크기 (X, Z 칸 수) - x는 3D의 X, y는 3D의 Z를 의미")]
    public Vector2Int gridSize = new Vector2Int(10, 8);

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
    private Dictionary<Vector3Int, DestructibleWall> placedWalls = new Dictionary<Vector3Int, DestructibleWall>();
    // 영구(파괴 불가) 벽 관리
    private Dictionary<Vector3Int, GameObject> placedPermanentWalls = new Dictionary<Vector3Int, GameObject>();
    private int wallLayer = -1;
    private bool permanentWallsGenerated = false;

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
    // 드래그 상태 값 (3D용)
    private float dragBaseY;           // 드래그 시작 시의 기준 Y 값
    private Vector2 offsetXZ;          // 마우스 대비 유닛의 XZ 평면 오프셋

    // AI 배치 디버그용 변수들
    private Dictionary<Vector3Int, float> _debugTileScores = new Dictionary<Vector3Int, float>();
    private Dictionary<Vector3Int, DebugScoreBreakdown> _debugScoreBreakdowns = new Dictionary<Vector3Int, DebugScoreBreakdown>();
    private bool _showDebugScores = false;
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

    /// <summary>
    /// 쿼터뷰 등 기울어진 카메라에서, 화면상의 마우스와 가장 겹쳐 보이는 그리드 셀을 찾습니다.
    /// 기준은 각 셀 중심의 스크린 좌표와 현재 마우스 스크린 좌표 간의 거리입니다.
    /// </summary>
    private Vector3Int GetBestGridUnderMouse(int searchRadius = 2)
    {
        Vector3 mouseWorld = GetMouseWorldPosition();
        Vector2 mouseScreen = Input.mousePosition;
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
                    Debug.Log($"[FieldManager] Grid derived from ground bounds: Origin={gridOrigin}, Size(X,Z)={gridSize}, CellSize={cellSize}");
                }
                else
                {
                    Debug.Log($"[FieldManager] Grid origin aligned to ground: Origin={gridOrigin}, keep Size(X,Z)={gridSize}");
                }
            }
            else
            {
                // Renderer가 없으면 Transform 위치를 기준으로 최소 인덱스 정렬
                int minIndexX = Mathf.FloorToInt(ground3D.transform.position.x / cellSize);
                int minIndexZ = Mathf.FloorToInt(ground3D.transform.position.z / cellSize);
                gridOrigin = new Vector3(minIndexX * cellSize, 0, minIndexZ * cellSize);
                Debug.Log($"[FieldManager] Grid origin aligned (no Renderer): Origin={gridOrigin}, keep Size(X,Z)={gridSize}");
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

        GeneratePermanentWallsIfNeeded();
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
        if (placementManager.GetCurrentMode() == PlacementMode.None)
        {
            HandleUnitDragAndDrop();
        }
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
        }
        // [수정] 게임 상태가 전투로 변경될 때의 처리
        else if (newState == GameManagers.GameState.Combat)
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

                selectedUnit.transform.position = originalWorldPos;
                // 드래그 중에는 placedUnits에서 제거되지 않으므로, 다시 Add할 필요가 없습니다.

                Debug.Log($"<color=orange>전투 시작으로 인해 {selectedUnit.Data.unitName}의 배치가 취소되고 원위치로 돌아갑니다.</color>");

                // 드래그 상태를 초기화합니다.
                selectedUnit = null;
            }
        }
    }

    #endregion

    #region 벽 생성 및 관리

    public void CreateWallAt(Vector3Int gridPosition)
    {
        if (destructibleWallPrefab == null)
        {
            Debug.LogError($"[FieldManager] CreateWallAt failed: destructibleWallPrefab is null (Player={playerManager?.playerId}) at {gridPosition}");
            return;
        }
        if (HasWallAt(gridPosition))
        {
            Debug.LogWarning($"[FieldManager] CreateWallAt ignored: wall already exists at {gridPosition} (Player={playerManager?.playerId})");
            return;
        }
        if (!IsValidGridPosition(gridPosition))
        {
            Debug.LogWarning($"[FieldManager] CreateWallAt 무시: 유효 범위 밖 위치 {gridPosition} (GridSize={gridSize})");
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
                    Debug.LogWarning($"[FieldManager] CreateWallAt aborted: no empty slot to relocate melee unit at {gridPosition} (Player={playerManager?.playerId})");
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
                return;
            }
            var spawned = runner.Spawn(netPrefab, worldPos, Quaternion.identity, playerManager.Object.InputAuthority);
            if (spawned == null)
            {
                Debug.LogError($"[FieldManager] Runner.Spawn 실패: {destructibleWallPrefab.name} (Player={playerManager?.playerId})");
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
            if (statusBarPrefab != null)
            {
                GameObject statusBarGO = Instantiate(statusBarPrefab, wallGO.transform);
                wallComponent.SetStatusBar(statusBarGO.GetComponent<StatusBarUI>());
            }
            wallComponent.Initialize(this, gridPosition);
            placedWalls.Add(gridPosition, wallComponent);

            Unit unitOnCell = GetUnitAt(gridPosition);
            if (unitOnCell != null && unitOnCell.Data.unitType == UnitType.Ranged)
            {
                Vector3 atopPos = GridToWorld(gridPosition, checkForWall: true);
                unitOnCell.transform.position = atopPos;
            }
        }
        else
        {
            Debug.LogError($"{destructibleWallPrefab.name} 프리팹에 DestructibleWall 컴포넌트가 없습니다!", wallGO);
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
                Debug.Log($"<color=orange>벽이 파괴되어 위에 있던 {unitOnTop.Data.unitName}이(가) 함께 파괴됩니다!</color>");
                unitOnTop.TakeDamage(99999, DamageType.Physical);
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



    // 영구(파괴 불가) 벽 생성
    private async void GeneratePermanentWallsIfNeeded()
    {
        if (permanentWallsGenerated) return;
        if (playerManager == null) return; // Initialize 미완료

        // 네트워크 환경에서는 서버(호스트)만 초기 랜덤 생성 수행
        var runner = playerManager != null ? playerManager.Runner : null;
        if (runner != null && runner.IsRunning && !runner.IsServer)
        {
            // 클라이언트는 서버의 RPC를 통해 동기화 대기
            return;
        }

        if (initialPermanentWallCount <= 0) { permanentWallsGenerated = true; return; }

        // 프리팹 확보 (Inspector 우선, 없으면 Addressables)
        GameObject prefab = permanentWallPrefab;
        if (prefab == null && !string.IsNullOrEmpty(permanentWallAddressKey))
        {
            prefab = await AssetLoader.LoadAssetAsync<GameObject>(permanentWallAddressKey);
        }

        if (prefab == null)
        {
            Debug.LogError($"[FieldManager] Permanent wall prefab not set and failed to load '{permanentWallAddressKey}'. Skipping generation.");
            permanentWallsGenerated = true;
            return;
        }

        // 스폰/골 목표 셀 계산 (3D)
        Vector3Int spawnCell = WorldToGridInt(playerManager.spawnPoint != null ? playerManager.spawnPoint.position : Vector3.zero);
        Vector3Int goalCell = WorldToGridInt(playerManager.goalTransform != null ? playerManager.goalTransform.position : Vector3.zero);

        // 후보 셀 수집
        List<Vector3Int> candidates = new List<Vector3Int>();
        for (int y = 0; y < gridSize.y; y++)
        {
            for (int x = 0; x < gridSize.x; x++)
            {
                var cell = new Vector3Int(x, y, 0);
                if (!IsValidGridPosition(cell)) continue;
                if (cell == spawnCell || cell == goalCell) continue; // 스폰/도착지 제외
                if (HasWallAt(cell)) continue; // 기존 벽 제외
                if (IsUnitAt(cell)) continue; // 유닛이 있는 칸 제외
                candidates.Add(cell);
            }
        }

        if (candidates.Count == 0)
        {
            Debug.LogWarning($"[FieldManager] No valid cells found for permanent walls (Player={playerManager?.playerId}).");
            permanentWallsGenerated = true;
            return;
        }

        int toPlace = Mathf.Min(initialPermanentWallCount, candidates.Count);
        List<Vector3Int> selected = new List<Vector3Int>(toPlace);
        for (int i = 0; i < toPlace; i++)
        {
            int idx = Random.Range(0, candidates.Count);
            var pos = candidates[idx];
            candidates.RemoveAt(idx);
            selected.Add(pos);
            CreatePermanentWallAt(pos, prefab); // 서버/오프라인에서만 실제 배치
        }

        // 네트워크 게임이라면, 선택된 좌표를 클라이언트에 브로드캐스트하여 동일 위치에 생성
        if (runner != null && runner.IsRunning && runner.IsServer && playerManager != null)
        {
            int[] flat = new int[selected.Count * 2];
            for (int i = 0; i < selected.Count; i++)
            {
                flat[i * 2] = selected[i].x;
                flat[i * 2 + 1] = selected[i].y;
            }
            playerManager.RPC_ApplyPermanentWalls(flat);
        }

        permanentWallsGenerated = true;
    }

    /// <summary>
    /// 서버가 선택한 영구 벽 좌표 목록을 받아, 클라이언트에서 동일하게 생성합니다.
    /// </summary>
    public async void ApplyPermanentWallsFromServer(int[] flatPositions)
    {
        if (flatPositions == null || flatPositions.Length == 0) return;

        // 프리팹 확보 (Inspector 우선, 없으면 Addressables)
        GameObject prefab = permanentWallPrefab;
        if (prefab == null && !string.IsNullOrEmpty(permanentWallAddressKey))
        {
            prefab = await AssetLoader.LoadAssetAsync<GameObject>(permanentWallAddressKey);
        }

        if (prefab == null)
        {
            Debug.LogError($"[FieldManager] Permanent wall prefab not available on client for ApplyPermanentWallsFromServer. Key='{permanentWallAddressKey}'");
            return;
        }

        int count = flatPositions.Length / 2;
        for (int i = 0; i < count; i++)
        {
            var pos = new Vector3Int(flatPositions[i * 2], flatPositions[i * 2 + 1], 0);
            if (!IsValidGridPosition(pos)) continue;
            if (HasWallAt(pos)) continue;
            CreatePermanentWallAt(pos, prefab);
        }

        permanentWallsGenerated = true;
    }

    private void CreatePermanentWallAt(Vector3Int gridPosition, GameObject prefab)
    {
        if (!IsValidGridPosition(gridPosition)) return;
        if (HasWallAt(gridPosition)) return;

        Vector3 worldPos = GridToWorld(gridPosition);
        float halfH = GetPrefabHeight(prefab) * 0.5f;
        worldPos.y += halfH;

        GameObject wallGO = Instantiate(prefab, worldPos, Quaternion.identity, wallParent);
        // 레이어 지정 (자식 포함)
        if (wallLayer >= 0) SetLayerRecursively(wallGO, wallLayer);

        placedPermanentWalls[gridPosition] = wallGO;

        // 벽 위에 원거리 유닛이 있었다면 올려놓기
        Unit unitOnCell = GetUnitAt(gridPosition);
        if (unitOnCell != null && unitOnCell.Data.unitType == UnitType.Ranged)
        {
            Vector3 atopPos = GridToWorld(gridPosition, checkForWall: true);
            unitOnCell.transform.position = atopPos;
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

    #endregion

    #region 유닛 생성 및 관리

    public Unit GetUnitAt(Vector3Int gridPosition)
    {
        placedUnits.TryGetValue(gridPosition, out Unit unit);
        return unit;
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
        return placedUnits.ContainsKey(gridPosition);
    }

    public void CreateAndPlaceUnitOnField(UnitData unitData, int starLevel)
    {
        Debug.Log($"<color=green>[Flow] Request CreateAndPlaceUnitOnField -> {unitData?.unitName} ({starLevel}★) Player={playerManager?.playerId}</color>");
        // AI 플레이어인지 ComponentRegistry를 통해 확인합니다. AIPlayerController가 자신의 ID로 등록한다고 가정합니다.
        if (ComponentRegistry.Has<AIPlayerController>(playerManager.playerId.ToString()))
        {
            Debug.Log($"<color=green>[Flow] AI path -> CreateAndPlaceUnitOnFieldForAI</color>");
            CreateAndPlaceUnitOnFieldForAI(unitData, starLevel);
            return; // AI 로직을 수행했으면 여기서 종료
        }

        Vector3Int? emptySlot = FindFirstEmptySlot(unitData);
        if (emptySlot.HasValue)
        {
            Debug.Log($"<color=green>[Flow] Placement slot found at {emptySlot.Value} -> CreateUnitAt</color>");
            CreateUnitAt(unitData, emptySlot.Value, starLevel);
        }
        else
        {
            Debug.LogWarning("[FieldManager] 필드에 빈 공간이 없어 유닛을 배치할 수 없습니다! 골드를 환불합니다.");
            Debug.Log("<color=green>[Flow] No empty slot -> refund</color>");
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
            Debug.LogWarning($"[FieldManager (AI)] {unitData.unitName}을(를) 배치할 유효한 위치를 찾지 못했습니다. 골드를 환불합니다.");
            int refundCost = (starLevel == 2) ? unitData.cost * 4 : unitData.cost;
            playerManager.AddGold(refundCost);
        }
    }

     public async void CreateUnitAt(UnitData data, Vector3Int gridPosition, int starLevel, bool markAsAIPurchased = false)
    {
        if (!IsValidGridPosition(gridPosition))
        {
            Debug.LogWarning($"[FieldManager] CreateUnitAt 무시: 유효 범위 밖 위치 {gridPosition} (GridSize={gridSize})");
            return;
        }
        // --- [핵심 수정 부분] ---
        string prefabKey = data.prefabsByStarLevel[starLevel - 1];
        GameObject prefabToCreate = await AssetLoader.LoadAssetAsync<GameObject>(prefabKey);
        // --- [수정 끝] ---

        if (prefabToCreate == null)
        {
            Debug.LogError($"{data.unitName}의 {starLevel}성에 해당하는 프리팹({prefabKey})을 로드할 수 없습니다!");
            return;
        }

        // 3D 그리드 사용 (벽 체크 포함)
        Vector3 worldPos = GridToWorld(gridPosition, checkForWall: true);

        GameObject newUnitGO = null;
        var runner = playerManager != null ? playerManager.Runner : null;
        bool hasNetPrefab = prefabToCreate.TryGetComponent<NetworkObject>(out var networkPrefab);
        Debug.Log($"<color=green>[Spawn] Request -> {data.unitName} ({starLevel}★) at {gridPosition}, runner={(runner!=null)}, hasNetPrefab={hasNetPrefab}, stateAuth={(playerManager!=null ? playerManager.Object.HasStateAuthority : false)}</color>");
        if (runner != null && hasNetPrefab)
        {
            if (!playerManager.Object.HasStateAuthority)
            {
                Debug.Log($"<color=green>[Spawn] Client attempted network spawn -> ignored (server only). Player={playerManager?.playerId}</color>");
                return;
            }

            var spawned = runner.Spawn(networkPrefab, worldPos, Quaternion.identity, playerManager.Object.InputAuthority);
            if (spawned == null)
            {
                Debug.LogError($"[FieldManager] Runner.Spawn 실패: {prefabToCreate.name} (Player={playerManager?.playerId})");
                return;
            }
            newUnitGO = spawned.gameObject;
            Debug.Log($"<color=green>[Spawn] Network unit spawned -> {newUnitGO.name} at {worldPos} (Player={playerManager?.playerId})</color>");
            if (unitParent != null)
            {
                newUnitGO.transform.SetParent(unitParent, true);
            }
            // 클라이언트들의 placedUnits 등록을 위해 브로드캐스트
            if (playerManager != null)
            {
                playerManager.RPC_RegisterUnitAt(spawned, gridPosition.x, gridPosition.y, data.name, starLevel);
            }
        }
        else
        {
            newUnitGO = Instantiate(prefabToCreate, worldPos, Quaternion.identity, unitParent);
            Debug.Log($"<color=green>[Spawn] Local instantiate (non-network) -> {newUnitGO.name} at {worldPos}</color>");
        }
        // Attach orientation fixer to ensure rig local rotation and face camera on spawn
        var orientationFixer = newUnitGO.AddComponent<UnitOrientationFixer>();
        orientationFixer.rigRootName = "Armature"; // adjust if your rig root name differs
        orientationFixer.rigLocalEulerTarget = new Vector3(-90f, 180f, 0f);
        orientationFixer.faceCameraOnSpawn = true;
        orientationFixer.enforceEveryLateUpdate = true;
        orientationFixer.targetCamera = playerCamera; // avoid ComponentRegistry lookup warnings
        orientationFixer.yawOffsetDeg = 180f; // compensate if model's visual forward is flipped
        Unit newUnitComponent = newUnitGO.GetComponent<Unit>();

        if (newUnitComponent != null)
        {
            if (statusBarPrefab != null)
            {
                GameObject statusBarGO = Instantiate(statusBarPrefab, newUnitGO.transform);
                newUnitComponent.SetStatusBar(statusBarGO.GetComponent<StatusBarUI>());
            }
            // Initialize가 비동기이므로 완료를 기다린 후 등록합니다.
            await newUnitComponent.Initialize(data, starLevel, playerManager);
            placedUnits.Add(gridPosition, newUnitComponent);
            CheckForCombination();
        }
        else
        {
            Debug.LogError($"{prefabToCreate.name} 프리팹에 Unit 컴포넌트가 없습니다!", newUnitGO);
            Destroy(newUnitGO);
        }
    }

    public void UnitDied(Unit deadUnit)
    {
        if (placedUnits.ContainsValue(deadUnit))
        {
            var item = placedUnits.First(kvp => kvp.Value == deadUnit);
            placedUnits.Remove(item.Key);
        }
    }

    public void MoveUnit(Vector3Int from, Vector3Int to)
    {
        if (!IsValidGridPosition(from) || !IsValidGridPosition(to))
        {
            Debug.LogWarning($"[FieldManager] MoveUnit 무시: 범위를 벗어난 이동 {from} -> {to} (GridSize={gridSize})");
            return;
        }

        if (placedUnits.TryGetValue(from, out Unit unit))
        {
            string uName = (unit != null && unit.Data != null) ? unit.Data.unitName : (unit != null ? unit.name : "Unit");
            Debug.Log($"<color=green>[MoveUnit] {uName} 이동: {from} -> {to}</color>");
            placedUnits.Remove(from);

            Vector3 finalWorldPos = GridToWorld(to, checkForWall: true);

            // ✅ NetworkTransform 처리
            var networkTransform = unit.GetComponent<Fusion.NetworkTransform>();

            if (networkTransform != null)
            {
                // NetworkTransform이 비활성화되어 있으면 재활성화
                if (!networkTransform.enabled)
                {
                    networkTransform.enabled = true;
                    Debug.Log($"<color=cyan>[MoveUnit] NetworkTransform 재활성화: {uName}</color>");
                }

                // StateAuthority가 있는 경우에만 Teleport 호출
                var networkObject = unit.GetComponent<Fusion.NetworkObject>();
                if (networkObject != null && networkObject.HasStateAuthority)
                {
                    networkTransform.Teleport(finalWorldPos, unit.transform.rotation);
                    Debug.Log($"<color=cyan>[MoveUnit] NetworkTransform.Teleport 호출 (StateAuthority): {uName} to {finalWorldPos}</color>");
                }
                else
                {
                    // StateAuthority가 없으면 직접 위치 설정 (네트워크 동기화 대기)
                    unit.transform.position = finalWorldPos;
                    string uNameNoAuth = (unit != null && unit.Data != null) ? unit.Data.unitName : (unit != null ? unit.name : "Unit");
                    Debug.Log($"<color=yellow>[MoveUnit] 직접 위치 설정 (No StateAuthority): {uNameNoAuth} to {finalWorldPos}</color>");
                }
            }
            else
            {
                // NetworkTransform이 없는 경우 (로컬 전용)
                unit.transform.position = finalWorldPos;
            }

            placedUnits.Add(to, unit);
            CheckForCombination();
        }
        else
        {
            Debug.LogWarning($"<color=red>[FieldManager] MoveUnit: '{from}' 위치에서 유닛을 찾을 수 없습니다.</color>");
        }
    }

    public void SwapUnits(Vector3Int a, Vector3Int b)
    {
        if (!IsValidGridPosition(a) || !IsValidGridPosition(b))
        {
            Debug.LogWarning($"[FieldManager] SwapUnits 무시: 범위를 벗어남 {a} <-> {b} (GridSize={gridSize})");
            return;
        }
        if (!placedUnits.TryGetValue(a, out Unit unitA) || !placedUnits.TryGetValue(b, out Unit unitB))
        {
            Debug.LogWarning($"[FieldManager] SwapUnits 실패: 대상 유닛을 찾을 수 없음 {a} <-> {b}");
            return;
        }

        string uNameA = (unitA != null && unitA.Data != null) ? unitA.Data.unitName : (unitA != null ? unitA.name : "UnitA");
        string uNameB = (unitB != null && unitB.Data != null) ? unitB.Data.unitName : (unitB != null ? unitB.name : "UnitB");
        Debug.Log($"<color=yellow>[SwapUnits] {uNameA} <-> {uNameB} ({a} <-> {b})</color>");

        Vector3 worldForA = GridToWorld(b, checkForWall: true);
        Vector3 worldForB = GridToWorld(a, checkForWall: true);

        // ✅ NetworkTransform 처리
        var networkTransformA = unitA.GetComponent<Fusion.NetworkTransform>();
        var networkTransformB = unitB.GetComponent<Fusion.NetworkTransform>();

        // unitA 처리
        if (networkTransformA != null)
        {
            if (!networkTransformA.enabled)
            {
                networkTransformA.enabled = true;
                Debug.Log($"<color=cyan>[SwapUnits] NetworkTransform 재활성화: {uNameA}</color>");
            }

            var networkObjectA = unitA.GetComponent<Fusion.NetworkObject>();
            if (networkObjectA != null && networkObjectA.HasStateAuthority)
            {
                networkTransformA.Teleport(worldForA, unitA.transform.rotation);
                Debug.Log($"<color=cyan>[SwapUnits] NetworkTransform.Teleport (StateAuthority): {uNameA} to {worldForA}</color>");
            }
            else
            {
                unitA.transform.position = worldForA;
                Debug.Log($"<color=yellow>[SwapUnits] 직접 위치 설정 (No StateAuthority): {uNameA} to {worldForA}</color>");
            }
        }
        else
        {
            unitA.transform.position = worldForA;
        }

        // unitB 처리
        if (networkTransformB != null)
        {
            if (!networkTransformB.enabled)
            {
                networkTransformB.enabled = true;
                Debug.Log($"<color=cyan>[SwapUnits] NetworkTransform 재활성화: {uNameB}</color>");
            }

            var networkObjectB = unitB.GetComponent<Fusion.NetworkObject>();
            if (networkObjectB != null && networkObjectB.HasStateAuthority)
            {
                networkTransformB.Teleport(worldForB, unitB.transform.rotation);
                Debug.Log($"<color=cyan>[SwapUnits] NetworkTransform.Teleport (StateAuthority): {uNameB} to {worldForB}</color>");
            }
            else
            {
                unitB.transform.position = worldForB;
                Debug.Log($"<color=yellow>[SwapUnits] 직접 위치 설정 (No StateAuthority): {uNameB} to {worldForB}</color>");
            }
        }
        else
        {
            unitB.transform.position = worldForB;
        }

        placedUnits[a] = unitB;
        placedUnits[b] = unitA;
        CheckForCombination();
    }

    private void RespawnAllUnits()
    {
        foreach (Unit unit in placedUnits.Values)
        {
            if (unit != null && unit.IsDead)
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
        // placedUnits 딕셔너리의 값들(Unit)을 리스트로 변환하여 반환합니다.
        return placedUnits.Values.ToList();
    }

    /// <summary>
    /// AI 재배치 로직을 위해 특정 유닛의 현재 그리드 위치를 반환합니다.
    /// </summary>
    public Vector3Int? GetUnitPosition(Unit unit)
    {
        var entry = placedUnits.FirstOrDefault(kvp => kvp.Value == unit);
        if (entry.Value != null) // 유닛을 찾았는지 확인합니다.
        {
            return entry.Key;
        }
        return null;
    }

    /// <summary>
    /// 유닛 타입에 따라 AI가 배치할 수 있는 모든 유효한 타일 위치 목록을 반환합니다.
    /// 이 메서드는 타일의 존재 여부와 타입만 확인하며, 해당 위치에 다른 유닛이 있는지는 확인하지 않습니다.
    /// </summary>
    public List<Vector3Int> GetValidPlacementTiles(UnitType unitType)
    {
        var validTiles = new List<Vector3Int>();
        // 3D 모드: 논리 그리드의 모든 셀이 배치 가능 (근접 유닛의 경우)
        if (unitType == UnitType.Melee)
        {
            for (int y = 0; y < gridSize.y; y++)
            {
                for (int x = 0; x < gridSize.x; x++)
                {
                    validTiles.Add(new Vector3Int(x, y, 0));
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

    #endregion

    #region AI-specific Public Methods

    /// <summary>
    /// 재배치를 위해 필드에 있는 모든 유닛의 등록을 해제합니다. (오브젝트는 파괴하지 않음)
    /// </summary>
    public void UnregisterAllUnits()
    {
        placedUnits.Clear();
    }

    /// <summary>
    /// 이미 존재하는 유닛 게임 오브젝트를 특정 위치에 등록하고 위치를 이동시킵니다.
    /// </summary>
    public void RegisterUnitAt(Unit unit, Vector3Int gridPosition)
    {
        if (!IsValidGridPosition(gridPosition))
        {
            Debug.LogWarning($"[FieldManager] RegisterUnitAt 무시: 유효 범위 밖 위치 {gridPosition} (GridSize={gridSize})");
            return;
        }
        Vector3 worldPos = GridToWorld(gridPosition, checkForWall: true);
        unit.transform.position = worldPos;
        placedUnits.Add(gridPosition, unit);
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
            Debug.LogWarning($"AI가 {unitData.unitType} 타입의 유닛을 배치할 유효한 타일을 찾지 못했습니다.");
            return null;
        }

        List<Vector3Int> candidateTiles = allValidTiles;

        // [핵심 수정] 근접 유닛의 경우, 배치 후보지를 몬스터 경로 위로 먼저 한정합니다.
        if (unitData.unitType == UnitType.Melee && monsterPathContext != null && monsterPathContext.Count > 0)
        {
            // 디버그: AI 필드 타일 범위와 몬스터 경로 범위 확인 (필요시 주석 해제)
            // var fieldTileRange = $"필드 타일 범위: ({allValidTiles.Min(t => t.x)}, {allValidTiles.Min(t => t.y)}) ~ ({allValidTiles.Max(t => t.x)}, {allValidTiles.Max(t => t.y)})";
            // var pathRange = $"받은 몬스터 경로 범위: ({monsterPathContext.Min(n => n.x)}, {monsterPathContext.Min(n => n.y)}) ~ ({monsterPathContext.Max(n => n.x)}, {monsterPathContext.Max(n => n.y)})";
            // Debug.Log($"[AI Placement Debug] {fieldTileRange}");
            // Debug.Log($"[AI Placement Debug] {pathRange}");
            // Debug.Log($"[AI Placement Debug] 받은 경로 첫 번째 노드: ({monsterPathContext[0].x}, {monsterPathContext[0].y}), 마지막 노드: ({monsterPathContext[monsterPathContext.Count-1].x}, {monsterPathContext[monsterPathContext.Count-1].y})");

            var pathTilePositions = new HashSet<Vector3Int>(monsterPathContext.Select(node => new Vector3Int(node.x, node.y, 0)));
            var onPathTiles = allValidTiles.Where(tile => pathTilePositions.Contains(tile)).ToList();

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
            // Debug.Log($"[AI Placement] {unitData.unitName}을(를) {bestPosition}에 배치 (점수: {highestScore:F2})");

            // 3초 후 디버그 표시 끄기
            Invoke(nameof(ClearDebugScores), 3.0f);

            return bestPosition;
        }

        // 점수 계산에 실패했더라도, 배치 가능한 첫 번째 위치라도 반환합니다.
        ClearDebugScores();
        return FindFirstEmptySlot(unitData);
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
        var combinableGroup = placedUnits.Values
            .Where(u => u != null && u.starLevel < 3)
            .GroupBy(u => new { u.Data.unitName, u.starLevel })
            .Where(g => g.Count() >= 3)
            .FirstOrDefault();

        if (combinableGroup != null)
        {
            List<Unit> unitsToCombine = combinableGroup.Take(3).ToList();
            for (int i = 0; i < 2; i++)
            {
                UnitDied(unitsToCombine[i]);
                Destroy(unitsToCombine[i].gameObject);
            }
            Unit baseUnit = unitsToCombine[2];
            await baseUnit.Upgrade();
            ReplaceUnitPrefab(baseUnit);
            CheckForCombination();
        }
    }

    private void ReplaceUnitPrefab(Unit unitToReplace)
    {
        Vector3Int currentPos = placedUnits.First(kvp => kvp.Value == unitToReplace).Key;
        UnitData unitData = unitToReplace.Data;
        int newStarLevel = unitToReplace.starLevel;
        UnitDied(unitToReplace);
        Destroy(unitToReplace.gameObject);
        // CreateUnitAt을 호출합니다. (await 불필요)
        CreateUnitAt(unitData, currentPos, newStarLevel);
    }

    #endregion

    #region 유닛 상세 정보 패널 및 드래그 앤 드롭

    /// <summary>
    /// [3D] 마우스 위치를 3D 월드 좌표로 변환합니다. Ground 평면과의 교차점을 사용합니다.
    /// </summary>
    private Vector3 GetMouseWorldPosition()
    {
        if (ground3D == null) return Vector3.zero;
        Ray ray = playerCamera.ScreenPointToRay(Input.mousePosition);
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

        // 3D Raycast (쿼터뷰/3D 환경용)
        Ray ray = playerCamera.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit, 1000f))
        {
            var unit3D = hit.collider.GetComponentInParent<Unit>();
            // 이 FieldManager가 관리하는 유닛만 선택되도록 제한합니다.
            if (unit3D != null && placedUnits.ContainsValue(unit3D)) return unit3D;
        }

        // 스크린 공간 기반 근사: 마우스와 가장 가까운 유닛 선택
        if (placedUnits.Count > 0)
        {
            Vector2 mouseScreen = Input.mousePosition;
            float thresholdSq = dragPickMaxScreenDistance * dragPickMaxScreenDistance;
            Unit bestUnit = null;
            float bestDistSq = thresholdSq;

            foreach (var unit in placedUnits.Values)
            {
                if (unit == null) continue;
                Vector3 unitScreen = playerCamera.WorldToScreenPoint(unit.transform.position);
                Vector2 delta = (Vector2)unitScreen - mouseScreen;
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

    private void HandleUnitDragAndDrop()
    {
        if (GameManagers.Instance == null)
        {
            // 아직 GameManagers가 준비되지 않았으면 아무것도 하지 않고 함수를 종료합니다.
            return;
        }
        var gameState = GameManagers.Instance.GetGameState();
        if (gameState != GameManagers.GameState.Prepare && gameState != GameManagers.GameState.Combat) return;

        if (playerCamera == null)
        {
            Debug.LogWarning("[FieldManager] playerCamera is null!");
            return;
        }

        // [3D] 초기화 확인
        if (ground3D == null)
        {
            Debug.LogWarning("[FieldManager] ground3D is null - not initialized yet!");
            return;
        }

        // [3D Migration] 마우스 월드 좌표 및 그리드 좌표 계산
        Vector3 mouseWorldPos = GetMouseWorldPosition();
        Vector3Int gridPos = WorldToGridInt(mouseWorldPos);

        // 마우스 버튼을 눌렀을 때
        if (Input.GetMouseButtonDown(0))
        {
            // 셀 기반이 아니라 실제 유닛 콜라이더를 클릭해야 드래그 시작
            Unit clickedUnit = GetUnitUnderMouse();
            bool pointerOverUI = UnityEngine.EventSystems.EventSystem.current != null &&
                                 UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();

            // 패널이 열려있는 상태에서
            if (unitDetailPanelInstance != null && unitDetailPanelInstance.activeSelf)
            {
                // 표시된 유닛을 다시 클릭한 경우 -> 패널 닫고 아무것도 안 함
                if (clickedUnit != null && clickedUnit == unitDisplayedInPanel)
                {
                    UIManagers.Instance.ReturnUIElement("UI_Pnl_UnitDetail");
                    unitDetailPanelInstance = null;
                    unitDisplayedInPanel = null;
                    selectedUnit = null; // 모든 상태 초기화
                    return;
                }

                // UI가 아닌 다른 곳을 클릭한 경우 -> 패널 닫고 클릭한 대상에 대한 처리 계속
                if (!UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
                {
                    UIManagers.Instance.ReturnUIElement("UI_Pnl_UnitDetail");
                    unitDetailPanelInstance = null;
                    unitDisplayedInPanel = null;
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
        }

        // 마우스 버튼을 누르고 있을 때
        if (Input.GetMouseButton(0) && selectedUnit != null)
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
                        Debug.Log($"<color=cyan>[Drag] NetworkTransform 비활성화: {selName}</color>");
                    }

                    // 드래그가 시작되면 열려있던 상세 정보 패널을 닫음
                    if (unitDetailPanelInstance != null && unitDetailPanelInstance.activeSelf)
                    {
                        UIManagers.Instance.ReturnUIElement("UI_Pnl_UnitDetail");
                        unitDetailPanelInstance = null;
                        unitDisplayedInPanel = null;
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
        if (Input.GetMouseButtonUp(0) && selectedUnit != null)
        {
            if (isDragStarted)
            {
                // 드래그 종료: 화면상 마우스와 가장 겹쳐 보이는 셀을 최종 선택
                Vector3Int bestGrid = GetBestGridUnderMouse();
                bestGrid.x = Mathf.Clamp(bestGrid.x, 0, gridSize.x - 1);
                bestGrid.y = Mathf.Clamp(bestGrid.y, 0, gridSize.y - 1);

                if (placementManager.IsPositionValidForPlacement(bestGrid, selectedUnit.Data))
                {
                    Debug.Log($"<color=green>[Drag] 유효한 위치로 이동: {originalUnitPosition} -> {bestGrid}</color>");
                    Debug.Log($"<color=green>[Drag] 현재 유닛 물리적 위치: {selectedUnit.transform.position}</color>");
                    Debug.Log($"<color=green>[Drag] 목표 그리드 월드 위치: {GridToWorld(bestGrid, checkForWall: true)}</color>");
                    // 네트워크 준비 상태 확인 (Runner가 실행 중이면 localPlayer와 InputAuthority 확인)
                    bool canSend = true;
                    var gmInst = GameManagers.Instance;
                    if (gmInst != null && gmInst.Runner != null && gmInst.Runner.IsRunning)
                    {
                        var lp = gmInst.localPlayer;
                        if (lp == null || lp.Object == null || !lp.Object.HasInputAuthority)
                        {
                            canSend = false;
                        }
                    }

                    // 드래그 중 비활성화한 NetworkTransform을 성공 드랍 시 항상 복구
                    if (selectedUnitNetworkTransform != null && !selectedUnitNetworkTransform.enabled)
                    {
                        selectedUnitNetworkTransform.enabled = true;
                    }

                    if (canSend)
                    {
                        if (gmInst != null && gmInst.Runner != null && gmInst.Runner.IsRunning && !gmInst.Runner.IsServer)
                        {
                            Debug.Log($"<color=#3399FF>[ClientFlow] Request MoveUnit {originalUnitPosition} -> {bestGrid}</color>");
                        }
                        var command = new MoveUnitCommand(playerManager.playerId, originalUnitPosition, bestGrid);
                        GameManagers.Instance.CommandProcessor.RequestCommandExecution(command);
                    }
                    else
                    {
                        Debug.Log($"<color=#3399FF>[ClientFlow] Network not ready, snapback</color>");
                        // 네트워크 준비가 안 되었으면 원위치 복귀
                        Vector3 originalWorldPos = GridToWorld(originalUnitPosition, checkForWall: true);
                        if (selectedUnitNetworkTransform != null)
                        {
                            var networkObject = selectedUnit.GetComponent<Fusion.NetworkObject>();
                            if (networkObject != null && networkObject.HasStateAuthority)
                            {
                                selectedUnitNetworkTransform.Teleport(originalWorldPos, selectedUnit.transform.rotation);
                            }
                            else
                            {
                                selectedUnit.transform.position = originalWorldPos;
                            }
                        }
                        else
                        {
                            selectedUnit.transform.position = originalWorldPos;
                        }
                    }
                }
                else
                {
                    Unit target = GetUnitAt(bestGrid);
                    if (target != null)
                    {
                        bool destWallForSelected = GetWallAt(bestGrid) != null;
                        bool destWallForTarget = GetWallAt(originalUnitPosition) != null;
                        bool invalidForSelected = selectedUnit.Data.unitType == UnitType.Melee && destWallForSelected;
                        bool invalidForTarget = target.Data.unitType == UnitType.Melee && destWallForTarget;
                        if (!invalidForSelected && !invalidForTarget)
                        {
                            Debug.Log($"<color=yellow>[Drag] 스왑: {originalUnitPosition} <-> {bestGrid}</color>");
                            // 네트워크 준비 상태 확인
                            bool canSend = true;
                            var gmInst = GameManagers.Instance;
                            if (gmInst != null && gmInst.Runner != null && gmInst.Runner.IsRunning)
                            {
                                var lp = gmInst.localPlayer;
                                if (lp == null || lp.Object == null || !lp.Object.HasInputAuthority)
                                {
                                    canSend = false;
                                }
                            }

                            // 성공 드랍 경로에서도 NetworkTransform 복구
                            if (selectedUnitNetworkTransform != null && !selectedUnitNetworkTransform.enabled)
                            {
                                selectedUnitNetworkTransform.enabled = true;
                            }

                            if (canSend)
                            {
                                if (gmInst != null && gmInst.Runner != null && gmInst.Runner.IsRunning && !gmInst.Runner.IsServer)
                                {
                                    Debug.Log($"<color=#3399FF>[ClientFlow] Request SwapUnit {originalUnitPosition} <-> {bestGrid}</color>");
                                }
                                var swapCmd = new SwapUnitCommand(playerManager.playerId, originalUnitPosition, bestGrid);
                                GameManagers.Instance.CommandProcessor.RequestCommandExecution(swapCmd);
                            }
                            else
                            {
                                Debug.Log($"<color=#3399FF>[ClientFlow] Network not ready, snapback</color>");
                                // 네트워크 준비가 안 되었으면 원위치 복귀
                                Vector3 originalWorldPos = GridToWorld(originalUnitPosition, checkForWall: true);
                                if (selectedUnitNetworkTransform != null)
                                {
                                    var networkObject = selectedUnit.GetComponent<Fusion.NetworkObject>();
                                    if (networkObject != null && networkObject.HasStateAuthority)
                                    {
                                        selectedUnitNetworkTransform.Teleport(originalWorldPos, selectedUnit.transform.rotation);
                                    }
                                    else
                                    {
                                        selectedUnit.transform.position = originalWorldPos;
                                    }
                                }
                                else
                                {
                                    selectedUnit.transform.position = originalWorldPos;
                                }
                            }
                        }
                        else
                        {
                            Debug.Log($"<color=red>[Drag] 스왑 불가, 원래 위치로 복귀</color>");
                            Vector3 originalWorldPos = GridToWorld(originalUnitPosition, checkForWall: true);

                            // ✅ NetworkTransform 처리
                            if (selectedUnitNetworkTransform != null)
                            {
                                selectedUnitNetworkTransform.enabled = true;

                                var networkObject = selectedUnit.GetComponent<Fusion.NetworkObject>();
                                if (networkObject != null && networkObject.HasStateAuthority)
                                {
                                    selectedUnitNetworkTransform.Teleport(originalWorldPos, selectedUnit.transform.rotation);
                                    Debug.Log($"<color=cyan>[Drag] NetworkTransform.Teleport로 원래 위치 복귀 (StateAuthority): {originalWorldPos}</color>");
                                }
                                else
                                {
                                    selectedUnit.transform.position = originalWorldPos;
                                    Debug.Log($"<color=yellow>[Drag] 직접 위치 설정으로 원래 위치 복귀 (No StateAuthority): {originalWorldPos}</color>");
                                }
                            }
                            else
                            {
                                selectedUnit.transform.position = originalWorldPos;
                            }
                        }
                    }
                    else
                    {
                        Debug.Log($"<color=red>[Drag] 유효하지 않은 위치, 원래 위치로 복귀</color>");
                        Vector3 originalWorldPos = GridToWorld(originalUnitPosition, checkForWall: true);

                        // ✅ NetworkTransform 처리
                        if (selectedUnitNetworkTransform != null)
                        {
                            selectedUnitNetworkTransform.enabled = true;

                            var networkObject = selectedUnit.GetComponent<Fusion.NetworkObject>();
                            if (networkObject != null && networkObject.HasStateAuthority)
                            {
                                selectedUnitNetworkTransform.Teleport(originalWorldPos, selectedUnit.transform.rotation);
                                Debug.Log($"<color=cyan>[Drag] NetworkTransform.Teleport로 원래 위치 복귀 (StateAuthority): {originalWorldPos}</color>");
                            }
                            else
                            {
                                selectedUnit.transform.position = originalWorldPos;
                                Debug.Log($"<color=yellow>[Drag] 직접 위치 설정으로 원래 위치 복귀 (No StateAuthority): {originalWorldPos}</color>");
                            }
                        }
                        else
                        {
                            selectedUnit.transform.position = originalWorldPos;
                        }
                    }
                }
            }
            else
            {
                // 짧은 클릭: 이 FieldManager 소유 유닛만 스냅백. (교차 플레이어 유닛 보호)
                if (placedUnits.ContainsValue(selectedUnit))
                {
                    Vector3 originalWorldPos = GridToWorld(originalUnitPosition, checkForWall: true);
                    selectedUnit.transform.position = originalWorldPos;
                }
                ShowUnitDetailPanel(selectedUnit);
            }

            // 상태 초기화
            selectedUnit = null;
            selectedUnitNetworkTransform = null;
            isDragStarted = false;
        }
    }

    private async void ShowUnitDetailPanel(Unit unit)
    {
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
    #endregion

    #region 범위 표시

    /// <summary>
    /// 지정된 유닛의 공격 및 스킬 범위를 원형으로 표시하고, 겹치는 경우 렌더링 순서를 조정합니다.
    /// </summary>
    public async void ShowRanges(Unit unit)
    {
        if (unit == null) return;

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
        indicatorPos.y = unit.transform.position.y + 0.1f;
        if (showAttack)
        {
            attackRangeIndicatorInstance = Instantiate(attackRangeIndicatorPrefab, indicatorPos, Quaternion.Euler(90f, 0f, 0f), transform);
            attackRangeIndicatorInstance.transform.localScale = new Vector3(attackDiameter, attackDiameter, 1f);
        }
        if (showSkill)
        {
            skillRangeIndicatorInstance = Instantiate(skillRangeIndicatorPrefab, indicatorPos, Quaternion.Euler(90f, 0f, 0f), transform);
            skillRangeIndicatorInstance.transform.localScale = new Vector3(skillDiameter, skillDiameter, 1f);
        }

        // 4. 두 범위가 모두 표시될 때 렌더링 순서(Sorting Order) 조정
        if (showAttack && showSkill)
        {
            SpriteRenderer attackRenderer = attackRangeIndicatorInstance.GetComponent<SpriteRenderer>();
            SpriteRenderer skillRenderer = skillRangeIndicatorInstance.GetComponent<SpriteRenderer>();

            if (attackRenderer != null && skillRenderer != null)
            {
                // 더 큰 범위를 뒤에(sortingOrder = 0), 작은 범위를 앞에(sortingOrder = 1) 렌더링
                if (attackDiameter > skillDiameter)
                {
                    attackRenderer.sortingOrder = 0; // 뒤
                    skillRenderer.sortingOrder = 1;  // 앞
                }
                else
                {
                    skillRenderer.sortingOrder = 0;  // 뒤
                    attackRenderer.sortingOrder = 1; // 앞
                }
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

    void OnGUI()
    {
        // AI 플레이어가 아니면 디버그 표시하지 않음
        bool isAIPlayer = ComponentRegistry.Has<AIPlayerController>(playerManager.playerId.ToString());
        if (!isAIPlayer) return;

        if (!_showDebugScores || _debugScoreBreakdowns.Count == 0) return;

        // 카메라가 없으면 표시하지 않음
        if (Camera.main == null) return;

        foreach (var kvp in _debugScoreBreakdowns)
        {
            Vector3Int tilePos = kvp.Key;
            var breakdown = kvp.Value;

            // AI 필드의 실제 월드 좌표 계산 (3D 그리드 기준)
            Vector3 worldPos = GridToWorld(tilePos, checkForWall: false);

            Vector3 screenPos = Camera.main.WorldToScreenPoint(worldPos);

            if (screenPos.z > 0) // 카메라 앞에 있는 경우만 표시
            {
                screenPos.y = Screen.height - screenPos.y; // Unity GUI 좌표계 변환

                // P점수/A점수를 한 줄로 표시 (Path/Ally)
                GUI.color = Color.white;
                GUI.Label(new Rect(screenPos.x - 25, screenPos.y - 10, 50, 20), $"{breakdown.pathScore:F1}/{breakdown.allyScore:F1}");
            }
        }

        GUI.color = Color.white; // 색상 리셋

        // 디버그 정보 표시
        if (_debugUnitData != null)
        {
            GUI.color = Color.white;
            GUI.Label(new Rect(10, 10, 300, 20), $"Player {playerManager.playerId} AI 배치 디버그: {_debugUnitData.unitName} ({_debugUnitData.unitType})");
            GUI.Label(new Rect(10, 30, 300, 20), $"후보 타일 수: {_debugScoreBreakdowns.Count}");
            GUI.Label(new Rect(10, 50, 300, 20), "형식: Path점수/Ally점수");
        }
    }

    // 디버그 표시를 끄는 메서드 (배치 완료 후 호출)
    public void ClearDebugScores()
    {
        _showDebugScores = false;
        _debugTileScores.Clear();
        _debugScoreBreakdowns.Clear();
        _debugUnitData = null;
    }

    #endregion
}
