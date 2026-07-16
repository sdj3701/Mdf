// Assets/Scripts/Game/Skills/ZoneEffect.cs
using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "New ZoneEffect", menuName = "Game/Skills/Effects/Zone Effect")]
public class ZoneEffect : SkillEffect, IDurationEffect
{
    [Header("Zone Settings")]
    [Tooltip("Duration in seconds.")]
    public float zoneDuration = 5f;

    [Tooltip("Tick interval in seconds.")]
    public float tickInterval = 1f;

    [Header("Tick Effects")]
    [Tooltip("Effects applied to targets inside the zone on every tick.")]
    public List<SkillEffect> effectsPerTick;

    [Header("Visual Effect")]
    [Tooltip("Zone visual prefab. Runtime duration and ticks are managed by CombatScheduler.")]
    public GameObject zonePrefab;

    public float Duration => zoneDuration;

    public override bool CanApplyEffect(
        MonoBehaviour runner,
        GameObject caster,
        List<GameObject> targets,
        float skillRange,
        TargetingStrategy targetingStrategy)
    {
        CombatScheduler scheduler = CombatScheduler.Instance;
        return scheduler != null &&
               scheduler.IsZoneSchedulerActive &&
               scheduler.CanScheduleZone(this, caster, skillRange, targetingStrategy);
    }

    public override bool TryApplyEffect(
        MonoBehaviour runner,
        GameObject caster,
        List<GameObject> targets,
        float skillRange,
        TargetingStrategy targetingStrategy)
    {
        CombatScheduler scheduler = CombatScheduler.Instance;
        return scheduler != null &&
               scheduler.IsZoneSchedulerActive &&
               scheduler.Object != null &&
               scheduler.Object.HasStateAuthority &&
               scheduler.TryScheduleZone(this, caster, runner, skillRange, targetingStrategy, out _);
    }

    public override void ApplyEffect(MonoBehaviour runner, GameObject caster, List<GameObject> targets, float skillRange, TargetingStrategy targetingStrategy)
    {
        if (TryApplyEffect(runner, caster, targets, skillRange, targetingStrategy))
        {
            return;
        }

        Debug.LogWarning($"[ZoneEffect] Ignored zone without active CombatScheduler. effect={name}");
    }
}
