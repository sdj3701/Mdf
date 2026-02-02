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

    private bool HasStateAuthorityOrNoNetwork()
    {
        if (Object == null || Runner == null || !Runner.IsRunning)
        {
            return true;
        }
        return Object.HasStateAuthority;
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
        if (Object != null && Object.HasStateAuthority)
        {
            health = initialHealth;
            gold = initialGold;
            wallCount = initialWallCount;
        }

        _changeDetector = GetChangeDetector(ChangeDetector.Source.SimulationState);
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
            Debug.LogError($"[Player {playerId}]: gridNetworkObject resolve 실패");
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
        Debug.Log($"[Player {playerId}]: 3D Ground 찾음? -> {(ground3D != null)}");
        Debug.Log($"[Player {playerId}]: AstarGrid 찾음? -> {(this.astarGrid != null)}");

        // AstarGrid 초기화는 FieldManager 초기화 이후에 수행하여 3D 그리드 정보를 공유합니다.

        // 하위 매니저 초기화
        // 3D Ground 기반 초기화
        if (fieldManager)
        {
            if (ground3D != null)
            {
                Debug.Log($"[Player {playerId}]: FieldManager를 3D 모드로 초기화합니다.");
                fieldManager.Initialize(this, ground3D);
            }
            else
            {
                Debug.LogError($"[Player {playerId}]: FieldManager 초기화 실패 - Ground 오브젝트를 찾을 수 없습니다!");
            }
        }

        // FieldManager 초기화 후 스폰/골 위치를 동적으로 설정
        // - 골: 필드 정 가운데 그리드
        // - 스폰: 동서남북 테두리 구멍 4곳 중 랜덤
        SetupSpawnAndGoalPositions(gridInstance);
        Debug.Log($"[Player {playerId}]: SpawnPoint 위치 -> {(this.spawnPoint != null ? this.spawnPoint.position.ToString() : "null")}");
        Debug.Log($"[Player {playerId}]: Goal 위치 -> {(this.goalTransform != null ? this.goalTransform.position.ToString() : "null")}");

        // 이제 FieldManager가 준비되었으므로 AstarGrid를 FieldManager와 동기화하여 초기화합니다.
        if (this.astarGrid != null)
        {
            this.astarGrid.fieldManager = fieldManager;
            this.astarGrid.useFieldManagerGrid = true;
            this.astarGrid.Initialize();
        }
        else
        {
            Debug.LogError($"[Player {playerId}]: AstarGrid 컴포넌트를 찾지 못해 경로 탐색을 초기화할 수 없습니다.");
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
        Debug.Log($"--- Player {playerId} RPC 초기화 완료 ---");

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
            Debug.Log($"<color=cyan>[RPC_SyncShopItems] Player {playerId}: {unitDataNames.Length}개 상점 아이템 동기화 완료</color>");
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
            Debug.Log($"<color=magenta>[RPC_SyncPresentedAugments] Player {playerId}: {augmentNames.Length}개 증강체 동기화 완료</color>");
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
        
        Debug.Log($"<color=yellow>[RPC_RequestSyncData] Player {playerId}에게 데이터 동기화 요청 수신</color>");
        
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
        Debug.Log($"<color=cyan>[RPC_SyncPermanentBonuses] Player {playerId}: AttackDmg={attackDamagePercent:P0}, AttackSpd={attackSpeedPercent:P0}</color>");
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
            Debug.Log($"<color=yellow>[RPC_RegisterUnitAt] recv pos=({x},{y}) key='{unitDataKey}' star={starLevel} stateAuth={(Object != null && Object.HasStateAuthority)} id={unitId}</color>");
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
                Debug.LogWarning($"<color=yellow>[RPC_RegisterUnitAt] failed to resolve NetworkObject by NetworkId='{unitId}' key='{unitDataKey}'</color>");
                return;
            }

            if (fieldManager == null || fieldManager.ground3D == null)
            {
                Debug.Log($"<color=yellow>[RPC_RegisterUnitAt] queued. fieldManagerReady={(fieldManager != null)} groundReady={(fieldManager != null && fieldManager.ground3D != null)}</color>");
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
            Debug.Log($"<color=yellow>[RPC_RegisterUnitAt] dispatched to Internal for pos=({x},{y})</color>");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[RPC_RegisterUnitAt] exception: {ex.Message}");
        }
    }

    private async Cysharp.Threading.Tasks.UniTask RPC_RegisterUnitAt_Internal(NetworkObject unitNO, int x, int y, string unitDataKey, int starLevel)
    {
        try
        {
            if (fieldManager == null)
            {
                Debug.LogWarning($"<color=yellow>[RPC_Internal] fieldManager null</color>");
                return;
            }
            var unit = unitNO.GetComponent<Unit>();
            if (unit == null)
            {
                Debug.LogWarning($"<color=yellow>[RPC_Internal] Unit component missing on '{unitNO?.name}'</color>");
                return;
            }
            var pos = new Vector3Int(x, y, 0);
            Debug.Log($"<color=yellow>[RPC_Internal] start pos={pos} currentData={(unit.Data != null ? unit.Data.name : "null")} key='{unitDataKey}'</color>");
            if (fieldManager.IsUnitAt(pos))
            {
                var existingAtPos = fieldManager.GetUnitAt(pos);
                if (existingAtPos != null && existingAtPos != unit)
                {
                    Debug.LogWarning($"<color=yellow>[RPC_Internal] position already occupied by another unit. Replacing. pos={pos}</color>");
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
                        Debug.Log($"<color=yellow>[RPC_Internal] waiting LoadManager ready...</color>");
                        await LoadManager.Instance.WaitUntilReady();
                    }
                    data = LoadManager.Instance.GetUnitData(unitDataKey);
                    if (data == null)
                    {
                        Debug.Log($"<color=yellow>[RPC_Internal] LoadManager miss for key='{unitDataKey}'. Trying Addressables fallback...</color>");
                        data = await AssetLoader.LoadAssetAsync<UnitData>(unitDataKey);
                    }
                }
                if (data != null)
                {
                    await unit.Initialize(data, starLevel, this);
                    Debug.Log($"<color=yellow>[RPC_Internal] unit.Initialize OK data='{unit.Data?.name}' star={starLevel}</color>");
                }
                else
                {
                    Debug.LogError($"[Player {playerId}] RPC_RegisterUnitAt could not resolve UnitData for key '{unitDataKey}'.");
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
                Debug.LogWarning($"<color=yellow>[RPC_Internal] unit.Data still null after resolve. Skip Register. key='{unitDataKey}', pos={pos}</color>");
                return;
            }
            fieldManager.RegisterUnitAt(unit, pos);
            Debug.Log($"<color=#3399FF>[ClientFlow] RegisterUnitAt via RPC -> {pos} (Player {playerId}) data='{unit.Data?.name}'</color>");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[RPC_Internal] exception: {ex.Message}");
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
        Debug.Log($"<color=magenta>[SetFightingState] Player {playerId}: isFighting={isFighting} 설정됨</color>");
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
            Debug.Log($"<color=orange>[PlayerManager] Player {playerId}: 몬스터 소환 증강 '{augment.augmentName}' 등록 (누적 {_activeMonsterSummonAugments.Count}개)</color>");
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
            Debug.Log($"<color=red>[PlayerManager] Player {playerId}: 보스 '{augment.bossMonsterData.monsterName}' 보유 추가 (총 {_ownedBossAugments.Count}마리)</color>");
        }
    }

    public IReadOnlyList<AugmentData> GetOwnedBosses() => _ownedBossAugments;

    public bool ConsumeOwnedBoss(MonsterData bossData)
    {
        var augment = _ownedBossAugments.FirstOrDefault(a => a.bossMonsterData == bossData);
        if (augment != null)
        {
            _ownedBossAugments.Remove(augment);
            Debug.Log($"<color=red>[PlayerManager] Player {playerId}: 보스 '{bossData.monsterName}' 소환 → 보유에서 제거 (남은 {_ownedBossAugments.Count}마리)</color>");
            return true;
        }
        return false;
    }
    #endregion

    #region 공격 시퀀스 몬스터 풀 관리
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
            
            Debug.Log($"<color=red>[PlayerManager] Player {playerId}: 보유 보스 '{augment.bossMonsterData.monsterName}' 풀에 표시</color>");
        }

        Debug.Log($"<color=magenta>[PlayerManager] Player {playerId}: 공격 몬스터 풀 갱신 완료 ({AttackMonsterPool.Count}종류, 보유 보스: {_ownedBossAugments.Count}마리)</color>");
        
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
        var gm = GameManagers.Instance;
        if (gm == null)
        {
            gm = FindObjectOfType<GameManagers>();
            if (gm == null)
            {
                Debug.LogWarning("<color=green>[NetFlow] GameManagers not found on server yet. Dropping command.</color>");
                return;
            }
            Debug.Log("<color=green>[NetFlow] GameManagers resolved via FindObjectOfType on server.</color>");
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

        if (existingSpawn != null)
        {
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

        Debug.Log($"[Player {playerId}]: 스폰 위치 설정 -> 그리드({spawnGridPos.x}, {spawnGridPos.y}), 월드{spawnWorldPos} (남쪽 고정, AI용)");
        Debug.Log($"[Player {playerId}]: 골 위치 설정 -> 그리드({centerX}, {centerY}), 월드{goalWorldPos}");
    }

    #endregion
}
