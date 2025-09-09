// Assets/Scripts/Game/Skills/Targeting/SelfTarget.cs
using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "New SelfTarget", menuName = "Game/Skills/Targeting/Self")]
public class SelfTarget : TargetingStrategy
{
    public override List<GameObject> FindTargets(GameObject caster, Vector3 targetPosition, float range)
    {
        // 자기 자신만 리스트에 담아 반환
        return new List<GameObject> { caster };
    }
}
