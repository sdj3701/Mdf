// Assets/Scripts/Game/Skills/Targeting/EnemiesInRadius.cs (수정)
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

[CreateAssetMenu(fileName = "New EnemiesInRadius", menuName = "Game/Skills/Targeting/Enemies in Radius")]
public class EnemiesInRadius : TargetingStrategy
{
    // [제거됨] public LayerMask enemyLayer; <- 이 변수는 더 이상 필요 없습니다.

    public override List<GameObject> FindTargets(GameObject caster, Vector3 targetPosition, float range)
    {
        // 1. 시전자로부터 Allegiance 컴포넌트를 가져옵니다.
        var allegiance = caster.GetComponent<Allegiance>();
        if (allegiance == null)
        {
            Debug.LogError($"스킬 시전자 '{caster.name}'에게 Allegiance 컴포넌트가 없습니다!", caster);
            return new List<GameObject>(); // 빈 리스트 반환
        }

        // 2. Allegiance 컴포넌트에 정의된 'EnemyLayer'를 사용하여 주변의 적들을 찾습니다. (3D)
        var colliders = Physics.OverlapSphere(targetPosition, range, allegiance.EnemyLayer);

        return colliders.Select(col => col.gameObject).ToList();
    }
}
