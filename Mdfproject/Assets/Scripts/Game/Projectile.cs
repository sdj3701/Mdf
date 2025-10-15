// Assets/Scripts/Game/Units/Projectile.cs (새 파일)
using UnityEngine;

public class Projectile : MonoBehaviour
{
    // --- 투사체가 받아야 할 정보 ---
    private Transform target;
    private Vector3 lastKnownTargetPosition;
    private float damage;
    private DamageType damageType;

    [Header("투사체 설정")]
    [SerializeField] private float speed = 20f; // 투사체의 비행 속도

    /// <summary>
    /// 발사한 유닛이 이 메서드를 호출하여 투사체에게 임무를 부여합니다.
    /// </summary>
    public void Initialize(Transform target, float damage, DamageType damageType)
    {
        this.target = target;
        this.damage = damage;
        this.damageType = damageType;
        if (target != null)
        {
            lastKnownTargetPosition = target.position;
        }
        else
        {
            lastKnownTargetPosition = transform.position;
        }
    }

    void Update()
    {
        Vector3 currentTargetPosition;
        if (target != null)
        {
            currentTargetPosition = target.position;
            lastKnownTargetPosition = currentTargetPosition;
        }
        else
        {
            currentTargetPosition = lastKnownTargetPosition;
        }

        Vector3 toTarget = currentTargetPosition - transform.position;
        float distanceThisFrame = speed * Time.deltaTime;

        if (toTarget.sqrMagnitude <= distanceThisFrame * distanceThisFrame)
        {
            transform.position = currentTargetPosition;

            if (target != null)
            {
                IEnemy enemy = target.GetComponent<IEnemy>();
                if (enemy != null)
                {
                    enemy.TakeDamage(damage, damageType);
                }
            }

            Destroy(gameObject);
            return;
        }

        transform.position += toTarget.normalized * distanceThisFrame;
    }
}