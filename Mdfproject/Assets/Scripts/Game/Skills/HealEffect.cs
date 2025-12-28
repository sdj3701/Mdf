// Assets/Scripts/Game/Skills/Effects/HealEffect.cs (새 파일)
using UnityEngine;
using System.Collections.Generic;
// using Fusion; // 네트워크 도입 시 주석 해제

[CreateAssetMenu(fileName = "New HealEffect", menuName = "Game/Skills/Effects/Heal")]
public class HealEffect : SkillEffect
{
    [Header("회복 설정")]
    [Tooltip("회복시킬 체력의 양입니다.")]
    public float healAmount;

    // public override void ApplyEffect(NetworkRunner runner, GameObject caster, List<GameObject> targets)
    public override void ApplyEffect(MonoBehaviour runner, GameObject caster, List<GameObject> targets, float skillRange, TargetingStrategy targetingStrategy)
    {
        // if (runner != null && !runner.IsServer) return; // 네트워크 모드에서는 이 라인이 필요합니다.

        foreach (var target in targets)
        {
            // 대상이 IHealth 인터페이스를 가지고 있는지 확인합니다.
            if (target != null && target.TryGetComponent<IHealth>(out var healthComponent))
            {
                healthComponent.Heal(healAmount);
            }
        }
    }
}