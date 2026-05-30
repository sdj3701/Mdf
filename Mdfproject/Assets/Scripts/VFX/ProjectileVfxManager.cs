using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Fusion;
using UnityEngine;

public class ProjectileVfxManager : MonoBehaviour
{
    [SerializeField] private VfxPoolManager pool;
    [SerializeField] private Transform vfxRoot;
    [SerializeField] private float minRemainingSeconds = 0.02f;
    [SerializeField] private float maxSpeed = 200f;
    [SerializeField] private bool alignToDirection = true;

    [Header("Network Compensation")]
    [Tooltip("Limits spawn progress for delayed network events. 0 always spawns at fire position, 1 allows exact catch-up position.")]
    [SerializeField, Range(0f, 1f)] private float maxSpawnProgress = 0.3f;

    private CombatScheduler _scheduler;
    private int _lastProcessedSeq;
    private bool _didCatchup;
    private readonly List<CombatScheduler.ProjectileEventData> _catchupEvents = new List<CombatScheduler.ProjectileEventData>();
    private readonly Dictionary<int, ActiveProjectile> _activeBySeq = new Dictionary<int, ActiveProjectile>();
    private readonly List<ActiveProjectile> _activeProjectiles = new List<ActiveProjectile>();

    private class ActiveProjectile
    {
        public int Sequence;
        public int FireTick;
        public int HitTick;
        public GameObject Instance;
        public NetworkObject TargetObject;
        public uint TargetNetworkIdRaw;
        public Transform TargetTransform;
        public Unit TargetUnit;
        public Monster TargetMonster;
        public Vector3 LastKnownTargetPos;
        public Vector3 LastKnownDirection;
        public ProjectileVfxConfig Config;
    }

    private void Awake()
    {
        if (pool == null)
        {
            pool = VfxPoolManager.Instance;
        }
    }

    private static void LogProjectile(string message)
    {
        Debug.Log(message);
    }

    private void Update()
    {
        if (_scheduler == null)
        {
            _scheduler = CombatScheduler.Instance;
            if (_scheduler == null)
            {
                return;
            }

            LogProjectile($"[ProjectileVfxManager] Scheduler found: {_scheduler.name}, HasStateAuthority: {_scheduler.Object?.HasStateAuthority}");
        }

        if (_scheduler.Runner == null || !_scheduler.Runner.IsRunning)
        {
            return;
        }

        if (!_didCatchup)
        {
            CatchupInFlight();
            _didCatchup = true;
        }

        ProcessNewEvents();
        UpdateActiveProjectiles();
    }

    private void CatchupInFlight()
    {
        int nowTick = _scheduler.Runner.Tick;
        _scheduler.GetInFlightEvents(nowTick, _catchupEvents);
        for (int i = 0; i < _catchupEvents.Count; i++)
        {
            HandleProjectileEvent(_catchupEvents[i]);
        }

        _lastProcessedSeq = _scheduler.EventSequence;
    }

    private void ProcessNewEvents()
    {
        int currentSeq = _scheduler.EventSequence;
        if (currentSeq <= 0)
        {
            return;
        }

        int minSeq = Mathf.Max(1, currentSeq - _scheduler.EventCapacity + 1);
        int startSeq = Mathf.Max(_lastProcessedSeq + 1, minSeq);

        if (startSeq <= currentSeq)
        {
            Debug.Log($"[ProjectileVfxManager] Processing events {startSeq} to {currentSeq}");
        }

        for (int seq = startSeq; seq <= currentSeq; seq++)
        {
            if (_scheduler.TryGetEvent(seq, out var evt))
            {
                Debug.Log($"[ProjectileVfxManager] Handling event seq={seq}, Attacker={(evt.Attacker != null ? evt.Attacker.name : "null")}, Target={(evt.Target != null ? evt.Target.name : "null")}");
                HandleProjectileEvent(evt);
            }
        }

        _lastProcessedSeq = currentSeq;
    }

    private void HandleProjectileEvent(CombatScheduler.ProjectileEventData evt)
    {
        if (_activeBySeq.ContainsKey(evt.Sequence))
        {
            return;
        }

        SpawnProjectileAsync(evt).Forget();
    }

    private async UniTaskVoid SpawnProjectileAsync(CombatScheduler.ProjectileEventData evt)
    {
        var runner = _scheduler.Runner;
        if (runner == null)
        {
            Debug.LogWarning($"[ProjectileVfxManager] SpawnProjectile FAILED: Runner is null (seq={evt.Sequence})");
            return;
        }

        if (!TryResolveProjectileVfx(evt, out var config))
        {
            Debug.LogWarning($"[ProjectileVfxManager] SpawnProjectile FAILED: Could not resolve projectile VFX config (seq={evt.Sequence}, Attacker={(evt.Attacker != null ? evt.Attacker.name : "null")})");
            return;
        }

        Vector3 firePos = ResolveFirePosition(evt);
        Vector3 targetPos = ResolveTargetPosition(evt, firePos);
        Vector3 travelDirection = ResolveTravelDirection(firePos, targetPos, evt.Attacker);
        float nowTime = GetRenderTime(runner);
        float fireTime = evt.FireTick * runner.DeltaTime;
        float hitTime = evt.HitTick * runner.DeltaTime;

        if (hitTime <= nowTime)
        {
            Debug.LogWarning($"[ProjectileVfxManager] SpawnProjectile SKIPPED: hitTime({hitTime:F3}) <= nowTime({nowTime:F3}) (seq={evt.Sequence})");
            SpawnImpactFlashAsync(config, targetPos, travelDirection).Forget();
            return;
        }

        SpawnMuzzleFlashAsync(config, firePos, travelDirection).Forget();

        string projectileKey = config.projectileKey;
        Debug.Log($"[ProjectileVfxManager] Loading projectile: {projectileKey} (seq={evt.Sequence})");

        GameObject prefab = await AssetLoader.LoadAssetAsync<GameObject>(projectileKey);
        if (prefab == null)
        {
            Debug.LogWarning($"[ProjectileVfxManager] SpawnProjectile FAILED: Prefab load returned null for key '{projectileKey}' (seq={evt.Sequence})");
            return;
        }

        if (this == null || !isActiveAndEnabled || _scheduler == null || _scheduler.Runner == null)
        {
            return;
        }

        runner = _scheduler.Runner;
        nowTime = GetRenderTime(runner);
        hitTime = evt.HitTick * runner.DeltaTime;
        firePos = ResolveFirePosition(evt);
        targetPos = ResolveTargetPositionIfTrackable(evt.Target, targetPos);
        travelDirection = ResolveTravelDirection(firePos, targetPos, evt.Attacker);

        if (hitTime <= nowTime)
        {
            Debug.LogWarning($"[ProjectileVfxManager] SpawnProjectile SKIPPED after load: hitTime({hitTime:F3}) <= nowTime({nowTime:F3}) (seq={evt.Sequence})");
            SpawnImpactFlashAsync(config, targetPos, travelDirection).Forget();
            return;
        }

        Vector3 pathPos = CalculateSpawnPosition(firePos, targetPos, fireTime, hitTime, nowTime);
        Quaternion projectileRotation = ProjectileVfxRuntimeUtility.ResolveVfxRotation(travelDirection, config.alignProjectileToDirection && alignToDirection, config.projectileRotationOffsetEuler);
        Vector3 spawnPos = ProjectileVfxRuntimeUtility.ApplyLocalOffset(pathPos, projectileRotation, config.projectileLocalPositionOffset);

        Debug.Log($"[ProjectileVfxManager] Spawning projectile at {spawnPos}, pool={(pool != null ? "exists" : "null")}, vfxRoot={(vfxRoot != null ? vfxRoot.name : "null")} (seq={evt.Sequence})");

        GameObject instance = pool != null
            ? pool.Spawn(prefab, spawnPos, projectileRotation, vfxRoot)
            : Instantiate(prefab, spawnPos, projectileRotation, vfxRoot);

        if (instance == null)
        {
            Debug.LogWarning($"[ProjectileVfxManager] SpawnProjectile FAILED: Instance is null after spawn (seq={evt.Sequence})");
            return;
        }

        instance.transform.localScale = ProjectileVfxRuntimeUtility.MultiplyScale(prefab.transform.localScale, config.ResolveProjectileScaleMultiplierVector());
        ProjectileVfxRuntimeUtility.PrepareVisualProjectile(instance);
        ProjectileVfxRuntimeUtility.RestartParticles(instance, config.ResolveProjectilePlaybackSpeed());
        CancelAutoDestroy(instance);

        Debug.Log($"[ProjectileVfxManager] Projectile spawned successfully: {instance.name} (seq={evt.Sequence})");

        var active = new ActiveProjectile
        {
            Sequence = evt.Sequence,
            FireTick = evt.FireTick,
            HitTick = evt.HitTick,
            Instance = instance,
            LastKnownTargetPos = targetPos,
            LastKnownDirection = travelDirection,
            Config = config
        };
        BindTarget(active, evt.Target);

        _activeBySeq[evt.Sequence] = active;
        _activeProjectiles.Add(active);
    }

    private Vector3 CalculateSpawnPosition(Vector3 firePos, Vector3 targetPos, float fireTime, float hitTime, float nowTime)
    {
        float totalTime = Mathf.Max(0.0001f, hitTime - fireTime);
        float rawProgress = Mathf.Clamp01((nowTime - fireTime) / totalTime);
        float progress = Mathf.Min(rawProgress, maxSpawnProgress);
        return Vector3.Lerp(firePos, targetPos, progress);
    }

    private bool TryResolveProjectileVfx(CombatScheduler.ProjectileEventData evt, out ProjectileVfxConfig config)
    {
        config = null;
        if (evt.Attacker == null)
        {
            return false;
        }

        var unit = evt.Attacker.GetComponent<Unit>();
        if (unit != null && unit.Data != null)
        {
            config = unit.Data.GetProjectileVfxConfig();
            return config != null && config.HasProjectileKey;
        }

        var monster = evt.Attacker.GetComponent<Monster>();
        if (monster != null && TryResolveMonsterProjectileKey(evt, monster, out string monsterProjectileKey))
        {
            config = ProjectileVfxConfig.CreateDefault(monsterProjectileKey);
            return true;
        }

        return false;
    }

    private bool TryResolveMonsterProjectileKey(CombatScheduler.ProjectileEventData evt, Monster monster, out string projectileKey)
    {
        projectileKey = null;

        if (monster.Data != null)
        {
            projectileKey = monster.Data.projectilePrefab;
            if (!string.IsNullOrEmpty(projectileKey))
            {
                return true;
            }

            Debug.LogWarning($"[ProjectileVfxManager] Monster '{monster.name}' has Data but projectilePrefab is empty!");
            return false;
        }

        string monsterName = evt.Attacker.name.Replace("(Clone)", "").Trim();
        Debug.Log($"[ProjectileVfxManager] Monster '{monsterName}' Data is null, trying prefab cache fallback...");

        var prefab = AssetLoader.GetCachedAsset<GameObject>(monsterName);
        if (prefab == null)
        {
            Debug.LogWarning($"[ProjectileVfxManager] Monster '{monsterName}' prefab not found in AssetLoader cache");
            return false;
        }

        var prefabMonster = prefab.GetComponent<Monster>();
        if (prefabMonster == null || prefabMonster.Data == null)
        {
            Debug.LogWarning($"[ProjectileVfxManager] Prefab '{monsterName}' found but Monster component or Data is null");
            return false;
        }

        projectileKey = prefabMonster.Data.projectilePrefab;
        if (!string.IsNullOrEmpty(projectileKey))
        {
            Debug.Log($"[ProjectileVfxManager] Monster Data fallback from prefab: {monsterName} -> {projectileKey}");
            return true;
        }

        Debug.LogWarning($"[ProjectileVfxManager] Prefab monster '{monsterName}' has Data but projectilePrefab is empty!");
        return false;
    }

    // Fire position is resolved from the attacker to keep network payload small.
    private Vector3 ResolveFirePosition(CombatScheduler.ProjectileEventData evt)
    {
        if (evt.Attacker == null)
        {
            return Vector3.zero;
        }

        var unit = evt.Attacker.GetComponent<Unit>();
        if (unit != null && unit.firePoint != null)
        {
            return unit.firePoint.position;
        }

        var monster = evt.Attacker.GetComponent<Monster>();
        if (monster != null)
        {
            if (monster.firePoint != null)
            {
                return monster.firePoint.position;
            }

            return evt.Attacker.transform.position + Vector3.up * 0.5f;
        }

        return evt.Attacker.transform.position;
    }

    private static Vector3 ResolveTargetPosition(CombatScheduler.ProjectileEventData evt, Vector3 fallback)
    {
        return evt.Target != null ? evt.Target.transform.position : fallback;
    }

    private static Vector3 ResolveTargetPositionIfTrackable(NetworkObject target, Vector3 fallback)
    {
        if (target == null)
        {
            return fallback;
        }

        Transform targetTransform = target.transform;
        uint targetNetworkIdRaw = target.IsValid ? target.Id.Raw : 0;
        Unit targetUnit = target.GetComponent<Unit>();
        Monster targetMonster = target.GetComponent<Monster>();
        return IsTrackedTargetStillLive(targetTransform, target, targetNetworkIdRaw, targetUnit, targetMonster)
            ? targetTransform.position
            : fallback;
    }

    private static void BindTarget(ActiveProjectile active, NetworkObject target)
    {
        if (active == null || target == null)
        {
            return;
        }

        active.TargetObject = target;
        active.TargetNetworkIdRaw = target.IsValid ? target.Id.Raw : 0;
        active.TargetTransform = target.transform;
        active.TargetUnit = target.GetComponent<Unit>();
        active.TargetMonster = target.GetComponent<Monster>();

        if (!IsTrackedTargetStillLive(active))
        {
            FreezeTrackedTarget(active);
        }
    }

    private static bool TryRefreshTrackedTargetPosition(ActiveProjectile active, out Vector3 targetOrigin)
    {
        targetOrigin = active.LastKnownTargetPos;
        if (active.TargetTransform == null)
        {
            return false;
        }

        if (!IsTrackedTargetStillLive(active))
        {
            FreezeTrackedTarget(active);
            return false;
        }

        targetOrigin = active.TargetTransform.position;
        active.LastKnownTargetPos = targetOrigin;
        return true;
    }

    private static bool IsTrackedTargetStillLive(ActiveProjectile active)
    {
        if (active == null)
        {
            return false;
        }

        return IsTrackedTargetStillLive(
            active.TargetTransform,
            active.TargetObject,
            active.TargetNetworkIdRaw,
            active.TargetUnit,
            active.TargetMonster);
    }

    private static bool IsTrackedTargetStillLive(
        Transform targetTransform,
        NetworkObject targetObject,
        uint targetNetworkIdRaw,
        Unit targetUnit,
        Monster targetMonster)
    {
        if (targetTransform == null)
        {
            return false;
        }

        GameObject targetGameObject = targetTransform.gameObject;
        if (targetGameObject == null || !targetGameObject.activeInHierarchy)
        {
            return false;
        }

        if (targetObject != null)
        {
            if (!targetObject.IsValid)
            {
                return false;
            }

            if (targetNetworkIdRaw != 0 && targetObject.Id.Raw != targetNetworkIdRaw)
            {
                return false;
            }
        }

        if (targetUnit != null)
        {
            if (targetUnit.IsDead)
            {
                return false;
            }

            NetworkObject unitObject = targetUnit.Object;
            if (unitObject != null && unitObject.IsValid && targetUnit.NetworkedIsDead)
            {
                return false;
            }
        }

        if (targetMonster != null && targetMonster.currentHP <= 0f)
        {
            return false;
        }

        return true;
    }

    private static void FreezeTrackedTarget(ActiveProjectile active)
    {
        active.TargetObject = null;
        active.TargetNetworkIdRaw = 0;
        active.TargetTransform = null;
        active.TargetUnit = null;
        active.TargetMonster = null;
    }

    private static Vector3 ResolveTravelDirection(Vector3 from, Vector3 to, Component attacker)
    {
        Vector3 direction = to - from;
        if (direction.sqrMagnitude > 1e-6f)
        {
            return direction.normalized;
        }

        return attacker != null ? attacker.transform.forward : Vector3.forward;
    }

    private static float GetRenderTime(NetworkRunner runner)
    {
        return runner != null ? (float)runner.LocalRenderTime : Time.time;
    }

    private async UniTaskVoid SpawnMuzzleFlashAsync(ProjectileVfxConfig config, Vector3 firePos, Vector3 direction)
    {
        if (config == null || !config.HasMuzzleFlashKey)
        {
            return;
        }

        Quaternion rotation = ProjectileVfxRuntimeUtility.ResolveVfxRotation(direction, true, config.muzzleRotationOffsetEuler);
        Vector3 position = ProjectileVfxRuntimeUtility.ApplyLocalOffset(firePos, rotation, config.muzzleLocalPositionOffset);
        await SpawnOneShotVfxAsync(
            config.muzzleFlashKey,
            position,
            rotation,
            config.ResolveMuzzleScaleMultiplierVector(),
            config.ResolveMuzzlePlaybackSpeed(),
            config.ResolveMuzzleLifetimeSeconds());
    }

    private async UniTaskVoid SpawnImpactFlashAsync(ProjectileVfxConfig config, Vector3 targetPos, Vector3 direction)
    {
        if (config == null || !config.HasImpactFlashKey)
        {
            return;
        }

        Quaternion rotation = ProjectileVfxRuntimeUtility.ResolveVfxRotation(direction, config.alignImpactToDirection, config.impactRotationOffsetEuler);
        Vector3 position = ProjectileVfxRuntimeUtility.ApplyLocalOffset(targetPos, rotation, config.impactLocalPositionOffset);
        await SpawnOneShotVfxAsync(
            config.impactFlashKey,
            position,
            rotation,
            config.ResolveImpactScaleMultiplierVector(),
            config.ResolveImpactPlaybackSpeed(),
            config.ResolveImpactLifetimeSeconds());
    }

    private async UniTask SpawnOneShotVfxAsync(string key, Vector3 position, Quaternion rotation, Vector3 scaleMultiplier, float playbackSpeed, float lifetimeSeconds)
    {
        GameObject prefab = await AssetLoader.LoadAssetAsync<GameObject>(key);
        if (prefab == null)
        {
            Debug.LogWarning($"[ProjectileVfxManager] One-shot VFX load failed: {key}");
            return;
        }

        if (this == null || !isActiveAndEnabled)
        {
            return;
        }

        GameObject instance = pool != null
            ? pool.Spawn(prefab, position, rotation, vfxRoot)
            : Instantiate(prefab, position, rotation, vfxRoot);

        if (instance == null)
        {
            Debug.LogWarning($"[ProjectileVfxManager] One-shot VFX spawn failed: {key}");
            return;
        }

        instance.transform.localScale = ProjectileVfxRuntimeUtility.MultiplyScale(prefab.transform.localScale, scaleMultiplier);
        ProjectileVfxRuntimeUtility.RestartParticles(instance, playbackSpeed);
        EnsureAutoDestroy(instance, Mathf.Max(0.01f, lifetimeSeconds));
    }

    private static void EnsureAutoDestroy(GameObject instance, float lifetimeSeconds)
    {
        if (instance == null)
        {
            return;
        }

        if (!instance.TryGetComponent<VFXAutoDestroy>(out var autoDestroy))
        {
            autoDestroy = instance.AddComponent<VFXAutoDestroy>();
        }

        autoDestroy.Initialize(lifetimeSeconds);
    }

    private static void CancelAutoDestroy(GameObject instance)
    {
        if (instance == null)
        {
            return;
        }

        if (instance.TryGetComponent<VFXAutoDestroy>(out var autoDestroy))
        {
            autoDestroy.Cancel();
        }
    }

    private void UpdateActiveProjectiles()
    {
        if (_activeProjectiles.Count == 0)
        {
            return;
        }

        var runner = _scheduler.Runner;
        if (runner == null)
        {
            return;
        }

        float nowTime = GetRenderTime(runner);

        for (int i = _activeProjectiles.Count - 1; i >= 0; i--)
        {
            var active = _activeProjectiles[i];
            if (active.Instance == null)
            {
                RemoveActive(active);
                continue;
            }

            float hitTime = active.HitTick * runner.DeltaTime;
            if (nowTime >= hitTime)
            {
                SpawnImpactFlashAsync(active.Config, active.LastKnownTargetPos, active.LastKnownDirection).Forget();
                DespawnProjectile(active);
                RemoveActive(active);
                continue;
            }

            TryRefreshTrackedTargetPosition(active, out Vector3 targetOrigin);

            Vector3 targetDirection = targetOrigin - active.Instance.transform.position;
            if (targetDirection.sqrMagnitude > 1e-6f)
            {
                active.LastKnownDirection = targetDirection.normalized;
            }

            ProjectileVfxConfig config = active.Config ?? ProjectileVfxConfig.CreateDefault();
            Quaternion rotation = ProjectileVfxRuntimeUtility.ResolveVfxRotation(active.LastKnownDirection, config.alignProjectileToDirection && alignToDirection, config.projectileRotationOffsetEuler);
            Vector3 targetPos = ProjectileVfxRuntimeUtility.ApplyLocalOffset(targetOrigin, rotation, config.projectileLocalPositionOffset);
            Vector3 direction = targetPos - active.Instance.transform.position;
            float remainingSeconds = Mathf.Max(minRemainingSeconds, hitTime - nowTime);
            float distance = direction.magnitude;
            if (distance <= 1e-6f)
            {
                continue;
            }

            float speedNeeded = distance / remainingSeconds;
            if (maxSpeed > 0f)
            {
                speedNeeded = Mathf.Min(speedNeeded, maxSpeed);
            }

            float dt = Mathf.Min(Time.deltaTime, remainingSeconds);
            Vector3 step = direction.normalized * speedNeeded * dt;
            if (step.magnitude >= distance)
            {
                active.Instance.transform.position = targetPos;
            }
            else
            {
                active.Instance.transform.position += step;
            }

            if (alignToDirection && config.alignProjectileToDirection)
            {
                active.Instance.transform.rotation = ProjectileVfxRuntimeUtility.ResolveVfxRotation(direction.normalized, true, config.projectileRotationOffsetEuler);
            }
        }
    }

    private void DespawnProjectile(ActiveProjectile active)
    {
        if (active.Instance == null)
        {
            return;
        }

        if (pool != null)
        {
            pool.Despawn(active.Instance);
        }
        else
        {
            Destroy(active.Instance);
        }
    }

    private void RemoveActive(ActiveProjectile active)
    {
        _activeBySeq.Remove(active.Sequence);
        _activeProjectiles.Remove(active);
    }
}
