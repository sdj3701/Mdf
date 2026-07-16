// Assets/Scripts/Game/Skills/Effects/BuffStatEffect.cs
using UnityEngine;
using System.Collections.Generic;
// using Fusion;

[CreateAssetMenu(fileName = "New BuffStatEffect", menuName = "Game/Skills/Effects/Buff Stat")]
public class BuffStatEffect : SkillEffect, IDurationEffect
{
    [Header("버프 설정")]
    public StatType statToBuff;
    public float value;
    public bool isPercentage;
    public float duration;

    // ✅ [수정] 인터페이스 구현
    public float Duration => duration;

    public override bool CanApplyEffect(
        MonoBehaviour runner,
        GameObject caster,
        List<GameObject> targets,
        float skillRange,
        TargetingStrategy targetingStrategy)
    {
        CombatScheduler scheduler = CombatScheduler.Instance;
        return scheduler != null &&
               scheduler.IsStatBuffSchedulerActive &&
               scheduler.CanApplyStatBuffBatch(CollectTargetBuffs(targets), this, caster);
    }

    public override bool TryApplyEffect(
        MonoBehaviour runner,
        GameObject caster,
        List<GameObject> targets,
        float skillRange,
        TargetingStrategy targetingStrategy)
    {
        bool applied = true;
        List<BuffManager> targetBuffs = CollectTargetBuffs(targets);
        for (int i = 0; i < targetBuffs.Count; i++)
        {
            applied &= targetBuffs[i].ApplyBuff(this, caster);
        }

        return applied;
    }

    public override void ApplyEffect(MonoBehaviour runner, GameObject caster, List<GameObject> targets, float skillRange, TargetingStrategy targetingStrategy)
    {
        TryApplyEffect(runner, caster, targets, skillRange, targetingStrategy);
    }

    private static List<BuffManager> CollectTargetBuffs(List<GameObject> targets)
    {
        var result = new List<BuffManager>();
        if (targets == null)
        {
            return result;
        }

        for (int i = 0; i < targets.Count; i++)
        {
            GameObject target = targets[i];
            if (target != null && target.TryGetComponent(out BuffManager buffManager) && !result.Contains(buffManager))
            {
                result.Add(buffManager);
            }
        }

        return result;
    }
}
