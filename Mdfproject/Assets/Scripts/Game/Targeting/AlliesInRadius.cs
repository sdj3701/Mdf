// Assets/Scripts/Game/Skills/Targeting/AlliesInRadius.cs (새 파일)
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

[CreateAssetMenu(fileName = "New AlliesInRadius", menuName = "Game/Skills/Targeting/Allies in Radius")]
public class AlliesInRadius : TargetingStrategy
{
    public override List<GameObject> FindTargets(GameObject caster, Vector3 targetPosition, float range)
    {
        var allegiance = caster.GetComponent<Allegiance>();
        if (allegiance == null)
        {
            Debug.LogError($"스킬 시전자 '{caster.name}'에게 Allegiance 컴포넌트가 없습니다!", caster);
            return new List<GameObject>();
        }

        // Allegiance 컴포넌트의 'AllyLayer'를 사용합니다.
        var colliders = Physics2D.OverlapCircleAll(targetPosition, range, allegiance.AllyLayer);
        
        // 자기 자신을 포함한 아군을 반환합니다.
        return colliders.Select(col => col.gameObject).ToList();
    }
}
