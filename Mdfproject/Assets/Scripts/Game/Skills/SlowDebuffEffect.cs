// Assets/Scripts/Game/Skills/Effects/SlowDebuffEffect.cs
using UnityEngine;
using System.Collections.Generic;
// using Fusion; // 네트워크 도입 시 주석 해제

[CreateAssetMenu(fileName = "New SlowDebuffEffect", menuName = "Game/Skills/Effects/Slow Debuff")]
public class SlowDebuffEffect : SkillEffect, IDurationEffect
{
    [Header("디버프 설정")]
    [Range(0.01f, 1f)]
    public float moveSpeedMultiplier = 0.5f;
    public float duration;

    // ✅ [수정] 인터페이스 구현
    public float Duration => duration;

    public override void ApplyEffect(MonoBehaviour runner, GameObject caster, List<GameObject> targets)
    {
        foreach (var target in targets)
        {
            if (target != null && target.TryGetComponent<BuffManager>(out var buffManager))
            {
                buffManager.ApplyDebuff(this, caster);
            }
        }
    }
}