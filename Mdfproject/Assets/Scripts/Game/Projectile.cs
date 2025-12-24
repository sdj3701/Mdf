// Assets/Scripts/Game/Projectile.cs
using UnityEngine;

public class Projectile : MonoBehaviour
{
    private Transform target;
    private Vector3 lastKnownTargetPosition;
    private float damage;
    private DamageType damageType;

    [Header("Projectile Settings")]
    [SerializeField] private float speed = 20f;

    private bool _visualOnly;

    public float Speed => speed;

    // Called by the firing unit to initialize gameplay projectile data.
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
