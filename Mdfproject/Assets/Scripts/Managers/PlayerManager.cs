// Assets/Scripts/Managers/PlayerManager.cs

using System.Collections;
using System.Collections.Generic;
using System;
using UnityEngine;
using UnityEngine.Serialization;
using System.Linq;
using Fusion; // Fusion 네임스페이스 추가
using Cysharp.Threading.Tasks;
using System.Threading;

public partial class PlayerManager : NetworkBehaviour // [수정] MonoBehaviour -> NetworkBehaviour
{
    // [수정] playerId를 모든 클라이언트가 동기화할 수 있도록 [Networked] 프로퍼티로 변경합니다.
    [Networked] public int playerId { get; set; }
    [Networked] public NetworkBool IsAiControlled { get; private set; }

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
    [Networked, Capacity(SHOP_SNAPSHOT_CAPACITY)] private NetworkArray<ShopSnapshotSlot> ShopSnapshotSlots { get; }
    [Networked] private int ShopSnapshotRevision { get; set; }
    [Networked] private int ShopSnapshotCount { get; set; }
    [Networked] private int ShopSnapshotRound { get; set; }
    private const int PRESENTED_AUGMENT_SNAPSHOT_CAPACITY = 3;
    [Networked, Capacity(PRESENTED_AUGMENT_SNAPSHOT_CAPACITY)] private NetworkArray<int> PresentedAugmentSnapshotIds { get; }
    [Networked] private int PresentedAugmentSnapshotCount { get; set; }
    private const int SELECTED_AUGMENT_SNAPSHOT_CAPACITY = 64;
    [Networked, Capacity(SELECTED_AUGMENT_SNAPSHOT_CAPACITY)] private NetworkArray<int> SelectedAugmentSnapshotIds { get; }
    [Networked, Capacity(SELECTED_AUGMENT_SNAPSHOT_CAPACITY)] private NetworkArray<int> SelectedAugmentSnapshotCounts { get; }
    [Networked] private int SelectedAugmentSnapshotCount { get; set; }
    [Networked] private NetworkBool SelectedAugmentSnapshotOverflow { get; set; }
    private const float PERMANENT_BONUS_NETWORK_SCALE = 10000f;
    [Networked] private int PermanentAttackDamageBonusPermille { get; set; }
    [Networked] private int PermanentAttackSpeedBonusPermille { get; set; }
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
    [Networked] public int AttackMonsterPoolRevision { get; private set; }
    private const int ATTACK_POOL_SNAPSHOT_CAPACITY = 32;
    [Networked] private int AttackMonsterPoolSnapshotCount { get; set; }
    [Networked, Capacity(ATTACK_POOL_SNAPSHOT_CAPACITY)] private NetworkArray<AttackMonsterPoolSnapshotSlot> AttackMonsterPoolSnapshotSlots { get; }
    private int _lastAppliedAttackMonsterPoolRevision;
    private int _pendingAttackMonsterPoolCommandRevision = -1;
    public int AppliedAttackMonsterPoolRevision =>
        Object != null && Object.HasStateAuthority ? AttackMonsterPoolRevision : _lastAppliedAttackMonsterPoolRevision;
    public bool HasAppliedCurrentAttackMonsterPoolSnapshot =>
        Object != null && Object.HasStateAuthority || _lastAppliedAttackMonsterPoolRevision == AttackMonsterPoolRevision;
    public bool HasPendingAttackMonsterPoolCommand =>
        Object != null &&
        !Object.HasStateAuthority &&
        _pendingAttackMonsterPoolCommandRevision >= 0 &&
        _lastAppliedAttackMonsterPoolRevision <= _pendingAttackMonsterPoolCommandRevision;
    [Networked] public int OwnedMagicScrollRevision { get; private set; }
    private int _lastAppliedOwnedMagicScrollRevision;
    private int _latestReceivedOwnedMagicScrollRevision;
    public int AppliedOwnedMagicScrollRevision =>
        Object != null && Object.HasStateAuthority ? OwnedMagicScrollRevision : _lastAppliedOwnedMagicScrollRevision;
    public bool HasAppliedCurrentOwnedMagicScrollSnapshot =>
        Object != null && Object.HasStateAuthority || _lastAppliedOwnedMagicScrollRevision == OwnedMagicScrollRevision;
    #endregion

    private ChangeDetector _changeDetector;
    private bool _runtimeInitialized;
    public bool IsReadyForPlayerActions => _runtimeInitialized && playerId >= 0 && fieldManager != null;
    private PlayerShopPurchaseCoordinator _shopPurchaseCoordinator;
    internal int CurrentShopSnapshotRevision => ShopSnapshotRevision;

    public struct PurchaseUnitResult
    {
        public bool Succeeded;
        public int ShopSlotIndex;
        public int Cost;
        public Unit Unit;
        public string FailureReason;

        public static PurchaseUnitResult Success(int shopSlotIndex, int cost, Unit unit)
        {
            return new PurchaseUnitResult
            {
                Succeeded = true,
                ShopSlotIndex = shopSlotIndex,
                Cost = cost,
                Unit = unit,
                FailureReason = null
            };
        }

        public static PurchaseUnitResult Failure(int shopSlotIndex, string reason)
        {
            return new PurchaseUnitResult
            {
                Succeeded = false,
                ShopSlotIndex = shopSlotIndex,
                Cost = 0,
                Unit = null,
                FailureReason = reason
            };
        }
    }

    private struct ShopSnapshotSlot : INetworkStruct
    {
        public int UnitKeyHash;
        public int PackedMeta;

        public int StarLevel => PackedMeta & 0xFF;
        public int Sold => (PackedMeta >> 8) & 0x1;
    }

    private struct AttackMonsterPoolSnapshotSlot : INetworkStruct
    {
        public int DataId;
        public int CountsAndFlags;
        public int BossUniqueId;
        public int PlayerIds;

        public int RemainingCount => CountsAndFlags & 0x7FFF;
        public int MaxCount => (CountsAndFlags >> 15) & 0x7FFF;
        public int IsBoss => (CountsAndFlags >> 30) & 0x1;
        public int TargetPlayerId => UnpackSnapshotPlayerId(PlayerIds & 0xFFFF);
        public int OriginPlayerId => UnpackSnapshotPlayerId((PlayerIds >> 16) & 0xFFFF);
    }

    public void SetAiControlled(bool isAi)
    {
        if (HasStateAuthorityOrNoNetwork())
        {
            IsAiControlled = isAi;
        }
    }

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

    private static int StableDataKeyHash(string value)
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

    private static int StableAugmentSnapshotId(string augmentName)
    {
        return StableDataKeyHash(string.IsNullOrWhiteSpace(augmentName) ? string.Empty : augmentName.Trim());
    }

    private static int StableMonsterDataKeyHash(string value)
    {
        return StableDataKeyHash(NormalizeShopUnitKey(value));
    }

    private static int PackShopSnapshotMeta(int starLevel, int sold)
    {
        int packedStarLevel = Mathf.Clamp(starLevel, 0, 255);
        int packedSold = sold != 0 ? 1 : 0;
        return packedStarLevel | (packedSold << 8);
    }

    private static int PackAttackMonsterCounts(int remainingCount, int maxCount, int isBoss)
    {
        int packedRemaining = Mathf.Clamp(remainingCount, 0, 0x7FFF);
        int packedMax = Mathf.Clamp(maxCount, 0, 0x7FFF);
        int packedBoss = isBoss != 0 ? 1 : 0;
        return packedRemaining | (packedMax << 15) | (packedBoss << 30);
    }

    private static int PackSnapshotPlayerIds(int targetPlayerId, int originPlayerId)
    {
        return PackSnapshotPlayerId(targetPlayerId) | (PackSnapshotPlayerId(originPlayerId) << 16);
    }

    private static int PackSnapshotPlayerId(int playerIdValue)
    {
        return Mathf.Clamp(playerIdValue + 1, 0, 0xFFFF);
    }

    private static int UnpackSnapshotPlayerId(int packedPlayerId)
    {
        return Mathf.Clamp(packedPlayerId, 0, 0xFFFF) - 1;
    }

    private static string ResolveLoadedUnitDataKeyByStableHash(int unitDataKeyHash)
    {
        if (unitDataKeyHash == 0)
        {
            return string.Empty;
        }

        var allUnits = LoadManager.Instance != null ? LoadManager.Instance.GetAllUnitData() : null;
        if (TryResolveUnitDataKeyByStableHash(allUnits, unitDataKeyHash, out string loadedKey))
        {
            return loadedKey;
        }

        return TryResolveUnitDataKeyByStableHash(Resources.FindObjectsOfTypeAll<UnitData>(), unitDataKeyHash, out string resourceKey)
            ? resourceKey
            : string.Empty;
    }

    private static bool TryResolveUnitDataKeyByStableHash(IEnumerable<UnitData> units, int unitDataKeyHash, out string key)
    {
        key = string.Empty;
        if (units == null)
        {
            return false;
        }

        foreach (var data in units)
        {
            if (data == null)
            {
                continue;
            }

            if (StableUnitDataKeyHash(data.name) == unitDataKeyHash ||
                StableUnitDataKeyHash(data.unitName) == unitDataKeyHash)
            {
                key = data.name;
                return !string.IsNullOrEmpty(key);
            }
        }

        return false;
    }

    private static string ResolveLoadedAugmentNameByStableId(int augmentId)
    {
        if (augmentId == 0)
        {
            return string.Empty;
        }

        foreach (var data in Resources.FindObjectsOfTypeAll<AugmentData>())
        {
            if (data == null)
            {
                continue;
            }

            if (StableAugmentSnapshotId(data.augmentName) == augmentId ||
                StableAugmentSnapshotId(data.name) == augmentId)
            {
                return !string.IsNullOrWhiteSpace(data.augmentName) ? data.augmentName.Trim() : data.name;
            }
        }

        return string.Empty;
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
            ShopSnapshotSlots.Set(i, default);
        }

        for (int i = 0; i < count; i++)
        {
            var item = items[i];
            string unitKey = NormalizeShopUnitKey(item.UnitData != null ? item.UnitData.name : string.Empty);
            int starLevel = item.StarLevel > 0 ? item.StarLevel : 1;
            int sold = soldFlags != null && i < soldFlags.Length && soldFlags[i] ? 1 : 0;

            ShopSnapshotSlots.Set(i, new ShopSnapshotSlot
            {
                UnitKeyHash = StableUnitDataKeyHash(unitKey),
                PackedMeta = PackShopSnapshotMeta(starLevel, sold)
            });
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
            var slot = ShopSnapshotSlots[i];
            int keyHash = slot.UnitKeyHash;
            unitKeys[i] = ResolveLoadedUnitDataKeyByStableHash(keyHash);
            if (keyHash != 0 && string.IsNullOrEmpty(unitKeys[i]))
            {
                return false;
            }

            starLevels[i] = Mathf.Max(1, slot.StarLevel);
            soldFlags[i] = slot.Sold != 0;
        }

        return true;
    }

    public void PublishSelectedAugmentSnapshot(AugmentData augment)
    {
        PublishSelectedAugmentSnapshot(augment != null ? augment.augmentName : string.Empty);
    }

    public void PublishSelectedAugmentSnapshot(string augmentName)
    {
        if (Object == null || !Object.IsValid || !Object.HasStateAuthority)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(augmentName))
        {
            return;
        }

        int augmentId = StableAugmentSnapshotId(augmentName);
        int uniqueCount = Mathf.Clamp(SelectedAugmentSnapshotCount, 0, SELECTED_AUGMENT_SNAPSHOT_CAPACITY);
        for (int i = 0; i < uniqueCount; i++)
        {
            if (SelectedAugmentSnapshotIds.Get(i) == augmentId)
            {
                SelectedAugmentSnapshotCounts.Set(i, Mathf.Max(1, SelectedAugmentSnapshotCounts.Get(i)) + 1);
                return;
            }
        }

        if (uniqueCount >= SELECTED_AUGMENT_SNAPSHOT_CAPACITY)
        {
            SelectedAugmentSnapshotOverflow = true;
            Debug.LogError($"[PlayerManager] Selected augment durable snapshot capacity exceeded. playerId={playerId}, augment={augmentName}");
            return;
        }

        SelectedAugmentSnapshotIds.Set(uniqueCount, augmentId);
        SelectedAugmentSnapshotCounts.Set(uniqueCount, 1);
        SelectedAugmentSnapshotCount = uniqueCount + 1;
    }

    public void PublishPresentedAugmentSnapshot(IEnumerable<string> augmentNames)
    {
        if (Object == null || !Object.IsValid || !Object.HasStateAuthority)
        {
            return;
        }

        for (int i = 0; i < PRESENTED_AUGMENT_SNAPSHOT_CAPACITY; i++)
        {
            PresentedAugmentSnapshotIds.Set(i, 0);
        }

        int count = 0;
        foreach (var rawName in augmentNames ?? System.Array.Empty<string>())
        {
            if (count >= PRESENTED_AUGMENT_SNAPSHOT_CAPACITY)
            {
                break;
            }

            string name = rawName != null ? rawName.Trim() : string.Empty;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            PresentedAugmentSnapshotIds.Set(count, StableAugmentSnapshotId(name));
            count++;
        }

        PresentedAugmentSnapshotCount = count;
    }

    public void ClearPresentedAugmentSnapshot()
    {
        PublishPresentedAugmentSnapshot(System.Array.Empty<string>());
    }

    public string[] GetPresentedAugmentSnapshotNames()
    {
        int count = Mathf.Clamp(PresentedAugmentSnapshotCount, 0, PRESENTED_AUGMENT_SNAPSHOT_CAPACITY);
        var names = new List<string>(count);
        for (int i = 0; i < count; i++)
        {
            string name = ResolveLoadedAugmentNameByStableId(PresentedAugmentSnapshotIds.Get(i));
            if (!string.IsNullOrWhiteSpace(name))
            {
                names.Add(name);
            }
        }

        return names.ToArray();
    }

    public string[] GetSelectedAugmentSnapshotNames()
    {
        int count = Mathf.Clamp(SelectedAugmentSnapshotCount, 0, SELECTED_AUGMENT_SNAPSHOT_CAPACITY);
        var names = new List<string>(count);
        for (int i = 0; i < count; i++)
        {
            string name = ResolveLoadedAugmentNameByStableId(SelectedAugmentSnapshotIds.Get(i));
            if (!string.IsNullOrWhiteSpace(name))
            {
                int repetitions = Mathf.Max(1, SelectedAugmentSnapshotCounts.Get(i));
                for (int repetition = 0; repetition < repetitions; repetition++)
                {
                    names.Add(name);
                }
            }
        }

        return names.ToArray();
    }

    public void RestoreAugmentSnapshotsAfterHostMigration(
        string[] presentedAugmentNames,
        string[] selectedAugmentNames,
        string context)
    {
        if (Object == null || !Object.IsValid || !Object.HasStateAuthority)
        {
            return;
        }

        for (int i = 0; i < PRESENTED_AUGMENT_SNAPSHOT_CAPACITY; i++)
        {
            PresentedAugmentSnapshotIds.Set(i, 0);
        }

        int presentedCount = 0;
        foreach (var rawName in presentedAugmentNames ?? System.Array.Empty<string>())
        {
            if (presentedCount >= PRESENTED_AUGMENT_SNAPSHOT_CAPACITY)
            {
                break;
            }

            string name = rawName != null ? rawName.Trim() : string.Empty;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            PresentedAugmentSnapshotIds.Set(presentedCount, StableAugmentSnapshotId(name));
            presentedCount++;
        }

        for (int i = 0; i < SELECTED_AUGMENT_SNAPSHOT_CAPACITY; i++)
        {
            SelectedAugmentSnapshotIds.Set(i, 0);
            SelectedAugmentSnapshotCounts.Set(i, 0);
        }

        var selectedCountsById = new Dictionary<int, int>();
        var selectedOrder = new List<int>();
        foreach (var rawName in selectedAugmentNames ?? System.Array.Empty<string>())
        {
            string name = rawName != null ? rawName.Trim() : string.Empty;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            int augmentId = StableAugmentSnapshotId(name);
            if (!selectedCountsById.ContainsKey(augmentId))
            {
                selectedCountsById[augmentId] = 0;
                selectedOrder.Add(augmentId);
            }
            selectedCountsById[augmentId]++;
        }

        SelectedAugmentSnapshotOverflow = selectedOrder.Count > SELECTED_AUGMENT_SNAPSHOT_CAPACITY;
        int selectedCount = Mathf.Min(selectedOrder.Count, SELECTED_AUGMENT_SNAPSHOT_CAPACITY);
        for (int i = 0; i < selectedCount; i++)
        {
            int augmentId = selectedOrder[i];
            SelectedAugmentSnapshotIds.Set(i, augmentId);
            SelectedAugmentSnapshotCounts.Set(i, selectedCountsById[augmentId]);
        }

        PresentedAugmentSnapshotCount = presentedCount;
        SelectedAugmentSnapshotCount = selectedCount;
        Debug.Log($"[PlayerManager] HostMigration augment snapshot restore complete ({context}) P{playerId} presented={presentedCount} selected={selectedCount}");
    }

    // Pending unit registrations received before FieldManager is ready
    private struct PendingUnitReg
    {
        public NetworkObject unitNO;
        public uint unitIdRaw;
        public int x;
        public int y;
        public string unitDataKey;
        public int starLevel;
    }
    private List<PendingUnitReg> _pendingUnitRegs = new List<PendingUnitReg>();
    private readonly HashSet<uint> _retiredUnitRegistrationIds = new HashSet<uint>();
    private readonly Dictionary<uint, PendingUnitReg> _latestUnitRegistrationById = new Dictionary<uint, PendingUnitReg>();
    private int[] _pendingPermanentWallFlatPositions;
    private int[] _pendingUnitRosterIdRaws;
    private int[] _pendingUnitRosterFlatPositions;
    private string[] _pendingUnitRosterDataKeys;
    private int[] _pendingUnitRosterDataKeyHashes;
    private int[] _pendingUnitRosterStarLevels;
    private Coroutine _permanentWallSyncBroadcastCoroutine;

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

        if (!isHostMigration && Object != null && Object.HasStateAuthority)
        {
            health = initialHealth;
            gold = initialGold;
            wallCount = initialWallCount;
        }

        _changeDetector = GetChangeDetector(ChangeDetector.Source.SimulationState);
        ApplyPermanentBonusesFromNetworkSnapshot();
        ApplyPendingDurableConnectionTokenHashOnSpawn();

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
            if (propertyName == nameof(PermanentAttackDamageBonusPermille) ||
                propertyName == nameof(PermanentAttackSpeedBonusPermille))
            {
                ApplyPermanentBonusesFromNetworkSnapshot();
            }
        }
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public async void Rpc_InitializePlayer(int id, NetworkId gridId)
    {
        bool preserveRuntimeInitialized = _runtimeInitialized && playerId == id && fieldManager != null;
        if (!preserveRuntimeInitialized)
        {
            _runtimeInitialized = false;
        }
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
            _runtimeInitialized = preserveRuntimeInitialized;
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

        // FieldManager 초기화 후 goal 위치를 동적으로 설정
        SetupGoalPosition(gridInstance);
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
            monsterSpawner.Initialize(this, this.astarGrid, this.goalTransform);
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
        DrainPendingPermanentWalls("Rpc_InitializePlayer");
        QueuePermanentWallSyncBroadcast("Rpc_InitializePlayer");
        _runtimeInitialized = playerId >= 0 && fieldManager != null;

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
                if (_retiredUnitRegistrationIds.Contains(p.unitIdRaw))
                {
                    continue;
                }

                await RPC_RegisterUnitAt_Internal(p.unitNO, p.x, p.y, p.unitDataKey, p.starLevel);
            }
        }

        await DrainPendingUnitRoster("Rpc_InitializePlayer");

        _runtimeInitialized = playerId >= 0 && fieldManager != null;
    }

    public void RebindRuntimeReferencesAfterMigration(
        string context,
        bool verboseFailure = true,
        bool rebuildUnitMap = true,
        bool repairUnitPresentation = true)
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

        bool goalInvalid = goalTransform == null || !IsTransformOwnedByCurrentRunner(goalTransform);
        bool goalParentMismatch = gridRoot != null && goalTransform != null && goalTransform.parent != gridRoot.transform;

        if (fieldManager != null && ground3D != null
            && (fieldManager.ground3D == null || fieldManager.ground3D != ground3D))
        {
            fieldManager.Initialize(this, ground3D);
            fieldReinitialized = true;
        }

        bool goalRefreshed = false;
        bool requireGoalRefresh = goalInvalid
                                  || goalParentMismatch
                                  || fieldReinitialized
                                  || gridRebound;
        if (fieldManager != null
            && fieldManager.ground3D != null
            && gridRoot != null
            && requireGoalRefresh)
        {
            SetupGoalPosition(gridRoot);
            goalRefreshed = true;
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
                                             || goalRefreshed;
            if (shouldReinitializeSpawner && astarGrid != null && goalTransform != null)
            {
                monsterSpawner.Initialize(this, astarGrid, goalTransform);
            }

            monsterSpawner.EnsureRuntimeReferencesForMigration(context, verboseFailure);
        }

        if (fieldManager != null)
        {
            fieldManager.RebuildWallMapsAfterMigration($"PlayerManager.{context}", verboseFailure, out _);
            if (rebuildUnitMap)
            {
                fieldManager.RebuildUnitMapAfterMigration(
                    $"PlayerManager.{context}",
                    verboseFailure,
                    out _,
                    repairPresentation: repairUnitPresentation);
            }
        }

        if (verboseFailure && !IsRuntimeReady(out string reason))
        {
            // Debug.LogWarning($"[PlayerManager] 런타임 참조 재결선 미완료 ({context}) player={playerId}, reason={reason}");
        }
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_RebindRuntimeStateAfterReconnect()
    {
        RebindRuntimeReferencesAfterMigration("RPC_RebindRuntimeStateAfterReconnect", false);
        if (playerId >= 0 && fieldManager != null)
        {
            _runtimeInitialized = true;
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

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_RequestSyncData(RpcInfo info = default)
    {
        // 서버만 처리
        if (Object == null || !Object.HasStateAuthority) return;
        if (info.Source == PlayerRef.None || Object.InputAuthority != info.Source) return;
        
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

        RPC_SyncOwnedMagicScrolls(OwnedMagicScrollRevision, BuildOwnedMagicScrollNameArray());
        RPC_SyncPermanentBonuses(permanentAttackDamagePercent, permanentAttackSpeedPercent);
        ResendAttackMonsterPoolToClientsIfAuthoritative();
    }

    /// <summary>
    /// 서버에서 적용된 영구 증강 보너스를 클라이언트에 동기화합니다.
    /// </summary>

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_SyncPermanentBonuses(float attackDamagePercent, float attackSpeedPercent)
    {
        SetPermanentBonusesFromSync(attackDamagePercent, attackSpeedPercent);
        // Debug.Log($"<color=cyan>[RPC_SyncPermanentBonuses] Player {playerId}: AttackDmg={attackDamagePercent:P0}, AttackSpd={attackSpeedPercent:P0}</color>");
    }


    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_ApplyPermanentWalls(int[] flatPositions)
    {
        if (flatPositions == null || flatPositions.Length == 0)
        {
            return;
        }

        if (fieldManager == null)
        {
            _pendingPermanentWallFlatPositions = flatPositions.ToArray();
            RebindRuntimeReferencesAfterMigration("RPC_ApplyPermanentWalls.Pending", false);
        }

        if (fieldManager != null)
        {
            fieldManager.ApplyPermanentWallsFromServer(flatPositions);
            _pendingPermanentWallFlatPositions = null;
        }
    }

    private void DrainPendingPermanentWalls(string context)
    {
        if (_pendingPermanentWallFlatPositions == null || _pendingPermanentWallFlatPositions.Length == 0)
        {
            return;
        }

        if (fieldManager == null)
        {
            RebindRuntimeReferencesAfterMigration($"DrainPendingPermanentWalls.{context}", false);
        }

        if (fieldManager == null)
        {
            return;
        }

        var pending = _pendingPermanentWallFlatPositions;
        _pendingPermanentWallFlatPositions = null;
        fieldManager.ApplyPermanentWallsFromServer(pending);
    }

    private void QueuePermanentWallSyncBroadcast(string context)
    {
        if (Object == null
            || !Object.HasStateAuthority
            || Runner == null
            || !Runner.IsRunning
            || !Runner.IsServer
            || fieldManager == null)
        {
            return;
        }

        if (_permanentWallSyncBroadcastCoroutine != null)
        {
            StopCoroutine(_permanentWallSyncBroadcastCoroutine);
        }

        _permanentWallSyncBroadcastCoroutine = StartCoroutine(BroadcastPermanentWallsForLatePeers(context));
    }

    private IEnumerator BroadcastPermanentWallsForLatePeers(string context)
    {
        const int attempts = 6;
        const float intervalSeconds = 1f;

        for (int attempt = 0; attempt < attempts; attempt++)
        {
            if (Object == null
                || !Object.HasStateAuthority
                || Runner == null
                || !Runner.IsRunning
                || !Runner.IsServer
                || fieldManager == null)
            {
                break;
            }

            int[] flat = fieldManager.BuildPermanentWallSyncPayload(
                $"PlayerManager.{context}.BroadcastPermanentWallsForLatePeers.{attempt + 1}");
            if (flat != null && flat.Length > 0)
            {
                RPC_ApplyPermanentWalls(flat);
            }

            yield return new WaitForSeconds(intervalSeconds);
        }

        _permanentWallSyncBroadcastCoroutine = null;
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public async void RPC_ReconcileUnitRoster(int[] unitIdRaws, int[] flatPositions, string[] unitDataKeys, int[] starLevels)
    {
        try
        {
            if (Object != null && Object.HasStateAuthority)
            {
                return;
            }

            if (!IsValidUnitRosterPayload(unitIdRaws, flatPositions, unitDataKeys, starLevels))
            {
                return;
            }

            if (fieldManager == null || fieldManager.ground3D == null)
            {
                StorePendingUnitRoster(unitIdRaws, flatPositions, unitDataKeys, null, starLevels);
                RebindRuntimeReferencesAfterMigration("RPC_ReconcileUnitRoster.Pending", false);
            }

            if (fieldManager == null || fieldManager.ground3D == null)
            {
                return;
            }

            await ApplyUnitRosterFromAuthority(unitIdRaws, flatPositions, unitDataKeys, null, starLevels);
        }
        catch (System.Exception)
        {
            // Roster correction is best-effort; command replay and register RPCs remain authoritative.
        }
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public async void RPC_ReconcileUnitRosterCompact(int[] flatRoster)
    {
        await ApplyCompactUnitRosterFromAuthorityAsync(flatRoster);
    }

    public void ApplyCompactUnitRosterFromAuthority(int[] flatRoster)
    {
        ApplyCompactUnitRosterFromAuthorityAsync(flatRoster).Forget();
    }

    private async UniTask ApplyCompactUnitRosterFromAuthorityAsync(int[] flatRoster)
    {
        try
        {
            if (Object != null && Object.HasStateAuthority)
            {
                return;
            }

            if (flatRoster == null || flatRoster.Length == 0)
            {
                return;
            }

            bool versionedRoster = flatRoster.Length >= 2 && flatRoster[0] == -2;
            int count;
            int stride;
            int startOffset;
            bool hasUnitDataHashes;

            if (versionedRoster)
            {
                count = flatRoster[1];
                stride = 6;
                startOffset = 2;
                hasUnitDataHashes = true;
                if (count < 0 || flatRoster.Length != startOffset + (count * stride))
                {
                    return;
                }
            }
            else
            {
                if (flatRoster.Length % 5 != 0)
                {
                    return;
                }

                count = flatRoster.Length / 5;
                stride = 5;
                startOffset = 0;
                hasUnitDataHashes = false;
            }

            int[] unitIdRaws = new int[count];
            int[] flatPositions = new int[count * 3];
            int[] starLevels = new int[count];
            int[] unitDataKeyHashes = hasUnitDataHashes ? new int[count] : null;

            for (int i = 0; i < count; i++)
            {
                int offset = startOffset + (i * stride);
                unitIdRaws[i] = flatRoster[offset + 0];
                flatPositions[(i * 3) + 0] = flatRoster[offset + 1];
                flatPositions[(i * 3) + 1] = flatRoster[offset + 2];
                flatPositions[(i * 3) + 2] = flatRoster[offset + 3];
                starLevels[i] = flatRoster[offset + 4];
                if (hasUnitDataHashes)
                {
                    unitDataKeyHashes[i] = flatRoster[offset + 5];
                }
            }

            if (fieldManager == null || fieldManager.ground3D == null)
            {
                StorePendingUnitRoster(unitIdRaws, flatPositions, null, unitDataKeyHashes, starLevels);
                RebindRuntimeReferencesAfterMigration("RPC_ReconcileUnitRosterCompact.Pending", false);
            }

            if (fieldManager == null || fieldManager.ground3D == null)
            {
                return;
            }

            await ApplyUnitRosterFromAuthority(unitIdRaws, flatPositions, null, unitDataKeyHashes, starLevels);
        }
        catch (System.Exception)
        {
            // Compact roster correction is best-effort; per-unit register RPCs carry identity metadata.
        }
    }

    private async UniTask DrainPendingUnitRoster(string context)
    {
        if (_pendingUnitRosterIdRaws == null)
        {
            return;
        }

        if (fieldManager == null || fieldManager.ground3D == null)
        {
            RebindRuntimeReferencesAfterMigration($"DrainPendingUnitRoster.{context}", false);
        }

        if (fieldManager == null || fieldManager.ground3D == null)
        {
            return;
        }

        var unitIdRaws = _pendingUnitRosterIdRaws;
        var flatPositions = _pendingUnitRosterFlatPositions;
        var unitDataKeys = _pendingUnitRosterDataKeys;
        var unitDataKeyHashes = _pendingUnitRosterDataKeyHashes;
        var starLevels = _pendingUnitRosterStarLevels;
        _pendingUnitRosterIdRaws = null;
        _pendingUnitRosterFlatPositions = null;
        _pendingUnitRosterDataKeys = null;
        _pendingUnitRosterDataKeyHashes = null;
        _pendingUnitRosterStarLevels = null;

        await ApplyUnitRosterFromAuthority(unitIdRaws, flatPositions, unitDataKeys, unitDataKeyHashes, starLevels);
    }

    private void StorePendingUnitRoster(int[] unitIdRaws, int[] flatPositions, string[] unitDataKeys, int[] unitDataKeyHashes, int[] starLevels)
    {
        _pendingUnitRosterIdRaws = unitIdRaws != null ? unitIdRaws.ToArray() : null;
        _pendingUnitRosterFlatPositions = flatPositions != null ? flatPositions.ToArray() : null;
        _pendingUnitRosterDataKeys = unitDataKeys != null ? unitDataKeys.ToArray() : null;
        _pendingUnitRosterDataKeyHashes = unitDataKeyHashes != null ? unitDataKeyHashes.ToArray() : null;
        _pendingUnitRosterStarLevels = starLevels != null ? starLevels.ToArray() : null;
    }

    private bool IsValidUnitRosterPayload(int[] unitIdRaws, int[] flatPositions, string[] unitDataKeys, int[] starLevels)
    {
        if (unitIdRaws == null || flatPositions == null || starLevels == null)
        {
            return false;
        }

        int count = unitIdRaws.Length;
        return flatPositions.Length == count * 3
            && starLevels.Length == count
            && (unitDataKeys == null || unitDataKeys.Length == count);
    }

    private async UniTask ApplyUnitRosterFromAuthority(int[] unitIdRaws, int[] flatPositions, string[] unitDataKeys, int[] unitDataKeyHashes, int[] starLevels)
    {
        if (!IsValidUnitRosterPayload(unitIdRaws, flatPositions, unitDataKeys, starLevels) || fieldManager == null)
        {
            return;
        }

        var authoritativePositions = BuildUnitRosterPositionMap(unitIdRaws, flatPositions);
        foreach (var unitIdRaw in authoritativePositions.Keys)
        {
            _retiredUnitRegistrationIds.Remove(unitIdRaw);
        }

        fieldManager.ReconcileUnitsToAuthoritativeRoster(authoritativePositions);

        for (int i = 0; i < unitIdRaws.Length; i++)
        {
            uint unitIdRaw = unchecked((uint)unitIdRaws[i]);
            if (_retiredUnitRegistrationIds.Contains(unitIdRaw))
            {
                continue;
            }

            var position = new Vector3Int(flatPositions[(i * 3) + 0], flatPositions[(i * 3) + 1], flatPositions[(i * 3) + 2]);
            string unitDataKey = unitDataKeys != null ? unitDataKeys[i] : string.Empty;
            int starLevel = starLevels[i];

            if (string.IsNullOrEmpty(unitDataKey) &&
                _latestUnitRegistrationById.TryGetValue(unitIdRaw, out var metadataForKey) &&
                metadataForKey.starLevel == starLevel)
            {
                unitDataKey = metadataForKey.unitDataKey ?? string.Empty;
            }

            if (string.IsNullOrEmpty(unitDataKey) &&
                unitDataKeyHashes != null &&
                i < unitDataKeyHashes.Length)
            {
                unitDataKey = await ResolveUnitDataKeyByStableHashAsync(unitDataKeyHashes[i]);
            }

            NetworkObject unitNO = await ResolveNetworkObjectByRawIdAsync(unitIdRaw);
            if (unitNO == null)
            {
                if (_latestUnitRegistrationById.TryGetValue(unitIdRaw, out var metadataForObject) &&
                    metadataForObject.unitNO != null &&
                    metadataForObject.unitNO.IsValid &&
                    metadataForObject.unitNO.Id.Raw == unitIdRaw)
                {
                    unitNO = metadataForObject.unitNO;
                }
            }

            if (unitNO == null)
            {
                continue;
            }

            await RPC_RegisterUnitAt_Internal(unitNO, position.x, position.y, unitDataKey, starLevel);
        }
    }

    private Dictionary<uint, Vector3Int> BuildUnitRosterPositionMap(int[] unitIdRaws, int[] flatPositions)
    {
        var positions = new Dictionary<uint, Vector3Int>();
        if (unitIdRaws == null || flatPositions == null)
        {
            return positions;
        }

        for (int i = 0; i < unitIdRaws.Length && (i * 3) + 2 < flatPositions.Length; i++)
        {
            positions[unchecked((uint)unitIdRaws[i])] = new Vector3Int(
                flatPositions[(i * 3) + 0],
                flatPositions[(i * 3) + 1],
                flatPositions[(i * 3) + 2]);
        }

        return positions;
    }

    private async UniTask<string> ResolveUnitDataKeyByStableHashAsync(int unitDataKeyHash)
    {
        if (unitDataKeyHash == 0)
        {
            return string.Empty;
        }

        if (LoadManager.Instance == null)
        {
            await UniTask.WaitUntil(() => LoadManager.Instance != null);
        }

        if (!LoadManager.Instance.IsReady)
        {
            await LoadManager.Instance.WaitUntilReady();
        }

        var allUnits = LoadManager.Instance.GetAllUnitData();
        if (allUnits == null)
        {
            return string.Empty;
        }

        foreach (var data in allUnits)
        {
            if (data != null && StableUnitDataKeyHash(data.name) == unitDataKeyHash)
            {
                return data.name;
            }
        }

        return string.Empty;
    }

    private static int StableUnitDataKeyHash(string value)
    {
        return StableDataKeyHash(NormalizeShopUnitKey(value));
    }

    private void RememberLatestUnitRegistration(uint unitIdRaw, NetworkObject unitNO, int x, int y, string unitDataKey, int starLevel)
    {
        _latestUnitRegistrationById[unitIdRaw] = new PendingUnitReg
        {
            unitNO = unitNO,
            unitIdRaw = unitIdRaw,
            x = x,
            y = y,
            unitDataKey = unitDataKey ?? string.Empty,
            starLevel = starLevel
        };
    }

    private async UniTask<NetworkObject> ResolveNetworkObjectByRawIdAsync(uint unitIdRaw)
    {
        for (int attempt = 0; attempt < 300; attempt++)
        {
            if (TryFindNetworkObjectByRawId(unitIdRaw, out var unitNO))
            {
                return unitNO;
            }

            await UniTask.Yield();
        }

        return null;
    }

    private bool TryFindNetworkObjectByRawId(uint unitIdRaw, out NetworkObject unitNO)
    {
        unitNO = null;
        if (Runner == null)
        {
            return false;
        }

        var runnerObjects = Runner.GetAllNetworkObjects();
        if (runnerObjects == null)
        {
            return false;
        }

        foreach (var candidate in runnerObjects)
        {
            if (candidate != null && candidate.IsValid && candidate.Id.Raw == unitIdRaw)
            {
                unitNO = candidate;
                return true;
            }
        }

        return false;
    }


    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_UnregisterUnitAt(NetworkId unitId, int x, int y, string unitDataKey, int starLevel)
    {
        uint unitIdRaw = unitId.Raw;
        _retiredUnitRegistrationIds.Add(unitIdRaw);
        _pendingUnitRegs.RemoveAll(reg => reg.unitIdRaw == unitIdRaw);
        _latestUnitRegistrationById.Remove(unitIdRaw);

        if (fieldManager == null)
        {
            return;
        }

        NetworkObject unitNO = null;
        Unit unit = null;
        if (Runner != null)
        {
            Runner.TryFindObject(unitId, out unitNO);
        }

        if (unitNO != null)
        {
            unit = unitNO.GetComponent<Unit>();
        }

        fieldManager.UnregisterUnitAt(unit, new Vector3Int(x, y, 0), unitDataKey, starLevel);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public async void RPC_RegisterUnitAt(NetworkId unitId, int x, int y, string unitDataKey, int starLevel)
    {
        try
        {
            // Debug.Log($"<color=yellow>[RPC_RegisterUnitAt] recv pos=({x},{y}) key='{unitDataKey}' star={starLevel} stateAuth={(Object != null && Object.HasStateAuthority)} id={unitId}</color>");
            if (Object != null && Object.HasStateAuthority) return;
            uint unitIdRaw = unitId.Raw;
            RememberLatestUnitRegistration(unitIdRaw, null, x, y, unitDataKey, starLevel);
            if (_retiredUnitRegistrationIds.Contains(unitIdRaw))
            {
                return;
            }

            NetworkObject unitNO = null;
            bool resolved = false;
            int attempts = 0;
            do
            {
                if (_retiredUnitRegistrationIds.Contains(unitIdRaw))
                {
                    return;
                }

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
            RememberLatestUnitRegistration(unitIdRaw, unitNO, x, y, unitDataKey, starLevel);

            if (fieldManager == null || fieldManager.ground3D == null)
            {
                // Debug.Log($"<color=yellow>[RPC_RegisterUnitAt] queued. fieldManagerReady={(fieldManager != null)} groundReady={(fieldManager != null && fieldManager.ground3D != null)}</color>");
                _pendingUnitRegs.Add(new PendingUnitReg
                {
                    unitNO = unitNO,
                    unitIdRaw = unitIdRaw,
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
        catch (System.Exception)
        {
            // Debug.LogError($"[RPC_RegisterUnitAt] exception: {ex.Message}");
        }
    }

    private async Cysharp.Threading.Tasks.UniTask RPC_RegisterUnitAt_Internal(NetworkObject unitNO, int x, int y, string unitDataKey, int starLevel)
    {
        try
        {
            if (unitNO != null && _retiredUnitRegistrationIds.Contains(unitNO.Id.Raw))
            {
                return;
            }

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

            bool needInit =
                unit.Data == null ||
                (!string.IsNullOrEmpty(unitDataKey) && unit.Data.name != unitDataKey) ||
                unit.starLevel != starLevel;
            if (needInit)
            {
                UnitData data = unit.Data;
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
        catch (System.Exception)
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
    public int GetMaxHealth() => Mathf.Max(1, initialHealth);
    public int GetGold() => gold;
    public int GetWallCount() => wallCount;
    public int GetWallReserveK() => wallReserveK;
    public Vector2 GetWallBuildDelayRange() => NormalizeDelayRange(wallBuildDelayRange);
    public Vector2 GetUnitPurchaseDelayRange() => NormalizeDelayRange(unitPurchaseDelayRange);
    public Vector2 GetUnitMoveDelayRange() => NormalizeDelayRange(unitMoveDelayRange);

    public void RestoreDurableStateAfterHostMigration(
        int restoredPlayerId,
        int restoredHealth,
        int restoredGold,
        int restoredWallCount,
        string[] shopUnitKeys,
        int[] shopStarLevels,
        bool[] shopSoldFlags,
        int shopRevision,
        int shopRound,
        string context)
    {
        if (Object == null || !Object.IsValid || !Object.HasStateAuthority)
        {
            return;
        }

        if (restoredPlayerId >= 0 && playerId != restoredPlayerId)
        {
            playerId = restoredPlayerId;
        }

        health = Mathf.Max(0, restoredHealth);
        gold = Mathf.Max(0, restoredGold);
        wallCount = Mathf.Max(0, restoredWallCount);
        _runtimeInitialized = true;

        if (shopUnitKeys != null && shopUnitKeys.Length > 0)
        {
            int count = Mathf.Clamp(shopUnitKeys.Length, 0, SHOP_SNAPSHOT_CAPACITY);
            for (int i = 0; i < SHOP_SNAPSHOT_CAPACITY; i++)
            {
                ShopSnapshotSlots.Set(i, default);
            }

            for (int i = 0; i < count; i++)
            {
                string unitKey = i < shopUnitKeys.Length ? NormalizeShopUnitKey(shopUnitKeys[i]) : string.Empty;
                int starLevel = shopStarLevels != null && i < shopStarLevels.Length
                    ? Mathf.Max(1, shopStarLevels[i])
                    : 1;
                int sold = shopSoldFlags != null && i < shopSoldFlags.Length && shopSoldFlags[i] ? 1 : 0;

                ShopSnapshotSlots.Set(i, new ShopSnapshotSlot
                {
                    UnitKeyHash = StableUnitDataKeyHash(unitKey),
                    PackedMeta = PackShopSnapshotMeta(starLevel, sold)
                });
            }

            ShopSnapshotCount = count;
            ShopSnapshotRound = Mathf.Max(0, shopRound);
            ShopSnapshotRevision = Mathf.Max(1, shopRevision);
        }

        GameEvents.TriggerPlayerStatsChanged(playerId, health, gold);
        GameEvents.TriggerPlayerWallCountChanged(playerId, wallCount);
        Debug.Log($"[PlayerManager] HostMigration durable restore complete ({context}) P{playerId} hp={health} gold={gold} walls={wallCount} shopRev={ShopSnapshotRevision} shopCount={ShopSnapshotCount}");
    }

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
            PublishAugmentRuntimeMigrationStateFromAuthority("register_active_summon");
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
            PublishAugmentRuntimeMigrationStateFromAuthority("add_owned_boss");
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
            PublishAugmentRuntimeMigrationStateFromAuthority("consume_owned_boss");
            SyncOwnedBossRemovalToClientsIfAuthoritative(bossData);
            // Debug.Log($"<color=red>[PlayerManager] Player {playerId}: 보스 '{bossData.monsterName}' 소환 → 보유에서 제거 (남은 {_ownedBossAugments.Count}마리)</color>");
            return true;
        }
        return false;
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_RemoveOwnedBossByMonsterDataName(string bossMonsterDataName)
    {
        if (Object != null && Object.HasStateAuthority)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(bossMonsterDataName))
        {
            return;
        }

        _ownedBossAugments.RemoveAll(augment =>
            augment != null &&
            augment.bossMonsterData != null &&
            augment.bossMonsterData.name == bossMonsterDataName);
    }

    private void SyncOwnedBossRemovalToClientsIfAuthoritative(MonsterData bossData)
    {
        if (bossData != null && Object != null && Object.HasStateAuthority)
        {
            RPC_RemoveOwnedBossByMonsterDataName(bossData.name);
        }
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
        if (!HasStateAuthorityOrNoNetwork())
        {
            return;
        }

        if (scrollData != null)
        {
            _ownedScrolls.Add(scrollData);
            BumpOwnedMagicScrollRevisionIfAuthoritativeOrOffline();
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
        if (!HasStateAuthorityOrNoNetwork())
        {
            return false;
        }

        if (scrollData == null) return false;
        
        // 같은 종류의 스크롤이 있는지 확인
        int slotIndex = _ownedScrolls.FindIndex(s => s == scrollData || (s != null && s.name == scrollData.name));
        if (slotIndex >= 0)
        {
            _ownedScrolls.RemoveAt(slotIndex);
            BumpOwnedMagicScrollRevisionIfAuthoritativeOrOffline();
            Debug.Log($"<color=magenta>[PlayerManager] Player {playerId}: 마법 스크롤 '{scrollData.scrollName}' 사용 (남은 {_ownedScrolls.Count}개)</color>");

            PublishOwnedMagicScrollsChanged();
            SyncOwnedMagicScrollsToClientsIfAuthoritative();
            return true;
        }
        
        return false;
    }

    public int FindOwnedMagicScrollSlot(MagicScrollData scrollData)
    {
        if (scrollData == null || _ownedScrolls == null)
        {
            return -1;
        }

        for (int i = 0; i < _ownedScrolls.Count; i++)
        {
            var owned = _ownedScrolls[i];
            if (owned == scrollData || (owned != null && owned.name == scrollData.name))
            {
                return i;
            }
        }

        return -1;
    }

    public bool TryGetMagicScrollAtSlot(int scrollSlotIndex, out MagicScrollData scrollData, out string reason)
    {
        scrollData = null;
        reason = null;
        if (_ownedScrolls == null)
        {
            reason = "owned_scrolls_missing";
            return false;
        }

        if (scrollSlotIndex < 0 || scrollSlotIndex >= _ownedScrolls.Count)
        {
            reason = "scroll_slot_out_of_range";
            return false;
        }

        scrollData = _ownedScrolls[scrollSlotIndex];
        if (scrollData == null)
        {
            reason = "scroll_slot_empty";
            return false;
        }

        return true;
    }

    public bool TryConsumeMagicScrollSlot(int scrollSlotIndex, out MagicScrollData consumedScroll, out string reason)
    {
        consumedScroll = null;
        if (!HasStateAuthorityOrNoNetwork())
        {
            reason = "state_authority_required";
            return false;
        }

        if (!TryGetMagicScrollAtSlot(scrollSlotIndex, out consumedScroll, out reason))
        {
            return false;
        }

        _ownedScrolls.RemoveAt(scrollSlotIndex);
        BumpOwnedMagicScrollRevisionIfAuthoritativeOrOffline();
        Debug.Log($"<color=magenta>[PlayerManager] Player {playerId}: 마법 스크롤 '{consumedScroll.scrollName}' 사용 (slot={scrollSlotIndex}, 남은 {_ownedScrolls.Count}개)</color>");

        PublishOwnedMagicScrollsChanged();
        SyncOwnedMagicScrollsToClientsIfAuthoritative();
        return true;
    }

    public bool TryRefundMagicScrollSlot(int scrollSlotIndex, MagicScrollData scrollData)
    {
        if (!HasStateAuthorityOrNoNetwork())
        {
            return false;
        }

        if (scrollData == null)
        {
            return false;
        }

        if (_ownedScrolls == null)
        {
            _ownedScrolls = new List<MagicScrollData>();
        }

        int insertIndex = Mathf.Clamp(scrollSlotIndex, 0, _ownedScrolls.Count);
        _ownedScrolls.Insert(insertIndex, scrollData);
        BumpOwnedMagicScrollRevisionIfAuthoritativeOrOffline();
        PublishOwnedMagicScrollsChanged();
        SyncOwnedMagicScrollsToClientsIfAuthoritative();
        return true;
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public async void RPC_SyncOwnedMagicScrolls(int revision, string[] scrollDataNames)
    {
        if (Object != null && Object.HasStateAuthority)
        {
            return;
        }

        if (revision < _latestReceivedOwnedMagicScrollRevision ||
            revision < _lastAppliedOwnedMagicScrollRevision)
        {
            return;
        }

        _latestReceivedOwnedMagicScrollRevision = revision;
        string[] requestedNames = scrollDataNames?
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToArray() ?? System.Array.Empty<string>();

        var syncedScrolls = new List<MagicScrollData>(scrollDataNames?.Length ?? 0);
        foreach (string scrollDataName in requestedNames)
        {
            if (revision != _latestReceivedOwnedMagicScrollRevision ||
                revision < _lastAppliedOwnedMagicScrollRevision)
            {
                return;
            }

            MagicScrollData scrollData = await AssetLoader.LoadAssetAsync<MagicScrollData>(scrollDataName);
            if (revision != _latestReceivedOwnedMagicScrollRevision ||
                revision < _lastAppliedOwnedMagicScrollRevision)
            {
                return;
            }

            if (scrollData == null)
            {
                Debug.LogWarning($"[PlayerManager] Owned magic scroll sync skipped missing asset '{scrollDataName}' at revision {revision}.");
                return;
            }

            syncedScrolls.Add(scrollData);
        }

        if (revision != _latestReceivedOwnedMagicScrollRevision ||
            revision < _lastAppliedOwnedMagicScrollRevision)
        {
            return;
        }

        _ownedScrolls = syncedScrolls;
        _lastAppliedOwnedMagicScrollRevision = revision;
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
            RPC_SyncOwnedMagicScrolls(OwnedMagicScrollRevision, BuildOwnedMagicScrollNameArray());
        }
    }

    public void ResendOwnedMagicScrollsToClientsIfAuthoritative()
    {
        SyncOwnedMagicScrollsToClientsIfAuthoritative();
    }

    public bool TryGetOwnedMagicScrollSnapshot(
        out int revision,
        out MagicScrollData[] scrollDataRefs,
        out string[] scrollDataNames)
    {
        revision = OwnedMagicScrollRevision;
        scrollDataRefs = System.Array.Empty<MagicScrollData>();
        scrollDataNames = System.Array.Empty<string>();

        if (_ownedScrolls == null)
        {
            return true;
        }

        var validScrolls = _ownedScrolls
            .Where(scroll => scroll != null && !string.IsNullOrWhiteSpace(scroll.name))
            .ToArray();
        scrollDataRefs = validScrolls;
        scrollDataNames = validScrolls.Select(scroll => scroll.name).ToArray();
        return true;
    }

    public void RestoreOwnedMagicScrollsFromMigrationSnapshot(
        int revision,
        MagicScrollData[] scrollDataRefs,
        string[] scrollDataNames,
        string context)
    {
        if (Object == null || !Object.IsValid || !Object.HasStateAuthority)
        {
            return;
        }

        int count = Mathf.Max(scrollDataRefs?.Length ?? 0, scrollDataNames?.Length ?? 0);
        var restored = new List<MagicScrollData>(count);
        bool requiresAsyncLoad = false;

        for (int i = 0; i < count; i++)
        {
            MagicScrollData data = scrollDataRefs != null && i < scrollDataRefs.Length
                ? scrollDataRefs[i]
                : null;
            if (data != null)
            {
                restored.Add(data);
                continue;
            }

            string name = scrollDataNames != null && i < scrollDataNames.Length ? scrollDataNames[i] : null;
            if (!string.IsNullOrWhiteSpace(name))
            {
                requiresAsyncLoad = true;
                break;
            }
        }

        if (!requiresAsyncLoad)
        {
            ApplyOwnedMagicScrollSnapshot(revision, restored, context);
            return;
        }

        RestoreOwnedMagicScrollsFromMigrationSnapshotAsync(revision, scrollDataRefs, scrollDataNames, context).Forget();
    }

    private async UniTask RestoreOwnedMagicScrollsFromMigrationSnapshotAsync(
        int revision,
        MagicScrollData[] scrollDataRefs,
        string[] scrollDataNames,
        string context)
    {
        int count = Mathf.Max(scrollDataRefs?.Length ?? 0, scrollDataNames?.Length ?? 0);
        var restored = new List<MagicScrollData>(count);
        for (int i = 0; i < count; i++)
        {
            MagicScrollData data = scrollDataRefs != null && i < scrollDataRefs.Length
                ? scrollDataRefs[i]
                : null;
            if (data == null)
            {
                string name = scrollDataNames != null && i < scrollDataNames.Length ? scrollDataNames[i] : null;
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                data = await AssetLoader.LoadAssetAsync<MagicScrollData>(name);
                if (data == null)
                {
                    Debug.LogWarning($"[PlayerManager] HostMigration owned scroll restore skipped missing asset '{name}' ({context}).");
                    return;
                }
            }

            restored.Add(data);
        }

        ApplyOwnedMagicScrollSnapshot(revision, restored, context);
    }

    private void ApplyOwnedMagicScrollSnapshot(int revision, List<MagicScrollData> restoredScrolls, string context)
    {
        if (Object == null || !Object.IsValid || !Object.HasStateAuthority)
        {
            return;
        }

        if (revision < OwnedMagicScrollRevision)
        {
            return;
        }

        _ownedScrolls = restoredScrolls ?? new List<MagicScrollData>();
        OwnedMagicScrollRevision = Mathf.Max(OwnedMagicScrollRevision, revision);
        _lastAppliedOwnedMagicScrollRevision = Mathf.Max(_lastAppliedOwnedMagicScrollRevision, OwnedMagicScrollRevision);
        _latestReceivedOwnedMagicScrollRevision = Mathf.Max(_latestReceivedOwnedMagicScrollRevision, OwnedMagicScrollRevision);
        PublishOwnedMagicScrollsChanged();
        SyncOwnedMagicScrollsToClientsIfAuthoritative();
        Debug.Log($"[PlayerManager] HostMigration owned scroll restore complete ({context}) P{playerId} rev={OwnedMagicScrollRevision} count={_ownedScrolls.Count}");
    }

    private void BumpOwnedMagicScrollRevisionIfAuthoritativeOrOffline()
    {
        if (Object == null || !Object.IsValid || Object.HasStateAuthority)
        {
            OwnedMagicScrollRevision++;
            _lastAppliedOwnedMagicScrollRevision = Mathf.Max(_lastAppliedOwnedMagicScrollRevision, OwnedMagicScrollRevision);
            _latestReceivedOwnedMagicScrollRevision = Mathf.Max(_latestReceivedOwnedMagicScrollRevision, OwnedMagicScrollRevision);
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
        if (Object != null && Object.IsValid && !Object.HasStateAuthority)
        {
            if (Object.HasInputAuthority)
            {
                RPC_RequestSyncData();
            }
            return;
        }

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
        SyncAttackMonsterPoolToClientsIfAuthoritative();
        PrewarmAttackMonsterPoolIfPossible("RefreshAttackMonsterPool");
    }

    private void PrewarmAttackMonsterPoolIfPossible(string context)
    {
        if (monsterSpawner == null || AttackMonsterPool == null || AttackMonsterPool.Count == 0)
        {
            return;
        }

        monsterSpawner.PrewarmAttackMonsterPoolAsync(AttackMonsterPool, context).Forget();
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
        SyncAttackMonsterPoolToClientsIfAuthoritative();
        return true;
    }

    public bool TryConsumeMonsterPoolSlot(int poolSlotIndex)
    {
        if (AttackMonsterPool == null ||
            poolSlotIndex < 0 ||
            poolSlotIndex >= AttackMonsterPool.Count)
        {
            return false;
        }

        var entry = AttackMonsterPool[poolSlotIndex];
        if (entry == null || entry.IsEmpty)
        {
            return false;
        }

        if (!entry.TryConsume())
        {
            return false;
        }

        GameEvents.TriggerMonsterPoolChanged(playerId, AttackMonsterPool);
        SyncAttackMonsterPoolToClientsIfAuthoritative();
        return true;
    }

    public void MarkAttackMonsterPoolCommandSubmitted(int observedRevision)
    {
        if (Object != null && Object.HasStateAuthority)
        {
            return;
        }

        if (observedRevision >= 0)
        {
            _pendingAttackMonsterPoolCommandRevision = Mathf.Max(_pendingAttackMonsterPoolCommandRevision, observedRevision);
        }
    }

    public bool TryRefundMonsterPoolSlot(int poolSlotIndex)
    {
        if (AttackMonsterPool == null ||
            poolSlotIndex < 0 ||
            poolSlotIndex >= AttackMonsterPool.Count)
        {
            return false;
        }

        var entry = AttackMonsterPool[poolSlotIndex];
        if (entry == null)
        {
            return false;
        }

        int max = Mathf.Max(entry.MaxCount, entry.RemainingCount + 1);
        entry.RemainingCount = Mathf.Min(max, entry.RemainingCount + 1);
        entry.MaxCount = max;

        GameEvents.TriggerMonsterPoolChanged(playerId, AttackMonsterPool);
        SyncAttackMonsterPoolToClientsIfAuthoritative();
        return true;
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_SyncAttackMonsterPool(
        int revision,
        string[] monsterDataNames,
        int[] remainingCounts,
        int[] maxCounts,
        int[] isBossValues,
        int[] bossUniqueIds,
        int[] targetPlayerIds,
        int[] originPlayerIds)
    {
        ApplyAttackMonsterPoolSnapshotAsync(
            revision,
            monsterDataNames,
            remainingCounts,
            maxCounts,
            isBossValues,
            bossUniqueIds,
            targetPlayerIds,
            originPlayerIds,
            false,
            "RPC_SyncAttackMonsterPool").Forget();
    }

    public bool TryGetAttackMonsterPoolSnapshot(
        out int revision,
        out MonsterData[] monsterDataRefs,
        out string[] monsterDataNames,
        out int[] remainingCounts,
        out int[] maxCounts,
        out int[] isBossValues,
        out int[] bossUniqueIds,
        out int[] targetPlayerIds,
        out int[] originPlayerIds)
    {
        if (TryGetReplicatedAttackMonsterPoolSnapshot(
                out revision,
                out monsterDataNames,
                out remainingCounts,
                out maxCounts,
                out isBossValues,
                out bossUniqueIds,
                out targetPlayerIds,
                out originPlayerIds))
        {
            monsterDataRefs = new MonsterData[monsterDataNames.Length];
            return true;
        }

        revision = AttackMonsterPoolRevision;
        BuildAttackMonsterPoolSnapshot(
            out monsterDataRefs,
            out monsterDataNames,
            out remainingCounts,
            out maxCounts,
            out isBossValues,
            out bossUniqueIds,
            out targetPlayerIds,
            out originPlayerIds);
        return AttackMonsterPool != null;
    }

    public bool TryGetAttackMonsterPoolSnapshotForComparison(
        out int revision,
        out string[] monsterDataNames,
        out int[] remainingCounts,
        out int[] maxCounts,
        out int[] isBossValues,
        out int[] bossUniqueIds,
        out int[] targetPlayerIds,
        out int[] originPlayerIds)
    {
        if (TryGetReplicatedAttackMonsterPoolSnapshot(
                out revision,
                out monsterDataNames,
                out remainingCounts,
                out maxCounts,
                out isBossValues,
                out bossUniqueIds,
                out targetPlayerIds,
                out originPlayerIds))
        {
            return true;
        }

        revision = AttackMonsterPoolRevision;
        BuildAttackMonsterPoolSnapshot(
            out _,
            out monsterDataNames,
            out remainingCounts,
            out maxCounts,
            out isBossValues,
            out bossUniqueIds,
            out targetPlayerIds,
            out originPlayerIds);
        return AttackMonsterPool != null;
    }

    public void RestoreAttackMonsterPoolFromMigrationSnapshot(
        int revision,
        MonsterData[] monsterDataRefs,
        string[] monsterDataNames,
        int[] remainingCounts,
        int[] maxCounts,
        int[] isBossValues,
        int[] bossUniqueIds,
        int[] targetPlayerIds,
        int[] originPlayerIds,
        string context)
    {
        if (Object == null || !Object.IsValid || !Object.HasStateAuthority)
        {
            return;
        }

        if (TryBuildAttackMonsterPoolFromRefs(
                monsterDataRefs,
                remainingCounts,
                maxCounts,
                isBossValues,
                bossUniqueIds,
                targetPlayerIds,
                originPlayerIds,
                out var restoredPool))
        {
            ApplyAttackMonsterPoolEntries(revision, restoredPool);
            ResendAttackMonsterPoolToClientsIfAuthoritative();
            Debug.Log($"[PlayerManager] AttackMonsterPool migration restore complete ({context}) P{playerId} rev={AttackMonsterPoolRevision} entries={AttackMonsterPool.Count}");
            return;
        }

        ApplyAttackMonsterPoolSnapshotAsync(
            revision,
            monsterDataNames,
            remainingCounts,
            maxCounts,
            isBossValues,
            bossUniqueIds,
            targetPlayerIds,
            originPlayerIds,
            true,
            context).Forget();
    }

    public void ResendAttackMonsterPoolToClientsIfAuthoritative()
    {
        PublishAttackMonsterPoolToClientsIfAuthoritative(false);
    }

    private void SyncAttackMonsterPoolToClientsIfAuthoritative()
    {
        PublishAttackMonsterPoolToClientsIfAuthoritative(true);
    }

    private void PublishAttackMonsterPoolToClientsIfAuthoritative(bool incrementRevision)
    {
        if (Object == null || !Object.HasStateAuthority || AttackMonsterPool == null)
        {
            return;
        }

        if (incrementRevision)
        {
            AttackMonsterPoolRevision++;
        }

        BuildAttackMonsterPoolSnapshot(
            out _,
            out string[] monsterDataNames,
            out int[] remainingCounts,
            out int[] maxCounts,
            out int[] isBossValues,
            out int[] bossUniqueIds,
            out int[] targetPlayerIds,
            out int[] originPlayerIds);

        PublishReplicatedAttackMonsterPoolSnapshot(
            monsterDataNames,
            remainingCounts,
            maxCounts,
            isBossValues,
            bossUniqueIds,
            targetPlayerIds,
            originPlayerIds);

        RPC_SyncAttackMonsterPool(
            AttackMonsterPoolRevision,
            monsterDataNames,
            remainingCounts,
            maxCounts,
            isBossValues,
            bossUniqueIds,
            targetPlayerIds,
            originPlayerIds);
    }

    private bool TryGetReplicatedAttackMonsterPoolSnapshot(
        out int revision,
        out string[] monsterDataNames,
        out int[] remainingCounts,
        out int[] maxCounts,
        out int[] isBossValues,
        out int[] bossUniqueIds,
        out int[] targetPlayerIds,
        out int[] originPlayerIds)
    {
        revision = AttackMonsterPoolRevision;
        int count = Mathf.Clamp(AttackMonsterPoolSnapshotCount, 0, ATTACK_POOL_SNAPSHOT_CAPACITY);
        monsterDataNames = Array.Empty<string>();
        remainingCounts = Array.Empty<int>();
        maxCounts = Array.Empty<int>();
        isBossValues = Array.Empty<int>();
        bossUniqueIds = Array.Empty<int>();
        targetPlayerIds = Array.Empty<int>();
        originPlayerIds = Array.Empty<int>();

        if (Object == null || !Object.IsValid || revision <= 0 || count <= 0)
        {
            return false;
        }

        monsterDataNames = new string[count];
        remainingCounts = new int[count];
        maxCounts = new int[count];
        isBossValues = new int[count];
        bossUniqueIds = new int[count];
        targetPlayerIds = new int[count];
        originPlayerIds = new int[count];

        for (int i = 0; i < count; i++)
        {
            var slot = AttackMonsterPoolSnapshotSlots[i];
            monsterDataNames[i] = ResolveLoadedMonsterDataNameByStableHash(slot.DataId);
            if (slot.DataId != 0 && string.IsNullOrEmpty(monsterDataNames[i]))
            {
                return false;
            }

            remainingCounts[i] = slot.RemainingCount;
            maxCounts[i] = slot.MaxCount;
            isBossValues[i] = slot.IsBoss;
            bossUniqueIds[i] = slot.BossUniqueId;
            targetPlayerIds[i] = slot.TargetPlayerId;
            originPlayerIds[i] = slot.OriginPlayerId;
        }

        return true;
    }

    private void PublishReplicatedAttackMonsterPoolSnapshot(
        string[] monsterDataNames,
        int[] remainingCounts,
        int[] maxCounts,
        int[] isBossValues,
        int[] bossUniqueIds,
        int[] targetPlayerIds,
        int[] originPlayerIds)
    {
        if (Object == null || !Object.IsValid || !Object.HasStateAuthority)
        {
            return;
        }

        int count = Mathf.Clamp(monsterDataNames?.Length ?? 0, 0, ATTACK_POOL_SNAPSHOT_CAPACITY);
        for (int i = 0; i < ATTACK_POOL_SNAPSHOT_CAPACITY; i++)
        {
            AttackMonsterPoolSnapshotSlots.Set(i, default);
        }

        for (int i = 0; i < count; i++)
        {
            int remainingCount = ReadArrayValue(remainingCounts, i, 0);
            int maxCount = ReadArrayValue(maxCounts, i, 0);
            int isBoss = ReadArrayValue(isBossValues, i, 0);
            int targetPlayerId = ReadArrayValue(targetPlayerIds, i, -1);
            int originPlayerId = ReadArrayValue(originPlayerIds, i, -1);

            AttackMonsterPoolSnapshotSlots.Set(i, new AttackMonsterPoolSnapshotSlot
            {
                DataId = StableMonsterDataKeyHash(monsterDataNames[i]),
                CountsAndFlags = PackAttackMonsterCounts(remainingCount, maxCount, isBoss),
                BossUniqueId = ReadArrayValue(bossUniqueIds, i, -1),
                PlayerIds = PackSnapshotPlayerIds(targetPlayerId, originPlayerId)
            });
        }

        AttackMonsterPoolSnapshotCount = count;
    }

    private void BuildAttackMonsterPoolSnapshot(
        out MonsterData[] monsterDataRefs,
        out string[] monsterDataNames,
        out int[] remainingCounts,
        out int[] maxCounts,
        out int[] isBossValues,
        out int[] bossUniqueIds,
        out int[] targetPlayerIds,
        out int[] originPlayerIds)
    {
        int count = AttackMonsterPool != null ? AttackMonsterPool.Count : 0;
        monsterDataRefs = new MonsterData[count];
        monsterDataNames = new string[count];
        remainingCounts = new int[count];
        maxCounts = new int[count];
        isBossValues = new int[count];
        bossUniqueIds = new int[count];
        targetPlayerIds = new int[count];
        originPlayerIds = new int[count];

        for (int i = 0; i < count; i++)
        {
            var entry = AttackMonsterPool[i];
            monsterDataRefs[i] = entry?.MonsterData;
            monsterDataNames[i] = entry?.MonsterData != null ? entry.MonsterData.name : string.Empty;
            remainingCounts[i] = entry != null ? entry.RemainingCount : 0;
            maxCounts[i] = entry != null ? entry.MaxCount : 0;
            isBossValues[i] = entry != null && entry.IsBoss ? 1 : 0;
            bossUniqueIds[i] = entry != null ? entry.BossUniqueId : -1;
            targetPlayerIds[i] = entry != null ? entry.TargetPlayerId : -1;
            originPlayerIds[i] = entry != null ? entry.OriginPlayerId : -1;
        }
    }

    private async UniTask ApplyAttackMonsterPoolSnapshotAsync(
        int revision,
        string[] monsterDataNames,
        int[] remainingCounts,
        int[] maxCounts,
        int[] isBossValues,
        int[] bossUniqueIds,
        int[] targetPlayerIds,
        int[] originPlayerIds,
        bool allowStateAuthorityApply,
        string context)
    {
        if (!allowStateAuthorityApply && Object != null && Object.HasStateAuthority)
        {
            return;
        }

        if (revision < _lastAppliedAttackMonsterPoolRevision)
        {
            return;
        }

        var syncedPool = new List<MonsterPoolEntry>(monsterDataNames?.Length ?? 0);
        int count = monsterDataNames != null ? monsterDataNames.Length : 0;
        for (int i = 0; i < count; i++)
        {
            string monsterDataName = monsterDataNames[i];
            if (string.IsNullOrWhiteSpace(monsterDataName))
            {
                continue;
            }

            MonsterData monsterData = await ResolveAttackMonsterDataAsync(monsterDataName);
            if (monsterData == null)
            {
                Debug.LogWarning($"[PlayerManager] AttackMonsterPool snapshot apply aborted: unresolved MonsterData '{monsterDataName}' P{playerId} rev={revision} context={context}");
                if (Object != null && Object.HasInputAuthority && !Object.HasStateAuthority)
                {
                    RPC_RequestSyncData();
                }
                return;
            }

            syncedPool.Add(BuildAttackMonsterPoolEntry(
                monsterData,
                i,
                remainingCounts,
                maxCounts,
                isBossValues,
                bossUniqueIds,
                targetPlayerIds,
                originPlayerIds));
        }

        if (revision < _lastAppliedAttackMonsterPoolRevision)
        {
            return;
        }

        ApplyAttackMonsterPoolEntries(revision, syncedPool);
        if (allowStateAuthorityApply)
        {
            ResendAttackMonsterPoolToClientsIfAuthoritative();
            Debug.Log($"[PlayerManager] AttackMonsterPool async restore complete ({context}) P{playerId} rev={AttackMonsterPoolRevision} entries={AttackMonsterPool.Count}");
        }
    }

    private async UniTask<MonsterData> ResolveAttackMonsterDataAsync(string monsterDataName)
    {
        if (string.IsNullOrWhiteSpace(monsterDataName))
        {
            return null;
        }

        MonsterData data = FindLoadedMonsterDataByName(monsterDataName);
        if (data != null)
        {
            return data;
        }

        data = FindWaveMonsterDataByName(monsterDataName);
        if (data != null)
        {
            return data;
        }

        if (augmentManager != null)
        {
            await augmentManager.WaitUntilAugmentDataLoaded();
            data = augmentManager.FindMonsterDataByName(monsterDataName);
            if (data != null)
            {
                return data;
            }
        }

        return await AssetLoader.LoadAssetAsync<MonsterData>(monsterDataName);
    }

    private string ResolveLoadedMonsterDataNameByStableHash(int monsterDataKeyHash)
    {
        if (monsterDataKeyHash == 0)
        {
            return string.Empty;
        }

        MonsterData data = FindLoadedMonsterDataByStableHash(monsterDataKeyHash);
        if (data == null)
        {
            data = FindWaveMonsterDataByStableHash(monsterDataKeyHash);
        }

        return data != null ? data.name : string.Empty;
    }

    private static MonsterData FindLoadedMonsterDataByStableHash(int monsterDataKeyHash)
    {
        var loaded = Resources.FindObjectsOfTypeAll<MonsterData>();
        foreach (var data in loaded)
        {
            if (MatchesMonsterDataHash(data, monsterDataKeyHash))
            {
                return data;
            }
        }

        return null;
    }

    private static MonsterData FindWaveMonsterDataByStableHash(int monsterDataKeyHash)
    {
        var waveDatabase = AddressablesManager.Instance?.WaveDatabase;
        if (waveDatabase?.rounds == null)
        {
            return null;
        }

        foreach (var round in waveDatabase.rounds)
        {
            if (round?.monsters == null) continue;
            foreach (var entry in round.monsters)
            {
                if (MatchesMonsterDataHash(entry?.monsterData, monsterDataKeyHash))
                {
                    return entry.monsterData;
                }
            }
        }

        return null;
    }

    private static MonsterData FindLoadedMonsterDataByName(string monsterDataName)
    {
        var loaded = Resources.FindObjectsOfTypeAll<MonsterData>();
        foreach (var data in loaded)
        {
            if (MatchesMonsterData(data, monsterDataName))
            {
                return data;
            }
        }

        return null;
    }

    private static MonsterData FindWaveMonsterDataByName(string monsterDataName)
    {
        var waveDatabase = AddressablesManager.Instance?.WaveDatabase;
        if (waveDatabase?.rounds == null)
        {
            return null;
        }

        foreach (var round in waveDatabase.rounds)
        {
            if (round?.monsters == null) continue;
            foreach (var entry in round.monsters)
            {
                if (MatchesMonsterData(entry?.monsterData, monsterDataName))
                {
                    return entry.monsterData;
                }
            }
        }

        return null;
    }

    private static bool MatchesMonsterData(MonsterData data, string monsterDataName)
    {
        if (data == null || string.IsNullOrWhiteSpace(monsterDataName))
        {
            return false;
        }

        return string.Equals(data.name, monsterDataName, System.StringComparison.Ordinal)
            || string.Equals(data.monsterName, monsterDataName, System.StringComparison.Ordinal)
            || string.Equals(data.monsterPrefab, monsterDataName, System.StringComparison.Ordinal);
    }

    private static bool MatchesMonsterDataHash(MonsterData data, int monsterDataKeyHash)
    {
        if (data == null || monsterDataKeyHash == 0)
        {
            return false;
        }

        return StableMonsterDataKeyHash(data.name) == monsterDataKeyHash
            || StableMonsterDataKeyHash(data.monsterName) == monsterDataKeyHash
            || StableMonsterDataKeyHash(data.monsterPrefab) == monsterDataKeyHash;
    }

    private bool TryBuildAttackMonsterPoolFromRefs(
        MonsterData[] monsterDataRefs,
        int[] remainingCounts,
        int[] maxCounts,
        int[] isBossValues,
        int[] bossUniqueIds,
        int[] targetPlayerIds,
        int[] originPlayerIds,
        out List<MonsterPoolEntry> restoredPool)
    {
        restoredPool = new List<MonsterPoolEntry>(monsterDataRefs?.Length ?? 0);
        if (monsterDataRefs == null)
        {
            return false;
        }

        for (int i = 0; i < monsterDataRefs.Length; i++)
        {
            MonsterData monsterData = monsterDataRefs[i];
            if (monsterData == null)
            {
                return false;
            }

            restoredPool.Add(BuildAttackMonsterPoolEntry(
                monsterData,
                i,
                remainingCounts,
                maxCounts,
                isBossValues,
                bossUniqueIds,
                targetPlayerIds,
                originPlayerIds));
        }

        return true;
    }

    private static MonsterPoolEntry BuildAttackMonsterPoolEntry(
        MonsterData monsterData,
        int index,
        int[] remainingCounts,
        int[] maxCounts,
        int[] isBossValues,
        int[] bossUniqueIds,
        int[] targetPlayerIds,
        int[] originPlayerIds)
    {
        int remaining = ReadArrayValue(remainingCounts, index, 0);
        int max = ReadArrayValue(maxCounts, index, remaining);
        bool isBoss = ReadArrayValue(isBossValues, index, 0) != 0;
        var entry = isBoss
            ? new MonsterPoolEntry(
                monsterData,
                remaining,
                ReadArrayValue(bossUniqueIds, index, -1),
                ReadArrayValue(targetPlayerIds, index, -1),
                ReadArrayValue(originPlayerIds, index, -1))
            : new MonsterPoolEntry(monsterData, remaining);

        entry.MaxCount = max;
        entry.RemainingCount = remaining;
        return entry;
    }

    private void ApplyAttackMonsterPoolEntries(int revision, List<MonsterPoolEntry> pool)
    {
        _lastAppliedAttackMonsterPoolRevision = Mathf.Max(_lastAppliedAttackMonsterPoolRevision, revision);
        if (_pendingAttackMonsterPoolCommandRevision >= 0 && revision > _pendingAttackMonsterPoolCommandRevision)
        {
            _pendingAttackMonsterPoolCommandRevision = -1;
        }

        if (Object != null && Object.HasStateAuthority)
        {
            AttackMonsterPoolRevision = Mathf.Max(AttackMonsterPoolRevision, revision);
        }

        AttackMonsterPool = pool ?? new List<MonsterPoolEntry>();
        GameEvents.TriggerMonsterPoolChanged(playerId, AttackMonsterPool);
        PrewarmAttackMonsterPoolIfPossible("ApplyAttackMonsterPoolEntries");
    }

    private static int ReadArrayValue(int[] values, int index, int fallback)
    {
        return values != null && index >= 0 && index < values.Length ? values[index] : fallback;
    }
    #endregion

    #region 스탯 및 자원 관리
    public void AddPermanentAttackDamagePercent(float percent)
    {
        SetPermanentBonusesFromSync(permanentAttackDamagePercent + percent, permanentAttackSpeedPercent);
        
        // 클라이언트에 동기화
        if (Object != null && Object.HasStateAuthority)
        {
            RPC_SyncPermanentBonuses(permanentAttackDamagePercent, permanentAttackSpeedPercent);
        }
    }

    public void AddPermanentAttackSpeedPercent(float percent)
    {
        SetPermanentBonusesFromSync(permanentAttackDamagePercent, permanentAttackSpeedPercent + percent);
        
        // 클라이언트에 동기화
        if (Object != null && Object.HasStateAuthority)
        {
            RPC_SyncPermanentBonuses(permanentAttackDamagePercent, permanentAttackSpeedPercent);
        }
    }

    public void SetPermanentBonusesFromSync(float attackDamagePercent, float attackSpeedPercent)
    {
        permanentAttackDamagePercent = attackDamagePercent;
        permanentAttackSpeedPercent = attackSpeedPercent;

        if (Object != null && Object.IsValid && Object.HasStateAuthority)
        {
            PermanentAttackDamageBonusPermille = EncodePermanentBonus(attackDamagePercent);
            PermanentAttackSpeedBonusPermille = EncodePermanentBonus(attackSpeedPercent);
        }

        ApplyPermanentBonusesToUnitsOnField();
    }

    public float GetSnapshotPermanentAttackDamagePercent()
    {
        if (Object != null && Object.IsValid)
        {
            return DecodePermanentBonus(PermanentAttackDamageBonusPermille);
        }

        return permanentAttackDamagePercent;
    }

    public float GetSnapshotPermanentAttackSpeedPercent()
    {
        if (Object != null && Object.IsValid)
        {
            return DecodePermanentBonus(PermanentAttackSpeedBonusPermille);
        }

        return permanentAttackSpeedPercent;
    }

    private void ApplyPermanentBonusesFromNetworkSnapshot()
    {
        if (Object == null || !Object.IsValid || Object.HasStateAuthority)
        {
            return;
        }

        float attackDamagePercent = DecodePermanentBonus(PermanentAttackDamageBonusPermille);
        float attackSpeedPercent = DecodePermanentBonus(PermanentAttackSpeedBonusPermille);
        if (Mathf.Approximately(permanentAttackDamagePercent, attackDamagePercent) &&
            Mathf.Approximately(permanentAttackSpeedPercent, attackSpeedPercent))
        {
            return;
        }

        permanentAttackDamagePercent = attackDamagePercent;
        permanentAttackSpeedPercent = attackSpeedPercent;
        ApplyPermanentBonusesToUnitsOnField();
    }

    private static int EncodePermanentBonus(float percent)
    {
        return Mathf.RoundToInt(percent * PERMANENT_BONUS_NETWORK_SCALE);
    }

    private static float DecodePermanentBonus(int encoded)
    {
        return encoded / PERMANENT_BONUS_NETWORK_SCALE;
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
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (MPTestCommandLine.IsGameFlowFrozen)
        {
            return;
        }
#endif
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

    public bool IsShopPurchaseTransactionPending(int shopSlotIndex)
    {
        return GetShopPurchaseCoordinator().IsPending(shopSlotIndex);
    }

    public async UniTask<PurchaseUnitResult> TryPurchaseShopUnitAsync(
        int shopSlotIndex,
        CancellationToken cancellationToken = default)
    {
        return await GetShopPurchaseCoordinator().ExecuteAsync(shopSlotIndex, cancellationToken);
    }

    private PlayerShopPurchaseCoordinator GetShopPurchaseCoordinator()
    {
        return _shopPurchaseCoordinator ?? (_shopPurchaseCoordinator = new PlayerShopPurchaseCoordinator(this));
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

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_RequestCommandToServer(CommandType type, int[] intParams, string[] stringParams, Vector3[] vectorParams, RpcInfo info = default)
    {
        if (Runner == null || !Runner.IsServer) return; // 서버에서만 처리
        intParams = intParams != null ? intParams.ToArray() : System.Array.Empty<int>();
        stringParams = stringParams != null ? stringParams.ToArray() : System.Array.Empty<string>();
        vectorParams = vectorParams != null ? vectorParams.ToArray() : System.Array.Empty<Vector3>();

        if (!ValidateClientCommandRequest(type, intParams, stringParams, vectorParams, info, out string rejectReason))
        {
            if (type == CommandType.ActivateSkill && ActivateSkillCommand.IsVolatileNoOpReason(rejectReason))
            {
                Debug.Log($"[RPC_RequestCommandToServer] Skipped command={type}, playerId={playerId}, reason={rejectReason}");
            }
            else
            {
                Debug.LogWarning($"[RPC_RequestCommandToServer] Rejected command={type}, playerId={playerId}, reason={rejectReason}");
            }
            return;
        }

        if (intParams.Length > 0)
        {
            intParams[0] = playerId;
        }

        if (type == CommandType.PlaceWall || type == CommandType.RemoveWall)
        {
            string wallPos = (vectorParams != null && vectorParams.Length > 0)
                ? Vector3Int.RoundToInt(vectorParams[0]).ToString()
                : "none";
            string source = info.Source != PlayerRef.None ? info.Source.ToString() : "None";
            Debug.Log($"[RPC_RequestCommandToServer] {type} accepted. authoritativePlayer={playerId}, wallPos={wallPos}, source={source}");
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (MPTestCommandLine.IsEnabled)
        {
            string source = info.Source != PlayerRef.None ? info.Source.ToString() : "None";
            string firstVector = vectorParams != null && vectorParams.Length > 0
                ? Vector3Int.RoundToInt(vectorParams[0]).ToString()
                : "none";
            MPTestLogger.Log("accepted_command", "pass", type.ToString(), null, new Dictionary<string, object>
            {
                { "playerId", playerId },
                { "source", source },
                { "intParamCount", intParams.Length },
                { "stringParamCount", stringParams.Length },
                { "vectorParamCount", vectorParams.Length },
                { "firstVector", firstVector }
            });
        }
#endif

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

        gm.CommandProcessor?.ReceiveAndEnqueueCommand(type, intParams, stringParams, vectorParams);
        if (CommandProcessor.ShouldBroadcastCommandToClients(type))
        {
            gm.RPC_BroadcastCommandToClients(type, intParams, stringParams, vectorParams);
        }
    }

    private bool ValidateClientCommandRequest(
        CommandType type,
        int[] intParams,
        string[] stringParams,
        Vector3[] vectorParams,
        RpcInfo info,
        out string reason)
    {
        reason = null;

        if (Object == null || !Object.IsValid || !Object.HasStateAuthority)
        {
            reason = "player_missing_state_authority";
            return false;
        }

        if (info.Source == PlayerRef.None)
        {
            reason = "missing_rpc_source";
            return false;
        }

        if (Object.InputAuthority != info.Source)
        {
            reason = "rpc_source_not_input_authority";
            return false;
        }

        if (!IsReadyForPlayerActions)
        {
            reason = "player_not_ready";
            return false;
        }

        if (intParams.Length == 0)
        {
            reason = "missing_player_id";
            return false;
        }

        if (intParams[0] != playerId)
        {
            reason = $"player_id_mismatch:{intParams[0]}";
            return false;
        }

        var gm = GameManagers.Instance;
        if (gm == null)
        {
            gm = FindObjectOfType<GameManagers>();
        }

        if (gm == null || gm.Runner == null || !gm.Runner.IsServer || gm.Object == null || !gm.Object.HasStateAuthority)
        {
            reason = "game_managers_not_authoritative";
            return false;
        }

        switch (type)
        {
            case CommandType.BuyUnit:
                return ValidateBuyUnitRequest(gm, intParams, out reason);
            case CommandType.MoveUnit:
                return ValidateMoveUnitRequest(gm, vectorParams, out reason);
            case CommandType.SwapUnit:
                return ValidateSwapUnitRequest(gm, vectorParams, out reason);
            case CommandType.SellUnit:
                return ValidateSellUnitRequest(gm, vectorParams, out reason);
            case CommandType.PlaceUnit:
                reason = "place_unit_requires_authoritative_inventory";
                return false;
            case CommandType.PlaceWall:
                return ValidatePlaceWallRequest(gm, vectorParams, out reason);
            case CommandType.RemoveWall:
                return ValidateRemoveWallRequest(gm, vectorParams, out reason);
            case CommandType.RerollShop:
                return ValidateRerollShopRequest(gm, out reason);
            case CommandType.SelectAugment:
                return ValidateSelectAugmentRequest(gm, intParams, out reason);
            case CommandType.ActivateSkill:
                return ValidateActivateSkillRequest(gm, intParams, out reason);
            case CommandType.SetSkillActivationMode:
                return ValidateSetSkillActivationModeRequest(gm, intParams, out reason);
            case CommandType.RequestSyncData:
                return true;
            default:
                reason = $"server_only_or_unknown_command:{type}";
                return false;
        }
    }

    private bool ValidatePreparePhase(GameManagers gm, out string reason)
    {
        if (gm == null || gm.currentState != GameManagers.GameState.Prepare)
        {
            reason = "command_requires_prepare_phase";
            return false;
        }

        if (gm.IsSequenceTransitioning)
        {
            reason = "command_blocked_during_sequence_transition";
            return false;
        }

        reason = null;
        return true;
    }

    private bool ValidateBattlePhase(GameManagers gm, out string reason)
    {
        if (gm == null || (gm.currentState != GameManagers.GameState.Battle1 && gm.currentState != GameManagers.GameState.Battle2))
        {
            reason = "command_requires_battle_phase";
            return false;
        }

        if (gm.IsSequenceTransitioning)
        {
            reason = "command_blocked_during_sequence_transition";
            return false;
        }

        reason = null;
        return true;
    }

    private bool ValidateBuyUnitRequest(GameManagers gm, int[] intParams, out string reason)
    {
        if (!ValidatePreparePhase(gm, out reason)) return false;
        if (intParams.Length < 2)
        {
            reason = "missing_shop_slot";
            return false;
        }

        if (shopManager == null || !shopManager.IsDatabaseLoaded)
        {
            reason = "shop_not_ready";
            return false;
        }

        int slotIndex = intParams[1];
        var items = shopManager.GetCurrentShopItems();
        if (slotIndex < 0 || slotIndex >= items.Count)
        {
            reason = "shop_slot_out_of_range";
            return false;
        }

        if (shopManager.IsSlotSold(slotIndex))
        {
            reason = "shop_slot_already_sold";
            return false;
        }

        if (IsShopPurchaseTransactionPending(slotIndex))
        {
            reason = "shop_slot_purchase_pending";
            return false;
        }

        var item = items[slotIndex];
        if (item.UnitData == null)
        {
            reason = "shop_item_missing_unit_data";
            return false;
        }

        if (GetGold() < item.CalculatedCost)
        {
            reason = "insufficient_gold";
            return false;
        }

        reason = null;
        return true;
    }

    private bool ValidateSetSkillActivationModeRequest(GameManagers gm, int[] intParams, out string reason)
    {
        if (gm == null || gm.IsSequenceTransitioning ||
            (gm.currentState != GameManagers.GameState.Prepare &&
             gm.currentState != GameManagers.GameState.Battle1 &&
             gm.currentState != GameManagers.GameState.Battle2))
        {
            reason = "skill_activation_mode_phase_invalid";
            return false;
        }
        if (intParams == null || intParams.Length < 3)
        {
            reason = "skill_activation_mode_payload_missing";
            return false;
        }
        if (intParams[0] != playerId)
        {
            reason = "skill_activation_mode_player_mismatch";
            return false;
        }
        if (intParams[1] == 0)
        {
            reason = "skill_activation_mode_network_id_invalid";
            return false;
        }

        var requestedMode = (SkillActivationType)intParams[2];
        if (requestedMode != SkillActivationType.Manual && requestedMode != SkillActivationType.Automatic)
        {
            reason = "skill_activation_mode_value_invalid";
            return false;
        }
        uint requestedNetworkId = unchecked((uint)intParams[1]);
        if (!SetSkillActivationModeCommand.TryValidate(
                gm,
                playerId,
                requestedNetworkId,
                requestedMode,
                out _,
                out reason))
        {
            return false;
        }

        reason = null;
        return true;
    }

    private bool ValidateMoveUnitRequest(GameManagers gm, Vector3[] vectorParams, out string reason)
    {
        if (!ValidatePreparePhase(gm, out reason)) return false;
        if (fieldManager == null)
        {
            reason = "field_not_ready";
            return false;
        }

        if (vectorParams.Length < 2)
        {
            reason = "missing_move_positions";
            return false;
        }

        Vector3Int from = Vector3Int.RoundToInt(vectorParams[0]);
        Vector3Int to = Vector3Int.RoundToInt(vectorParams[1]);
        if (!fieldManager.IsValidGridPosition(from) || !fieldManager.IsValidGridPosition(to))
        {
            reason = "move_position_out_of_range";
            return false;
        }

        var unit = fieldManager.GetUnitAt(from);
        UnitData sourceUnitData = unit != null ? unit.Data : null;
        if (unit == null && !fieldManager.HasPendingUnitAt(from))
        {
            reason = "move_source_empty";
            return false;
        }

        if (unit != null && !OwnsUnitForCommand(unit))
        {
            reason = "move_source_not_owned_by_player";
            return false;
        }

        if (unit == null && fieldManager.TryGetPendingUnitDataAt(from, out var pendingUnitData))
        {
            sourceUnitData = pendingUnitData;
        }

        if (fieldManager.IsUnitAt(to))
        {
            reason = "move_destination_occupied";
            return false;
        }

        if (sourceUnitData == null && fieldManager.HasWallAt(to))
        {
            reason = "move_pending_unit_type_unknown_for_wall";
            return false;
        }

        if (sourceUnitData != null && sourceUnitData.unitType == UnitType.Melee && fieldManager.HasWallAt(to))
        {
            reason = "melee_unit_cannot_move_to_wall";
            return false;
        }

        reason = null;
        return true;
    }

    private bool OwnsUnitForCommand(Unit unit)
    {
        return IsUnitOwnedByPlayerForCommand(this, unit);
    }

    public static bool IsUnitOwnedByPlayerForCommand(PlayerManager player, Unit unit)
    {
        if (unit == null)
        {
            return false;
        }

        if (player == null)
        {
            return false;
        }

        if (unit.Owner != null)
        {
            return unit.Owner == player || unit.Owner.playerId == player.playerId;
        }

        int rosterOwnerId = unit.OwnerPlayerIdForRoster;
        if (rosterOwnerId >= 0)
        {
            return rosterOwnerId == player.playerId;
        }

        return player.ownedUnits != null && player.ownedUnits.Contains(unit);
    }

    private bool ValidateSwapUnitRequest(GameManagers gm, Vector3[] vectorParams, out string reason)
    {
        if (!ValidatePreparePhase(gm, out reason)) return false;
        if (fieldManager == null)
        {
            reason = "field_not_ready";
            return false;
        }

        if (vectorParams.Length < 2)
        {
            reason = "missing_swap_positions";
            return false;
        }

        Vector3Int posA = Vector3Int.RoundToInt(vectorParams[0]);
        Vector3Int posB = Vector3Int.RoundToInt(vectorParams[1]);
        if (!fieldManager.IsValidGridPosition(posA) || !fieldManager.IsValidGridPosition(posB))
        {
            reason = "swap_position_out_of_range";
            return false;
        }

        var unitA = fieldManager.GetUnitAt(posA);
        var unitB = fieldManager.GetUnitAt(posB);
        if (unitA == null || unitB == null)
        {
            reason = "swap_requires_two_units";
            return false;
        }

        if (!OwnsUnitForCommand(unitA) || !OwnsUnitForCommand(unitB))
        {
            reason = "swap_unit_not_owned_by_player";
            return false;
        }

        if (unitA.Data == null || unitB.Data == null)
        {
            reason = "swap_unit_data_unresolved";
            return false;
        }

        if (unitA.Data != null && unitA.Data.unitType == UnitType.Melee && fieldManager.HasWallAt(posB))
        {
            reason = "melee_unit_a_cannot_swap_to_wall";
            return false;
        }

        if (unitB.Data != null && unitB.Data.unitType == UnitType.Melee && fieldManager.HasWallAt(posA))
        {
            reason = "melee_unit_b_cannot_swap_to_wall";
            return false;
        }

        reason = null;
        return true;
    }

    private bool ValidateSellUnitRequest(GameManagers gm, Vector3[] vectorParams, out string reason)
    {
        if (!ValidatePreparePhase(gm, out reason)) return false;
        if (fieldManager == null)
        {
            reason = "field_not_ready";
            return false;
        }

        if (vectorParams.Length < 1)
        {
            reason = "missing_sell_position";
            return false;
        }

        Vector3Int position = Vector3Int.RoundToInt(vectorParams[0]);
        if (!fieldManager.IsValidGridPosition(position))
        {
            reason = "sell_position_out_of_range";
            return false;
        }

        if (fieldManager.GetUnitAt(position) == null)
        {
            reason = "sell_position_empty";
            return false;
        }

        reason = null;
        return true;
    }

    private bool ValidatePlaceWallRequest(GameManagers gm, Vector3[] vectorParams, out string reason)
    {
        if (!ValidatePreparePhase(gm, out reason)) return false;
        if (fieldManager == null)
        {
            reason = "field_not_ready";
            return false;
        }

        if (vectorParams.Length < 1)
        {
            reason = "missing_wall_position";
            return false;
        }

        Vector3Int position = Vector3Int.RoundToInt(vectorParams[0]);
        if (!fieldManager.IsValidGridPosition(position))
        {
            reason = "wall_position_out_of_range";
            return false;
        }

        if (fieldManager.HasWallAt(position))
        {
            reason = "wall_position_occupied";
            return false;
        }

        var occupant = fieldManager.GetUnitAt(position);
        if (occupant != null && !OwnsUnitForCommand(occupant))
        {
            reason = "wall_position_foreign_unit";
            return false;
        }

        if (occupant != null && occupant.Data == null)
        {
            reason = "wall_position_unresolved_unit";
            return false;
        }

        if (GetWallCount() <= 0)
        {
            reason = "insufficient_wall_stock";
            return false;
        }

        if (goalTransform != null && position == fieldManager.WorldToGridInt(goalTransform.position))
        {
            reason = "wall_goal_cell_blocked";
            return false;
        }

        reason = null;
        return true;
    }

    private bool ValidateRemoveWallRequest(GameManagers gm, Vector3[] vectorParams, out string reason)
    {
        if (!ValidatePreparePhase(gm, out reason)) return false;
        if (fieldManager == null)
        {
            reason = "field_not_ready";
            return false;
        }

        if (vectorParams.Length < 1)
        {
            reason = "missing_remove_wall_position";
            return false;
        }

        Vector3Int position = Vector3Int.RoundToInt(vectorParams[0]);
        if (!fieldManager.IsValidGridPosition(position))
        {
            reason = "remove_wall_position_out_of_range";
            return false;
        }

        if (fieldManager.GetWallAt(position) == null)
        {
            reason = "remove_wall_missing";
            return false;
        }

        reason = null;
        return true;
    }

    private bool ValidateRerollShopRequest(GameManagers gm, out string reason)
    {
        if (!ValidatePreparePhase(gm, out reason)) return false;
        if (shopManager == null || !shopManager.IsDatabaseLoaded)
        {
            reason = "shop_not_ready";
            return false;
        }

        int cost = shopManager.GetRerollCost();
        if (GetGold() < cost)
        {
            reason = "insufficient_gold";
            return false;
        }

        reason = null;
        return true;
    }

    private bool ValidateSelectAugmentRequest(GameManagers gm, int[] intParams, out string reason)
    {
        if (!ValidatePreparePhase(gm, out reason)) return false;
        if (intParams.Length < 2)
        {
            reason = "missing_augment_index";
            return false;
        }

        if (augmentManager == null)
        {
            reason = "augment_manager_not_ready";
            return false;
        }

        var presentedAugments = augmentManager.GetPresentedAugments();
        int index = intParams[1];
        if (presentedAugments == null || index < 0 || index >= presentedAugments.Count)
        {
            reason = "augment_index_out_of_range";
            return false;
        }

        if (presentedAugments[index] == null)
        {
            reason = "augment_choice_missing";
            return false;
        }

        reason = null;
        return true;
    }

    private bool ValidateActivateSkillRequest(GameManagers gm, int[] intParams, out string reason)
    {
        if (intParams.Length < 2)
        {
            reason = "missing_skill_unit_id";
            return false;
        }

        uint unitNetworkId = (uint)intParams[1];
        const CommandExecutionScope scope = CommandExecutionScope.ClientRequest;
        const string source = "client_rpc";
        SkillCommandMpTestLogger.Request(playerId, unitNetworkId, scope, source);

        if (!ActivateSkillCommand.TryValidate(
                gm,
                playerId,
                unitNetworkId,
                scope,
                source,
                requireStateAuthority: true,
                out _,
                out SkillData skillData,
                out BattleCommandResult result))
        {
            reason = result.ErrorCode;
            if (ActivateSkillCommand.IsVolatileNoOp(result))
            {
                SkillCommandMpTestLogger.Skipped(result, unitNetworkId, skillData != null ? skillData.name : "unknown");
                return false;
            }

            int sequence = BattleCommandTelemetry.RecordRejected(CommandType.ActivateSkill);
            var rejected = BattleCommandResult.Rejected(
                CommandType.ActivateSkill,
                result.PlayerId,
                result.ErrorCode,
                result.Message,
                result.OpponentPlayerId,
                result.Scope,
                result.Source,
                sequence);
            SkillCommandMpTestLogger.Rejected(rejected, unitNetworkId, skillData != null ? skillData.name : "unknown");
            gm?.SyncBattleCommandTelemetryToClientsIfAuthoritative();
            return false;
        }

        SkillCommandMpTestLogger.Accepted(result, unitNetworkId, skillData != null ? skillData.name : "unknown");
        reason = null;
        return true;
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

    #region Goal 위치 설정

    /// <summary>
    /// goal 위치를 동적으로 설정합니다.
    /// - 골: 필드 정 가운데 그리드
    /// </summary>
    private void SetupGoalPosition(GameObject gridInstance)
    {
        if (gridInstance == null)
        {
            // Debug.LogWarning($"[Player {playerId}]: SetupGoalPosition skipped - gridInstance is null.");
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

        // 기존 Goal 오브젝트를 찾아보고, 없으면 새로 생성
        Transform existingGoal = gridInstance.transform.Find("Goal");
        if (existingGoal == null)
        {
            existingGoal = FindChildByNameRecursive(gridInstance.transform, "Goal");
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
