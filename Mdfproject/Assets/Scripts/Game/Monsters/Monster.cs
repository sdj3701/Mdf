// Assets/Scripts/Game/Monsters/Monster.cs
using UnityEngine;
using System.Collections;
using System.Collections.Generic;

[RequireComponent(typeof(ManaController))]
public class Monster : MonoBehaviour, IEnemy
{
    [Header("참조 데이터")]
    public MonsterData monsterData;

    [Header("현재 상태")]
    public int currentHP;

    // --- 시스템 컴포넌트 ---
    private ManaController manaController;
    private ISkill skillInstance;
    
    // --- 내부 시스템 변수 ---
    private Transform goalTransform;
    private PlayerManager ownerPlayer;
    private AstarGrid pathfinder;
    private bool isBlocked = false;
    private Unit blockingUnit;
    private Coroutine movementCoroutine;
    private Coroutine attackCoroutine; // 공격 전용 코루틴
    private static bool isQuitting = false;
    private bool isMoving = false;

    void OnApplicationQuit() { isQuitting = true; }

    public void Initialize(PlayerManager owner, Transform goal, MonsterData data)
    {
        this.ownerPlayer = owner;
        this.goalTransform = goal;
        this.monsterData = data;
        this.name = monsterData.monsterName;
        
        currentHP = monsterData.maxHealth;
        pathfinder = FindObjectOfType<AstarGrid>();
        
        manaController = GetComponent<ManaController>();
        manaController.Initialize(monsterData.maxMana);

        if (monsterData.skillData != null && monsterData.skillData.skillLogicPrefab != null)
        {
            GameObject skillObject = Instantiate(monsterData.skillData.skillLogicPrefab, transform);
            skillInstance = skillObject.GetComponent<ISkill>();
            manaController.OnManaFull += ActivateSkill;
        }
    }

    void Update()
    {
        if (skillInstance != null)
        {
            manaController.GainManaOverTime(10f);
        }
    }

    private void ActivateSkill()
    {
        if (skillInstance == null || !manaController.IsManaFull) return;
        if (manaController.UseMana(monsterData.skillData.manaCost))
        {
            skillInstance.Activate(this.gameObject);
        }
    }
    
    public void TakeDamage(float baseDamage, DamageType damageType)
    {
        if (monsterData == null) return;
        int finalDamage = DamageCalculator.CalculateDamage(baseDamage, damageType, monsterData.defense, monsterData.magicResistance);
        currentHP -= finalDamage;
        if (currentHP <= 0) Die();
    }

    public void ApplyBuff(float healthMultiplier, float speedMultiplier)
    {
        int newMaxHP = (int)(monsterData.maxHealth * healthMultiplier);
        currentHP = (int)((float)currentHP / monsterData.maxHealth * newMaxHP);
        // TODO: 이동 속도 버프 적용
        Debug.Log($"{gameObject.name}이 강화되었습니다! HP: {currentHP}/{newMaxHP}");
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

    #region 공격 로직
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
            target.TakeDamage(monsterData.attackDamage, monsterData.damageType);
            Debug.Log($"{monsterData.monsterName}이(가) {(target as MonoBehaviour).name}을(를) 공격!");
        }
        
        Debug.Log("공격 대상이 사라졌습니다. 이동을 재개합니다.");
        attackCoroutine = null;
        Unblock();
    }
    #endregion

    #region 이동 및 경로탐색 로직
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
            OnPathBlocked(null); // 경로가 없으면 OnPathBlocked 호출
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
            Vector2 currentTarget = new Vector2(path[currentPathIndex].x + 0.5f, path[currentPathIndex].y + 0.5f);
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
        // 이 부분은 새로운 AstarGrid 로직으로 인해 호출될 가능성이 낮아졌습니다.
        // A*가 벽을 부수는 경로를 찾아주기 때문입니다.
        // 하지만 만약을 위해 남겨둡니다.
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

        Vector2Int currentGridPos = new Vector2Int(Mathf.FloorToInt(transform.position.x), Mathf.FloorToInt(transform.position.y));
        Vector2Int targetGridPos = new Vector2Int(Mathf.FloorToInt(goalTransform.position.x), Mathf.FloorToInt(goalTransform.position.y));
        
        // ✅ [수정] AstarGrid.FindPath 호출 방식 변경
        if (pathfinder.FindPath(currentGridPos, targetGridPos))
        {
            List<AstarNode> newPath = pathfinder.FinalPath; // 성공 시 경로를 가져옴
            StartFollowingPath(newPath);
        }
        else
        {
             Debug.LogWarning($"{monsterData.monsterName}이(가) Unblock 후 경로를 찾지 못했습니다.");
             // 경로를 못찾으면 파괴
             Destroy(gameObject);
        }
    }
    #endregion
}