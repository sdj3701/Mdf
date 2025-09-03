// Assets/Scripts/Game/Skills/Effects/SlowDebuffEffect.cs
using UnityEngine;
using System.Collections.Generic;
// using Fusion; // 네트워크 도입 시 주석 해제

[CreateAssetMenu(fileName = "New SlowDebuffEffect", menuName = "Game/Skills/Effects/Slow Debuff")]
public class SlowDebuffEffect : SkillEffect
{
    [Header("디버프 설정")]
    [Tooltip("이동 속도에 곱해질 값입니다. 0.5는 이동 속도를 50%로 만듭니다.")]
    [Range(0.01f, 1f)]
    public float moveSpeedMultiplier = 0.5f;

    [Tooltip("디버프가 지속될 시간(초)입니다.")]
    public float duration;

    // public override void ApplyEffect(NetworkRunner runner, GameObject caster, List<GameObject> targets)
    public override void ApplyEffect(MonoBehaviour runner, GameObject caster, List<GameObject> targets)
    {
        // if (runner != null && !runner.IsServer) return; // 네트워크 모드에서는 이 라인이 필요합니다.

        foreach (var target in targets)
        {
            // 대상에게 BuffManager가 있어야 디버프를 적용할 수 있습니다.
            if (target != null && target.TryGetComponent<BuffManager>(out var buffManager))
            {
                buffManager.ApplyDebuff(this);
            }
        }
    }
}