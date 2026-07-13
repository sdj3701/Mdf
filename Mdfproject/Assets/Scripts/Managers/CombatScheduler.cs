using System.Collections.Generic;
using Fusion;
using UnityEngine;

public partial class CombatScheduler : NetworkBehaviour
{
    public static CombatScheduler Instance { get; private set; }

    [Header("Hit Scheduling")]
    [SerializeField] private int hitBufferSize = 256;
    [SerializeField] private float defaultProjectileSpeed = 20f;

    private const int PendingFireCapacity = 64;
    private const int PendingHitCapacity = 96;
    private const int ProjectileEventBufferCapacity = 0;
    private const int FixedPointScale = 1000;

    [Networked] private int PendingFireSequence { get; set; }
    [Networked, Capacity(PendingFireCapacity)] private NetworkArray<PendingFireSnapshot> PendingFireSnapshots { get; }
    [Networked] private int PendingHitSequence { get; set; }
    [Networked, Capacity(PendingHitCapacity)] private NetworkArray<PendingHitSnapshot> PendingHitSnapshots { get; }

    private List<PendingFire>[] _fireBuckets;
    private List<PendingHit>[] _hitBuckets;
    private List<PendingBasicAttackVfx>[] _basicAttackVfxBuckets;
    private readonly HashSet<int> _localPendingFireSequences = new HashSet<int>();
    private readonly HashSet<int> _localPendingHitSequences = new HashSet<int>();

    private struct PendingFire
    {
        public NetworkObject Attacker;
        public NetworkObject Target;
        public Vector3 FirePosition;
        public float Damage;
        public DamageType DamageType;
        public bool IsRanged;
        public bool EmitVfx;
        public float ProjectileSpeedOverride;
        public int FireTick;
        public float SplashRadius;
        public LayerMask EnemyLayerMask;
        public int SnapshotSequence;
    }

    private struct PendingHit
    {
        public NetworkId TargetId;
        public Vector3 ImpactPosition;
        public float Damage;
        public DamageType DamageType;
        public int HitTick;
        public float SplashRadius;
        public LayerMask EnemyLayerMask;
        public int SnapshotSequence;
    }

    private struct PendingBasicAttackVfx
    {
        public NetworkObject Attacker;
        public NetworkObject Target;
        public int Tick;
    }

    private struct PendingFireSnapshot : INetworkStruct
    {
        public int Sequence;
        public int FireTick;
        public NetworkId AttackerId;
        public NetworkId TargetId;
        public int FirePositionX;
        public int FirePositionY;
        public int FirePositionZ;
        public int Damage;
        public int PackedMeta;
        public int ProjectileSpeedOverride;
        public int SplashRadius;
        public int EnemyLayerMask;

        public int DamageType => PackedMeta & 0xFF;
        public int IsRanged => (PackedMeta >> 8) & 0x1;
        public int EmitVfx => (PackedMeta >> 9) & 0x1;
    }

    private struct PendingHitSnapshot : INetworkStruct
    {
        public int Sequence;
        public int HitTick;
        public NetworkId TargetId;
        public int ImpactPositionX;
        public int ImpactPositionY;
        public int ImpactPositionZ;
        public int Damage;
        public int DamageType;
        public int SplashRadius;
        public int EnemyLayerMask;
    }

    public struct ProjectileEventData
    {
        public int Sequence;
        public int FireTick;
        public int HitTick;
        public NetworkRunner Runner;
        public NetworkObject Attacker;
        public NetworkObject Target;
        public bool HasFirePositionOverride;
        public bool HasTargetPositionOverride;
        public bool SuppressMuzzleFlash;
        public bool AllowFullCatchUp;
        public Vector3 FirePositionOverride;
        public Vector3 TargetPositionOverride;
        public ProjectileVfxConfig VfxConfigOverride;
    }

    public override void Spawned()
    {
        if (Instance == null || Instance == this || !IsInstanceValidForRunner(Instance, Runner))
        {
            Instance = this;
        }
        else
        {
            return;
        }

        InitializeHitBuckets();
        RebuildPendingBucketsFromNetworkSnapshots();
        RebuildZonePayloadsFromNetworkEntries();
        RebuildStatBuffCachesFromNetworkEntries();
        RebuildStatusCachesFromNetworkEntries();
    }

    private void OnEnable()
    {
        GameEvents.OnGameStateChanged += HandleSchedulerGameStateChanged;
    }

    private void OnDisable()
    {
        GameEvents.OnGameStateChanged -= HandleSchedulerGameStateChanged;
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void HandleSchedulerGameStateChanged(GameManagers.GameState newState)
    {
        if (newState != GameManagers.GameState.Battle1 &&
            newState != GameManagers.GameState.Battle2)
        {
            ClearAllScheduledZones($"stateChanged:{newState}");
        }
    }

    public static void RebindInstanceForMigration(CombatScheduler scheduler, string reason = null)
    {
        if (scheduler == null ||
            scheduler.Runner == null ||
            !scheduler.Runner.IsRunning ||
            scheduler.Object == null ||
            !scheduler.Object.IsValid)
        {
            return;
        }

        bool changed = Instance != scheduler;
        Instance = scheduler;
        scheduler.InitializeHitBuckets();
        scheduler.RebuildPendingBucketsFromNetworkSnapshots();
        scheduler.RebuildZonePayloadsFromNetworkEntries();
        scheduler.RebuildStatBuffCachesFromNetworkEntries();
        scheduler.RebuildStatusCachesFromNetworkEntries();
        if (changed)
        {
            Debug.Log($"[CombatScheduler] Rebound instance for migration. reason={reason}, runner={scheduler.Runner.name}, stateAuth={scheduler.Object.HasStateAuthority}");
        }
    }

    private static bool IsInstanceValidForRunner(CombatScheduler instance, NetworkRunner runner)
    {
        return instance != null &&
               instance.Runner == runner &&
               instance.Runner != null &&
               instance.Runner.IsRunning &&
               instance.Object != null &&
               instance.Object.IsValid;
    }

    public override void FixedUpdateNetwork()
    {
        if (!Object.HasStateAuthority ||
            _fireBuckets == null || _fireBuckets.Length == 0 ||
            _hitBuckets == null || _hitBuckets.Length == 0 ||
            _basicAttackVfxBuckets == null || _basicAttackVfxBuckets.Length == 0)
        {
            return;
        }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (MPTestCommandLine.IsGameFlowFrozen)
        {
            return;
        }
#endif

        ProcessDueStatBuffs();
        ProcessDueStatusEffects();
        ProcessDueZones();
        ProcessDueBasicAttackVfx();
        RebuildPendingBucketsFromNetworkSnapshots();
        ProcessDueFires();
        ProcessDueHits();
        RefreshNetworkBudgetPeaks();
    }

    public void ScheduleHit(NetworkObject attacker, NetworkObject target, Vector3 firePos, float damage,
        DamageType damageType, bool isRanged, bool emitVfx, float projectileSpeedOverride = 0f, float splashRadius = 0f,
        LayerMask enemyLayerMask = default, float fireDelaySeconds = 0f)
    {
        if (!Object.HasStateAuthority || Runner == null || target == null || _fireBuckets == null || _hitBuckets == null)
        {
            return;
        }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (MPTestCommandLine.IsGameFlowFrozen)
        {
            return;
        }
#endif

        int fireDelayTicks = SecondsToTicksCeil(fireDelaySeconds);
        int fireTick = Runner.Tick + fireDelayTicks;
        if (fireDelayTicks > 0)
        {
            EnqueuePendingFire(new PendingFire
            {
                Attacker = attacker,
                Target = target,
                FirePosition = firePos,
                Damage = damage,
                DamageType = damageType,
                IsRanged = isRanged,
                EmitVfx = emitVfx,
                ProjectileSpeedOverride = projectileSpeedOverride,
                FireTick = fireTick,
                SplashRadius = splashRadius,
                EnemyLayerMask = enemyLayerMask
            });
            return;
        }

        ScheduleResolvedHit(attacker, target, firePos, damage, damageType, isRanged, emitVfx,
            projectileSpeedOverride, splashRadius, enemyLayerMask, fireTick);
    }

    /// <summary>
    /// Enqueues one authority-owned direct hit at an already-resolved simulation tick. This is used
    /// by presentation-only attackers such as the King, whose visual fire point is not a component
    /// on the PlayerManager NetworkObject and therefore cannot be reconstructed by ScheduleHit.
    /// </summary>
    public bool TryScheduleDirectHitAtTick(
        NetworkObject target,
        Vector3 capturedImpactPosition,
        float damage,
        DamageType damageType,
        int hitTick)
    {
        if (Object == null
            || !Object.HasStateAuthority
            || Runner == null
            || target == null
            || !target.IsValid
            || target.Runner != Runner
            || damage <= 0f
            || _hitBuckets == null)
        {
            return false;
        }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (MPTestCommandLine.IsGameFlowFrozen)
        {
            return false;
        }
#endif

        var pendingHit = new PendingHit
        {
            TargetId = target.Id,
            ImpactPosition = capturedImpactPosition,
            Damage = damage,
            DamageType = damageType,
            HitTick = Mathf.Max(Runner.Tick + 1, hitTick),
            SplashRadius = 0f,
            EnemyLayerMask = default
        };
        return EnqueuePendingHit(pendingHit);
    }

    public void ScheduleBasicAttackVfx(NetworkObject attacker, NetworkObject target, float delaySeconds = 0f)
    {
        if (!Object.HasStateAuthority || Runner == null || attacker == null || target == null || _basicAttackVfxBuckets == null)
        {
            return;
        }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (MPTestCommandLine.IsGameFlowFrozen)
        {
            return;
        }
#endif

        int delayTicks = SecondsToTicksCeil(delaySeconds);
        int tick = Runner.Tick + delayTicks;
        if (delayTicks > 0)
        {
            EnqueuePendingBasicAttackVfx(new PendingBasicAttackVfx
            {
                Attacker = attacker,
                Target = target,
                Tick = tick
            });
            return;
        }

        if (IsPendingBasicAttackVfxValid(attacker, target))
        {
            PublishBasicAttackVfxEvent(attacker, target);
        }
    }

    private void ProcessDueFires()
    {
        int bucketIndex = Runner.Tick % _fireBuckets.Length;
        var bucket = _fireBuckets[bucketIndex];
        for (int i = bucket.Count - 1; i >= 0; i--)
        {
            var fire = bucket[i];
            if (fire.FireTick > Runner.Tick)
            {
                continue;
            }

            if (IsPendingFireValid(fire))
            {
                int fireTick = Mathf.Max(fire.FireTick, Runner.Tick);
                Vector3 firePosition = ResolveCurrentFirePosition(fire.Attacker, fire.FirePosition);
                ScheduleResolvedHit(
                    fire.Attacker,
                    fire.Target,
                    firePosition,
                    fire.Damage,
                    fire.DamageType,
                    fire.IsRanged,
                    fire.EmitVfx,
                    fire.ProjectileSpeedOverride,
                    fire.SplashRadius,
                    fire.EnemyLayerMask,
                    fireTick);
            }

            ClearPendingFireSnapshot(fire.SnapshotSequence);
            bucket.RemoveAt(i);
        }
    }

    private void ProcessDueBasicAttackVfx()
    {
        int bucketIndex = Runner.Tick % _basicAttackVfxBuckets.Length;
        var bucket = _basicAttackVfxBuckets[bucketIndex];
        for (int i = bucket.Count - 1; i >= 0; i--)
        {
            var pending = bucket[i];
            if (pending.Tick > Runner.Tick)
            {
                continue;
            }

            if (IsPendingBasicAttackVfxValid(pending.Attacker, pending.Target))
            {
                PublishBasicAttackVfxEvent(pending.Attacker, pending.Target);
            }

            bucket.RemoveAt(i);
        }
    }

    private void ProcessDueHits()
    {
        int bucketIndex = Runner.Tick % _hitBuckets.Length;
        var bucket = _hitBuckets[bucketIndex];
        for (int i = bucket.Count - 1; i >= 0; i--)
        {
            var hit = bucket[i];
            if (hit.HitTick > Runner.Tick)
            {
                continue;
            }

            ApplyHit(hit);
            ClearPendingHitSnapshot(hit.SnapshotSequence);
            bucket.RemoveAt(i);
        }
    }

    private void ScheduleResolvedHit(NetworkObject attacker, NetworkObject target, Vector3 firePos, float damage,
        DamageType damageType, bool isRanged, bool emitVfx, float projectileSpeedOverride, float splashRadius,
        LayerMask enemyLayerMask, int fireTick)
    {
        if (Runner == null || target == null || _hitBuckets == null)
        {
            return;
        }

        int hitTick = fireTick;
        Vector3 targetPosition = target.transform.position;

        if (isRanged)
        {
            float projectileSpeed = projectileSpeedOverride > 0f ? projectileSpeedOverride : defaultProjectileSpeed;
            if (projectileSpeed > 0f)
            {
                float distance = Vector3.Distance(firePos, targetPosition);
                float travelTime = distance / projectileSpeed;
                int travelTicks = SecondsToTicksCeil(travelTime);
                hitTick = fireTick + travelTicks;
            }
        }

        if (hitTick <= fireTick)
        {
            hitTick = fireTick + 1;
        }

        var pendingHit = new PendingHit
        {
            TargetId = target.Id,
            ImpactPosition = targetPosition,
            Damage = damage,
            DamageType = damageType,
            HitTick = hitTick,
            SplashRadius = splashRadius,
            EnemyLayerMask = enemyLayerMask
        };

        if (!EnqueuePendingHit(pendingHit))
        {
            return;
        }

        if (emitVfx)
        {
            PublishProjectileVfxEvent(attacker, target, fireTick, hitTick);
        }
    }

    private void ApplyHit(PendingHit hit)
    {
        if (hit.SplashRadius > 0f)
        {
            Collider[] enemiesInRange = Physics.OverlapSphere(hit.ImpactPosition, hit.SplashRadius, hit.EnemyLayerMask);
            var damagedTargets = new HashSet<int>();
            foreach (var enemyCollider in enemiesInRange)
            {
                if (TryResolveUniqueEnemy(enemyCollider, damagedTargets, out var enemy))
                {
                    enemy.TakeDamage(hit.Damage, hit.DamageType);
                }
            }

            return;
        }

        if (Runner.TryFindObject(hit.TargetId, out var targetObj))
        {
            var enemy = targetObj.GetComponent<IEnemy>();
            if (enemy != null)
            {
                enemy.TakeDamage(hit.Damage, hit.DamageType);
            }
        }
    }

    private void InitializeHitBuckets()
    {
        int size = Mathf.Max(1, hitBufferSize);
        _fireBuckets = new List<PendingFire>[size];
        _hitBuckets = new List<PendingHit>[size];
        _basicAttackVfxBuckets = new List<PendingBasicAttackVfx>[size];
        _localPendingFireSequences.Clear();
        _localPendingHitSequences.Clear();
        for (int i = 0; i < size; i++)
        {
            _fireBuckets[i] = new List<PendingFire>();
            _hitBuckets[i] = new List<PendingHit>();
            _basicAttackVfxBuckets[i] = new List<PendingBasicAttackVfx>();
        }
    }

    private bool EnqueuePendingFire(PendingFire fire)
    {
        fire.SnapshotSequence = WritePendingFireSnapshot(fire);
        if (fire.SnapshotSequence <= 0)
        {
            return false;
        }

        AddPendingFireToBucket(fire);
        RefreshNetworkBudgetPeaks();
        return true;
    }

    private void AddPendingFireToBucket(PendingFire fire)
    {
        int bucketIndex = fire.FireTick % _fireBuckets.Length;
        _fireBuckets[bucketIndex].Add(fire);
        if (fire.SnapshotSequence > 0)
        {
            _localPendingFireSequences.Add(fire.SnapshotSequence);
        }
    }

    private bool EnqueuePendingHit(PendingHit hit)
    {
        hit.SnapshotSequence = WritePendingHitSnapshot(hit);
        if (hit.SnapshotSequence <= 0)
        {
            return false;
        }

        AddPendingHitToBucket(hit);
        RefreshNetworkBudgetPeaks();
        return true;
    }

    private void AddPendingHitToBucket(PendingHit hit)
    {
        int bucketIndex = hit.HitTick % _hitBuckets.Length;
        _hitBuckets[bucketIndex].Add(hit);
        if (hit.SnapshotSequence > 0)
        {
            _localPendingHitSequences.Add(hit.SnapshotSequence);
        }
    }

    private void EnqueuePendingBasicAttackVfx(PendingBasicAttackVfx pending)
    {
        int bucketIndex = pending.Tick % _basicAttackVfxBuckets.Length;
        _basicAttackVfxBuckets[bucketIndex].Add(pending);
    }

    private int SecondsToTicksCeil(float seconds)
    {
        if (seconds <= 0f)
        {
            return 0;
        }

        float deltaTime = Runner != null && Runner.DeltaTime > 0f ? Runner.DeltaTime : Time.deltaTime;
        return Mathf.Max(1, Mathf.CeilToInt(seconds / Mathf.Max(0.0001f, deltaTime)));
    }

    private void RebuildPendingBucketsFromNetworkSnapshots()
    {
        if (Runner == null || _fireBuckets == null || _hitBuckets == null)
        {
            return;
        }

        int nowTick = Runner.Tick;
        for (int i = 0; i < PendingFireCapacity; i++)
        {
            PendingFireSnapshot snapshot = PendingFireSnapshots[i];
            if (snapshot.Sequence <= 0 || _localPendingFireSequences.Contains(snapshot.Sequence))
            {
                continue;
            }

            NetworkObject target = ResolveNetworkObject(snapshot.TargetId);
            if (target == null)
            {
                if (Object != null && Object.HasStateAuthority && snapshot.FireTick <= nowTick)
                {
                    ClearPendingFireSnapshot(snapshot.Sequence);
                }

                continue;
            }

            PendingFire fire = ReadPendingFireSnapshot(snapshot, nowTick);
            fire.Attacker = ResolveNetworkObject(snapshot.AttackerId);
            fire.Target = target;
            AddPendingFireToBucket(fire);
        }

        for (int i = 0; i < PendingHitCapacity; i++)
        {
            PendingHitSnapshot snapshot = PendingHitSnapshots[i];
            if (snapshot.Sequence <= 0 || _localPendingHitSequences.Contains(snapshot.Sequence))
            {
                continue;
            }

            AddPendingHitToBucket(ReadPendingHitSnapshot(snapshot, nowTick));
        }
    }

    private static bool IsPendingFireValid(PendingFire fire)
    {
        if (fire.Target == null)
        {
            return false;
        }

        if (!fire.Target.IsValid)
        {
            return false;
        }

        if (fire.Attacker == null)
        {
            return true;
        }

        var unit = fire.Attacker.GetComponent<Unit>();
        if (unit != null)
        {
            if (unit.IsDead)
            {
                return false;
            }

            NetworkObject unitObject = unit.Object;
            if (unitObject != null && unitObject.IsValid && unit.NetworkedIsDead)
            {
                return false;
            }
        }

        var monster = fire.Attacker.GetComponent<Monster>();
        if (monster != null && monster.currentHP <= 0f)
        {
            return false;
        }

        return true;
    }

    private static bool IsPendingBasicAttackVfxValid(NetworkObject attacker, NetworkObject target)
    {
        if (attacker == null || target == null || !attacker.IsValid || !target.IsValid)
        {
            return false;
        }

        var unit = attacker.GetComponent<Unit>();
        var monster = target.GetComponent<Monster>();
        if (unit != null)
        {
            if (unit.IsDead)
            {
                return false;
            }

            NetworkObject unitObject = unit.Object;
            if (unitObject != null && unitObject.IsValid && unit.NetworkedIsDead)
            {
                return false;
            }

            return unit.CanPlayBasicAttackVfxForTarget(monster);
        }

        return false;
    }

    private static Vector3 ResolveCurrentFirePosition(NetworkObject attacker, Vector3 fallback)
    {
        if (attacker == null)
        {
            return fallback;
        }

        var unit = attacker.GetComponent<Unit>();
        if (unit != null)
        {
            return unit.firePoint != null ? unit.firePoint.position : unit.transform.position;
        }

        var monster = attacker.GetComponent<Monster>();
        if (monster != null)
        {
            return monster.firePoint != null ? monster.firePoint.position : monster.transform.position + Vector3.up * 0.5f;
        }

        return attacker.transform.position;
    }

    private int WritePendingFireSnapshot(PendingFire fire)
    {
        int nextSeq = PendingFireSequence + 1;
        int index = FindEmptyPendingFireSnapshotSlot();
        if (index < 0)
        {
            RecordNetworkBudgetDrop(NetworkBudgetDropKind.PendingFire);
            Debug.LogWarning($"[CombatScheduler] Pending fire capacity exceeded. capacity={PendingFireCapacity}, sequence={nextSeq}");
            return 0;
        }

        PendingFireSequence = nextSeq;
        PendingFireSnapshots.Set(index, new PendingFireSnapshot
        {
            Sequence = nextSeq,
            FireTick = fire.FireTick,
            AttackerId = GetNetworkId(fire.Attacker),
            TargetId = GetNetworkId(fire.Target),
            FirePositionX = PackFloat(fire.FirePosition.x),
            FirePositionY = PackFloat(fire.FirePosition.y),
            FirePositionZ = PackFloat(fire.FirePosition.z),
            Damage = PackFloat(fire.Damage),
            PackedMeta = PackPendingFireMeta((int)fire.DamageType, fire.IsRanged, fire.EmitVfx),
            ProjectileSpeedOverride = PackFloat(fire.ProjectileSpeedOverride),
            SplashRadius = PackFloat(fire.SplashRadius),
            EnemyLayerMask = fire.EnemyLayerMask.value
        });

        return nextSeq;
    }

    private int WritePendingHitSnapshot(PendingHit hit)
    {
        int nextSeq = PendingHitSequence + 1;
        int index = FindEmptyPendingHitSnapshotSlot();
        if (index < 0)
        {
            RecordNetworkBudgetDrop(NetworkBudgetDropKind.PendingHit);
            Debug.LogWarning($"[CombatScheduler] Pending hit capacity exceeded. capacity={PendingHitCapacity}, sequence={nextSeq}");
            return 0;
        }

        PendingHitSequence = nextSeq;
        PendingHitSnapshots.Set(index, new PendingHitSnapshot
        {
            Sequence = nextSeq,
            HitTick = hit.HitTick,
            TargetId = hit.TargetId,
            ImpactPositionX = PackFloat(hit.ImpactPosition.x),
            ImpactPositionY = PackFloat(hit.ImpactPosition.y),
            ImpactPositionZ = PackFloat(hit.ImpactPosition.z),
            Damage = PackFloat(hit.Damage),
            DamageType = (int)hit.DamageType,
            SplashRadius = PackFloat(hit.SplashRadius),
            EnemyLayerMask = hit.EnemyLayerMask.value
        });

        return nextSeq;
    }

    private void ClearPendingFireSnapshot(int sequence)
    {
        if (sequence <= 0)
        {
            return;
        }

        int index = FindPendingFireSnapshotSlot(sequence);
        if (index >= 0)
        {
            PendingFireSnapshots.Set(index, default);
        }

        _localPendingFireSequences.Remove(sequence);
    }

    private void ClearPendingHitSnapshot(int sequence)
    {
        if (sequence <= 0)
        {
            return;
        }

        int index = FindPendingHitSnapshotSlot(sequence);
        if (index >= 0)
        {
            PendingHitSnapshots.Set(index, default);
        }

        _localPendingHitSequences.Remove(sequence);
    }

    private int FindEmptyPendingFireSnapshotSlot()
    {
        for (int i = 0; i < PendingFireCapacity; i++)
        {
            if (PendingFireSnapshots[i].Sequence <= 0)
            {
                return i;
            }
        }

        return -1;
    }

    private int FindEmptyPendingHitSnapshotSlot()
    {
        for (int i = 0; i < PendingHitCapacity; i++)
        {
            if (PendingHitSnapshots[i].Sequence <= 0)
            {
                return i;
            }
        }

        return -1;
    }

    private int FindPendingFireSnapshotSlot(int sequence)
    {
        for (int i = 0; i < PendingFireCapacity; i++)
        {
            if (PendingFireSnapshots[i].Sequence == sequence)
            {
                return i;
            }
        }

        return -1;
    }

    private int FindPendingHitSnapshotSlot(int sequence)
    {
        for (int i = 0; i < PendingHitCapacity; i++)
        {
            if (PendingHitSnapshots[i].Sequence == sequence)
            {
                return i;
            }
        }

        return -1;
    }

    private PendingFire ReadPendingFireSnapshot(PendingFireSnapshot snapshot, int nowTick)
    {
        return new PendingFire
        {
            FirePosition = UnpackVector(snapshot.FirePositionX, snapshot.FirePositionY, snapshot.FirePositionZ),
            Damage = UnpackFloat(snapshot.Damage),
            DamageType = (DamageType)snapshot.DamageType,
            IsRanged = snapshot.IsRanged != 0,
            EmitVfx = snapshot.EmitVfx != 0,
            ProjectileSpeedOverride = UnpackFloat(snapshot.ProjectileSpeedOverride),
            FireTick = Mathf.Max(snapshot.FireTick, nowTick),
            SplashRadius = UnpackFloat(snapshot.SplashRadius),
            EnemyLayerMask = new LayerMask { value = snapshot.EnemyLayerMask },
            SnapshotSequence = snapshot.Sequence
        };
    }

    private PendingHit ReadPendingHitSnapshot(PendingHitSnapshot snapshot, int nowTick)
    {
        return new PendingHit
        {
            TargetId = snapshot.TargetId,
            ImpactPosition = UnpackVector(snapshot.ImpactPositionX, snapshot.ImpactPositionY, snapshot.ImpactPositionZ),
            Damage = UnpackFloat(snapshot.Damage),
            DamageType = (DamageType)snapshot.DamageType,
            HitTick = Mathf.Max(snapshot.HitTick, nowTick),
            SplashRadius = UnpackFloat(snapshot.SplashRadius),
            EnemyLayerMask = new LayerMask { value = snapshot.EnemyLayerMask },
            SnapshotSequence = snapshot.Sequence
        };
    }

    private NetworkObject ResolveNetworkObject(NetworkId id)
    {
        if (Runner == null || id.Raw == 0)
        {
            return null;
        }

        return Runner.TryFindObject(id, out var networkObject) ? networkObject : null;
    }

    private static NetworkId GetNetworkId(NetworkObject networkObject)
    {
        return networkObject != null && networkObject.IsValid ? networkObject.Id : default;
    }

    private static int PackFloat(float value)
    {
        return Mathf.RoundToInt(value * FixedPointScale);
    }

    private static float UnpackFloat(int value)
    {
        return value / (float)FixedPointScale;
    }

    private static Vector3 UnpackVector(int x, int y, int z)
    {
        return new Vector3(UnpackFloat(x), UnpackFloat(y), UnpackFloat(z));
    }

    private static int PackPendingFireMeta(int damageType, bool isRanged, bool emitVfx)
    {
        int packedDamageType = Mathf.Clamp(damageType, 0, 0xFF);
        int ranged = isRanged ? 1 : 0;
        int vfx = emitVfx ? 1 : 0;
        return packedDamageType | (ranged << 8) | (vfx << 9);
    }

    private void PublishProjectileVfxEvent(NetworkObject attacker, NetworkObject target, int fireTick, int hitTick)
    {
        if (attacker == null || target == null || !attacker.IsValid || !target.IsValid)
        {
            RecordNetworkBudgetDrop(NetworkBudgetDropKind.PresentationEvent);
            return;
        }

        TrackProjectileVfxEventWrite();
        RPC_PlayProjectileVfx(attacker.Id, target.Id, fireTick, hitTick);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_PlayProjectileVfx(NetworkId attackerId, NetworkId targetId, int fireTick, int hitTick)
    {
        if (Runner == null)
        {
            return;
        }

        if (Object == null || !Object.HasStateAuthority)
        {
            TrackProjectileVfxEventWrite();
        }

        if (!Runner.TryFindObject(attackerId, out NetworkObject attacker) ||
            !Runner.TryFindObject(targetId, out NetworkObject target))
        {
            return;
        }

        if (!LocalVfxVisibility.ShouldPlay(attacker, target, LocalVfxVisibilityEventKind.Projectile))
        {
            ProjectileVfxManager.RecordSkippedCombatEvent(Runner, attacker, target, fireTick, hitTick);
            return;
        }

        ProjectileVfxManager.PlayFromCombatEvent(Runner, attacker, target, fireTick, hitTick);
    }

    private void PublishBasicAttackVfxEvent(NetworkObject attacker, NetworkObject target)
    {
        if (attacker == null || target == null)
        {
            return;
        }

        TrackBasicAttackVfxEventWrite();
        RPC_PlayBasicAttackVfx(attacker.Id, target.Id);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_PlayBasicAttackVfx(NetworkId attackerId, NetworkId targetId)
    {
        if (Runner == null)
        {
            return;
        }

        if (Object == null || !Object.HasStateAuthority)
        {
            TrackBasicAttackVfxEventWrite();
        }

        if (!Runner.TryFindObject(attackerId, out NetworkObject attacker) ||
            !Runner.TryFindObject(targetId, out NetworkObject target))
        {
            return;
        }

        if (!LocalVfxVisibility.ShouldPlay(attacker, target, LocalVfxVisibilityEventKind.BasicAttack))
        {
            return;
        }

        Unit attackerUnit = attacker.GetComponent<Unit>();
        if (attackerUnit == null)
        {
            return;
        }

        attackerUnit.PlayBasicAttackVfxFromCombatEvent(target);
    }

    private static bool TryResolveUniqueEnemy(Collider collider, HashSet<int> damagedTargets, out IEnemy enemy)
    {
        enemy = null;
        if (collider == null || damagedTargets == null)
        {
            return false;
        }

        enemy = collider.GetComponentInParent<IEnemy>();
        if (enemy == null)
        {
            return false;
        }

        int key = enemy is MonoBehaviour behaviour
            ? behaviour.GetInstanceID()
            : collider.GetInstanceID();
        return damagedTargets.Add(key);
    }
}
