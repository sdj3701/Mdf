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
using MDF.Runtime.Assets;

public partial class PlayerManager : NetworkBehaviour // [수정] MonoBehaviour -> NetworkBehaviour
{
    private AddressableAssetOwner _assetOwner = new AddressableAssetOwner();

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
    [Networked] public int UnitRosterRevision { get; private set; }
    private const int SHOP_SNAPSHOT_CAPACITY = 5;
    [Networked, Capacity(SHOP_SNAPSHOT_CAPACITY)] private NetworkArray<ShopSnapshotSlot> ShopSnapshotSlots { get; }
    [Networked] private int ShopSnapshotRevision { get; set; }
    [Networked] private int ShopSnapshotCount { get; set; }
    [Networked] private int ShopSnapshotRound { get; set; }
    [Networked] private NetworkBool ShopSnapshotOverflow { get; set; }
    private const int PRESENTED_AUGMENT_SNAPSHOT_CAPACITY = 3;
    [Networked, Capacity(PRESENTED_AUGMENT_SNAPSHOT_CAPACITY)] private NetworkArray<int> PresentedAugmentSnapshotIds { get; }
    [Networked] private int PresentedAugmentSnapshotCount { get; set; }
    [Networked] private int PresentedAugmentSnapshotRevision { get; set; }
    [Networked] private NetworkBool PresentedAugmentSnapshotOverflow { get; set; }
    private const int SELECTED_AUGMENT_SNAPSHOT_CAPACITY = 64;
    private const int MAX_SELECTED_AUGMENT_MIGRATION_ENTRY_COUNT = 4096;
    [Networked, Capacity(SELECTED_AUGMENT_SNAPSHOT_CAPACITY)] private NetworkArray<int> SelectedAugmentSnapshotIds { get; }
    [Networked, Capacity(SELECTED_AUGMENT_SNAPSHOT_CAPACITY)] private NetworkArray<int> SelectedAugmentSnapshotCounts { get; }
    [Networked] private int SelectedAugmentSnapshotCount { get; set; }
    [Networked] private int SelectedAugmentSnapshotRevision { get; set; }
    [Networked] private NetworkBool SelectedAugmentSnapshotOverflow { get; set; }
    // Before immutable content ids shipped, four different scroll augments shared this display
    // name. A legacy hash/name with this value is intrinsically ambiguous and must fail closed.
    private const string KNOWN_AMBIGUOUS_LEGACY_SCROLL_NAME =
        "\uB9C8\uBC95\uC2A4\uD06C\uB864(\uD68C\uBCF5)";
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
    private int _pendingBlackMagicCommandRevision = -1;
    private float _pendingAttackMonsterPoolCommandStartedAt;
    private CancellationTokenSource _attackMonsterPrewarmCancellation;
    private CancellationTokenSource _shopSnapshotSyncLifetimeCancellation;
    private int _attackMonsterPrewarmGeneration;
    private const float ATTACK_MONSTER_COMMAND_PENDING_TIMEOUT_SECONDS = 2f;
    public int AppliedAttackMonsterPoolRevision =>
        Object != null && Object.HasStateAuthority ? AttackMonsterPoolRevision : _lastAppliedAttackMonsterPoolRevision;
    public bool HasAppliedCurrentAttackMonsterPoolSnapshot =>
        Object != null && Object.HasStateAuthority || _lastAppliedAttackMonsterPoolRevision == AttackMonsterPoolRevision;
    public bool HasPendingAttackMonsterPoolCommand
    {
        get
        {
            bool pending = Object != null &&
                           !Object.HasStateAuthority &&
                           _pendingAttackMonsterPoolCommandRevision >= 0 &&
                           _lastAppliedAttackMonsterPoolRevision <= _pendingAttackMonsterPoolCommandRevision &&
                           (_pendingBlackMagicCommandRevision < 0 ||
                            AppliedBlackMagicRevision <= _pendingBlackMagicCommandRevision);
            if (!pending)
            {
                ClearPendingAttackMonsterPoolCommand();
                return false;
            }

            if (Time.unscaledTime - _pendingAttackMonsterPoolCommandStartedAt <= ATTACK_MONSTER_COMMAND_PENDING_TIMEOUT_SECONDS)
            {
                return true;
            }

            ClearPendingAttackMonsterPoolCommand();
            if (Object.HasInputAuthority)
            {
                RPC_RequestSyncData();
            }
            return false;
        }
    }
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
    public bool IsReadyForPlayerActions => _runtimeInitialized
                                           && playerId >= 0
                                           && fieldManager != null
                                           && IsKingRuntimeDataReady(out _);
    private PlayerShopPurchaseCoordinator _shopPurchaseCoordinator;
    private PlayerCommandRequestValidator _commandRequestValidator;
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
        return StableDataKeyUtility.NormalizeKey(key);
    }

    private static int StableDataKeyHash(string value)
    {
        return StableDataKeyUtility.StableHash(value);
    }

    private static int StableAugmentSnapshotId(string augmentContentId)
    {
        return StableDataKeyUtility.StableContentIdHash(augmentContentId);
    }

    private static int StableLegacyAugmentSnapshotId(string legacyName)
    {
        return StableDataKeyHash(string.IsNullOrWhiteSpace(legacyName) ? string.Empty : legacyName.Trim());
    }

    private static bool IsKnownAmbiguousLegacyAugmentReference(string legacyReference)
    {
        return string.Equals(
            legacyReference?.Trim(),
            KNOWN_AMBIGUOUS_LEGACY_SCROLL_NAME,
            StringComparison.Ordinal);
    }

    private static int NextSnapshotRevision(int revision)
    {
        return revision >= int.MaxValue - 1 ? 1 : Mathf.Max(1, revision + 1);
    }

    private static int StableMonsterDataKeyHash(string value)
    {
        return StableDataKeyHash(NormalizeShopUnitKey(value));
    }

    private static int PackShopSnapshotMeta(int starLevel, int sold)
    {
        return PlayerSnapshotCodec.PackShopMeta(starLevel, sold != 0);
    }

    private static int PackAttackMonsterCounts(int remainingCount, int maxCount, int isBoss)
    {
        return PlayerSnapshotCodec.PackMonsterCounts(remainingCount, maxCount, isBoss != 0);
    }

    private static int PackSnapshotPlayerIds(int targetPlayerId, int originPlayerId)
    {
        return PlayerSnapshotCodec.PackPlayerIds(targetPlayerId, originPlayerId);
    }

    private static int PackSnapshotPlayerId(int playerIdValue)
    {
        return PlayerSnapshotCodec.PackPlayerId(playerIdValue);
    }

    private static int UnpackSnapshotPlayerId(int packedPlayerId)
    {
        return PlayerSnapshotCodec.UnpackPlayerId(packedPlayerId);
    }

    private static string ResolveLoadedUnitDataKeyByStableHash(int unitDataKeyHash)
    {
        return TryResolveLoadedUnitDataKeyByStableHash(
                unitDataKeyHash,
                out string unitDataKey,
                out _)
            ? unitDataKey
            : string.Empty;
    }

    private static bool TryResolveLoadedUnitDataKeyByStableHash(
        int unitDataKeyHash,
        out string key,
        out string failureReason)
    {
        key = string.Empty;
        failureReason = string.Empty;
        if (unitDataKeyHash == 0)
        {
            failureReason = "shop_unit_key_hash_zero";
            return false;
        }

        LoadManager loadManager = LoadManager.Instance;
        UnitData indexedData = loadManager?.GetUnitDataByContentIdHash(unitDataKeyHash);
        if (indexedData != null)
        {
            key = NormalizeShopUnitKey(indexedData.name);
            return !string.IsNullOrEmpty(key);
        }

        var contentIdMatches = new HashSet<UnitData>();
        var legacyMatches = new HashSet<UnitData>();
        void AppendMatches(IEnumerable<UnitData> units)
        {
            if (units == null)
            {
                return;
            }

            foreach (UnitData data in units)
            {
                if (data == null || string.IsNullOrWhiteSpace(data.name))
                {
                    continue;
                }

                if (data.ContentIdHash == unitDataKeyHash)
                {
                    contentIdMatches.Add(data);
                }
                else if (StableUnitDataKeyHash(data.name) == unitDataKeyHash ||
                         StableUnitDataKeyHash(data.unitName) == unitDataKeyHash)
                {
                    legacyMatches.Add(data);
                }
            }
        }

        AppendMatches(loadManager != null ? loadManager.GetAllUnitData() : null);
        AppendMatches(Resources.FindObjectsOfTypeAll<UnitData>());
        if (contentIdMatches.Count == 1)
        {
            key = NormalizeShopUnitKey(contentIdMatches.First().name);
            return !string.IsNullOrEmpty(key);
        }
        if (contentIdMatches.Count > 1)
        {
            failureReason = $"shop_unit_content_id_hash_ambiguous:{unitDataKeyHash}";
            return false;
        }

        if (legacyMatches.Count == 1)
        {
            key = NormalizeShopUnitKey(legacyMatches.First().name);
            return !string.IsNullOrEmpty(key);
        }

        failureReason = legacyMatches.Count > 1
            ? $"shop_unit_key_hash_ambiguous:{unitDataKeyHash}"
            : $"shop_unit_key_hash_unresolved:{unitDataKeyHash}";
        return false;
    }

    private static string ResolveLoadedAugmentContentIdByStableId(int augmentId)
    {
        return TryResolveLoadedAugmentContentIdByStableId(augmentId, out string contentId, out _)
            ? contentId
            : string.Empty;
    }

    private static bool TryResolveLoadedAugmentContentIdByStableId(
        int augmentId,
        out string contentId,
        out string failureReason)
    {
        contentId = string.Empty;
        failureReason = string.Empty;
        if (augmentId == 0)
        {
            failureReason = "augment_id_zero";
            return false;
        }

        AugmentData[] loadedAugments = Resources.FindObjectsOfTypeAll<AugmentData>();
        string[] contentIdMatches = loadedAugments
            .Where(data => data != null && data.ContentIdHash == augmentId)
            .Select(data => data.ContentId)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (contentIdMatches.Length == 1)
        {
            contentId = contentIdMatches[0];
            return true;
        }

        if (contentIdMatches.Length > 1)
        {
            failureReason = $"augment_content_id_hash_ambiguous:{augmentId}";
            return false;
        }

        if (augmentId == StableLegacyAugmentSnapshotId(KNOWN_AMBIGUOUS_LEGACY_SCROLL_NAME))
        {
            failureReason = $"known_ambiguous_legacy_augment_id:{augmentId}";
            return false;
        }

        // Compatibility for snapshots captured before immutable content ids shipped. A legacy
        // display/asset-name hash is accepted only when it identifies exactly one immutable id.
        string[] legacyMatches = loadedAugments
            .Where(data => data != null &&
                           (StableLegacyAugmentSnapshotId(data.augmentName) == augmentId ||
                            StableLegacyAugmentSnapshotId(data.name) == augmentId))
            .Select(data => data.ContentId)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (legacyMatches.Length == 1)
        {
            contentId = legacyMatches[0];
            return true;
        }

        failureReason = legacyMatches.Length > 1
            ? $"legacy_augment_id_ambiguous:{augmentId}"
            : $"augment_id_unresolved:{augmentId}";
        return false;
    }

    private static bool TryResolveAugmentContentIdReference(
        string augmentReference,
        out string contentId,
        out string failureReason)
    {
        contentId = string.Empty;
        failureReason = string.Empty;
        if (string.IsNullOrWhiteSpace(augmentReference))
        {
            failureReason = "augment_reference_empty";
            return false;
        }

        string normalized = StableDataKeyUtility.NormalizeContentId(augmentReference);
        AugmentData[] loadedAugments = Resources.FindObjectsOfTypeAll<AugmentData>();
        string[] contentIdMatches = loadedAugments
            .Where(data => data != null && string.Equals(data.ContentId, normalized, StringComparison.Ordinal))
            .Select(data => data.ContentId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (contentIdMatches.Length == 1)
        {
            contentId = contentIdMatches[0];
            return true;
        }

        if (contentIdMatches.Length > 1)
        {
            failureReason = $"augment_content_id_ambiguous:{normalized}";
            return false;
        }

        // Compatibility for host-migration payloads captured by older builds. Duplicate legacy
        // names are deliberately rejected because choosing the first match corrupts gameplay.
        string legacyReference = augmentReference.Trim();
        if (IsKnownAmbiguousLegacyAugmentReference(legacyReference))
        {
            failureReason = $"known_ambiguous_legacy_augment_reference:{legacyReference}";
            return false;
        }

        string[] legacyMatches = loadedAugments
            .Where(data => data != null &&
                           (string.Equals(data.augmentName, legacyReference, StringComparison.Ordinal) ||
                            string.Equals(data.name, legacyReference, StringComparison.Ordinal)))
            .Select(data => data.ContentId)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (legacyMatches.Length == 1)
        {
            contentId = legacyMatches[0];
            return true;
        }

        failureReason = legacyMatches.Length > 1
            ? $"legacy_augment_reference_ambiguous:{legacyReference}"
            : $"augment_reference_unresolved:{legacyReference}";
        return false;
    }

    public void PublishShopSnapshot(IReadOnlyList<ShopItem> items, bool[] soldFlags, string context)
    {
        if (Object == null || !Object.IsValid || !Object.HasStateAuthority)
        {
            return;
        }

        int requestedCount = items?.Count ?? 0;
        for (int i = 0; i < SHOP_SNAPSHOT_CAPACITY; i++)
        {
            ShopSnapshotSlots.Set(i, default);
        }

        string failureReason = string.Empty;
        if (items == null)
        {
            failureReason = "items_null";
        }
        else if (requestedCount > SHOP_SNAPSHOT_CAPACITY)
        {
            failureReason = $"capacity_exceeded:{requestedCount}/{SHOP_SNAPSHOT_CAPACITY}";
        }
        else if (soldFlags == null || soldFlags.Length < requestedCount)
        {
            failureReason = $"sold_flags_shape_invalid:{soldFlags?.Length ?? -1}/{requestedCount}";
        }

        for (int i = 0; string.IsNullOrEmpty(failureReason) && i < requestedCount; i++)
        {
            var item = items[i];
            if (item.UnitData == null)
            {
                failureReason = $"slot_{i}:unit_data_missing";
                break;
            }

            string unitKey = NormalizeShopUnitKey(item.UnitData.name);
            int starLevel = item.StarLevel;
            if (string.IsNullOrWhiteSpace(unitKey) || starLevel <= 0 || starLevel > byte.MaxValue)
            {
                failureReason = $"slot_{i}:identity_or_star_invalid";
                break;
            }
            int sold = soldFlags != null && i < soldFlags.Length && soldFlags[i] ? 1 : 0;

            ShopSnapshotSlots.Set(i, new ShopSnapshotSlot
            {
                UnitKeyHash = item.UnitData.ContentIdHash != 0
                    ? item.UnitData.ContentIdHash
                    : StableUnitDataKeyHash(unitKey),
                PackedMeta = PackShopSnapshotMeta(starLevel, sold)
            });
        }

        if (!string.IsNullOrEmpty(failureReason))
        {
            for (int i = 0; i < SHOP_SNAPSHOT_CAPACITY; i++)
            {
                ShopSnapshotSlots.Set(i, default);
            }
        }

        ShopSnapshotCount = string.IsNullOrEmpty(failureReason) ? requestedCount : 0;
        ShopSnapshotOverflow = !string.IsNullOrEmpty(failureReason);
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
        if (!string.IsNullOrEmpty(failureReason))
        {
            Debug.LogError($"[PlayerManager] Shop durable snapshot rejected ({context}) P{playerId}: {failureReason}");
        }
    }

    public bool TryGetShopSnapshot(out string[] unitKeys, out int[] starLevels, out bool[] soldFlags, out int revision, out int round)
    {
        return TryGetShopSnapshot(
            out unitKeys,
            out starLevels,
            out soldFlags,
            out revision,
            out round,
            out _);
    }

    public bool TryGetShopSnapshot(
        out string[] unitKeys,
        out int[] starLevels,
        out bool[] soldFlags,
        out int revision,
        out int round,
        out string failureReason)
    {
        unitKeys = System.Array.Empty<string>();
        starLevels = System.Array.Empty<int>();
        soldFlags = System.Array.Empty<bool>();
        revision = 0;
        round = 0;
        failureReason = string.Empty;

        if (Object == null || !Object.IsValid)
        {
            failureReason = "shop_snapshot_network_object_invalid";
            return false;
        }
        if (ShopSnapshotOverflow)
        {
            failureReason = "shop_snapshot_overflow";
            return false;
        }

        revision = ShopSnapshotRevision;
        round = ShopSnapshotRound;
        int count = ShopSnapshotCount;
        if (revision <= 0)
        {
            failureReason = $"shop_snapshot_revision_invalid:{revision}";
            return false;
        }
        if (round < 0)
        {
            failureReason = $"shop_snapshot_round_invalid:{round}";
            return false;
        }
        if (count < 0 || count > SHOP_SNAPSHOT_CAPACITY)
        {
            failureReason = $"shop_snapshot_count_out_of_range:{count}";
            return false;
        }

        unitKeys = new string[count];
        starLevels = new int[count];
        soldFlags = new bool[count];

        for (int i = 0; i < count; i++)
        {
            var slot = ShopSnapshotSlots[i];
            int keyHash = slot.UnitKeyHash;
            if (!TryResolveLoadedUnitDataKeyByStableHash(
                    keyHash,
                    out unitKeys[i],
                    out string unitFailureReason))
            {
                failureReason = $"shop_snapshot_slot_{i}:{unitFailureReason}";
                unitKeys = System.Array.Empty<string>();
                starLevels = System.Array.Empty<int>();
                soldFlags = System.Array.Empty<bool>();
                return false;
            }

            if ((slot.PackedMeta & ~0x1FF) != 0 || slot.StarLevel <= 0)
            {
                failureReason = $"shop_snapshot_slot_{i}:packed_meta_invalid:{slot.PackedMeta}";
                unitKeys = System.Array.Empty<string>();
                starLevels = System.Array.Empty<int>();
                soldFlags = System.Array.Empty<bool>();
                return false;
            }

            starLevels[i] = slot.StarLevel;
            soldFlags[i] = slot.Sold != 0;
        }

        return true;
    }

    public void PublishSelectedAugmentSnapshot(AugmentData augment)
    {
        PublishSelectedAugmentSnapshot(augment != null ? augment.ContentId : string.Empty);
    }

    public void PublishSelectedAugmentSnapshot(string augmentContentId)
    {
        if (Object == null || !Object.IsValid || !Object.HasStateAuthority)
        {
            return;
        }

        if (!TryResolveAugmentContentIdReference(
                augmentContentId,
                out string resolvedContentId,
                out string resolveFailureReason))
        {
            SelectedAugmentSnapshotOverflow = true;
            SelectedAugmentSnapshotRevision = NextSnapshotRevision(SelectedAugmentSnapshotRevision);
            Debug.LogError($"[PlayerManager] Selected augment snapshot rejected. playerId={playerId}, reference={augmentContentId}, reason={resolveFailureReason}");
            return;
        }

        int augmentId = StableAugmentSnapshotId(resolvedContentId);
        int uniqueCount = SelectedAugmentSnapshotCount;
        if (uniqueCount < 0 || uniqueCount > SELECTED_AUGMENT_SNAPSHOT_CAPACITY)
        {
            SelectedAugmentSnapshotOverflow = true;
            SelectedAugmentSnapshotRevision = NextSnapshotRevision(SelectedAugmentSnapshotRevision);
            Debug.LogError($"[PlayerManager] Selected augment durable snapshot count invalid. playerId={playerId}, count={uniqueCount}");
            return;
        }
        long currentExpandedCount = 0;
        for (int i = 0; i < uniqueCount; i++)
        {
            int existingRepetitions = SelectedAugmentSnapshotCounts.Get(i);
            if (existingRepetitions <= 0)
            {
                SelectedAugmentSnapshotOverflow = true;
                SelectedAugmentSnapshotRevision = NextSnapshotRevision(SelectedAugmentSnapshotRevision);
                Debug.LogError($"[PlayerManager] Selected augment durable snapshot row count invalid. playerId={playerId}, slot={i}, count={existingRepetitions}");
                return;
            }
            currentExpandedCount += existingRepetitions;
        }
        if (currentExpandedCount >= MAX_SELECTED_AUGMENT_MIGRATION_ENTRY_COUNT)
        {
            SelectedAugmentSnapshotOverflow = true;
            SelectedAugmentSnapshotRevision = NextSnapshotRevision(SelectedAugmentSnapshotRevision);
            Debug.LogError($"[PlayerManager] Selected augment durable snapshot expanded capacity exceeded. playerId={playerId}, count={currentExpandedCount}");
            return;
        }

        for (int i = 0; i < uniqueCount; i++)
        {
            if (SelectedAugmentSnapshotIds.Get(i) == augmentId)
            {
                int repetitions = SelectedAugmentSnapshotCounts.Get(i);
                if (repetitions <= 0 || repetitions >= MAX_SELECTED_AUGMENT_MIGRATION_ENTRY_COUNT)
                {
                    SelectedAugmentSnapshotOverflow = true;
                    SelectedAugmentSnapshotRevision = NextSnapshotRevision(SelectedAugmentSnapshotRevision);
                    Debug.LogError($"[PlayerManager] Selected augment durable snapshot repetitions invalid. playerId={playerId}, augmentId={resolvedContentId}, count={repetitions}");
                    return;
                }

                SelectedAugmentSnapshotCounts.Set(i, repetitions + 1);
                SelectedAugmentSnapshotRevision = NextSnapshotRevision(SelectedAugmentSnapshotRevision);
                return;
            }
        }

        if (uniqueCount >= SELECTED_AUGMENT_SNAPSHOT_CAPACITY)
        {
            SelectedAugmentSnapshotOverflow = true;
            SelectedAugmentSnapshotRevision = NextSnapshotRevision(SelectedAugmentSnapshotRevision);
            Debug.LogError($"[PlayerManager] Selected augment durable snapshot capacity exceeded. playerId={playerId}, augmentId={resolvedContentId}");
            return;
        }

        SelectedAugmentSnapshotIds.Set(uniqueCount, augmentId);
        SelectedAugmentSnapshotCounts.Set(uniqueCount, 1);
        SelectedAugmentSnapshotCount = uniqueCount + 1;
        SelectedAugmentSnapshotRevision = NextSnapshotRevision(SelectedAugmentSnapshotRevision);
    }

    public void PublishPresentedAugmentSnapshot(IEnumerable<string> augmentContentIds)
    {
        if (Object == null || !Object.IsValid || !Object.HasStateAuthority)
        {
            return;
        }

        var resolvedIds = new List<int>(PRESENTED_AUGMENT_SNAPSHOT_CAPACITY);
        var resolvedContentIds = new HashSet<string>(StringComparer.Ordinal);
        string failureReason = string.Empty;
        foreach (var rawContentId in augmentContentIds ?? System.Array.Empty<string>())
        {
            if (resolvedIds.Count >= PRESENTED_AUGMENT_SNAPSHOT_CAPACITY)
            {
                failureReason = $"presented_augment_snapshot_capacity_exceeded:{resolvedIds.Count + 1}/{PRESENTED_AUGMENT_SNAPSHOT_CAPACITY}";
                break;
            }

            if (!TryResolveAugmentContentIdReference(
                    rawContentId,
                    out string contentId,
                    out string resolveFailureReason))
            {
                failureReason = resolveFailureReason;
                break;
            }
            if (!resolvedContentIds.Add(contentId))
            {
                failureReason = $"presented_augment_duplicate:{contentId}";
                break;
            }

            int stableId = StableAugmentSnapshotId(contentId);
            if (stableId == 0 || resolvedIds.Contains(stableId))
            {
                failureReason = $"presented_augment_hash_invalid_or_colliding:{contentId}";
                break;
            }

            resolvedIds.Add(stableId);
        }

        for (int i = 0; i < PRESENTED_AUGMENT_SNAPSHOT_CAPACITY; i++)
        {
            PresentedAugmentSnapshotIds.Set(i, string.IsNullOrEmpty(failureReason) && i < resolvedIds.Count ? resolvedIds[i] : 0);
        }

        PresentedAugmentSnapshotCount = string.IsNullOrEmpty(failureReason) ? resolvedIds.Count : 0;
        PresentedAugmentSnapshotOverflow = !string.IsNullOrEmpty(failureReason);
        PresentedAugmentSnapshotRevision = NextSnapshotRevision(PresentedAugmentSnapshotRevision);
        if (!string.IsNullOrEmpty(failureReason))
        {
            Debug.LogError($"[PlayerManager] Presented augment durable snapshot rejected. playerId={playerId}, reason={failureReason}");
        }
    }

    public void ClearPresentedAugmentSnapshot()
    {
        PublishPresentedAugmentSnapshot(System.Array.Empty<string>());
    }

    private void InitializeEmptyAugmentSnapshotsOnFreshSpawn()
    {
        for (int i = 0; i < PRESENTED_AUGMENT_SNAPSHOT_CAPACITY; i++)
        {
            PresentedAugmentSnapshotIds.Set(i, 0);
        }
        for (int i = 0; i < SELECTED_AUGMENT_SNAPSHOT_CAPACITY; i++)
        {
            SelectedAugmentSnapshotIds.Set(i, 0);
            SelectedAugmentSnapshotCounts.Set(i, 0);
        }

        PresentedAugmentSnapshotCount = 0;
        PresentedAugmentSnapshotOverflow = false;
        PresentedAugmentSnapshotRevision = 1;
        SelectedAugmentSnapshotCount = 0;
        SelectedAugmentSnapshotOverflow = false;
        SelectedAugmentSnapshotRevision = 1;
    }

    public string[] GetPresentedAugmentSnapshotNames()
    {
        if (TryGetPresentedAugmentSnapshot(out string[] contentIds, out string failureReason))
        {
            return contentIds;
        }

        Debug.LogError($"[PlayerManager] Presented augment snapshot is invalid. P{playerId}: {failureReason}");
        return System.Array.Empty<string>();
    }

    public bool TryGetPresentedAugmentSnapshot(out string[] contentIds, out string failureReason)
    {
        contentIds = System.Array.Empty<string>();
        failureReason = string.Empty;
        if (Object == null || !Object.IsValid)
        {
            failureReason = "presented_augment_network_object_invalid";
            return false;
        }
        if (PresentedAugmentSnapshotRevision <= 0)
        {
            failureReason = $"presented_augment_snapshot_revision_invalid:{PresentedAugmentSnapshotRevision}";
            return false;
        }
        if (PresentedAugmentSnapshotOverflow)
        {
            failureReason = "presented_augment_snapshot_overflow";
            return false;
        }

        int count = PresentedAugmentSnapshotCount;
        if (count < 0 || count > PRESENTED_AUGMENT_SNAPSHOT_CAPACITY)
        {
            failureReason = $"presented_augment_count_out_of_range:{count}";
            return false;
        }

        var names = new string[count];
        var uniqueContentIds = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < count; i++)
        {
            int augmentId = PresentedAugmentSnapshotIds.Get(i);
            if (!TryResolveLoadedAugmentContentIdByStableId(
                    augmentId,
                    out string contentId,
                    out string resolveFailureReason))
            {
                failureReason = $"presented_augment_slot_{i}:{resolveFailureReason}";
                return false;
            }
            if (!uniqueContentIds.Add(contentId))
            {
                failureReason = $"presented_augment_duplicate:{contentId}";
                return false;
            }

            names[i] = contentId;
        }

        contentIds = names;
        return true;
    }

    public string[] GetSelectedAugmentSnapshotNames()
    {
        if (TryGetSelectedAugmentSnapshot(out string[] contentIds, out string failureReason))
        {
            return contentIds;
        }

        Debug.LogError($"[PlayerManager] Selected augment snapshot is invalid. P{playerId}: {failureReason}");
        return System.Array.Empty<string>();
    }

    public bool TryGetSelectedAugmentSnapshot(out string[] contentIds, out string failureReason)
    {
        contentIds = System.Array.Empty<string>();
        failureReason = string.Empty;
        if (Object == null || !Object.IsValid)
        {
            failureReason = "selected_augment_network_object_invalid";
            return false;
        }
        if (SelectedAugmentSnapshotRevision <= 0)
        {
            failureReason = $"selected_augment_snapshot_revision_invalid:{SelectedAugmentSnapshotRevision}";
            return false;
        }
        if (SelectedAugmentSnapshotOverflow)
        {
            failureReason = "selected_augment_snapshot_overflow";
            return false;
        }

        int count = SelectedAugmentSnapshotCount;
        if (count < 0 || count > SELECTED_AUGMENT_SNAPSHOT_CAPACITY)
        {
            failureReason = $"selected_augment_unique_count_out_of_range:{count}";
            return false;
        }

        var rows = new List<KeyValuePair<string, int>>(count);
        var uniqueContentIds = new HashSet<string>(StringComparer.Ordinal);
        long expandedCount = 0;
        for (int i = 0; i < count; i++)
        {
            int augmentId = SelectedAugmentSnapshotIds.Get(i);
            if (!TryResolveLoadedAugmentContentIdByStableId(
                    augmentId,
                    out string contentId,
                    out string resolveFailureReason))
            {
                failureReason = $"selected_augment_slot_{i}:{resolveFailureReason}";
                return false;
            }
            if (!uniqueContentIds.Add(contentId))
            {
                failureReason = $"selected_augment_duplicate_row:{contentId}";
                return false;
            }

            int repetitions = SelectedAugmentSnapshotCounts.Get(i);
            if (repetitions <= 0)
            {
                failureReason = $"selected_augment_slot_{i}:count_invalid:{repetitions}";
                return false;
            }

            expandedCount += repetitions;
            if (expandedCount > MAX_SELECTED_AUGMENT_MIGRATION_ENTRY_COUNT)
            {
                failureReason = $"selected_augment_expanded_count_out_of_range:{expandedCount}";
                return false;
            }

            rows.Add(new KeyValuePair<string, int>(contentId, repetitions));
        }

        var names = new List<string>((int)expandedCount);
        foreach (KeyValuePair<string, int> row in rows)
        {
            for (int repetition = 0; repetition < row.Value; repetition++)
            {
                names.Add(row.Key);
            }
        }

        contentIds = names.ToArray();
        return true;
    }

    public MigrationRestoreReport RestoreAugmentSnapshotsAfterHostMigration(
        bool hasPresentedAugmentSnapshot,
        string[] presentedAugmentNames,
        bool hasSelectedAugmentSnapshot,
        string[] selectedAugmentNames,
        string context)
    {
        string scope = $"augment_snapshots:P{playerId}";
        int captured = 2 + (presentedAugmentNames?.Length ?? 0) + (selectedAugmentNames?.Length ?? 0);
        if (Object == null || !Object.IsValid || !Object.HasStateAuthority)
        {
            return MigrationRestoreReport.FailedScope(scope, captured, "state_authority_required");
        }

        if (!TryNormalizePresentedAugmentMigrationSnapshot(
                hasPresentedAugmentSnapshot,
                presentedAugmentNames,
                out string[] resolvedPresentedContentIds,
                out string presentedFailureReason))
        {
            return MigrationRestoreReport.FailedScope(scope, captured, presentedFailureReason);
        }
        if (!TryNormalizeSelectedAugmentMigrationSnapshot(
                hasSelectedAugmentSnapshot,
                selectedAugmentNames,
                out int[] selectedOrder,
                out int[] selectedCounts,
                out string[] canonicalSelectedContentIds,
                out string selectedFailureReason))
        {
            return MigrationRestoreReport.FailedScope(scope, captured, selectedFailureReason);
        }

        for (int i = 0; i < PRESENTED_AUGMENT_SNAPSHOT_CAPACITY; i++)
        {
            PresentedAugmentSnapshotIds.Set(
                i,
                i < resolvedPresentedContentIds.Length
                    ? StableAugmentSnapshotId(resolvedPresentedContentIds[i])
                    : 0);
        }

        for (int i = 0; i < SELECTED_AUGMENT_SNAPSHOT_CAPACITY; i++)
        {
            SelectedAugmentSnapshotIds.Set(i, i < selectedOrder.Length ? selectedOrder[i] : 0);
            SelectedAugmentSnapshotCounts.Set(i, i < selectedCounts.Length ? selectedCounts[i] : 0);
        }

        PresentedAugmentSnapshotCount = resolvedPresentedContentIds.Length;
        PresentedAugmentSnapshotOverflow = false;
        PresentedAugmentSnapshotRevision = NextSnapshotRevision(PresentedAugmentSnapshotRevision);
        SelectedAugmentSnapshotCount = selectedOrder.Length;
        SelectedAugmentSnapshotOverflow = false;
        SelectedAugmentSnapshotRevision = NextSnapshotRevision(SelectedAugmentSnapshotRevision);

        if (!TryGetPresentedAugmentSnapshot(out string[] verifiedPresented, out string verifyPresentedFailure)
            || !verifiedPresented.SequenceEqual(resolvedPresentedContentIds, StringComparer.Ordinal))
        {
            return MigrationRestoreReport.FailedScope(
                scope,
                captured,
                string.IsNullOrWhiteSpace(verifyPresentedFailure)
                    ? "presented_augment_post_restore_mismatch"
                    : $"presented_augment_post_restore_invalid:{verifyPresentedFailure}");
        }
        if (!TryGetSelectedAugmentSnapshot(out string[] verifiedSelected, out string verifySelectedFailure)
            || !verifiedSelected.SequenceEqual(canonicalSelectedContentIds, StringComparer.Ordinal))
        {
            return MigrationRestoreReport.FailedScope(
                scope,
                captured,
                string.IsNullOrWhiteSpace(verifySelectedFailure)
                    ? "selected_augment_post_restore_mismatch"
                    : $"selected_augment_post_restore_invalid:{verifySelectedFailure}");
        }

        Debug.Log($"[PlayerManager] HostMigration augment snapshot restore complete ({context}) P{playerId} presented={resolvedPresentedContentIds.Length} selectedUnique={selectedOrder.Length} selectedTotal={canonicalSelectedContentIds.Length}");
        return new MigrationRestoreReport(scope, captured, captured, 0, 0);
    }

    public async UniTask<MigrationRestoreReport> RestorePresentedAugmentRuntimeAfterHostMigrationAsync(
        bool hasPresentedAugmentSnapshot,
        string[] presentedAugmentReferences,
        string context,
        NetworkRunner expectedRunner,
        CancellationToken cancellationToken)
    {
        string scope = $"presented_augment_runtime:P{playerId}";
        int captured = 1 + (presentedAugmentReferences?.Length ?? 0);
        if (!IsMigrationRuntimeRestoreContextValid(expectedRunner))
        {
            return MigrationRestoreReport.FailedScope(scope, captured, "runner_or_state_authority_invalid");
        }

        if (!TryNormalizePresentedAugmentMigrationSnapshot(
                hasPresentedAugmentSnapshot,
                presentedAugmentReferences,
                out string[] expectedContentIds,
                out string normalizeFailureReason))
        {
            return MigrationRestoreReport.FailedScope(scope, captured, normalizeFailureReason);
        }

        if (!TryGetPresentedAugmentSnapshot(
                out string[] networkContentIds,
                out string networkSnapshotFailure)
            || !networkContentIds.SequenceEqual(expectedContentIds, StringComparer.Ordinal))
        {
            string reason = string.IsNullOrWhiteSpace(networkSnapshotFailure)
                ? "presented_augment_network_snapshot_mismatch"
                : $"presented_augment_network_snapshot_invalid:{networkSnapshotFailure}";
            return MigrationRestoreReport.FailedScope(scope, captured, reason);
        }

        if (augmentManager == null)
        {
            return MigrationRestoreReport.FailedScope(scope, captured, "augment_manager_missing");
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool runtimeApplied = await augmentManager.TrySetPresentedAugmentsByContentIdsExactAsync(
                expectedContentIds,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!runtimeApplied)
            {
                return MigrationRestoreReport.FailedScope(
                    scope,
                    captured,
                    "presented_augment_runtime_apply_failed");
            }
        }
        catch (OperationCanceledException)
        {
            return MigrationRestoreReport.FailedScope(scope, captured, "presented_augment_runtime_restore_canceled");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, this);
            return MigrationRestoreReport.FailedScope(
                scope,
                captured,
                $"presented_augment_runtime_restore_exception:{exception.GetType().Name}");
        }

        if (!IsMigrationRuntimeRestoreContextValid(expectedRunner))
        {
            return MigrationRestoreReport.FailedScope(scope, captured, "runner_lifecycle_changed_after_await");
        }

        List<AugmentData> runtimeAugments = augmentManager.GetPresentedAugments();
        string[] runtimeContentIds = runtimeAugments == null
            ? System.Array.Empty<string>()
            : runtimeAugments
                .Select(augment => augment != null ? augment.ContentId : string.Empty)
                .ToArray();
        if (!runtimeContentIds.SequenceEqual(expectedContentIds, StringComparer.Ordinal))
        {
            return MigrationRestoreReport.FailedScope(
                scope,
                captured,
                $"presented_augment_runtime_post_restore_mismatch:expected={string.Join(",", expectedContentIds)},actual={string.Join(",", runtimeContentIds)}");
        }

        Debug.Log($"[PlayerManager] HostMigration presented augment runtime restore complete ({context}) P{playerId} count={runtimeContentIds.Length}");
        return new MigrationRestoreReport(scope, captured, captured, 0, 0);
    }

    private static bool TryNormalizePresentedAugmentMigrationSnapshot(
        bool hasSnapshot,
        string[] references,
        out string[] contentIds,
        out string failureReason)
    {
        contentIds = System.Array.Empty<string>();
        failureReason = string.Empty;
        if (!hasSnapshot)
        {
            failureReason = "presented_augment_snapshot_not_captured";
            return false;
        }
        if (references == null)
        {
            failureReason = "presented_augment_snapshot_null";
            return false;
        }
        if (references.Length > PRESENTED_AUGMENT_SNAPSHOT_CAPACITY)
        {
            failureReason = $"presented_augment_snapshot_truncated:{references.Length}/{PRESENTED_AUGMENT_SNAPSHOT_CAPACITY}";
            return false;
        }

        var resolved = new string[references.Length];
        var uniqueContentIds = new HashSet<string>(StringComparer.Ordinal);
        var stableIds = new Dictionary<int, string>();
        for (int i = 0; i < references.Length; i++)
        {
            if (!TryResolveAugmentContentIdReference(
                    references[i],
                    out string contentId,
                    out string resolveFailureReason))
            {
                failureReason = $"presented_augment_entry_{i}:{resolveFailureReason}";
                return false;
            }
            if (!uniqueContentIds.Add(contentId))
            {
                failureReason = $"presented_augment_duplicate:{contentId}";
                return false;
            }

            int stableId = StableAugmentSnapshotId(contentId);
            if (stableId == 0)
            {
                failureReason = $"presented_augment_id_zero:{contentId}";
                return false;
            }
            if (stableIds.TryGetValue(stableId, out string existingContentId)
                && !string.Equals(existingContentId, contentId, StringComparison.Ordinal))
            {
                failureReason = $"presented_augment_hash_collision:{existingContentId}:{contentId}";
                return false;
            }

            stableIds[stableId] = contentId;
            resolved[i] = contentId;
        }

        contentIds = resolved;
        return true;
    }

    private static bool TryNormalizeSelectedAugmentMigrationSnapshot(
        bool hasSnapshot,
        string[] references,
        out int[] selectedOrder,
        out int[] selectedCounts,
        out string[] canonicalContentIds,
        out string failureReason)
    {
        selectedOrder = System.Array.Empty<int>();
        selectedCounts = System.Array.Empty<int>();
        canonicalContentIds = System.Array.Empty<string>();
        failureReason = string.Empty;
        if (!hasSnapshot)
        {
            failureReason = "selected_augment_snapshot_not_captured";
            return false;
        }
        if (references == null)
        {
            failureReason = "selected_augment_snapshot_null";
            return false;
        }
        if (references.Length > MAX_SELECTED_AUGMENT_MIGRATION_ENTRY_COUNT)
        {
            failureReason = $"selected_augment_snapshot_expanded_count_out_of_range:{references.Length}";
            return false;
        }

        var countsByStableId = new Dictionary<int, int>();
        var contentIdByStableId = new Dictionary<int, string>();
        var order = new List<int>();
        for (int i = 0; i < references.Length; i++)
        {
            if (!TryResolveAugmentContentIdReference(
                    references[i],
                    out string contentId,
                    out string resolveFailureReason))
            {
                failureReason = $"selected_augment_entry_{i}:{resolveFailureReason}";
                return false;
            }

            int stableId = StableAugmentSnapshotId(contentId);
            if (stableId == 0)
            {
                failureReason = $"selected_augment_id_zero:{contentId}";
                return false;
            }
            if (contentIdByStableId.TryGetValue(stableId, out string existingContentId)
                && !string.Equals(existingContentId, contentId, StringComparison.Ordinal))
            {
                failureReason = $"selected_augment_hash_collision:{existingContentId}:{contentId}";
                return false;
            }
            if (!countsByStableId.ContainsKey(stableId))
            {
                if (order.Count >= SELECTED_AUGMENT_SNAPSHOT_CAPACITY)
                {
                    failureReason = $"selected_augment_snapshot_unique_overflow:{order.Count + 1}/{SELECTED_AUGMENT_SNAPSHOT_CAPACITY}";
                    return false;
                }

                countsByStableId[stableId] = 0;
                contentIdByStableId[stableId] = contentId;
                order.Add(stableId);
            }
            countsByStableId[stableId]++;
        }

        selectedOrder = order.ToArray();
        selectedCounts = order.Select(id => countsByStableId[id]).ToArray();
        var canonical = new List<string>(references.Length);
        foreach (int stableId in order)
        {
            for (int repetition = 0; repetition < countsByStableId[stableId]; repetition++)
            {
                canonical.Add(contentIdByStableId[stableId]);
            }
        }

        canonicalContentIds = canonical.ToArray();
        return true;
    }

    // Pending unit registrations received before FieldManager is ready
    private struct PendingUnitReg
    {
        public NetworkObject unitNO;
        public uint unitIdRaw;
        public int x;
        public int y;
        public int z;
        public string unitDataKey;
        public int unitDataKeyHash;
        public int starLevel;
        public int rosterRevision;
        public int registrationGeneration;
        public int lifecycleGeneration;
    }

    private readonly struct DesiredUnitRosterEntry
    {
        public DesiredUnitRosterEntry(
            uint unitIdRaw,
            int x,
            int y,
            int z,
            int unitDataKeyHash,
            int starLevel)
        {
            UnitIdRaw = unitIdRaw;
            X = x;
            Y = y;
            Z = z;
            UnitDataKeyHash = unitDataKeyHash;
            StarLevel = starLevel;
        }

        public uint UnitIdRaw { get; }
        public int X { get; }
        public int Y { get; }
        public int Z { get; }
        public int UnitDataKeyHash { get; }
        public int StarLevel { get; }
    }

    private enum UnitRosterRevisionAcceptance
    {
        Rejected,
        Duplicate,
        AcceptedNew
    }

    private List<PendingUnitReg> _pendingUnitRegs = new List<PendingUnitReg>();
    private readonly HashSet<uint> _retiredUnitRegistrationIds = new HashSet<uint>();
    private readonly Dictionary<uint, PendingUnitReg> _latestUnitRegistrationById = new Dictionary<uint, PendingUnitReg>();
    private readonly Dictionary<uint, DesiredUnitRosterEntry> _desiredUnitRosterById = new Dictionary<uint, DesiredUnitRosterEntry>();
    private readonly Dictionary<uint, SemaphoreSlim> _unitRegistrationApplyGates = new Dictionary<uint, SemaphoreSlim>();
    private int _latestAcceptedUnitRosterRevision = -1;
    private int _latestAcceptedUnitRosterFingerprint;
    private int _unitRosterApplyGeneration;
    private int _unitRegistrationGeneration;
    private int _unitRosterLifecycleGeneration;
    private int[] _pendingPermanentWallFlatPositions;
    private int _pendingPermanentWallLayoutRevision = -1;
    private int[] _pendingUnitRosterIdRaws;
    private int[] _pendingUnitRosterFlatPositions;
    private string[] _pendingUnitRosterDataKeys;
    private int[] _pendingUnitRosterDataKeyHashes;
    private int[] _pendingUnitRosterStarLevels;
    private int _pendingUnitRosterRevision = -1;
    private int _pendingUnitRosterFingerprint;
    private int _pendingUnitRosterApplyGeneration;
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
        ResetLocalUnitRosterSyncState();
        ResetShopSnapshotSyncLifetime();
        if (_assetOwner == null || _assetOwner.IsDisposed)
        {
            _assetOwner = new AddressableAssetOwner();
        }

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
            InitializeEmptyAugmentSnapshotsOnFreshSpawn();
        }

        InitializeKingRuntimeOnSpawn();
        InitializeDemonRuntimeOnSpawn();
        InitializeMapThemeOnSpawn();

        InitializePermanentWallStateOnSpawn(isHostMigration);

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
            if (propertyName == nameof(PermanentWallPlacementCount))
            {
                GameEvents.TriggerPlayerPermanentWallCountChanged(playerId, PermanentWallPlacementCount);
            }
            if (propertyName == nameof(PermanentAttackDamageBonusPermille) ||
                propertyName == nameof(PermanentAttackSpeedBonusPermille))
            {
                ApplyPermanentBonusesFromNetworkSnapshot();
            }
        }

        PublishBlackMagicChangedFromRenderIfNeeded();
        RenderKingRuntime();
        RenderMapTheme();
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
        BindMapThemePresenter(gridInstance);

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
                if (LoadManager.Instance != null && Runner != null && Runner.IsRunning)
                {
                    try
                    {
                        // All PlayerManager initializers on this peer share one runner-scoped task.
                        // Keep player actions closed until prefab Awake/Instantiate work is pooled.
                        int unitPoolTarget = LoadManager.Instance.ResolveUnitNetworkPoolTarget(Runner);
                        await LoadManager.Instance.PrewarmUnitNetworkPoolAsync(Runner, unitPoolTarget);
                    }
                    catch (System.Exception exception)
                    {
                        Debug.LogWarning(
                            $"[PlayerManager] Unit network pool prewarm skipped for P{id}: {exception.Message}");
                    }
                }
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

        // A complete authoritative roster owns membership. Apply it before any
        // per-entry key/object enrichment that arrived while the field was unavailable.
        await DrainPendingUnitRoster("Rpc_InitializePlayer");

        // Process any unit registrations that arrived early.
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

                await RPC_RegisterUnitAt_Internal(
                    p.unitNO,
                    p.x,
                    p.y,
                    p.unitDataKey,
                    p.starLevel,
                    p.rosterRevision,
                    p.registrationGeneration);
            }
        }

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

        BindMapThemePresenter(gridRoot);

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

        if (!IsKingRuntimeDataReady(out string kingReason))
        {
            QueueKingDataLoad(SelectedKingUnitKeyHash);
            reason = $"kingRuntimeNotReady({kingReason})";
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

        expectedPosition = gm.GetPlayerFieldPosition(playerId);
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
    public async void RPC_SyncShopItems(
        string[] unitDataNames,
        int[] starLevels,
        int snapshotRevision,
        int snapshotRound)
    {
        // 서버는 이미 상점 아이템을 가지고 있으므로 무시
        if (Object != null && Object.HasStateAuthority) return;

        if (shopManager != null)
        {
            try
            {
                CancellationToken cancellationToken =
                    _shopSnapshotSyncLifetimeCancellation?.Token ?? CancellationToken.None;
                bool applied = await shopManager.ApplySnapshotFromNetworkAtOrAfterRevisionAsync(
                    snapshotRevision,
                    snapshotRound,
                    $"RPC_SyncShopItems.P{playerId}.R{snapshotRevision}",
                    triggerRefreshedEvent: true,
                    cancellationToken: cancellationToken);
                if (!applied)
                {
                    Debug.LogError(
                        $"[PlayerManager] Shop RPC snapshot sync failed P{playerId}, revision={snapshotRevision}, round={snapshotRound}.");
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when this PlayerManager is despawned or replaced during migration.
            }
        }
    }

    /// <summary>
    /// 서버에서 생성한 증강체 목록을 모든 클라이언트에 동기화합니다.
    /// </summary>

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public async void RPC_SyncPresentedAugments(string[] augmentContentIds)
    {
        // 서버는 이미 증강체 목록을 가지고 있으므로 무시
        if (Object != null && Object.HasStateAuthority) return;

        if (augmentManager != null)
        {
            bool applied = await augmentManager.SetPresentedAugmentsByContentIdsAsync(augmentContentIds);
            if (!applied)
            {
                Debug.LogError($"[PlayerManager] Presented augment RPC sync rejected for P{playerId}.");
                return;
            }
            // Debug.Log($"<color=magenta>[RPC_SyncPresentedAugments] Player {playerId}: {augmentContentIds.Length} augments synchronized</color>");
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
        if (TryGetShopSnapshot(
                out string[] shopNames,
                out int[] shopStars,
                out _,
                out int shopRevision,
                out int shopRound))
        {
            RPC_SyncShopItems(shopNames, shopStars, shopRevision, shopRound);
        }
        else
        {
            Debug.LogError(
                $"[PlayerManager] RPC_RequestSyncData rejected shop sync for P{playerId}: authoritative snapshot unavailable.");
        }
        
        // 증강체 동기화
        if (augmentManager != null)
        {
            var augments = augmentManager.GetPresentedAugments();
            if (augments.Count > 0)
            {
                string[] augmentContentIds = augments.Select(a => a?.ContentId ?? "").ToArray();
                RPC_SyncPresentedAugments(augmentContentIds);
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
    public void RPC_ApplyPermanentWalls(int layoutRevision, int[] packedPositions)
    {
        packedPositions ??= System.Array.Empty<int>();

        if (fieldManager == null)
        {
            _pendingPermanentWallFlatPositions = packedPositions.ToArray();
            _pendingPermanentWallLayoutRevision = layoutRevision;
            RebindRuntimeReferencesAfterMigration("RPC_ApplyPermanentWalls.Pending", false);
        }

        if (fieldManager != null)
        {
            fieldManager.ApplyPermanentWallsFromServer(layoutRevision, packedPositions);
            _pendingPermanentWallFlatPositions = null;
            _pendingPermanentWallLayoutRevision = -1;
        }
    }

    private void DrainPendingPermanentWalls(string context)
    {
        if (_pendingPermanentWallFlatPositions == null || _pendingPermanentWallLayoutRevision < 0)
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
        int pendingRevision = _pendingPermanentWallLayoutRevision;
        _pendingPermanentWallFlatPositions = null;
        _pendingPermanentWallLayoutRevision = -1;
        fieldManager.ApplyPermanentWallsFromServer(pendingRevision, pending);
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
            RPC_ApplyPermanentWalls(PermanentWallLayoutRevision, flat ?? System.Array.Empty<int>());

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

            if (!IsValidUnitRosterPayload(unitIdRaws, flatPositions, unitDataKeys, null, starLevels))
            {
                return;
            }

            int[] unitDataKeyHashes = new int[unitIdRaws.Length];
            for (int i = 0; i < unitDataKeyHashes.Length; i++)
            {
                unitDataKeyHashes[i] = StableUnitDataIdentityHash(unitDataKeys[i]);
            }

            int fingerprint = ComputeUnitRosterFingerprint(
                unitIdRaws,
                flatPositions,
                unitDataKeyHashes,
                starLevels);
            await ReceiveUnitRosterSnapshotAsync(
                unitIdRaws,
                flatPositions,
                unitDataKeys,
                unitDataKeyHashes,
                starLevels,
                0,
                fingerprint,
                "RPC_ReconcileUnitRoster");
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

            bool fingerprintedRoster = flatRoster.Length >= 4 && flatRoster[0] == -4;
            bool revisionedRoster = flatRoster.Length >= 3 && flatRoster[0] == -3;
            bool versionedRoster = flatRoster.Length >= 2 && flatRoster[0] == -2;
            int count;
            int stride;
            int startOffset;
            bool hasUnitDataHashes;
            int rosterRevision;
            int advertisedFingerprint = 0;

            if (fingerprintedRoster)
            {
                rosterRevision = flatRoster[1];
                advertisedFingerprint = flatRoster[2];
                count = flatRoster[3];
                stride = 6;
                startOffset = 4;
                hasUnitDataHashes = true;
                if (rosterRevision < 0 || count < 0 || flatRoster.Length != startOffset + (count * stride))
                {
                    return;
                }
            }
            else if (revisionedRoster)
            {
                rosterRevision = flatRoster[1];
                count = flatRoster[2];
                stride = 6;
                startOffset = 3;
                hasUnitDataHashes = true;
                if (rosterRevision < 0 || count < 0 || flatRoster.Length != startOffset + (count * stride))
                {
                    return;
                }
            }
            else if (versionedRoster)
            {
                rosterRevision = 0;
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
                rosterRevision = 0;
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
            int[] unitDataKeyHashes = new int[count];

            for (int i = 0; i < count; i++)
            {
                int offset = startOffset + (i * stride);
                unitIdRaws[i] = flatRoster[offset + 0];
                flatPositions[(i * 3) + 0] = flatRoster[offset + 1];
                flatPositions[(i * 3) + 1] = flatRoster[offset + 2];
                flatPositions[(i * 3) + 2] = flatRoster[offset + 3];
                starLevels[i] = flatRoster[offset + 4];
                unitDataKeyHashes[i] = hasUnitDataHashes ? flatRoster[offset + 5] : 0;
            }

            int fingerprint = ComputeUnitRosterFingerprint(
                unitIdRaws,
                flatPositions,
                unitDataKeyHashes,
                starLevels);
            if (fingerprintedRoster && advertisedFingerprint != fingerprint)
            {
                return;
            }

            await ReceiveUnitRosterSnapshotAsync(
                unitIdRaws,
                flatPositions,
                null,
                unitDataKeyHashes,
                starLevels,
                rosterRevision,
                fingerprint,
                "RPC_ReconcileUnitRosterCompact");
        }
        catch (System.Exception)
        {
            // Compact roster correction is best-effort; per-unit register RPCs carry identity metadata.
        }
    }

    private async UniTask ReceiveUnitRosterSnapshotAsync(
        int[] unitIdRaws,
        int[] flatPositions,
        string[] unitDataKeys,
        int[] unitDataKeyHashes,
        int[] starLevels,
        int rosterRevision,
        int rosterFingerprint,
        string context)
    {
        if (!IsValidUnitRosterPayload(
                unitIdRaws,
                flatPositions,
                unitDataKeys,
                unitDataKeyHashes,
                starLevels))
        {
            return;
        }

        UnitRosterRevisionAcceptance acceptance = TryAcceptUnitRosterRevision(
            rosterRevision,
            rosterFingerprint,
            unitIdRaws,
            flatPositions,
            unitDataKeyHashes,
            starLevels,
            out int rosterApplyGeneration);
        if (acceptance != UnitRosterRevisionAcceptance.AcceptedNew)
        {
            return;
        }

        if (fieldManager == null || fieldManager.ground3D == null)
        {
            StorePendingUnitRoster(
                unitIdRaws,
                flatPositions,
                unitDataKeys,
                unitDataKeyHashes,
                starLevels,
                rosterRevision,
                rosterFingerprint,
                rosterApplyGeneration);
            RebindRuntimeReferencesAfterMigration($"{context}.Pending", false);
        }

        if (fieldManager == null || fieldManager.ground3D == null)
        {
            return;
        }

        if (_pendingUnitRosterIdRaws != null)
        {
            await DrainPendingUnitRoster(context);
            return;
        }

        await ApplyAcceptedUnitRosterFromAuthority(
            unitIdRaws,
            flatPositions,
            unitDataKeyHashes,
            starLevels,
            rosterRevision,
            rosterFingerprint,
            rosterApplyGeneration);
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
        var unitDataKeyHashes = _pendingUnitRosterDataKeyHashes;
        var starLevels = _pendingUnitRosterStarLevels;
        int rosterRevision = _pendingUnitRosterRevision;
        int rosterFingerprint = _pendingUnitRosterFingerprint;
        int rosterApplyGeneration = _pendingUnitRosterApplyGeneration;
        ClearPendingUnitRoster();

        await ApplyAcceptedUnitRosterFromAuthority(
            unitIdRaws,
            flatPositions,
            unitDataKeyHashes,
            starLevels,
            rosterRevision,
            rosterFingerprint,
            rosterApplyGeneration);
    }

    private bool StorePendingUnitRoster(
        int[] unitIdRaws,
        int[] flatPositions,
        string[] unitDataKeys,
        int[] unitDataKeyHashes,
        int[] starLevels,
        int rosterRevision,
        int rosterFingerprint,
        int rosterApplyGeneration)
    {
        if (!IsUnitRosterApplyCurrent(rosterRevision, rosterFingerprint, rosterApplyGeneration))
        {
            return false;
        }

        if (_pendingUnitRosterIdRaws != null)
        {
            if (rosterRevision < _pendingUnitRosterRevision ||
                (rosterRevision == _pendingUnitRosterRevision &&
                 rosterFingerprint != _pendingUnitRosterFingerprint))
            {
                return false;
            }

            if (rosterRevision == _pendingUnitRosterRevision)
            {
                return true;
            }
        }

        _pendingUnitRosterIdRaws = unitIdRaws != null ? unitIdRaws.ToArray() : null;
        _pendingUnitRosterFlatPositions = flatPositions != null ? flatPositions.ToArray() : null;
        _pendingUnitRosterDataKeys = unitDataKeys != null ? unitDataKeys.ToArray() : null;
        _pendingUnitRosterDataKeyHashes = unitDataKeyHashes != null ? unitDataKeyHashes.ToArray() : null;
        _pendingUnitRosterStarLevels = starLevels != null ? starLevels.ToArray() : null;
        _pendingUnitRosterRevision = rosterRevision;
        _pendingUnitRosterFingerprint = rosterFingerprint;
        _pendingUnitRosterApplyGeneration = rosterApplyGeneration;
        return true;
    }

    private void ClearPendingUnitRoster()
    {
        _pendingUnitRosterIdRaws = null;
        _pendingUnitRosterFlatPositions = null;
        _pendingUnitRosterDataKeys = null;
        _pendingUnitRosterDataKeyHashes = null;
        _pendingUnitRosterStarLevels = null;
        _pendingUnitRosterRevision = -1;
        _pendingUnitRosterFingerprint = 0;
        _pendingUnitRosterApplyGeneration = 0;
    }

    private bool IsValidUnitRosterPayload(
        int[] unitIdRaws,
        int[] flatPositions,
        string[] unitDataKeys,
        int[] unitDataKeyHashes,
        int[] starLevels)
    {
        if (unitIdRaws == null || flatPositions == null || starLevels == null)
        {
            return false;
        }

        int count = unitIdRaws.Length;
        return flatPositions.Length == count * 3
            && starLevels.Length == count
            && (unitDataKeys == null || unitDataKeys.Length == count)
            && (unitDataKeyHashes == null || unitDataKeyHashes.Length == count);
    }

    private async UniTask ApplyAcceptedUnitRosterFromAuthority(
        int[] unitIdRaws,
        int[] flatPositions,
        int[] unitDataKeyHashes,
        int[] starLevels,
        int rosterRevision,
        int rosterFingerprint,
        int rosterApplyGeneration)
    {
        if (!IsValidUnitRosterPayload(unitIdRaws, flatPositions, null, unitDataKeyHashes, starLevels) ||
            fieldManager == null ||
            !IsUnitRosterApplyCurrent(rosterRevision, rosterFingerprint, rosterApplyGeneration))
        {
            return;
        }

        var authoritativePositions = BuildUnitRosterPositionMap(unitIdRaws, flatPositions);
        fieldManager.ReconcileUnitsToAuthoritativeRoster(authoritativePositions);

        for (int i = 0; i < unitIdRaws.Length; i++)
        {
            if (!IsUnitRosterApplyCurrent(rosterRevision, rosterFingerprint, rosterApplyGeneration))
            {
                return;
            }

            uint unitIdRaw = unchecked((uint)unitIdRaws[i]);
            if (!_latestUnitRegistrationById.TryGetValue(unitIdRaw, out PendingUnitReg registration) ||
                !IsUnitRegistrationCurrent(registration))
            {
                continue;
            }

            string unitDataKey = registration.unitDataKey;
            if (string.IsNullOrEmpty(unitDataKey) && registration.unitDataKeyHash != 0)
            {
                unitDataKey = await ResolveUnitDataKeyByStableHashAsync(registration.unitDataKeyHash);
                if (!IsUnitRosterApplyCurrent(rosterRevision, rosterFingerprint, rosterApplyGeneration) ||
                    !IsUnitRegistrationCurrent(registration))
                {
                    continue;
                }

                if (!TryUpdateLatestUnitRegistration(registration, null, unitDataKey, out registration))
                {
                    continue;
                }
            }

            NetworkObject unitNO = await ResolveNetworkObjectByRawIdAsync(unitIdRaw, registration);
            if (!IsUnitRosterApplyCurrent(rosterRevision, rosterFingerprint, rosterApplyGeneration) ||
                !IsUnitRegistrationCurrent(registration))
            {
                continue;
            }

            if (unitNO == null &&
                _latestUnitRegistrationById.TryGetValue(unitIdRaw, out PendingUnitReg metadataForObject) &&
                metadataForObject.registrationGeneration == registration.registrationGeneration &&
                metadataForObject.unitNO != null &&
                metadataForObject.unitNO.IsValid &&
                metadataForObject.unitNO.Id.Raw == unitIdRaw)
            {
                unitNO = metadataForObject.unitNO;
            }

            if (unitNO == null ||
                !TryUpdateLatestUnitRegistration(registration, unitNO, unitDataKey, out registration))
            {
                continue;
            }

            await RPC_RegisterUnitAt_Internal(
                unitNO,
                registration.x,
                registration.y,
                registration.unitDataKey,
                registration.starLevel,
                registration.rosterRevision,
                registration.registrationGeneration);
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

        return TryResolveLoadedUnitDataKeyByStableHash(
                unitDataKeyHash,
                out string unitDataKey,
                out _)
            ? unitDataKey
            : string.Empty;
    }

    private static int StableUnitDataKeyHash(string value)
    {
        return StableDataKeyHash(NormalizeShopUnitKey(value));
    }

    private static int StableUnitDataIdentityHash(string value)
    {
        string normalizedKey = NormalizeShopUnitKey(value);
        if (string.IsNullOrEmpty(normalizedKey))
        {
            return 0;
        }

        var matches = new HashSet<UnitData>();
        void AppendMatches(IEnumerable<UnitData> units)
        {
            if (units == null)
            {
                return;
            }

            foreach (UnitData data in units)
            {
                if (data != null
                    && (string.Equals(NormalizeShopUnitKey(data.name), normalizedKey, StringComparison.Ordinal)
                        || string.Equals(NormalizeShopUnitKey(data.unitName), normalizedKey, StringComparison.Ordinal)))
                {
                    matches.Add(data);
                }
            }
        }

        LoadManager loadManager = LoadManager.Instance;
        AppendMatches(loadManager != null ? loadManager.GetAllUnitData() : null);
        AppendMatches(Resources.FindObjectsOfTypeAll<UnitData>());
        if (matches.Count == 1)
        {
            UnitData data = matches.First();
            if (data.ContentIdHash != 0)
            {
                return data.ContentIdHash;
            }
        }

        return StableUnitDataKeyHash(normalizedKey);
    }

    public static int ComputeUnitRosterFingerprint(
        int[] unitIdRaws,
        int[] flatPositions,
        int[] unitDataKeyHashes,
        int[] starLevels)
    {
        if (unitIdRaws == null || flatPositions == null || unitDataKeyHashes == null || starLevels == null ||
            flatPositions.Length != unitIdRaws.Length * 3 ||
            unitDataKeyHashes.Length != unitIdRaws.Length ||
            starLevels.Length != unitIdRaws.Length)
        {
            return 0;
        }

        unchecked
        {
            uint hash = 2166136261u;
            AppendUnitRosterFingerprintValue(ref hash, unitIdRaws.Length);
            for (int i = 0; i < unitIdRaws.Length; i++)
            {
                AppendUnitRosterFingerprintValue(ref hash, unitIdRaws[i]);
                AppendUnitRosterFingerprintValue(ref hash, flatPositions[(i * 3) + 0]);
                AppendUnitRosterFingerprintValue(ref hash, flatPositions[(i * 3) + 1]);
                AppendUnitRosterFingerprintValue(ref hash, flatPositions[(i * 3) + 2]);
                AppendUnitRosterFingerprintValue(ref hash, starLevels[i]);
                AppendUnitRosterFingerprintValue(ref hash, unitDataKeyHashes[i]);
            }
            return unchecked((int)hash);
        }
    }

    private static void AppendUnitRosterFingerprintValue(ref uint hash, int value)
    {
        unchecked
        {
            uint bits = unchecked((uint)value);
            for (int shift = 0; shift < 32; shift += 8)
            {
                hash ^= (byte)(bits >> shift);
                hash *= 16777619u;
            }
        }
    }

    private UnitRosterRevisionAcceptance TryAcceptUnitRosterRevision(
        int rosterRevision,
        int rosterFingerprint,
        int[] unitIdRaws,
        int[] flatPositions,
        int[] unitDataKeyHashes,
        int[] starLevels,
        out int applyGeneration)
    {
        rosterRevision = Mathf.Max(0, rosterRevision);
        applyGeneration = _unitRosterApplyGeneration;
        if (rosterRevision < _latestAcceptedUnitRosterRevision)
        {
            return UnitRosterRevisionAcceptance.Rejected;
        }

        if (rosterRevision == _latestAcceptedUnitRosterRevision)
        {
            return rosterFingerprint == _latestAcceptedUnitRosterFingerprint
                ? UnitRosterRevisionAcceptance.Duplicate
                : UnitRosterRevisionAcceptance.Rejected;
        }

        var desired = new Dictionary<uint, DesiredUnitRosterEntry>(unitIdRaws.Length);
        for (int i = 0; i < unitIdRaws.Length; i++)
        {
            uint unitIdRaw = unchecked((uint)unitIdRaws[i]);
            var entry = new DesiredUnitRosterEntry(
                unitIdRaw,
                flatPositions[(i * 3) + 0],
                flatPositions[(i * 3) + 1],
                flatPositions[(i * 3) + 2],
                unitDataKeyHashes[i],
                starLevels[i]);
            if (desired.ContainsKey(unitIdRaw))
            {
                return UnitRosterRevisionAcceptance.Rejected;
            }
            desired.Add(unitIdRaw, entry);
        }

        uint[] previouslyKnownIds = _latestUnitRegistrationById.Keys.ToArray();

        // Invalidate every continuation before publishing the replacement desired map.
        _unitRosterApplyGeneration = NextPositiveGeneration(_unitRosterApplyGeneration);
        _unitRegistrationGeneration = NextPositiveGeneration(_unitRegistrationGeneration);
        _latestAcceptedUnitRosterRevision = rosterRevision;
        _latestAcceptedUnitRosterFingerprint = rosterFingerprint;
        _desiredUnitRosterById.Clear();
        _latestUnitRegistrationById.Clear();
        _pendingUnitRegs.Clear();
        ClearPendingUnitRoster();

        foreach (var pair in desired)
        {
            DesiredUnitRosterEntry entry = pair.Value;
            _desiredUnitRosterById.Add(pair.Key, entry);
            _retiredUnitRegistrationIds.Remove(pair.Key);
            _unitRegistrationGeneration = NextPositiveGeneration(_unitRegistrationGeneration);
            _latestUnitRegistrationById.Add(pair.Key, new PendingUnitReg
            {
                unitNO = null,
                unitIdRaw = pair.Key,
                x = entry.X,
                y = entry.Y,
                z = entry.Z,
                unitDataKey = string.Empty,
                unitDataKeyHash = entry.UnitDataKeyHash,
                starLevel = entry.StarLevel,
                rosterRevision = rosterRevision,
                registrationGeneration = _unitRegistrationGeneration,
                lifecycleGeneration = _unitRosterLifecycleGeneration
            });
        }

        foreach (uint previouslyKnownId in previouslyKnownIds)
        {
            if (!desired.ContainsKey(previouslyKnownId))
            {
                _retiredUnitRegistrationIds.Add(previouslyKnownId);
            }
        }

        applyGeneration = _unitRosterApplyGeneration;
        return UnitRosterRevisionAcceptance.AcceptedNew;
    }

    private bool IsUnitRosterApplyCurrent(int rosterRevision, int rosterFingerprint, int applyGeneration)
    {
        return Mathf.Max(0, rosterRevision) == _latestAcceptedUnitRosterRevision &&
               rosterFingerprint == _latestAcceptedUnitRosterFingerprint &&
               applyGeneration == _unitRosterApplyGeneration;
    }

    private bool RememberLatestUnitRegistration(
        uint unitIdRaw,
        NetworkObject unitNO,
        int x,
        int y,
        string unitDataKey,
        int starLevel,
        int rosterRevision,
        out PendingUnitReg registration)
    {
        registration = default;
        rosterRevision = Mathf.Max(0, rosterRevision);
        string normalizedUnitDataKey = unitDataKey ?? string.Empty;
        int unitDataKeyHash = StableUnitDataIdentityHash(normalizedUnitDataKey);
        if (rosterRevision != _latestAcceptedUnitRosterRevision ||
            !_desiredUnitRosterById.TryGetValue(unitIdRaw, out DesiredUnitRosterEntry desired) ||
            desired.X != x ||
            desired.Y != y ||
            desired.Z != 0 ||
            desired.StarLevel != starLevel ||
            desired.UnitDataKeyHash != unitDataKeyHash ||
            (unitNO != null && (!unitNO.IsValid || unitNO.Id.Raw != unitIdRaw)))
        {
            return false;
        }

        if (!_latestUnitRegistrationById.TryGetValue(unitIdRaw, out registration) ||
            registration.rosterRevision != rosterRevision ||
            registration.lifecycleGeneration != _unitRosterLifecycleGeneration)
        {
            _unitRegistrationGeneration = NextPositiveGeneration(_unitRegistrationGeneration);
            registration = new PendingUnitReg
            {
                unitNO = unitNO,
                unitIdRaw = unitIdRaw,
                x = desired.X,
                y = desired.Y,
                z = desired.Z,
                unitDataKey = normalizedUnitDataKey,
                unitDataKeyHash = desired.UnitDataKeyHash,
                starLevel = desired.StarLevel,
                rosterRevision = rosterRevision,
                registrationGeneration = _unitRegistrationGeneration,
                lifecycleGeneration = _unitRosterLifecycleGeneration
            };
        }
        else
        {
            if (unitNO != null)
            {
                registration.unitNO = unitNO;
            }
            registration.unitDataKey = normalizedUnitDataKey;
        }

        _retiredUnitRegistrationIds.Remove(unitIdRaw);
        _latestUnitRegistrationById[unitIdRaw] = registration;
        return true;
    }

    private bool IsUnitRegistrationCurrent(PendingUnitReg registration)
    {
        return !_retiredUnitRegistrationIds.Contains(registration.unitIdRaw) &&
               registration.lifecycleGeneration == _unitRosterLifecycleGeneration &&
               Mathf.Max(0, registration.rosterRevision) == _latestAcceptedUnitRosterRevision &&
               _desiredUnitRosterById.TryGetValue(registration.unitIdRaw, out DesiredUnitRosterEntry desired) &&
               desired.X == registration.x &&
               desired.Y == registration.y &&
               desired.Z == registration.z &&
               desired.StarLevel == registration.starLevel &&
               desired.UnitDataKeyHash == registration.unitDataKeyHash &&
               _latestUnitRegistrationById.TryGetValue(registration.unitIdRaw, out PendingUnitReg latest) &&
               latest.rosterRevision == registration.rosterRevision &&
               latest.registrationGeneration == registration.registrationGeneration &&
               latest.lifecycleGeneration == registration.lifecycleGeneration;
    }

    private bool TryUpdateLatestUnitRegistration(
        PendingUnitReg expected,
        NetworkObject unitNO,
        string unitDataKey,
        out PendingUnitReg updated)
    {
        updated = expected;
        if (!IsUnitRegistrationCurrent(expected) ||
            !_latestUnitRegistrationById.TryGetValue(expected.unitIdRaw, out PendingUnitReg latest))
        {
            return false;
        }

        updated = latest;

        if (unitNO != null)
        {
            if (!unitNO.IsValid || unitNO.Id.Raw != expected.unitIdRaw)
            {
                return false;
            }
            updated.unitNO = unitNO;
        }
        if (!string.IsNullOrEmpty(unitDataKey))
        {
            if (StableUnitDataIdentityHash(unitDataKey) != expected.unitDataKeyHash)
            {
                return false;
            }
            updated.unitDataKey = unitDataKey;
        }

        _latestUnitRegistrationById[expected.unitIdRaw] = updated;
        return true;
    }

    private bool CanApplyUnitUnregister(uint unitIdRaw, int rosterRevision)
    {
        return Mathf.Max(0, rosterRevision) == _latestAcceptedUnitRosterRevision &&
               !_desiredUnitRosterById.ContainsKey(unitIdRaw);
    }

    private void ResetLocalUnitRosterSyncState()
    {
        _unitRosterLifecycleGeneration = NextPositiveGeneration(_unitRosterLifecycleGeneration);
        _unitRosterApplyGeneration = NextPositiveGeneration(_unitRosterApplyGeneration);
        _unitRegistrationGeneration = NextPositiveGeneration(_unitRegistrationGeneration);
        _latestAcceptedUnitRosterRevision = -1;
        _latestAcceptedUnitRosterFingerprint = 0;
        _desiredUnitRosterById.Clear();
        _latestUnitRegistrationById.Clear();
        // Per-unit gates intentionally survive lifecycle resets. An Initialize from the
        // previous lifecycle may still be unwinding, and a reused raw id must serialize
        // behind it before the new lifecycle applies its state.
        _retiredUnitRegistrationIds.Clear();
        _pendingUnitRegs.Clear();
        ClearPendingUnitRoster();
    }

    private static int NextPositiveGeneration(int current)
    {
        return current == int.MaxValue ? 1 : current + 1;
    }

    private SemaphoreSlim GetUnitRegistrationApplyGate(uint unitIdRaw)
    {
        if (!_unitRegistrationApplyGates.TryGetValue(unitIdRaw, out SemaphoreSlim gate))
        {
            gate = new SemaphoreSlim(1, 1);
            _unitRegistrationApplyGates.Add(unitIdRaw, gate);
        }

        return gate;
    }

    public int AdvanceUnitRosterRevisionForAuthority()
    {
        if (Object == null || !Object.IsValid || !Object.HasStateAuthority)
        {
            return Mathf.Max(0, UnitRosterRevision);
        }

        UnitRosterRevision = NextPositiveGeneration(UnitRosterRevision);
        return UnitRosterRevision;
    }

    private async UniTask<NetworkObject> ResolveNetworkObjectByRawIdAsync(
        uint unitIdRaw,
        PendingUnitReg registration)
    {
        for (int attempt = 0; attempt < 300; attempt++)
        {
            if (!IsUnitRegistrationCurrent(registration))
            {
                return null;
            }

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
    public void RPC_UnregisterUnitAt(
        NetworkId unitId,
        int x,
        int y,
        string unitDataKey,
        int starLevel,
        int rosterRevision)
    {
        uint unitIdRaw = unitId.Raw;
        if (!CanApplyUnitUnregister(unitIdRaw, rosterRevision))
        {
            return;
        }

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
    public async void RPC_RegisterUnitAt(
        NetworkId unitId,
        int x,
        int y,
        string unitDataKey,
        int starLevel,
        int rosterRevision)
    {
        try
        {
            // Debug.Log($"<color=yellow>[RPC_RegisterUnitAt] recv pos=({x},{y}) key='{unitDataKey}' star={starLevel} stateAuth={(Object != null && Object.HasStateAuthority)} id={unitId}</color>");
            if (Object != null && Object.HasStateAuthority) return;
            uint unitIdRaw = unitId.Raw;
            if (!RememberLatestUnitRegistration(
                    unitIdRaw,
                    null,
                    x,
                    y,
                    unitDataKey,
                    starLevel,
                    rosterRevision,
                    out PendingUnitReg registration))
            {
                return;
            }

            NetworkObject unitNO = null;
            bool resolved = false;
            int attempts = 0;
            do
            {
                if (!IsUnitRegistrationCurrent(registration))
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
            if (!resolved || unitNO == null || !IsUnitRegistrationCurrent(registration))
            {
                // Debug.LogWarning($"<color=yellow>[RPC_RegisterUnitAt] failed to resolve NetworkObject by NetworkId='{unitId}' key='{unitDataKey}'</color>");
                return;
            }
            if (!TryUpdateLatestUnitRegistration(registration, unitNO, unitDataKey, out registration))
            {
                return;
            }

            if (fieldManager == null || fieldManager.ground3D == null)
            {
                // Debug.Log($"<color=yellow>[RPC_RegisterUnitAt] queued. fieldManagerReady={(fieldManager != null)} groundReady={(fieldManager != null && fieldManager.ground3D != null)}</color>");
                _pendingUnitRegs.RemoveAll(reg => reg.unitIdRaw == unitIdRaw);
                _pendingUnitRegs.Add(registration);
                return;
            }

            await RPC_RegisterUnitAt_Internal(
                unitNO,
                registration.x,
                registration.y,
                registration.unitDataKey,
                registration.starLevel,
                registration.rosterRevision,
                registration.registrationGeneration);
            // Debug.Log($"<color=yellow>[RPC_RegisterUnitAt] dispatched to Internal for pos=({x},{y})</color>");
        }
        catch (System.Exception)
        {
            // Debug.LogError($"[RPC_RegisterUnitAt] exception: {ex.Message}");
        }
    }

    private async Cysharp.Threading.Tasks.UniTask RPC_RegisterUnitAt_Internal(
        NetworkObject unitNO,
        int x,
        int y,
        string unitDataKey,
        int starLevel,
        int rosterRevision,
        int registrationGeneration)
    {
        SemaphoreSlim applyGate = null;
        bool gateEntered = false;
        try
        {
            uint unitIdRaw = unitNO != null ? unitNO.Id.Raw : 0;
            if (unitNO == null ||
                !_latestUnitRegistrationById.TryGetValue(unitIdRaw, out PendingUnitReg registration) ||
                registration.rosterRevision != Mathf.Max(0, rosterRevision) ||
                registration.registrationGeneration != registrationGeneration ||
                !IsUnitRegistrationCurrent(registration))
            {
                return;
            }

            applyGate = GetUnitRegistrationApplyGate(unitIdRaw);
            await applyGate.WaitAsync();
            gateEntered = true;

            // A newer full roster can arrive while this call waits behind an older
            // Initialize. Re-read the token after acquiring the per-unit gate so the
            // newest accepted registration is always the final writer.
            if (!_latestUnitRegistrationById.TryGetValue(unitIdRaw, out registration) ||
                registration.rosterRevision != Mathf.Max(0, rosterRevision) ||
                registration.registrationGeneration != registrationGeneration ||
                !IsUnitRegistrationCurrent(registration))
            {
                return;
            }

            unitNO = registration.unitNO != null ? registration.unitNO : unitNO;
            if (unitNO == null || !unitNO.IsValid || unitNO.Id.Raw != unitIdRaw)
            {
                return;
            }

            x = registration.x;
            y = registration.y;
            unitDataKey = registration.unitDataKey;
            starLevel = registration.starLevel;

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

            fieldManager.AttachStatusBar(unit.gameObject, unit.SetStatusBar);

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
                        await Cysharp.Threading.Tasks.UniTask.WaitUntil(() =>
                            LoadManager.Instance != null || !IsUnitRegistrationCurrent(registration));
                        if (!IsUnitRegistrationCurrent(registration))
                        {
                            return;
                        }
                    }
                    var lmReady = LoadManager.Instance.IsReady;
                    if (!lmReady)
                    {
                        // Debug.Log($"<color=yellow>[RPC_Internal] waiting LoadManager ready...</color>");
                        await Cysharp.Threading.Tasks.UniTask.WaitUntil(() =>
                            !IsUnitRegistrationCurrent(registration) ||
                            (LoadManager.Instance != null && LoadManager.Instance.IsReady));
                        if (!IsUnitRegistrationCurrent(registration))
                        {
                            return;
                        }
                    }
                    data = LoadManager.Instance.GetUnitData(unitDataKey);
                    if (data == null)
                    {
                        // Debug.Log($"<color=yellow>[RPC_Internal] LoadManager miss for key='{unitDataKey}'. Trying Addressables fallback...</color>");
                        data = await AssetLoader.LoadAssetAsync<UnitData>(unitDataKey, _assetOwner);
                        if (!IsUnitRegistrationCurrent(registration))
                        {
                            return;
                        }
                    }
                }
                if (data != null && IsUnitRegistrationCurrent(registration))
                {
                    await unit.Initialize(data, starLevel, this);
                    if (!IsUnitRegistrationCurrent(registration))
                    {
                        return;
                    }
                    // Debug.Log($"<color=yellow>[RPC_Internal] unit.Initialize OK data='{unit.Data?.name}' star={starLevel}</color>");
                }
                else
                {
                    // Debug.LogError($"[Player {playerId}] RPC_RegisterUnitAt could not resolve UnitData for key '{unitDataKey}'.");
                }
            }

            if (unit.Data == null)
            {
                // Debug.LogWarning($"<color=yellow>[RPC_Internal] unit.Data still null after resolve. Skip Register. key='{unitDataKey}', pos={pos}</color>");
                return;
            }
            if (!IsUnitRegistrationCurrent(registration))
            {
                return;
            }
            fieldManager.RegisterUnitAt(unit, pos);
            // Debug.Log($"<color=#3399FF>[ClientFlow] RegisterUnitAt via RPC -> {pos} (Player {playerId}) data='{unit.Data?.name}'</color>");
        }
        catch (System.Exception)
        {
            // Debug.LogError($"[RPC_Internal] exception: {ex.Message}");
        }
        finally
        {
            if (gateEntered)
            {
                applyGate.Release();
            }
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

    public MigrationRestoreReport RestoreDurableStateAfterHostMigration(
        int restoredPlayerId,
        int restoredHealth,
        int restoredGold,
        int restoredWallCount,
        bool hasShopSnapshot,
        string[] shopUnitKeys,
        int[] shopStarLevels,
        bool[] shopSoldFlags,
        int shopRevision,
        int shopRound,
        string context)
    {
        string scope = $"durable_state:P{restoredPlayerId}";
        int captured = 5 + (shopUnitKeys?.Length ?? 0);
        if (Object == null || !Object.IsValid || !Object.HasStateAuthority)
        {
            return MigrationRestoreReport.FailedScope(scope, captured, "state_authority_required");
        }

        if (!TryValidateDurablePlayerMigrationPayload(
                restoredPlayerId,
                restoredHealth,
                restoredGold,
                restoredWallCount,
                hasShopSnapshot,
                shopUnitKeys,
                shopStarLevels,
                shopSoldFlags,
                shopRevision,
                shopRound,
                out string[] resolvedShopUnitKeys,
                out string failureReason))
        {
            return MigrationRestoreReport.FailedScope(scope, captured, failureReason);
        }

        playerId = restoredPlayerId;
        health = restoredHealth;
        gold = restoredGold;
        wallCount = restoredWallCount;

        for (int i = 0; i < SHOP_SNAPSHOT_CAPACITY; i++)
        {
            ShopSnapshotSlots.Set(i, default);
        }

        for (int i = 0; i < resolvedShopUnitKeys.Length; i++)
        {
            ShopSnapshotSlots.Set(i, new ShopSnapshotSlot
            {
                UnitKeyHash = StableUnitDataIdentityHash(resolvedShopUnitKeys[i]),
                PackedMeta = PackShopSnapshotMeta(shopStarLevels[i], shopSoldFlags[i] ? 1 : 0)
            });
        }

        ShopSnapshotCount = resolvedShopUnitKeys.Length;
        ShopSnapshotRound = shopRound;
        ShopSnapshotRevision = shopRevision;
        ShopSnapshotOverflow = false;

        bool shopVerified = TryGetShopSnapshot(
            out string[] verifiedShopUnitKeys,
            out int[] verifiedShopStarLevels,
            out bool[] verifiedShopSoldFlags,
            out int verifiedShopRevision,
            out int verifiedShopRound,
            out string shopVerifyFailure);
        if (playerId != restoredPlayerId
            || health != restoredHealth
            || gold != restoredGold
            || wallCount != restoredWallCount
            || !shopVerified
            || verifiedShopRevision != shopRevision
            || verifiedShopRound != shopRound
            || !verifiedShopUnitKeys.SequenceEqual(resolvedShopUnitKeys, StringComparer.Ordinal)
            || !verifiedShopStarLevels.SequenceEqual(shopStarLevels)
            || !verifiedShopSoldFlags.SequenceEqual(shopSoldFlags))
        {
            string verifyReason = string.IsNullOrWhiteSpace(shopVerifyFailure)
                ? "durable_state_post_restore_mismatch"
                : $"shop_post_restore_invalid:{shopVerifyFailure}";
            return MigrationRestoreReport.FailedScope(scope, captured, verifyReason);
        }

        _runtimeInitialized = true;
        GameEvents.TriggerPlayerStatsChanged(playerId, health, gold);
        GameEvents.TriggerPlayerWallCountChanged(playerId, wallCount);
        Debug.Log($"[PlayerManager] HostMigration durable restore complete ({context}) P{playerId} hp={health} gold={gold} walls={wallCount} shopRev={ShopSnapshotRevision} shopCount={ShopSnapshotCount}");
        return new MigrationRestoreReport(scope, captured, captured, 0, 0);
    }

    public async UniTask<MigrationRestoreReport> RestoreShopRuntimeAfterHostMigrationAsync(
        bool hasShopSnapshot,
        string[] expectedShopUnitKeys,
        int[] expectedShopStarLevels,
        bool[] expectedShopSoldFlags,
        int expectedShopRevision,
        int expectedShopRound,
        string context,
        NetworkRunner expectedRunner,
        CancellationToken cancellationToken)
    {
        string scope = $"shop_runtime:P{playerId}";
        int captured = 1 + (expectedShopUnitKeys?.Length ?? 0);
        if (!IsMigrationRuntimeRestoreContextValid(expectedRunner))
        {
            return MigrationRestoreReport.FailedScope(scope, captured, "runner_or_state_authority_invalid");
        }
        if (!hasShopSnapshot)
        {
            return MigrationRestoreReport.FailedScope(scope, captured, "shop_snapshot_not_captured");
        }
        if (expectedShopUnitKeys == null || expectedShopStarLevels == null || expectedShopSoldFlags == null
            || expectedShopUnitKeys.Length != expectedShopStarLevels.Length
            || expectedShopUnitKeys.Length != expectedShopSoldFlags.Length)
        {
            return MigrationRestoreReport.FailedScope(scope, captured, "shop_runtime_expected_shape_invalid");
        }
        if (shopManager == null)
        {
            return MigrationRestoreReport.FailedScope(scope, captured, "shop_manager_missing");
        }

        bool networkSnapshotValid = TryGetShopSnapshot(
            out string[] networkUnitKeys,
            out int[] networkStarLevels,
            out bool[] networkSoldFlags,
            out int networkRevision,
            out int networkRound,
            out string networkFailureReason);
        if (!networkSnapshotValid
            || networkRevision != expectedShopRevision
            || networkRound != expectedShopRound
            || !networkUnitKeys.SequenceEqual(expectedShopUnitKeys, StringComparer.Ordinal)
            || !networkStarLevels.SequenceEqual(expectedShopStarLevels)
            || !networkSoldFlags.SequenceEqual(expectedShopSoldFlags))
        {
            string reason = string.IsNullOrWhiteSpace(networkFailureReason)
                ? "shop_network_snapshot_mismatch"
                : $"shop_network_snapshot_invalid:{networkFailureReason}";
            return MigrationRestoreReport.FailedScope(scope, captured, reason);
        }

        bool applied;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            applied = await shopManager.ApplySnapshotFromNetworkAsync(
                $"HostMigration.{context}",
                triggerRefreshedEvent: false,
                cancellationToken: cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException)
        {
            return MigrationRestoreReport.FailedScope(scope, captured, "shop_runtime_restore_canceled");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, this);
            return MigrationRestoreReport.FailedScope(
                scope,
                captured,
                $"shop_runtime_restore_exception:{exception.GetType().Name}");
        }

        if (!IsMigrationRuntimeRestoreContextValid(expectedRunner))
        {
            return MigrationRestoreReport.FailedScope(scope, captured, "runner_lifecycle_changed_after_await");
        }
        if (!applied && expectedShopUnitKeys.Length > 0)
        {
            return MigrationRestoreReport.FailedScope(scope, captured, "shop_runtime_apply_failed");
        }

        List<ShopItem> runtimeItems = shopManager.GetCurrentShopItems();
        if (runtimeItems == null || runtimeItems.Count != expectedShopUnitKeys.Length)
        {
            return MigrationRestoreReport.FailedScope(
                scope,
                captured,
                $"shop_runtime_count_mismatch:{runtimeItems?.Count ?? -1}/{expectedShopUnitKeys.Length}");
        }

        for (int i = 0; i < expectedShopUnitKeys.Length; i++)
        {
            ShopItem runtimeItem = runtimeItems[i];
            string runtimeKey = NormalizeShopUnitKey(runtimeItem.UnitData != null ? runtimeItem.UnitData.name : string.Empty);
            if (!string.Equals(runtimeKey, expectedShopUnitKeys[i], StringComparison.Ordinal)
                || runtimeItem.StarLevel != expectedShopStarLevels[i]
                || shopManager.IsSlotSold(i) != expectedShopSoldFlags[i])
            {
                return MigrationRestoreReport.FailedScope(scope, captured, $"shop_runtime_slot_{i}_mismatch");
            }
        }

        bool[] fullRuntimeSoldFlags = shopManager.GetSoldSlotSnapshot();
        for (int i = expectedShopSoldFlags.Length; i < fullRuntimeSoldFlags.Length; i++)
        {
            if (fullRuntimeSoldFlags[i])
            {
                return MigrationRestoreReport.FailedScope(scope, captured, $"shop_runtime_trailing_sold_flag_set:{i}");
            }
        }

        Debug.Log($"[PlayerManager] HostMigration shop runtime restore complete ({context}) P{playerId} revision={expectedShopRevision} count={runtimeItems.Count}");
        return new MigrationRestoreReport(scope, captured, captured, 0, 0);
    }

    private bool IsMigrationRuntimeRestoreContextValid(NetworkRunner expectedRunner)
    {
        return expectedRunner != null
            && expectedRunner.IsRunning
            && Runner == expectedRunner
            && Object != null
            && Object.IsValid
            && Object.HasStateAuthority;
    }

    private static bool TryValidateDurablePlayerMigrationPayload(
        int restoredPlayerId,
        int restoredHealth,
        int restoredGold,
        int restoredWallCount,
        bool hasShopSnapshot,
        string[] shopUnitKeys,
        int[] shopStarLevels,
        bool[] shopSoldFlags,
        int shopRevision,
        int shopRound,
        out string[] resolvedShopUnitKeys,
        out string failureReason)
    {
        resolvedShopUnitKeys = System.Array.Empty<string>();
        failureReason = string.Empty;
        if (restoredPlayerId < 0 || restoredPlayerId > PlayerSnapshotCodec.MaxPlayerId)
        {
            failureReason = $"player_id_out_of_range:{restoredPlayerId}";
            return false;
        }
        if (restoredHealth < 0)
        {
            failureReason = $"health_out_of_range:{restoredHealth}";
            return false;
        }
        if (restoredGold < 0)
        {
            failureReason = $"gold_out_of_range:{restoredGold}";
            return false;
        }
        if (restoredWallCount < 0)
        {
            failureReason = $"wall_count_out_of_range:{restoredWallCount}";
            return false;
        }
        if (!hasShopSnapshot)
        {
            failureReason = "shop_snapshot_not_captured";
            return false;
        }
        if (shopUnitKeys == null || shopStarLevels == null || shopSoldFlags == null)
        {
            failureReason = "shop_snapshot_array_null";
            return false;
        }
        if (shopUnitKeys.Length != shopStarLevels.Length || shopUnitKeys.Length != shopSoldFlags.Length)
        {
            failureReason = $"shop_snapshot_shape_mismatch:keys={shopUnitKeys.Length},stars={shopStarLevels.Length},sold={shopSoldFlags.Length}";
            return false;
        }
        if (shopUnitKeys.Length > SHOP_SNAPSHOT_CAPACITY)
        {
            failureReason = $"shop_snapshot_truncated:{shopUnitKeys.Length}/{SHOP_SNAPSHOT_CAPACITY}";
            return false;
        }
        if (shopRevision <= 0)
        {
            failureReason = $"shop_revision_invalid:{shopRevision}";
            return false;
        }
        if (shopRound < 0)
        {
            failureReason = $"shop_round_invalid:{shopRound}";
            return false;
        }

        var resolvedKeys = new string[shopUnitKeys.Length];
        for (int i = 0; i < shopUnitKeys.Length; i++)
        {
            string normalizedUnitKey = NormalizeShopUnitKey(shopUnitKeys[i]);
            if (string.IsNullOrWhiteSpace(normalizedUnitKey))
            {
                failureReason = $"shop_slot_{i}:unit_key_empty";
                return false;
            }
            int unitKeyHash = StableUnitDataIdentityHash(normalizedUnitKey);
            if (!TryResolveLoadedUnitDataKeyByStableHash(
                    unitKeyHash,
                    out string resolvedUnitKey,
                    out string resolveFailureReason))
            {
                failureReason = $"shop_slot_{i}:{resolveFailureReason}";
                return false;
            }
            if (shopStarLevels[i] <= 0 || shopStarLevels[i] > byte.MaxValue)
            {
                failureReason = $"shop_slot_{i}:star_level_out_of_range:{shopStarLevels[i]}";
                return false;
            }

            resolvedKeys[i] = resolvedUnitKey;
        }

        resolvedShopUnitKeys = resolvedKeys;
        return true;
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
        if (augment != null && augment.TryGetBossMonster(out _))
        {
            _ownedBossAugments.Add(augment);
            PublishAugmentRuntimeMigrationStateFromAuthority("add_owned_boss");
            // Debug.Log($"<color=red>[PlayerManager] Player {playerId}: 보스 '{augment.bossMonsterData.monsterName}' 보유 추가 (총 {_ownedBossAugments.Count}마리)</color>");
        }
    }

    public IReadOnlyList<AugmentData> GetOwnedBosses() => _ownedBossAugments;

    public bool ConsumeOwnedBoss(MonsterData bossData)
    {
        var augment = _ownedBossAugments.FirstOrDefault(candidate =>
            candidate != null &&
            candidate.TryGetBossMonster(out MonsterData ownedBoss) &&
            ownedBoss == bossData);
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

        RemoveOneOwnedBossByMonsterDataName(bossMonsterDataName);
    }

    private bool RemoveOneOwnedBossByMonsterDataName(string bossMonsterDataName)
    {
        int matchingIndex = _ownedBossAugments.FindIndex(augment =>
            augment != null &&
            augment.TryGetBossMonster(out MonsterData ownedBoss) &&
            ownedBoss.name == bossMonsterDataName);
        if (matchingIndex < 0)
        {
            return false;
        }

        _ownedBossAugments.RemoveAt(matchingIndex);
        return true;
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
    private readonly PlayerMagicScrollInventory _scrollInventory = new PlayerMagicScrollInventory();
    
    /// <summary>
    /// 보유 중인 마법 스크롤 목록 (읽기 전용)
    /// </summary>
    public IReadOnlyList<MagicScrollData> OwnedScrolls => _scrollInventory.Items;

    /// <summary>
    /// 마법 스크롤을 플레이어 인벤토리에 추가합니다.
    /// </summary>
    public void AddMagicScroll(MagicScrollData scrollData)
    {
        if (!HasStateAuthorityOrNoNetwork())
        {
            return;
        }

        if (_scrollInventory.TryAdd(scrollData))
        {
            BumpOwnedMagicScrollRevisionIfAuthoritativeOrOffline();
            Debug.Log($"<color=magenta>[PlayerManager] Player {playerId}: 마법 스크롤 '{scrollData.scrollName}' 획득 (총 {_scrollInventory.Count}개)</color>");

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

        if (_scrollInventory.TryConsume(scrollData, out _))
        {
            BumpOwnedMagicScrollRevisionIfAuthoritativeOrOffline();
            Debug.Log($"<color=magenta>[PlayerManager] Player {playerId}: 마법 스크롤 '{scrollData.scrollName}' 사용 (남은 {_scrollInventory.Count}개)</color>");

            PublishOwnedMagicScrollsChanged();
            SyncOwnedMagicScrollsToClientsIfAuthoritative();
            return true;
        }
        
        return false;
    }

    public int FindOwnedMagicScrollSlot(MagicScrollData scrollData)
    {
        return _scrollInventory.FindSlot(scrollData);
    }

    public bool TryGetMagicScrollAtSlot(int scrollSlotIndex, out MagicScrollData scrollData, out string reason)
    {
        return _scrollInventory.TryGetAt(scrollSlotIndex, out scrollData, out reason);
    }

    public bool TryConsumeMagicScrollSlot(int scrollSlotIndex, out MagicScrollData consumedScroll, out string reason)
    {
        consumedScroll = null;
        if (!HasStateAuthorityOrNoNetwork())
        {
            reason = "state_authority_required";
            return false;
        }

        if (!_scrollInventory.TryConsumeAt(scrollSlotIndex, out consumedScroll, out reason))
        {
            return false;
        }

        BumpOwnedMagicScrollRevisionIfAuthoritativeOrOffline();
        Debug.Log($"<color=magenta>[PlayerManager] Player {playerId}: 마법 스크롤 '{consumedScroll.scrollName}' 사용 (slot={scrollSlotIndex}, 남은 {_scrollInventory.Count}개)</color>");

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

        if (!_scrollInventory.TryRefundAt(scrollSlotIndex, scrollData))
        {
            return false;
        }

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

            MagicScrollData scrollData = await AssetLoader.LoadAssetAsync<MagicScrollData>(scrollDataName, _assetOwner);
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

        _scrollInventory.Replace(syncedScrolls);
        _lastAppliedOwnedMagicScrollRevision = revision;
        PublishOwnedMagicScrollsChanged();
    }

    private string[] BuildOwnedMagicScrollNameArray()
    {
        return _scrollInventory.BuildAssetNames();
    }

    private void PublishOwnedMagicScrollsChanged()
    {
        GameEvents.TriggerMagicScrollPoolChanged(playerId, _scrollInventory.Items);
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
        out string[] scrollContentIds,
        out string[] scrollDataNames)
    {
        revision = OwnedMagicScrollRevision;
        scrollDataRefs = System.Array.Empty<MagicScrollData>();
        scrollContentIds = System.Array.Empty<string>();
        scrollDataNames = System.Array.Empty<string>();

        var validScrolls = _scrollInventory.BuildValidSnapshot();
        scrollDataRefs = validScrolls;
        scrollContentIds = _scrollInventory.BuildContentIds();
        scrollDataNames = _scrollInventory.BuildAssetNames();
        return true;
    }

    public async UniTask<MigrationRestoreReport> RestoreOwnedMagicScrollsFromMigrationSnapshotAsync(
        int revision,
        MagicScrollData[] scrollDataRefs,
        string[] scrollContentIds,
        string[] scrollDataNames,
        string context,
        NetworkRunner expectedMigrationRunner,
        CancellationToken migrationCancellationToken)
    {
        string scope = $"owned_magic_scrolls:P{playerId}";
        if (!TryValidateOwnedMagicScrollMigrationSnapshot(
                revision,
                scrollDataRefs,
                scrollContentIds,
                scrollDataNames,
                out int count,
                out string validationError))
        {
            return MigrationRestoreReport.FailedScope(
                scope,
                Mathf.Max(1, count),
                $"owned_scroll_snapshot_invalid:{validationError}");
        }

        if (!CanApplyOwnedMagicScrollMigration(expectedMigrationRunner, migrationCancellationToken))
        {
            return MigrationRestoreReport.FailedScope(
                scope,
                Mathf.Max(1, count),
                "owned_scroll_restore_canceled_or_lifecycle_changed");
        }

        if (OwnedMagicScrollRevision > revision ||
            _lastAppliedOwnedMagicScrollRevision > revision ||
            _latestReceivedOwnedMagicScrollRevision > revision)
        {
            return MigrationRestoreReport.FailedScope(
                scope,
                Mathf.Max(1, count),
                $"owned_scroll_snapshot_stale:current={OwnedMagicScrollRevision},applied={_lastAppliedOwnedMagicScrollRevision},received={_latestReceivedOwnedMagicScrollRevision},snapshot={revision}");
        }

        var restored = new List<MagicScrollData>(count);
        for (int i = 0; i < count; i++)
        {
            MagicScrollData data = scrollDataRefs[i];
            string expectedContentId = scrollContentIds != null
                ? StableDataKeyUtility.NormalizeContentId(scrollContentIds[i])
                : string.Empty;
            string expectedName = scrollDataNames[i];
            if (data == null)
            {
                IEnumerable<MagicScrollData> candidates = GetLoadedMagicScrollCandidates();
                if (!PlayerMagicScrollInventory.TryResolveSnapshotIdentity(
                        candidates,
                        expectedContentId,
                        expectedName,
                        out data,
                        out string resolutionFailure))
                {
                    if (resolutionFailure.IndexOf("ambiguous", StringComparison.Ordinal) >= 0)
                    {
                        return MigrationRestoreReport.FailedScope(
                            scope,
                            Mathf.Max(1, count),
                            $"owned_scroll_identity_ambiguous:index={i},reason={resolutionFailure}");
                    }

                    if (string.IsNullOrWhiteSpace(expectedName))
                    {
                        return MigrationRestoreReport.FailedScope(
                            scope,
                            Mathf.Max(1, count),
                            $"owned_scroll_asset_unresolved:index={i},contentId={expectedContentId}");
                    }

                    data = await AssetLoader.LoadAssetAsync<MagicScrollData>(expectedName, _assetOwner);
                }

                if (!CanApplyOwnedMagicScrollMigration(expectedMigrationRunner, migrationCancellationToken))
                {
                    return MigrationRestoreReport.FailedScope(
                        scope,
                        Mathf.Max(1, count),
                        "owned_scroll_restore_canceled_or_lifecycle_changed");
                }

                if (data == null)
                {
                    return MigrationRestoreReport.FailedScope(
                        scope,
                        Mathf.Max(1, count),
                        $"owned_scroll_asset_missing:index={i},contentId={expectedContentId},name={expectedName}");
                }
            }

            if (!MatchesOwnedMagicScrollSnapshotIdentity(data, expectedContentId, expectedName))
            {
                return MigrationRestoreReport.FailedScope(
                    scope,
                    Mathf.Max(1, count),
                    $"owned_scroll_identity_mismatch:index={i},actualContentId={data.ContentId},expectedContentId={expectedContentId},actualName={data.name},legacyName={expectedName}");
            }

            restored.Add(data);
        }

        if (!CanApplyOwnedMagicScrollMigration(expectedMigrationRunner, migrationCancellationToken))
        {
            return MigrationRestoreReport.FailedScope(
                scope,
                Mathf.Max(1, count),
                "owned_scroll_restore_canceled_or_lifecycle_changed");
        }

        if (OwnedMagicScrollRevision > revision ||
            _lastAppliedOwnedMagicScrollRevision > revision ||
            _latestReceivedOwnedMagicScrollRevision > revision)
        {
            return MigrationRestoreReport.FailedScope(
                scope,
                Mathf.Max(1, count),
                $"owned_scroll_snapshot_became_stale:current={OwnedMagicScrollRevision},applied={_lastAppliedOwnedMagicScrollRevision},received={_latestReceivedOwnedMagicScrollRevision},snapshot={revision}");
        }

        _scrollInventory.Replace(restored);
        OwnedMagicScrollRevision = revision;
        _lastAppliedOwnedMagicScrollRevision = revision;
        _latestReceivedOwnedMagicScrollRevision = revision;
        PublishOwnedMagicScrollsChanged();
        SyncOwnedMagicScrollsToClientsIfAuthoritative();

        string mismatch = VerifyOwnedMagicScrollMigrationSnapshot(
            revision,
            scrollDataRefs,
            scrollContentIds,
            scrollDataNames);
        if (!string.IsNullOrEmpty(mismatch))
        {
            return MigrationRestoreReport.FailedScope(
                scope,
                Mathf.Max(1, count),
                $"owned_scroll_verify_failed:{mismatch}");
        }

        Debug.Log($"[PlayerManager] HostMigration owned scroll restore complete ({context}) P{playerId} rev={OwnedMagicScrollRevision} count={_scrollInventory.Count}");
        return new MigrationRestoreReport(scope, count, count, 0, 0);
    }

    private static bool TryValidateOwnedMagicScrollMigrationSnapshot(
        int revision,
        MagicScrollData[] scrollDataRefs,
        string[] scrollContentIds,
        string[] scrollDataNames,
        out int count,
        out string error)
    {
        count = scrollDataRefs?.Length ?? scrollDataNames?.Length ?? 0;
        error = string.Empty;
        if (revision < 0)
        {
            error = $"revision_invalid:{revision}";
            return false;
        }

        if (scrollDataRefs == null || scrollDataNames == null)
        {
            error = "null_array";
            return false;
        }

        if (scrollDataNames.Length != count ||
            (scrollContentIds != null && scrollContentIds.Length != count))
        {
            error = $"shape_mismatch:refs={count},contentIds={scrollContentIds?.Length ?? -1},names={scrollDataNames.Length}";
            return false;
        }

        for (int i = 0; i < count; i++)
        {
            MagicScrollData data = scrollDataRefs[i];
            string expectedContentId = scrollContentIds != null
                ? StableDataKeyUtility.NormalizeContentId(scrollContentIds[i])
                : string.Empty;
            string expectedName = scrollDataNames[i];
            if (string.IsNullOrEmpty(expectedContentId) && string.IsNullOrWhiteSpace(expectedName))
            {
                error = $"identity_missing:index={i}";
                return false;
            }

            if (data != null &&
                !MatchesOwnedMagicScrollSnapshotIdentity(data, expectedContentId, expectedName))
            {
                error = $"identity_mismatch:index={i},refContentId={data.ContentId},contentId={expectedContentId},refName={data.name},name={expectedName}";
                return false;
            }
        }

        return true;
    }

    private string VerifyOwnedMagicScrollMigrationSnapshot(
        int revision,
        MagicScrollData[] scrollDataRefs,
        string[] scrollContentIds,
        string[] scrollDataNames)
    {
        if (OwnedMagicScrollRevision != revision ||
            _lastAppliedOwnedMagicScrollRevision != revision ||
            _latestReceivedOwnedMagicScrollRevision != revision)
        {
            return $"revision={OwnedMagicScrollRevision}/{_lastAppliedOwnedMagicScrollRevision}/{_latestReceivedOwnedMagicScrollRevision}/{revision}";
        }

        int count = scrollDataRefs.Length;
        if (_scrollInventory.Count != count)
        {
            return $"count={_scrollInventory.Count}/{count}";
        }

        for (int i = 0; i < count; i++)
        {
            MagicScrollData restored = _scrollInventory.Items[i];
            MagicScrollData expectedRef = scrollDataRefs[i];
            string expectedContentId = scrollContentIds != null
                ? StableDataKeyUtility.NormalizeContentId(scrollContentIds[i])
                : string.Empty;
            string expectedName = scrollDataNames[i];
            bool identityMatches = MatchesOwnedMagicScrollSnapshotIdentity(
                restored,
                expectedContentId,
                expectedName);
            if (identityMatches && expectedRef != null)
            {
                identityMatches = MatchesOwnedMagicScrollSnapshotIdentity(
                    expectedRef,
                    expectedContentId,
                    expectedName);
            }

            if (!identityMatches)
            {
                return $"entry={i},identity={identityMatches},actualContentId={restored?.ContentId},expectedContentId={expectedContentId},actualName={restored?.name},legacyName={expectedName}";
            }
        }

        return string.Empty;
    }

    private static IEnumerable<MagicScrollData> GetLoadedMagicScrollCandidates()
    {
        var candidates = new HashSet<MagicScrollData>();
        LoadManager loadManager = LoadManager.Instance;
        if (loadManager != null)
        {
            foreach (MagicScrollData data in loadManager.GetAllMagicScrollData())
            {
                if (data != null)
                {
                    candidates.Add(data);
                }
            }
        }

        foreach (MagicScrollData data in Resources.FindObjectsOfTypeAll<MagicScrollData>())
        {
            if (data != null)
            {
                candidates.Add(data);
            }
        }

        return candidates;
    }

    private static bool MatchesOwnedMagicScrollSnapshotIdentity(
        MagicScrollData data,
        string expectedContentId,
        string legacyAssetName)
    {
        if (data == null)
        {
            return false;
        }

        string normalizedContentId = StableDataKeyUtility.NormalizeContentId(expectedContentId);
        if (!string.IsNullOrEmpty(normalizedContentId))
        {
            return string.Equals(data.ContentId, normalizedContentId, StringComparison.Ordinal);
        }

        return !string.IsNullOrWhiteSpace(legacyAssetName) &&
               string.Equals(
                   StableDataKeyUtility.NormalizeKey(data.name),
                   StableDataKeyUtility.NormalizeKey(legacyAssetName),
                   StringComparison.Ordinal);
    }

    private bool CanApplyOwnedMagicScrollMigration(
        NetworkRunner expectedMigrationRunner,
        CancellationToken migrationCancellationToken)
    {
        return expectedMigrationRunner != null &&
               !migrationCancellationToken.IsCancellationRequested &&
               expectedMigrationRunner.IsRunning &&
               Object != null &&
               Object.IsValid &&
               Object.HasStateAuthority &&
               Runner == expectedMigrationRunner;
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
    /// 공격 시퀀스 풀을 갱신합니다. 일반 몬스터는 흑마력으로 반복 소환하는
    /// 고정 카탈로그이며, 보스만 증강으로 획득한 수량을 소비합니다.
    /// </summary>
    /// <param name="round">현재 라운드</param>
    /// <param name="currentBattleOpponentId">현재 전투에서 매칭된 상대 ID (-1이면 opponentManager 사용)</param>
    public void RefreshAttackMonsterPool(
        int round,
        int currentBattleOpponentId = -1,
        bool schedulePrewarm = true)
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

        // 1. 모든 비보스 몬스터를 재사용 가능한 카탈로그 항목으로 노출합니다.
        // RemainingCount=1은 기존 UI/AI의 가용성 표현을 유지하기 위한 값이며,
        // 일반 몬스터 소환 시에는 이 수량을 소비하지 않고 흑마력만 소비합니다.
        var waveDatabase = AddressablesManager.Instance?.WaveDatabase;
        if (waveDatabase?.attackSequenceMonsterCatalog != null)
        {
            var seenNormalMonsters = new HashSet<MonsterData>();
            foreach (var monsterData in waveDatabase.attackSequenceMonsterCatalog)
            {
                if (monsterData == null ||
                    monsterData.monsterRank == MonsterRank.Boss ||
                    !seenNormalMonsters.Add(monsterData))
                {
                    continue;
                }

                AttackMonsterPool.Add(new MonsterPoolEntry(monsterData, 1));
            }
        }
        else
        {
            Debug.LogError($"[PlayerManager] Attack sequence monster catalog is missing for P{playerId}, round={round}.");
        }

        // 2. 보유 보스는 종류별로 합쳐 기존 증강 획득 수량만큼 표시합니다.
        foreach (var bossGroup in _ownedBossAugments
                     .Select(augment => augment != null && augment.TryGetBossMonster(out MonsterData boss) ? boss : null)
                     .Where(boss => boss != null)
                     .GroupBy(boss => boss))
        {
            MonsterData bossData = bossGroup.Key;
            AttackMonsterPool.Add(new MonsterPoolEntry(
                bossData,
                bossGroup.Count(),
                -1,
                -1,
                this.playerId
            ));
        }

        if (AttackMonsterPool.Count > ATTACK_POOL_SNAPSHOT_CAPACITY)
        {
            Debug.LogError($"[PlayerManager] Attack monster pool capacity exceeded: P{playerId} count={AttackMonsterPool.Count} capacity={ATTACK_POOL_SNAPSHOT_CAPACITY}.");
            AttackMonsterPool.RemoveRange(
                ATTACK_POOL_SNAPSHOT_CAPACITY,
                AttackMonsterPool.Count - ATTACK_POOL_SNAPSHOT_CAPACITY);
        }

        // 이벤트 발생 (UI 갱신용)
        GameEvents.TriggerMonsterPoolChanged(playerId, AttackMonsterPool);
        SyncAttackMonsterPoolToClientsIfAuthoritative();
        if (schedulePrewarm)
        {
            PrewarmAttackMonsterPoolIfPossible("RefreshAttackMonsterPool");
        }
    }

    private void PrewarmAttackMonsterPoolIfPossible(string context)
    {
        if (monsterSpawner == null || AttackMonsterPool == null || AttackMonsterPool.Count == 0)
        {
            return;
        }

        monsterSpawner.PrewarmAttackMonsterPoolAsync(AttackMonsterPool, context).Forget();
    }

    public void MarkAttackMonsterPoolCommandSubmitted(int observedRevision, int observedBlackMagicRevision = -1)
    {
        if (Object != null && Object.HasStateAuthority)
        {
            return;
        }

        if (observedRevision >= 0)
        {
            _pendingAttackMonsterPoolCommandRevision = Mathf.Max(_pendingAttackMonsterPoolCommandRevision, observedRevision);
        }
        if (observedBlackMagicRevision >= 0)
        {
            _pendingBlackMagicCommandRevision = Mathf.Max(_pendingBlackMagicCommandRevision, observedBlackMagicRevision);
        }
        _pendingAttackMonsterPoolCommandStartedAt = Time.unscaledTime;
    }

    private void ClearPendingAttackMonsterPoolCommand()
    {
        _pendingAttackMonsterPoolCommandRevision = -1;
        _pendingBlackMagicCommandRevision = -1;
        _pendingAttackMonsterPoolCommandStartedAt = 0f;
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_SyncAttackMonsterPool(
        int prepareRound,
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
            prepareRound,
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

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_ReportMonsterPrewarmComplete(
        int revision,
        NetworkBool succeeded,
        int failedPrefabCount,
        RpcInfo info = default)
    {
        if (Object == null || !Object.IsValid || !Object.HasStateAuthority)
        {
            return;
        }

        if (info.Source == PlayerRef.None || Object.InputAuthority != info.Source)
        {
            Debug.LogWarning(
                $"[PlayerManager] Rejected monster prewarm completion from unauthorized source. " +
                $"source={info.Source}, owner={Object.InputAuthority}, playerId={playerId}, " +
                $"revision={revision}");
            return;
        }

        int authorityRound = GameManagers.Instance != null
            ? Mathf.Max(1, GameManagers.Instance.currentRound)
            : 1;
        GameManagers.Instance?.RecordRemoteMonsterPrewarmCompletion(
            info.Source,
            playerId,
            authorityRound,
            revision,
            succeeded,
            $"failedPrefabs={Mathf.Max(0, failedPrefabCount)}");
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
            out MonsterData[] monsterDataRefs,
            out monsterDataNames,
            out remainingCounts,
            out maxCounts,
            out isBossValues,
            out bossUniqueIds,
            out targetPlayerIds,
            out originPlayerIds);
        return AttackMonsterPool != null;
    }

    public async UniTask<MigrationRestoreReport> RestoreAttackMonsterPoolFromMigrationSnapshotAsync(
        int revision,
        MonsterData[] monsterDataRefs,
        string[] monsterDataNames,
        int[] remainingCounts,
        int[] maxCounts,
        int[] isBossValues,
        int[] bossUniqueIds,
        int[] targetPlayerIds,
        int[] originPlayerIds,
        string context,
        NetworkRunner expectedMigrationRunner,
        CancellationToken migrationCancellationToken)
    {
        string scope = $"attack_pool:P{playerId}";
        if (!TryValidateAttackMonsterPoolMigrationSnapshot(
                monsterDataRefs,
                monsterDataNames,
                remainingCounts,
                maxCounts,
                isBossValues,
                bossUniqueIds,
                targetPlayerIds,
                originPlayerIds,
                out int captured,
                out string validationError))
        {
            return MigrationRestoreReport.FailedScope(
                scope,
                Mathf.Max(1, captured),
                $"attack_pool_snapshot_invalid:{validationError}");
        }

        if (Object == null || !Object.IsValid || !Object.HasStateAuthority)
        {
            return MigrationRestoreReport.FailedScope(
                scope,
                Mathf.Max(1, captured),
                "state_authority_missing");
        }

        if (!CanApplyAttackMonsterPoolMigration(expectedMigrationRunner, migrationCancellationToken))
        {
            return MigrationRestoreReport.FailedScope(
                scope,
                Mathf.Max(1, captured),
                "attack_pool_restore_canceled_or_lifecycle_changed");
        }

        if (AttackMonsterPoolRevision > revision || _lastAppliedAttackMonsterPoolRevision > revision)
        {
            return MigrationRestoreReport.FailedScope(
                scope,
                Mathf.Max(1, captured),
                $"attack_pool_snapshot_stale:current={AttackMonsterPoolRevision},applied={_lastAppliedAttackMonsterPoolRevision},snapshot={revision}");
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
            return BuildAttackMonsterPoolMigrationVerificationReport(
                scope,
                revision,
                monsterDataRefs,
                monsterDataNames,
                remainingCounts,
                maxCounts,
                isBossValues,
                bossUniqueIds,
                targetPlayerIds,
                originPlayerIds,
                context);
        }

        await ApplyAttackMonsterPoolSnapshotAsync(
            GameManagers.Instance != null ? Mathf.Max(1, GameManagers.Instance.currentRound) : 1,
            revision,
            monsterDataNames,
            remainingCounts,
            maxCounts,
            isBossValues,
            bossUniqueIds,
            targetPlayerIds,
            originPlayerIds,
            true,
            context,
            expectedMigrationRunner,
            migrationCancellationToken);

        if (!CanApplyAttackMonsterPoolMigration(expectedMigrationRunner, migrationCancellationToken))
        {
            return MigrationRestoreReport.FailedScope(
                scope,
                Mathf.Max(1, captured),
                "attack_pool_restore_canceled_or_lifecycle_changed");
        }

        return BuildAttackMonsterPoolMigrationVerificationReport(
            scope,
            revision,
            monsterDataRefs,
            monsterDataNames,
            remainingCounts,
            maxCounts,
            isBossValues,
            bossUniqueIds,
            targetPlayerIds,
            originPlayerIds,
            context);
    }

    private static bool TryValidateAttackMonsterPoolMigrationSnapshot(
        MonsterData[] monsterDataRefs,
        string[] monsterDataNames,
        int[] remainingCounts,
        int[] maxCounts,
        int[] isBossValues,
        int[] bossUniqueIds,
        int[] targetPlayerIds,
        int[] originPlayerIds,
        out int count,
        out string error)
    {
        count = monsterDataRefs?.Length ?? monsterDataNames?.Length ?? 0;
        error = string.Empty;

        if (monsterDataRefs == null || monsterDataNames == null || remainingCounts == null ||
            maxCounts == null || isBossValues == null || bossUniqueIds == null ||
            targetPlayerIds == null || originPlayerIds == null)
        {
            error = "null_array";
            return false;
        }

        if (monsterDataNames.Length != count || remainingCounts.Length != count ||
            maxCounts.Length != count || isBossValues.Length != count ||
            bossUniqueIds.Length != count || targetPlayerIds.Length != count ||
            originPlayerIds.Length != count)
        {
            error = $"shape_mismatch:refs={count},names={monsterDataNames.Length},remaining={remainingCounts.Length},max={maxCounts.Length},boss={isBossValues.Length},unique={bossUniqueIds.Length},target={targetPlayerIds.Length},origin={originPlayerIds.Length}";
            return false;
        }

        for (int i = 0; i < count; i++)
        {
            MonsterData data = monsterDataRefs[i];
            string dataName = monsterDataNames[i];
            if (data == null && string.IsNullOrWhiteSpace(dataName))
            {
                error = $"identity_missing:index={i}";
                return false;
            }

            if (data != null && !string.IsNullOrWhiteSpace(dataName) && !MatchesMonsterData(data, dataName))
            {
                error = $"identity_mismatch:index={i},ref={data.name},name={dataName}";
                return false;
            }

            if (remainingCounts[i] < 0 || maxCounts[i] < remainingCounts[i])
            {
                error = $"count_invalid:index={i},remaining={remainingCounts[i]},max={maxCounts[i]}";
                return false;
            }

            if (isBossValues[i] != 0 && isBossValues[i] != 1)
            {
                error = $"boss_flag_invalid:index={i},value={isBossValues[i]}";
                return false;
            }
        }

        return true;
    }

    private MigrationRestoreReport BuildAttackMonsterPoolMigrationVerificationReport(
        string scope,
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
        int captured = monsterDataRefs.Length;
        int restoredCount = AttackMonsterPool != null ? AttackMonsterPool.Count : 0;
        string mismatch = string.Empty;

        if (AttackMonsterPoolRevision != revision)
        {
            mismatch = $"revision={AttackMonsterPoolRevision}/{revision}";
        }
        else if (restoredCount != captured)
        {
            mismatch = $"count={restoredCount}/{captured}";
        }
        else
        {
            for (int i = 0; i < captured; i++)
            {
                MonsterPoolEntry restored = AttackMonsterPool[i];
                MonsterData expectedRef = monsterDataRefs[i];
                string expectedName = monsterDataNames[i];
                bool identityMatches = restored?.MonsterData != null &&
                    ((expectedRef != null &&
                      (ReferenceEquals(restored.MonsterData, expectedRef) || MatchesMonsterData(restored.MonsterData, expectedRef.name))) ||
                     (expectedRef == null && MatchesMonsterData(restored.MonsterData, expectedName)));

                if (!identityMatches ||
                    (!string.IsNullOrWhiteSpace(expectedName) && !MatchesMonsterData(restored.MonsterData, expectedName)) ||
                    restored.RemainingCount != remainingCounts[i] ||
                    restored.MaxCount != maxCounts[i] ||
                    restored.IsBoss != (isBossValues[i] == 1) ||
                    restored.BossUniqueId != bossUniqueIds[i] ||
                    restored.TargetPlayerId != targetPlayerIds[i] ||
                    restored.OriginPlayerId != originPlayerIds[i])
                {
                    mismatch = $"entry={i},identity={identityMatches},remaining={restored?.RemainingCount}/{remainingCounts[i]},max={restored?.MaxCount}/{maxCounts[i]},boss={restored?.IsBoss}/{(isBossValues[i] == 1)},unique={restored?.BossUniqueId}/{bossUniqueIds[i]},target={restored?.TargetPlayerId}/{targetPlayerIds[i]},origin={restored?.OriginPlayerId}/{originPlayerIds[i]}";
                    break;
                }
            }
        }

        if (!string.IsNullOrEmpty(mismatch))
        {
            return MigrationRestoreReport.FailedScope(
                scope,
                Mathf.Max(1, captured),
                $"attack_pool_verify_failed:{mismatch}");
        }

        Debug.Log($"[PlayerManager] AttackMonsterPool migration restore complete ({context}) P{playerId} rev={AttackMonsterPoolRevision} entries={restoredCount}");
        return new MigrationRestoreReport(scope, captured, restoredCount, 0, 0);
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
            out MonsterData[] monsterDataRefs,
            out string[] monsterDataNames,
            out int[] remainingCounts,
            out int[] maxCounts,
            out int[] isBossValues,
            out int[] bossUniqueIds,
            out int[] targetPlayerIds,
            out int[] originPlayerIds);

        PublishReplicatedAttackMonsterPoolSnapshot(
            monsterDataRefs,
            monsterDataNames,
            remainingCounts,
            maxCounts,
            isBossValues,
            bossUniqueIds,
            targetPlayerIds,
            originPlayerIds);

        RPC_SyncAttackMonsterPool(
            GameManagers.Instance != null ? Mathf.Max(1, GameManagers.Instance.currentRound) : 1,
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
        MonsterData[] monsterDataRefs,
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
            MonsterData monsterData = monsterDataRefs != null && i < monsterDataRefs.Length
                ? monsterDataRefs[i]
                : null;

            AttackMonsterPoolSnapshotSlots.Set(i, new AttackMonsterPoolSnapshotSlot
            {
                DataId = monsterData != null && monsterData.ContentIdHash != 0
                    ? monsterData.ContentIdHash
                    : StableMonsterDataKeyHash(monsterDataNames[i]),
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
        int prepareRound,
        int revision,
        string[] monsterDataNames,
        int[] remainingCounts,
        int[] maxCounts,
        int[] isBossValues,
        int[] bossUniqueIds,
        int[] targetPlayerIds,
        int[] originPlayerIds,
        bool allowStateAuthorityApply,
        string context,
        NetworkRunner expectedMigrationRunner = null,
        CancellationToken migrationCancellationToken = default)
    {
        if (!CanApplyAttackMonsterPoolMigration(expectedMigrationRunner, migrationCancellationToken))
        {
            return;
        }

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
            if (!CanApplyAttackMonsterPoolMigration(expectedMigrationRunner, migrationCancellationToken))
            {
                return;
            }

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

        if (!CanApplyAttackMonsterPoolMigration(expectedMigrationRunner, migrationCancellationToken))
        {
            return;
        }

        ApplyAttackMonsterPoolEntries(revision, syncedPool, schedulePrewarm: false);
        if (!allowStateAuthorityApply && Object != null && Object.IsValid && !Object.HasStateAuthority)
        {
            CancellationTokenSource generationCts = BeginAttackMonsterPrewarmGeneration(out int prewarmGeneration);
            MonsterPrewarmReport prewarmReport;
            try
            {
                prewarmReport = await PrewarmAppliedAttackMonsterPoolAsync(
                    prepareRound,
                    revision,
                    prewarmGeneration,
                    context,
                    generationCts.Token);
            }
            finally
            {
                CompleteAttackMonsterPrewarmGeneration(generationCts);
            }

            if (IsAttackMonsterPrewarmGenerationCurrent(prewarmGeneration, revision) &&
                Object != null &&
                Object.IsValid &&
                Object.HasInputAuthority)
            {
                RPC_ReportMonsterPrewarmComplete(
                    revision,
                    prewarmReport.Succeeded,
                    prewarmReport.FailedPrefabCount);
            }
        }

        if (allowStateAuthorityApply)
        {
            ResendAttackMonsterPoolToClientsIfAuthoritative();
            Debug.Log($"[PlayerManager] AttackMonsterPool async restore complete ({context}) P{playerId} rev={AttackMonsterPoolRevision} entries={AttackMonsterPool.Count}");
        }
    }

    private bool CanApplyAttackMonsterPoolMigration(
        NetworkRunner expectedMigrationRunner,
        CancellationToken migrationCancellationToken)
    {
        if (expectedMigrationRunner == null)
        {
            return true;
        }

        return !migrationCancellationToken.IsCancellationRequested &&
               expectedMigrationRunner.IsRunning &&
               Object != null &&
               Object.IsValid &&
               Object.HasStateAuthority &&
               Runner == expectedMigrationRunner;
    }

    private async UniTask<MonsterPrewarmReport> PrewarmAppliedAttackMonsterPoolAsync(
        int prepareRound,
        int revision,
        int prewarmGeneration,
        string context,
        CancellationToken generationCancellationToken)
    {
        string prewarmContext = $"{context}.ClientPrewarm.P{playerId}.Rev{revision}";
        if (monsterSpawner == null)
        {
            MonsterPrewarmReport missingSpawner = MonsterPrewarmReport.Failed(
                prewarmContext,
                1,
                "MonsterSpawner unavailable");
            Debug.LogWarning($"[PlayerManager] Client monster prewarm failed. {missingSpawner}");
            return missingSpawner;
        }

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(generationCancellationToken);
            timeoutCts.CancelAfter(System.TimeSpan.FromSeconds(20));
            return await PrewarmAppliedAttackMonsterPoolCoreAsync(
                prepareRound,
                revision,
                prewarmGeneration,
                prewarmContext,
                timeoutCts.Token);
        }
        catch (System.OperationCanceledException)
        {
            string reason = generationCancellationToken.IsCancellationRequested
                ? "generation canceled"
                : "timeout after 20s";
            MonsterPrewarmReport canceled = MonsterPrewarmReport.Failed(
                prewarmContext,
                1,
                reason);
            Debug.LogWarning($"[PlayerManager] Client monster prewarm canceled; snapshot apply will continue. {canceled}");
            return canceled;
        }
        catch (System.Exception exception)
        {
            MonsterPrewarmReport failure = MonsterPrewarmReport.Failed(prewarmContext, 1, exception.Message);
            Debug.LogWarning($"[PlayerManager] Client monster prewarm failed; snapshot apply will continue. {failure}");
            return failure;
        }
    }

    private async UniTask<MonsterPrewarmReport> PrewarmAppliedAttackMonsterPoolCoreAsync(
        int prepareRound,
        int revision,
        int prewarmGeneration,
        string context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        int round = Mathf.Max(1, prepareRound);
        bool isLocalInputPlayer = Object != null && Object.IsValid && Object.HasInputAuthority;
        RoundWaveData waveData = isLocalInputPlayer
            ? AddressablesManager.Instance?.WaveDatabase?.GetWaveForRound(round)
            : null;
        List<PlayerManager> activePlayers = GameManagers.Instance != null
            ? GameManagers.Instance.AllPlayers
                .Where(player => player != null && player.GetHealth() > 0)
                .ToList()
            : new List<PlayerManager>();
        int activePlayerCount = Mathf.Max(1, activePlayers.Count);
        int maximumProjectedBlackMagic = activePlayers
            .Select(player => player.GetProjectedBlackMagicMaximumForRound(round))
            .DefaultIfEmpty(GetProjectedBlackMagicMaximumForRound(round))
            .Max();
        MonsterPrewarmReport poolReport = await monsterSpawner.PrewarmAttackMonsterPoolAsync(
            AttackMonsterPool,
            $"{context}.Catalog",
            activePlayerCount,
            maximumProjectedBlackMagic,
            concurrentWaveData: waveData,
            cancellationToken: cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfAttackMonsterPrewarmGenerationStale(prewarmGeneration, revision, cancellationToken);

        if (!isLocalInputPlayer)
        {
            return poolReport;
        }

        MonsterPrewarmReport waveReport = waveData != null
            ? await monsterSpawner.PrewarmWaveAsync(
                waveData,
                $"{context}.NextWave.R{round}",
                activePlayerCount,
                cancellationToken)
            : MonsterPrewarmReport.Failed($"{context}.NextWave.R{round}", 1, "RoundWaveData unavailable");
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfAttackMonsterPrewarmGenerationStale(prewarmGeneration, revision, cancellationToken);
        MonsterPrewarmReport combined = MonsterPrewarmReport.Combine(context, poolReport, waveReport);
        if (!combined.Succeeded)
        {
            Debug.LogWarning($"[PlayerManager] Client monster prewarm completed with failures. {combined}");
        }
        else
        {
            Debug.Log($"[PlayerManager] Client monster prewarm ready. {combined}");
        }

        return combined;
    }

    private CancellationTokenSource BeginAttackMonsterPrewarmGeneration(out int generation)
    {
        _attackMonsterPrewarmCancellation?.Cancel();
        _attackMonsterPrewarmCancellation?.Dispose();
        _attackMonsterPrewarmCancellation = new CancellationTokenSource();
        generation = ++_attackMonsterPrewarmGeneration;
        return _attackMonsterPrewarmCancellation;
    }

    private void CompleteAttackMonsterPrewarmGeneration(CancellationTokenSource generationCts)
    {
        if (ReferenceEquals(_attackMonsterPrewarmCancellation, generationCts))
        {
            _attackMonsterPrewarmCancellation = null;
        }

        generationCts?.Dispose();
    }

    private bool IsAttackMonsterPrewarmGenerationCurrent(int generation, int revision)
    {
        return generation == _attackMonsterPrewarmGeneration &&
               revision == _lastAppliedAttackMonsterPoolRevision;
    }

    private void ThrowIfAttackMonsterPrewarmGenerationStale(
        int generation,
        int revision,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsAttackMonsterPrewarmGenerationCurrent(generation, revision))
        {
            throw new System.OperationCanceledException("Monster prewarm generation became stale.", cancellationToken);
        }
    }

    private void CancelAttackMonsterPrewarm()
    {
        _attackMonsterPrewarmGeneration++;
        _attackMonsterPrewarmCancellation?.Cancel();
        _attackMonsterPrewarmCancellation?.Dispose();
        _attackMonsterPrewarmCancellation = null;
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

        return await AssetLoader.LoadAssetAsync<MonsterData>(monsterDataName, _assetOwner);
    }

    private string ResolveLoadedMonsterDataNameByStableHash(int monsterDataKeyHash)
    {
        if (monsterDataKeyHash == 0)
        {
            return string.Empty;
        }

        if (!TryResolveMonsterDataByStableHash(monsterDataKeyHash, out MonsterData data))
        {
            return string.Empty;
        }

        return data.name;
    }

    private static bool TryResolveMonsterDataByStableHash(int monsterDataKeyHash, out MonsterData resolved)
    {
        resolved = null;
        if (monsterDataKeyHash == 0)
        {
            return false;
        }

        LoadManager loadManager = LoadManager.Instance;
        MonsterData indexedData = loadManager?.GetMonsterDataByContentIdHash(monsterDataKeyHash);
        if (indexedData != null)
        {
            resolved = indexedData;
            return true;
        }

        var candidates = new HashSet<MonsterData>();
        if (loadManager != null)
        {
            foreach (MonsterData data in loadManager.GetAllMonsterData())
            {
                if (data != null)
                {
                    candidates.Add(data);
                }
            }
        }

        foreach (MonsterData data in Resources.FindObjectsOfTypeAll<MonsterData>())
        {
            if (data != null)
            {
                candidates.Add(data);
            }
        }

        WaveDatabase waveDatabase = AddressablesManager.Instance?.WaveDatabase;
        if (waveDatabase != null)
        {
            if (waveDatabase.attackSequenceMonsterCatalog != null)
            {
                foreach (MonsterData data in waveDatabase.attackSequenceMonsterCatalog)
                {
                    if (data != null)
                    {
                        candidates.Add(data);
                    }
                }
            }

            if (waveDatabase.rounds != null)
            {
                foreach (RoundWaveData round in waveDatabase.rounds)
                {
                    if (round?.monsters == null)
                    {
                        continue;
                    }

                    foreach (WaveMonsterEntry entry in round.monsters)
                    {
                        if (entry?.monsterData != null)
                        {
                            candidates.Add(entry.monsterData);
                        }
                    }
                }
            }
        }

        MonsterData contentIdMatch = null;
        foreach (MonsterData data in candidates)
        {
            if (data.ContentIdHash != monsterDataKeyHash)
            {
                continue;
            }

            if (contentIdMatch != null && contentIdMatch != data)
            {
                return false;
            }

            contentIdMatch = data;
        }

        if (contentIdMatch != null)
        {
            resolved = contentIdMatch;
            return true;
        }

        MonsterData legacyMatch = null;
        foreach (MonsterData data in candidates)
        {
            bool matchesLegacy = StableMonsterDataKeyHash(data.name) == monsterDataKeyHash
                || StableMonsterDataKeyHash(data.monsterName) == monsterDataKeyHash
                || StableMonsterDataKeyHash(data.monsterPrefab) == monsterDataKeyHash;
            if (!matchesLegacy)
            {
                continue;
            }

            if (legacyMatch != null && legacyMatch != data)
            {
                return false;
            }

            legacyMatch = data;
        }

        resolved = legacyMatch;
        return resolved != null;
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
        if (waveDatabase == null)
        {
            return null;
        }

        if (waveDatabase.attackSequenceMonsterCatalog != null)
        {
            foreach (var monsterData in waveDatabase.attackSequenceMonsterCatalog)
            {
                if (MatchesMonsterData(monsterData, monsterDataName))
                {
                    return monsterData;
                }
            }
        }

        if (waveDatabase.rounds == null) return null;

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

    private void ApplyAttackMonsterPoolEntries(
        int revision,
        List<MonsterPoolEntry> pool,
        bool schedulePrewarm = true)
    {
        _lastAppliedAttackMonsterPoolRevision = Mathf.Max(_lastAppliedAttackMonsterPoolRevision, revision);
        if (_pendingAttackMonsterPoolCommandRevision >= 0 && revision > _pendingAttackMonsterPoolCommandRevision)
        {
            ClearPendingAttackMonsterPoolCommand();
        }

        if (Object != null && Object.HasStateAuthority)
        {
            AttackMonsterPoolRevision = Mathf.Max(AttackMonsterPoolRevision, revision);
        }

        AttackMonsterPool = pool ?? new List<MonsterPoolEntry>();
        GameEvents.TriggerMonsterPoolChanged(playerId, AttackMonsterPool);
        if (schedulePrewarm)
        {
            PrewarmAttackMonsterPoolIfPossible("ApplyAttackMonsterPoolEntries");
        }
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
        if (_commandRequestValidator == null)
        {
            _commandRequestValidator = new PlayerCommandRequestValidator(this);
        }

        return _commandRequestValidator.Validate(
            type, intParams, stringParams, vectorParams, info, out reason);
    }

    public static bool IsUnitOwnedByPlayerForCommand(PlayerManager player, Unit unit)
    {
        return PlayerCommandRequestValidator.IsUnitOwnedByPlayer(player, unit);
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

        OnKingGoalTransformReady();

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

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        ResetLocalUnitRosterSyncState();
        CancelShopSnapshotSyncLifetime();
        CancelAttackMonsterPrewarm();
        DisposeKingRuntime();
        _assetOwner?.Dispose();
        base.Despawned(runner, hasState);
    }

    private void OnDestroy()
    {
        ResetLocalUnitRosterSyncState();
        CancelShopSnapshotSyncLifetime();
        CancelAttackMonsterPrewarm();
        DisposeKingRuntime();
        _assetOwner?.Dispose();
    }

    private void ResetShopSnapshotSyncLifetime()
    {
        CancelShopSnapshotSyncLifetime();
        _shopSnapshotSyncLifetimeCancellation = new CancellationTokenSource();
    }

    private void CancelShopSnapshotSyncLifetime()
    {
        _shopSnapshotSyncLifetimeCancellation?.Cancel();
        _shopSnapshotSyncLifetimeCancellation?.Dispose();
        _shopSnapshotSyncLifetimeCancellation = null;
    }
}
