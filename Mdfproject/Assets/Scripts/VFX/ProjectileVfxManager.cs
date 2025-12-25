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
        public Transform TargetTransform;
        public Vector3 LastKnownTargetPos;
    }

    private void Awake()
    {
        if (pool == null)
        {
            pool = VfxPoolManager.Instance;
        }
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

        for (int seq = startSeq; seq <= currentSeq; seq++)
        {
            if (_scheduler.TryGetEvent(seq, out var evt))
            {
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
            return;
        }

        if (evt.HitTick <= runner.Tick)
        {
            return;
        }

        if (!TryResolveProjectileKey(evt, out var projectileKey))
        {
            return;
        }

        GameObject prefab = await AssetLoader.LoadAssetAsync<GameObject>(projectileKey);
        if (prefab == null || runner.Tick >= evt.HitTick)
        {
            return;
        }

        Vector3 firePos = ResolveFirePosition(evt);
        Vector3 spawnPos = CalculateSpawnPosition(firePos, evt, runner.Tick);
        GameObject instance = pool != null
            ? pool.Spawn(prefab, spawnPos, Quaternion.identity, vfxRoot)
            : Instantiate(prefab, spawnPos, Quaternion.identity, vfxRoot);

        if (instance == null)
        {
            return;
        }

        var projectile = instance.GetComponent<Projectile>();
        if (projectile != null)
        {
            projectile.SetVisualOnly(true);
        }

        var active = new ActiveProjectile
        {
            Sequence = evt.Sequence,
            FireTick = evt.FireTick,
            HitTick = evt.HitTick,
            Instance = instance,
            TargetTransform = evt.Target != null ? evt.Target.transform : null,
            LastKnownTargetPos = evt.Target != null ? evt.Target.transform.position : firePos
        };

        _activeBySeq[evt.Sequence] = active;
        _activeProjectiles.Add(active);
    }

    private Vector3 CalculateSpawnPosition(Vector3 firePos, CombatScheduler.ProjectileEventData evt, int nowTick)
    {
        int totalTicks = Mathf.Max(1, evt.HitTick - evt.FireTick);
        float progress = Mathf.Clamp01((nowTick - evt.FireTick) / (float)totalTicks);
        Vector3 targetPos = evt.Target != null ? evt.Target.transform.position : firePos;
        return Vector3.Lerp(firePos, targetPos, progress);
    }

    private bool TryResolveProjectileKey(CombatScheduler.ProjectileEventData evt, out string projectileKey)
    {
        projectileKey = null;
        if (evt.Attacker == null)
        {
            return false;
        }

        var unit = evt.Attacker.GetComponent<Unit>();
        if (unit == null || unit.Data == null || unit.Data.projectilePrefabsByStarLevel == null)
        {
            return false;
        }

        int starIndex = Mathf.Clamp(unit.starLevel - 1, 0, unit.Data.projectilePrefabsByStarLevel.Length - 1);
        projectileKey = unit.Data.projectilePrefabsByStarLevel[starIndex];
        return !string.IsNullOrEmpty(projectileKey);
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

        return evt.Attacker.transform.position;
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

        for (int i = _activeProjectiles.Count - 1; i >= 0; i--)
        {
            var active = _activeProjectiles[i];
            if (active.Instance == null)
            {
                RemoveActive(active);
                continue;
            }

            if (runner.Tick >= active.HitTick)
            {
                DespawnProjectile(active);
                RemoveActive(active);
                continue;
            }

            Vector3 targetPos = active.LastKnownTargetPos;
            if (active.TargetTransform != null)
            {
                targetPos = active.TargetTransform.position;
                active.LastKnownTargetPos = targetPos;
            }

            Vector3 direction = targetPos - active.Instance.transform.position;
            float remainingSeconds = Mathf.Max(minRemainingSeconds, (active.HitTick - runner.Tick) * runner.DeltaTime);
            float speedNeeded = direction.magnitude / remainingSeconds;
            if (maxSpeed > 0f)
            {
                speedNeeded = Mathf.Min(speedNeeded, maxSpeed);
            }

            if (direction.sqrMagnitude > 1e-6f)
            {
                Vector3 step = direction.normalized * speedNeeded * Time.deltaTime;
                active.Instance.transform.position += step;
                if (alignToDirection)
                {
                    active.Instance.transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
                }
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
