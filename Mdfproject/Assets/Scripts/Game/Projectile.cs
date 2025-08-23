// Assets/Scripts/Game/Units/Projectile.cs (새 파일)
using UnityEngine;

public class Projectile : MonoBehaviour
{
    // --- 투사체가 받아야 할 정보 ---
    private Transform target;
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
    }

    void Update()
    {
        // 1. 목표가 사라졌는지 확인합니다. (몬스터가 이미 죽은 경우 등)
        if (target == null)
        {
            Destroy(gameObject); // 목표가 없으면 스스로를 파괴
            return;
        }

        // 2. 목표를 향해 이동합니다.
        Vector3 direction = (target.position - transform.position).normalized;
        transform.position += direction * speed * Time.deltaTime;

        // 3. 목표와의 거리를 확인합니다.
        float distanceToTarget = Vector3.Distance(transform.position, target.position);

        // 4. 목표에 충분히 가까워졌다면 데미지를 주고 소멸합니다.
        if (distanceToTarget < 0.5f)
        {
            // 목표 오브젝트에서 IEnemy 인터페이스를 찾아 TakeDamage를 호출합니다.
            IEnemy enemy = target.GetComponent<IEnemy>();
            if (enemy != null)
            {
                enemy.TakeDamage(damage, damageType);
            }
            
            // TODO: 여기에 폭발 이펙트나 사운드를 재생하는 코드를 추가할 수 있습니다.
            
            Destroy(gameObject); // 임무 완수 후 스스로를 파괴
        }
    }
}