// Assets/Scripts/Game/Monsters/Monster.cs
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Fusion;
using Cysharp.Threading.Tasks;

public class Monster : NetworkBehaviour, IEnemy, IHealth
{
    [Header("참조 데이터")]
    [SerializeField] private MonsterData _monsterData;
    public MonsterData Data => _monsterData;

    public bool HasTrait(MonsterTraits trait)
    {
        if (_monsterData == null) return false;
        return (_monsterData.traits & trait) != 0;
    }
    
    private BuffManager _buffManager;

    [Tooltip("벽 레이어 마스크")]
    public LayerMask wallLayerMask;

    [Header("StatusBar 설정")]
    [Tooltip("StatusBar 프리팹 참조 (MonsterSpawner에서 전달받음)")]
    public GameObject statusBarPrefab;

    #region 애니메이션 관련
    [Header("Animation")]
    [SerializeField] private Animator animator;
    [SerializeField] private string attackTriggerParam = "AttackTrigger";
    [SerializeField] private string isWalkingParam = "IsWalking";
    [SerializeField] private string moveSpeedParam = "MoveSpeed";    // 이동 애니메이션 속도 배율
    [SerializeField] private string attackSpeedParam = "AttackSpeed"; // 공격 애니메이션 속도 배율
    [SerializeField] private float rotationSpeed = 10f;
    
    // 대기 중인 공격 정보 (애니메이션 이벤트 기반 데미지 적용용)
    private bool _hasPendingAttack;
    private IEnemy _pendingAttackTarget;
    
    // 원거리 공격 관련
    [Header("원거리 공격 설정")]
    [Tooltip("원거리 몬스터의 투사체 발사 위치입니다. 비어있으면 몬스터 위치 + Vector3.up * 0.5f를 사용합니다.")]
    public Transform firePoint;
    private Unit _rangedTarget;              // 원거리 공격 대상 유닛
    private Coroutine _rangedAttackCoroutine;
    private float _nextRangedAttackTime;
    private bool _isRangedAttacking;         // 원거리 공격 중 플래그
    [SerializeField] private LayerMask unitLayerMask; // Unit 레이어
    [SerializeField] private float postRangedAttackDelay = 0.5f; // 원거리 공격 후 정지 시간
    #endregion

    // === 현재 상태 (Networked) ===
    // [Networked] 속성으로 서버/클라이언트 간 HP 동기화
    [Networked] public float NetworkedHP { get; set; }
    [Networked] public float NetworkedMaxHP { get; set; }
    [Networked] public NetworkBool NetworkedIsBoss { get; set; }
    [Networked] public int NetworkedBossOriginPlayerId { get; set; }
    [Networked] public int NetworkedBossUniqueId { get; set; }
    [Networked] private int NetworkedOwnerPlayerIdEncoded { get; set; }
    [Networked] private NetworkString<_64> NetworkedMonsterDataKey { get; set; }
    [Networked] private int NetworkedMonsterTypeValue { get; set; }
    [Networked] private int NetworkedMonsterTraitsValue { get; set; }
    
    // [Networked] 공격 애니메이션 동기화 (RPC 대체로 네트워크 부하 감소)
    // 서버에서 값을 변경하면 ChangeDetector가 감지하여 클라이언트에서 애니메이션 재생
    [Networked] public int NetworkedAttackTrigger { get; set; } // 값 변경 시 애니메이션 트리거
    [Networked] public float NetworkedAttackSpeedRatio { get; set; } // 공격속도 비율

    private bool _hasSpawned;
    private bool _hasLocalHealthValues;
    private float _localHP;
    private float _localMaxHP;
    
    // 로컬 접근용 프로퍼티 (IHealth 인터페이스 호환성 유지)
    public float currentHP
    {
        get => CanReadNetworkedHealth() ? NetworkedHP : _localHP;
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
    private float currentMaxHP
    {
        get => CanReadNetworkedHealth() ? NetworkedMaxHP : _localMaxHP;
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

    public float CurrentHealth => CanReadNetworkedHealth() ? NetworkedHP : _localHP;
    public float MaxHealth => CanReadNetworkedHealth() ? NetworkedMaxHP : _localMaxHP;
    public event System.Action<float, float> OnHealthChanged;

    private ManaController manaController;

    private Transform goalTransform;
    private PlayerManager ownerPlayer;
    private AstarGrid pathfinder;
    private bool isBlocked = false;
    private Unit blockingUnit;
    private StatusBarUI statusBarUI;
    private Coroutine movementCoroutine;
    private Coroutine attackCoroutine;
    private static bool isQuitting = false;
    private bool isMoving = false;
    
    #region 3단계 스탯 시스템
    // ========================================
    // 1단계: Base (MonsterData 기본값)
    // ========================================
    private float _baseMaxHealth;
    private float _baseMoveSpeed;
    private float _baseAttackDamage;
    private float _baseAttackSpeed;
    
    // ========================================
    // 2단계: Permanent (증강체 효과 적용)
    // ========================================
    private float _permanentMaxHealth;
    private float _permanentMoveSpeed;
    private float _permanentAttackDamage;
    private float _permanentAttackSpeed;
    
    // ========================================
    // 3단계: Final (버서커, 스킬 버프, 웨이브 스케일링)
    // ========================================
    private float _currentMoveSpeed;
    private float _currentAttackDamage;
    private float _currentAttackSpeed;
    
    // 공개 프로퍼티 (읽기 전용)
    public float BaseMaxHealth => _baseMaxHealth;
    public float BaseMoveSpeed => _baseMoveSpeed;
    public float BaseAttackDamage => _baseAttackDamage;
    public float BaseAttackSpeed => _baseAttackSpeed;
    
    public float PermanentMaxHealth => _permanentMaxHealth;
    public float PermanentMoveSpeed => _permanentMoveSpeed;
    public float PermanentAttackDamage => _permanentAttackDamage;
    public float PermanentAttackSpeed => _permanentAttackSpeed;
    
    public float currentMoveSpeed => _currentMoveSpeed;
    public float currentAttackDamage => _currentAttackDamage;
    public float currentAttackSpeed => _currentAttackSpeed;
    #endregion
    
    private MonsterReleaseScheduler releaseScheduler;
    private Coroutine resumeCoroutine;
    private int currentBlockerId = 0;
    private ChangeDetector _changeDetector;
    private bool _networkMonsterDataLoadRequested;

    public bool SnapshotIsBoss => CanReadNetworkedHealth() ? NetworkedIsBoss : _isBoss;
    public int SnapshotBossOriginPlayerId => CanReadNetworkedHealth() ? NetworkedBossOriginPlayerId : _originPlayerId;
    public int SnapshotBossUniqueId => CanReadNetworkedHealth() ? NetworkedBossUniqueId : _bossUniqueId;
    public int SnapshotOwnerPlayerId
    {
        get
        {
            if (CanReadNetworkedHealth())
            {
                int decoded = DecodeSnapshotOwnerId(NetworkedOwnerPlayerIdEncoded);
                if (decoded >= 0)
                {
                    return decoded;
                }
            }

            return ownerPlayer != null ? ownerPlayer.playerId : -1;
        }
    }

    public string SnapshotMonsterDataKey
    {
        get
        {
            if (CanReadNetworkedHealth())
            {
                string networkKey = NormalizeMonsterDataKey(NetworkedMonsterDataKey.ToString());
                if (!string.IsNullOrEmpty(networkKey))
                {
                    return networkKey;
                }
            }

            return BuildSnapshotMonsterDataKey(_monsterData);
        }
    }

    public string SnapshotMonsterTypeName
    {
        get
        {
            if (CanReadNetworkedHealth() && NetworkedMonsterTypeValue > 0)
            {
                return ((MonsterType)(NetworkedMonsterTypeValue - 1)).ToString();
            }

            return _monsterData != null ? _monsterData.monsterType.ToString() : string.Empty;
        }
    }

    public string SnapshotMonsterTraitsName
    {
        get
        {
            if (CanReadNetworkedHealth() && NetworkedMonsterTraitsValue > 0)
            {
                return ((MonsterTraits)(NetworkedMonsterTraitsValue - 1)).ToString();
            }

            return _monsterData != null ? _monsterData.traits.ToString() : string.Empty;
        }
    }

    #region 보스 몬스터 관련
    // 보스 몬스터 플래그 및 생존 시 다음 라운드 침공을 위한 정보
    private bool _isBoss = false;
    private int _originPlayerId = -1;
    private int _bossUniqueId = -1; // 보스 고유 ID (턴당 1회 침공 추적용)
    private bool _hasRegisteredAsSurvivor = false; // 중복 등록 방지 플래그
    #endregion

    private bool HasStateAuthorityOrNoNetwork()
    {
        if (Object == null || Runner == null || !Runner.IsRunning)
        {
            return true;
        }
        return Object.HasStateAuthority;
    }

    /// <summary>
    /// Fusion NetworkBehaviour의 Spawned 콜백.
    /// </summary>
    public override void Spawned()
    {
        base.Spawned();
        _changeDetector = GetChangeDetector(ChangeDetector.Source.SimulationState);
        
        if (_hasSpawned)
        {
            _hasLocalHealthValues = false;
            _localHP = 0;
            _localMaxHP = 0;
            _networkMonsterDataLoadRequested = false;
            
            if (Object != null && Object.HasStateAuthority)
            {
                NetworkedMaxHP = 0;
                NetworkedHP = 0;
                NetworkedIsBoss = false;
                NetworkedBossOriginPlayerId = -1;
                NetworkedBossUniqueId = -1;
                ResetNetworkSnapshotIdentity();
            }
            
            var existingStatusBar = GetComponentInChildren<StatusBarUI>(true);
            existingStatusBar?.ResetForReuse(initializeImmediately: false);
        }
        _hasSpawned = true;
        TryRebindOwnerFromNetworkSnapshot();
        TryRecoverMonsterDataFromNetworkSnapshot();
    }
    
    /// <summary>
    /// 클라이언트에서 HP 변경을 감지하고 이벤트를 발생시킵니다.
    /// </summary>
    public override void Render()
    {
        TryRebindOwnerFromNetworkSnapshot();
        TryRecoverMonsterDataFromNetworkSnapshot();
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
            // 공격 애니메이션 동기화 (클라이언트에서만 실행)
            else if (propertyName == nameof(NetworkedAttackTrigger))
            {
                HandleNetworkedAttackTriggered();
            }
        }
    }
    
    /// <summary>
    /// 클라이언트에서 공격 애니메이션을 재생합니다. (ChangeDetector 콜백)
    /// </summary>
    private void HandleNetworkedAttackTriggered()
    {
        // 서버/호스트는 이미 TriggerAttackAnimation()에서 실행했으므로 무시
        if (Object != null && Object.HasStateAuthority) return;
        
        if (animator == null || string.IsNullOrEmpty(attackTriggerParam)) return;
        
        // 공격속도 설정
        if (!string.IsNullOrEmpty(attackSpeedParam))
        {
            animator.SetFloat(attackSpeedParam, NetworkedAttackSpeedRatio);
        }
        
        animator.ResetTrigger(attackTriggerParam);
        animator.SetTrigger(attackTriggerParam);
    }

    private bool CanWriteNetworkedHealth()
    {
        return _hasSpawned
            && Runner != null
            && Runner.IsRunning
            && Object != null
            && Object.HasStateAuthority;
    }

    /// <summary>
    /// Networked 프로퍼티를 안전하게 읽을 수 있는지 확인합니다.
    /// 싱글플레이(Runner == null)나 Spawned() 전에는 false를 반환합니다.
    /// </summary>
    private bool CanReadNetworkedHealth()
    {
        return _hasSpawned
            && Runner != null
            && Runner.IsRunning
            && Object != null;
    }

    private static int EncodeSnapshotOwnerId(int ownerPlayerId)
    {
        return ownerPlayerId >= 0 ? ownerPlayerId + 1 : 0;
    }

    private static int DecodeSnapshotOwnerId(int encodedOwnerPlayerId)
    {
        return encodedOwnerPlayerId > 0 ? encodedOwnerPlayerId - 1 : -1;
    }

    private static string NormalizeMonsterDataKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return string.Empty;
        }

        return key.Replace("(Clone)", string.Empty).Trim();
    }

    private static string BuildSnapshotMonsterDataKey(MonsterData data)
    {
        if (data == null)
        {
            return string.Empty;
        }

        string assetKey = NormalizeMonsterDataKey(data.name);
        if (!string.IsNullOrEmpty(assetKey))
        {
            return assetKey;
        }

        return NormalizeMonsterDataKey(data.monsterName);
    }

    private void ResetNetworkSnapshotIdentity()
    {
        if (Object == null || !Object.HasStateAuthority)
        {
            return;
        }

        NetworkedOwnerPlayerIdEncoded = 0;
        NetworkedMonsterDataKey = string.Empty;
        NetworkedMonsterTypeValue = 0;
        NetworkedMonsterTraitsValue = 0;
    }

    private void SyncNetworkSnapshotIdentityFromLocalData()
    {
        if (Object == null || !Object.HasStateAuthority)
        {
            return;
        }

        NetworkedOwnerPlayerIdEncoded = EncodeSnapshotOwnerId(ownerPlayer != null ? ownerPlayer.playerId : -1);
        NetworkedMonsterDataKey = BuildSnapshotMonsterDataKey(_monsterData);
        NetworkedMonsterTypeValue = _monsterData != null ? (int)_monsterData.monsterType + 1 : 0;
        NetworkedMonsterTraitsValue = _monsterData != null ? (int)_monsterData.traits + 1 : 0;
    }

    private void TryRebindOwnerFromNetworkSnapshot()
    {
        int ownerId = SnapshotOwnerPlayerId;
        if (ownerId < 0)
        {
            return;
        }

        var gameManagers = GameManagers.Instance;
        var owner = gameManagers != null ? gameManagers.GetPlayer(ownerId) : null;
        if (owner == null)
        {
            return;
        }

        ownerPlayer = owner;
        if (owner.astarGrid != null)
        {
            pathfinder = owner.astarGrid;
        }

        if (owner.goalTransform != null)
        {
            goalTransform = owner.goalTransform;
        }

        var monsterParent = owner.monsterSpawner != null ? owner.monsterSpawner.monsterParent : null;
        if (monsterParent != null && transform.parent != monsterParent)
        {
            transform.SetParent(monsterParent, true);
        }
        TryRecoverMonsterDataFromNetworkSnapshot();
    }

    private void TryRecoverMonsterDataFromNetworkSnapshot()
    {
        if (_monsterData != null || _networkMonsterDataLoadRequested)
        {
            return;
        }

        string key = SnapshotMonsterDataKey;
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        _networkMonsterDataLoadRequested = true;
        RecoverMonsterDataFromNetworkSnapshotAsync(key).Forget();
    }

    private async UniTaskVoid RecoverMonsterDataFromNetworkSnapshotAsync(string key)
    {
        MonsterData data = await AssetLoader.LoadAssetAsync<MonsterData>(key);
        if (data == null)
        {
            _networkMonsterDataLoadRequested = false;
            return;
        }

        _monsterData = data;
        name = data.monsterName;
        if (pathfinder != null)
        {
            wallLayerMask = pathfinder.wallLayers;
        }

        _baseMaxHealth = data.maxHealth;
        _baseMoveSpeed = data.moveSpeed;
        _baseAttackDamage = data.attackDamage;
        _baseAttackSpeed = data.attackSpeed;
        _permanentMaxHealth = _baseMaxHealth;
        _permanentMoveSpeed = _baseMoveSpeed;
        _permanentAttackDamage = _baseAttackDamage;
        _permanentAttackSpeed = _baseAttackSpeed;
        _currentMoveSpeed = _permanentMoveSpeed;
        _currentAttackDamage = _permanentAttackDamage;
        _currentAttackSpeed = _permanentAttackSpeed;
        if (isActiveAndEnabled && !HasTrait(MonsterTraits.Destroyer))
        {
            GameEvents.OnWallDestroyed -= OnWallDestroyed;
            GameEvents.OnWallDestroyed += OnWallDestroyed;
        }
        EnsureAnimator();

        manaController = GetComponent<ManaController>();
        if (manaController != null)
        {
            int maxMana = data.skillData != null ? data.skillData.manaCost : 0;
            manaController.Initialize(maxMana);
        }
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

    void OnApplicationQuit() { isQuitting = true; }
    
    private void OnEnable()
    {
        // 일반 몬스터만 벽 파괴 이벤트 구독 (파괴자는 이미 최단 경로로 이동)
        if (_monsterData != null && !HasTrait(MonsterTraits.Destroyer))
        {
            GameEvents.OnWallDestroyed += OnWallDestroyed;
        }
    }
    
    private void OnDisable()
    {
        if (_monsterData != null && !HasTrait(MonsterTraits.Destroyer))
        {
            GameEvents.OnWallDestroyed -= OnWallDestroyed;
        }
    }

    public void SetStatusBar(StatusBarUI ui)
    {
        this.statusBarUI = ui;
    }

    public void Initialize(PlayerManager owner, Transform goal, MonsterData data, AstarGrid pathfinder)
    {
        this.ownerPlayer = owner;
        this.goalTransform = goal;
        this._monsterData = data;
        this._networkMonsterDataLoadRequested = false;
        this.pathfinder = pathfinder;
        this.releaseScheduler = owner != null ? owner.GetComponentInChildren<MonsterReleaseScheduler>(true) : null;
        this.name = _monsterData.monsterName;
        this.wallLayerMask = pathfinder.wallLayers;
        
        // ========================================
        // 1단계: Base 초기화 (MonsterData 기본값)
        // ========================================
        _baseMaxHealth = _monsterData.maxHealth;
        _baseMoveSpeed = _monsterData.moveSpeed;
        _baseAttackDamage = _monsterData.attackDamage;
        _baseAttackSpeed = _monsterData.attackSpeed;
        
        // ========================================
        // 2단계: Permanent 초기화 (증강체 적용 전 = Base와 동일)
        // ========================================
        _permanentMaxHealth = _baseMaxHealth;
        _permanentMoveSpeed = _baseMoveSpeed;
        _permanentAttackDamage = _baseAttackDamage;
        _permanentAttackSpeed = _baseAttackSpeed;
        
        // ========================================
        // 3단계: Final 초기화 (버프 적용 전 = Permanent와 동일)
        // ========================================
        _currentMoveSpeed = _permanentMoveSpeed;
        _currentAttackDamage = _permanentAttackDamage;
        _currentAttackSpeed = _permanentAttackSpeed;

        // [Fix] 오브젝트 재사용 시 이전 상태 초기화
        isBlocked = false;
        blockingUnit = null;
        isMoving = false;
        _isBoss = false;
        _originPlayerId = -1;
        _bossUniqueId = -1;
        _hasRegisteredAsSurvivor = false;
        if (attackCoroutine != null)
        {
            StopCoroutine(attackCoroutine);
            attackCoroutine = null;
        }
        if (resumeCoroutine != null)
        {
            StopCoroutine(resumeCoroutine);
            resumeCoroutine = null;
        }
        currentBlockerId = 0;
        _hasPendingAttack = false;
        _pendingAttackTarget = null;

        // [Fix] 오브젝트 풀 재사용 시 HP 강제 리셋
        // StateAuthority가 있으면 NetworkedHP에 직접 쓰기
        float maxHp = _monsterData.maxHealth;
        if (Object != null && Object.HasStateAuthority)
        {
            NetworkedMaxHP = maxHp;
            NetworkedHP = maxHp;
            NetworkedIsBoss = false;
            NetworkedBossOriginPlayerId = -1;
            NetworkedBossUniqueId = -1;
            SyncNetworkSnapshotIdentityFromLocalData();
        }
        // 로컬 값도 설정 (아직 Spawned 되지 않았을 경우를 위해)
        _hasLocalHealthValues = true;
        _localMaxHP = maxHp;
        _localHP = maxHp;
        
        // StatusBarUI 생성 (statusBarPrefab이 이미 할당된 상태)
        EnsureStatusBarUI();
        statusBarUI?.ResetForReuse();
        
        OnHealthChanged?.Invoke(maxHp, maxHp);

        manaController = GetComponent<ManaController>();
        _buffManager = GetComponent<BuffManager>();

        // 일반 몬스터만 벽 파괴 이벤트 구독 (Initialize에서 _monsterData가 설정된 후)
        if (_monsterData != null && !HasTrait(MonsterTraits.Destroyer))
        {
            // 중복 구독 방지를 위해 먼저 해제
            GameEvents.OnWallDestroyed -= OnWallDestroyed;
            GameEvents.OnWallDestroyed += OnWallDestroyed;
        }

        int maxMana = 0;
        if (_monsterData.skillData != null)
        {
            maxMana = _monsterData.skillData.manaCost;
            manaController.OnManaFull += ActivateSkill;
        }
        manaController.Initialize(maxMana);
        
        // 원거리 몬스터: unitLayerMask 자동 설정
        if (_monsterData.attackType == MonsterAttackType.Ranged && unitLayerMask == 0)
        {
            int unitLayer = LayerMask.NameToLayer("Unit");
            if (unitLayer >= 0)
            {
                unitLayerMask = 1 << unitLayer;
                // Debug.Log($"<color=yellow>[Monster] '{name}' unitLayerMask 자동 설정: {unitLayerMask.value}</color>");
            }
            else
            {
                // Debug.LogWarning($"[Monster] '{name}' Unit 레이어를 찾을 수 없습니다. 원거리 공격이 작동하지 않을 수 있습니다.");
            }
        }
        
        // 애니메이터 초기화
        EnsureAnimator();
    }
    
    /// <summary>
    /// StatusBarUI가 없으면 생성합니다.
    /// </summary>
    private void EnsureStatusBarUI()
    {
        if (statusBarUI != null) return;
        
        // 이미 자식으로 StatusBarUI가 있는지 확인
        var existing = GetComponentInChildren<StatusBarUI>(true);
        if (existing != null)
        {
            statusBarUI = existing;
            return;
        }
        
        // statusBarPrefab이 없으면 MonsterSpawner에서 가져오기 시도
        if (statusBarPrefab == null)
        {
            var spawner = FindObjectOfType<MonsterSpawner>();
            if (spawner != null && spawner.statusBarPrefab != null)
            {
                statusBarPrefab = spawner.statusBarPrefab;
            }
        }
        
        // 프리팹이 있으면 생성
        if (statusBarPrefab != null)
        {
            GameObject statusBarGO = Instantiate(statusBarPrefab, transform);
            statusBarUI = statusBarGO.GetComponent<StatusBarUI>();
            // Debug.Log($"<color=cyan>[Monster] {name}: StatusBarUI 생성 (HasStateAuthority={(Object != null ? Object.HasStateAuthority.ToString() : "N/A")})</color>");
        }
    }
    
    /// <summary>
    /// 서버에서 클라이언트로 초기화 데이터를 전송합니다.
    /// </summary>
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_InitializeOnClient(NetworkId ownerPlayerId, string monsterDataName)
    {
        // 서버는 이미 Initialize()로 초기화되었으므로 무시
        if (Object != null && Object.HasStateAuthority) return;
        
        // Debug.Log($"<color=yellow>[Monster.RPC_InitializeOnClient] {name}: 클라이언트 초기화 시작 (monsterDataName={monsterDataName})</color>");
        
        InitializeOnClientAsync(ownerPlayerId, monsterDataName).Forget();
    }
    
    private async UniTaskVoid InitializeOnClientAsync(NetworkId ownerPlayerId, string monsterDataName)
    {
        // ownerPlayer 찾기
        NetworkObject ownerNO = null;
        int attempts = 0;
        while (ownerNO == null && attempts < 60)
        {
            if (Runner != null)
            {
                Runner.TryFindObject(ownerPlayerId, out ownerNO);
            }
            if (ownerNO == null)
            {
                await UniTask.Yield();
                attempts++;
            }
        }
        
        PlayerManager owner = ownerNO != null ? ownerNO.GetComponent<PlayerManager>() : null;
        
        if (owner == null)
        {
            // Debug.LogWarning($"[Monster.RPC_InitializeOnClient] ownerPlayer를 찾을 수 없습니다.");
            // StatusBarUI만이라도 생성
            EnsureStatusBarUI();
            return;
        }
        
        // MonsterData 로드 (Addressables에서 로드)
        if (_monsterData == null && !string.IsNullOrEmpty(monsterDataName))
        {
            // AssetLoader를 통해 MonsterData 로드
            _monsterData = await AssetLoader.LoadAssetAsync<MonsterData>(monsterDataName);
            _networkMonsterDataLoadRequested = false;
            
            if (_monsterData == null)
            {
                // Debug.LogError($"[Monster.InitializeOnClientAsync] MonsterData '{monsterDataName}'를 로드할 수 없습니다!");
            }
            else
            {
                // Debug.Log($"<color=green>[Monster.InitializeOnClientAsync] MonsterData '{monsterDataName}' 로드 성공!</color>");
            }
        }
        
        // 필수 참조 설정
        this.ownerPlayer = owner;
        this.pathfinder = owner.astarGrid;
        var monsterParent = owner.monsterSpawner != null ? owner.monsterSpawner.monsterParent : null;
        if (monsterParent != null && transform.parent != monsterParent)
        {
            transform.SetParent(monsterParent, true);
        }
        
        if (owner.goalTransform != null)
        {
            this.goalTransform = owner.goalTransform;
        }
        else
        {
            int goalAttempts = 0;
            while (owner.goalTransform == null && goalAttempts < 30)
            {
                await UniTask.Yield();
                goalAttempts++;
            }
            
            if (owner.goalTransform != null)
            {
                this.goalTransform = owner.goalTransform;
            }
            else if (owner.fieldManager != null)
            {
                Vector2Int gridSize = owner.fieldManager.gridSize;
                Vector3 gridOrigin = owner.fieldManager.gridOrigin;
                float cellSize = owner.fieldManager.cellSize;
                int centerX = gridSize.x / 2;
                int centerY = gridSize.y / 2;
                
                GameObject fallbackGoal = new GameObject("FallbackGoal_Monster");
                fallbackGoal.transform.position = new Vector3(
                    gridOrigin.x + (centerX + 0.5f) * cellSize,
                    gridOrigin.y,
                    gridOrigin.z + (centerY + 0.5f) * cellSize
                );
                this.goalTransform = fallbackGoal.transform;
                // Debug.LogWarning($"[Monster] goalTransform fallback used - calculated from FieldManager center");
            }
        }
        
        if (_monsterData != null)
        {
            this.name = _monsterData.monsterName;
            this.wallLayerMask = pathfinder != null ? pathfinder.wallLayers : default;
            // 클라이언트 초기화: 3단계 스탯 설정 (증강체/버프는 서버에서 동기화)
            _baseMoveSpeed = _monsterData.moveSpeed;
            _baseAttackDamage = _monsterData.attackDamage;
            _baseAttackSpeed = _monsterData.attackSpeed;
            _permanentMoveSpeed = _baseMoveSpeed;
            _permanentAttackDamage = _baseAttackDamage;
            _permanentAttackSpeed = _baseAttackSpeed;
            _currentMoveSpeed = _permanentMoveSpeed;
            _currentAttackDamage = _permanentAttackDamage;
            _currentAttackSpeed = _permanentAttackSpeed;
            // currentMaxHP, currentHP는 설정하지 않음 - 서버에서 동기화된 NetworkedHP/NetworkedMaxHP 사용
        }
        
        // StatusBarUI 생성
        EnsureStatusBarUI();
        
        // 서버에서 동기화된 HP 값을 UI에 반영
        statusBarUI?.ResetForReuse();
        OnHealthChanged?.Invoke(NetworkedHP, NetworkedMaxHP);
        
        manaController = GetComponent<ManaController>();
        if (manaController != null && _monsterData != null)
        {
            int maxMana = 0;
            if (_monsterData.skillData != null)
            {
                maxMana = _monsterData.skillData.manaCost;
            }
            manaController.Initialize(maxMana);
        }
        
        // 애니메이터 초기화 (클라이언트에서도 필요)
        EnsureAnimator();
        
        // Debug.Log($"<color=cyan>[Monster.RPC_InitializeOnClient] {name}: 클라이언트 초기화 완료 (animator={animator != null})</color>");
    }

    void Update()
    {
        if (!HasStateAuthorityOrNoNetwork()) return;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (MPTestCommandLine.IsGameFlowFrozen)
        {
            return;
        }
#endif
        
        // 마나 회복 (스킬이 있는 경우)
        if (_monsterData != null && _monsterData.skillData != null)
        {
            manaController.GainManaOverTime(10f);
        }
        
        // 원거리 몬스터: 이동 중에도 범위 내 적 탐색 및 공격
        if (_monsterData != null && _monsterData.attackType == MonsterAttackType.Ranged)
        {
            TryRangedAttack();
        }
    }
    
    #region 원거리 공격 로직
    
    /// <summary>
    /// 원거리 몬스터의 공격을 시도합니다.
    /// </summary>
    private void TryRangedAttack()
    {
        // 이미 공격 중이면 리턴
        if (_isRangedAttacking) return;
        if (_buffManager != null && !_buffManager.CanAttack) return;
        if (Time.time < _nextRangedAttackTime) return;
        
        Unit target = FindBestTargetUnit();
        if (target == null) return;
        
        // 원거리 몬스터는 항상 정지 후 공격
        StartCoroutine(PauseAndRangedAttack(target));
    }
    
    /// <summary>
    /// 정지하고 원거리 공격 후 이동 재개
    /// </summary>
    private IEnumerator PauseAndRangedAttack(Unit target)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        while (MPTestCommandLine.IsGameFlowFrozen)
        {
            yield return null;
        }
#endif
        // 원거리 공격 중 플래그 설정
        _isRangedAttacking = true;
        
        // Idle 상태로 전환
        SetWalkingAnimation(false);
        
        // 대상 방향으로 회전
        Vector3 direction = (target.transform.position - transform.position).normalized;
        if (direction != Vector3.zero)
        {
            Quaternion targetRotation = Quaternion.LookRotation(direction);
            transform.rotation = targetRotation;
        }
        
        // 공격 수행 (애니메이션 트리거 + pendingAttack 설정)
        if (target == null || target.IsDead)
        {
            _isRangedAttacking = false;
            if (isMoving) SetWalkingAnimation(true);
            yield break;
        }
        
        _rangedTarget = target;
        _hasPendingAttack = true;
        _pendingAttackTarget = target;
        TriggerAttackAnimation();
        
        // 공격 애니메이션 전체 시간 대기 (Animation Event가 중간에 발사)
        // fallback용 대기 시간: 애니메이션 이벤트 타이밍보다 충분히 길게 설정
        float attackAnimDuration = 1f / currentAttackSpeed; // 공격 애니메이션 총 길이
        float fallbackWaitTime = Mathf.Max(attackAnimDuration * 0.9f, 0.8f); // 애니메이션의 90% 또는 최소 0.8초
        yield return new WaitForSeconds(fallbackWaitTime);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        while (MPTestCommandLine.IsGameFlowFrozen)
        {
            yield return null;
        }
#endif
        
        // Animation Event가 호출되지 않았으면 직접 실행 (fallback)
        if (_hasPendingAttack && _pendingAttackTarget != null)
        {
            // Debug.LogWarning($"[Monster] '{name}' 원거리 공격 Animation Event fallback 실행 - 애니메이션 이벤트 설정을 확인하세요!");
            ExecutePendingAttack();
        }
        
        // 공격 애니메이션 완료 대기 (나머지 시간)
        float remainingAnimTime = Mathf.Max(attackAnimDuration - fallbackWaitTime, 0.1f);
        yield return new WaitForSeconds(remainingAnimTime);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        while (MPTestCommandLine.IsGameFlowFrozen)
        {
            yield return null;
        }
#endif
        
        // 공격 후 추가 정지 시간
        yield return new WaitForSeconds(postRangedAttackDelay);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        while (MPTestCommandLine.IsGameFlowFrozen)
        {
            yield return null;
        }
#endif
        
        // 원거리 공격 중 플래그 해제
        _isRangedAttacking = false;
        
        // 공격 쿨타임 설정 (공격 종료 시점 기준)
        _nextRangedAttackTime = Time.time + 1f / currentAttackSpeed;
        
        // 다시 Walk 상태로
        if (isMoving)
        {
            SetWalkingAnimation(true);
        }
    }
    
    /// <summary>
    /// 원거리 몬스터의 타겟 우선순위에 따라 최적의 타겟을 찾습니다.
    /// 우선순위: 원거리 유닛 > 근접 유닛, 같은 타입이면 가까운 순
    /// </summary>
    private Unit FindBestTargetUnit()
    {
        Collider[] unitsInRange = Physics.OverlapSphere(transform.position, _monsterData.attackRange, unitLayerMask);
        if (unitsInRange.Length == 0) return null;
        
        Unit bestRangedUnit = null;
        Unit bestMeleeUnit = null;
        float closestRangedDist = float.MaxValue;
        float closestMeleeDist = float.MaxValue;
        
        foreach (var col in unitsInRange)
        {
            if (!col.TryGetComponent<Unit>(out var unit)) continue;
            if (unit.IsDead || unit.Data == null) continue;
            
            float distance = Vector3.Distance(transform.position, unit.transform.position);
            
            if (unit.Data.unitType == UnitType.Ranged)
            {
                if (distance < closestRangedDist)
                {
                    closestRangedDist = distance;
                    bestRangedUnit = unit;
                }
            }
            else // Melee
            {
                if (distance < closestMeleeDist)
                {
                    closestMeleeDist = distance;
                    bestMeleeUnit = unit;
                }
            }
        }
        
        // 원거리 유닛 우선
        return bestRangedUnit != null ? bestRangedUnit : bestMeleeUnit;
    }
    
    #endregion

    private void ActivateSkill()
    {
        if (!HasStateAuthorityOrNoNetwork()) return;
        
        if (_buffManager != null && !_buffManager.CanUseSkill) return;
        
        SkillData skillData = _monsterData.skillData;

        if (skillData == null || skillData.targetingStrategy == null || skillData.effects.Count == 0)
        {
            // Debug.LogError($"{_monsterData.monsterName}의 SkillData 또는 그 내용이 올바르게 설정되지 않았습니다.");
            return;
        }

        if (!manaController.IsManaFull) return;

        if (manaController.UseMana(skillData.manaCost))
        {
            // Debug.Log($"<color=magenta>{_monsterData.monsterName} 스킬 발동: {skillData.skillName}</color>");

            List<GameObject> targets = skillData.targetingStrategy.FindTargets(this.gameObject, transform.position, skillData.range);

            foreach (var effect in skillData.effects)
            {
                if (effect != null)
                {
                    effect.ApplyEffect(null, this.gameObject, targets, skillData.range, skillData.targetingStrategy);
                }
            }

            if (skillData.vfxPrefab != null)
            {
                GameObject vfxInstance = Instantiate(skillData.vfxPrefab, transform.position, Quaternion.identity);

                float maxDuration = 0f;
                foreach (var effect in skillData.effects)
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
                    // Debug.LogWarning($"VFX 프리팹 '{vfxInstance.name}'에 VFXAutoDestroy.cs 컴포넌트가 없습니다. 자동으로 파괴되지 않습니다.");
                }
            }
        }
    }

    public void Heal(float amount)
    {
        // 서버에서만 HP 수정 (클라이언트는 Networked 속성 동기화로 반영)
        if (!HasStateAuthorityOrNoNetwork()) return;
        if (currentHP <= 0 || amount <= 0) return;
        currentHP = Mathf.Min(currentHP + amount, currentMaxHP);
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
        if (_monsterData == null) return;
        int finalDamage = DamageCalculator.CalculateDamage(baseDamage, damageType, _monsterData.defense, _monsterData.magicResistance);
        currentHP -= finalDamage;
        if (currentHP <= 0) Die();
    }

    #region 3단계 스탯 버프 메서드
    
    /// <summary>
    /// [2단계] 증강체/웨이브 스케일링을 Permanent 스탯에 적용합니다.
    /// 호출 후 Final 스탯도 자동 갱신됩니다.
    /// 주의: 보스 몬스터도 적용받음 (버서커만 면역)
    /// </summary>
    public void ApplyAugmentBuffs(float healthMultiplier, float speedMultiplier, float damageMultiplier = 1f)
    {
        // 보스도 증강체/웨이브 스케일링은 적용받음 (버서커만 면역)
        
        // 2단계: Permanent = Base × 증강체 배수
        _permanentMaxHealth = _baseMaxHealth * healthMultiplier;
        _permanentMoveSpeed = _baseMoveSpeed * speedMultiplier;
        _permanentAttackDamage = _baseAttackDamage * damageMultiplier;
        _permanentAttackSpeed = _baseAttackSpeed; // 공격속도 증강은 현재 없음
        
        // 3단계: Final = Permanent (버프 초기화)
        RefreshFinalStats();
        
        // HP 비율 유지하면서 MaxHP 갱신
        float healthPercentage = currentHP / currentMaxHP;
        currentMaxHP = _permanentMaxHealth;
        currentHP = currentMaxHP * healthPercentage;
        
        OnHealthChanged?.Invoke(currentHP, currentMaxHP);
        // Debug.Log($"<color=cyan>[Monster] '{name}' 증강체 적용: HP {currentMaxHP:F0}, 속도 {_permanentMoveSpeed:F1}, 공격력 {_permanentAttackDamage:F1}</color>");
    }
    
    /// <summary>
    /// [3단계] 웨이브 스케일링 버프를 Final 스탯에 적용합니다. (기존 ApplyBuff 호환용)
    /// </summary>
    public void ApplyBuff(float healthMultiplier, float speedMultiplier, float damageMultiplier = 1f)
    {
        // 보스는 모든 버프에 면역
        if (SnapshotIsBoss) return;
        
        // 3단계: Final = Permanent × 버프 배수
        float healthPercentage = currentHP / currentMaxHP;
        currentMaxHP = _permanentMaxHealth * healthMultiplier;
        currentHP = currentMaxHP * healthPercentage;
        _currentMoveSpeed = _permanentMoveSpeed * speedMultiplier;
        _currentAttackDamage = _permanentAttackDamage * damageMultiplier;
        
        OnHealthChanged?.Invoke(currentHP, currentMaxHP);
        // Debug.Log($"<color=orange>[Monster] '{name}' 웨이브 버프: HP {currentMaxHP:F0}, 속도 {_currentMoveSpeed:F1}, 공격력 {_currentAttackDamage:F1}</color>");
    }
    
    /// <summary>
    /// Final 스탯을 Permanent 기준으로 초기화합니다. (버프 리셋)
    /// </summary>
    private void RefreshFinalStats()
    {
        _currentMoveSpeed = _permanentMoveSpeed;
        _currentAttackDamage = _permanentAttackDamage;
        _currentAttackSpeed = _permanentAttackSpeed;
    }
    
    #endregion

    #region 보스 몬스터 메서드
    /// <summary>
    /// 이 몬스터를 보스로 설정합니다. 살아남아 목표 도달 시 다음 라운드에 전체 유저 중 랜덤 침공합니다.
    /// </summary>
    /// <param name="isBoss">보스 여부</param>
    /// <param name="originPlayerId">보스를 소환한 플레이어 ID</param>
    /// <param name="bossUniqueId">보스 고유 ID (턴당 1회 침공 추적용)</param>
    public void SetAsBoss(bool isBoss, int originPlayerId, int bossUniqueId = -1)
    {
        _isBoss = isBoss;
        _originPlayerId = originPlayerId;
        _bossUniqueId = bossUniqueId;
        if (Object != null && Object.HasStateAuthority)
        {
            NetworkedIsBoss = isBoss;
            NetworkedBossOriginPlayerId = originPlayerId;
            NetworkedBossUniqueId = bossUniqueId;
        }
        
        if (isBoss)
        {
            // Debug.Log($"<color=red>[Monster] '{name}'이 보스로 설정됨 (OriginPlayer: {originPlayerId}, UniqueId: {bossUniqueId})</color>");
        }
    }
    
    /// <summary>
    /// 보스 고유 ID를 반환합니다.
    /// </summary>
    public int GetBossUniqueId() => SnapshotBossUniqueId;

    /// <summary>
    /// 현재 체력을 직접 설정합니다. (생존 보스 재소환 시 사용)
    /// </summary>
    public void SetCurrentHP(float hp, float maxHp)
    {
        currentMaxHP = maxHp;
        currentHP = Mathf.Min(hp, maxHp);
        OnHealthChanged?.Invoke(currentHP, currentMaxHP);
    }

    /// <summary>
    /// 보스 여부를 반환합니다.
    /// </summary>
    public bool IsBoss() => SnapshotIsBoss;
    #endregion

    private void Die()
    {
        // 이미 파괴 중인 오브젝트면 무시
        if (this == null || gameObject == null) return;
        
        // Debug.Log($"{_monsterData.monsterName}이(가) 죽었습니다!");
        
        // 보스가 죽으면 SurvivorBossManager에 알림 (더 이상 다음 라운드에 소환되지 않음)
        if (SnapshotIsBoss && SurvivorBossManager.Instance != null && _monsterData != null)
        {
            SurvivorBossManager.Instance.OnBossDied(_monsterData.monsterName);
        }
        
        if (isBlocked && blockingUnit != null)
        {
            blockingUnit.ReleaseBlockedMonster(this);
        }
        
        // 안전하게 NetworkObject 가져오기 (NetworkBehaviour의 Object 프로퍼티 사용)
        NetworkObject no = Object;
        
        if (no != null && no.Runner != null && no.Runner.IsRunning)
        {
            if (!no.HasStateAuthority)
            {
                return;
            }
            _buffManager?.ClearAllStatusEffects();
            no.Runner.Despawn(no);
            return;
        }
        _buffManager?.ClearAllStatusEffects();
        Destroy(gameObject);
    }

    private void OnDestroy()
    {
        if (manaController != null) manaController.OnManaFull -= ActivateSkill;
        
        // 파괴 시 벽 파괴 이벤트 구독 해제 (이벤트 콜백에서 MissingReferenceException 방지)
        GameEvents.OnWallDestroyed -= OnWallDestroyed;
    }

    /// <summary>
    /// 네트워크 환경에서는 Despawn하여 풀에 반환하고, 로컬에서는 Destroy합니다.
    /// </summary>
    private void DespawnOrDestroy()
    {
        if (this == null || gameObject == null) return;
        
        NetworkObject no = Object;
        if (no != null && no.Runner != null && no.Runner.IsRunning)
        {
            if (no.HasStateAuthority)
            {
                no.Runner.Despawn(no);
            }
            return;
        }
        
        // 네트워크가 없는 순수 로컬 환경에서만 Destroy
        Destroy(gameObject);
    }


    #region 공격 로직
    private void StartAttacking(IEnemy target)
    {
        if (target == null) return;
        StopAllCoroutines();
        resumeCoroutine = null;
        currentBlockerId = 0;
        var blocker = target as MonoBehaviour;
        if (blocker != null)
        {
            currentBlockerId = blocker.GetInstanceID();
            
            // 공격 대상(벽/유닛)을 바라보도록 회전
            Vector3 direction = (blocker.transform.position - transform.position).normalized;
            if (direction != Vector3.zero)
            {
                Quaternion targetRotation = Quaternion.LookRotation(direction);
                transform.rotation = targetRotation;
            }
        }
        isMoving = false;
        
        // 저지 상태: Idle 애니메이션으로 전환
        SetWalkingAnimation(false);
        
        attackCoroutine = StartCoroutine(AttackLoop(target));
    }

    private IEnumerator AttackLoop(IEnemy target)
    {
        while (target != null && (target as MonoBehaviour) != null)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (MPTestCommandLine.IsGameFlowFrozen)
            {
                yield return null;
                continue;
            }
#endif
            if (_buffManager != null && !_buffManager.CanAttack)
            {
                yield return null;
                continue;
            }
            
            // 벽의 체력이 0 이하이면 즉시 종료 (Destroy 전에 감지)
            if (target is IHealth healthTarget && healthTarget.CurrentHealth <= 0)
            {
                break;
            }
            
            yield return new WaitForSeconds(1f / currentAttackSpeed);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            while (MPTestCommandLine.IsGameFlowFrozen)
            {
                yield return null;
            }
#endif

            if ((target as MonoBehaviour) == null) break;
            
            // 다시 한번 체력 확인
            if (target is IHealth healthCheck && healthCheck.CurrentHealth <= 0)
            {
                break;
            }

            _pendingAttackTarget = target;
            _hasPendingAttack = true;
            TriggerAttackAnimation();
            
            yield return new WaitForSeconds(0.5f);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            while (MPTestCommandLine.IsGameFlowFrozen)
            {
                yield return null;
            }
#endif
            if (_hasPendingAttack && _pendingAttackTarget != null)
            {
                ExecutePendingAttack();
            }
        }

        // Debug.Log("공격 대상이 사라졌습니다. 이동을 재개합니다.");
        attackCoroutine = null;
        _hasPendingAttack = false;
        _pendingAttackTarget = null;

        ScheduleResumeFromBlocker();
    }
    
    /// <summary>
    /// 공격 애니메이션의 타격 시점에서 호출됩니다. (Animation Event)
    /// </summary>
    public void AnimEvent_AttackImpact()
    {
        ExecutePendingAttack();
    }
    
    private void ExecutePendingAttack()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (MPTestCommandLine.IsGameFlowFrozen)
        {
            return;
        }
#endif
        if (!_hasPendingAttack || _pendingAttackTarget == null) return;
        
        var targetMono = _pendingAttackTarget as MonoBehaviour;
        if (targetMono == null) 
        {
            _hasPendingAttack = false;
            _pendingAttackTarget = null;
            return;
        }
        
        string targetName = targetMono.name;
        
        // 원거리 몬스터: CombatScheduler를 통해 투사체 예약
        if (_monsterData.attackType == MonsterAttackType.Ranged)
        {
            var scheduler = CombatScheduler.Instance;
            var targetNo = targetMono.GetComponentInParent<NetworkObject>();
            
            if (scheduler != null && scheduler.Runner != null && scheduler.Runner.IsRunning && targetNo != null)
            {
                // firePoint가 있으면 사용, 없으면 기본 오프셋
                Vector3 firePos = firePoint != null ? firePoint.position : transform.position + Vector3.up * 0.5f;
                float projectileSpeed = _monsterData.projectileSpeed > 0 ? _monsterData.projectileSpeed : 20f;
                
                scheduler.ScheduleHit(
                    Object,           // attacker
                    targetNo,         // target
                    firePos,          // 발사 위치
                    currentAttackDamage,
                    _monsterData.damageType,
                    true,             // isRanged
                    true,             // emitVfx
                    projectileSpeed,  // 투사체 속도
                    0f,               // splashRadius (단일 대상)
                    unitLayerMask     // enemyLayerMask
                );
                // Debug.Log($"<color=magenta>{_monsterData.monsterName}이(가) {targetName}을(를) 향해 투사체 발사!</color>");
            }
            else
            {
                // CombatScheduler가 없으면 즉시 데미지
                _pendingAttackTarget.TakeDamage(currentAttackDamage, _monsterData.damageType);
                // Debug.Log($"{_monsterData.monsterName}이(가) {targetName}을(를) 공격!");
            }
        }
        else
        {
            // 근접 몬스터: 기존 로직 (즉시 데미지)
            _pendingAttackTarget.TakeDamage(currentAttackDamage, _monsterData.damageType);
            // Debug.Log($"{_monsterData.monsterName}이(가) {targetName}을(를) 공격!");
        }
        
        _hasPendingAttack = false;
        _pendingAttackTarget = null;
        _rangedTarget = null;
    }

    private void ScheduleResumeFromBlocker()
    {
        int blockerId = currentBlockerId;
        currentBlockerId = 0;

        if (resumeCoroutine != null)
        {
            StopCoroutine(resumeCoroutine);
            resumeCoroutine = null;
        }

        float delay = 0f;
        if (releaseScheduler != null)
        {
            delay = releaseScheduler.ReserveDelay(blockerId);
        }

        if (delay <= 0f)
        {
            FindNewPathToGoal();
            return;
        }

        resumeCoroutine = StartCoroutine(ResumeAfterDelay(delay));
    }

    private IEnumerator ResumeAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        while (MPTestCommandLine.IsGameFlowFrozen)
        {
            yield return null;
        }
#endif
        resumeCoroutine = null;
        FindNewPathToGoal();
    }
    #endregion

    #region 이동 및 경로탐색 로직

    private void FindNewPathToGoal()
    {
        // 이미 파괴된 오브젝트에서 호출된 경우 무시
        if (this == null || gameObject == null) return;
        if (!HasStateAuthorityOrNoNetwork()) return;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (MPTestCommandLine.IsGameFlowFrozen)
        {
            return;
        }
#endif
        if (pathfinder == null || goalTransform == null) return;
        
        Vector2Int currentGridPos = pathfinder.WorldToCell(pathfinder.ClampToGrid(transform.position));
        Vector2Int targetGridPos = pathfinder.WorldToCell(pathfinder.ClampToGrid(goalTransform.position));

        // 파괴자 특성이 있으면 파괴 가능한 벽을 무시하고 최단 경로 탐색
        bool isDestroyer = HasTrait(MonsterTraits.Destroyer);
        
        if (pathfinder.FindPath(currentGridPos, targetGridPos, ignoreWalls: false, ignoreBreakableWalls: isDestroyer))
        {
            List<AstarNode> newPath = pathfinder.FinalPath;
            
            // 경로에 벽이 포함되어 있는지 확인
            int wallCount = 0;
            foreach (var node in newPath)
            {
                if (node.isWall && node.isBreakable)
                {
                    wallCount++;
                }
            }
            
            StartFollowingPath(newPath);
        }
        else
        {
             // Debug.LogWarning($"{_monsterData.monsterName}이(가) 경로를 찾지 못했습니다. 소멸합니다.");
             DespawnOrDestroy();
        }
    }
    
    /// <summary>
    /// 벽이 파괴되었을 때 호출되는 이벤트 핸들러.
    /// 일반 몬스터는 더 짧은 경로가 생겼는지 확인하고 경로를 재탐색합니다.
    /// </summary>
    private void OnWallDestroyed(Vector3Int destroyedWallPosition, FieldManager field)
    {
        // 이미 파괴된 오브젝트에서 호출된 경우 무시
        if (this == null || gameObject == null) return;
        
        // 자신이 속한 필드에서 벽이 파괴된 경우만 처리
        if (ownerPlayer == null || ownerPlayer.fieldManager != field) return;
        
        // 이동 중이고 저지되지 않은 상태에서만 경로 재탐색
        if (!isMoving || isBlocked) return;
        
        // 현재 경로상에 있거나 근처에서 벽이 부서진 경우 경로 재탐색
        // 단순화: 벽이 부서지면 무조건 경로 재탐색 (더 짧은 경로가 있을 수 있음)
        FindNewPathToGoal();
    }

    public void StartFollowingPath(List<AstarNode> path)
    {
        if (Object != null && !Object.HasStateAuthority)
        {
            isMoving = false;
            return;
        }
        StopAllCoroutines();

        isMoving = true;
        
        // 이동 시작: Walk 애니메이션으로 전환
        SetWalkingAnimation(true);
        
        if (_monsterData.monsterType == MonsterType.Flying)
        {
            movementCoroutine = StartCoroutine(FlyDirectlyCoroutine());
        }
        else if (path != null && path.Count > 0)
        {
            movementCoroutine = StartCoroutine(SmoothMoveCoroutine(path));
        }
        else
        {
            isMoving = false;
            SetWalkingAnimation(false);
            OnPathBlocked(null);
        }
    }


    private IEnumerator FlyDirectlyCoroutine()
    {
        Vector3 targetPosition = new Vector3(
            goalTransform.position.x,
            transform.position.y,
            goalTransform.position.z
        );
        while (Vector3.Distance(transform.position, targetPosition) > 0.1f && isMoving)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (MPTestCommandLine.IsGameFlowFrozen)
            {
                SetWalkingAnimation(false);
                yield return null;
                continue;
            }
#endif
            // 버프로 이동 불가 또는 원거리 공격 중이면 대기
            if ((_buffManager != null && !_buffManager.CanMove) || _isRangedAttacking)
            {
                SetWalkingAnimation(false);
                yield return null;
                continue;
            }
            
            float dt = (Runner != null) ? Runner.DeltaTime : Time.deltaTime;
            Vector3 nextPos = Vector3.MoveTowards(
                transform.position,
                targetPosition,
                currentMoveSpeed * dt
            );
            if (pathfinder != null)
            {
                nextPos = pathfinder.ClampToGrid(nextPos);
            }
            
            RotateTowardsMovementDirection(targetPosition, dt);
            
            transform.position = nextPos;
            yield return null;
        }
        OnPathCompleted();
    }
    private IEnumerator SmoothMoveCoroutine(List<AstarNode> path)
    {
        int currentPathIndex = 1;
        while (currentPathIndex < path.Count && isMoving)
        {
            AstarNode targetNode = path[currentPathIndex];
            Vector3 currentTarget = (pathfinder != null)
                ? pathfinder.CellToWorldCenter(new Vector2Int(targetNode.x, targetNode.y), transform.position.y)
                : new Vector3(targetNode.x + 0.5f, transform.position.y, targetNode.y + 0.5f);

            if (targetNode.isWall)
            {
                float radius = 0.4f * ((pathfinder != null) ? Mathf.Max(0.0001f, pathfinder.cellSize) : 1f);
                Collider[] wallColliders = Physics.OverlapSphere(currentTarget, radius, wallLayerMask);
                DestructibleWall wall = null;
                foreach (var col in wallColliders)
                {
                    if (col.TryGetComponent(out wall)) break;
                }

                if (wall != null)
                {
                    StartAttacking(wall);
                    yield break;
                }
            }

            while (Vector3.Distance(transform.position, currentTarget) > 0.1f && isMoving)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (MPTestCommandLine.IsGameFlowFrozen)
                {
                    SetWalkingAnimation(false);
                    yield return null;
                    continue;
                }
#endif
                // 버프로 이동 불가 또는 원거리 공격 중이면 대기
                if ((_buffManager != null && !_buffManager.CanMove) || _isRangedAttacking)
                {
                    SetWalkingAnimation(false);
                    yield return null;
                    continue;
                }
                
                float dt = (Runner != null) ? Runner.DeltaTime : Time.deltaTime;
                Vector3 nextPos = Vector3.MoveTowards(transform.position, currentTarget, currentMoveSpeed * dt);
                if (pathfinder != null)
                {
                    nextPos = pathfinder.ClampToGrid(nextPos);
                }
                
                RotateTowardsMovementDirection(currentTarget, dt);
                
                transform.position = nextPos;

                if (!isBlocked && _monsterData.monsterType != MonsterType.Flying && !HasTrait(MonsterTraits.Unblockable))
                {
                    float moveDistance = currentMoveSpeed * dt;
                    float detectionRadius = Mathf.Max(0.6f, moveDistance + 0.3f);
                    float blockDistance = 0.6f;
                    
                    Collider[] nearbyUnits = Physics.OverlapSphere(nextPos, detectionRadius);
                    foreach (var col in nearbyUnits)
                    {
                        if (col.TryGetComponent<Unit>(out var unit) &&
                            unit.Data.blockCount > 0 &&
                            unit.Data.unitType == UnitType.Melee &&
                            !unit.IsBlockingFull())
                        {
                            float distToUnit = Vector3.Distance(nextPos, unit.transform.position);
                            if (distToUnit <= blockDistance)
                            {
                                if (unit.TryBlockMonster(this))
                                {
                                    break;
                                }
                            }
                        }
                    }
                }

                yield return null;
            }
            currentPathIndex++;
        }
        OnPathCompleted();
    }
    private void OnPathCompleted()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (MPTestCommandLine.IsGameFlowFrozen)
        {
            return;
        }
#endif
        isMoving = false;
        SetWalkingAnimation(false);
        
        // 보스가 목표 도달 시 생존 등록 (중복 등록 방지 플래그 체크)
        if (SnapshotIsBoss && !_hasRegisteredAsSurvivor && SurvivorBossManager.Instance != null && _monsterData != null)
        {
            _hasRegisteredAsSurvivor = true;
            SurvivorBossManager.Instance.RegisterSurvivorBoss(
                _monsterData,
                currentHP,
                currentMaxHP,
                SnapshotBossOriginPlayerId,
                SnapshotBossUniqueId
            );
            // Debug.Log($"<color=red>[Monster] 보스 '{name}' 목표 도달 → 생존 등록 (HP: {currentHP:F0}/{currentMaxHP:F0})</color>");
        }
        
        if (GameManagers.Instance != null && ownerPlayer != null)
        {
            GameManagers.Instance.OnMonsterReachedGoal(ownerPlayer);
        }
        DespawnOrDestroy();
    }
    #endregion

    #region 저지 및 경로 막힘 처리
    public bool IsBlocked() { return isBlocked; }

    public void Block(Unit unit)
    {
        if (isBlocked) return;
        isBlocked = true;
        blockingUnit = unit;
        StartAttacking(unit.GetComponent<IEnemy>());
    }

    public void OnPathBlocked(GameObject obstacle)
    {
        if (obstacle != null && obstacle.TryGetComponent<IEnemy>(out var enemyWall))
        {
            StartAttacking(enemyWall);
        }
    }

    public void Unblock()
    {
        if (isQuitting || !isBlocked) return;
        if (!HasStateAuthorityOrNoNetwork()) return;

        isBlocked = false;
        blockingUnit = null;

        if (attackCoroutine != null)
        {
            StopCoroutine(attackCoroutine);
            attackCoroutine = null;
        }

        ScheduleResumeFromBlocker();
    }
    #endregion

    #region 전투 종료 처리

    /// <summary>
    /// 유저 HP 패널티 없이 몬스터를 강제 제거합니다. (전투 타이머 만료 시 사용)
    /// </summary>
    public void ForceRemoveWithoutPenalty()
    {
        if (this == null || gameObject == null) return;
        
        // Debug.Log($"<color=gray>[Monster] '{name}' 강제 제거 (전투 종료)</color>");
        
        // 블로킹 상태 해제
        if (isBlocked && blockingUnit != null)
        {
            blockingUnit.ReleaseBlockedMonster(this);
        }
        
        // 코루틴 정지
        StopAllCoroutines();
        
        // 네트워크 또는 로컬 제거
        NetworkObject no = Object;
        if (no != null && no.Runner != null && no.Runner.IsRunning)
        {
            if (no.HasStateAuthority)
            {
                no.Runner.Despawn(no);
            }
        }
        else
        {
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// 보스를 생존 등록 후 제거합니다. (전투 타이머 만료 시 사용)
    /// 유저 HP 패널티 없이 다음 라운드에 재배치됩니다.
    /// </summary>
    public void RegisterAsSurvivorAndRemove()
    {
        if (this == null || gameObject == null) return;
        
        // 이미 등록된 경우 중복 등록 방지 (목표 도달 시 이미 등록된 경우)
        if (_hasRegisteredAsSurvivor)
        {
            // Debug.Log($"<color=yellow>[Monster] 보스 '{name}' 이미 생존 등록됨 → 스킵</color>");
            ForceRemoveWithoutPenalty();
            return;
        }
        
        // 네트워크 상태와 관계없이 안전하게 HP 값 접근
        float safeCurrentHP = _hasSpawned && CanWriteNetworkedHealth() ? NetworkedHP : _localHP;
        float safeMaxHP = _hasSpawned && CanWriteNetworkedHealth() ? NetworkedMaxHP : _localMaxHP;
        
        // 유효하지 않은 HP 값이면 MonsterData에서 가져옴
        if (safeMaxHP <= 0 && _monsterData != null)
        {
            safeMaxHP = _monsterData.maxHealth;
            safeCurrentHP = safeMaxHP; // 기본값으로 풀피 사용
        }
        
        // Debug.Log($"<color=red>[Monster] 보스 '{name}' 생존 등록 (전투 종료, HP: {safeCurrentHP:F0}/{safeMaxHP:F0}, UniqueId: {_bossUniqueId})</color>");
        
        // SurvivorBossManager에 생존 등록 (고유 ID 유지)
        if (SurvivorBossManager.Instance != null && _monsterData != null)
        {
            _hasRegisteredAsSurvivor = true;
            SurvivorBossManager.Instance.RegisterSurvivorBoss(
                _monsterData,
                safeCurrentHP,
                safeMaxHP,
                SnapshotBossOriginPlayerId,
                SnapshotBossUniqueId
            );
        }
        
        // HP 패널티 없이 제거
        ForceRemoveWithoutPenalty();
    }

    #endregion

    #region 이동속도 수정 (디버프 지원)

    /// <summary>
    /// BuffManager에서 호출하여 이동속도 수정자를 적용합니다.
    /// </summary>
    /// <param name="speedMultiplier">이동속도 배율 (1.0 = 기본, 0.5 = 50% 감소)</param>
    public void ApplyMoveSpeedModifier(float speedMultiplier)
    {
        _currentMoveSpeed = _permanentMoveSpeed * speedMultiplier;
        UpdateMoveAnimationSpeed(); // 애니메이션 속도도 업데이트
        // Debug.Log($"<color=cyan>[Monster] '{name}' 이동속도 변경: {_currentMoveSpeed:F2} (x{speedMultiplier:F2})</color>");
    }

    /// <summary>
    /// 기본 이동속도를 반환합니다. (BuffManager 스탯 계산용)
    /// </summary>
    public float GetBaseMoveSpeed() => _permanentMoveSpeed;

    #endregion

    #region 폭주 모드

    /// <summary>
    /// 폭주 모드를 적용합니다. (전투 종료 5초 전)
    /// [수정] 기본값이 아닌 현재값 기준으로 배수 적용 (증강체 버프 유지)
    /// </summary>
    public void ApplyBerserkMode()
    {
        // 보스는 모든 버프에 면역
        if (SnapshotIsBoss) return;
        
        // [3단계] Permanent 기준으로 버서커 배수 적용
        _currentMoveSpeed = _permanentMoveSpeed * 2f;
        _currentAttackDamage = _permanentAttackDamage * 1.5f;
        _currentAttackSpeed = _permanentAttackSpeed * 1.5f;
        
        // 애니메이션 속도 업데이트
        UpdateMoveAnimationSpeed();
        
        // Debug.Log($"<color=red>[Monster] '{name}' 폭주 모드 발동! (공속 1.5배, 공격력 1.5배, 이속 2배)</color>");
    }

    #endregion

    #region 애니메이션 헬퍼

    /// <summary>
    /// Animator 참조를 확인하고 MonsterAnimationEventProxy를 설정합니다.
    /// </summary>
    private void EnsureAnimator()
    {
        if (animator == null)
        {
            animator = GetComponent<Animator>();
            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>();
            }
        }

        if (animator == null)
        {
            return;
        }
        
        // 화면 밖 오브젝트의 CPU 부하 감소 (Transform 업데이트만 건너뜀, 상태머신은 계속 실행)
        animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

        // MonsterAnimationEventProxy 설정 (Animator가 어디에 있든 설정)
        GameObject animatorObj = animator.gameObject;
        var proxy = animatorObj.GetComponent<MonsterAnimationEventProxy>();
        if (proxy == null)
        {
            proxy = animatorObj.AddComponent<MonsterAnimationEventProxy>();
        }
        proxy.Initialize(this);
    }

    /// <summary>
    /// Walking 애니메이션 상태를 설정하고 이동속도에 따른 애니메이션 속도를 업데이트합니다.
    /// </summary>
    private void SetWalkingAnimation(bool isWalking)
    {
        if (animator == null || string.IsNullOrEmpty(isWalkingParam))
        {
            return;
        }

        animator.SetBool(isWalkingParam, isWalking);
        
        // 이동속도에 비례하여 애니메이션 속도 조절 (Walk/Idle 모두 적용)
        UpdateMoveAnimationSpeed();
        
        // 네트워크 환경에서 클라이언트에게 이동 애니메이션 동기화
        if (Object != null && Runner != null && Runner.IsRunning && Object.HasStateAuthority)
        {
            float speedRatio = (_permanentMoveSpeed > 0f) ? _currentMoveSpeed / _permanentMoveSpeed : 1f;
            RPC_SetWalkingAnimation(isWalking, speedRatio);
        }
    }
    
    /// <summary>
    /// 이동 애니메이션을 모든 클라이언트에 동기화합니다.
    /// </summary>
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_SetWalkingAnimation(bool isWalking, float speedRatio)
    {
        // 서버/호스트는 이미 SetWalkingAnimation()에서 실행했으므로 무시
        if (Object != null && Object.HasStateAuthority) return;
        
        if (animator == null || string.IsNullOrEmpty(isWalkingParam)) return;
        
        animator.SetBool(isWalkingParam, isWalking);
        
        // 이동속도 설정
        if (!string.IsNullOrEmpty(moveSpeedParam))
        {
            animator.SetFloat(moveSpeedParam, speedRatio);
        }
    }
    
    /// <summary>
    /// 이동속도에 비례하여 애니메이션 속도를 업데이트합니다.
    /// </summary>
    private void UpdateMoveAnimationSpeed()
    {
        if (animator == null || string.IsNullOrEmpty(moveSpeedParam)) return;
        if (_permanentMoveSpeed <= 0f) return;
        
        float speedRatio = _currentMoveSpeed / _permanentMoveSpeed;
        animator.SetFloat(moveSpeedParam, speedRatio);
    }

    /// <summary>
    /// 공격 애니메이션을 트리거하고 공격속도에 따른 애니메이션 속도를 적용합니다.
    /// RPC 대신 Networked 속성을 사용하여 네트워크 부하를 감소시킵니다.
    /// </summary>
    private void TriggerAttackAnimation()
    {
        if (animator == null || string.IsNullOrEmpty(attackTriggerParam))
        {
            return;
        }
        
        // 공격속도에 비례하여 애니메이션 속도 조절
        float attackSpeedRatio = 1f;
        if (!string.IsNullOrEmpty(attackSpeedParam) && _monsterData != null && _monsterData.attackSpeed > 0f)
        {
            attackSpeedRatio = currentAttackSpeed / _monsterData.attackSpeed;
            animator.SetFloat(attackSpeedParam, attackSpeedRatio);
        }

        animator.ResetTrigger(attackTriggerParam);
        animator.SetTrigger(attackTriggerParam);
        
        // 네트워크 환경에서 Networked 속성 변경으로 클라이언트에 동기화
        // ChangeDetector가 자동 감지하여 HandleNetworkedAttackTriggered() 호출
        if (Object != null && Runner != null && Runner.IsRunning && Object.HasStateAuthority)
        {
            NetworkedAttackSpeedRatio = attackSpeedRatio;
            NetworkedAttackTrigger++; // 값 변경으로 ChangeDetector 트리거
        }
    }

    /// <summary>
    /// 이동 방향을 바라보도록 몬스터를 회전시킵니다.
    /// </summary>
    private void RotateTowardsMovementDirection(Vector3 targetPosition, float deltaTime)
    {
        Vector3 direction = targetPosition - transform.position;
        direction.y = 0f; // Y축 회전만 적용 (위에서 아래로 보는 시점)

        if (direction.sqrMagnitude < 0.001f)
        {
            return;
        }

        Quaternion targetRotation = Quaternion.LookRotation(direction);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * deltaTime);
    }

    #endregion
}
