// Assets/Scripts/Game/Skills/AreaDamageSkill.cs
using UnityEngine;

public class AreaDamageSkill : MonoBehaviour, ISkill
{
    [Header("스킬 효과 설정")]
    [SerializeField] private float skillDamage = 50f;
    [SerializeField] private float skillRadius = 3f;
    [SerializeField] private DamageType damageType = DamageType.Magic;
    
    // 이제 쿨타임은 ManaController가 관리하므로 제거합니다.

    public void Activate(GameObject user)
    {
        Debug.Log($"{user.name}가 광역 스킬 발동!");

        // 주변의 모든 유닛 찾기 (몬스터가 사용 시) 또는 몬스터 찾기 (유닛이 사용 시)
        // LayerMask를 통해 대상을 유연하게 설정할 수 있습니다.
        Collider2D[] hits = Physics2D.OverlapCircleAll(user.transform.position, skillRadius, LayerMask.GetMask("Unit", "Monster"));

        foreach (var hit in hits)
        {
            // 자기 자신은 공격하지 않도록 체크
            if (hit.gameObject == user) continue;

            if (hit.TryGetComponent<IEnemy>(out var enemy))
            {
                enemy.TakeDamage(skillDamage, damageType);
            }
        }
    }
}