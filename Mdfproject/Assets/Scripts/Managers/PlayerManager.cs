// Assets/Scripts/Managers/PlayerManager.cs

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using System.Linq;
using Fusion; // Fusion 네임스페이스 추가
using Cysharp.Threading.Tasks;

public class PlayerManager : NetworkBehaviour // [수정] MonoBehaviour -> NetworkBehaviour
{
    // [수정] playerId를 모든 클라이언트가 동기화할 수 있도록 [Networked] 프로퍼티로 변경합니다.
    [Networked] public int playerId { get; set; }

    [Header("핵심 능력치 (읽기 전용)")]
    // 참고: 이 능력치들도 [Networked]로 변경하면 더 안정적이지만,
    // 현재는 Command 패턴을 사용하므로 playerId만 동기화해도 동작합니다.
    [FormerlySerializedAs("health")]
    [SerializeField] private int initialHealth = 100;
    [FormerlySerializedAs("gold")]
    [SerializeField] private int initialGold = 10;
    [FormerlySerializedAs("wallCount")]
    [SerializeField] private int initialWallCount = 5;

    [Networked] private int health { get; set; }
    [Networked] private int gold { get; set; }
    [Networked] private int wallCount { get; set; }
    private const int SHOP_SNAPSHOT_CAPACITY = 5;
    [Networked, Capacity(SHOP_SNAPSHOT_CAPACITY)] private NetworkArray<NetworkString<_64>> ShopSnapshotUnitKeys { get; }
    [Networked, Capacity(SHOP_SNAPSHOT_CAPACITY)] private NetworkArray<int> ShopSnapshotStarLevels { get; }
    [Networked, Capacity(SHOP_SNAPSHOT_CAPACITY)] private NetworkArray<int> ShopSnapshotSoldFlags { get; }
    [Networked] private int ShopSnapshotRevision { get; set; }
    [Networked] private int ShopSnapshotCount { get; set; }
    [Networked] private int ShopSnapshotRound { get; set; }
    private const int MAX_WALL_COUNT = 5;
    [SerializeField] private int wallReserveK = 2;
    [SerializeField] private Vector2 wallBuildDelayRange = new Vector2(0.3f, 0.8f);
    [SerializeField] private Vector2 unitPurchaseDelayRange = new Vector2(0.5f, 1.0f);
    [SerializeField] private Vector2 unitMoveDelayRange = new Vector2(0.4f, 0.9f);

    [HideInInspector] public List<UnityEngine.Vector3Int> mazePlannedOrder = new List<UnityEngine.Vector3Int>();
    [HideInInspector] public bool mazePlanned = false;
    [HideInInspector] public int mazeBuildCursor = 0;

    // AI 준비 단계 진행 상태 추적
    [HideInInspector] public bool mazeConstructionComplete = false;
    [HideInInspector] public bool unitPurchaseComplete = false;

    [Header("소유 객체 목록")]
    public List<Unit> ownedUnits = new List<Unit>();
    public List<AugmentData> chosenAugments = new List<AugmentData>();
    
    // 활성화된 몬스터 소환 증강 리스트 (일반 몬스터: 매 라운드 상대에게 추가 침공)
    private List<AugmentData> _activeMonsterSummonAugments = new List<AugmentData>();
    
    // 보유 중인 보스 증강 리스트 (영구 보유, 플레이어가 원할 때 소환)
    private List<AugmentData> _ownedBossAugments = new List<AugmentData>();

    [Header("Permanent Augment Bonuses")]
    [Tooltip("영구 증강으로 인한 아군 공격력(%) 가산. 0.1 = +10%")]
    public float permanentAttackDamagePercent = 0f;
    [Tooltip("영구 증강으로 인한 아군 공격속도(%) 가산. 0.1 = +10%")]
    public float permanentAttackSpeedPercent = 0f;

    [Header("하위 매니저 참조 (자동 할당)")]
    public FieldManager fieldManager;
    public ShopManager shopManager;
    public MonsterSpawner monsterSpawner;
    public AugmentManager augmentManager;
    public AstarGrid astarGrid;
    public Transform spawnPoint { get; private set; }
    public Transform goalTransform { get; private set; }

    [HideInInspector]
    public PlayerManager opponentManager;

    [Networked] public NetworkBool IsActivelyFighting { get; set; }

    #region 공격 시퀀스 관련 필드
    /// <summary>
    /// 현재 전투에서 공격자인지 여부. GameManagers에서 설정됨.
    /// </summary>
    [Networked] public NetworkBool IsAttackerInCurrentBattle { get; set; }

    /// <summary>
    /// 공격 시퀀스에서 소환 가능한 몬스터 풀
    /// </summary>
    public List<MonsterPoolEntry> AttackMonsterPool { get; private set; } = new List<MonsterPoolEntry>();
    #endregion

    private ChangeDetector _changeDetector;
    private bool _runtimeInitialized;
    public bool IsReadyForPlayerActions => _runtimeInitialized && playerId >= 0 && fieldManager != null;

    private bool HasStateAuthorityOrNoNetwork()
    {
        if (Object == null || Runner == null || !Runner.IsRunning)
        {
            return true;
        }
        return Object.HasStateAuthority;
    }

    private static string NormalizeShopUnitKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return string.Empty;
        }

        return key.Replace("(Clone)", string.Empty).Trim();
    }

    public void PublishShopSnapshot(IReadOnlyList<ShopItem> items, bool[] soldFlags, string context)
    {
        if (Object == null || !Object.IsValid || !Object.HasStateAuthority)
        {
            return;
        }

        int count = Mathf.Clamp(items?.Count ?? 0, 0, SHOP_SNAPSHOT_CAPACITY);
        for (int i = 0; i < SHOP_SNAPSHOT_CAPACITY; i++)
        {
            ShopSnapshotUnitKeys.Set(i, string.Empty);
            ShopSnapshotStarLevels.Set(i, 0);
            ShopSnapshotSoldFlags.Set(i, 0);
        }

        for (int i = 0; i < count; i++)
        {
            var item = items[i];
            string unitKey = NormalizeShopUnitKey(item.UnitData != null ? item.UnitData.name : string.Empty);
            int starLevel = item.StarLevel > 0 ? item.StarLevel : 1;
            int sold = soldFlags != null && i < soldFlags.Length && soldFlags[i] ? 1 : 0;

            ShopSnapshotUnitKeys.Set(i, unitKey);
            ShopSnapshotStarLevels.Set(i, starLevel);
            ShopSnapshotSoldFlags.Set(i, sold);
        }

        ShopSnapshotCount = count;
        ShopSnapshotRound = (GameManagers.Instance != null && GameManagers.Instance.IsReadyForNetworkAccess)
            ? GameManagers.Instance.currentRound
            : 0;

        if (ShopSnapshotRevision >= int.MaxValue - 1)
        {
            ShopSnapshotRevision = 1;
        }
        else
        {
            ShopSnapshotRevision++;
        }
    }

    public bool TryGetShopSnapshot(out string[] unitKeys, out int[] starLevels, out bool[] soldFlags, out int revision, out int round)
    {
        unitKeys = System.Array.Empty<string>();
        starLevels = System.Array.Empty<int>();
        soldFlags = System.Array.Empty<bool>();
        revision = 0;
        round = 0;

        if (Object == null || !Object.IsValid)
        {
            return false;
        }

        revision = ShopSnapshotRevision;
        round = ShopSnapshotRound;
        int count = Mathf.Clamp(ShopSnapshotCount, 0, SHOP_SNAPSHOT_CAPACITY);
        if (revision <= 0 || count <= 0)
        {
            return false;
        }

        unitKeys = new string[count];
        starLevels = new int[count];
        soldFlags = new bool[count];

        for (int i = 0; i < count; i++)
        {
            unitKeys[i] = NormalizeShopUnitKey(ShopSnapshotUnitKeys[i].ToString());
            starLevels[i] = Mathf.Max(1, ShopSnapshotStarLevels[i]);
            soldFlags[i] = ShopSnapshotSoldFlags[i] != 0;
        }

        return true;
    }

    // Pending unit registrations received before FieldManager is ready
    private struct PendingUnitReg
    {
        public NetworkObject unitNO;
        public int x;
        public int y;
        public string unitDataKey;
        public int starLevel;
    }
    private List<PendingUnitReg> _pendingUnitRegs = new List<PendingUnitReg>();

     void Awake()
    {
        // Awake는 그대로 유지하여 하위 컴포넌트 참조를 미리 찾아둡니다.
        fieldManager = GetComponentInChildren<FieldManager>();
        shopManager = GetComponentInChildren<ShopManager>();
        monsterSpawner = GetComponentInChildren<MonsterSpawner>();
        augmentManager = GetComponentInChildren<AugmentManager>();
    }

    public override void Spawned()
    {
        bool isHostMigration = HostMigrationHandler.Instance != null && HostMigrationHandler.Instance.IsMigrating;
        _runtimeInitialized = isHostMigration;

        // Rpc_InitializePlayer 이전에는 playerId가 미확정 상태임을 명시하여
        // Spawned 단계의 재바인딩에서 잘못된 슬롯/필드 매칭을 방지합니다.
        if (!isHostMigration && Object != null && Object.HasStateAuthority)
        {
            playerId = -1;
        }

        if (Object != null && Object.HasStateAuthority)
        {
            health = initialHealth;
            gold = initialGold;
            wallCount = initialWallCount;
        }

        _changeDetector = GetChangeDetector(ChangeDetector.Source.SimulationState);

        // Host Migration 복원 직후에도 런타임 참조가 비지 않도록 즉시 재결선
        RebindRuntimeReferencesAfterMigration("PlayerManager.Spawned", false);

        var attackSeqMgr = GetComponent<AttackSequenceManager>();
        if (attackSeqMgr == null)
        {
            attackSeqMgr = gameObject.AddComponent<AttackSequenceManager>();
        }
        if (attackSeqMgr.Owner != this)
        {
            attackSeqMgr.Initialize(this);
        }
    }

    public override void Render()
    {
        if (_changeDetector == null)
        {
            _changeDetector = GetChangeDetector(ChangeDetector.Source.SimulationState);
        }

        foreach (var propertyName in _changeDetector.DetectChanges(this))
        {
            if (propertyName == nameof(health) || propertyName == nameof(gold))
            {
                GameEvents.TriggerPlayerStatsChanged(playerId, this.health, this.gold);
            }
            if (propertyName == nameof(wallCount))
            {
                GameEvents.TriggerPlayerWallCountChanged(playerId, wallCount);
            }
        }
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public async void Rpc_InitializePlayer(int id, NetworkId gridId)
    {
        _runtimeInitialized = false;
        playerId = id;

        NetworkObject gridNO = null;
        int attempts = 0;
        while ((gridNO == null || !gridNO) && attempts < 120)
        {
            if (Runner != null)
            {
                Runner.TryFindObject(gridId, out gridNO);
            }
            if (gridNO == null)
            {
                await Cysharp.Threading.Tasks.UniTask.Yield(PlayerLoopTiming.FixedUpdate);
            }
            attempts++;
        }
        if (gridNO == null)
        {
            // Debug.LogError($"[Player {playerId}]: gridNetworkObject resolve 실패");
            return;
        }
        //Debug.Log($"[Player {playerId}]: gridNetworkObject를 성공적으로 받았습니다. (ID: {gridNetworkObject.Id})");

        var gridInstance = gridNO.gameObject;

        // 3D Ground 오브젝트 찾기
        GameObject ground3D = null;
        // 우선 활성화된 오브젝트 중에서 이름이 "Ground" 또는 "Field"인 것을 찾습니다.
        var groundCandidates = gridInstance.GetComponentsInChildren<Transform>(true)
            .Where(t => t != null && (t.name == "Ground" || t.name == "Field"))
            .Select(t => t.gameObject)
            .ToList();

        ground3D = groundCandidates.FirstOrDefault(go => go != null && go.activeInHierarchy);
        // 폴백: 없다면 첫 후보를 사용
        if (ground3D == null)
        {
            ground3D = groundCandidates.FirstOrDefault();
        }

        this.astarGrid = gridInstance.GetComponentInChildren<AstarGrid>();

        // 각 컴포넌트/오브젝트를 찾았는지 확인하는 로그
        // Debug.Log($"[Player {playerId}]: 3D Ground 찾음? -> {(ground3D != null)}");
        // Debug.Log($"[Player {playerId}]: AstarGrid 찾음? -> {(this.astarGrid != null)}");

        // AstarGrid 초기화는 FieldManager 초기화 이후에 수행하여 3D 그리드 정보를 공유합니다.

        // 하위 매니저 초기화
        // 3D Ground 기반 초기화
        if (fieldManager)
        {
            if (ground3D != null)
            {
                // Debug.Log($"[Player {playerId}]: FieldManager를 3D 모드로 초기화합니다.");
                fieldManager.Initialize(this, ground3D);
            }
            else
            {
                // Debug.LogError($"[Player {playerId}]: FieldManager 초기화 실패 - Ground 오브젝트를 찾을 수 없습니다!");
            }
        }

        // FieldManager 초기화 후 스폰/골 위치를 동적으로 설정
        // - 골: 필드 정 가운데 그리드
        // - 스폰: 동서남북 테두리 구멍 4곳 중 랜덤
        SetupSpawnAndGoalPositions(gridInstance);
        // Debug.Log($"[Player {playerId}]: SpawnPoint 위치 -> {(this.spawnPoint != null ? this.spawnPoint.position.ToString() : "null")}");
        // Debug.Log($"[Player {playerId}]: Goal 위치 -> {(this.goalTransform != null ? this.goalTransform.position.ToString() : "null")}");

        // 이제 FieldManager가 준비되었으므로 AstarGrid를 FieldManager와 동기화하여 초기화합니다.
        if (this.astarGrid != null)
        {
            this.astarGrid.fieldManager = fieldManager;
            this.astarGrid.useFieldManagerGrid = true;
            this.astarGrid.Initialize();
        }
        else
        {
            // Debug.LogError($"[Player {playerId}]: AstarGrid 컴포넌트를 찾지 못해 경로 탐색을 초기화할 수 없습니다.");
        }
        if (shopManager) shopManager.playerManager = this;

        if (monsterSpawner)
        {
            var waveDatabase = AddressablesManager.Instance?.WaveDatabase;
            monsterSpawner.Initialize(this, this.astarGrid, waveDatabase, this.spawnPoint, this.goalTransform);
        }

        if (augmentManager) augmentManager.playerManager = this;

        // AttackSequenceManager 초기화
        var attackSeqMgr = GetComponent<AttackSequenceManager>();
        if (attackSeqMgr == null)
        {
            attackSeqMgr = gameObject.AddComponent<AttackSequenceManager>();
        }
        attackSeqMgr.Initialize(this);
        RebindRuntimeReferencesAfterMigration("Rpc_InitializePlayer", true);

        // CameraManager 초기화 (로컬 플레이어만)
        if (Object.HasInputAuthority && CameraManager.Instance != null)
        {
            CameraManager.Instance.Initialize(this);
        }

        // AttackSequenceUIController 동적 로드 (로컬 플레이어만)
        if (Object.HasInputAuthority)
        {
            await AttackSequenceUIController.GetOrCreateAsync(this, attackSeqMgr);
        }

        IsActivelyFighting = false;
        // Debug.Log($"--- Player {playerId} RPC 초기화 완료 ---");

        // Process any unit registrations that arrived early
        if (_pendingUnitRegs.Count > 0)
        {
            // Make a copy to avoid modification during iteration
            var pending = new List<PendingUnitReg>(_pendingUnitRegs);
            _pendingUnitRegs.Clear();
            foreach (var p in pending)
            {
                await RPC_RegisterUnitAt_Internal(p.unitNO, p.x, p.y, p.unitDataKey, p.starLevel);
            }
        }

        _runtimeInitialized = true;
    }

    public void RebindRuntimeReferencesAfterMigration(string context, bool verboseFailure = true)
    {
        fieldManager = fieldManager != null ? fieldManager : GetComponentInChildren<FieldManager>(true);
        shopManager = shopManager != null ? shopManager : GetComponentInChildren<ShopManager>(true);
        monsterSpawner = monsterSpawner != null ? monsterSpawner : GetComponentInChildren<MonsterSpawner>(true);
        augmentManager = augmentManager != null ? augmentManager : GetComponentInChildren<AugmentManager>(true);

        if (fieldManager != null)
        {
            fieldManager.playerManager = this;
        }

        if (shopManager != null)
        {
            shopManager.playerManager = this;
        }

        if (augmentManager != null)
        {
            augmentManager.playerManager = this;
        }

        bool gridRebound = false;
        if (astarGrid == null || !IsGridOwnedByCurrentRunner(astarGrid))
        {
            // 1차: 자기 하위에서 탐색
            var childGrid = GetComponentInChildren<AstarGrid>(true);
            if (IsGridOwnedByCurrentRunner(childGrid))
            {
                astarGrid = childGrid;
                gridRebound = true;
            }
            else
            {
                astarGrid = null;
            }
        }

        GameObject gridRoot = ResolveGridRootObject(context, verboseFailure);
        if ((astarGrid == null || !IsGridOwnedByCurrentRunner(astarGrid))
            && TryResolveGridFromRunner(context, verboseFailure, out var resolvedGridRoot))
        {
            gridRoot = resolvedGridRoot != null ? resolvedGridRoot : gridRoot;
            gridRebound = true;
        }

        if (gridRoot == null)
        {
            gridRoot = ResolveGridRootObject(context, verboseFailure);
        }

        bool fieldReinitialized = false;
        GameObject ground3D = fieldManager != null ? fieldManager.ground3D : null;
        if (ground3D != null && !IsGameObjectOwnedByCurrentRunner(ground3D))
        {
            ground3D = null;
        }
        if (ground3D == null)
        {
            ground3D = ResolveGroundObject(gridRoot);
        }

        bool spawnInvalid = spawnPoint == null || !IsTransformOwnedByCurrentRunner(spawnPoint);
        bool goalInvalid = goalTransform == null || !IsTransformOwnedByCurrentRunner(goalTransform);
        bool spawnParentMismatch = gridRoot != null && spawnPoint != null && spawnPoint.parent != gridRoot.transform;
        bool goalParentMismatch = gridRoot != null && goalTransform != null && goalTransform.parent != gridRoot.transform;

        if (fieldManager != null && ground3D != null
            && (fieldManager.ground3D == null || fieldManager.ground3D != ground3D))
        {
            fieldManager.Initialize(this, ground3D);
            fieldReinitialized = true;
        }

        bool spawnGoalRefreshed = false;
        bool requireSpawnGoalRefresh = spawnInvalid
                                       || goalInvalid
                                       || spawnParentMismatch
                                       || goalParentMismatch
                                       || fieldReinitialized
                                       || gridRebound;
        if (fieldManager != null
            && fieldManager.ground3D != null
            && gridRoot != null
            && requireSpawnGoalRefresh)
        {
            SetupSpawnAndGoalPositions(gridRoot);
            spawnGoalRefreshed = true;
        }

        if (astarGrid != null && fieldManager != null)
        {
            bool needAstarRebind = astarGrid.fieldManager != fieldManager
                                   || !astarGrid.useFieldManagerGrid
                                   || fieldReinitialized
                                   || gridRebound;
            if (needAstarRebind)
            {
                astarGrid.fieldManager = fieldManager;
                astarGrid.useFieldManagerGrid = true;
                astarGrid.Initialize();
            }
        }

        if (monsterSpawner != null)
        {
            bool shouldReinitializeSpawner = monsterSpawner.monsterParent == null
                                             || fieldReinitialized
                                             || gridRebound
                                             || spawnGoalRefreshed;
            if (shouldReinitializeSpawner && astarGrid != null && spawnPoint != null && goalTransform != null)
            {
                var waveDatabase = AddressablesManager.Instance?.WaveDatabase;
                monsterSpawner.Initialize(this, astarGrid, waveDatabase, spawnPoint, goalTransform);
            }

            monsterSpawner.EnsureRuntimeReferencesForMigration(context, verboseFailure);
        }

        if (fieldManager != null)
        {
            fieldManager.RebuildWallMapsAfterMigration($"PlayerManager.{context}", verboseFailure, out _);
            fieldManager.RebuildUnitMapAfterMigration($"PlayerManager.{context}", verboseFailure, out _);
        }

        if (verboseFailure && !IsRuntimeReady(out string reason))
        {
            // Debug.LogWarning($"[PlayerManager] 런타임 참조 재결선 미완료 ({context}) player={playerId}, reason={reason}");
        }
    }

    public bool IsRuntimeReady(out string reason)
    {
        if (fieldManager == null)
        {
            reason = "fieldManager=null";
            return false;
        }

        if (fieldManager.ground3D == null)
        {
            reason = "fieldManager.ground3D=null";
            return false;
        }

        if (!fieldManager.IsWallMapReady)
        {
            reason = "fieldManager.wallMapNotReady";
            return false;
        }

        if (!fieldManager.IsUnitMapReady)
        {
            reason = "fieldManager.unitMapNotReady";
            return false;
        }

        var units = fieldManager.GetAlliedUnitsOnField();
        var missingDataUnit = units.FirstOrDefault(unit => unit != null && unit.Data == null);
        if (missingDataUnit != null)
        {
            reason = $"unitDataMissing:{missingDataUnit.name}";
            return false;
        }

        var missingProxyUnit = units.FirstOrDefault(unit => unit != null && !unit.HasAnimationEventProxy());
        if (missingProxyUnit != null)
        {
            reason = $"unitAnimProxyMissing:{missingProxyUnit.name}";
            return false;
        }

        if (astarGrid == null)
        {
            reason = "astarGrid=null";
            return false;
        }

        if (spawnPoint == null)
        {
            reason = "spawnPoint=null";
            return false;
        }

        if (goalTransform == null)
        {
            reason = "goalTransform=null";
            return false;
        }

        if (monsterSpawner == null)
        {
            reason = "monsterSpawner=null";
            return false;
        }

        if (!monsterSpawner.IsRuntimeReady(out string spawnerReason))
        {
            reason = $"monsterSpawnerNotReady({spawnerReason})";
            return false;
        }

        reason = null;
        return true;
    }

    private GameObject ResolveGridRootObject(string context, bool verboseFailure)
    {
        if (IsGridOwnedByCurrentRunner(astarGrid))
        {
            var rootFromGrid = GetGridRootFromAstar(astarGrid);
            if (rootFromGrid != null)
            {
                return rootFromGrid;
            }
        }

        if (spawnPoint != null && IsTransformOwnedByCurrentRunner(spawnPoint))
        {
            return spawnPoint.parent != null ? spawnPoint.parent.gameObject : spawnPoint.gameObject;
        }

        if (goalTransform != null && IsTransformOwnedByCurrentRunner(goalTransform))
        {
            return goalTransform.parent != null ? goalTransform.parent.gameObject : goalTransform.gameObject;
        }

        if (TryResolveGridFromRunner(context, verboseFailure, out var resolvedGridRoot))
        {
            return resolvedGridRoot;
        }

        return null;
    }

    private bool TryResolveGridFromRunner(string context, bool verboseFailure, out GameObject resolvedGridRoot)
    {
        resolvedGridRoot = null;

        if (Runner == null || !Runner.IsRunning)
        {
            return false;
        }

        bool hasExpectedAnchor = TryGetExpectedFieldAnchor(out var expectedAnchor);
        if (verboseFailure && hasExpectedAnchor)
        {
            // Debug.Log($"[PlayerManager] Grid resolve anchor ({context}) player={playerId}, expected={expectedAnchor}");
        }

        AstarGrid bestGrid = null;
        NetworkObject bestGridNO = null;
        float bestScore = float.MinValue;

        var runnerObjects = Runner.GetAllNetworkObjects();
        if (runnerObjects != null)
        {
            foreach (var no in runnerObjects)
            {
                if (no == null || !no.IsValid || no.gameObject == null)
                {
                    continue;
                }

                if (Object != null && no == Object)
                {
                    continue;
                }

                var candidateGrid = no.GetComponentInChildren<AstarGrid>(true);
                if (candidateGrid == null)
                {
                    continue;
                }

                float score = 0f;

                if (Object != null && Object.InputAuthority != PlayerRef.None && no.InputAuthority == Object.InputAuthority)
                {
                    score += 500f;
                }

                // Host migration 이후에도 playerId 슬롯 기준으로 같은 필드를 재결선한다.
                if (hasExpectedAnchor)
                {
                    float slotDistance = (no.transform.position - expectedAnchor).sqrMagnitude;
                    score += Mathf.Clamp(1500f - (slotDistance * 120f), 0f, 1500f);
                }

                float sqrDistance = (no.transform.position - transform.position).sqrMagnitude;
                score += Mathf.Clamp(100f - (sqrDistance * 5f), 0f, 100f);

                if (fieldManager != null && candidateGrid.fieldManager == fieldManager)
                {
                    score += 80f;
                }

                if (spawnPoint != null && spawnPoint.parent == no.transform)
                {
                    score += 40f;
                }

                if (goalTransform != null && goalTransform.parent == no.transform)
                {
                    score += 40f;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    bestGrid = candidateGrid;
                    bestGridNO = no;
                }
            }
        }

        if (bestGrid == null)
        {
            var allGrids = UnityEngine.Object.FindObjectsOfType<AstarGrid>(true);
            foreach (var grid in allGrids)
            {
                if (grid == null)
                {
                    continue;
                }

                var no = grid.GetComponentInParent<NetworkObject>();
                if (no != null && no.Runner != Runner)
                {
                    continue;
                }

                float score = 0f;
                if (Object != null && Object.InputAuthority != PlayerRef.None && no != null && no.InputAuthority == Object.InputAuthority)
                {
                    score += 500f;
                }

                Vector3 anchorPos = no != null ? no.transform.position : grid.transform.position;

                if (hasExpectedAnchor)
                {
                    float slotDistance = (anchorPos - expectedAnchor).sqrMagnitude;
                    score += Mathf.Clamp(1500f - (slotDistance * 120f), 0f, 1500f);
                }

                float sqrDistance = (anchorPos - transform.position).sqrMagnitude;
                score += Mathf.Clamp(100f - (sqrDistance * 5f), 0f, 100f);

                if (fieldManager != null && grid.fieldManager == fieldManager)
                {
                    score += 80f;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    bestGrid = grid;
                    bestGridNO = no;
                }
            }
        }

        if (bestGrid == null)
        {
            return false;
        }

        astarGrid = bestGrid;
        resolvedGridRoot = bestGridNO != null ? bestGridNO.gameObject : GetGridRootFromAstar(bestGrid);

        if (verboseFailure)
        {
            // Debug.Log($"[PlayerManager] AstarGrid 재결선 성공 ({context}) player={playerId}, grid={bestGrid.name}, root={resolvedGridRoot?.name ?? "null"}");
        }

        return true;
    }

    private bool TryGetExpectedFieldAnchor(out Vector3 expectedPosition)
    {
        expectedPosition = Vector3.zero;

        if (playerId < 0)
        {
            return false;
        }

        var gm = GameManagers.Instance;
        if (gm == null)
        {
            return false;
        }

        expectedPosition = gm.player1BasePosition + gm.GetResolvedPlayerOffset() * playerId;
        return true;
    }

    private bool IsGridOwnedByCurrentRunner(AstarGrid grid)
    {
        if (grid == null)
        {
            return false;
        }

        if (Runner == null || !Runner.IsRunning)
        {
            return true;
        }

        var no = grid.GetComponentInParent<NetworkObject>();
        return no == null || no.Runner == Runner;
    }

    private bool IsTransformOwnedByCurrentRunner(Transform target)
    {
        if (target == null)
        {
            return false;
        }

        if (Runner == null || !Runner.IsRunning)
        {
            return true;
        }

        var no = target.GetComponentInParent<NetworkObject>();
        return no == null || no.Runner == Runner;
    }

    private bool IsGameObjectOwnedByCurrentRunner(GameObject target)
    {
        if (target == null)
        {
            return false;
        }

        if (Runner == null || !Runner.IsRunning)
        {
            return true;
        }

        var no = target.GetComponentInParent<NetworkObject>();
        return no == null || no.Runner == Runner;
    }

    private static GameObject GetGridRootFromAstar(AstarGrid grid)
    {
        if (grid == null)
        {
            return null;
        }

        var gridNetworkObject = grid.GetComponentInParent<NetworkObject>();
        if (gridNetworkObject != null)
        {
            return gridNetworkObject.gameObject;
        }

        if (grid.transform.parent != null)
        {
            return grid.transform.parent.gameObject;
        }

        return grid.gameObject;
    }

    private static GameObject ResolveGroundObject(GameObject gridRoot)
    {
        if (gridRoot == null)
        {
            return null;
        }

        var candidates = gridRoot.GetComponentsInChildren<Transform>(true)
            .Where(t => t != null && (t.name == "Ground" || t.name == "Field"))
            .Select(t => t.gameObject)
            .ToList();

        var activeGround = candidates.FirstOrDefault(go => go != null && go.activeInHierarchy);
        return activeGround ?? candidates.FirstOrDefault();
    }

    #region RPC Methods (네트워크 동기화)
    /// <summary>
    /// 서버에서 생성한 상점 아이템을 모든 클라이언트에 동기화합니다.
    /// </summary>

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public async void RPC_SyncShopItems(string[] unitDataNames, int[] starLevels)
    {
        // 서버는 이미 상점 아이템을 가지고 있으므로 무시
        if (Object != null && Object.HasStateAuthority) return;

        if (shopManager != null)
        {
            await shopManager.SetShopItemsFromServerAsync(unitDataNames, starLevels);
            // Debug.Log($"<color=cyan>[RPC_SyncShopItems] Player {playerId}: {unitDataNames.Length}개 상점 아이템 동기화 완료</color>");
        }
    }

    /// <summary>
    /// 서버에서 생성한 증강체 목록을 모든 클라이언트에 동기화합니다.
    /// </summary>

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public async void RPC_SyncPresentedAugments(string[] augmentNames)
    {
        // 서버는 이미 증강체 목록을 가지고 있으므로 무시
        if (Object != null && Object.HasStateAuthority) return;

        if (augmentManager != null)
        {
            await augmentManager.SetPresentedAugmentsByNamesAsync(augmentNames);
            // Debug.Log($"<color=magenta>[RPC_SyncPresentedAugments] Player {playerId}: {augmentNames.Length}개 증강체 동기화 완료</color>");
        }
    }

    /// <summary>
    /// 클라이언트가 서버에 상점 및 증강체 데이터 동기화를 요청합니다.
    /// </summary>

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_RequestSyncData()
    {
        // 서버만 처리
        if (Object == null || !Object.HasStateAuthority) return;
        
        // Debug.Log($"<color=yellow>[RPC_RequestSyncData] Player {playerId}에게 데이터 동기화 요청 수신</color>");
        
        // 상점 동기화
        if (shopManager != null)
        {
            var items = shopManager.GetCurrentShopItems();
            if (items.Count > 0)
            {
                string[] shopNames = items.Select(i => i.UnitData?.name ?? "").ToArray();
                int[] shopStars = items.Select(i => i.StarLevel).ToArray();
                RPC_SyncShopItems(shopNames, shopStars);
            }
        }
        
        // 증강체 동기화
        if (augmentManager != null)
        {
            var augments = augmentManager.GetPresentedAugments();
            if (augments.Count > 0)
            {
                string[] augNames = augments.Select(a => a?.augmentName ?? "").ToArray();
                RPC_SyncPresentedAugments(augNames);
            }
        }

        RPC_SyncOwnedMagicScrolls(BuildOwnedMagicScrollNameArray());
    }

    /// <summary>
    /// 서버에서 적용된 영구 증강 보너스를 클라이언트에 동기화합니다.
    /// </summary>

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_SyncPermanentBonuses(float attackDamagePercent, float attackSpeedPercent)
    {
        this.permanentAttackDamagePercent = attackDamagePercent;
        this.permanentAttackSpeedPercent = attackSpeedPercent;
        ApplyPermanentBonusesToUnitsOnField();
        // Debug.Log($"<color=cyan>[RPC_SyncPermanentBonuses] Player {playerId}: AttackDmg={attackDamagePercent:P0}, AttackSpd={attackSpeedPercent:P0}</color>");
    }


    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_ApplyPermanentWalls(int[] flatPositions)
    {
        if (fieldManager != null)
        {
            fieldManager.ApplyPermanentWallsFromServer(flatPositions);
        }
    }


    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public async void RPC_RegisterUnitAt(NetworkId unitId, int x, int y, string unitDataKey, int starLevel)
    {
        try
        {
            // Debug.Log($"<color=yellow>[RPC_RegisterUnitAt] recv pos=({x},{y}) key='{unitDataKey}' star={starLevel} stateAuth={(Object != null && Object.HasStateAuthority)} id={unitId}</color>");
            if (Object != null && Object.HasStateAuthority) return;
            NetworkObject unitNO = null;
            bool resolved = false;
            int attempts = 0;
            do
            {
                if (Runner != null)
                {
                    resolved = Runner.TryFindObject(unitId, out unitNO);
                }
                if (!resolved)
                {
                    await Cysharp.Threading.Tasks.UniTask.Yield();
                    attempts++;
                }
            } while (!resolved && attempts < 300);
            if (!resolved || unitNO == null)
            {
                // Debug.LogWarning($"<color=yellow>[RPC_RegisterUnitAt] failed to resolve NetworkObject by NetworkId='{unitId}' key='{unitDataKey}'</color>");
                return;
            }

            if (fieldManager == null || fieldManager.ground3D == null)
            {
                // Debug.Log($"<color=yellow>[RPC_RegisterUnitAt] queued. fieldManagerReady={(fieldManager != null)} groundReady={(fieldManager != null && fieldManager.ground3D != null)}</color>");
                _pendingUnitRegs.Add(new PendingUnitReg
                {
                    unitNO = unitNO,
                    x = x,
                    y = y,
                    unitDataKey = unitDataKey,
                    starLevel = starLevel
                });
                return;
            }

            await RPC_RegisterUnitAt_Internal(unitNO, x, y, unitDataKey, starLevel);
            // Debug.Log($"<color=yellow>[RPC_RegisterUnitAt] dispatched to Internal for pos=({x},{y})</color>");
        }
        catch (System.Exception ex)
        {
            // Debug.LogError($"[RPC_RegisterUnitAt] exception: {ex.Message}");
        }
    }

    private async Cysharp.Threading.Tasks.UniTask RPC_RegisterUnitAt_Internal(NetworkObject unitNO, int x, int y, string unitDataKey, int starLevel)
    {
        try
        {
            if (fieldManager == null)
            {
                // Debug.LogWarning($"<color=yellow>[RPC_Internal] fieldManager null</color>");
                return;
            }
            var unit = unitNO.GetComponent<Unit>();
            if (unit == null)
            {
                // Debug.LogWarning($"<color=yellow>[RPC_Internal] Unit component missing on '{unitNO?.name}'</color>");
                return;
            }
            var pos = new Vector3Int(x, y, 0);
            // Debug.Log($"<color=yellow>[RPC_Internal] start pos={pos} currentData={(unit.Data != null ? unit.Data.name : "null")} key='{unitDataKey}'</color>");
            if (fieldManager.IsUnitAt(pos))
            {
                var existingAtPos = fieldManager.GetUnitAt(pos);
                if (existingAtPos != null && existingAtPos != unit)
                {
                    // Debug.LogWarning($"<color=yellow>[RPC_Internal] position already occupied by another unit. Replacing. pos={pos}</color>");
                }
            }

            if (fieldManager.statusBarPrefab != null)
            {
                var preExistingStatusBar = unit.GetComponentInChildren<StatusBarUI>(includeInactive: true);
                if (preExistingStatusBar == null)
                {
                    var statusBarGO = UnityEngine.Object.Instantiate(fieldManager.statusBarPrefab, unit.transform);
                    var statusBarUI = statusBarGO.GetComponent<StatusBarUI>();
                    if (statusBarUI != null)
                    {
                        unit.SetStatusBar(statusBarUI);
                    }
                }
            }

            bool needInit = unit.Data == null || (!string.IsNullOrEmpty(unitDataKey) && unit.Data.name != unitDataKey);
            if (needInit)
            {
                UnitData data = null;
                if (!string.IsNullOrEmpty(unitDataKey))
                {
                    if (LoadManager.Instance == null)
                    {
                        await Cysharp.Threading.Tasks.UniTask.WaitUntil(() => LoadManager.Instance != null);
                    }
                    var lmReady = LoadManager.Instance.IsReady;
                    if (!lmReady)
                    {
                        // Debug.Log($"<color=yellow>[RPC_Internal] waiting LoadManager ready...</color>");
                        await LoadManager.Instance.WaitUntilReady();
                    }
                    data = LoadManager.Instance.GetUnitData(unitDataKey);
                    if (data == null)
                    {
                        // Debug.Log($"<color=yellow>[RPC_Internal] LoadManager miss for key='{unitDataKey}'. Trying Addressables fallback...</color>");
                        data = await AssetLoader.LoadAssetAsync<UnitData>(unitDataKey);
                    }
                }
                if (data != null)
                {
                    await unit.Initialize(data, starLevel, this);
                    // Debug.Log($"<color=yellow>[RPC_Internal] unit.Initialize OK data='{unit.Data?.name}' star={starLevel}</color>");
                }
                else
                {
                    // Debug.LogError($"[Player {playerId}] RPC_RegisterUnitAt could not resolve UnitData for key '{unitDataKey}'.");
                }
            }

            if (fieldManager.statusBarPrefab != null)
            {
                var existingStatusBar = unit.GetComponentInChildren<StatusBarUI>(includeInactive: true);
                if (existingStatusBar == null)
                {
                    var statusBarGO = UnityEngine.Object.Instantiate(fieldManager.statusBarPrefab, unit.transform);
                    var statusBarUI = statusBarGO.GetComponent<StatusBarUI>();
                    if (statusBarUI != null)
                    {
                        unit.SetStatusBar(statusBarUI);
                    }
                }
            }
            if (unit.Data == null)
            {
                // Debug.LogWarning($"<color=yellow>[RPC_Internal] unit.Data still null after resolve. Skip Register. key='{unitDataKey}', pos={pos}</color>");
                return;
            }
            fieldManager.RegisterUnitAt(unit, pos);
            // Debug.Log($"<color=#3399FF>[ClientFlow] RegisterUnitAt via RPC -> {pos} (Player {playerId}) data='{unit.Data?.name}'</color>");
        }
        catch (System.Exception ex)
        {
            // Debug.LogError($"[RPC_Internal] exception: {ex.Message}");
        }
    }

    // ... (이하 나머지 코드는 기존과 동일) ...
    #endregion


    public void SetFightingState(bool isFighting)
    {
        // StartBattleForPlayers에서 호스트가 호출하므로 권한 체크 없이 직접 설정
        // (Networked 속성은 자동으로 동기화됨)
        if (IsActivelyFighting == isFighting) return; // 변경 없으면 스킵
        
        IsActivelyFighting = isFighting;
        // Debug.Log($"<color=magenta>[SetFightingState] Player {playerId}: isFighting={isFighting} 설정됨</color>");
    }

    #region Public Getters & Stat Modifiers

    public int GetHealth() => health;
    public int GetGold() => gold;
    public int GetWallCount() => wallCount;
    public int GetWallReserveK() => wallReserveK;
    public Vector2 GetWallBuildDelayRange() => NormalizeDelayRange(wallBuildDelayRange);
    public Vector2 GetUnitPurchaseDelayRange() => NormalizeDelayRange(unitPurchaseDelayRange);
    public Vector2 GetUnitMoveDelayRange() => NormalizeDelayRange(unitMoveDelayRange);

    #region 몬스터 소환 증강 관리
    /// <summary>
    /// 일반 몬스터 소환 증강을 활성화 등록합니다. 
    /// 매 라운드 이 플레이어의 상대에게 추가 몬스터가 침공하게 됩니다.
    /// 같은 증강을 여러 번 선택하면 그 수만큼 누적됩니다.
    /// </summary>
    public void RegisterActiveMonsterSummonAugment(AugmentData augment)
    {
        if (augment != null)
        {
            _activeMonsterSummonAugments.Add(augment);
            // Debug.Log($"<color=orange>[PlayerManager] Player {playerId}: 몬스터 소환 증강 '{augment.augmentName}' 등록 (누적 {_activeMonsterSummonAugments.Count}개)</color>");
        }
    }

    /// <summary>
    /// 활성화된 몬스터 소환 증강 목록을 반환합니다.
    /// </summary>
    public List<AugmentData> GetActiveMonsterSummonAugments()
    {
        return _activeMonsterSummonAugments;
    }
    #endregion

    #region 보유 보스 관리
    public void AddOwnedBoss(AugmentData augment)
    {
        if (augment?.bossMonsterData != null)
        {
            _ownedBossAugments.Add(augment);
            // Debug.Log($"<color=red>[PlayerManager] Player {playerId}: 보스 '{augment.bossMonsterData.monsterName}' 보유 추가 (총 {_ownedBossAugments.Count}마리)</color>");
        }
    }

    public IReadOnlyList<AugmentData> GetOwnedBosses() => _ownedBossAugments;

    public bool ConsumeOwnedBoss(MonsterData bossData)
    {
        var augment = _ownedBossAugments.FirstOrDefault(a => a.bossMonsterData == bossData);
        if (augment != null)
        {
            _ownedBossAugments.Remove(augment);
            // Debug.Log($"<color=red>[PlayerManager] Player {playerId}: 보스 '{bossData.monsterName}' 소환 → 보유에서 제거 (남은 {_ownedBossAugments.Count}마리)</color>");
            return true;
        }
        return false;
    }
    #endregion

    #region 마법 스크롤 관리
    // 보유 중인 마법 스크롤 리스트
    private List<MagicScrollData> _ownedScrolls = new List<MagicScrollData>();
    
    /// <summary>
    /// 보유 중인 마법 스크롤 목록 (읽기 전용)
    /// </summary>
    public IReadOnlyList<MagicScrollData> OwnedScrolls => _ownedScrolls;

    /// <summary>
    /// 마법 스크롤을 플레이어 인벤토리에 추가합니다.
    /// </summary>
    public void AddMagicScroll(MagicScrollData scrollData)
    {
        if (scrollData != null)
        {
            _ownedScrolls.Add(scrollData);
            Debug.Log($"<color=magenta>[PlayerManager] Player {playerId}: 마법 스크롤 '{scrollData.scrollName}' 획득 (총 {_ownedScrolls.Count}개)</color>");

            PublishOwnedMagicScrollsChanged();
            SyncOwnedMagicScrollsToClientsIfAuthoritative();
        }
    }

    /// <summary>
    /// 마법 스크롤 사용 시 인벤토리에서 제거합니다.
    /// </summary>
    /// <returns>스크롤 보유 시 true, 미보유 시 false</returns>
    public bool TryConsumeMagicScroll(MagicScrollData scrollData)
    {
        if (scrollData == null) return false;
        
        // 같은 종류의 스크롤이 있는지 확인
        var found = _ownedScrolls.Find(s => s == scrollData || s.name == scrollData.name);
        if (found != null)
        {
            _ownedScrolls.Remove(found);
            Debug.Log($"<color=magenta>[PlayerManager] Player {playerId}: 마법 스크롤 '{scrollData.scrollName}' 사용 (남은 {_ownedScrolls.Count}개)</color>");

            PublishOwnedMagicScrollsChanged();
            SyncOwnedMagicScrollsToClientsIfAuthoritative();
            return true;
        }
        
        return false;
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public async void RPC_SyncOwnedMagicScrolls(string[] scrollDataNames)
    {
        if (Object != null && Object.HasStateAuthority)
        {
            return;
        }

        var syncedScrolls = new List<MagicScrollData>(scrollDataNames?.Length ?? 0);
        if (scrollDataNames != null)
        {
            foreach (string scrollDataName in scrollDataNames)
            {
                if (string.IsNullOrWhiteSpace(scrollDataName))
                {
                    continue;
                }

                MagicScrollData scrollData = await AssetLoader.LoadAssetAsync<MagicScrollData>(scrollDataName);
                if (scrollData != null)
                {
                    syncedScrolls.Add(scrollData);
                }
            }
        }

        _ownedScrolls = syncedScrolls;
        PublishOwnedMagicScrollsChanged();
    }

    private string[] BuildOwnedMagicScrollNameArray()
    {
        return _ownedScrolls
            .Where(scroll => scroll != null && !string.IsNullOrWhiteSpace(scroll.name))
            .Select(scroll => scroll.name)
            .ToArray();
    }

    private void PublishOwnedMagicScrollsChanged()
    {
        GameEvents.TriggerMagicScrollPoolChanged(playerId, _ownedScrolls);
    }

    private void SyncOwnedMagicScrollsToClientsIfAuthoritative()
    {
        if (Object != null && Object.HasStateAuthority)
        {
            RPC_SyncOwnedMagicScrolls(BuildOwnedMagicScrollNameArray());
        }
    }
    #endregion
    /// <summary>
    /// 라운드별 공격 몬스터 풀을 갱신합니다. (기본 웨이브 + 증강 공격 유닛 + 보스)
    /// </summary>
    /// <param name="round">현재 라운드</param>
    /// <param name="currentBattleOpponentId">현재 전투에서 매칭된 상대 ID (-1이면 opponentManager 사용)</param>
    public void RefreshAttackMonsterPool(int round, int currentBattleOpponentId = -1)
    {
        AttackMonsterPool.Clear();
        
        // 1. 기본 웨이브 몬스터 가져오기
        var waveDatabase = AddressablesManager.Instance?.WaveDatabase;
        if (waveDatabase != null)
        {
            var waveData = waveDatabase.GetWaveForRound(round);
            if (waveData?.monsters != null)
            {
                foreach (var entry in waveData.monsters)
                {
                    if (entry?.monsterData != null && entry.count > 0)
                    {
                        // 기존 풀에 같은 몬스터가 있으면 수량 추가
                        var existing = AttackMonsterPool.Find(p => p.MonsterData == entry.monsterData && !p.IsBoss);
                        if (existing != null)
                        {
                            existing.RemainingCount += entry.count;
                            existing.MaxCount += entry.count;
                        }
                        else
                        {
                            AttackMonsterPool.Add(new MonsterPoolEntry(entry.monsterData, entry.count));
                        }
                    }
                }
            }
        }

        // 2. 증강 공격 유닛 추가
        foreach (var augment in _activeMonsterSummonAugments)
        {
            if (augment?.monsterSpawnEntries == null) continue;
            
            foreach (var entry in augment.monsterSpawnEntries)
            {
                if (entry?.monsterData != null && entry.count > 0)
                {
                    var existing = AttackMonsterPool.Find(p => p.MonsterData == entry.monsterData && !p.IsBoss);
                    if (existing != null)
                    {
                        existing.RemainingCount += entry.count;
                        existing.MaxCount += entry.count;
                    }
                    else
                    {
                        AttackMonsterPool.Add(new MonsterPoolEntry(entry.monsterData, entry.count));
                    }
                }
            }
        }
        
        // 3. 보유 보스 표시 (영구 보유, 소환 시에만 제거)
        foreach (var augment in _ownedBossAugments)
        {
            if (augment?.bossMonsterData == null) continue;
            
            AttackMonsterPool.Add(new MonsterPoolEntry(
                augment.bossMonsterData,
                1,
                -1,
                -1,
                this.playerId
            ));
            
            // Debug.Log($"<color=red>[PlayerManager] Player {playerId}: 보유 보스 '{augment.bossMonsterData.monsterName}' 풀에 표시</color>");
        }

        // Debug.Log($"<color=magenta>[PlayerManager] Player {playerId}: 공격 몬스터 풀 갱신 완료 ({AttackMonsterPool.Count}종류, 보유 보스: {_ownedBossAugments.Count}마리)</color>");
        
        // 이벤트 발생 (UI 갱신용)
        GameEvents.TriggerMonsterPoolChanged(playerId, AttackMonsterPool);
    }

    /// <summary>
    /// 풀에서 몬스터 1마리를 소비합니다.
    /// </summary>
    /// <param name="monsterData">소비할 몬스터 데이터</param>
    /// <returns>성공 여부</returns>
    public bool TryConsumeMonsterFromPool(MonsterData monsterData)
    {
        if (monsterData == null) return false;

        var entry = AttackMonsterPool.Find(p => p.MonsterData == monsterData);
        if (entry == null || entry.IsEmpty) return false;

        entry.TryConsume();
        
        // 이벤트 발생 (UI 갱신용)
        GameEvents.TriggerMonsterPoolChanged(playerId, AttackMonsterPool);
        return true;
    }
    #endregion

    #region 스탯 및 자원 관리
    public void AddPermanentAttackDamagePercent(float percent)
    {
        permanentAttackDamagePercent += percent;
        ApplyPermanentBonusesToUnitsOnField();
        
        // 클라이언트에 동기화
        if (Object != null && Object.HasStateAuthority)
        {
            RPC_SyncPermanentBonuses(permanentAttackDamagePercent, permanentAttackSpeedPercent);
        }
    }

    public void AddPermanentAttackSpeedPercent(float percent)
    {
        permanentAttackSpeedPercent += percent;
        ApplyPermanentBonusesToUnitsOnField();
        
        // 클라이언트에 동기화
        if (Object != null && Object.HasStateAuthority)
        {
            RPC_SyncPermanentBonuses(permanentAttackDamagePercent, permanentAttackSpeedPercent);
        }
    }

    public void ApplyPermanentBonusesToUnitsOnField()
    {
        if (fieldManager != null)
        {
            fieldManager.ApplyPermanentBonusesToAllUnits();
        }
    }

    public bool SpendGold(int amount)
    {
        if (amount <= 0) return true;
        if (gold < amount) return false;
        if (!HasStateAuthorityOrNoNetwork())
        {
            return true;
        }
        gold -= amount;
        if (Runner == null || !Runner.IsRunning)
        {
            GameEvents.TriggerPlayerStatsChanged(playerId, this.health, this.gold);
        }
        return true;
    }

    public void AddGold(int amount)
    {
        if (amount <= 0) return;
        if (!HasStateAuthorityOrNoNetwork()) return;
        gold += amount;
        if (Runner == null || !Runner.IsRunning)
        {
            GameEvents.TriggerPlayerStatsChanged(playerId, this.health, this.gold);
        }
    }

    public void TakeDamage(int damage)
    {
        if (damage <= 0) return;
        if (!HasStateAuthorityOrNoNetwork()) return;
        health -= damage;

        // 체력 음수 허용: 라운드 종료 시 GameManagers에서 판정
        // (더 이상 즉시 탈락하지 않음)

        if (Runner == null || !Runner.IsRunning)
        {
            GameEvents.TriggerPlayerStatsChanged(playerId, this.health, this.gold);
        }
    }

    public void AddUnit(UnitData unitData, int starLevel)
    {
        if(fieldManager != null)
        {
            fieldManager.CreateAndPlaceUnitOnField(unitData, starLevel);
        }
    }

    public void AddWalls(int amount)
    {
        if (amount <= 0) return;
        if (!HasStateAuthorityOrNoNetwork()) return;
        wallCount += amount;
        mazeConstructionComplete = false;
        if (Runner == null || !Runner.IsRunning)
        {
            GameEvents.TriggerPlayerWallCountChanged(playerId, wallCount);
        }
    }

    public bool TryUseWall()
    {
        if (wallCount <= 0) return false;
        if (!HasStateAuthorityOrNoNetwork())
        {
            return true;
        }
        wallCount--;
        if (Runner == null || !Runner.IsRunning)
        {
            GameEvents.TriggerPlayerWallCountChanged(playerId, wallCount);
        }
        return true;
    }

    public void ReturnWall()
    {
        if (wallCount < MAX_WALL_COUNT)
        {
            if (!HasStateAuthorityOrNoNetwork())
            {
                return;
            }
            wallCount++;
            if (Runner == null || !Runner.IsRunning)
            {
                GameEvents.TriggerPlayerWallCountChanged(playerId, wallCount);
            }
        }
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_RequestCommandToServer(CommandType type, int[] intParams, string[] stringParams, Vector3[] vectorParams, RpcInfo info = default)
    {
        if (Runner == null || !Runner.IsServer) return; // 서버에서만 처리
        if (!IsReadyForPlayerActions)
        {
            Debug.LogWarning($"[RPC_RequestCommandToServer] Player init not ready. command={type}, playerId={playerId}");
            return;
        }

        if (intParams != null && intParams.Length > 0)
        {
            if (intParams[0] != playerId)
            {
                Debug.LogWarning($"[RPC_RequestCommandToServer] PlayerId mismatch corrected. cmd={type}, requested={intParams[0]}, authoritative={playerId}");
            }
            intParams[0] = playerId;
        }

        if (type == CommandType.PlaceWall || type == CommandType.RemoveWall)
        {
            string requestedPos = (vectorParams != null && vectorParams.Length > 0)
                ? Vector3Int.RoundToInt(vectorParams[0]).ToString()
                : "none";
            string source = info.Source != PlayerRef.None ? info.Source.ToString() : "None";
            Debug.Log($"[RPC_RequestCommandToServer] {type} accepted. authoritativePlayer={playerId}, requestedPos={requestedPos}, source={source}");
        }

        var gm = GameManagers.Instance;
        if (gm == null)
        {
            gm = FindObjectOfType<GameManagers>();
            if (gm == null)
            {
                // Debug.LogWarning("<color=green>[NetFlow] GameManagers not found on server yet. Dropping command.</color>");
                return;
            }
            // Debug.Log("<color=green>[NetFlow] GameManagers resolved via FindObjectOfType on server.</color>");
        }
        gm.RPC_BroadcastCommandToClients(type, intParams, stringParams, vectorParams);
    }

    private static Vector2 NormalizeDelayRange(Vector2 range)
    {
        float min = Mathf.Min(range.x, range.y);
        float max = Mathf.Max(range.x, range.y);
        if (min < 0f) min = 0f;
        if (max < min) max = min;
        return new Vector2(min, max);
    }

    #endregion

    #region 디버그 시각화

    void OnDrawGizmos()
    {
        // 미로 계획 시각화 (연한 노란색)
        if (mazePlanned && mazePlannedOrder != null && mazePlannedOrder.Count > 0 && fieldManager != null)
        {
            Gizmos.color = new Color(1f, 1f, 0f, 0.3f); // 연한 노란색

            foreach (var wallPos in mazePlannedOrder)
            {
                // 이미 건설된 벽은 건너뛰기
                if (fieldManager.HasWallAt(wallPos)) continue;

                // 그리드 좌표를 월드 좌표로 변환
                Vector3 worldPos = fieldManager.GridToWorld(wallPos);

                // 큐브로 표시 (연하게)
                Gizmos.DrawCube(worldPos, new Vector3(0.9f, 0.5f, 0.9f));
                Gizmos.DrawWireCube(worldPos, new Vector3(0.9f, 0.5f, 0.9f));
            }

            // 건설 순서 표시 (선으로 연결)
            Gizmos.color = new Color(1f, 0.8f, 0f, 0.5f);
            for (int i = 0; i < mazePlannedOrder.Count - 1; i++)
            {
                if (fieldManager.HasWallAt(mazePlannedOrder[i])) continue;
                if (fieldManager.HasWallAt(mazePlannedOrder[i + 1])) continue;

                Vector3 from = fieldManager.GridToWorld(mazePlannedOrder[i]) + Vector3.up * 0.5f;
                Vector3 to = fieldManager.GridToWorld(mazePlannedOrder[i + 1]) + Vector3.up * 0.5f;
                Gizmos.DrawLine(from, to);
            }
        }
    }

    #endregion

    #region Spawn/Goal 위치 설정

    /// <summary>
    /// 스폰 위치와 골 위치를 동적으로 설정합니다.
    /// - 골: 필드 정 가운데 그리드
    /// - 스폰: 남쪽(하단 가운데) 고정 - AI 웨이브 소환용
    /// 참고: 플레이어 vs 플레이어 전투에서는 공격자가 직접 위치를 선택하여 소환
    /// </summary>
    private void SetupSpawnAndGoalPositions(GameObject gridInstance)
    {
        if (gridInstance == null)
        {
            // Debug.LogWarning($"[Player {playerId}]: SetupSpawnAndGoalPositions skipped - gridInstance is null.");
            return;
        }

        // FieldManager에서 gridSize와 gridOrigin을 가져옴
        Vector2Int gridSize = fieldManager != null ? fieldManager.gridSize : new Vector2Int(10, 9);
        Vector3 gridOrigin = fieldManager != null ? fieldManager.gridOrigin : Vector3.zero;
        float cellSize = fieldManager != null ? fieldManager.cellSize : 1f;

        // 골 위치: 필드 정 가운데 그리드
        int centerX = gridSize.x / 2;
        int centerY = gridSize.y / 2;
        Vector3 goalWorldPos = new Vector3(
            gridOrigin.x + (centerX + 0.5f) * cellSize,
            gridOrigin.y,
            gridOrigin.z + (centerY + 0.5f) * cellSize
        );

        // 스폰 위치: 남쪽(하단 가운데) 고정 - AI 웨이브 소환용
        Vector2Int spawnGridPos = new Vector2Int(centerX, 0); // 남쪽 (하단 가운데)
        Vector3 spawnWorldPos = new Vector3(
            gridOrigin.x + (spawnGridPos.x + 0.5f) * cellSize,
            gridOrigin.y,
            gridOrigin.z + (spawnGridPos.y + 0.5f) * cellSize
        );

        // 기존 SpawnPoint/Goal 오브젝트를 찾아보고, 없으면 새로 생성
        Transform existingSpawn = gridInstance.transform.Find("SpawnPoint");
        Transform existingGoal = gridInstance.transform.Find("Goal");
        if (existingSpawn == null)
        {
            existingSpawn = FindChildByNameRecursive(gridInstance.transform, "SpawnPoint");
        }
        if (existingGoal == null)
        {
            existingGoal = FindChildByNameRecursive(gridInstance.transform, "Goal");
        }

        if (existingSpawn != null)
        {
            if (existingSpawn.parent != gridInstance.transform)
            {
                existingSpawn.SetParent(gridInstance.transform, true);
            }
            existingSpawn.position = spawnWorldPos;
            this.spawnPoint = existingSpawn;
        }
        else
        {
            GameObject spawnGO = new GameObject("SpawnPoint");
            spawnGO.transform.SetParent(gridInstance.transform);
            spawnGO.transform.position = spawnWorldPos;
            this.spawnPoint = spawnGO.transform;
        }

        if (existingGoal != null)
        {
            if (existingGoal.parent != gridInstance.transform)
            {
                existingGoal.SetParent(gridInstance.transform, true);
            }
            existingGoal.position = goalWorldPos;
            this.goalTransform = existingGoal;
        }
        else
        {
            GameObject goalGO = new GameObject("Goal");
            goalGO.transform.SetParent(gridInstance.transform);
            goalGO.transform.position = goalWorldPos;
            this.goalTransform = goalGO.transform;
        }

        // Debug.Log($"[Player {playerId}]: 스폰 위치 설정 -> 그리드({spawnGridPos.x}, {spawnGridPos.y}), 월드{spawnWorldPos} (남쪽 고정, AI용)");
        // Debug.Log($"[Player {playerId}]: 골 위치 설정 -> 그리드({centerX}, {centerY}), 월드{goalWorldPos}");
    }

    private static Transform FindChildByNameRecursive(Transform root, string childName)
    {
        if (root == null || string.IsNullOrEmpty(childName))
        {
            return null;
        }

        return root.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(t => t != null && t.name == childName);
    }

    #endregion
}
