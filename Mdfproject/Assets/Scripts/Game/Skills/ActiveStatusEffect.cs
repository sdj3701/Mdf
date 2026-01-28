// Assets/Scripts/Game/Skills/ActiveStatusEffect.cs
using UnityEngine;

public class ActiveStatusEffect
{
    public StatusEffectType Type { get; }
    public float RemainingDuration { get; set; }
    public float TickInterval { get; }
    public float NextTickTime { get; set; }
    public float DamagePerTick { get; }
    public float SlowMultiplier { get; }
    public DamageType DamageType { get; }
    public GameObject Caster { get; }
    
    public ActiveStatusEffect(
        StatusEffectType type,
        float duration,
        GameObject caster,
        float tickInterval = 0f,
        float damagePerTick = 0f,
        float slowMultiplier = 1f,
        DamageType damageType = DamageType.Physical)
    {
        Type = type;
        RemainingDuration = duration;
        Caster = caster;
        TickInterval = tickInterval;
        DamagePerTick = damagePerTick;
        SlowMultiplier = slowMultiplier;
        DamageType = damageType;
        NextTickTime = tickInterval > 0 ? Time.time + tickInterval : float.MaxValue;
    }
}
