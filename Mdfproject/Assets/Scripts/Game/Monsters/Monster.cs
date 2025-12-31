// Assets/Scripts/Game/Monsters/Monster.cs
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Fusion;

public class Monster : NetworkBehaviour, IEnemy, IHealth
{
    [Header("참조 데이터")]
    public MonsterData monsterData;

    [Tooltip("공격하거나 파괴할 수 있는 벽의 레이어를 설정해야 합니다.")]
    public LayerMask wallLayerMask;

    [Header("StatusBar 설정")]
    [Tooltip("StatusBar 프리팹 참조 (MonsterSpawner에서 전달받음)")]
    public GameObject statusBarPrefab;

    // === 현재 상태 (Networked) ===
    // [Networked] 속성으로 서버/클라이언트 간 HP 동기화
    [Networked] public float NetworkedHP { get; set; }
    [Networked] public float NetworkedMaxHP { get; set; }
    
    // 로컬 접근용 프로퍼티 (IHealth 인터페이스 호환성 유지)
    public float currentHP
    {
        get => NetworkedHP;
        set => NetworkedHP = value;
    }
    private float currentMaxHP
    {
        get => NetworkedMaxHP;
        set => NetworkedMaxHP = value;
    }

    public float CurrentHealth => NetworkedHP;
    public float MaxHealth => NetworkedMaxHP;
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
    private MonsterReleaseScheduler releaseScheduler;
    private Coroutine resumeCoroutine;
    private int currentBlockerId = 0;
    private bool isInitialized = false;
    private ChangeDetector _changeDetector;

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

    void OnApplicationQuit() { isQuitting = true; }

    public void SetStatusBar(StatusBarUI ui)
    {
        this.statusBarUI = ui;
    }

    public void Initialize(PlayerManager owner, Transform goal, MonsterData data, AstarGrid pathfinder)
    {
        this.ownerPlayer = owner;
        this.goalTransform = goal;
        this.monsterData = data;
        this.pathfinder = pathfinder;
        this.releaseScheduler = owner != null ? owner.GetComponentInChildren<MonsterReleaseScheduler>(true) : null;
        this.name = monsterData.monsterName;
        this.wallLayerMask = pathfinder.wallLayers;
        baseMoveSpeed = monsterData.moveSpeed;
        currentMoveSpeed = baseMoveSpeed;

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

        currentMaxHP = monsterData.maxHealth;
        currentHP = currentMaxHP;
        
        // StatusBarUI 생성 (statusBarPrefab이 이미 할당된 상태)
        EnsureStatusBarUI();
        
        OnHealthChanged?.Invoke(currentHP, currentMaxHP);

        manaController = GetComponent<ManaController>();

        int maxMana = 0;
        if (monsterData.skillData != null)
        {
            maxMana = monsterData.skillData.manaCost;
            manaController.OnManaFull += ActivateSkill;
        }
        manaController.Initialize(maxMana);
        
        isInitialized = true;
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
        
        // MonsterData 로드 (프리팹에 이미 할당되어 있거나 Addressables에서 로드)
        if (monsterData == null && !string.IsNullOrEmpty(monsterDataName))
        {
            // 프리팹의 monsterData가 있으면 사용
            var prefabMonster = GetComponent<Monster>();
            if (prefabMonster != null && prefabMonster.monsterData != null)
            {
                monsterData = prefabMonster.monsterData;
            }
        }
        
        // 필수 참조 설정
        this.ownerPlayer = owner;
        this.goalTransform = owner.goalTransform;
        this.pathfinder = owner.astarGrid;
        
        if (monsterData != null)
        {
            this.name = monsterData.monsterName;
            this.wallLayerMask = pathfinder != null ? pathfinder.wallLayers : default;
            baseMoveSpeed = monsterData.moveSpeed;
            currentMoveSpeed = baseMoveSpeed;
            currentMaxHP = monsterData.maxHealth;
            currentHP = currentMaxHP;
        }
        
        // StatusBarUI 생성
        EnsureStatusBarUI();
        
        OnHealthChanged?.Invoke(currentHP, currentMaxHP);
        
        manaController = GetComponent<ManaController>();
        if (manaController != null && monsterData != null)
        {
            int maxMana = 0;
            if (monsterData.skillData != null)
            {
                maxMana = monsterData.skillData.manaCost;
            }
            manaController.Initialize(maxMana);
        }
        
        isInitialized = true;
        Debug.Log($"<color=cyan>[Monster.RPC_InitializeOnClient] {name}: 클라이언트 초기화 완료</color>");
    }

    void Update()
    {
        if (monsterData != null && monsterData.skillData != null)
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
        SkillData skillData = monsterData.skillData;

        if (skillData == null || skillData.targetingStrategy == null || skillData.effects.Count == 0)
        {
            Debug.LogError($"{monsterData.monsterName}의 SkillData 또는 그 내용이 올바르게 설정되지 않았습니다.");
            return;
        }

        if (!manaController.IsManaFull) return;

        if (manaController.UseMana(skillData.manaCost))
        {
            Debug.Log($"<color=magenta>{monsterData.monsterName} 스킬 발동: {skillData.skillName}</color>");

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
                    // ✅ [수정] 컴파일 오류를 유발하던 아래 코드를 완전히 삭제했습니다.
                    // GameManagers.Instance.RegisterActiveVFX(vfxInstance);
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
        // Render()에서 ChangeDetector가 OnHealthChanged 이벤트를 발생시킴
    }

    public void TakeDamage(float baseDamage, DamageType damageType)
    {
        // 서버에서만 HP 수정 (클라이언트는 Networked 속성 동기화로 반영)
        if (!HasStateAuthorityOrNoNetwork()) return;
        if (monsterData == null) return;
        int finalDamage = DamageCalculator.CalculateDamage(baseDamage, damageType, monsterData.defense, monsterData.magicResistance);
        currentHP -= finalDamage;
        // Render()에서 ChangeDetector가 OnHealthChanged 이벤트를 발생시킴
        if (currentHP <= 0) Die();
    }

    public void ApplyBuff(float healthMultiplier, float speedMultiplier)
    {
        float healthPercentage = currentHP / currentMaxHP;
        currentMaxHP = monsterData.maxHealth * healthMultiplier;
        currentHP = currentMaxHP * healthPercentage;
        currentMoveSpeed = baseMoveSpeed * speedMultiplier;
        OnHealthChanged?.Invoke(currentHP, currentMaxHP);
        Debug.Log($"{gameObject.name}이 강화되었습니다! HP: {currentHP}/{currentMaxHP}");
    }

    private void Die()
    {
        // 이미 파괴 중인 오브젝트면 무시
        if (this == null || gameObject == null) return;
        
        Debug.Log($"{monsterData.monsterName}이(가) 죽었습니다!");
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

    #region 공격 로직 (이하 동일)
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
        attackCoroutine = StartCoroutine(AttackLoop(target));
    }

    private IEnumerator AttackLoop(IEnemy target)
    {
        while (target != null && (target as MonoBehaviour) != null)
        {
            yield return new WaitForSeconds(1f / monsterData.attackSpeed);

            if ((target as MonoBehaviour) == null) break;

            string targetName = (target as MonoBehaviour).name;
            target.TakeDamage(monsterData.attackDamage, monsterData.damageType);
            Debug.Log($"{monsterData.monsterName}이(가) {targetName}을(를) 공격!");
        }

        Debug.Log("공격 대상이 사라졌습니다. 이동을 재개합니다.");
        attackCoroutine = null;

        ScheduleResumeFromBlocker();
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

    #region 이동 및 경로탐색 로직 (이하 동일)

    private void FindNewPathToGoal()
    {
        // 클라이언트에서는 경로탐색 안함
        if (!HasStateAuthorityOrNoNetwork()) return;
        
        // null 체크
        if (pathfinder == null || goalTransform == null)
        {
            Debug.LogWarning($"[Monster] pathfinder 또는 goalTransform이 null입니다.");
            return;
        }
        
        // [3D Migration] FieldManager/AstarGrid 그리드 기준으로 변환
        Vector2Int currentGridPos = pathfinder.WorldToCell(pathfinder.ClampToGrid(transform.position));
        Vector2Int targetGridPos = pathfinder.WorldToCell(pathfinder.ClampToGrid(goalTransform.position));

        if (pathfinder.FindPath(currentGridPos, targetGridPos))
        {
            List<AstarNode> newPath = pathfinder.FinalPath;
            StartFollowingPath(newPath);
        }
        else
        {
             Debug.LogWarning($"{monsterData.monsterName}이(가) 경로를 찾지 못했습니다. 소멸합니다.");
             Destroy(gameObject);
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
        if (monsterData.monsterType == MonsterType.Flying)
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
            OnPathBlocked(null);
        }
    }
    private IEnumerator FlyDirectlyCoroutine()
    {
        // [3D Migration] Move on XZ plane, keep current Y fixed
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
            // [3D Migration] Navigate using XZ; keep current Y
            Vector3 currentTarget = (pathfinder != null)
                ? pathfinder.CellToWorldCenter(new Vector2Int(targetNode.x, targetNode.y), transform.position.y)
                : new Vector3(targetNode.x + 0.5f, transform.position.y, targetNode.y + 0.5f);

            if (targetNode.isWall)
            {
                // [3D Migration] Use 3D physics to locate wall object
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
                else
                {
                    Debug.LogWarning($"경로상에 벽({currentTarget})이 있지만, 실제 벽 오브젝트를 찾을 수 없습니다. 경로를 계속 진행합니다.");
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
                transform.position = nextPos;

                // [Fix] 저지 유닛 능동 탐지 - OnTriggerEnter 누락 방지
                // 이동 거리에 비례하여 탐지 범위를 동적으로 설정 (빠른 몬스터도 감지)
                if (!isBlocked && monsterData.monsterType != MonsterType.Flying)
                {
                    float moveDistance = currentMoveSpeed * dt;
                    float detectionRadius = Mathf.Max(0.6f, moveDistance + 0.3f);
                    float blockDistance = 0.6f; // 실제 저지가 발생하는 최대 거리
                    
                    Collider[] nearbyUnits = Physics.OverlapSphere(nextPos, detectionRadius);
                    foreach (var col in nearbyUnits)
                    {
                        if (col.TryGetComponent<Unit>(out var unit) &&
                            unit.Data.blockCount > 0 &&
                            unit.Data.unitType == UnitType.Melee &&
                            !unit.IsBlockingFull())
                        {
                            // 유닛과의 실제 거리 확인 - 충분히 가까울 때만 저지
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
        if (GameManagers.Instance != null && ownerPlayer != null)
        {
            GameManagers.Instance.OnMonsterReachedGoal(ownerPlayer);
        }
        Destroy(gameObject);
    }
    #endregion

    #region 저지 및 경로 막힘 처리 (이하 동일)
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
            Debug.Log($"{monsterData.monsterName}의 경로가 {obstacle.name}에 의해 막혔습니다. 공격을 시작합니다.");
            StartAttacking(enemyWall);
        }
        else
        {
            Debug.LogWarning($"{monsterData.monsterName}의 경로가 막혔지만, 대상을 공격할 수 없습니다.");
        }
    }

    public void Unblock()
    {
        if (isQuitting || !isBlocked) return;
        
        // 클라이언트에서는 처리하지 않음
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
}
