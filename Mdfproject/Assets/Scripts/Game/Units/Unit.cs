// Assets/Scripts/Game/Units/Unit.cs
using UnityEngine;
using System.Collections;
using System.Collections.Generic; // List<T>를 사용하기 위해 추가
using System.Linq;

public class Unit : MonoBehaviour
{
    [Header("참조 데이터")]
    [SerializeField] // SerializeField는 유지하여 디버깅 시 인스펙터에서 확인은 가능하게 합니다.
    private UnitData unitData; // 유닛의 모든 원본 데이터를 담고 있는 ScriptableObject

    public UnitData Data => unitData;

    [Header("현재 상태 (인게임 변수)")]
    public int starLevel = 1;
    private float currentHP;
    private float currentAttackDamage;
    private float currentAttackSpeed;
    private float currentAttackRange;

    // 저지하고 있는 몬스터를 추적하기 위한 리스트
    private List<Monster> blockedMonsters = new List<Monster>();

    [Header("공격 대상 설정")]
    public LayerMask enemyLayerMask;
    private Transform targetEnemy;
    private Coroutine attackCoroutine;

    /// <summary>
    /// 외부에서 UnitData를 주입받아 모든 초기화를 수행하는 public 메서드.
    /// 이 함수는 유닛이 생성된 직후 FieldManager에 의해 단 한번 호출됩니다.
    /// </summary>
    public void Initialize(UnitData data)
    {
        this.unitData = data;
        InitializeStats();
        StartAttackLoop();
    }

    /// <summary>
    /// UnitData와 성급에 따라 스탯을 초기화합니다.
    /// </summary>
    public void InitializeStats()
    {
        if (unitData == null)
        {
            Debug.LogError($"{gameObject.name}에 UnitData가 할당되지 않았습니다!");
            return;
        }

        float starMultiplier = Mathf.Pow(2, starLevel - 1);

        currentHP = unitData.baseHealth * starMultiplier;
        currentAttackDamage = unitData.baseAttackDamage * starMultiplier;
        currentAttackSpeed = unitData.attackSpeed;
        currentAttackRange = unitData.attackRange;

        Debug.Log($"{unitData.unitName} ({starLevel}성) 스탯 초기화 완료. HP: {currentHP}, DMG: {currentAttackDamage}");
    }

    /// <summary>
    /// 유닛을 한 단계 업그레이드합니다.
    /// </summary>
    public void Upgrade()
    {
        if (starLevel < 3)
        {
            starLevel++;
            InitializeStats();
            Debug.Log($"{unitData.unitName}이(가) {starLevel}성으로 업그레이드되었습니다!");
        }
    }

    #region 공격 로직

    public void StartAttackLoop()
    {
        if (attackCoroutine == null)
        {
            attackCoroutine = StartCoroutine(AttackLoop());
        }
    }

    public void StopAttackLoop()
    {
        if (attackCoroutine != null)
        {
            StopCoroutine(attackCoroutine);
            attackCoroutine = null;
        }
    }

    private IEnumerator AttackLoop()
    {
        while (true)
        {
            FindNearestEnemy();

            if (targetEnemy != null)
            {
                Attack();
            }

            yield return new WaitForSeconds(1f / currentAttackSpeed);
        }
    }

    private void FindNearestEnemy()
    {
        Collider2D[] enemiesInRange = Physics2D.OverlapCircleAll(transform.position, currentAttackRange, enemyLayerMask);
        
        float closestDistanceSqr = float.MaxValue;
        Transform nearestEnemy = null;

        foreach (var enemyCollider in enemiesInRange)
        {
            Monster monster = enemyCollider.GetComponent<Monster>();

            if (monster == null) continue;

            if (unitData.unitType == UnitType.Melee && monster.monsterType == MonsterType.Flying)
            {
                continue;
            }

            float distanceSqr = (transform.position - enemyCollider.transform.position).sqrMagnitude;
            if (distanceSqr < closestDistanceSqr)
            {
                closestDistanceSqr = distanceSqr;
                nearestEnemy = enemyCollider.transform;
            }
        }
        targetEnemy = nearestEnemy;
    }

    private void Attack()
    {
        if (targetEnemy == null) return;

        if (Vector2.Distance(transform.position, targetEnemy.position) > currentAttackRange)
        {
            targetEnemy = null;
            return;
        }

        Monster monster = targetEnemy.GetComponent<Monster>();
        if (monster != null)
        {
            monster.TakeDamage((int)currentAttackDamage);
            Debug.Log($"{unitData.unitName}이(가) {monster.name}을(를) 공격! (데미지: {currentAttackDamage})");
        }
    }

    #endregion

    #region 체력 및 생존 관련

    public void TakeDamage(int damage)
    {
        currentHP -= damage;
        if (currentHP <= 0)
        {
            Die();
        }
    }

    private void Die()
    {
        Debug.Log($"{unitData.unitName} 사망.");
        Destroy(gameObject);
    }

    #endregion

    #region 저지 시스템

    /// <summary>
    /// 몬스터가 유닛의 저지 범위(Trigger)에 들어왔을 때 호출됩니다.
    /// </summary>
    private void OnTriggerEnter2D(Collider2D other)
    {
        Monster monster = other.GetComponent<Monster>();

        // 몬스터가 아니거나, 이미 내가 저지하고 있는 몬스터라면 즉시 반환
        if (monster == null || blockedMonsters.Contains(monster))
        {
            return;
        }

        if (Data.blockCount <= 0) return;
        if (blockedMonsters.Count >= Data.blockCount) return;

        if (monster.monsterType == MonsterType.Flying || monster.IsBlocked())
        {
            return;
        }

        blockedMonsters.Add(monster);
        monster.Block(this);
    }

    /// <summary>
    /// 저지하던 몬스터가 죽었을 때, 해당 몬스터가 이 함수를 호출하여 저지 목록에서 제외시킵니다.
    /// </summary>
    public void ReleaseBlockedMonster(Monster monster)
    {
        if (blockedMonsters.Contains(monster))
        {
            blockedMonsters.Remove(monster);
        }
    }

    #endregion

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, currentAttackRange);
    }
    
    private void OnDestroy()
    {
        StopAttackLoop();
        
        // 유닛이 파괴될 때, 막고 있던 모든 몬스터를 풀어줍니다.
        foreach (var monster in blockedMonsters)
        {
            if (monster != null)
            {
                monster.Unblock();
            }
        }
        blockedMonsters.Clear();
    }
}