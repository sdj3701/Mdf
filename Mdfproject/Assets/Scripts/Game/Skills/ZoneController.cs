using System.Collections.Generic;
using UnityEngine;

public class ZoneController : MonoBehaviour
{
    private ZoneEffect zoneEffect;
    private GameObject caster;
    private MonoBehaviour runner;
    private float skillRange;
    private TargetingStrategy targetingStrategy;

    private float remainingDuration;
    private float tickTimer;
    private bool isInitialized;

    private void OnEnable()
    {
        GameEvents.OnGameStateChanged += HandleGameStateChanged;
    }

    private void OnDisable()
    {
        GameEvents.OnGameStateChanged -= HandleGameStateChanged;
    }

    public void Initialize(ZoneEffect effect, GameObject caster, MonoBehaviour runner, float skillRange, TargetingStrategy targetingStrategy)
    {
        zoneEffect = effect;
        this.caster = caster;
        this.runner = runner;
        this.skillRange = skillRange;
        this.targetingStrategy = targetingStrategy;

        remainingDuration = effect.zoneDuration;
        tickTimer = 0f;
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

    private void Update()
    {
        if (!isInitialized || !HasStateAuthorityOrNoNetwork())
        {
            return;
        }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (MPTestCommandLine.IsGameFlowFrozen)
        {
            return;
        }
#endif

        remainingDuration -= Time.deltaTime;
        if (remainingDuration <= 0)
        {
            LogZone($"<color=magenta>[Zone] {zoneEffect.name} ended.</color>");
            Destroy(gameObject);
            return;
        }

        tickTimer -= Time.deltaTime;
        if (tickTimer <= 0)
        {
            ApplyTickEffects();
            tickTimer = zoneEffect.tickInterval;
        }
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
            Destroy(gameObject);
        }
    }

    public bool IsSnapshotActive => isInitialized;

    public string BuildSnapshotPart()
    {
        string effectName = zoneEffect != null ? zoneEffect.name : "unknown";
        string targeterName = targetingStrategy != null ? targetingStrategy.name : "unknown";
        int rangeBucket = Mathf.RoundToInt(skillRange * 10f);
        int tickBucket = zoneEffect != null ? Mathf.RoundToInt(zoneEffect.tickInterval * 10f) : 0;
        int durationBucket = zoneEffect != null ? Mathf.RoundToInt(zoneEffect.zoneDuration * 10f) : 0;
        Vector3 pos = transform.position;
        int xBucket = Mathf.RoundToInt(pos.x * 10f);
        int zBucket = Mathf.RoundToInt(pos.z * 10f);
        return $"zone={effectName};targeting={targeterName};range={rangeBucket};tick={tickBucket};duration={durationBucket};x={xBucket};z={zBucket}";
    }

    private bool HasStateAuthorityOrNoNetwork()
    {
        var gm = GameManagers.Instance;
        if (gm != null &&
            gm.Runner != null &&
            gm.Runner.IsRunning &&
            gm.Object != null)
        {
            return gm.Object.HasStateAuthority;
        }

        return true;
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
