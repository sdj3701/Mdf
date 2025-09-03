// Assets/Scripts/Game/Monsters/Monster.cs
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
// using Fusion; // 네트워크 모드에서 주석 해제

// public class Monster : NetworkBehaviour, IEnemy, IHealth // 네트워크 모드에서 NetworkBehaviour로 변경
public class Monster : MonoBehaviour, IEnemy, IHealth
{
    [Header("참조 데이터")]
    public MonsterData monsterData;

    [Tooltip("공격하거나 파괴할 수 있는 벽의 레이어를 설정해야 합니다.")]
    public LayerMask wallLayerMask;

    [Header("현재 상태")]
    public float currentHP;
    private float currentMaxHP;

    public float CurrentHealth => currentHP;
    public float MaxHealth => currentMaxHP;
    public event System.Action<float, float> OnHealthChanged;

    // --- 시스템 컴포넌트 ---
    private ManaController manaController;
    // [제거됨] private ISkill skillInstance;
    
    // --- 내부 시스템 변수 ---
    private Transform goalTransform;
    private PlayerManager ownerPlayer;
    private AstarGrid pathfinder;
    private bool isBlocked = false;
    private Unit blockingUnit;
    private Coroutine movementCoroutine;
    private Coroutine attackCoroutine;
    private static bool isQuitting = false;
    private bool isMoving = false;

    void OnApplicationQuit() { isQuitting = true; }

    public void Initialize(PlayerManager owner, Transform goal, MonsterData data, AstarGrid pathfinder)
    {
        this.ownerPlayer = owner;
        this.goalTransform = goal;
        this.monsterData = data;
        this.pathfinder = pathfinder;
        this.name = monsterData.monsterName;

        this.wallLayerMask = pathfinder.wallLayers;
        
        currentMaxHP = monsterData.maxHealth;
        currentHP = currentMaxHP;
        OnHealthChanged?.Invoke(currentHP, currentMaxHP);
        
        manaController = GetComponent<ManaController>();
        manaController.Initialize(monsterData.maxMana);

        // [변경됨] 스킬 데이터가 있는 경우에만 마나 이벤트 핸들러를 연결합니다.
        if (monsterData.skillData != null)
        {
            manaController.OnManaFull += ActivateSkill;
        }
    }

    void Update()
    {
        // 몬스터가 스킬을 가지고 있을 때만 마나를 채웁니다.
        if (monsterData != null && monsterData.skillData != null)
        {
            manaController.GainManaOverTime(10f);
        }
    }

    /// <summary>
    /// [핵심 수정] 새로운 스킬 시스템을 사용하여 스킬을 발동합니다.
    /// </summary>
    private void ActivateSkill()
    {
        // [네트워크] 스킬 로직은 서버(StateAuthority)에서만 실행되어야 합니다.
        // if (!Object.HasStateAuthority) return;

        SkillData skillData = monsterData.skillData;

        // 스킬 데이터, 타겟팅 전략, 효과가 모두 설정되어 있는지 확인합니다.
        if (skillData == null || skillData.targetingStrategy == null || skillData.effects.Count == 0)
        {
            Debug.LogError($"{monsterData.monsterName}의 SkillData 또는 그 내용이 올바르게 설정되지 않았습니다.");
            return;
        }
        
        if (!manaController.IsManaFull) return;

        if (manaController.UseMana(skillData.manaCost))
        {
            Debug.Log($"<color=magenta>{monsterData.monsterName} 스킬 발동: {skillData.skillName}</color>");

            // 1. 타겟팅 전략을 사용해 대상들을 찾습니다.
            List<GameObject> targets = skillData.targetingStrategy.FindTargets(this.gameObject, transform.position);

            // 2. SkillData에 등록된 모든 효과를 순차적으로 적용합니다.
            foreach (var effect in skillData.effects)
            {
                if (effect != null)
                {
                    // effect.ApplyEffect(Runner, this.gameObject, targets); // 네트워크 모드
                    effect.ApplyEffect(null, this.gameObject, targets); // 비-네트워크 모드
                }
            }
            
            // 3. (선택) 시각 효과 재생
            if (skillData.vfxPrefab != null)
            {
                // RPC_PlaySkillVFX(skillData.vfxPrefab.name, transform.position); // 네트워크 모드
                Instantiate(skillData.vfxPrefab, transform.position, Quaternion.identity); // 비-네트워크 모드
            }
        }
    }
    
    
    public void Heal(float amount)
    {
        if (currentHP <= 0 || amount <= 0) return; // 이미 죽었으면 회복 불가

        currentHP = Mathf.Min(currentHP + amount, currentMaxHP); // 최대 체력을 넘지 않도록
        OnHealthChanged?.Invoke(currentHP, currentMaxHP);
    }

    /*
    // [네트워크] 스킬 시각 효과(VFX)를 모든 클라이언트에서 재생하기 위한 RPC
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_PlaySkillVFX(string vfxPrefabName, Vector3 position)
    {
        // ... Unit.cs와 동일한 로직 ...
    }
    */

    public void TakeDamage(float baseDamage, DamageType damageType)
    {
        if (monsterData == null) return;
        int finalDamage = DamageCalculator.CalculateDamage(baseDamage, damageType, monsterData.defense, monsterData.magicResistance);
        currentHP -= finalDamage;
        OnHealthChanged?.Invoke(currentHP, currentMaxHP);
        if (currentHP <= 0) Die();
    }

    public void ApplyBuff(float healthMultiplier, float speedMultiplier)
    {
        float healthPercentage = currentHP / currentMaxHP;
        currentMaxHP = monsterData.maxHealth * healthMultiplier;
        currentHP = currentMaxHP * healthPercentage;
        OnHealthChanged?.Invoke(currentHP, currentMaxHP);
        // TODO: 이동 속도 버프 적용
        Debug.Log($"{gameObject.name}이 강화되었습니다! HP: {currentHP}/{currentMaxHP}");
    }

    private void Die()
    {
        Debug.Log($"{monsterData.monsterName}이(가) 죽었습니다!");
        if (isBlocked && blockingUnit != null)
        {
            blockingUnit.ReleaseBlockedMonster(this);
        }
        Destroy(gameObject);
    }
    
    private void OnDestroy()
    {
        if (manaController != null) manaController.OnManaFull -= ActivateSkill;
    }

    #region 공격 로직 (기존과 동일)
    private void StartAttacking(IEnemy target)
    {
        if (target == null) return;
        StopAllCoroutines();
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
        
        FindNewPathToGoal();
    }
    #endregion

    #region 이동 및 경로탐색 로직 (기존과 동일)

    private void FindNewPathToGoal()
    {
        Vector2Int currentGridPos = new Vector2Int(Mathf.RoundToInt(transform.position.x), Mathf.RoundToInt(transform.position.y));
        Vector2Int targetGridPos = new Vector2Int(Mathf.RoundToInt(goalTransform.position.x), Mathf.RoundToInt(goalTransform.position.y));
        
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
        Vector2 targetPosition = goalTransform.position;
        while (Vector2.Distance(transform.position, targetPosition) > 0.1f && isMoving)
        {
            transform.position = Vector2.MoveTowards(transform.position, targetPosition, monsterData.moveSpeed * Time.deltaTime);
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
            Vector2 currentTarget = new Vector2(targetNode.x + 0.5f, targetNode.y + 0.5f);

            if (targetNode.isWall)
            {
                Collider2D[] wallColliders = Physics2D.OverlapCircleAll(currentTarget, 0.4f, wallLayerMask);
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
            
            while (Vector2.Distance(transform.position, currentTarget) > 0.1f && isMoving)
            {
                transform.position = Vector2.MoveTowards(transform.position, currentTarget, monsterData.moveSpeed * Time.deltaTime);
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

    #region 저지 및 경로 막힘 처리 (기존과 동일)
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
        
        isBlocked = false;
        blockingUnit = null;
        
        if (attackCoroutine != null)
        {
            StopCoroutine(attackCoroutine);
            attackCoroutine = null;
        }

        FindNewPathToGoal();
    }
    #endregion
}