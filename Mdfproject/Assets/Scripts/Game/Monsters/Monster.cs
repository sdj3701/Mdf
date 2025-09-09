// Assets/Scripts/Game/Monsters/Monster.cs
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

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
        this.name = monsterData.monsterName;
        this.wallLayerMask = pathfinder.wallLayers;
        
        currentMaxHP = monsterData.maxHealth;
        currentHP = currentMaxHP;
        OnHealthChanged?.Invoke(currentHP, currentMaxHP);
        
        manaController = GetComponent<ManaController>();
        
        int maxMana = 0;
        if (monsterData.skillData != null)
        {
            maxMana = monsterData.skillData.manaCost;
            manaController.OnManaFull += ActivateSkill;
        }
        manaController.Initialize(maxMana);
    }

    void Update()
    {
        if (monsterData != null && monsterData.skillData != null)
        {
            manaController.GainManaOverTime(10f);
        }
    }

    private void ActivateSkill()
    {
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
                    effect.ApplyEffect(null, this.gameObject, targets);
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
        if (currentHP <= 0 || amount <= 0) return;
        currentHP = Mathf.Min(currentHP + amount, currentMaxHP);
        OnHealthChanged?.Invoke(currentHP, currentMaxHP);
    }

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

    #region 공격 로직 (이하 동일)
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

    #region 이동 및 경로탐색 로직 (이하 동일)

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
