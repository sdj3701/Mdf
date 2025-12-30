// Assets/Scripts/Game/Projectile.cs
using UnityEngine;

public class Projectile : MonoBehaviour
{
    private Transform target;
    private Vector3 lastKnownTargetPosition;
    private float damage;
    private DamageType damageType;
    private float _splashRadius;

    [Header("Projectile Settings")]
    [SerializeField] private float speed = 20f;
    [SerializeField] private LayerMask enemyLayerMask;

    private bool _visualOnly;

    public float Speed => speed;

    /// <summary>
    /// 투사체를 초기화합니다.
    /// </summary>
    /// <param name="target">타겟 Transform</param>
    /// <param name="damage">데미지</param>
    /// <param name="damageType">데미지 타입</param>
    /// <param name="splashRadius">스플래시 반경 (0이면 단일 대상)</param>
    public void Initialize(Transform target, float damage, DamageType damageType, float splashRadius = 0f)
    {
        this.target = target;
        this.damage = damage;
        this.damageType = damageType;
        this._splashRadius = splashRadius;
        if (target != null)
        {
            lastKnownTargetPosition = target.position;
        }
        else
        {
            lastKnownTargetPosition = transform.position;
        }
    }

    /// <summary>
    /// 적 레이어 마스크를 설정합니다 (스플래시 공격에 필요).
    /// </summary>
    public void SetEnemyLayerMask(LayerMask mask)
    {
        enemyLayerMask = mask;
    }

    // Visual-only projectiles are moved by ProjectileVfxManager.
    public void SetVisualOnly(bool visualOnly)
    {
        _visualOnly = visualOnly;
        if (_visualOnly)
        {
            target = null;
        }
    }

    private void Update()
    {
        if (_visualOnly)
        {
            return;
        }

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

        if (toTarget.sqrMagnitude > 1e-6f)
        {
            transform.rotation = Quaternion.LookRotation(toTarget.normalized, Vector3.up);
        }

        if (toTarget.sqrMagnitude <= distanceThisFrame * distanceThisFrame)
        {
            transform.position = currentTargetPosition;
            ApplyDamageOnImpact(currentTargetPosition);
            Destroy(gameObject);
            return;
        }

        transform.position += toTarget.normalized * distanceThisFrame;
    }

    /// <summary>
    /// 투사체가 도착했을 때 데미지를 적용합니다.
    /// 스플래시 범위가 설정되어 있으면 범위 내 모든 적에게 데미지, 아니면 단일 타겟에게만 데미지.
    /// </summary>
    private void ApplyDamageOnImpact(Vector3 impactPosition)
    {
        if (_splashRadius > 0f)
        {
            // 스플래시 공격: 폭발 지점 주변 범위 내 모든 적에게 데미지
            Collider[] enemiesInRange = Physics.OverlapSphere(impactPosition, _splashRadius, enemyLayerMask);
            foreach (var enemyCollider in enemiesInRange)
            {
                if (enemyCollider.TryGetComponent<IEnemy>(out var enemy))
                {
                    enemy.TakeDamage(damage, damageType);
                }
            }
        }
        else
        {
            // 단일 대상 공격: 기존 타겟에게만 데미지
            if (target != null)
            {
                IEnemy enemy = target.GetComponent<IEnemy>();
                if (enemy != null)
                {
                    enemy.TakeDamage(damage, damageType);
                }
            }
        }
    }
}
