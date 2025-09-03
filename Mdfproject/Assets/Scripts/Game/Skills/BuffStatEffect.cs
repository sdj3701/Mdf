// Assets/Scripts/Game/Skills/Effects/BuffStatEffect.cs
using UnityEngine;
using System.Collections.Generic;
// using Fusion;

[CreateAssetMenu(fileName = "New BuffStatEffect", menuName = "Game/Skills/Effects/Buff Stat")]
public class BuffStatEffect : SkillEffect
{
    [Header("버프 설정")]
    public StatType statToBuff;
    public float value;
    public bool isPercentage;
    public float duration;

    // public override void ApplyEffect(NetworkRunner runner, GameObject caster, List<GameObject> targets)
    public override void ApplyEffect(MonoBehaviour runner, GameObject caster, List<GameObject> targets)
    {
        // if (runner != null && !runner.IsServer) return;

        foreach (var target in targets)
        {
            if (target != null && target.TryGetComponent<BuffManager>(out var buffManager))
            {
                buffManager.ApplyBuff(this);
            }
        }
    }
}