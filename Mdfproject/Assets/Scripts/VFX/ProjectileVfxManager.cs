using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Fusion;
using MDF.Runtime.Assets;
using UnityEngine;

public class ProjectileVfxManager : MonoBehaviour
{
    public static int ActiveProjectileCountForDiagnostics => Instance != null ? Instance._activeProjectiles.Count : 0;
    public static ProjectileVfxManager Instance { get; private set; }

    [SerializeField] private VfxPoolManager pool;
    [SerializeField] private Transform vfxRoot;
    [SerializeField] private float minRemainingSeconds = 0.02f;
    [SerializeField] private float maxSpeed = 200f;
    [SerializeField] private bool alignToDirection = true;

    [Header("Network Compensation")]
    [Tooltip("Limits spawn progress for delayed network events. 0 always spawns at fire position, 1 allows exact catch-up position.")]
    [SerializeField, Range(0f, 1f)] private float maxSpawnProgress = 0.3f;
    [SerializeField] private int catchUpBufferCapacity = 128;

    private int _nextPresentationSequence;
    private int _lifecycleGeneration;
    private readonly Dictionary<int, ActiveProjectile> _activeBySeq = new Dictionary<int, ActiveProjectile>();
    private readonly List<ActiveProjectile> _activeProjectiles = new List<ActiveProjectile>();
    private readonly List<SkippedProjectileEvent> _skippedProjectileEvents = new List<SkippedProjectileEvent>();
    private readonly Dictionary<string, AddressableAssetLease<GameObject>> _unpooledPrefabLeases =
        new Dictionary<string, AddressableAssetLease<GameObject>>(System.StringComparer.Ordinal);
    private readonly Dictionary<string, UniTaskCompletionSource<GameObject>> _unpooledPrefabLoads =
        new Dictionary<string, UniTaskCompletionSource<GameObject>>(System.StringComparer.Ordinal);

    private class ActiveProjectile
    {
        public int Sequence;
        public int FireTick;
        public int HitTick;
        public NetworkRunner Runner;
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

    private struct SkippedProjectileEvent
    {
        public int FieldOwnerPlayerId;
        public int FireTick;
        public int HitTick;
        public NetworkRunner Runner;
        public NetworkId AttackerId;
        public NetworkId TargetId;
        public Vector3 FirePosition;
        public Vector3 TargetPosition;
    }

    private void Awake()
    {
        if (Instance == null || Instance == this)
        {
            Instance = this;
        }

        if (pool == null)
        {
            pool = VfxPoolManager.Instance;
        }
    }

    private void OnDestroy()
    {
        AdvanceLifecycleGeneration();
        foreach (AddressableAssetLease<GameObject> lease in _unpooledPrefabLeases.Values)
        {
            lease.Dispose();
        }

        _unpooledPrefabLeases.Clear();
        _unpooledPrefabLoads.Clear();

        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void OnEnable()
    {
        AdvanceLifecycleGeneration();
        CameraManager.OnCurrentViewingFieldChanged += HandleViewingFieldChanged;
    }

    private void OnDisable()
    {
        AdvanceLifecycleGeneration();
        CameraManager.OnCurrentViewingFieldChanged -= HandleViewingFieldChanged;
        ClearActiveProjectiles();
        _skippedProjectileEvents.Clear();
    }

    private void AdvanceLifecycleGeneration()
    {
        unchecked
        {
            _lifecycleGeneration++;
            if (_lifecycleGeneration == 0)
            {
                _lifecycleGeneration = 1;
            }
        }
    }

    private void ClearActiveProjectiles()
    {
        for (int i = _activeProjectiles.Count - 1; i >= 0; i--)
        {
            DespawnProjectile(_activeProjectiles[i]);
        }

        _activeProjectiles.Clear();
        _activeBySeq.Clear();
    }

    private void Update()
    {
        UpdateActiveProjectiles();
        PruneExpiredCatchUpEvents();
    }

    public static void PlayFromCombatEvent(NetworkRunner runner, NetworkObject attacker, NetworkObject target, int fireTick, int hitTick)
    {
        ProjectileVfxManager manager = Instance != null
            ? Instance
            : UnityEngine.Object.FindObjectOfType<ProjectileVfxManager>();
        if (manager == null || !manager.isActiveAndEnabled || runner == null || !runner.IsRunning)
        {
            return;
        }

        if (attacker == null || target == null)
        {
            return;
        }

        manager.HandleProjectileEvent(new CombatScheduler.ProjectileEventData
        {
            Sequence = manager.AllocatePresentationSequence(),
            FireTick = fireTick,
            HitTick = hitTick,
            Runner = runner,
            Attacker = attacker,
            Target = target
        });
    }

    public static bool PlayFromKingAttack(
        NetworkRunner runner,
        PlayerManager kingOwner,
        UnitData baseUnitData,
        Vector3 firePosition,
        Vector3 targetPosition,
        NetworkObject target,
        int fireTick,
        int hitTick)
    {
        ProjectileVfxManager manager = Instance != null
            ? Instance
            : UnityEngine.Object.FindObjectOfType<ProjectileVfxManager>();
        if (manager == null || !manager.isActiveAndEnabled || runner == null || !runner.IsRunning ||
            kingOwner == null || baseUnitData == null)
        {
            return false;
        }

        ProjectileVfxConfig config = baseUnitData.GetProjectileVfxConfig();
        if (config == null || !config.HasProjectileKey)
        {
            return false;
        }

        NetworkObject attacker = kingOwner.Object;
        if (attacker == null || !attacker.IsValid ||
            !LocalVfxVisibility.ShouldPlay(attacker, null, LocalVfxVisibilityEventKind.Projectile))
        {
            return false;
        }

        if (target != null && (!target.IsValid || target.Runner != runner))
        {
            target = null;
        }

        if (fireTick <= 0)
        {
            fireTick = runner.Tick;
        }
        if (hitTick <= fireTick)
        {
            float deltaTime = Mathf.Max(0.0001f, runner.DeltaTime);
            float projectileSpeed = Mathf.Max(0.01f, config.ResolveProjectileSpeed());
            float travelSeconds = Vector3.Distance(firePosition, targetPosition) / projectileSpeed;
            int travelTicks = Mathf.Max(1, Mathf.CeilToInt(travelSeconds / deltaTime));
            hitTick = fireTick + travelTicks;
        }

        manager.HandleProjectileEvent(new CombatScheduler.ProjectileEventData
        {
            Sequence = manager.AllocatePresentationSequence(),
            FireTick = fireTick,
            HitTick = hitTick,
            Runner = runner,
            Attacker = attacker,
            Target = target,
            HasFirePositionOverride = true,
            HasTargetPositionOverride = true,
            FirePositionOverride = firePosition,
            TargetPositionOverride = targetPosition,
            VfxConfigOverride = config
        });
        return true;
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    public static bool MPTestScheduleExpiryBurst(
        NetworkRunner runner,
        NetworkObject attacker,
        ProjectileVfxConfig config,
        Vector3 firePosition,
        Vector3 targetPosition,
        int projectileCount,
        float lifetimeSeconds,
        out int hitTick,
        out string reason)
    {
        hitTick = 0;
        reason = null;
        ProjectileVfxManager manager = Instance != null
            ? Instance
            : UnityEngine.Object.FindObjectOfType<ProjectileVfxManager>();
        if (!MPTestCommandLine.IsEnabled)
        {
            reason = "missing --mpTest";
            return false;
        }

        if (manager == null || !manager.isActiveAndEnabled || runner == null || !runner.IsRunning)
        {
            reason = "projectile manager or runner is unavailable";
            return false;
        }

        if (attacker == null || !attacker.IsValid || config == null || !config.HasProjectileKey)
        {
            reason = "projectile attacker or config is unavailable";
            return false;
        }

        projectileCount = Mathf.Clamp(projectileCount, 1, 256);
        lifetimeSeconds = Mathf.Clamp(lifetimeSeconds, 2f, 30f);
        int fireTick = runner.Tick + 2;
        int lifetimeTicks = Mathf.Max(2, Mathf.CeilToInt(lifetimeSeconds / Mathf.Max(0.0001f, runner.DeltaTime)));
        hitTick = fireTick + lifetimeTicks;

        for (int i = 0; i < projectileCount; i++)
        {
            manager.HandleProjectileEvent(new CombatScheduler.ProjectileEventData
            {
                Sequence = manager.AllocatePresentationSequence(),
                FireTick = fireTick,
                HitTick = hitTick,
                Runner = runner,
                Attacker = attacker,
                HasFirePositionOverride = true,
                HasTargetPositionOverride = true,
                FirePositionOverride = firePosition,
                TargetPositionOverride = targetPosition,
                VfxConfigOverride = config,
                SuppressMuzzleFlash = true
            });
        }

        reason = $"scheduled {projectileCount} projectiles for hit tick {hitTick}";
        return true;
    }
#endif

    public static void RecordSkippedCombatEvent(NetworkRunner runner, NetworkObject attacker, NetworkObject target, int fireTick, int hitTick)
    {
        ProjectileVfxManager manager = Instance != null
            ? Instance
            : UnityEngine.Object.FindObjectOfType<ProjectileVfxManager>();
        if (manager == null || !manager.isActiveAndEnabled)
        {
            return;
        }

        manager.RecordSkippedProjectileEvent(runner, attacker, target, fireTick, hitTick);
    }

    private int AllocatePresentationSequence()
    {
        if (_nextPresentationSequence == int.MaxValue)
        {
            _nextPresentationSequence = 0;
        }

        _nextPresentationSequence++;
        return _nextPresentationSequence;
    }

    private void HandleProjectileEvent(CombatScheduler.ProjectileEventData evt)
    {
        if (_activeBySeq.ContainsKey(evt.Sequence))
        {
            return;
        }

        SpawnProjectileAsync(evt, _lifecycleGeneration).Forget();
    }

    private void RecordSkippedProjectileEvent(NetworkRunner runner, NetworkObject attacker, NetworkObject target, int fireTick, int hitTick)
    {
        if (runner == null || !runner.IsRunning || attacker == null || target == null)
        {
            return;
        }

        float nowTime = GetRenderTime(runner);
        float hitTime = hitTick * runner.DeltaTime;
        if (hitTime <= nowTime + minRemainingSeconds)
        {
            return;
        }

        int fieldOwnerPlayerId = LocalVfxVisibility.ResolveEventFieldOwnerPlayerId(attacker, target);
        if (fieldOwnerPlayerId < 0)
        {
            return;
        }

        for (int i = 0; i < _skippedProjectileEvents.Count; i++)
        {
            SkippedProjectileEvent existing = _skippedProjectileEvents[i];
            if (existing.Runner == runner &&
                existing.AttackerId.Raw == attacker.Id.Raw &&
                existing.TargetId.Raw == target.Id.Raw &&
                existing.FireTick == fireTick &&
                existing.HitTick == hitTick)
            {
                return;
            }
        }

        var evt = new CombatScheduler.ProjectileEventData
        {
            Runner = runner,
            Attacker = attacker,
            Target = target,
            FireTick = fireTick,
            HitTick = hitTick
        };
        Vector3 firePosition = ResolveFirePosition(evt);

        _skippedProjectileEvents.Add(new SkippedProjectileEvent
        {
            FieldOwnerPlayerId = fieldOwnerPlayerId,
            FireTick = fireTick,
            HitTick = hitTick,
            Runner = runner,
            AttackerId = attacker.Id,
            TargetId = target.Id,
            FirePosition = firePosition,
            TargetPosition = ResolveTargetPosition(evt, firePosition)
        });

        TrimCatchUpBuffer();
    }

    private void HandleViewingFieldChanged(PlayerManager viewingField)
    {
        CatchUpVisibleProjectiles();
    }

    private void CatchUpVisibleProjectiles()
    {
        if (_skippedProjectileEvents.Count == 0)
        {
            return;
        }

        int viewedPlayerId = LocalVfxVisibility.ResolveViewedPlayerId();
        if (viewedPlayerId < 0)
        {
            return;
        }

        for (int i = _skippedProjectileEvents.Count - 1; i >= 0; i--)
        {
            SkippedProjectileEvent skipped = _skippedProjectileEvents[i];
            if (!IsCatchUpEventStillInFlight(skipped))
            {
                _skippedProjectileEvents.RemoveAt(i);
                continue;
            }

            if (skipped.FieldOwnerPlayerId != viewedPlayerId)
            {
                continue;
            }

            if (!TryBuildCatchUpEvent(skipped, out CombatScheduler.ProjectileEventData evt))
            {
                _skippedProjectileEvents.RemoveAt(i);
                continue;
            }

            _skippedProjectileEvents.RemoveAt(i);
            HandleProjectileEvent(evt);
        }
    }

    private bool TryBuildCatchUpEvent(SkippedProjectileEvent skipped, out CombatScheduler.ProjectileEventData evt)
    {
        evt = default;
        NetworkRunner runner = skipped.Runner;
        if (runner == null || !runner.IsRunning)
        {
            return false;
        }

        if (!runner.TryFindObject(skipped.AttackerId, out NetworkObject attacker) || attacker == null)
        {
            return false;
        }

        runner.TryFindObject(skipped.TargetId, out NetworkObject target);
        evt = new CombatScheduler.ProjectileEventData
        {
            Sequence = AllocatePresentationSequence(),
            FireTick = skipped.FireTick,
            HitTick = skipped.HitTick,
            Runner = runner,
            Attacker = attacker,
            Target = target,
            HasFirePositionOverride = true,
            HasTargetPositionOverride = true,
            SuppressMuzzleFlash = true,
            AllowFullCatchUp = true,
            FirePositionOverride = skipped.FirePosition,
            TargetPositionOverride = skipped.TargetPosition
        };
        return true;
    }

    private bool IsCatchUpEventStillInFlight(SkippedProjectileEvent skipped)
    {
        NetworkRunner runner = skipped.Runner;
        if (runner == null || !runner.IsRunning)
        {
            return false;
        }

        float nowTime = GetRenderTime(runner);
        float hitTime = skipped.HitTick * runner.DeltaTime;
        return hitTime > nowTime + minRemainingSeconds;
    }

    private void PruneExpiredCatchUpEvents()
    {
        if (_skippedProjectileEvents.Count == 0)
        {
            return;
        }

        for (int i = _skippedProjectileEvents.Count - 1; i >= 0; i--)
        {
            if (!IsCatchUpEventStillInFlight(_skippedProjectileEvents[i]))
            {
                _skippedProjectileEvents.RemoveAt(i);
            }
        }
    }

    private void TrimCatchUpBuffer()
    {
        int capacity = Mathf.Max(0, catchUpBufferCapacity);
        if (capacity == 0)
        {
            _skippedProjectileEvents.Clear();
            return;
        }

        while (_skippedProjectileEvents.Count > capacity)
        {
            _skippedProjectileEvents.RemoveAt(0);
        }
    }

    private async UniTaskVoid SpawnProjectileAsync(
        CombatScheduler.ProjectileEventData evt,
        int lifecycleGeneration)
    {
        var runner = evt.Runner;
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
        Vector3 projectileFirePos = ApplyProjectileVisualHeight(firePos, config);
        Vector3 projectileTargetPos = ApplyProjectileVisualHeight(targetPos, config);
        Vector3 travelDirection = ResolveTravelDirection(projectileFirePos, projectileTargetPos, evt.Attacker);
        float nowTime = GetRenderTime(runner);
        float fireTime = evt.FireTick * runner.DeltaTime;
        float hitTime = evt.HitTick * runner.DeltaTime;

        if (hitTime <= nowTime)
        {
            LogDiagnosticWarning($"[ProjectileVfxManager] SpawnProjectile SKIPPED: hitTime({hitTime:F3}) <= nowTime({nowTime:F3}) (seq={evt.Sequence})");
            SpawnImpactFlashAsync(config, targetPos, travelDirection, lifecycleGeneration).Forget();
            return;
        }

        if (!await WaitForFireTickAsync(runner, evt.FireTick, lifecycleGeneration))
        {
            return;
        }

        nowTime = GetRenderTime(runner);
        firePos = ResolveFirePosition(evt);
        targetPos = ResolveTargetPositionIfTrackable(evt.Target, targetPos);
        projectileFirePos = ApplyProjectileVisualHeight(firePos, config);
        projectileTargetPos = ApplyProjectileVisualHeight(targetPos, config);
        travelDirection = ResolveTravelDirection(projectileFirePos, projectileTargetPos, evt.Attacker);
        if (hitTime <= nowTime)
        {
            SpawnImpactFlashAsync(config, targetPos, travelDirection, lifecycleGeneration).Forget();
            return;
        }

        if (!evt.SuppressMuzzleFlash)
        {
            SpawnMuzzleFlashAsync(config, firePos, travelDirection, lifecycleGeneration).Forget();
        }

        string projectileKey = config.projectileKey;

        GameObject prefab = await LoadVfxPrefabAsync(projectileKey);
        if (prefab == null)
        {
            Debug.LogWarning($"[ProjectileVfxManager] SpawnProjectile FAILED: Prefab load returned null for key '{projectileKey}' (seq={evt.Sequence})");
            return;
        }

        if (!IsLifecycleCurrent(lifecycleGeneration)
            || runner == null
            || !runner.IsRunning)
        {
            return;
        }

        nowTime = GetRenderTime(runner);
        hitTime = evt.HitTick * runner.DeltaTime;
        firePos = ResolveFirePosition(evt);
        targetPos = ResolveTargetPositionIfTrackable(evt.Target, targetPos);
        projectileFirePos = ApplyProjectileVisualHeight(firePos, config);
        projectileTargetPos = ApplyProjectileVisualHeight(targetPos, config);
        travelDirection = ResolveTravelDirection(projectileFirePos, projectileTargetPos, evt.Attacker);

        if (hitTime <= nowTime)
        {
            LogDiagnosticWarning($"[ProjectileVfxManager] SpawnProjectile SKIPPED after load: hitTime({hitTime:F3}) <= nowTime({nowTime:F3}) (seq={evt.Sequence})");
            SpawnImpactFlashAsync(config, targetPos, travelDirection, lifecycleGeneration).Forget();
            return;
        }

        Vector3 pathPos = CalculateSpawnPosition(projectileFirePos, projectileTargetPos, fireTime, hitTime, nowTime, evt.AllowFullCatchUp);
        Quaternion projectileRotation = ProjectileVfxRuntimeUtility.ResolveVfxRotation(travelDirection, config.alignProjectileToDirection && alignToDirection, config.projectileRotationOffsetEuler);
        Vector3 spawnPos = ProjectileVfxRuntimeUtility.ApplyLocalOffset(pathPos, projectileRotation, config.projectileLocalPositionOffset);

        GameObject instance = pool != null
            ? pool.Spawn(prefab, spawnPos, projectileRotation, vfxRoot)
            : Instantiate(prefab, spawnPos, projectileRotation, vfxRoot);

        if (instance == null)
        {
            Debug.LogWarning($"[ProjectileVfxManager] SpawnProjectile FAILED: Instance is null after spawn (seq={evt.Sequence})");
            return;
        }

        instance.transform.localScale = ProjectileVfxRuntimeUtility.MultiplyScale(prefab.transform.localScale, config.ResolveProjectileScaleMultiplierVector());
        float dynamicLightIntensity = config.ResolveProjectileDynamicLightIntensity();
        float dynamicLightRange = config.ResolveProjectileDynamicLightRange();
        ProjectileVfxRuntimeUtility.PrepareVisualProjectile(instance, dynamicLightIntensity, dynamicLightRange);
        ProjectileVfxRuntimeUtility.RestartParticles(instance, config.ResolveProjectilePlaybackSpeed(), dynamicLightIntensity, dynamicLightRange);
        CancelAutoDestroy(instance);

        var active = new ActiveProjectile
        {
            Sequence = evt.Sequence,
            FireTick = evt.FireTick,
            HitTick = evt.HitTick,
            Runner = runner,
            Instance = instance,
            LastKnownTargetPos = targetPos,
            LastKnownDirection = travelDirection,
            Config = config
        };
        BindTarget(active, evt.Target);

        _activeBySeq[evt.Sequence] = active;
        _activeProjectiles.Add(active);
    }

    private async UniTask<bool> WaitForFireTickAsync(
        NetworkRunner runner,
        int fireTick,
        int lifecycleGeneration)
    {
        if (runner == null || fireTick <= 0)
        {
            return IsLifecycleCurrent(lifecycleGeneration);
        }

        float fireTime = fireTick * runner.DeltaTime;
        while (IsLifecycleCurrent(lifecycleGeneration)
               && runner != null
               && runner.IsRunning
               && GetRenderTime(runner) < fireTime)
        {
            await UniTask.Yield(PlayerLoopTiming.Update);
        }

        return IsLifecycleCurrent(lifecycleGeneration)
               && runner != null
               && runner.IsRunning;
    }

    private bool IsLifecycleCurrent(int lifecycleGeneration)
    {
        return this != null
               && isActiveAndEnabled
               && lifecycleGeneration == _lifecycleGeneration;
    }

    private Vector3 CalculateSpawnPosition(Vector3 firePos, Vector3 targetPos, float fireTime, float hitTime, float nowTime, bool allowFullCatchUp)
    {
        float totalTime = Mathf.Max(0.0001f, hitTime - fireTime);
        float rawProgress = Mathf.Clamp01((nowTime - fireTime) / totalTime);
        float progress = allowFullCatchUp ? rawProgress : Mathf.Min(rawProgress, maxSpawnProgress);
        return Vector3.Lerp(firePos, targetPos, progress);
    }

    private bool TryResolveProjectileVfx(CombatScheduler.ProjectileEventData evt, out ProjectileVfxConfig config)
    {
        config = evt.VfxConfigOverride;
        if (config != null && config.HasProjectileKey)
        {
            return true;
        }

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
        Debug.LogWarning($"[ProjectileVfxManager] Monster '{monsterName}' Data is unavailable; skipping projectile VFX resolution.");
        return false;
    }

    // Fire position is resolved from the attacker to keep network payload small.
    private Vector3 ResolveFirePosition(CombatScheduler.ProjectileEventData evt)
    {
        if (evt.HasFirePositionOverride)
        {
            return evt.FirePositionOverride;
        }

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
        if (evt.HasTargetPositionOverride)
        {
            return evt.TargetPositionOverride;
        }

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

    private static Vector3 ApplyProjectileVisualHeight(Vector3 position, ProjectileVfxConfig config)
    {
        float visualHeightOffset = config != null ? config.ResolveProjectileVisualHeightOffset() : 0f;
        return position + Vector3.up * visualHeightOffset;
    }

    private static float GetRenderTime(NetworkRunner runner)
    {
        return runner != null ? (float)runner.LocalRenderTime : Time.time;
    }

    private async UniTaskVoid SpawnMuzzleFlashAsync(
        ProjectileVfxConfig config,
        Vector3 firePos,
        Vector3 direction,
        int lifecycleGeneration)
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
            config.ResolveMuzzleLifetimeSeconds(),
            lifecycleGeneration);
    }

    private async UniTaskVoid SpawnImpactFlashAsync(
        ProjectileVfxConfig config,
        Vector3 targetPos,
        Vector3 direction,
        int lifecycleGeneration)
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
            config.ResolveImpactLifetimeSeconds(),
            lifecycleGeneration);
    }

    private async UniTask SpawnOneShotVfxAsync(
        string key,
        Vector3 position,
        Quaternion rotation,
        Vector3 scaleMultiplier,
        float playbackSpeed,
        float lifetimeSeconds,
        int lifecycleGeneration)
    {
        GameObject prefab = await LoadVfxPrefabAsync(key);
        if (prefab == null)
        {
            Debug.LogWarning($"[ProjectileVfxManager] One-shot VFX load failed: {key}");
            return;
        }

        if (!IsLifecycleCurrent(lifecycleGeneration))
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

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        int activeCountAtStart = _activeProjectiles.Count;
        long performanceStart = MPTestPerformanceRecorder.StartTimestamp();
#endif
        for (int i = _activeProjectiles.Count - 1; i >= 0; i--)
        {
            var active = _activeProjectiles[i];
            var runner = active.Runner;
            if (runner == null || !runner.IsRunning)
            {
                DespawnProjectile(active);
                RemoveActiveAt(i, active);
                continue;
            }

            float nowTime = GetRenderTime(runner);
            if (active.Instance == null)
            {
                RemoveActiveAt(i, active);
                continue;
            }

            float hitTime = active.HitTick * runner.DeltaTime;
            if (nowTime >= hitTime)
            {
                SpawnImpactFlashAsync(
                    active.Config,
                    active.LastKnownTargetPos,
                    active.LastKnownDirection,
                    _lifecycleGeneration).Forget();
                DespawnProjectile(active);
                RemoveActiveAt(i, active);
                continue;
            }

            TryRefreshTrackedTargetPosition(active, out Vector3 targetOrigin);

            ProjectileVfxConfig config = active.Config ?? ProjectileVfxConfig.CreateDefault();
            Vector3 visualTargetOrigin = ApplyProjectileVisualHeight(targetOrigin, config);
            Vector3 targetDirection = visualTargetOrigin - active.Instance.transform.position;
            if (targetDirection.sqrMagnitude > 1e-6f)
            {
                active.LastKnownDirection = targetDirection.normalized;
            }

            Quaternion rotation = ProjectileVfxRuntimeUtility.ResolveVfxRotation(active.LastKnownDirection, config.alignProjectileToDirection && alignToDirection, config.projectileRotationOffsetEuler);
            Vector3 targetPos = ProjectileVfxRuntimeUtility.ApplyLocalOffset(visualTargetOrigin, rotation, config.projectileLocalPositionOffset);
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
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        MPTestPerformanceRecorder.RecordDuration("projectile_vfx_update", performanceStart, activeCountAtStart);
#endif
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

    private void RemoveActiveAt(int index, ActiveProjectile active)
    {
        int lastIndex = _activeProjectiles.Count - 1;
        if (active == null || index < 0 || index > lastIndex)
        {
            return;
        }

        _activeBySeq.Remove(active.Sequence);
        if (index != lastIndex)
        {
            // Presentation order is irrelevant. Swap the already-processed tail entry into the
            // removed slot so expiry stays O(1), including sparse simultaneous expirations.
            _activeProjectiles[index] = _activeProjectiles[lastIndex];
        }

        _activeProjectiles.RemoveAt(lastIndex);
    }

    private async UniTask<GameObject> LoadVfxPrefabAsync(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        if (pool == null)
        {
            pool = VfxPoolManager.Instance;
        }

        if (pool != null)
        {
            return await pool.LoadAddressablePrefabAsync(key);
        }

        string normalizedKey = key.Trim();
        if (_unpooledPrefabLeases.TryGetValue(normalizedKey, out AddressableAssetLease<GameObject> lease))
        {
            if (lease.Asset != null)
            {
                return lease.Asset;
            }

            lease.Dispose();
            _unpooledPrefabLeases.Remove(normalizedKey);
            _unpooledPrefabLoads.Remove(normalizedKey);
        }

        if (!_unpooledPrefabLoads.TryGetValue(normalizedKey, out UniTaskCompletionSource<GameObject> pendingLoad))
        {
            pendingLoad = new UniTaskCompletionSource<GameObject>();
            _unpooledPrefabLoads.Add(normalizedKey, pendingLoad);
            PublishUnpooledPrefabLoadAsync(normalizedKey, pendingLoad).Forget();
        }

        return await pendingLoad.Task;
    }

    private async UniTaskVoid PublishUnpooledPrefabLoadAsync(
        string key,
        UniTaskCompletionSource<GameObject> completion)
    {
        try
        {
            GameObject prefab = await LoadAndRetainUnpooledPrefabAsync(key);
            completion.TrySetResult(prefab);
        }
        catch (System.Exception exception)
        {
            completion.TrySetException(exception);
        }
        finally
        {
            if (this != null &&
                _unpooledPrefabLoads.TryGetValue(key, out UniTaskCompletionSource<GameObject> current) &&
                ReferenceEquals(current, completion))
            {
                _unpooledPrefabLoads.Remove(key);
            }
        }
    }

    private async UniTask<GameObject> LoadAndRetainUnpooledPrefabAsync(string key)
    {
        AddressableAssetLease<GameObject> lease = await AssetLoader.AcquireAssetAsync<GameObject>(key);
        if (lease == null)
        {
            return null;
        }

        if (this == null)
        {
            lease.Dispose();
            return null;
        }

        if (_unpooledPrefabLeases.TryGetValue(key, out AddressableAssetLease<GameObject> existing))
        {
            lease.Dispose();
            return existing.Asset;
        }

        _unpooledPrefabLeases.Add(key, lease);
        return lease.Asset;
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    private static void LogDiagnostic(string message)
    {
        Debug.Log(message);
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    private static void LogDiagnosticWarning(string message)
    {
        Debug.LogWarning(message);
    }
}
