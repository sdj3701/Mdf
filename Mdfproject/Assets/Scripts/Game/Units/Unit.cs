// Assets/Scripts/Game/Units/Unit.cs

using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using System.Threading.Tasks;
using Fusion;
using MDF.Runtime.Assets;
public class Unit : NetworkBehaviour, IEnemy, IHealth
{
    private AddressableAssetOwner _addressableAssets = new AddressableAssetOwner();

    internal UniTask<T> LoadOwnedAddressableAsync<T>(string key) where T : class
    {
        return AssetLoader.LoadAssetAsync<T>(key, _addressableAssets);
    }

    [Header("참조 데이터")]
    [SerializeField] private UnitData unitData;
    public UnitData Data => unitData;

    [Header("원거리 유닛 참조")]
    public Transform firePoint;

    [Header("현재 상태 (읽기 전용)")]
    [SerializeField]
    private int m_starLevel = 1;
    public int starLevel { get { return m_starLevel; } private set { m_starLevel = value; } }
    
    public bool IsDead { get; private set; } = false;

    // === HP (Networked) ===
    // [수정] 서버/클라이언트 간 HP 동기화를 위한 Networked 속성
    [Networked] public float NetworkedHP { get; set; }
    [Networked] public float NetworkedMaxHP { get; set; }
    
    // === 상태 동기화 (클라이언트 애니메이션/사망 처리용) ===
    [Networked] public NetworkBool NetworkedIsDead { get; set; }
    [Networked] public NetworkBool NetworkedIsAttacking { get; set; }
    [Networked] private int NetworkedStarLevel { get; set; }
    [Networked] private int NetworkedUnitDataKeyHash { get; set; }
    [Networked] private int NetworkedOwnerPlayerId { get; set; }
    [Networked] private NetworkBool NetworkedHasOwnerPlayerId { get; set; }
    [Networked] private SkillActivationType NetworkedSkillActivationType { get; set; }
    [Networked] private NetworkBool NetworkedHasSkillActivationType { get; set; }

    private bool _hasSpawned;
    private int _combatTargetLifecycleGeneration;
    private string _localUnitDataKey = string.Empty;
    private int _localOwnerPlayerId = -1;
    private bool _localHasOwnerPlayerId;
    private bool _hasLocalHealthValues;
    private SkillActivationType _localSkillActivationType;
    private float _localHP;
    private float _localMaxHP;
    
    public bool HasValidNetworkObject => Object != null && Object.IsValid;
    public int CombatTargetLifecycleGeneration => _combatTargetLifecycleGeneration;

    private readonly struct AsyncLifecycleStamp
    {
        public AsyncLifecycleStamp(
            int generation,
            uint networkIdRaw,
            PlayerManager capturedOwner,
            UnitData capturedData,
            AddressableAssetOwner assetOwner)
        {
            Generation = generation;
            NetworkIdRaw = networkIdRaw;
            CapturedOwner = capturedOwner;
            CapturedData = capturedData;
            AssetOwner = assetOwner;
        }

        public int Generation { get; }
        public uint NetworkIdRaw { get; }
        public PlayerManager CapturedOwner { get; }
        public UnitData CapturedData { get; }
        public AddressableAssetOwner AssetOwner { get; }
    }

    private AsyncLifecycleStamp CaptureAsyncLifecycle()
    {
        uint networkIdRaw = Object != null && Object.IsValid ? Object.Id.Raw : 0;
        return new AsyncLifecycleStamp(
            _combatTargetLifecycleGeneration,
            networkIdRaw,
            owner,
            unitData,
            _addressableAssets);
    }

    private bool IsAsyncLifecycleCurrent(AsyncLifecycleStamp stamp)
    {
        if (this == null ||
            stamp.Generation != _combatTargetLifecycleGeneration ||
            !ReferenceEquals(stamp.CapturedOwner, owner) ||
            !ReferenceEquals(stamp.CapturedData, unitData) ||
            !ReferenceEquals(stamp.AssetOwner, _addressableAssets) ||
            stamp.AssetOwner == null ||
            stamp.AssetOwner.IsDisposed)
        {
            return false;
        }

        if (stamp.NetworkIdRaw == 0)
        {
            return Object == null || !Object.IsValid;
        }

        return _hasSpawned &&
               Object != null &&
               Object.IsValid &&
               Object.Id.Raw == stamp.NetworkIdRaw;
    }
    private bool CanReadNetworkedState => _hasSpawned
        && Runner != null
        && Runner.IsRunning
        && Object != null
        && Object.IsValid;
    private bool CanReadNetworkedIdentity() => CanReadNetworkedState;

    public float CurrentHealth => CanReadNetworkedState ? NetworkedHP : _localHP;
    public float MaxHealth => CanReadNetworkedState ? NetworkedMaxHP : _localMaxHP;
    public event System.Action<float, float> OnHealthChanged;
    
    // 로컬 접근용 프로퍼티 (기존 코드 호환성 유지)
    public float currentHP
    {
        get => CanReadNetworkedState ? NetworkedHP : _localHP;
        set
        {
            _hasLocalHealthValues = true;
            _localHP = value;
            if (CanWriteNetworkedHealth())
            {
                NetworkedHP = value;
            }
        }
    }
    public float maxHP
    {
        get => CanReadNetworkedState ? NetworkedMaxHP : _localMaxHP;
        set
        {
            _hasLocalHealthValues = true;
            _localMaxHP = value;
            if (CanWriteNetworkedHealth())
            {
                NetworkedMaxHP = value;
            }
        }
    }

    // === 핵심 스탯 (Networked - 호스트/클라이언트 동기화) ===
    [Networked] private float _networkedAttackDamage { get; set; }
    [Networked] private float _networkedAttackSpeed { get; set; }
    [Networked] private float _networkedAttackRange { get; set; }
    [Networked] private float _networkedDefense { get; set; }
    [Networked] private float _networkedMagicResistance { get; set; }

    // 로컬 폴백 값 (네트워크 미연결 시 사용)
    private float _localAttackDamage;
    private float _localAttackSpeed;
    private float _localAttackRange;
    private float _localDefense;
    private float _localMagicResistance;

    // === 최종 스탯 프로퍼티 (3단계: 버프 적용된 최종값) ===
    public float currentAttackDamage => CanReadNetworkedState ? _networkedAttackDamage : _localAttackDamage;
    public float currentAttackSpeed => CanReadNetworkedState ? _networkedAttackSpeed : _localAttackSpeed;
    public float currentAttackRange => CanReadNetworkedState ? _networkedAttackRange : _localAttackRange;
    public float currentDefense => CanReadNetworkedState ? _networkedDefense : _localDefense;
    public float currentMagicResistance => CanReadNetworkedState ? _networkedMagicResistance : _localMagicResistance;

    #region 3단계 스탯 시스템
    // 1단계: 기본 스탯 (UnitData + 성급 배수)
    public float BaseAttackDamage => unitData != null ? unitData.baseAttackDamage * Mathf.Pow(1.8f, starLevel - 1) : 0f;
    public float BaseAttackSpeed => unitData?.attackSpeed ?? 0f;
    public float BaseAttackRange => unitData?.attackRange ?? 0f;
    public float BaseDefense => unitData?.defense ?? 0f;
    public float BaseMagicResistance => unitData?.magicResistance ?? 0f;
    public float BaseMaxHealth => unitData != null ? unitData.baseHealth * Mathf.Pow(1.8f, starLevel - 1) : 0f;

    // 2단계: 영구 효과 적용 스탯 (증강체 등)
    private float PermanentDamageBonus => owner?.permanentAttackDamagePercent ?? 0f;
    private float PermanentSpeedBonus => owner?.permanentAttackSpeedPercent ?? 0f;
    private float KingBuffFlat(KingBuffStat stat) =>
        owner != null ? owner.GetKingBuffModifier(unitData, stat, KingBuffModifierMode.Flat) : 0f;
    private float KingBuffPercent(KingBuffStat stat) =>
        owner != null ? owner.GetKingBuffModifier(unitData, stat, KingBuffModifierMode.Percent) : 0f;
    public float PermanentAttackDamage =>
        BaseAttackDamage * (1f + PermanentDamageBonus + KingBuffPercent(KingBuffStat.AttackDamage))
        + KingBuffFlat(KingBuffStat.AttackDamage);
    public float PermanentAttackSpeed =>
        BaseAttackSpeed * (1f + PermanentSpeedBonus + KingBuffPercent(KingBuffStat.AttackSpeed))
        + KingBuffFlat(KingBuffStat.AttackSpeed);
    // 공격범위, 방어력, 마저는 현재 영구 버프 없음
    public float PermanentAttackRange =>
        BaseAttackRange * (1f + KingBuffPercent(KingBuffStat.Range))
        + KingBuffFlat(KingBuffStat.Range);
    public float PermanentDefense =>
        BaseDefense * (1f + KingBuffPercent(KingBuffStat.Defense))
        + KingBuffFlat(KingBuffStat.Defense);
    public float PermanentMagicResistance =>
        BaseMagicResistance * (1f + KingBuffPercent(KingBuffStat.MagicResistance))
        + KingBuffFlat(KingBuffStat.MagicResistance);
    public float PermanentMaxHealth =>
        BaseMaxHealth * (1f + KingBuffPercent(KingBuffStat.MaxHealth))
        + KingBuffFlat(KingBuffStat.MaxHealth);
    #endregion
    
    public SkillActivationType currentSkillActivationType
    {
        get => CanReadNetworkedState && NetworkedHasSkillActivationType
            ? NetworkedSkillActivationType
            : _localSkillActivationType;
        private set
        {
            _localSkillActivationType = value;
            if (CanWriteNetworkedIdentity())
            {
                NetworkedSkillActivationType = value;
                NetworkedHasSkillActivationType = true;
            }
        }
    }

    private void InitializeSkillActivationMode(SkillActivationType defaultMode)
    {
        if (CanReadNetworkedState && NetworkedHasSkillActivationType)
        {
            _localSkillActivationType = NetworkedSkillActivationType;
            return;
        }

        currentSkillActivationType = defaultMode;
    }

    public bool TrySetSkillActivationModeAuthoritative(SkillActivationType mode)
    {
        if (mode != SkillActivationType.Automatic && mode != SkillActivationType.Manual)
        {
            return false;
        }

        if (Object != null && Runner != null && Runner.IsRunning && !Object.HasStateAuthority)
        {
            return false;
        }

        if (!DoesHaveSkill())
        {
            return false;
        }

        currentSkillActivationType = mode;
        return true;
    }
    private SkillData _loadedSkillData;
    private ManaController manaController;
    private StatusBarUI statusBarUI;
    private Coroutine attackCoroutine;
    [SerializeField] private List<Monster> blockedMonsters = new List<Monster>();
    private List<Unit> subscribedAllies = new List<Unit>();
    public LayerMask enemyLayerMask;
    private IEnemy targetEnemy;
    private Transform targetTransform;
    [SerializeField] private Animator animator;
    [SerializeField] private string attackTriggerParam = "AttackTrigger";
    [SerializeField] private string skillTriggerParam = "SkillTrigger";
    [SerializeField] private string skillStateTag = "Skill";
    [SerializeField] private bool blockAttacksDuringSkill = true;
    [SerializeField] private float maxAttackAnimationsPerSecond = 3f;
    [SerializeField] private float baseAttackAnimationDuration = 1f;
    private const string AttackStateTag = "Attack";
    private const int AttackPlaybackInactiveFramesBeforeReset = 2;
    private const float MeleeAttackRangeTolerance = 0.1f;
    private const float MeleeBlockDistance = 0.6f;
    private const float MeleeBlockDistanceTolerance = 0.15f;
    private const float BasicTargetSearchInterval = 0.15f;
    private const int InitialFallbackTargetBufferSize = 256;
    private const int MaxFallbackTargetBufferSize = 2048;
    private static Collider[] s_fallbackTargetBuffer = new Collider[InitialFallbackTargetBufferSize];
    private float lastAttackAnimTime = -999f;
    private Coroutine animSpeedResetRoutine;
    private bool attackClipDurationInitialized = false;
    private Coroutine attackClipDetectRoutine;
    private PlayerManager owner;
    private FieldManager _registeredCombatTargetField;
    private CombatTargetHandle _currentTargetHandle;
    private bool _hasFallbackTargetSearchSchedule;
    private float _nextFallbackTargetSearchTime;
    private float _nextProjectileVfxTime;
    private float _cachedProjectileSpeed = -1f;
    private UnitAttackVfxPresenter _attackVfxPresenter;
    private bool _hasPendingAttack;
    private int _pendingAttackVersion;
    private PendingAttack _pendingAttack;
    private bool _isSkillCasting;
    private Coroutine _skillCastingRoutine;
    private ChangeDetector _changeDetector;
    private BuffManager _buffManager;
    private float _lastRecoverFailureLogTime;
    private float _lastMissingUnitDataLogTime;
    private const float AttackRangePadding = 0.1f;
    private const float BlockedMonsterReleasePadding = 0.65f;

    /// <summary>
    /// 이 유닛의 소유자(PlayerManager)에 대한 외부 접근자.
    /// StatusBarUI에서 커맨드 전송 시 playerId를 얻기 위해 사용됩니다.
    /// </summary>
    public PlayerManager Owner => owner;
    public string UnitDataKeyForRoster => GetSnapshotUnitDataKey();
    public int StarLevelForRoster => starLevel > 0
        ? starLevel
        : TryGetNetworkedStarLevel(out int networkedStarLevel) ? networkedStarLevel : 1;

    public int OwnerPlayerIdForRoster
    {
        get
        {
            if (owner != null)
            {
                return owner.playerId;
            }

            return TryGetOwnerPlayerIdForRoster(out int ownerPlayerId) ? ownerPlayerId : -1;
        }
    }

    public void SyncFieldPlacementIdentity(PlayerManager fieldOwner, Vector3Int gridPosition)
    {
        if (fieldOwner != null)
        {
            SetOwnerReference(fieldOwner);
        }

        UpdateLocalNetworkIdentityMirror();

        if (!CanWriteNetworkedIdentity())
        {
            return;
        }

        if (fieldOwner != null && fieldOwner.playerId >= 0)
        {
            NetworkedOwnerPlayerId = fieldOwner.playerId;
            NetworkedHasOwnerPlayerId = true;
        }
    }

    private void SetOwnerReference(PlayerManager newOwner)
    {
        FieldManager newField = newOwner != null ? newOwner.fieldManager : null;
        if (_registeredCombatTargetField != null && _registeredCombatTargetField != newField)
        {
            _registeredCombatTargetField.UnregisterCombatUnit(this);
            _registeredCombatTargetField = null;
        }

        if (owner != null && owner != newOwner && owner.ownedUnits != null)
        {
            owner.ownedUnits.RemoveAll(unit => unit == null || unit == this);
        }

        owner = newOwner;
        if (owner != null && owner.ownedUnits != null && !owner.ownedUnits.Contains(this))
        {
            owner.ownedUnits.Add(this);
        }

        RegisterCombatTarget();
    }

    private void RegisterCombatTarget()
    {
        if (IsDead || gameObject == null || !gameObject.activeInHierarchy)
        {
            UnregisterCombatTarget();
            return;
        }

        FieldManager field = owner != null ? owner.fieldManager : null;
        if (field == null)
        {
            return;
        }

        if (_registeredCombatTargetField != null && _registeredCombatTargetField != field)
        {
            _registeredCombatTargetField.UnregisterCombatUnit(this);
        }

        field.RegisterCombatUnit(this);
        _registeredCombatTargetField = field;
    }

    private void UnregisterCombatTarget()
    {
        if (_registeredCombatTargetField != null)
        {
            _registeredCombatTargetField.UnregisterCombatUnit(this);
            _registeredCombatTargetField = null;
        }
    }

    /// <summary>
    /// 이 유닛이 로컬 플레이어가 소유한 유닛인지 확인합니다.
    /// 멀티플레이어에서 스킬 버튼 등 자신의 유닛에만 표시되어야 하는 UI에 사용합니다.
    /// </summary>
    public bool IsLocalPlayerOwned
    {
        get
        {
            if (owner == null) return false;
            var gm = GameManagers.Instance;
            if (gm == null) return false;
            
            // 방법 1: localPlayer 참조 비교
            if (gm.localPlayer != null && gm.localPlayer == owner) return true;
            
            // 방법 2: playerId 비교 (객체가 다르지만 같은 플레이어일 경우)
            if (gm.localPlayer != null && gm.localPlayer.playerId == owner.playerId) return true;
            
            // 방법 3: InputAuthority 확인 (Fusion 네트워크 권한)
            if (owner.Object != null && owner.Object.HasInputAuthority) return true;
            
            return false;
        }
    }

    public bool IsInCombatPhase => isCombatPhase;
    public bool IsSkillCastingActive => IsSkillCasting();
    public bool HasConfiguredSkill => DoesHaveSkill();
    public SkillData LoadedSkillData => _loadedSkillData;
    public bool CanUseSkillByStatus => _buffManager == null || _buffManager.CanUseSkill;
    public bool IsSkillManaFull => manaController != null && manaController.IsManaFull;
    public float SkillCurrentMana => manaController != null ? manaController.CurrentMana : 0f;
    public float SkillMaxMana => manaController != null ? manaController.MaxMana : 0f;

    public bool TryGetConfiguredSkillKey(out string skillKey)
    {
        skillKey = null;
        if (!DoesHaveSkill())
        {
            return false;
        }

        skillKey = unitData.skillsByStarLevel[starLevel - 1];
        return !string.IsNullOrWhiteSpace(skillKey);
    }

    public bool IsManualOrAiStrategicSkill(SkillData skillData = null)
    {
        SkillData resolvedSkill = skillData != null ? skillData : _loadedSkillData;
        return currentSkillActivationType == SkillActivationType.Manual ||
               resolvedSkill != null && resolvedSkill.canAiUseStrategically;
    }

    public int CountSkillTargets(SkillData skillData = null)
    {
        SkillData resolvedSkill = skillData != null ? skillData : _loadedSkillData;
        if (resolvedSkill == null || resolvedSkill.targetingStrategy == null)
        {
            return 0;
        }

        try
        {
            var targets = resolvedSkill.targetingStrategy.FindTargets(gameObject, transform.position, resolvedSkill.range);
            return targets != null ? targets.Count(target => target != null) : 0;
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[Unit] Skill target query failed for {name}: {ex.GetType().Name}");
            return 0;
        }
    }

    public bool HasSkillTargetsAvailable(SkillData skillData = null)
    {
        SkillData resolvedSkill = skillData != null ? skillData : _loadedSkillData;
        if (resolvedSkill == null || resolvedSkill.targetingStrategy == null || resolvedSkill.effects == null || resolvedSkill.effects.Count == 0)
        {
            return false;
        }

        if (CountSkillTargets(resolvedSkill) > 0)
        {
            return true;
        }

        return resolvedSkill.effects.Any(effect => effect is ZoneEffect);
    }


    private struct PendingAttack
    {
        public NetworkObject Target;
        public IEnemy TargetEnemy;
        public CombatTargetHandle TargetHandle;
        public float Damage;
        public DamageType DamageType;
        public float ProjectileSpeed;
        public bool IsRanged;
        public bool EmitVfx;
        public float SplashRadius;
        public int Version;
    }

    private bool isCombatPhase = false;
    
    /// <summary>
    /// Fusion NetworkBehaviour의 Spawned 콜백.
    /// </summary>
    public override void Spawned()
    {
        base.Spawned();
        if (_addressableAssets == null || _addressableAssets.IsDisposed)
        {
            _addressableAssets = new AddressableAssetOwner();
        }
        _combatTargetLifecycleGeneration++;
        _hasSpawned = true;
        _changeDetector = GetChangeDetector(ChangeDetector.Source.SimulationState);
        TryApplyPendingHealthToNetworked();
        RebindAfterMigration(owner, "Unit.Spawned", false);
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        _combatTargetLifecycleGeneration++;
        _hasSpawned = false;
        _changeDetector = null;
        UnregisterCombatTarget();
        CancelPendingAttack();
        ClearCurrentTarget();
        StopAttackPlaybackState();
        InvalidateAttackPresentationState();
        _addressableAssets?.Dispose();
        base.Despawned(runner, hasState);
    }
    
    /// <summary>
    /// 클라이언트에서 HP 변경을 감지하고 이벤트를 발생시킵니다.
    /// </summary>
    public override void Render()
    {
        if (_changeDetector == null)
        {
            _changeDetector = GetChangeDetector(ChangeDetector.Source.SimulationState);
        }
        
        foreach (var propertyName in _changeDetector.DetectChanges(this))
        {
            if (propertyName == nameof(NetworkedHP) || propertyName == nameof(NetworkedMaxHP))
            {
                OnHealthChanged?.Invoke(NetworkedHP, NetworkedMaxHP);
            }
            else if (propertyName == nameof(NetworkedIsDead))
            {
                HandleNetworkedDeathStateChanged();
            }
            else if (propertyName == nameof(NetworkedIsAttacking))
            {
                HandleNetworkedAttackStateChanged();
            }
        }
    }
    
    private void HandleNetworkedDeathStateChanged()
    {
        if (!CanReadNetworkedState)
        {
            return;
        }

        if (Object != null && Object.HasStateAuthority)
        {
            return;
        }

        if (NetworkedIsDead && !IsDead)
        {
            IsDead = true;
            UnregisterCombatTarget();
            CancelPendingAttack();
            ClearCurrentTarget();
            StopAttackPlaybackState();
            InvalidateAttackPresentationState();
            SetDeathPresentationActive(false);
        }
        else if (!NetworkedIsDead && IsDead)
        {
            Respawn();
        }
    }
    
    private void HandleNetworkedAttackStateChanged()
    {
        if (!gameObject.activeInHierarchy || IsDead) return;
        
        if (animator != null && Object != null && !Object.HasStateAuthority)
        {
            float animRate = GetCappedAttackAnimationRate();
            float minInterval = 0f;
            if (animRate > 0f)
            {
                animator.speed = CalculateAttackAnimationPlaybackSpeed(animRate);
                minInterval = 1f / animRate;
            }

            animator.ResetTrigger(attackTriggerParam);
            animator.SetTrigger(attackTriggerParam);

            if (minInterval > 0f)
            {
                if (animSpeedResetRoutine != null)
                {
                    StopCoroutine(animSpeedResetRoutine);
                }
                animSpeedResetRoutine = StartCoroutine(ResetAnimatorSpeedWhenAttackAnimationFinishes(minInterval));
            }
        }
    }

    private bool CanWriteNetworkedHealth()
    {
        return _hasSpawned
            && Runner != null
            && Runner.IsRunning
            && Object != null
            && Object.IsValid
            && Object.HasStateAuthority;
    }

    private bool CanWriteNetworkedStats()
    {
        return _hasSpawned
            && Runner != null
            && Runner.IsRunning
            && Object != null
            && Object.IsValid
            && Object.HasStateAuthority;
    }

    private void TryApplyPendingHealthToNetworked()
    {
        if (!_hasLocalHealthValues)
        {
            return;
        }

        if (!CanWriteNetworkedHealth())
        {
            return;
        }

        NetworkedMaxHP = _localMaxHP;
        NetworkedHP = _localHP;
    }

    private bool HasStateAuthorityOrNoNetwork()
    {
        // NetworkBehaviour이므로 Object/Runner 프로퍼티 직접 사용
        if (Object == null || Runner == null || !Runner.IsRunning)
        {
            return true;
        }
        return Object.HasStateAuthority;
    }

    private bool CanRunCombatSimulation()
    {
        if (Runner == null)
        {
            return Object == null || !Object.IsValid;
        }

        return Runner.IsRunning && Object != null && Object.IsValid && Object.HasStateAuthority;
    }

    private bool CanWriteNetworkedIdentity()
    {
        return _hasSpawned
            && Runner != null
            && Runner.IsRunning
            && Object != null
            && Object.IsValid
            && Object.HasStateAuthority;
    }

    private static string StripTrailingDigits(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        int end = value.Length;
        while (end > 0 && char.IsDigit(value[end - 1]))
        {
            end--;
        }

        return end > 0 ? value.Substring(0, end) : value;
    }

    private static string RemoveUnitDataPrefix(string value)
    {
        const string prefix = "UnitData_";
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        return value.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase)
            ? value.Substring(prefix.Length)
            : value;
    }

    private void TryRecoverOwnerReference(string context)
    {
        if (owner != null)
        {
            return;
        }

        owner = GetComponentInParent<PlayerManager>();

        if (owner == null && TryGetOwnerPlayerIdForRoster(out int ownerPlayerId) && GameManagers.Instance != null)
        {
            owner = GameManagers.Instance.AllPlayers.FirstOrDefault(pm =>
                pm != null &&
                pm.playerId == ownerPlayerId);
        }

        if (owner == null && Object != null && Object.InputAuthority != PlayerRef.None)
        {
            var allPlayers = FindObjectsOfType<PlayerManager>();
            owner = allPlayers.FirstOrDefault(pm =>
                pm != null &&
                pm.Object != null &&
                pm.Object.InputAuthority == Object.InputAuthority);
        }

        if (owner == null && GameManagers.Instance != null)
        {
            owner = GameManagers.Instance.AllPlayers.FirstOrDefault(pm =>
                pm != null &&
                pm.ownedUnits != null &&
                pm.ownedUnits.Contains(this));
        }

        if (owner != null)
        {
            SetOwnerReference(owner);
        }
    }

    private bool EnsureRuntimeReferences(string context, bool verboseFailure)
    {
        if (owner == null)
        {
            TryRecoverOwnerReference(context);
        }

        if (manaController == null)
        {
            manaController = GetComponent<ManaController>();
        }

        if (_buffManager == null)
        {
            _buffManager = GetComponent<BuffManager>();
        }

        if (animator == null)
        {
            animator = GetComponent<Animator>();
            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>();
            }
        }

        if (unitData == null)
        {
            TryRecoverUnitData(context);
        }

        bool ready = unitData != null;
        if (!ready && verboseFailure && Time.unscaledTime - _lastRecoverFailureLogTime > 0.5f)
        {
            _lastRecoverFailureLogTime = Time.unscaledTime;
            Debug.LogWarning($"[Unit] Runtime 참조 미복구 ({context}) name={name}, owner={(owner != null ? owner.playerId.ToString() : "null")}, hasObject={(Object != null)}, hasRunner={(Runner != null)}");
        }

        return ready;
    }

    private static string NormalizeUnitDataKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return string.Empty;
        }

        return key.Replace("(Clone)", string.Empty).Trim();
    }

    private void SyncNetworkIdentityFromLocalData()
    {
        UpdateLocalNetworkIdentityMirror();

        if (!CanWriteNetworkedIdentity())
        {
            return;
        }

        if (starLevel > 0)
        {
            NetworkedStarLevel = starLevel;
        }

        string key = unitData != null ? NormalizeUnitDataKey(unitData.name) : string.Empty;
        int keyHash = StableUnitDataKeyHash(key);
        if (keyHash != 0)
        {
            NetworkedUnitDataKeyHash = keyHash;
        }

        if (owner != null && owner.playerId >= 0)
        {
            NetworkedOwnerPlayerId = owner.playerId;
            NetworkedHasOwnerPlayerId = true;
        }
    }

    private void UpdateLocalNetworkIdentityMirror()
    {
        _localUnitDataKey = unitData != null ? NormalizeUnitDataKey(unitData.name) : _localUnitDataKey;
        if (owner != null && owner.playerId >= 0)
        {
            _localOwnerPlayerId = owner.playerId;
            _localHasOwnerPlayerId = true;
        }
    }

    private bool TryGetOwnerPlayerIdForRoster(out int ownerPlayerId)
    {
        ownerPlayerId = -1;

        if (CanReadNetworkedIdentity() && NetworkedHasOwnerPlayerId)
        {
            ownerPlayerId = NetworkedOwnerPlayerId;
            if (ownerPlayerId >= 0)
            {
                _localOwnerPlayerId = ownerPlayerId;
                _localHasOwnerPlayerId = true;
                return true;
            }
        }

        if (_localHasOwnerPlayerId && _localOwnerPlayerId >= 0)
        {
            ownerPlayerId = _localOwnerPlayerId;
            return true;
        }

        return false;
    }

    private bool TryGetNetworkedStarLevel(out int networkedStarLevel)
    {
        networkedStarLevel = 0;
        if (!CanReadNetworkedIdentity())
        {
            return false;
        }

        networkedStarLevel = NetworkedStarLevel;
        return networkedStarLevel > 0;
    }

    private string GetSnapshotUnitDataKey()
    {
        if (CanReadNetworkedIdentity())
        {
            int networkKeyHash = NetworkedUnitDataKeyHash;
            if (networkKeyHash != 0 && TryResolveUnitDataKeyByStableHash(networkKeyHash, out string networkKey))
            {
                _localUnitDataKey = networkKey;
                return networkKey;
            }
        }

        if (!string.IsNullOrEmpty(_localUnitDataKey))
        {
            return _localUnitDataKey;
        }

        return unitData != null ? NormalizeUnitDataKey(unitData.name) : string.Empty;
    }

    private static int StableUnitDataKeyHash(string value)
    {
        return StableDataKeyUtility.StableKeyHash(value);
    }

    private static bool TryResolveUnitDataKeyByStableHash(int unitDataKeyHash, out string key)
    {
        key = string.Empty;
        if (unitDataKeyHash == 0)
        {
            return false;
        }

        var lm = LoadManager.Instance;
        if (lm != null && lm.IsReady && TryResolveUnitDataKeyByStableHash(lm.GetAllUnitData(), unitDataKeyHash, out key))
        {
            return true;
        }

        return TryResolveUnitDataKeyByStableHash(Resources.FindObjectsOfTypeAll<UnitData>(), unitDataKeyHash, out key);
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
                key = NormalizeUnitDataKey(data.name);
                return !string.IsNullOrEmpty(key);
            }
        }

        return false;
    }

    private void TryRecoverUnitDataFromNetworkIdentity(string context)
    {
        if (unitData != null)
        {
            return;
        }

        string key = GetSnapshotUnitDataKey();
        if (string.IsNullOrEmpty(key))
        {
            return;
        }

        var lm = LoadManager.Instance;
        if (lm == null || !lm.IsReady)
        {
            return;
        }

        UnitData resolved = lm.GetUnitData(key);
        if (resolved == null)
        {
            string stripped = RemoveUnitDataPrefix(key);
            resolved = lm.GetUnitData(stripped);
            if (resolved == null)
            {
                var all = lm.GetAllUnitData();
                resolved = all.FirstOrDefault(d =>
                    d != null &&
                    (string.Equals(NormalizeUnitDataKey(d.name), key, System.StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(NormalizeUnitDataKey(d.unitName), key, System.StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(NormalizeUnitDataKey(d.name), stripped, System.StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(NormalizeUnitDataKey(d.unitName), stripped, System.StringComparison.OrdinalIgnoreCase)));
            }
        }

        if (resolved != null)
        {
            unitData = resolved;
            if (Time.unscaledTime - _lastMissingUnitDataLogTime > 0.5f)
            {
                _lastMissingUnitDataLogTime = Time.unscaledTime;
                Debug.Log($"[Unit] Network identity로 UnitData 복구 ({context}) name={name}, key={key}, resolved={resolved.name}");
            }
        }
    }

    public bool RebindAfterMigration(PlayerManager expectedOwner, string context, bool verboseFailure = false)
    {
        if (expectedOwner != null)
        {
            SetOwnerReference(expectedOwner);
        }

        UpdateLocalNetworkIdentityMirror();

        if (starLevel <= 0)
        {
            starLevel = TryGetNetworkedStarLevel(out int networkedStarLevel) ? networkedStarLevel : 1;
        }

        TryRecoverUnitDataFromNetworkIdentity(context);
        HandleNetworkedDeathStateChanged();
        bool ready = EnsureRuntimeReferences(context, verboseFailure);

        if (animator == null)
        {
            animator = GetComponent<Animator>();
            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>(true);
            }
        }
        CacheAttackClipDurationFromController();
        EnsureAnimationEventProxy();
        SyncNetworkIdentityFromLocalData();

        return ready;
    }

    private void TryRecoverUnitData(string context)
    {
        TryRecoverOwnerReference(context);

        var lm = LoadManager.Instance;
        if (lm == null || !lm.IsReady)
        {
            return;
        }

        var all = lm.GetAllUnitData();
        if (all == null || all.Count == 0)
        {
            return;
        }

        string rawName = gameObject != null ? gameObject.name : string.Empty;
        string normalized = rawName.Replace("(Clone)", string.Empty).Trim();
        string normalizedWithoutDigits = StripTrailingDigits(normalized);

        var candidates = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        void AddCandidate(string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                candidates.Add(value.Trim());
            }
        }

        AddCandidate(normalized);
        AddCandidate(normalizedWithoutDigits);
        AddCandidate(RemoveUnitDataPrefix(normalized));
        AddCandidate(RemoveUnitDataPrefix(normalizedWithoutDigits));
        AddCandidate($"UnitData_{normalized}");
        AddCandidate($"UnitData_{normalizedWithoutDigits}");

        bool MatchesPrefabKey(UnitData data, string candidate, bool fuzzy)
        {
            if (data?.prefabsByStarLevel == null || string.IsNullOrWhiteSpace(candidate))
            {
                return false;
            }

            string candidateTrimmed = candidate.Trim();
            string candidateNoDigits = StripTrailingDigits(candidateTrimmed);
            for (int i = 0; i < data.prefabsByStarLevel.Length; i++)
            {
                string prefabKey = data.prefabsByStarLevel[i];
                if (string.IsNullOrWhiteSpace(prefabKey))
                {
                    continue;
                }

                string prefabTrimmed = prefabKey.Trim();
                string prefabNoClone = prefabTrimmed.Replace("(Clone)", string.Empty).Trim();
                string prefabNoDigits = StripTrailingDigits(prefabNoClone);

                if (!fuzzy)
                {
                    if (string.Equals(prefabTrimmed, candidateTrimmed, System.StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(prefabNoClone, candidateTrimmed, System.StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(prefabNoDigits, candidateTrimmed, System.StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(prefabNoDigits, candidateNoDigits, System.StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }

                    continue;
                }

                if (prefabTrimmed.IndexOf(candidateTrimmed, System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    prefabNoClone.IndexOf(candidateTrimmed, System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (!string.IsNullOrWhiteSpace(candidateNoDigits) &&
                     prefabNoDigits.IndexOf(candidateNoDigits, System.StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    return true;
                }
            }

            return false;
        }

        UnitData resolved = all.FirstOrDefault(d =>
            d != null && candidates.Any(candidate =>
                string.Equals(d.name, candidate, System.StringComparison.OrdinalIgnoreCase) ||
                string.Equals(RemoveUnitDataPrefix(d.name), candidate, System.StringComparison.OrdinalIgnoreCase) ||
                string.Equals(d.unitName, candidate, System.StringComparison.OrdinalIgnoreCase) ||
                string.Equals(RemoveUnitDataPrefix(d.unitName), candidate, System.StringComparison.OrdinalIgnoreCase) ||
                MatchesPrefabKey(d, candidate, false)));

        if (resolved == null)
        {
            var fuzzy = all.Where(d => d != null && candidates.Any(candidate =>
            {
                if (string.IsNullOrWhiteSpace(candidate))
                {
                    return false;
                }

                bool inName = d.name != null && d.name.IndexOf(candidate, System.StringComparison.OrdinalIgnoreCase) >= 0;
                bool inUnitName = d.unitName != null && d.unitName.IndexOf(candidate, System.StringComparison.OrdinalIgnoreCase) >= 0;
                bool inNameNoPrefix = RemoveUnitDataPrefix(d.name)?.IndexOf(candidate, System.StringComparison.OrdinalIgnoreCase) >= 0;
                bool inUnitNameNoPrefix = RemoveUnitDataPrefix(d.unitName)?.IndexOf(candidate, System.StringComparison.OrdinalIgnoreCase) >= 0;
                bool inPrefabKey = MatchesPrefabKey(d, candidate, true);
                return inName || inUnitName || inNameNoPrefix || inUnitNameNoPrefix || inPrefabKey;
            })).ToList();
            if (fuzzy.Count == 1)
            {
                resolved = fuzzy[0];
            }
        }

        if (resolved != null)
        {
            unitData = resolved;
            Debug.LogWarning($"[Unit] HostMigration 후 UnitData 자동 복구 성공 ({context}) name={name}, resolved={resolved.name}, owner={(owner != null ? owner.playerId.ToString() : "null")}");
        }
        else if (Time.unscaledTime - _lastMissingUnitDataLogTime > 1f)
        {
            _lastMissingUnitDataLogTime = Time.unscaledTime;
            Debug.LogWarning($"[Unit] UnitData 복구 실패 ({context}) name={name}, normalized={normalized}, candidates=[{string.Join(", ", candidates)}]");
        }
    }

    void OnEnable()
    {
        GameEvents.OnGameStateChanged += HandleGameStateChanged;
    }

    private bool IsSkillCasting()
    {
        return blockAttacksDuringSkill && _isSkillCasting;
    }

    private void BeginSkillCasting()
    {
        TriggerSkillAnimation();
        CancelPendingAttack();

        if (!blockAttacksDuringSkill || animator == null || string.IsNullOrEmpty(skillStateTag))
        {
            return;
        }

        _isSkillCasting = true;

        if (_skillCastingRoutine != null)
        {
            StopCoroutine(_skillCastingRoutine);
        }

        _skillCastingRoutine = StartCoroutine(MonitorSkillAnimation());
    }

    private void TriggerSkillAnimation()
    {
        if (animator == null || string.IsNullOrEmpty(skillTriggerParam))
        {
            return;
        }

        if (animSpeedResetRoutine != null)
        {
            StopCoroutine(animSpeedResetRoutine);
            animSpeedResetRoutine = null;
        }

        animator.speed = 1f;

        if (!string.IsNullOrEmpty(attackTriggerParam))
        {
            animator.ResetTrigger(attackTriggerParam);
        }

        animator.ResetTrigger(skillTriggerParam);
        animator.SetTrigger(skillTriggerParam);
    }

    private IEnumerator MonitorSkillAnimation()
    {
        float elapsed = 0f;
        const float enterTimeout = 0.5f;

        while (elapsed < enterTimeout)
        {
            if (animator == null)
            {
                _isSkillCasting = false;
                _skillCastingRoutine = null;
                yield break;
            }

            var st = animator.GetCurrentAnimatorStateInfo(0);
            if (st.IsTag(skillStateTag))
            {
                break;
            }

            elapsed += Time.deltaTime;
            yield return null;
        }

        while (animator != null)
        {
            var st = animator.GetCurrentAnimatorStateInfo(0);
            if (!st.IsTag(skillStateTag))
            {
                break;
            }

            yield return null;
        }

        _isSkillCasting = false;
        _skillCastingRoutine = null;
    }

    private void CancelPendingAttack()
    {
        unchecked
        {
            _pendingAttackVersion++;
            if (_pendingAttackVersion <= 0)
            {
                _pendingAttackVersion = 1;
            }
        }

        _hasPendingAttack = false;
        _pendingAttack = new PendingAttack();
    }

    private int AllocatePendingAttackVersion()
    {
        unchecked
        {
            _pendingAttackVersion++;
            if (_pendingAttackVersion <= 0)
            {
                _pendingAttackVersion = 1;
            }

            return _pendingAttackVersion;
        }
    }

    private void InvalidateAttackPresentationState()
    {
        if (_attackVfxPresenter != null)
        {
            _attackVfxPresenter.InvalidatePendingPlays();
        }
    }

    private void StopAttackPlaybackState()
    {
        if (attackCoroutine != null)
        {
            StopCoroutine(attackCoroutine);
            attackCoroutine = null;
        }

        if (animSpeedResetRoutine != null)
        {
            StopCoroutine(animSpeedResetRoutine);
            animSpeedResetRoutine = null;
        }

        if (animator != null)
        {
            if (!string.IsNullOrEmpty(attackTriggerParam))
            {
                animator.ResetTrigger(attackTriggerParam);
            }

            animator.speed = 1f;
        }
    }

    private bool TryPlayAttackAnimation()
    {
        if (IsDead || !isCombatPhase) return false;
        if (IsSkillCasting()) return false;
        if (animator == null) return false;
        float animRate = GetCappedAttackAnimationRate();
        if (animRate <= 0f) return false;
        float now = Time.time;
        float minInterval = 1f / animRate;
        if (now - lastAttackAnimTime < minInterval) return false;
        lastAttackAnimTime = now;
        animator.speed = CalculateAttackAnimationPlaybackSpeed(animRate);
        animator.ResetTrigger(attackTriggerParam);
        animator.SetTrigger(attackTriggerParam);
        if (animSpeedResetRoutine != null)
        {
            StopCoroutine(animSpeedResetRoutine);
        }
        animSpeedResetRoutine = StartCoroutine(ResetAnimatorSpeedWhenAttackAnimationFinishes(minInterval));
        if (!attackClipDurationInitialized && attackClipDetectRoutine == null)
        {
            attackClipDetectRoutine = StartCoroutine(CaptureAttackClipDuration());
        }
        
        if (CanWriteNetworkedStats())
        {
            NetworkedIsAttacking = !NetworkedIsAttacking;
        }
        return true;
    }

    public float GetCappedAttackAnimationPlaybackSpeed()
    {
        float animRate = GetCappedAttackAnimationRate();
        if (animRate <= 0f)
        {
            return 1f;
        }

        return CalculateAttackAnimationPlaybackSpeed(animRate);
    }

    private float GetCappedAttackAnimationRate()
    {
        return Mathf.Min(currentAttackSpeed, maxAttackAnimationsPerSecond);
    }

    private float CalculateAttackAnimationPlaybackSpeed(float animRate)
    {
        float speed = baseAttackAnimationDuration > 0f ? baseAttackAnimationDuration * animRate : animRate;
        return Mathf.Max(0.01f, speed);
    }

    private IEnumerator ResetAnimatorSpeedWhenAttackAnimationFinishes(float fallbackSeconds)
    {
        float fallbackRemaining = Mathf.Max(0f, fallbackSeconds);
        bool observedAttackPlayback = false;
        int inactiveFrames = 0;

        // StartCoroutine executes immediately until its first yield. Let Animator consume the
        // trigger before inspecting state so a hitch cannot synchronously reset this attack to 1x.
        yield return null;

        while (animator != null)
        {
            bool isInTransition = animator.IsInTransition(0);
            AnimatorStateInfo currentState = animator.GetCurrentAnimatorStateInfo(0);
            AnimatorStateInfo nextState = isInTransition
                ? animator.GetNextAnimatorStateInfo(0)
                : default;
            bool isAttackPlaybackActive = IsAttackPlaybackActive(
                currentState.IsTag(AttackStateTag),
                isInTransition,
                nextState.IsTag(AttackStateTag));

            if (isAttackPlaybackActive)
            {
                observedAttackPlayback = true;
                inactiveFrames = 0;
            }
            else if (observedAttackPlayback)
            {
                // Give a queued trigger time to enter its Attack transition before resetting
                // the global Animator speed. This is especially important on observer clients.
                inactiveFrames++;
                if (inactiveFrames >= AttackPlaybackInactiveFramesBeforeReset)
                {
                    break;
                }
            }
            else
            {
                fallbackRemaining -= Time.deltaTime;
                if (fallbackRemaining <= 0f)
                {
                    break;
                }
            }

            yield return null;
        }

        if (animator != null)
        {
            animator.speed = 1f;
        }
        animSpeedResetRoutine = null;
    }

    private static bool IsAttackPlaybackActive(
        bool currentStateIsAttack,
        bool isInTransition,
        bool nextStateIsAttack)
    {
        return currentStateIsAttack || isInTransition && nextStateIsAttack;
    }

    private IEnumerator CaptureAttackClipDuration()
    {
        float elapsed = 0f;
        float timeout = 1f;
        while (elapsed < timeout)
        {
            if (animator == null) break;
            var st = animator.GetCurrentAnimatorStateInfo(0);
            if (st.IsTag(AttackStateTag))
            {
                var infos = animator.GetCurrentAnimatorClipInfo(0);
                if (infos != null && infos.Length > 0 && infos[0].clip != null)
                {
                    baseAttackAnimationDuration = Mathf.Max(0.01f, infos[0].clip.length);
                    attackClipDurationInitialized = true;
                    break;
                }
            }
            elapsed += Time.deltaTime;
            yield return null;
        }
        attackClipDetectRoutine = null;
    }

    private void CacheAttackClipDurationFromController()
    {
        if (animator == null || animator.runtimeAnimatorController == null) return;
        var clips = animator.runtimeAnimatorController.animationClips;
        if (clips == null || clips.Length == 0) return;

        AnimationClip attackClip = null;
        foreach (var clip in clips)
        {
            if (clip == null) continue;
            if (IsAttackClipName(clip))
            {
                attackClip = clip;
                break;
            }
        }

        if (attackClip == null)
        {
            attackClip = clips[0];
        }

        baseAttackAnimationDuration = Mathf.Max(0.01f, attackClip.length);
        attackClipDurationInitialized = true;
    }

    private static bool IsAttackClipName(AnimationClip clip)
    {
        if (clip == null || string.IsNullOrWhiteSpace(clip.name))
        {
            return false;
        }

        return clip.name.IndexOf("attack", System.StringComparison.OrdinalIgnoreCase) >= 0
            || clip.name.IndexOf("atk", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private void EnsureAnimationEventProxy()
    {
        if (animator == null)
        {
            return;
        }

        // UnitAnimationEventProxy 설정 (Animator가 어디에 있든 설정)
        GameObject animatorObj = animator.gameObject;
        var proxy = animatorObj.GetComponent<UnitAnimationEventProxy>();
        if (proxy == null)
        {
            proxy = animatorObj.AddComponent<UnitAnimationEventProxy>();
        }
        proxy.Initialize(this);
    }

    public bool HasAnimationEventProxy()
    {
        if (animator == null)
        {
            animator = GetComponent<Animator>();
            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>(true);
            }
        }

        if (animator == null)
        {
            return false;
        }

        return animator.GetComponent<UnitAnimationEventProxy>() != null;
    }

    public float GetPermanentAdjustedBaseAttackDamage()
    {
        return PermanentAttackDamage;
    }

    public float GetPermanentAdjustedBaseAttackSpeed()
    {
        return PermanentAttackSpeed;
    }

    public void RefreshPermanentBonuses()
    {
        if (!HasStateAuthorityOrNoNetwork() || unitData == null)
        {
            return;
        }

        float healthRatio = maxHP > 0f ? Mathf.Clamp01(currentHP / maxHP) : 1f;
        maxHP = Mathf.Max(1f, PermanentMaxHealth);
        currentHP = Mathf.Clamp(maxHP * healthRatio, 0f, maxHP);
        SetPersistentNonDamageStats(PermanentAttackRange, PermanentDefense, PermanentMagicResistance);
        OnHealthChanged?.Invoke(currentHP, maxHP);

        var buffManager = GetComponent<BuffManager>();
        if (buffManager != null)
        {
            buffManager.RecalculateStats();
            return;
        }
        float baseDmg = PermanentAttackDamage;
        float baseSpd = PermanentAttackSpeed;
        ApplyStatModifiers(baseDmg, baseSpd);
    }

    void OnDisable()
    {
        GameEvents.OnGameStateChanged -= HandleGameStateChanged;

        CancelPendingAttack();
        ClearCurrentTarget();
        InvalidateAttackPresentationState();
        if (attackCoroutine != null)
        {
            StopCoroutine(attackCoroutine);
            attackCoroutine = null;
        }
        UnsubscribeFromAllies();
        if (animSpeedResetRoutine != null)
        {
            StopCoroutine(animSpeedResetRoutine);
            animSpeedResetRoutine = null;
        }
        if (animator != null)
        {
            animator.speed = 1f;
        }
        if (attackClipDetectRoutine != null)
        {
            StopCoroutine(attackClipDetectRoutine);
            attackClipDetectRoutine = null;
        }
        if (_skillCastingRoutine != null)
        {
            StopCoroutine(_skillCastingRoutine);
            _skillCastingRoutine = null;
        }
        _isSkillCasting = false;
    }

    public void SetStatusBar(StatusBarUI ui)
    {
        this.statusBarUI = ui;
    }

    // Force AI purchase to enable skill auto-use during the next initialization.
    // This flag is consumed during InitializeStats() and then cleared.
    private bool _forceSkillAutoUseOnNextInitialize = false;
    public void SetForceSkillAutoUse(bool enabled)
    {
        _forceSkillAutoUseOnNextInitialize = enabled;
    }

   public async UniTask Initialize(UnitData data, int initialStarLevel, PlayerManager owner)
    {
        this.unitData = data;
        SetOwnerReference(owner);
        // NetworkBehaviour이므로 Object 프로퍼티 직접 사용 (별도 캐싱 불필요)
        if(this.unitData == null)
        {
            Debug.LogError($"UnitData is null for unit {name}");
            return;
        }
        this.starLevel = initialStarLevel;
        SyncNetworkIdentityFromLocalData();
        manaController = GetComponent<ManaController>();
        _buffManager = GetComponent<BuffManager>();
        if (animator == null)
        {
            animator = GetComponent<Animator>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
        }
        
        // 화면 밖 오브젝트의 CPU 부하 감소 (Transform 업데이트만 건너뜀, 상태머신은 계속 실행)
        if (animator != null)
        {
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
        }
        
        CacheAttackClipDurationFromController();
        EnsureAnimationEventProxy();

        // AI가 소유한 유닛인 경우, 스킬 자동 사용을 강제합니다.
        if (owner != null && ComponentRegistry.Has<AIPlayerController>(owner.playerId.ToString()))
        {
            SetForceSkillAutoUse(true);
        }

        // 이벤트 구독/해지는 그대로 둡니다.
        if (manaController != null)
        {
            manaController.OnManaFull -= HandleManaFull;
            manaController.OnManaFull += HandleManaFull;
        }
        AsyncLifecycleStamp lifecycle = CaptureAsyncLifecycle();
        
        // InitializeStats가 비동기 함수가 되었으므로 await로 호출을 기다립니다.
        await InitializeStats();
        if (!IsAsyncLifecycleCurrent(lifecycle))
        {
            return;
        }

        await CacheProjectileSpeedAsync();
        if (!IsAsyncLifecycleCurrent(lifecycle))
        {
            return;
        }
    }

    void Update()
    {
        if (!isCombatPhase || !DoesHaveSkill() || unitData.manaRegenType != ManaRegenType.Passive)
        {
            return;
        }

        if (!HasStateAuthorityOrNoNetwork())
        {
            return;
        }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (MPTestCommandLine.IsGameFlowFrozen)
        {
            return;
        }
#endif

        if (manaController != null)
        {
            manaController.GainManaOverTime(unitData.manaPerSecond);
        }
    }
    
    private void HandleGameStateChanged(GameManagers.GameState newState)
    {
        isCombatPhase = (newState == GameManagers.GameState.Battle1 || newState == GameManagers.GameState.Battle2);

        if (isCombatPhase)
        {
            // Reset attack cooldown; first target acquisition is staggered within one search interval.
            lastAttackAnimTime = -999f;
            RegisterCombatTarget();
            ResetTargetSearchSchedule();
            ClearCurrentTarget();
            blockedMonsters.Clear();
            CancelPendingAttack();
            InvalidateAttackPresentationState();
            EnsureRuntimeReferences("HandleGameStateChanged(BattleEnter)", false);
            
            StartAttackLoop();
            _nextProjectileVfxTime = Time.time;

            // 힐러인 경우, 전투 시작 시 주변 아군 유닛의 체력 변화를 구독합니다.
            if (DoesHaveSkill() && _loadedSkillData != null && _loadedSkillData.name == "Skill_Heal" && currentSkillActivationType == SkillActivationType.Automatic)
            {
                SubscribeToNearbyAllies();
            }
        }
        else
        {
            ResetTargetSearchSchedule();
            StopAttackPlaybackState();
            CancelPendingAttack();
            ClearCurrentTarget();
            InvalidateAttackPresentationState();
            // 전투 종료 시 구독을 해제합니다.
            UnsubscribeFromAllies();

            // 전투 종료 시 체력을 최대로, 마나를 0으로 초기화합니다.
            Heal(maxHP);

            if (manaController != null)
            {
                int maxMana = 0;
                if (DoesHaveSkill() && _loadedSkillData != null)
                {
                    maxMana = _loadedSkillData.manaCost;
                }
                manaController.Initialize(maxMana);
            }

            if (_skillCastingRoutine != null)
            {
                StopCoroutine(_skillCastingRoutine);
                _skillCastingRoutine = null;
            }
            _isSkillCasting = false;
            
            // 폭주 모드 해제
            ClearBerserkMode();
            
            // [안전장치] 준비 시퀀스 진입 시 모든 일시 버프 해제 후 2단계(Permanent) 스탯으로 초기화
            ResetToPermanentStats();
        }
    }

    public async UniTask InitializeStats()
    {
        if (unitData == null) return;
        AsyncLifecycleStamp lifecycle = CaptureAsyncLifecycle();
        UnitData initializingData = unitData;
        int initializingStarLevel = starLevel;
        maxHP = Mathf.Max(1f, PermanentMaxHealth);
        currentHP = maxHP;
        OnHealthChanged?.Invoke(currentHP, maxHP);
        // 스탯 초기화 (Networked 값 설정)
        SetStatsDirect(
            PermanentAttackDamage,
            PermanentAttackSpeed,
            PermanentAttackRange,
            PermanentDefense,
            PermanentMagicResistance
        );

        int newMaxMana = 0;
        
        // --- [핵심 수정 부분] ---
        if (DoesHaveSkill())
        {
            // 주소(string)를 사용해 AssetLoader로 실제 SkillData를 로드합니다.
            string skillKey = initializingData.skillsByStarLevel[initializingStarLevel - 1];
            SkillData loadedSkillData = await LoadOwnedAddressableAsync<SkillData>(skillKey);
            if (!IsAsyncLifecycleCurrent(lifecycle) ||
                !ReferenceEquals(initializingData, unitData) ||
                initializingStarLevel != starLevel)
            {
                return;
            }

            _loadedSkillData = loadedSkillData;

            if (_loadedSkillData != null)
            {
                newMaxMana = _loadedSkillData.manaCost;
                InitializeSkillActivationMode(_loadedSkillData.activationType);
                // If this unit was created by an AI purchase and requested auto-skill, override activation type.
                if (_forceSkillAutoUseOnNextInitialize)
                {
                    currentSkillActivationType = SkillActivationType.Automatic;
                    _forceSkillAutoUseOnNextInitialize = false;
                }
            }
        }
        else
        {
            _loadedSkillData = null;
        }
        // --- [수정 끝] ---

        if (!IsAsyncLifecycleCurrent(lifecycle))
        {
            return;
        }

        manaController?.Initialize(newMaxMana);

        if (statusBarUI != null)
        {
            // InitializeSkillButton도 비동기가 되었으므로 await로 호출합니다.
            await statusBarUI.InitializeSkillButton(this);
            if (!IsAsyncLifecycleCurrent(lifecycle))
            {
                return;
            }
        }
        else
        {
            Debug.LogWarning($"[Unit] {gameObject.name}에 StatusBarUI가 주입되지 않았습니다.", this.gameObject);
        }
    }
    
    private async UniTask CacheProjectileSpeedAsync()
    {
        if (unitData == null || unitData.unitType != UnitType.Ranged)
        {
            return;
        }

        AsyncLifecycleStamp lifecycle = CaptureAsyncLifecycle();
        UnitData projectileData = unitData;

        ProjectileVfxConfig projectileConfig = projectileData.GetProjectileVfxConfig();
        if (projectileConfig != null && projectileConfig.projectileSpeed > 0f)
        {
            _cachedProjectileSpeed = projectileConfig.ResolveProjectileSpeed();
            return;
        }

        // Legacy projectile prefabs still carry Projectile.Speed; pure VFX wrappers store speed in ProjectileVfxConfig.
        string projectileKey = projectileData.GetProjectilePrefabKey();
        if (string.IsNullOrEmpty(projectileKey))
        {
            if (IsAsyncLifecycleCurrent(lifecycle))
            {
                _cachedProjectileSpeed = projectileData.ResolveProjectileSpeed();
            }
            return;
        }

        VfxPoolManager vfxPool = VfxPoolManager.Instance;
        GameObject projectilePrefab = vfxPool != null
            ? await vfxPool.LoadAddressablePrefabAsync(projectileKey)
            : await LoadOwnedAddressableAsync<GameObject>(projectileKey);
        if (!IsAsyncLifecycleCurrent(lifecycle) || !ReferenceEquals(projectileData, unitData))
        {
            return;
        }

        if (projectilePrefab == null)
        {
            _cachedProjectileSpeed = projectileData.ResolveProjectileSpeed();
            return;
        }

        var projectile = projectilePrefab.GetComponent<Projectile>();
        if (projectile != null)
        {
            _cachedProjectileSpeed = projectile.Speed;
            return;
        }

        _cachedProjectileSpeed = projectileData.ResolveProjectileSpeed();
    }

    public async Task Upgrade()
    {
        if (starLevel < 3)
        {
            AsyncLifecycleStamp lifecycle = CaptureAsyncLifecycle();
            starLevel++;
            await InitializeStats();
            if (!IsAsyncLifecycleCurrent(lifecycle))
            {
                return;
            }

            await CacheProjectileSpeedAsync();
            if (!IsAsyncLifecycleCurrent(lifecycle))
            {
                return;
            }
            Debug.Log($"<color=cyan>{unitData.unitName}이(가) {starLevel}성으로 업그레이드되었습니다!</color>");
        }
    }
    
    public async void Respawn()
    {
        bool inactive = gameObject != null && (!gameObject.activeSelf || !gameObject.activeInHierarchy);
        RegisterCombatTarget();
        if (!IsDead && !inactive) return;
        CancelPendingAttack();
        ClearCurrentTarget();
        InvalidateAttackPresentationState();
        IsDead = false;
        
        if (Object != null && Object.HasStateAuthority)
        {
            NetworkedIsDead = false;
        }
        
        blockedMonsters.Clear();
        if (gameObject != null && !gameObject.activeSelf)
        {
            gameObject.SetActive(true);
        }
        SetDeathPresentationActive(true);

        AsyncLifecycleStamp lifecycle = CaptureAsyncLifecycle();
        await InitializeStats();
        if (!IsAsyncLifecycleCurrent(lifecycle))
        {
            return;
        }

        await CacheProjectileSpeedAsync();
        if (!IsAsyncLifecycleCurrent(lifecycle))
        {
            return;
        }

        IsDead = false;
        if (Object != null && Object.HasStateAuthority)
        {
            NetworkedIsDead = false;
        }
        if (gameObject != null && !gameObject.activeSelf)
        {
            gameObject.SetActive(true);
        }
        SetDeathPresentationActive(true);
        RegisterCombatTarget();
        Debug.Log($"<color=green>{unitData.unitName}이(가) 부활했습니다!</color>");
    }

    private void SetDeathPresentationActive(bool active)
    {
        foreach (var renderer in GetComponentsInChildren<Renderer>(true))
        {
            if (renderer != null)
            {
                renderer.enabled = active;
            }
        }

        foreach (var collider in GetComponentsInChildren<Collider>(true))
        {
            if (collider != null)
            {
                collider.enabled = active;
            }
        }

        foreach (var canvas in GetComponentsInChildren<Canvas>(true))
        {
            if (canvas != null)
            {
                canvas.enabled = active;
            }
        }
    }

    public void EnsureAlivePresentationActive()
    {
        if (IsDead)
        {
            return;
        }

        if (gameObject != null && !gameObject.activeSelf)
        {
            gameObject.SetActive(true);
        }

        SetDeathPresentationActive(true);
    }

    private void HandleManaFull()
    {
        if (!isCombatPhase || !DoesHaveSkill()) return;
        if (!HasStateAuthorityOrNoNetwork()) return;
        if (IsSkillCasting()) return;
        
        // --- [핵심 수정 부분] ---
        // 더 이상 SkillData를 직접 접근하거나 로드할 필요가 없습니다.
        // InitializeStats에서 미리 저장해 둔 currentSkillActivationType 값을 사용합니다.
        if (currentSkillActivationType == SkillActivationType.Automatic)
        {
            // 'Skill_Heal' 스킬은 특별한 발동 조건 확인
            if (_loadedSkillData != null && _loadedSkillData.name == "Skill_Heal")
            {
                // 마나가 꽉 찼을 때 즉시 힐이 필요한 아군이 있는지 확인합니다.
                bool needsHeal = subscribedAllies.Any(ally => ally != null && !ally.IsDead && ally.CurrentHealth / ally.MaxHealth < 0.7f);
                if (needsHeal)
                {
                    ActivateSkill();
                }
                // 필요한 아군이 없다면, OnAllyHealthChanged 이벤트에 의해 스킬이 발동되기를 기다립니다.
            }
            else
            {
                // 그 외 스킬은 스킬 범위 내에 적이 있을 때만 발동합니다.
                if (IsEnemyInSkillRange())
                {
                    ActivateSkill();
                }
            }
        }
        // --- [수정 끝] ---
    }

    public async void ActivateSkill()
    {
        if (!isCombatPhase || !DoesHaveSkill()) return;
        if (IsDead) return;
        if (!HasStateAuthorityOrNoNetwork()) return;
        if (IsSkillCasting()) return;
        
        // 상태 효과로 스킬 사용 불가 상태 체크 (침묵, 기절 등)
        if (_buffManager != null && !_buffManager.CanUseSkill) return;

        AsyncLifecycleStamp lifecycle = CaptureAsyncLifecycle();
        
        // 스킬 데이터가 로드되었는지 다시 한번 확인합니다.
        if (_loadedSkillData == null)
        {
            // 만약 로드가 안됐다면, 이 시점에서 다시 로드를 시도할 수도 있습니다.
            string skillKey = unitData.skillsByStarLevel[starLevel - 1];
            SkillData loadedSkillData = await LoadOwnedAddressableAsync<SkillData>(skillKey);
            if (!IsAsyncLifecycleCurrent(lifecycle) ||
                !isCombatPhase ||
                IsDead ||
                !HasStateAuthorityOrNoNetwork() ||
                IsSkillCasting() ||
                (_buffManager != null && !_buffManager.CanUseSkill))
            {
                return;
            }

            _loadedSkillData = loadedSkillData;
            if (_loadedSkillData == null) return; // 그래도 없으면 종료
        }
        
        SkillData currentSkillData = _loadedSkillData; // 로드된 데이터를 사용합니다.

        if (currentSkillData.targetingStrategy == null || currentSkillData.effects.Count == 0)
        {
            Debug.LogError($"{unitData.unitName} ({starLevel}성)의 SkillData 또는 그 내용이 올바르게 설정되지 않았습니다.");
            return;
        }
        
        if (!HasSkillTargetsAvailable(currentSkillData))
        {
            return;
        }

        if (!manaController.IsManaFull) return;

        if (manaController.UseMana(currentSkillData.manaCost))
        {
            BeginSkillCasting();
            Debug.Log($"<color=yellow>{unitData.unitName} 스킬 발동: {currentSkillData.skillName}</color>");

            List<GameObject> targets = currentSkillData.targetingStrategy.FindTargets(this.gameObject, transform.position, currentSkillData.range);

            foreach (var effect in currentSkillData.effects)
            {
                if (effect != null)
                {
                    effect.ApplyEffect(null, this.gameObject, targets, currentSkillData.range, currentSkillData.targetingStrategy);
                }
            }
            
            if (currentSkillData.vfxPrefab != null)
            {
                GameObject vfxInstance = Instantiate(currentSkillData.vfxPrefab, transform.position, Quaternion.identity);
                
                float maxDuration = 0f;
                foreach (var effect in currentSkillData.effects)
                {
                    if (effect is IDurationEffect durationEffect)
                    {
                        if (durationEffect.Duration > maxDuration)
                        {
                            maxDuration = durationEffect.Duration;
                        }
                    }
                }

                float vfxLifetime = (maxDuration > 0) ? maxDuration : 2f;

                if (vfxInstance.TryGetComponent<VFXAutoDestroy>(out var autoDestroy))
                {
                    autoDestroy.Initialize(vfxLifetime);
                }
                else
                {
                    Debug.LogWarning($"VFX 프리팹 '{vfxInstance.name}'에 VFXAutoDestroy.cs 컴포넌트가 없습니다. 자동으로 파괴되지 않습니다.");
                }
            }
        }
    }

    private bool DoesHaveSkill() {
        if (unitData == null) return false;
        if (unitData.skillsByStarLevel == null) return false;
        if (starLevel <= 0) return false;
        var arr = unitData.skillsByStarLevel;
        if (arr.Length < starLevel) return false;
        // 예전 의미와 동일: null만 배제하고 빈 문자열은 허용
        return !string.IsNullOrWhiteSpace(arr[starLevel - 1]);
    }

    private bool IsEnemyInSkillRange()
    {
        if (_loadedSkillData == null) return false;
        // 3D 환경: XZ 평면 기준 구면 탐색
        Collider[] enemiesInRange = Physics.OverlapSphere(transform.position, _loadedSkillData.range, enemyLayerMask);
        // 적이 한 명이라도 있으면 true를 반환합니다.
        return enemiesInRange.Length > 0;
    }

    #region 힐러 스킬 로직
    private void SubscribeToNearbyAllies()
    {
        if (_loadedSkillData?.targetingStrategy == null) return;

        // 자신을 제외한 아군을 찾습니다.
        List<GameObject> alliesGO = _loadedSkillData.targetingStrategy.FindTargets(this.gameObject, transform.position, _loadedSkillData.range)
            .Where(go => go != this.gameObject).ToList();

        foreach (var allyGO in alliesGO)
        {
            if (allyGO.TryGetComponent<Unit>(out var allyUnit))
            {
                if (!subscribedAllies.Contains(allyUnit))
                {
                    subscribedAllies.Add(allyUnit);
                    allyUnit.OnHealthChanged += OnAllyHealthChanged;
                    
                    // 구독 시점에도 체력이 낮은 아군이 있다면 즉시 힐을 시도할 수 있도록 체크합니다.
                    OnAllyHealthChanged(allyUnit.CurrentHealth, allyUnit.MaxHealth);
                }
            }
        }
    }

    private void UnsubscribeFromAllies()
    {
        foreach (var ally in subscribedAllies)
        {
            if (ally != null)
            {
                ally.OnHealthChanged -= OnAllyHealthChanged;
            }
        }
        subscribedAllies.Clear();
    }

    private void OnAllyHealthChanged(float currentHP, float maxHP)
    {
        // 전투 중이 아니거나, 스킬이 없거나, 힐 스킬이 아니거나, 최대 체력이 0 이하면 무시
        if (!isCombatPhase || !DoesHaveSkill() || _loadedSkillData?.name != "Skill_Heal" || maxHP <= 0) return;
        if (!HasStateAuthorityOrNoNetwork()) return;

        // 체력이 70% 미만으로 떨어졌을 때
        if (currentHP / maxHP < 0.7f)
        {
            // 마나가 가득 찼고, 스킬이 자동사용 모드일 때
            if (manaController.IsManaFull && currentSkillActivationType == SkillActivationType.Automatic)
            {
                ActivateSkill();
            }
        }
    }
    #endregion

    #region 공격 로직 (이하 동일)
    public void StartAttackLoop()
    {
        if (attackCoroutine != null)
        {
            StopCoroutine(attackCoroutine);
            attackCoroutine = null;
        }

        if (IsDead)
        {
            CancelPendingAttack();
            return;
        }

        if (!HasStateAuthorityOrNoNetwork())
        {
            CancelPendingAttack();
            ClearCurrentTarget();
            return;
        }

        EnsureRuntimeReferences("StartAttackLoop", false);
        attackCoroutine = StartCoroutine(AttackLoop());
    }

    private IEnumerator AttackLoop()
    {
        float nextAttackTime = 0f;
        bool hadTargetLastFrame = false;
        
        while (isCombatPhase)
        {
            if (!HasStateAuthorityOrNoNetwork())
            {
                CancelPendingAttack();
                ClearCurrentTarget();
                yield break;
            }

            if (!CanRunCombatSimulation())
            {
                CancelPendingAttack();
                ClearCurrentTarget();
                yield break;
            }

            if (IsCombatSuspendedForHostMigration())
            {
                CancelPendingAttack();
                ClearCurrentTarget();
                yield return null;
                continue;
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (MPTestCommandLine.IsGameFlowFrozen)
            {
                yield return null;
                continue;
            }
#endif
            if (IsDead)
            {
                CancelPendingAttack();
                ClearCurrentTarget();
                yield break;
            }

            if (!EnsureRuntimeReferences("AttackLoop", true))
            {
                yield return null;
                continue;
            }

            bool isMelee = unitData.unitType == UnitType.Melee;

            if (currentAttackSpeed <= 0)
            {
                yield return null;
                continue;
            }

            if (IsSkillCasting())
            {
                yield return null;
                continue;
            }
            
            // 상태 효과로 공격 불가 상태 체크 (기절 등)
            if (_buffManager != null && !_buffManager.CanAttack)
            {
                yield return null;
                continue;
            }

            bool hasTarget;
            
            if (isMelee)
            {
                PruneBlockedMonsters();
                
                if (blockedMonsters.Count > 0)
                {
                    var firstBlocked = blockedMonsters[0];
                    if (targetEnemy != firstBlocked || !_currentTargetHandle.IsCurrentLifecycle(firstBlocked))
                    {
                        SetCurrentTarget(firstBlocked, CombatTargetHandle.Capture(firstBlocked));
                    }
                    hasTarget = IsCurrentTargetValidForAttack(false);
                }
                else
                {
                    hasTarget = TryRetainOrAcquireTarget(isRanged: false);
                }
            }
            else
            {
                hasTarget = TryRetainOrAcquireTarget(isRanged: true);
            }

            if (!hasTarget)
            {
                ClearCurrentTarget();
            }

            if (hasTarget)
            {
                // [Fix] 타겟이 없다가 새로 발견되었을 때 즉시 공격 가능하도록 쿨타임 리셋
                if (!hadTargetLastFrame)
                {
                    nextAttackTime = Time.time;
                }
                
                if (Time.time >= nextAttackTime)
                {
                    Attack();
                    nextAttackTime = Time.time + 1f / currentAttackSpeed;
                }
            }
            
            hadTargetLastFrame = hasTarget;

            // 매 프레임마다 적 탐색/쿨다운 확인
            yield return null;
        }
    }

    private bool TryRetainOrAcquireTarget(bool isRanged)
    {
        if (IsCurrentTargetValidForAttack(isRanged))
        {
            return true;
        }

        ClearCurrentTarget();
        if (!ShouldSearchForTarget())
        {
            return false;
        }

        if (isRanged)
        {
            FindNearestEnemy();
        }
        else
        {
            FindNearestGroundMonster();
        }

        return IsCurrentTargetValidForAttack(isRanged);
    }

    private bool ShouldSearchForTarget()
    {
        if (TryGetCombatTargetRegistry(out FieldCombatTargetRegistry registry))
        {
            return registry.TryBeginSearch(this, Time.time, BasicTargetSearchInterval);
        }

        if (!_hasFallbackTargetSearchSchedule)
        {
            _hasFallbackTargetSearchSchedule = true;
            _nextFallbackTargetSearchTime = Time.time +
                FieldCombatTargetRegistry.ComputeInitialSearchDelay(GetInstanceID(), BasicTargetSearchInterval);
            return false;
        }

        if (Time.time + Mathf.Epsilon < _nextFallbackTargetSearchTime)
        {
            return false;
        }

        _nextFallbackTargetSearchTime = Time.time + BasicTargetSearchInterval;
        return true;
    }

    private void ResetTargetSearchSchedule()
    {
        if (TryGetCombatTargetRegistry(out FieldCombatTargetRegistry registry))
        {
            registry.ResetSearchSchedule(this);
        }

        _hasFallbackTargetSearchSchedule = false;
        _nextFallbackTargetSearchTime = 0f;
    }

    private bool TryGetCombatTargetRegistry(out FieldCombatTargetRegistry registry)
    {
        registry = null;
        FieldManager field = owner != null ? owner.fieldManager : null;
        return field != null && field.TryGetCombatTargetRegistry(out registry);
    }

    private void FindNearestEnemy()
    {
        if (TryGetCombatTargetRegistry(out FieldCombatTargetRegistry registry))
        {
            FieldCombatTargetRegistry.QueryStatus status = registry.FindNearestMonster(
                this,
                currentAttackRange,
                groundOnly: false,
                enemyLayerMask,
                out CombatTargetHandle handle);
            if (status == FieldCombatTargetRegistry.QueryStatus.Found && handle.Actor is Monster monster)
            {
                SetCurrentTarget(monster, handle);
            }
            else if (status == FieldCombatTargetRegistry.QueryStatus.NoTarget)
            {
                ClearCurrentTarget();
            }

            if (status != FieldCombatTargetRegistry.QueryStatus.Unavailable)
            {
                return;
            }

            ClearCurrentTarget();
            if (!CanUsePhysicsTargetFallback())
            {
                return;
            }
        }
        else if (!CanUsePhysicsTargetFallback())
        {
            ClearCurrentTarget();
            return;
        }

        int count = OverlapTargetsNonAlloc(transform.position, currentAttackRange, enemyLayerMask);
        float closestDistanceSqr = float.MaxValue;
        Monster nearestMonster = null;
        Collider nearestCollider = null;

        for (int i = 0; i < count; i++)
        {
            Collider enemyCollider = s_fallbackTargetBuffer[i];
            Monster monster = enemyCollider != null ? enemyCollider.GetComponentInParent<Monster>() : null;
            if (monster == null || monster.Data == null || monster.CurrentHealth <= 0f ||
                !IsMonsterOnSameField(monster))
            {
                continue;
            }

            float distanceSqr = (transform.position - enemyCollider.transform.position).sqrMagnitude;
            if (distanceSqr < closestDistanceSqr)
            {
                closestDistanceSqr = distanceSqr;
                nearestMonster = monster;
                nearestCollider = enemyCollider;
            }
        }

        SetCurrentTarget(
            nearestMonster,
            nearestMonster != null ? CombatTargetHandle.Capture(nearestMonster, nearestCollider) : default);
    }
    
    private void FindNearestGroundMonster()
    {
        if (TryGetCombatTargetRegistry(out FieldCombatTargetRegistry registry))
        {
            FieldCombatTargetRegistry.QueryStatus status = registry.FindNearestMonster(
                this,
                currentAttackRange,
                groundOnly: true,
                enemyLayerMask,
                out CombatTargetHandle handle);
            if (status == FieldCombatTargetRegistry.QueryStatus.Found && handle.Actor is Monster monster)
            {
                SetCurrentTarget(monster, handle);
            }
            else if (status == FieldCombatTargetRegistry.QueryStatus.NoTarget)
            {
                ClearCurrentTarget();
            }

            if (status != FieldCombatTargetRegistry.QueryStatus.Unavailable)
            {
                return;
            }

            ClearCurrentTarget();
            if (!CanUsePhysicsTargetFallback())
            {
                return;
            }
        }
        else if (!CanUsePhysicsTargetFallback())
        {
            ClearCurrentTarget();
            return;
        }

        int count = OverlapTargetsNonAlloc(transform.position, currentAttackRange, enemyLayerMask);
        float closestDistanceSqr = float.MaxValue;
        Monster nearestMonster = null;
        Collider nearestCollider = null;
        
        for (int i = 0; i < count; i++)
        {
            Collider col = s_fallbackTargetBuffer[i];
            Monster monster = col != null ? col.GetComponentInParent<Monster>() : null;
            if (!IsMeleeMonsterAttackable(monster))
            {
                continue;
            }

            Vector3 targetPoint = col.ClosestPoint(transform.position);
            float distanceSqr = FlatDistanceSqr(transform.position, targetPoint);
            if (distanceSqr < closestDistanceSqr)
            {
                closestDistanceSqr = distanceSqr;
                nearestMonster = monster;
                nearestCollider = col;
            }
        }

        SetCurrentTarget(
            nearestMonster,
            nearestMonster != null ? CombatTargetHandle.Capture(nearestMonster, nearestCollider) : default);
    }

    private static int OverlapTargetsNonAlloc(Vector3 position, float range, LayerMask layerMask)
    {
        int count;
        while (true)
        {
            count = Physics.OverlapSphereNonAlloc(position, range, s_fallbackTargetBuffer, layerMask);
            if (count < s_fallbackTargetBuffer.Length || s_fallbackTargetBuffer.Length >= MaxFallbackTargetBufferSize)
            {
                return count;
            }

            int nextSize = Mathf.Min(s_fallbackTargetBuffer.Length * 2, MaxFallbackTargetBufferSize);
            System.Array.Resize(ref s_fallbackTargetBuffer, nextSize);
        }
    }

    private bool CanUsePhysicsTargetFallback()
    {
        FieldManager field = owner != null ? owner.fieldManager : null;
        NetworkRunner fieldRunner = field != null && field.playerManager != null
            ? field.playerManager.Runner
            : null;
        return !IsCombatSuspendedForHostMigration() &&
               fieldRunner == null &&
               Runner == null &&
               (Object == null || !Object.IsValid);
    }

    private static bool IsCombatSuspendedForHostMigration()
    {
        return HostMigrationHandler.Instance != null && HostMigrationHandler.Instance.IsMigrating;
    }

    private void PruneBlockedMonsters()
    {
        for (int i = blockedMonsters.Count - 1; i >= 0; i--)
        {
            var monster = blockedMonsters[i];
            if (!IsMeleeMonsterAttackable(monster, BlockedMonsterReleasePadding))
            {
                blockedMonsters.RemoveAt(i);
                if (monster != null && monster.IsBlocked() && HasStateAuthorityOrNoNetwork())
                {
                    monster.Unblock();
                }
            }
        }
    }

    private bool IsCurrentTargetValidForAttack(bool isRanged)
    {
        if (targetEnemy == null || targetTransform == null)
        {
            return false;
        }

        if (targetEnemy is IHealth healthTarget && healthTarget.CurrentHealth <= 0f)
        {
            return false;
        }

        if (targetEnemy is Monster targetMonster)
        {
            if (!_currentTargetHandle.IsCurrentLifecycle(targetMonster) || !IsMonsterOnSameField(targetMonster))
            {
                return false;
            }
        }

        if (!isRanged)
        {
            var monster = targetTransform.GetComponentInParent<Monster>();
            return IsMeleeMonsterAttackable(monster);
        }

        return IsTargetWithinAttackRange(targetTransform, AttackRangePadding);
    }

    private bool IsValidGroundMeleeMonster(Monster monster)
    {
        return monster != null &&
               monster.Data != null &&
               monster.Data.monsterType != MonsterType.Flying &&
               monster.currentHP > 0f;
    }

    private bool IsTargetWithinAttackRange(Transform target, float padding)
    {
        if (target == null)
        {
            return false;
        }

        float allowedRange = Mathf.Max(0f, currentAttackRange) + Mathf.Max(0f, padding);
        Vector3 targetPoint = target == targetTransform && _currentTargetHandle.IsCurrentLifecycle()
            ? _currentTargetHandle.ClosestPoint(transform.position)
            : GetClosestTargetPoint(target, transform.position);
        return FlatDistanceSqr(transform.position, targetPoint) <= allowedRange * allowedRange;
    }

    private static Vector3 GetClosestTargetPoint(Transform target, Vector3 from)
    {
        var targetCollider = target.GetComponentInChildren<Collider>();
        return targetCollider != null ? targetCollider.ClosestPoint(from) : target.position;
    }

    private static float FlatDistanceSqr(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return dx * dx + dz * dz;
    }

    private void SetCurrentTarget(IEnemy enemy, CombatTargetHandle handle)
    {
        if (enemy == null)
        {
            ClearCurrentTarget();
            return;
        }

        targetEnemy = enemy;
        if (handle.Actor == null && enemy is NetworkBehaviour networkTarget)
        {
            handle = CombatTargetHandle.Capture(networkTarget);
        }

        _currentTargetHandle = handle;
        targetTransform = handle.Transform != null
            ? handle.Transform
            : (enemy as MonoBehaviour)?.transform;
    }

    private void ClearCurrentTarget()
    {
        targetEnemy = null;
        targetTransform = null;
        _currentTargetHandle = default;
    }

    private bool CanBlockMonster(Monster monster)
    {
        if (Data == null || monster == null || monster.Data == null)
        {
            return false;
        }

        if (blockedMonsters.Contains(monster) || monster.IsBlocked() ||
            monster.HasTrait(MonsterTraits.Unblockable) ||
            monster.Data.monsterType == MonsterType.Flying ||
            monster.currentHP <= 0 ||
            Data.blockCount <= 0 || blockedMonsters.Count >= Data.blockCount)
        {
            return false;
        }

        return IsMonsterOnSameField(monster) && IsTargetWithinHorizontalRange(monster.transform, MeleeBlockDistance + MeleeBlockDistanceTolerance);
    }

    private bool IsMeleeMonsterAttackable(Monster monster)
    {
        return IsMeleeMonsterAttackable(monster, AttackRangePadding);
    }

    private bool IsMeleeMonsterAttackable(Monster monster, float padding)
    {
        if (monster == null || monster.Data == null || monster.currentHP <= 0)
        {
            return false;
        }

        if (monster.Data.monsterType == MonsterType.Flying)
        {
            return false;
        }

        return IsMonsterOnSameField(monster) &&
               IsTargetWithinAttackRange(monster.transform, padding);
    }

    private bool IsMonsterOnSameField(Monster monster)
    {
        if (monster == null)
        {
            return false;
        }

        int unitOwnerId = OwnerPlayerIdForRoster;
        int monsterOwnerId = monster.SnapshotOwnerPlayerId;
        if (unitOwnerId < 0 || monsterOwnerId < 0)
        {
            return !Application.isPlaying;
        }

        return monsterOwnerId == unitOwnerId;
    }

    private bool IsTargetWithinHorizontalRange(Transform target, float allowedRange)
    {
        if (target == null || allowedRange < 0f)
        {
            return false;
        }

        Vector3 delta = target.position - transform.position;
        delta.y = 0f;
        return delta.sqrMagnitude <= allowedRange * allowedRange;
    }

    private void Attack()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (MPTestCommandLine.IsGameFlowFrozen)
        {
            return;
        }
#endif
        if (!CanRunCombatSimulation())
        {
            return;
        }

        if (IsCombatSuspendedForHostMigration())
        {
            CancelPendingAttack();
            return;
        }

        if (IsDead || !isCombatPhase || unitData == null)
        {
            CancelPendingAttack();
            return;
        }

        if (IsSkillCasting())
        {
            return;
        }
        
        bool isRanged = unitData.unitType == UnitType.Ranged;
        if (!IsCurrentTargetValidForAttack(isRanged))
        {
            ClearCurrentTarget();
            return;
        }
        
        // Spawn-time trigger jitter must not let melee units keep attacking targets outside their reach.
        if (isRanged)
        {
            if (targetEnemy == null || targetTransform == null || 
                !IsTargetWithinAttackRange(targetTransform, AttackRangePadding))
            {
                ClearCurrentTarget();
                return;
            }
        }
        else
        {
            // 근접 유닛: 타겟이 없거나 사거리 밖이면 리턴
            if (!IsMeleeMonsterAttackable(targetEnemy as Monster))
            {
                ClearCurrentTarget();
                return;
            }
        }
        bool playedAnim = TryPlayAttackAnimation();
        bool canSyncToAnimation = playedAnim && !_hasPendingAttack;
        bool canSyncMelee = canSyncToAnimation && currentAttackSpeed <= maxAttackAnimationsPerSecond + 1e-4f;
        bool canSyncRanged = canSyncToAnimation && ShouldEmitProjectileVfx();

        // NetworkBehaviour이므로 Object 프로퍼티 직접 사용
        bool hasAuthority = Object == null || Object.HasStateAuthority;
        if (hasAuthority)
        {
            var scheduler = CombatScheduler.Instance;
            bool schedulerReady = scheduler != null && scheduler.Runner != null && scheduler.Runner.IsRunning;
            var targetNo = targetTransform.GetComponentInParent<NetworkObject>();
            if (!isRanged && playedAnim)
            {
                if (schedulerReady && targetNo != null)
                {
                    scheduler.ScheduleBasicAttackVfx(Object, targetNo, ResolveBasicAttackVfxSpawnDelaySeconds());
                }
                else if (targetNo != null)
                {
                    PlayBasicAttackVfxFromCombatEvent(targetNo);
                }
            }

            if (isRanged)
            {
                if (schedulerReady && targetNo != null)
                {
                    if (canSyncRanged)
                    {
                        Vector3 firePos = firePoint != null ? firePoint.position : transform.position;
                        float splashRadius = unitData.attackTargetType == AttackTargetType.Splash ? unitData.splashRadius : 0f;
                        float fireDelaySeconds = ResolveProjectileFireDelaySeconds();
                        scheduler.ScheduleHit(Object, targetNo, firePos, currentAttackDamage, unitData.damageType,
                            true, true, _cachedProjectileSpeed, splashRadius, enemyLayerMask, fireDelaySeconds);
                    }
                    else
                    {
                        Vector3 firePos = firePoint != null ? firePoint.position : transform.position;
                        // 원거리 스플래시 공격: splashRadius와 enemyLayerMask 전달
                        float splashRadius = unitData.attackTargetType == AttackTargetType.Splash ? unitData.splashRadius : 0f;
                        scheduler.ScheduleHit(Object, targetNo, firePos, currentAttackDamage, unitData.damageType,
                            true, false, _cachedProjectileSpeed, splashRadius, enemyLayerMask);
                    }
                }
                else if (targetEnemy != null)
                {
                    targetEnemy.TakeDamage(currentAttackDamage, unitData.damageType);
                }
            }
            else
            {
                // 근접 유닛 스플래시 공격: 저지 중인 모든 몬스터에게 데미지
                if (unitData.attackTargetType == AttackTargetType.Splash && blockedMonsters.Count > 0)
                {
                    // 스플래시 공격: 저지 중인 모든 몬스터에게 동시에 데미지
                    foreach (var monster in blockedMonsters.ToList())
                    {
                        if (IsMeleeMonsterAttackable(monster))
                        {
                            if (schedulerReady)
                            {
                                var monsterNo = monster.GetComponent<NetworkObject>();
                                if (monsterNo != null)
                                {
                                    Vector3 firePos = firePoint != null ? firePoint.position : transform.position;
                                    scheduler.ScheduleHit(Object, monsterNo, firePos, currentAttackDamage, unitData.damageType,
                                        false, false, 0f);
                                }
                                else
                                {
                                    monster.TakeDamage(currentAttackDamage, unitData.damageType);
                                }
                            }
                            else
                            {
                                monster.TakeDamage(currentAttackDamage, unitData.damageType);
                            }
                        }
                    }
                }
                else
                {
                    // 근접 유닛 단일 공격: 첫 번째 저지 몬스터 공격
                    if (canSyncMelee)
                    {
                        int attackVersion = AllocatePendingAttackVersion();
                        _pendingAttack = new PendingAttack
                        {
                            Target = targetNo,
                            TargetEnemy = targetEnemy,
                            TargetHandle = _currentTargetHandle,
                            Damage = currentAttackDamage,
                            DamageType = unitData.damageType,
                            ProjectileSpeed = 0f,
                            IsRanged = false,
                            EmitVfx = false,
                            Version = attackVersion
                        };
                        _hasPendingAttack = true;
                    }
                    else
                    {
                        if (schedulerReady && targetNo != null)
                        {

                            Vector3 firePos = firePoint != null ? firePoint.position : transform.position;
                            scheduler.ScheduleHit(Object, targetNo, firePos, currentAttackDamage, unitData.damageType,
                                false, false, 0f);
                        }
                        else if (targetEnemy != null)
                        {

                            targetEnemy.TakeDamage(currentAttackDamage, unitData.damageType);
                        }
                    }
                }
            }
        }

        if (DoesHaveSkill() && unitData.manaRegenType == ManaRegenType.OnAttack && HasStateAuthorityOrNoNetwork())
        {
            manaController.GainMana(unitData.manaOnAttack);
        }
    }

    private bool ShouldEmitProjectileVfx()
    {
        if (maxAttackAnimationsPerSecond <= 0f)
        {
            return false;
        }

        if (currentAttackSpeed <= maxAttackAnimationsPerSecond + 1e-4f)
        {
            return true;
        }

        if (Time.time >= _nextProjectileVfxTime)
        {
            _nextProjectileVfxTime = Time.time + 1f / maxAttackAnimationsPerSecond;
            return true;
        }

        return false;
    }

    private float ResolveProjectileFireDelaySeconds()
    {
        ProjectileVfxConfig config = unitData != null ? unitData.GetProjectileVfxConfig() : null;
        float normalizedTime = config != null ? config.ResolveProjectileSpawnNormalizedTime() : 0f;
        if (normalizedTime <= 0f)
        {
            return 0f;
        }

        float animRate = GetCappedAttackAnimationRate();
        if (animRate <= 0f)
        {
            return 0f;
        }

        return normalizedTime / animRate;
    }

    private float ResolveBasicAttackVfxSpawnDelaySeconds()
    {
        BasicAttackVfxConfig config = unitData != null ? unitData.GetBasicAttackVfxConfig(starLevel) : null;
        float normalizedTime = config != null ? Mathf.Clamp(config.spawnNormalizedTime, 0f, 0.95f) : 0f;
        if (normalizedTime <= 0f)
        {
            return 0f;
        }

        float animRate = GetCappedAttackAnimationRate();
        if (animRate <= 0f)
        {
            return 0f;
        }

        return normalizedTime / animRate;
    }

    public bool CanPlayBasicAttackVfxForTarget(Monster targetMonster)
    {
        if (IsDead || !isCombatPhase || unitData == null || unitData.unitType != UnitType.Melee)
        {
            return false;
        }

        return IsMeleeMonsterAttackable(targetMonster);
    }

    public void PlayBasicAttackVfxFromCombatEvent(NetworkObject targetObject)
    {
        Monster targetMonster = targetObject != null ? targetObject.GetComponent<Monster>() : null;
        if (!CanPlayBasicAttackVfxForTarget(targetMonster))
        {
            return;
        }

        if (_attackVfxPresenter == null)
        {
            _attackVfxPresenter = GetComponent<UnitAttackVfxPresenter>();
            if (_attackVfxPresenter == null)
            {
                _attackVfxPresenter = gameObject.AddComponent<UnitAttackVfxPresenter>();
            }
        }

        _attackVfxPresenter.PlayBasicAttack(this, targetMonster.transform);
    }

    public void AnimEvent_AttackImpact()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (MPTestCommandLine.IsGameFlowFrozen)
        {
            return;
        }
#endif
        if (!_hasPendingAttack)
        {
            return;
        }

        TryExecutePendingAttack(_pendingAttack.Version);
    }

    private void TryExecutePendingAttack(int attackVersion)
    {
        if (!_hasPendingAttack || _pendingAttack.Version != attackVersion)
        {
            return;
        }

        if (!CanRunCombatSimulation() || IsCombatSuspendedForHostMigration() ||
            IsDead || !isCombatPhase || unitData == null)
        {
            CancelPendingAttack();
            return;
        }

        MonoBehaviour pendingTarget = _pendingAttack.TargetEnemy as MonoBehaviour;
        if (pendingTarget == null || !_pendingAttack.TargetHandle.IsCurrentLifecycle(pendingTarget))
        {
            CancelPendingAttack();
            return;
        }

        if (!_pendingAttack.IsRanged && !IsMeleeMonsterAttackable(_pendingAttack.TargetEnemy as Monster))
        {
            CancelPendingAttack();
            return;
        }

        // NetworkBehaviour이므로 Object 프로퍼티 직접 사용
        bool hasAuthority = Object == null || Object.HasStateAuthority;
        if (!hasAuthority)
        {
            CancelPendingAttack();
            return;
        }

        if (!_pendingAttack.IsRanged && !IsPendingMeleeAttackStillValid())
        {
            CancelPendingAttack();
            return;
        }

        var scheduler = CombatScheduler.Instance;
        if (scheduler != null && scheduler.Runner != null && scheduler.Runner.IsRunning && _pendingAttack.Target != null)
        {
            Vector3 firePos = firePoint != null ? firePoint.position : transform.position;
            scheduler.ScheduleHit(Object, _pendingAttack.Target, firePos, _pendingAttack.Damage,
                _pendingAttack.DamageType, _pendingAttack.IsRanged, _pendingAttack.EmitVfx, _pendingAttack.ProjectileSpeed,
                _pendingAttack.SplashRadius, enemyLayerMask);
        }
        else if (_pendingAttack.TargetEnemy != null)
        {
            _pendingAttack.TargetEnemy.TakeDamage(_pendingAttack.Damage, _pendingAttack.DamageType);
        }

        CancelPendingAttack();
    }

    private bool IsPendingMeleeAttackStillValid()
    {
        var targetMono = _pendingAttack.TargetEnemy as MonoBehaviour;
        if (targetMono == null)
        {
            return false;
        }

        if (_pendingAttack.TargetEnemy is IHealth healthTarget && healthTarget.CurrentHealth <= 0f)
        {
            return false;
        }

        var monster = targetMono.GetComponentInParent<Monster>();
        if (monster != null)
        {
            return IsMeleeMonsterAttackable(monster);
        }

        return IsTargetWithinAttackRange(targetMono.transform, AttackRangePadding);
    }

    public void AnimEvent_SkillEnd()
    {
        if (!blockAttacksDuringSkill)
        {
            return;
        }

        _isSkillCasting = false;
        if (_skillCastingRoutine != null)
        {
            StopCoroutine(_skillCastingRoutine);
            _skillCastingRoutine = null;
        }
    }
    #endregion

    #region 저지, 스킬 UI, IEnemy 구현 등
    private void OnTriggerEnter(Collider other)
    {
        if (other.TryGetComponent<Monster>(out var monster))
        {
            if (!CanBlockMonster(monster))
            {
                return;
            }

            blockedMonsters.Add(monster);
            monster.Block(this);
        }
    }

    /// <summary>
    /// 현재 저지 수가 최대치에 도달했는지 확인합니다.
    /// </summary>
    public bool IsBlockingFull()
    {
        if (Data == null) return false;
        return blockedMonsters.Count >= Data.blockCount;
    }

    /// <summary>
    /// Monster에서 호출하여 저지를 시도합니다. OnTriggerEnter 누락 시 백업용.
    /// </summary>
    public bool TryBlockMonster(Monster monster)
    {
        if (!CanBlockMonster(monster))
        {
            return false;
        }

        blockedMonsters.Add(monster);
        monster.Block(this);
        return true;
    }

    public void ReleaseBlockedMonster(Monster monster)
    {
        if (blockedMonsters.Contains(monster))
        {
            blockedMonsters.Remove(monster);
        }
    }

    private void OnDestroy()
    {
        _addressableAssets?.Dispose();
        GameEvents.OnGameStateChanged -= HandleGameStateChanged;
        UnregisterCombatTarget();
        CancelPendingAttack();
        ClearCurrentTarget();
        InvalidateAttackPresentationState();

        if (owner != null && owner.fieldManager != null)
        {
            owner.fieldManager.UnitDied(this);
        }

        // [수정됨] 오브젝트 파괴 시 이벤트 구독을 확실히 해제합니다.
        if (manaController != null)
        {
            manaController.OnManaFull -= HandleManaFull;
        }
    }

    public void TakeDamage(float baseDamage, DamageType damageType)
    {
        // 서버에서만 HP 수정 (클라이언트는 Networked 속성 동기화로 반영)
        if (!HasStateAuthorityOrNoNetwork()) return;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (MPTestCommandLine.IsGameFlowFrozen)
        {
            return;
        }
#endif
        if (unitData == null || IsDead) return;
        int finalDamage = DamageCalculator.CalculateDamage(baseDamage, damageType, currentDefense, currentMagicResistance);
        currentHP -= finalDamage;
        // Render()에서 ChangeDetector가 OnHealthChanged 이벤트를 발생시킴
        if (currentHP <= 0)
        {
            Die();
        }
    }
    private void Die()
    {
        if (IsDead) return;
        IsDead = true;
        UnregisterCombatTarget();
        CancelPendingAttack();
        ClearCurrentTarget();
        StopAttackPlaybackState();
        InvalidateAttackPresentationState();
        
        if (Object != null && Object.HasStateAuthority)
        {
            NetworkedIsDead = true;
        }

        _buffManager?.ClearAllStatusEffects();
        
        foreach (var monster in blockedMonsters)
        {
            if (monster != null)
            {
                monster.Unblock();
            }
        }
        blockedMonsters.Clear();
        
        SetDeathPresentationActive(false);
        string deadUnitName = unitData != null ? unitData.unitName : name;
        Debug.Log($"<color=red>{deadUnitName}이(가) 전투에서 쓰러졌습니다.</color>");
    }

    public void Heal(float amount)
    {
        // 서버에서만 HP 수정 (클라이언트는 Networked 속성 동기화로 반영)
        if (!HasStateAuthorityOrNoNetwork()) return;
        if (IsDead || amount <= 0) return;

        currentHP += amount;
        if (currentHP > maxHP)
        {
            currentHP = maxHP;
        }
        // Render()에서 ChangeDetector가 OnHealthChanged 이벤트를 발생시킴
    }
    #endregion

    #region 스탯 수정 메서드 (BuffManager용)

    public void ApplyStatModifiers(float attackDamage, float attackSpeed)
    {
        _localAttackDamage = attackDamage;
        _localAttackSpeed = attackSpeed;

        if (CanWriteNetworkedStats())
        {
            _networkedAttackDamage = attackDamage;
            _networkedAttackSpeed = attackSpeed;
        }
    }

    private void SetPersistentNonDamageStats(float range, float defense, float magicRes)
    {
        _localAttackRange = range;
        _localDefense = defense;
        _localMagicResistance = magicRes;

        if (CanWriteNetworkedStats())
        {
            _networkedAttackRange = range;
            _networkedDefense = defense;
            _networkedMagicResistance = magicRes;
        }
    }

    /// <summary>
    /// 모든 스탯을 직접 설정합니다. (InitializeStats용)
    /// </summary>
    private void SetStatsDirect(float damage, float speed, float range, float defense, float magicRes)
    {
        _localAttackDamage = damage;
        _localAttackSpeed = speed;
        _localAttackRange = range;
        _localDefense = defense;
        _localMagicResistance = magicRes;

        if (CanWriteNetworkedStats())
        {
            _networkedAttackDamage = damage;
            _networkedAttackSpeed = speed;
            _networkedAttackRange = range;
            _networkedDefense = defense;
            _networkedMagicResistance = magicRes;
        }
    }

    /// <summary>
    /// 모든 일시 버프를 해제하고 2단계(Permanent) 스탯으로 초기화합니다.
    /// 준비 시퀀스 진입 시 안전장치로 사용됩니다.
    /// </summary>
    public void ResetToPermanentStats()
    {
        if (!HasStateAuthorityOrNoNetwork()) return;
        
        // BuffManager의 일시 버프 모두 해제
        if (_buffManager != null)
        {
            _buffManager.ClearAllBuffs();
        }
        
        // 2단계(Permanent) 스탯으로 초기화 (증강체 적용, 버프 미적용)
        SetStatsDirect(
            PermanentAttackDamage,
            PermanentAttackSpeed,
            PermanentAttackRange,
            PermanentDefense,
            PermanentMagicResistance
        );
    }

    #endregion

    #region 폭주 모드

    private bool _isBerserk = false;

    /// <summary>
    /// 폭주 모드를 적용합니다. (전투 종료 5초 전)
    /// </summary>
    public void ApplyBerserkMode()
    {
        if (_isBerserk) return;
        if (!HasStateAuthorityOrNoNetwork()) return;  // 서버에서만 적용
        
        _isBerserk = true;

        if (CombatScheduler.Instance != null &&
            CombatScheduler.Instance.IsStatBuffSchedulerActive &&
            _buffManager != null &&
            CombatScheduler.Instance.ApplyBerserkStatBuffs(_buffManager, gameObject, false, 9999f))
        {
            Debug.Log($"<color=red>[Unit] '{name}' ??＜ 紐⑤뱶 諛쒕룞! (scheduler)</color>");
            return;
        }

        float berserkDamage = currentAttackDamage * 1.5f;
        float berserkSpeed = currentAttackSpeed * 1.5f;
        _localAttackDamage = berserkDamage;
        _localAttackSpeed = berserkSpeed;
        if (CanWriteNetworkedStats())
        {
            _networkedAttackDamage = berserkDamage;
            _networkedAttackSpeed = berserkSpeed;
        }
        Debug.Log($"<color=red>[Unit] '{name}' 폭주 모드 발동! (공속 1.5배, 공격력 1.5배)</color>");
    }

    /// <summary>
    /// 폭주 모드를 해제합니다. (전투 종료 시)
    /// </summary>
    public void ClearBerserkMode()
    {
        if (!_isBerserk) return;  // 폭주 모드가 아니면 무시
        
        _isBerserk = false;
        
        // 스탯을 원래대로 복구 (증강체 + 버프 적용된 정상 스탯)
        if (HasStateAuthorityOrNoNetwork())
        {
            RefreshPermanentBonuses();
            Debug.Log($"<color=green>[Unit] '{name}' 폭주 모드 해제! 스탯 복구됨.</color>");
        }
    }

    #endregion
}
