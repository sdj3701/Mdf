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

    public override void ApplyEffect(
        MonoBehaviour runner, 
        GameObject caster, 
        List<GameObject> targets, 
        float skillRange, 
        TargetingStrategy targetingStrategy)
    {
        foreach (var target in targets)
        {
            if (target == null) continue;
            
            if (target.TryGetComponent<BuffManager>(out var buffManager))
            {
                bool needsSlow = effectType == StatusEffectType.Slowed || 
                                 effectType == StatusEffectType.Frostbitten;
                
                bool needsDot = effectType == StatusEffectType.Burning || 
                                effectType == StatusEffectType.Frostbitten ||
                                effectType == StatusEffectType.Bleeding ||
                                effectType == StatusEffectType.Poisoned;
                
                buffManager.ApplyStatusEffect(
                    effectType,
                    duration,
                    caster,
                    tickInterval: needsDot && damagePerTick > 0 ? tickInterval : 0f,
                    damagePerTick: needsDot ? damagePerTick : 0f,
                    slowMultiplier: needsSlow ? slowMultiplier : 1f,
                    damageType: dotDamageType
                );
            }
        }
    }
}
