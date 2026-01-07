// Assets/Scripts/Game/Monsters/Monster.cs
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Fusion;

public class Monster : NetworkBehaviour, IEnemy, IHealth
{
    [Header("참조 데이터")]
    [SerializeField] private MonsterData _monsterData;
    public MonsterData Data => _monsterData;

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
    #endregion

    // === 현재 상태 (Networked) ===
    // [Networked] 속성으로 서버/클라이언트 간 HP 동기화
    [Networked] public float NetworkedHP { get; set; }
    [Networked] public float NetworkedMaxHP { get; set; }

    private bool _hasSpawned;
    private bool _hasLocalHealthValues;
    private float _localHP;
    private float _localMaxHP;
    
    // 로컬 접근용 프로퍼티 (IHealth 인터페이스 호환성 유지)
    public float currentHP
    {
        get => _hasSpawned ? NetworkedHP : _localHP;
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
        get => _hasSpawned ? NetworkedMaxHP : _localMaxHP;
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

    public float CurrentHealth => _hasSpawned ? NetworkedHP : _localHP;
    public float MaxHealth => _hasSpawned ? NetworkedMaxHP : _localMaxHP;
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
    private float baseMoveSpeed;
    private float currentMoveSpeed;
    private float currentAttackDamage; // 공격력 스케일링 지원
    private float currentAttackSpeed;  // 공격속도 스케일링 지원
    private MonsterReleaseScheduler releaseScheduler;
    private Coroutine resumeCoroutine;
    private int currentBlockerId = 0;
    private ChangeDetector _changeDetector;

    #region 보스 몬스터 관련
    // 보스 몬스터 플래그 및 생존 시 다음 라운드 침공을 위한 정보
    private bool _isBoss = false;
    private int _originPlayerId = -1;
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
        _hasSpawned = true;
        _changeDetector = GetChangeDetector(ChangeDetector.Source.SimulationState);
        TryApplyPendingHealthToNetworked();
        // StatusBarUI 생성은 Initialize()에서 처리합니다.
        // Spawned()는 statusBarPrefab이 할당되기 전에 호출되므로 여기서는 생성하지 않습니다.
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
        }
    }

    private bool CanWriteNetworkedHealth()
    {
        return _hasSpawned
            && Runner != null
            && Runner.IsRunning
            && Object != null
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

    void OnApplicationQuit() { isQuitting = true; }

    public void SetStatusBar(StatusBarUI ui)
    {
        this.statusBarUI = ui;
    }

    public void Initialize(PlayerManager owner, Transform goal, MonsterData data, AstarGrid pathfinder)
    {
        this.ownerPlayer = owner;
        this.goalTransform = goal;
        this._monsterData = data;
        this.pathfinder = pathfinder;
        this.releaseScheduler = owner != null ? owner.GetComponentInChildren<MonsterReleaseScheduler>(true) : null;
        this.name = _monsterData.monsterName;
        this.wallLayerMask = pathfinder.wallLayers;
        baseMoveSpeed = _monsterData.moveSpeed;
        currentMoveSpeed = baseMoveSpeed;
        currentAttackDamage = _monsterData.attackDamage; // 초기화
        currentAttackSpeed = _monsterData.attackSpeed;   // 초기화

        // [Fix] 오브젝트 재사용 시 이전 상태 초기화
        isBlocked = false;
        blockingUnit = null;
        isMoving = false;
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

        currentMaxHP = _monsterData.maxHealth;
        currentHP = currentMaxHP;
        
        // StatusBarUI 생성 (statusBarPrefab이 이미 할당된 상태)
        EnsureStatusBarUI();
        
        OnHealthChanged?.Invoke(currentHP, currentMaxHP);

        manaController = GetComponent<ManaController>();

        int maxMana = 0;
        if (_monsterData.skillData != null)
        {
            maxMana = _monsterData.skillData.manaCost;
            manaController.OnManaFull += ActivateSkill;
        }
        manaController.Initialize(maxMana);
        
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
            Debug.Log($"<color=cyan>[Monster] {name}: StatusBarUI 생성 (HasStateAuthority={(Object != null ? Object.HasStateAuthority.ToString() : "N/A")})</color>");
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
        
        Debug.Log($"<color=yellow>[Monster.RPC_InitializeOnClient] {name}: 클라이언트 초기화 시작 (monsterDataName={monsterDataName})</color>");
        
        StartCoroutine(InitializeOnClientCoroutine(ownerPlayerId, monsterDataName));
    }
    
    private System.Collections.IEnumerator InitializeOnClientCoroutine(NetworkId ownerPlayerId, string monsterDataName)
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
                yield return null;
                attempts++;
            }
        }
        
        PlayerManager owner = ownerNO != null ? ownerNO.GetComponent<PlayerManager>() : null;
        
        if (owner == null)
        {
            Debug.LogWarning($"[Monster.RPC_InitializeOnClient] ownerPlayer를 찾을 수 없습니다.");
            // StatusBarUI만이라도 생성
            EnsureStatusBarUI();
            yield break;
        }
        
        // MonsterData 로드 (프리팩에 이미 할당되어 있거나 Addressables에서 로드)
        if (_monsterData == null && !string.IsNullOrEmpty(monsterDataName))
        {
            // 프리팩의 _monsterData가 있으면 사용
            var prefabMonster = GetComponent<Monster>();
            if (prefabMonster != null && prefabMonster._monsterData != null)
            {
                _monsterData = prefabMonster._monsterData;
            }
        }
        
        // 필수 참조 설정
        this.ownerPlayer = owner;
        this.goalTransform = owner.goalTransform;
        this.pathfinder = owner.astarGrid;
        
        if (_monsterData != null)
        {
            this.name = _monsterData.monsterName;
            this.wallLayerMask = pathfinder != null ? pathfinder.wallLayers : default;
            baseMoveSpeed = _monsterData.moveSpeed;
            currentMoveSpeed = baseMoveSpeed;
            currentAttackDamage = _monsterData.attackDamage; // 초기화
            currentMaxHP = _monsterData.maxHealth;
            currentHP = currentMaxHP;
        }
        
        // StatusBarUI 생성
        EnsureStatusBarUI();
        
        OnHealthChanged?.Invoke(currentHP, currentMaxHP);
        
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
        
        Debug.Log($"<color=cyan>[Monster.RPC_InitializeOnClient] {name}: 클라이언트 초기화 완료</color>");
    }

    void Update()
    {
        if (_monsterData != null && _monsterData.skillData != null)
        {
            if (!HasStateAuthorityOrNoNetwork())
            {
                return;
            }
            manaController.GainManaOverTime(10f);
        }
    }

    private void ActivateSkill()
    {
        if (!HasStateAuthorityOrNoNetwork())
        {
            return;
        }
        SkillData skillData = _monsterData.skillData;

        if (skillData == null || skillData.targetingStrategy == null || skillData.effects.Count == 0)
        {
            Debug.LogError($"{_monsterData.monsterName}의 SkillData 또는 그 내용이 올바르게 설정되지 않았습니다.");
            return;
        }

        if (!manaController.IsManaFull) return;

        if (manaController.UseMana(skillData.manaCost))
        {
            Debug.Log($"<color=magenta>{_monsterData.monsterName} 스킬 발동: {skillData.skillName}</color>");

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
                    Debug.LogWarning($"VFX 프리팹 '{vfxInstance.name}'에 VFXAutoDestroy.cs 컴포넌트가 없습니다. 자동으로 파괴되지 않습니다.");
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
        if (_monsterData == null) return;
        int finalDamage = DamageCalculator.CalculateDamage(baseDamage, damageType, _monsterData.defense, _monsterData.magicResistance);
        currentHP -= finalDamage;
        if (currentHP <= 0) Die();
    }

    public void ApplyBuff(float healthMultiplier, float speedMultiplier, float damageMultiplier = 1f)
    {
        // 보스는 모든 버프에 면역
        if (_isBoss) return;
        
        float healthPercentage = currentHP / currentMaxHP;
        currentMaxHP = _monsterData.maxHealth * healthMultiplier;
        currentHP = currentMaxHP * healthPercentage;
        currentMoveSpeed = baseMoveSpeed * speedMultiplier;
        currentAttackDamage = _monsterData.attackDamage * damageMultiplier; // 공격력 스케일링 적용
        
        OnHealthChanged?.Invoke(currentHP, currentMaxHP);
        Debug.Log($"<color=orange>{gameObject.name} 강화: HP {currentHP:F0}/{currentMaxHP:F0}, 속도 {currentMoveSpeed:F1}, 공격력 {currentAttackDamage:F1}</color>");
    }

    #region 보스 몬스터 메서드
    /// <summary>
    /// 이 몬스터를 보스로 설정합니다. 살아남아 목표 도달 시 다음 라운드에 전체 유저 중 랜덤 침공합니다.
    /// </summary>
    /// <param name="isBoss">보스 여부</param>
    /// <param name="originPlayerId">보스를 소환한 플레이어 ID</param>
    public void SetAsBoss(bool isBoss, int originPlayerId)
    {
        _isBoss = isBoss;
        _originPlayerId = originPlayerId;
        
        if (isBoss)
        {
            Debug.Log($"<color=red>[Monster] '{name}'이 보스로 설정됨 (OriginPlayer: {originPlayerId})</color>");
        }
    }

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
    public bool IsBoss() => _isBoss;
    #endregion

    private void Die()
    {
        // 이미 파괴 중인 오브젝트면 무시
        if (this == null || gameObject == null) return;
        
        Debug.Log($"{_monsterData.monsterName}이(가) 죽었습니다!");
        
        // 보스가 죽으면 SurvivorBossManager에 알림 (더 이상 다음 라운드에 소환되지 않음)
        if (_isBoss && SurvivorBossManager.Instance != null)
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
            no.Runner.Despawn(no);
            return;
        }
        Destroy(gameObject);
    }

    private void OnDestroy()
    {
        if (manaController != null) manaController.OnManaFull -= ActivateSkill;
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
            yield return new WaitForSeconds(1f / currentAttackSpeed);

            if ((target as MonoBehaviour) == null) break;

            // 공격 애니메이션 트리거 + 대기 공격 설정
            _pendingAttackTarget = target;
            _hasPendingAttack = true;
            TriggerAttackAnimation();
            
            // 애니메이션 이벤트가 없는 경우를 대비한 폴백 (0.5초 후에도 대기 중이면 직접 데미지)
            yield return new WaitForSeconds(0.5f);
            if (_hasPendingAttack && _pendingAttackTarget != null)
            {
                ExecutePendingAttack();
            }
        }

        Debug.Log("공격 대상이 사라졌습니다. 이동을 재개합니다.");
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
        if (!_hasPendingAttack || _pendingAttackTarget == null) return;
        
        var targetMono = _pendingAttackTarget as MonoBehaviour;
        if (targetMono == null) 
        {
            _hasPendingAttack = false;
            _pendingAttackTarget = null;
            return;
        }
        
        string targetName = targetMono.name;
        _pendingAttackTarget.TakeDamage(currentAttackDamage, _monsterData.damageType);
        Debug.Log($"{_monsterData.monsterName}이(가) {targetName}을(를) 공격!");
        
        _hasPendingAttack = false;
        _pendingAttackTarget = null;
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
        resumeCoroutine = null;
        FindNewPathToGoal();
    }
    #endregion

    #region 이동 및 경로탐색 로직

    private void FindNewPathToGoal()
    {
        if (!HasStateAuthorityOrNoNetwork()) return;
        if (pathfinder == null || goalTransform == null) return;
        
        Vector2Int currentGridPos = pathfinder.WorldToCell(pathfinder.ClampToGrid(transform.position));
        Vector2Int targetGridPos = pathfinder.WorldToCell(pathfinder.ClampToGrid(goalTransform.position));

        if (pathfinder.FindPath(currentGridPos, targetGridPos))
        {
            List<AstarNode> newPath = pathfinder.FinalPath;
            StartFollowingPath(newPath);
        }
        else
        {
             Debug.LogWarning($"{_monsterData.monsterName}이(가) 경로를 찾지 못했습니다. 소멸합니다.");
             DespawnOrDestroy();
        }
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
            
            // 이동 방향으로 회전
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
                float dt = (Runner != null) ? Runner.DeltaTime : Time.deltaTime;
                Vector3 nextPos = Vector3.MoveTowards(transform.position, currentTarget, currentMoveSpeed * dt);
                if (pathfinder != null)
                {
                    nextPos = pathfinder.ClampToGrid(nextPos);
                }
                
                // 이동 방향으로 회전
                RotateTowardsMovementDirection(currentTarget, dt);
                
                transform.position = nextPos;

                if (!isBlocked && _monsterData.monsterType != MonsterType.Flying)
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
        isMoving = false;
        SetWalkingAnimation(false);
        
        if (_isBoss && SurvivorBossManager.Instance != null)
        {
            SurvivorBossManager.Instance.RegisterSurvivorBoss(
                _monsterData,
                currentHP,
                currentMaxHP,
                _originPlayerId
            );
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
        
        Debug.Log($"<color=gray>[Monster] '{name}' 강제 제거 (전투 종료)</color>");
        
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
        
        Debug.Log($"<color=red>[Monster] 보스 '{name}' 생존 등록 (전투 종료, HP: {currentHP:F0}/{currentMaxHP:F0})</color>");
        
        // SurvivorBossManager에 생존 등록
        if (SurvivorBossManager.Instance != null && _monsterData != null)
        {
            SurvivorBossManager.Instance.RegisterSurvivorBoss(
                _monsterData,
                currentHP,
                currentMaxHP,
                _originPlayerId
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
        currentMoveSpeed = baseMoveSpeed * speedMultiplier;
        UpdateMoveAnimationSpeed(); // 애니메이션 속도도 업데이트
        Debug.Log($"<color=cyan>[Monster] '{name}' 이동속도 변경: {currentMoveSpeed:F2} (x{speedMultiplier:F2})</color>");
    }

    /// <summary>
    /// 기본 이동속도를 반환합니다. (BuffManager 스탯 계산용)
    /// </summary>
    public float GetBaseMoveSpeed() => baseMoveSpeed;

    #endregion

    #region 폭주 모드

    /// <summary>
    /// 폭주 모드를 적용합니다. (전투 종료 5초 전)
    /// </summary>
    public void ApplyBerserkMode()
    {
        // 보스는 모든 버프에 면역
        if (_isBoss) return;
        
        currentMoveSpeed = baseMoveSpeed * 2f;
        currentAttackDamage = _monsterData.attackDamage * 1.5f;
        currentAttackSpeed = _monsterData.attackSpeed * 1.5f;
        
        // 애니메이션 속도 업데이트
        UpdateMoveAnimationSpeed();
        
        Debug.Log($"<color=red>[Monster] '{name}' 폭주 모드 발동! (공속 1.5배, 공격력 1.5배, 이속 2배)</color>");
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

        // Animator가 자식 오브젝트에 있는 경우 EventProxy 설정
        if (animator.gameObject != gameObject)
        {
            var proxy = animator.GetComponent<MonsterAnimationEventProxy>();
            if (proxy == null)
            {
                proxy = animator.gameObject.AddComponent<MonsterAnimationEventProxy>();
            }
            proxy.Initialize(this);
        }
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
    }
    
    /// <summary>
    /// 이동속도에 비례하여 애니메이션 속도를 업데이트합니다.
    /// </summary>
    private void UpdateMoveAnimationSpeed()
    {
        if (animator == null || string.IsNullOrEmpty(moveSpeedParam)) return;
        if (baseMoveSpeed <= 0f) return;
        
        float speedRatio = currentMoveSpeed / baseMoveSpeed;
        animator.SetFloat(moveSpeedParam, speedRatio);
    }

    /// <summary>
    /// 공격 애니메이션을 트리거하고 공격속도에 따른 애니메이션 속도를 적용합니다.
    /// </summary>
    private void TriggerAttackAnimation()
    {
        if (animator == null || string.IsNullOrEmpty(attackTriggerParam))
        {
            return;
        }
        
        // 공격속도에 비례하여 애니메이션 속도 조절
        if (!string.IsNullOrEmpty(attackSpeedParam) && _monsterData != null && _monsterData.attackSpeed > 0f)
        {
            float attackSpeedRatio = currentAttackSpeed / _monsterData.attackSpeed;
            animator.SetFloat(attackSpeedParam, attackSpeedRatio);
        }

        animator.ResetTrigger(attackTriggerParam);
        animator.SetTrigger(attackTriggerParam);
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
