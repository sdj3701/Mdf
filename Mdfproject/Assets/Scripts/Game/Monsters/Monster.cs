// Assets/Scripts/Game/Monsters/Monster.cs
using UnityEngine;
using System.Collections;
using System.Collections.Generic;

[RequireComponent(typeof(ManaController))]
public class Monster : MonoBehaviour, IEnemy
{
    [Header("참조 데이터")]
    public MonsterData monsterData;

    [Tooltip("공격하거나 파괴할 수 있는 벽의 레이어를 설정해야 합니다.")]
    public LayerMask wallLayerMask;

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

    public void Initialize(PlayerManager owner, Transform goal, MonsterData data, AstarGrid pathfinder)
    {
        this.ownerPlayer = owner;
        this.goalTransform = goal;
        this.monsterData = data;
        this.pathfinder = pathfinder; // [수정] 외부에서 올바른 AstarGrid를 주입받습니다.
        this.name = monsterData.monsterName;

        // AstarGrid와 동일한 레이어 마스크를 사용하도록 보장하여 탐지 불일치 문제를 해결합니다.
        this.wallLayerMask = pathfinder.wallLayers;
        
        currentHP = monsterData.maxHealth;
        
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

            // 코루틴이 재개된 후 목표가 여전히 유효한지 확인합니다. (다른 몬스터에 의해 파괴되었을 수 있음)
            if ((target as MonoBehaviour) == null) break;

            // [수정] TakeDamage 호출 후 대상이 파괴될 수 있으므로, 예외 발생을 막기 위해 이름을 미리 저장합니다.
            string targetName = (target as MonoBehaviour).name;
            target.TakeDamage(monsterData.attackDamage, monsterData.damageType);
            Debug.Log($"{monsterData.monsterName}이(가) {targetName}을(를) 공격!");
        }
        
        Debug.Log("공격 대상이 사라졌습니다. 이동을 재개합니다.");
        attackCoroutine = null;
        
        // [수정] Unblock() 대신, 경로를 다시 찾는 로직을 직접 호출하여 멈춤 현상을 해결합니다.
        FindNewPathToGoal();
    }
    #endregion

    #region 이동 및 경로탐색 로직

    /// <summary>
    /// [새로 추가된 메서드] 현재 위치에서 목표 지점까지의 새로운 경로를 탐색하고 이동을 시작합니다.
    /// </summary>
    private void FindNewPathToGoal()
    {
        // [수정] 좌표 계산 시 FloorToInt 대신 RoundToInt를 사용하여 정확도를 높입니다.
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
            AstarNode targetNode = path[currentPathIndex];
            Vector2 currentTarget = new Vector2(targetNode.x + 0.5f, targetNode.y + 0.5f);

            // [수정된 핵심 로직] 다음 목표가 벽인지 확인합니다.
            if (targetNode.isWall)
            {
                // 해당 위치의 벽 오브젝트를 찾습니다.
                // ✅ [수정] AstarGrid의 탐지 반경(0.4f)과 일치시켜 탐지 오류를 해결합니다.
                // ✅ [수정] OverlapCircleAll을 사용하여 여러 콜라이더가 있을 경우에도 DestructibleWall을 확실히 찾도록 수정합니다.
                Collider2D[] wallColliders = Physics2D.OverlapCircleAll(currentTarget, 0.4f, wallLayerMask);
                DestructibleWall wall = null;
                foreach (var col in wallColliders)
                {
                    if (col.TryGetComponent(out wall)) break;
                }

                if (wall != null)
                {
                    // 벽을 찾았다면, 이동을 멈추고 공격을 시작합니다.
                    StartAttacking(wall);
                    yield break; // 이동 코루틴을 완전히 종료합니다.
                }
                else
                {
                    // 게임 시작 시 모든 벽 타일에 DestructibleWall 오브젝트가 생성되므로, 이 경고는 발생하면 안 됩니다.
                    // 만약 이 메시지가 보인다면, A* 경로와 실제 월드의 벽 상태가 일치하지 않는 것입니다.
                    Debug.LogWarning($"경로상에 벽({currentTarget})이 있지만, 실제 벽 오브젝트를 찾을 수 없습니다. 경로를 계속 진행합니다.");
                }
            }
            
            // 기존 이동 로직
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

        // [수정] 경로 탐색 로직을 새 메서드로 분리하여 호출합니다.
        FindNewPathToGoal();
    }
    #endregion
}
