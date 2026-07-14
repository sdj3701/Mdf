// Assets/Scripts/Game/Skills/StatusEffectApplier.cs
using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "New StatusEffect", menuName = "Game/Skills/Effects/Status Effect")]
public class StatusEffectApplier : SkillEffect, IDurationEffect
{
    [Header("상태 효과 선택")]
    [Tooltip("적용할 상태 효과 타입")]
    public StatusEffectType effectType = StatusEffectType.Slowed;
    
    [Header("기본 설정")]
    [Tooltip("효과 지속 시간 (초)")]
    public float duration = 3f;
    
    [Header("슬로우 설정 (Slowed, Frostbitten)")]
    [Tooltip("이동속도 배율 (0.5 = 50% 감소)")]
    [Range(0.1f, 1f)]
    public float slowMultiplier = 1f;
    
    [Header("DoT 설정 (Burning, Frostbitten, Bleeding, Poisoned)")]
    [Tooltip("틱당 데미지")]
    public float damagePerTick = 0f;
    
    [Tooltip("틱 간격 (초)")]
    public float tickInterval = 1f;
    
    [Tooltip("DoT 데미지 타입")]
    public DamageType dotDamageType = DamageType.Magic;
    
    public float Duration => duration;

    public override bool CanApplyEffect(
        MonoBehaviour runner,
        GameObject caster,
        List<GameObject> targets,
        float skillRange,
        TargetingStrategy targetingStrategy)
    {
        CombatScheduler scheduler = CombatScheduler.Instance;
        if (scheduler == null || !scheduler.IsStatusEffectSchedulerActive)
        {
            return false;
        }

        List<BuffManager> targetBuffs = CollectTargetBuffs(targets);
        ResolveParameters(out float scheduledTickInterval, out float scheduledDamage, out float scheduledSlow);
        return scheduler.CanApplyStatusEffectBatch(
            targetBuffs,
            effectType,
            duration,
            caster,
            scheduledTickInterval,
            scheduledDamage,
            scheduledSlow,
            dotDamageType);
    }

    public override bool TryApplyEffect(
        MonoBehaviour runner,
        GameObject caster,
        List<GameObject> targets,
        float skillRange,
        TargetingStrategy targetingStrategy)
    {
        ResolveParameters(out float scheduledTickInterval, out float scheduledDamage, out float scheduledSlow);
        bool applied = true;
        List<BuffManager> targetBuffs = CollectTargetBuffs(targets);
        for (int i = 0; i < targetBuffs.Count; i++)
        {
            applied &= targetBuffs[i].ApplyStatusEffect(
                effectType,
                duration,
                caster,
                scheduledTickInterval,
                scheduledDamage,
                scheduledSlow,
                dotDamageType);
        }

        return applied;
    }

    public override void ApplyEffect(
        MonoBehaviour runner, 
        GameObject caster, 
        List<GameObject> targets, 
        float skillRange, 
        TargetingStrategy targetingStrategy)
    {
        TryApplyEffect(runner, caster, targets, skillRange, targetingStrategy);
    }

    private List<BuffManager> CollectTargetBuffs(List<GameObject> targets)
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

    private void ResolveParameters(out float scheduledTickInterval, out float scheduledDamage, out float scheduledSlow)
    {
        bool needsSlow = effectType == StatusEffectType.Slowed || effectType == StatusEffectType.Frostbitten;
        bool needsDot = effectType == StatusEffectType.Burning ||
                        effectType == StatusEffectType.Frostbitten ||
                        effectType == StatusEffectType.Bleeding ||
                        effectType == StatusEffectType.Poisoned;
        scheduledTickInterval = needsDot && damagePerTick > 0f ? tickInterval : 0f;
        scheduledDamage = needsDot ? damagePerTick : 0f;
        scheduledSlow = needsSlow ? slowMultiplier : 1f;
    }
}
