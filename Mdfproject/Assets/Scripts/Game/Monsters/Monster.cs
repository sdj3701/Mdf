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
    private static bool isQuitting = false;
    
    // ✅ [수정] 누락되었던 핵심 변수를 다시 추가했습니다.
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
        if (currentHP <= 0)
        {
            Die();
        }
    }

    // ✅ [수정] 누락되었던 메서드를 다시 추가했습니다.
    public void ApplyBuff(float healthMultiplier, float speedMultiplier)
    {
        int newMaxHP = (int)(monsterData.maxHealth * healthMultiplier);
        currentHP = (int)((float)currentHP / monsterData.maxHealth * newMaxHP);
        
        // TODO: 이동 속도 버프 적용을 위해 Monster 클래스에 currentMoveSpeed 변수 추가 필요
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
        if (manaController != null)
        {
            manaController.OnManaFull -= ActivateSkill;
        }
    }

    #region 이동 및 저지 로직 (완전 복구)
    public void StartFollowingPath(List<AstarNode> path)
    {
        if (movementCoroutine != null) StopCoroutine(movementCoroutine);
        
        isMoving = true; // isMoving 상태 활성화
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
            isMoving = false; // 경로 없으면 이동 중지
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
    public bool IsBlocked() { return isBlocked; }
    public void Block(Unit unit)
    {
        if (isBlocked) return;
        isBlocked = true;
        blockingUnit = unit;
        if (movementCoroutine != null) StopCoroutine(movementCoroutine);
        isMoving = false; // isMoving 상태 비활성화
    }
    public void Unblock()
    {
        if (isQuitting || !isBlocked) return;
        isBlocked = false;
        blockingUnit = null;
        Vector2Int currentGridPos = new Vector2Int(Mathf.FloorToInt(transform.position.x), Mathf.FloorToInt(transform.position.y));
        Vector2Int targetGridPos = new Vector2Int(Mathf.FloorToInt(goalTransform.position.x), Mathf.FloorToInt(goalTransform.position.y));
        List<AstarNode> newPath = pathfinder.FindPath(currentGridPos, targetGridPos);
        StartFollowingPath(newPath);
    }
    #endregion
}