// Assets/Scripts/Game/Units/Unit.cs
using UnityEngine;
using System.Collections;
using System.Linq;

public class Unit : MonoBehaviour
{
    [Header("참조 데이터")]
    public UnitData unitData; // 유닛의 모든 원본 데이터를 담고 있는 ScriptableObject

    [Header("현재 상태 (인게임 변수)")]
    public int starLevel = 1;
    private float currentHP;
    private float currentAttackDamage;
    private float currentAttackSpeed;
    private float currentAttackRange;
    private int currentBlockCount; // 현재 저지하고 있는 적의 수
    
    [Header("공격 대상 설정")]
    public LayerMask enemyLayerMask;
    private Transform targetEnemy;
    private Coroutine attackCoroutine;

    void Start()
    {
        InitializeStats();
        // 공격 로직은 게임 상태(e.g., 전투 페이즈)에 따라 활성화/비활성화 되어야 함
        // 여기서는 예시로 바로 시작하지만, 실제로는 GameManager가 제어해야 함
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

        // 성급에 따른 스탯 강화 (예: 2배씩)
        float starMultiplier = Mathf.Pow(2, starLevel - 1);

        currentHP = unitData.baseHealth * starMultiplier;
        currentAttackDamage = unitData.baseAttackDamage * starMultiplier;
        currentAttackSpeed = unitData.attackSpeed;
        currentAttackRange = unitData.attackRange;
        // 저지 가능 수는 UnitData에서 가져옴 (업그레이드 시 변경될 수 있음)
        // currentBlockCount = unitData.blockCount; 

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

    #region 공격 로직 (AttackPlayer 기능 통합)

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
                // 공격 로직 실행
                Attack();
            }

            // 공격 속도에 맞춰 대기
            yield return new WaitForSeconds(1f / currentAttackSpeed);
        }
    }

    private void FindNearestEnemy()
    {
        // AttackPlayer의 OverlapSphere 방식을 사용하여 범위 내 모든 적을 감지
        Collider[] enemiesInRange = Physics.OverlapSphere(transform.position, currentAttackRange, enemyLayerMask);
        
        float closestDistanceSqr = float.MaxValue;
        Transform nearestEnemy = null;

        foreach (var enemyCollider in enemiesInRange)
        {
            // 기획 추가: 유닛 타입(지상/원거리)에 따라 타겟팅할 수 있는 몬스터(지상/공중)를 필터링하는 로직
            // Monster monster = enemyCollider.GetComponent<Monster>();
            // if (unitData.unitType == UnitType.Melee && monster.isFlying) continue; // 근접 유닛은 공중 공격 불가

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

        // 대상이 여전히 사거리 내에 있는지 한 번 더 확인
        if (Vector3.Distance(transform.position, targetEnemy.position) > currentAttackRange)
        {
            targetEnemy = null;
            return;
        }

        Monster monster = targetEnemy.GetComponent<Monster>();
        if (monster != null)
        {
            monster.TakeDamage((int)currentAttackDamage);
            
            // 기획 추가: 원거리 유닛일 경우 발사체 생성, 근접 유닛일 경우 공격 이펙트 표시
            // if(unitData.unitType == UnitType.Ranged) { /* 발사체 로직 */ }
            
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
        // TODO: 유닛 사망 처리 (풀링 시스템에 반환, 필드에서 제거 등)
        Debug.Log($"{unitData.unitName} 사망.");
        // FieldManager에 사망 사실을 알려서 placedUnits 딕셔너리에서 제거하도록 해야 함
        // FindObjectOfType<FieldManager>().UnitDied(this); 
        Destroy(gameObject);
    }

    #endregion

    void OnDrawGizmosSelected()
    {
        // 공격 범위를 시각적으로 표시하여 디버깅에 용이하게 함
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, currentAttackRange);
    }
    
    private void OnDestroy()
    {
        // 오브젝트 파괴 시 코루틴이 남아있지 않도록 확실히 정리
        StopAttackLoop();
    }
}