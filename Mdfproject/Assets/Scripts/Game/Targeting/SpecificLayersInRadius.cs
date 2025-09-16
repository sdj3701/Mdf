// Assets/Scripts/Game/Skills/Targeting/SpecificLayersInRadius.cs (새 파일)
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

[CreateAssetMenu(fileName = "New SpecificLayersInRadius", menuName = "Game/Skills/Targeting/Specific Layers in Radius")]
public class SpecificLayersInRadius : TargetingStrategy
{
    [Header("타겟 설정")]
    [Tooltip("이 스킬이 대상으로 삼을 레이어들을 선택합니다.")]
    public LayerMask targetLayers;

    public override List<GameObject> FindTargets(GameObject caster, Vector3 targetPosition, float range)
    {
        // 이 타겟팅 전략은 시전자의 Allegiance 컴포넌트를 사용하지 않고,
        // 인스펙터에 직접 설정된 targetLayers를 사용합니다.
        var colliders = Physics2D.OverlapCircleAll(targetPosition, range, targetLayers);
        
        // Collider2D 배열에서 GameObject 리스트로 변환하여 반환합니다.
        return colliders.Select(col => col.gameObject).ToList();
    }
}
