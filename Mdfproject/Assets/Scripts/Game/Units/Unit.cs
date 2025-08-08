// Assets/Scripts/Game/Units/Unit.cs
using UnityEngine;
using System.Collections;
using System.Linq;

public class Unit : MonoBehaviour
{
    [Header("참조 데이터")]
    public UnitData unitData;

    [Header("현재 상태")]
    public int starLevel = 1;
    private float currentHP;
    private float currentAttackDamage;
    private float currentAttackSpeed;
    private float currentAttackRange;

    [Header("공격 대상 설정")]
    public LayerMask enemyLayerMask;
    private Transform targetEnemy;
    private Coroutine attackCoroutine;

    void Start()
    {
        InitializeStats();
        attackCoroutine = StartCoroutine(AttackLoop());
    }

    /// <summary>
    /// UnitData와 성급에 따라 스탯을 초기화합니다.
    /// </summary>
    public void InitializeStats()
    {
        // 성급에 따라 스탯 강화 (예: 2배씩)
        float starMultiplier = Mathf.Pow(2, starLevel - 1);

        currentHP = unitData.baseHealth * starMultiplier;
        currentAttackDamage = unitData.baseAttackDamage * starMultiplier;
        currentAttackSpeed = unitData.attackSpeed; // 공속은 다른 방식으로 적용 가능
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
            // TODO: 외형 변경 등 시각적 효과 추가
            Debug.Log($"{unitData.unitName}이(가) {starLevel}성으로 업그레이드되었습니다!");
        }
    }

    private IEnumerator AttackLoop()
    {
        while (true)
        {
            FindNearestEnemy();

            if (targetEnemy != null)
            {
                // 공격 로직 실행
                Attack();
            }

            // 공격 속도에 맞춰 대기
            yield return new WaitForSeconds(1f / currentAttackSpeed);
        }
    }

    private void FindNearestEnemy()
    {
        Collider[] enemies = Physics.OverlapSphere(transform.position, currentAttackRange, enemyLayerMask);
        
        float closestDistance = float.MaxValue;
        Transform nearestEnemy = null;

        foreach (var enemyCollider in enemies)
        {
            float distance = Vector3.Distance(transform.position, enemyCollider.transform.position);
            if (distance < closestDistance)
            {
                closestDistance = distance;
                nearestEnemy = enemyCollider.transform;
            }
        }
        targetEnemy = nearestEnemy;
    }

    private void Attack()
    {
        if (targetEnemy == null) return;

        Monster monster = targetEnemy.GetComponent<Monster>();
        if (monster != null)
        {
            monster.TakeDamage((int)currentAttackDamage);
            // TODO: 발사체 생성 또는 공격 이펙트 표시
            Debug.Log($"{unitData.unitName}이(가) {monster.name}을(를) 공격! (데미지: {currentAttackDamage})");
        }
    }

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
        // TODO: 유닛 사망 처리 (풀링 시스템에 반환 등)
        Destroy(gameObject);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, unitData != null ? unitData.attackRange : currentAttackRange);
    }
}