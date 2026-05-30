using System.Collections.Generic;
using Fusion;
using UnityEngine;

public class CombatScheduler : NetworkBehaviour
{
    public static CombatScheduler Instance { get; private set; }

    [Header("Hit Scheduling")]
    [SerializeField] private int hitBufferSize = 256;
    [SerializeField] private float defaultProjectileSpeed = 20f;

    private const int MaxPlayers = 4;
    private const int MaxUnitsPerPlayer = 20;
    private const int MaxAttackVfxPerSecond = 4;
    private const int MaxProjectileFlightSeconds = 4;
    private const int EventBufferSafetyMargin = 256;
    private const int EventBufferCapacity =
        MaxPlayers * MaxUnitsPerPlayer * MaxAttackVfxPerSecond * MaxProjectileFlightSeconds + EventBufferSafetyMargin;

    [Networked] public int EventSequence { get; private set; }
    [Networked, Capacity(EventBufferCapacity)] private NetworkArray<int> EventSeqs { get; }
    [Networked, Capacity(EventBufferCapacity)] private NetworkArray<int> EventFireTicks { get; }
    [Networked, Capacity(EventBufferCapacity)] private NetworkArray<int> EventHitTicks { get; }
    [Networked, Capacity(EventBufferCapacity)] private NetworkArray<NetworkObject> EventAttackers { get; }
    [Networked, Capacity(EventBufferCapacity)] private NetworkArray<NetworkObject> EventTargets { get; }
    [Networked] public int BasicAttackVfxEventSequence { get; private set; }
    [Networked, Capacity(EventBufferCapacity)] private NetworkArray<int> BasicAttackVfxEventSeqs { get; }
    [Networked, Capacity(EventBufferCapacity)] private NetworkArray<int> BasicAttackVfxEventTicks { get; }
    [Networked, Capacity(EventBufferCapacity)] private NetworkArray<NetworkObject> BasicAttackVfxEventAttackers { get; }
    [Networked, Capacity(EventBufferCapacity)] private NetworkArray<NetworkObject> BasicAttackVfxEventTargets { get; }

    private List<PendingFire>[] _fireBuckets;
    private List<PendingHit>[] _hitBuckets;
    private List<PendingBasicAttackVfx>[] _basicAttackVfxBuckets;

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
    }

    private struct PendingBasicAttackVfx
    {
        public NetworkObject Attacker;
        public NetworkObject Target;
        public int Tick;
    }

    public struct ProjectileEventData
    {
        public int Sequence;
        public int FireTick;
        public int HitTick;
        public NetworkObject Attacker;
        public NetworkObject Target;
    }

    public struct BasicAttackVfxEventData
    {
        public int Sequence;
        public int Tick;
        public NetworkObject Attacker;
        public NetworkObject Target;
    }

    public int EventCapacity => EventBufferCapacity;
    public int BasicAttackVfxEventCapacity => EventBufferCapacity;

    public override void Spawned()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else if (Instance != this)
        {
            return;
        }

        InitializeHitBuckets();
    }

    private void OnDisable()
    {
        if (Instance == this)
        {
            Instance = null;
        }
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

        ProcessDueBasicAttackVfx();
        ProcessDueFires();
        ProcessDueHits();
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

    public bool TryGetEvent(int sequence, out ProjectileEventData data)
    {
        data = default;
        if (sequence <= 0)
        {
            return false;
        }

        int index = sequence % EventBufferCapacity;
        if (EventSeqs[index] != sequence)
        {
            return false;
        }

        data.Sequence = sequence;
        data.FireTick = EventFireTicks[index];
        data.HitTick = EventHitTicks[index];
        data.Attacker = EventAttackers[index];
        data.Target = EventTargets[index];
        return true;
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
            WriteBasicAttackVfxEvent(attacker, target, tick);
        }
    }

    public bool TryGetBasicAttackVfxEvent(int sequence, out BasicAttackVfxEventData data)
    {
        data = default;
        if (sequence <= 0)
        {
            return false;
        }

        int index = sequence % EventBufferCapacity;
        if (BasicAttackVfxEventSeqs[index] != sequence)
        {
            return false;
        }

        data.Sequence = sequence;
        data.Tick = BasicAttackVfxEventTicks[index];
        data.Attacker = BasicAttackVfxEventAttackers[index];
        data.Target = BasicAttackVfxEventTargets[index];
        return true;
    }

    public void GetInFlightEvents(int nowTick, List<ProjectileEventData> results)
    {
        if (results == null)
        {
            return;
        }

        results.Clear();
        int currentSeq = EventSequence;
        if (currentSeq <= 0)
        {
            return;
        }

        int minSeq = Mathf.Max(1, currentSeq - EventBufferCapacity + 1);
        for (int seq = minSeq; seq <= currentSeq; seq++)
        {
            if (!TryGetEvent(seq, out var evt))
            {
                continue;
            }

            if (evt.HitTick > nowTick)
            {
                results.Add(evt);
            }
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
                int tick = Mathf.Max(pending.Tick, Runner.Tick);
                WriteBasicAttackVfxEvent(pending.Attacker, pending.Target, tick);
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

        EnqueuePendingHit(new PendingHit
        {
            TargetId = target.Id,
            ImpactPosition = targetPosition,
            Damage = damage,
            DamageType = damageType,
            HitTick = hitTick,
            SplashRadius = splashRadius,
            EnemyLayerMask = enemyLayerMask
        });

        if (emitVfx)
        {
            WriteProjectileEvent(attacker, target, fireTick, hitTick);
        }
    }

    private void ApplyHit(PendingHit hit)
    {
        if (hit.SplashRadius > 0f)
        {
            Collider[] enemiesInRange = Physics.OverlapSphere(hit.ImpactPosition, hit.SplashRadius, hit.EnemyLayerMask);
            foreach (var enemyCollider in enemiesInRange)
            {
                var enemy = enemyCollider.GetComponent<IEnemy>();
                if (enemy != null)
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
        for (int i = 0; i < size; i++)
        {
            _fireBuckets[i] = new List<PendingFire>();
            _hitBuckets[i] = new List<PendingHit>();
            _basicAttackVfxBuckets[i] = new List<PendingBasicAttackVfx>();
        }
    }

    private void EnqueuePendingFire(PendingFire fire)
    {
        int bucketIndex = fire.FireTick % _fireBuckets.Length;
        _fireBuckets[bucketIndex].Add(fire);
    }

    private void EnqueuePendingHit(PendingHit hit)
    {
        int bucketIndex = hit.HitTick % _hitBuckets.Length;
        _hitBuckets[bucketIndex].Add(hit);
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
        if (attacker == null || target == null || !target.IsValid)
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

    private void WriteProjectileEvent(NetworkObject attacker, NetworkObject target, int fireTick, int hitTick)
    {
        int nextSeq = EventSequence + 1;
        EventSequence = nextSeq;

        int index = nextSeq % EventBufferCapacity;
        EventSeqs.Set(index, nextSeq);
        EventFireTicks.Set(index, fireTick);
        EventHitTicks.Set(index, hitTick);
        EventAttackers.Set(index, attacker);
        EventTargets.Set(index, target);
    }

    private void WriteBasicAttackVfxEvent(NetworkObject attacker, NetworkObject target, int tick)
    {
        int nextSeq = BasicAttackVfxEventSequence + 1;
        BasicAttackVfxEventSequence = nextSeq;

        int index = nextSeq % EventBufferCapacity;
        BasicAttackVfxEventSeqs.Set(index, nextSeq);
        BasicAttackVfxEventTicks.Set(index, tick);
        BasicAttackVfxEventAttackers.Set(index, attacker);
        BasicAttackVfxEventTargets.Set(index, target);
    }
}
