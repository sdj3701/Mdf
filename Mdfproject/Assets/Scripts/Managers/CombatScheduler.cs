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

    private List<PendingHit>[] _hitBuckets;

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

    public struct ProjectileEventData
    {
        public int Sequence;
        public int FireTick;
        public int HitTick;
        public NetworkObject Attacker;
        public NetworkObject Target;
    }

    public int EventCapacity => EventBufferCapacity;

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
        if (!Object.HasStateAuthority || _hitBuckets == null || _hitBuckets.Length == 0)
        {
            return;
        }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (MPTestCommandLine.IsGameFlowFrozen)
        {
            return;
        }
#endif

        int bucketIndex = Runner.Tick % _hitBuckets.Length;
        var bucket = _hitBuckets[bucketIndex];
        for (int i = 0; i < bucket.Count; i++)
        {
            var hit = bucket[i];
            if (hit.HitTick != Runner.Tick)
            {
                continue;
            }

            // 스플래시 공격 처리
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
            }
            else
            {
                // 단일 대상 공격
                if (Runner.TryFindObject(hit.TargetId, out var targetObj))
                {
                    var enemy = targetObj.GetComponent<IEnemy>();
                    if (enemy != null)
                    {
                        enemy.TakeDamage(hit.Damage, hit.DamageType);
                    }
                }
            }
        }
        bucket.Clear();
    }

    public void ScheduleHit(NetworkObject attacker, NetworkObject target, Vector3 firePos, float damage,
        DamageType damageType, bool isRanged, bool emitVfx, float projectileSpeedOverride = 0f, float splashRadius = 0f, LayerMask enemyLayerMask = default)
    {
        if (!Object.HasStateAuthority || Runner == null || target == null || _hitBuckets == null)
        {
            return;
        }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (MPTestCommandLine.IsGameFlowFrozen)
        {
            return;
        }
#endif

        int fireTick = Runner.Tick;
        int hitTick = fireTick;

        if (isRanged)
        {
            float projectileSpeed = projectileSpeedOverride > 0f ? projectileSpeedOverride : defaultProjectileSpeed;
            if (projectileSpeed > 0f)
            {
                float distance = Vector3.Distance(firePos, target.transform.position);
                float travelTime = distance / projectileSpeed;
                float deltaTime = Runner.DeltaTime > 0f ? Runner.DeltaTime : Time.deltaTime;
                int travelTicks = Mathf.Max(0, Mathf.CeilToInt(travelTime / Mathf.Max(0.0001f, deltaTime)));
                hitTick = fireTick + travelTicks;
            }
        }

        if (hitTick <= fireTick)
        {
            hitTick = fireTick + 1;
        }

        int bucketIndex = hitTick % _hitBuckets.Length;
        _hitBuckets[bucketIndex].Add(new PendingHit
        {
            TargetId = target.Id,
            ImpactPosition = target.transform.position,
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

    private void InitializeHitBuckets()
    {
        int size = Mathf.Max(1, hitBufferSize);
        _hitBuckets = new List<PendingHit>[size];
        for (int i = 0; i < size; i++)
        {
            _hitBuckets[i] = new List<PendingHit>();
        }
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
}
