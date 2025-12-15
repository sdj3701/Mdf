using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/*public struct PlayerDasta
{
    public int Hp;
    public int Damage;
    public float AttackTime;

    public PlayerDasta(int hp, int damage, float attackTime)
    {
        Hp = hp;
        Damage = damage;
        AttackTime = attackTime;
    }

};*/

public class AttackPlayer : MonoBehaviour
{

    [Header("공격 설정")]
    public float attackRange = 2.5f;          // 공격 범위
    public float attackInterval = 1f;       // 공격 간격 (초)
    public int attackDamage = 10;           // 공격 데미지
    public LayerMask enemyLayerMask = -1;   // 적 레이어 마스크

    [Header("디버그")]
    public bool showGizmos = true;          // 기즈모 표시 여부
    public Color rangeColor = Color.red;    // 범위 색상

    public Monster monster;
    //public PlayerDasta PlayerDasta = new PlayerDasta(100, 100, 1.0f);


    private List<GameObject> enemiesInRange = new List<GameObject>();
    private Coroutine attackCoroutine;

    void Start()
    {
        // 공격 코루틴 시작
        attackCoroutine = StartCoroutine(AttackLoop());
    }

    void Update()
    {
        // 범위 내 적 탐지
        DetectEnemiesInRange();
    }

    /// <summary>
    /// 범위 내 적들을 탐지하는 함수
    /// </summary>
    void DetectEnemiesInRange()
    {
        enemiesInRange.Clear();

        // 구형 범위 내의 모든 콜라이더 탐지
        Collider[] colliders = Physics.OverlapSphere(transform.position, attackRange, enemyLayerMask);

        foreach (Collider collider in colliders)
        {
            // 자기 자신은 제외
            if (collider.gameObject != gameObject)
            {
                // Enemy 태그나 IEnemy 인터페이스가 있는 오브젝트만 추가
                if (collider.CompareTag("Enemy") || collider.GetComponent<IEnemy>() != null)
                {
                    enemiesInRange.Add(collider.gameObject);
                    Debug.Log("추가");
                }
            }
        }
    }

    /// <summary>
    /// 1초마다 공격하는 코루틴
    /// </summary>
    IEnumerator AttackLoop()
    {
        while (true)
        {
            yield return new WaitForSeconds(attackInterval);

            if (enemiesInRange.Count > 0)
            {
                AttackEnemiesInRange();
            }
        }
    }

    /// <summary>
    /// 범위 내 모든 적을 공격
    /// </summary>
    void AttackEnemiesInRange()
    {
        for (int i = enemiesInRange.Count - 1; i >= 0; i--)
        {
            GameObject enemy = enemiesInRange[i];

            // 적이 여전히 유효하고 범위 내에 있는지 확인
            if (enemy != null && Vector3.Distance(transform.position, enemy.transform.position) <= attackRange)
            {
                // 데미지 적용
                ApplyDamage(enemy);
            }
            else
            {
                // 범위를 벗어났거나 파괴된 적 제거
                enemiesInRange.RemoveAt(i);
            }
        }
    }

    /// <summary>
    /// 적에게 데미지 적용
    /// </summary>
    void ApplyDamage(GameObject enemy)
    {
        // EnemyHealth 컴포넌트가 있는 경우
        Monster enemyHealth = enemy.GetComponent<Monster>();
        if (enemyHealth != null)
        {
            enemyHealth.TakeDamage(attackDamage);
            Debug.Log($"{enemy.name}에게 {attackDamage} 데미지를 입혔습니다!");
            return;
        }

        // IEnemy 인터페이스가 있는 경우
        IEnemy enemyInterface = enemy.GetComponent<IEnemy>();
        if (enemyInterface != null)
        {
            enemyInterface.TakeDamage(attackDamage);
            Debug.Log($"{enemy.name}에게 {attackDamage} 데미지를 입혔습니다!");
            return;
        }

        // HP가 있는 일반적인 경우 (예시)
        var enemyScript = enemy.GetComponent<Monster>();
        if (enemyScript != null)
        {
            enemyScript.md.Hp -= attackDamage;
            Debug.Log($"{enemy.name}에게 {attackDamage} 데미지를 입혔습니다!");
        }
    }

    /// <summary>
    /// 기즈모를 그려서 공격 범위 표시
    /// </summary>
    void OnDrawGizmos()
    {
        if (showGizmos)
        {
            Gizmos.color = rangeColor;
            Gizmos.DrawWireSphere(transform.position, attackRange);
        }
    }

    void OnDestroy()
    {
        // 코루틴 정리
        if (attackCoroutine != null)
        {
            StopCoroutine(attackCoroutine);
        }
    }

}
