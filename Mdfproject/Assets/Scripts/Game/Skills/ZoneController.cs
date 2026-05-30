using System.Collections.Generic;
using UnityEngine;

public class ZoneController : MonoBehaviour
{
    private ZoneEffect zoneEffect;
    private GameObject caster;
    private MonoBehaviour runner;
    private float skillRange;
    private TargetingStrategy targetingStrategy;

    private bool isInitialized;
    private int schedulerSequence;

    private void OnEnable()
    {
        GameEvents.OnGameStateChanged += HandleGameStateChanged;
    }

    private void OnDisable()
    {
        GameEvents.OnGameStateChanged -= HandleGameStateChanged;
    }

    public void Initialize(
        ZoneEffect effect,
        GameObject caster,
        MonoBehaviour runner,
        float skillRange,
        TargetingStrategy targetingStrategy,
        int schedulerSequence = 0)
    {
        zoneEffect = effect;
        this.caster = caster;
        this.runner = runner;
        this.skillRange = skillRange;
        this.targetingStrategy = targetingStrategy;
        this.schedulerSequence = schedulerSequence;
        isInitialized = true;

        LogZone($"<color=magenta>[Zone] {effect.name} created. duration={effect.zoneDuration:F1}s range={skillRange:F1}</color>");
    }

    private static void LogZone(string message)
    {
        Debug.Log(message);
    }

    private static void LogZoneWarning(string message)
    {
        Debug.LogWarning(message);
    }

    public void ApplyScheduledTick()
    {
        if (!isInitialized)
        {
            return;
        }

        ApplyTickEffects();
    }

    public void DestroyScheduledZone()
    {
        if (this == null)
        {
            return;
        }

        LogZone($"<color=magenta>[Zone] {(zoneEffect != null ? zoneEffect.name : "unknown")} ended.</color>");
        Destroy(gameObject);
    }

    private void ApplyTickEffects()
    {
        if (zoneEffect.effectsPerTick == null || zoneEffect.effectsPerTick.Count == 0)
        {
            LogZoneWarning($"[Zone] {zoneEffect.name} has no tick effects.");
            return;
        }

        List<GameObject> targetsInZone = FindTargetsInZone();
        if (targetsInZone.Count == 0)
        {
            return;
        }

        LogZone($"<color=magenta>[Zone] {zoneEffect.name} applying effects to {targetsInZone.Count} targets.</color>");

        foreach (var effect in zoneEffect.effectsPerTick)
        {
            if (effect != null)
            {
                effect.ApplyEffect(runner, caster, targetsInZone, skillRange, targetingStrategy);
            }
        }
    }

    private List<GameObject> FindTargetsInZone()
    {
        if (targetingStrategy != null)
        {
            return targetingStrategy.FindTargets(caster, transform.position, skillRange);
        }

        LogZoneWarning("[Zone] TargetingStrategy is missing.");
        return new List<GameObject>();
    }

    private void HandleGameStateChanged(GameManagers.GameState newState)
    {
        if (!isInitialized)
        {
            return;
        }

        if (newState != GameManagers.GameState.Battle1 &&
            newState != GameManagers.GameState.Battle2)
        {
            if (schedulerSequence > 0)
            {
                CombatScheduler.Instance?.ClearScheduledZone(schedulerSequence, $"stateChanged:{newState}");
                return;
            }

            Destroy(gameObject);
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (!isInitialized)
        {
            return;
        }

        Gizmos.color = new Color(1f, 0f, 0f, 0.3f);
        Gizmos.DrawSphere(transform.position, skillRange);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, skillRange);
    }
}
