// Assets/Scripts/Game/Skills/Effects/SkillEffect.cs
using UnityEngine;
using System.Collections.Generic;
// using Fusion;

public abstract class SkillEffect : ScriptableObject
{
    public static bool CanApplyAllEffects(
        IReadOnlyList<SkillEffect> effects,
        MonoBehaviour runner,
        GameObject caster,
        List<GameObject> targets,
        float skillRange,
        TargetingStrategy targetingStrategy)
    {
        return CanApplyAllEffectsRepeated(
            effects,
            1,
            runner,
            caster,
            targets,
            skillRange,
            targetingStrategy);
    }

    public static bool CanApplyAllEffectsRepeated(
        IReadOnlyList<SkillEffect> effects,
        int repeatCount,
        MonoBehaviour runner,
        GameObject caster,
        List<GameObject> targets,
        float skillRange,
        TargetingStrategy targetingStrategy)
    {
        return CanApplyEffectsFromIndex(
            effects,
            0,
            repeatCount,
            runner,
            caster,
            targets,
            skillRange,
            targetingStrategy);
    }

    public static bool CanApplyEffectsFromIndex(
        IReadOnlyList<SkillEffect> effects,
        int startIndex,
        int repeatCount,
        MonoBehaviour runner,
        GameObject caster,
        List<GameObject> targets,
        float skillRange,
        TargetingStrategy targetingStrategy)
    {
        CombatScheduler scheduler = CombatScheduler.Instance;
        bool ownsPreflight = scheduler != null && scheduler.TryBeginEffectCapacityPreflight();
        if (scheduler != null && !ownsPreflight)
        {
            return false;
        }

        try
        {
            if (effects == null)
            {
                return true;
            }

            int repeats = Mathf.Max(1, repeatCount);
            int firstEffect = Mathf.Clamp(startIndex, 0, effects.Count);
            for (int repeat = 0; repeat < repeats; repeat++)
            {
                for (int i = firstEffect; i < effects.Count; i++)
                {
                    SkillEffect effect = effects[i];
                    if (effect != null && !effect.CanApplyEffect(
                            runner,
                            caster,
                            targets,
                            skillRange,
                            targetingStrategy))
                    {
                        return false;
                    }
                }
            }

            return true;
        }
        finally
        {
            if (ownsPreflight)
            {
                scheduler.EndEffectCapacityPreflight();
            }
        }
    }

    // NetworkRunner 대신 범용적인 MonoBehaviour를 받도록 변경
    // public abstract void ApplyEffect(NetworkRunner runner, GameObject caster, List<GameObject> targets);
    public abstract void ApplyEffect(MonoBehaviour runner, GameObject caster, List<GameObject> targets, float skillRange, TargetingStrategy targetingStrategy);

    public virtual bool CanApplyEffect(
        MonoBehaviour runner,
        GameObject caster,
        List<GameObject> targets,
        float skillRange,
        TargetingStrategy targetingStrategy)
    {
        return true;
    }

    public virtual bool TryApplyEffect(
        MonoBehaviour runner,
        GameObject caster,
        List<GameObject> targets,
        float skillRange,
        TargetingStrategy targetingStrategy)
    {
        ApplyEffect(runner, caster, targets, skillRange, targetingStrategy);
        return true;
    }
}
