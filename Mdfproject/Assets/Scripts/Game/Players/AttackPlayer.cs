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

    [Header("���� ����")]
    public float attackRange = 2.5f;          // ���� ����
    public float attackInterval = 1f;       // ���� ���� (��)
    public int attackDamage = 10;           // ���� ������
    public LayerMask enemyLayerMask = -1;   // �� ���̾� ����ũ

    [Header("�����")]
    public bool showGizmos = true;          // ����� ǥ�� ����
    public Color rangeColor = Color.red;    // ���� ����

    public Monster monster;
    //public PlayerDasta PlayerDasta = new PlayerDasta(100, 100, 1.0f);


    private List<GameObject> enemiesInRange = new List<GameObject>();
    private Coroutine attackCoroutine;

    void Start()
    {
        // ���� �ڷ�ƾ ����
        attackCoroutine = StartCoroutine(AttackLoop());
    }

    void Update()
    {
        // ���� �� �� Ž��
        DetectEnemiesInRange();
    }

    /// <summary>
    /// ���� �� ������ Ž���ϴ� �Լ�
    /// </summary>
    void DetectEnemiesInRange()
    {
        enemiesInRange.Clear();

        // ���� ���� ���� ��� �ݶ��̴� Ž��
        Collider[] colliders = Physics.OverlapSphere(transform.position, attackRange, enemyLayerMask);

        foreach (Collider collider in colliders)
        {
            // �ڱ� �ڽ��� ����
            if (collider.gameObject != gameObject)
            {
                // Enemy �±׳� IEnemy �������̽��� �ִ� ������Ʈ�� �߰�
                if (collider.CompareTag("Enemy") || collider.GetComponent<IEnemy>() != null)
                {
                    enemiesInRange.Add(collider.gameObject);
                    Debug.Log("�߰�");
                }
            }
        }
    }

    /// <summary>
    /// 1�ʸ��� �����ϴ� �ڷ�ƾ
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
    /// ���� �� ��� ���� ����
    /// </summary>
    void AttackEnemiesInRange()
    {
        for (int i = enemiesInRange.Count - 1; i >= 0; i--)
        {
            GameObject enemy = enemiesInRange[i];

            // ���� ������ ��ȿ�ϰ� ���� ���� �ִ��� Ȯ��
            if (enemy != null && Vector3.Distance(transform.position, enemy.transform.position) <= attackRange)
            {
                // ������ ����
                ApplyDamage(enemy);
            }
            else
            {
                // ������ ����ų� �ı��� �� ����
                enemiesInRange.RemoveAt(i);
            }
        }
    }

    /// <summary>
    /// ������ ������ ����
    /// </summary>
    void ApplyDamage(GameObject enemy)
    {
        // EnemyHealth ������Ʈ�� �ִ� ���
        Monster enemyHealth = enemy.GetComponent<Monster>();
        if (enemyHealth != null)
        {
            enemyHealth.TakeDamage(attackDamage);
            Debug.Log($"{enemy.name}���� {attackDamage} �������� �������ϴ�!");
            return;
        }

        // IEnemy �������̽��� �ִ� ���
        IEnemy enemyInterface = enemy.GetComponent<IEnemy>();
        if (enemyInterface != null)
        {
            enemyInterface.TakeDamage(attackDamage);
            Debug.Log($"{enemy.name}���� {attackDamage} �������� �������ϴ�!");
            return;
        }

        // HP�� �ִ� �Ϲ����� ��� (����)
        var enemyScript = enemy.GetComponent<Monster>();
        if (enemyScript != null)
        {
            enemyScript.md.Hp -= attackDamage;
            Debug.Log($"{enemy.name}���� {attackDamage} �������� �������ϴ�!");
        }
    }

    /// <summary>
    /// ����� �׷��� ���� ���� ǥ��
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
        // �ڷ�ƾ ����
        if (attackCoroutine != null)
        {
            StopCoroutine(attackCoroutine);
        }
    }

}
