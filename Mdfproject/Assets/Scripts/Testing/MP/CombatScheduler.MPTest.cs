#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using Fusion;
using UnityEngine;

public partial class CombatScheduler
{
    private readonly HashSet<int> _mpTestInjectedPendingFireSequences = new HashSet<int>();
    private readonly HashSet<int> _mpTestInjectedPendingHitSequences = new HashSet<int>();

    public bool MPTestInjectPendingCombatLoad(
        int pendingFireCount,
        int pendingHitCount,
        int delayTicks,
        out string reason)
    {
        reason = null;
        if (!MPTestCommandLine.IsEnabled)
        {
            reason = "missing --mpTest";
            return false;
        }

        if (Runner == null || !Runner.IsRunning || Object == null || !Object.IsValid)
        {
            reason = "scheduler network object is not ready";
            return false;
        }

        if (!Object.HasStateAuthority)
        {
            reason = "scheduler requires state authority";
            return false;
        }

        if (_fireBuckets == null || _hitBuckets == null)
        {
            InitializeHitBuckets();
        }

        pendingFireCount = Mathf.Max(0, pendingFireCount);
        pendingHitCount = Mathf.Max(0, pendingHitCount);
        delayTicks = Mathf.Max(1, delayTicks);

        int startPendingFireDrops = GetNetworkBudgetReport().PendingFireCapacityDrops;
        int startPendingHitDrops = GetNetworkBudgetReport().PendingHitCapacityDrops;
        if (!MPTestTryResolvePendingCombatObjects(out NetworkObject attackerObject, out NetworkObject targetObject, out reason))
        {
            return false;
        }

        Vector3 position = targetObject.transform.position;
        int baseTick = Runner.Tick;
        int fireTick = baseTick + delayTicks;
        int hitTick = baseTick + delayTicks + 1;

        for (int i = 0; i < pendingFireCount; i++)
        {
            var pending = new PendingFire
            {
                Attacker = attackerObject,
                Target = targetObject,
                FirePosition = position,
                Damage = 0f,
                DamageType = default,
                IsRanged = true,
                EmitVfx = false,
                ProjectileSpeedOverride = 0f,
                FireTick = fireTick,
                SplashRadius = 0f,
                EnemyLayerMask = default
            };

            if (!EnqueuePendingFire(pending))
            {
                reason = $"pending fire capacity exceeded after {i} injected entries";
                return false;
            }

            _mpTestInjectedPendingFireSequences.Add(PendingFireSequence);
        }

        for (int i = 0; i < pendingHitCount; i++)
        {
            var pending = new PendingHit
            {
                TargetId = targetObject.Id,
                ImpactPosition = position,
                Damage = 0f,
                DamageType = default,
                HitTick = hitTick,
                SplashRadius = 0f,
                EnemyLayerMask = default
            };

            if (!EnqueuePendingHit(pending))
            {
                reason = $"pending hit capacity exceeded after {i} injected entries";
                return false;
            }

            _mpTestInjectedPendingHitSequences.Add(PendingHitSequence);
        }

        var report = GetNetworkBudgetReport();
        if (report.PendingFireCapacityDrops > startPendingFireDrops || report.PendingHitCapacityDrops > startPendingHitDrops)
        {
            reason = "pending capacity drop recorded";
            return false;
        }

        reason = $"injected pendingFire={pendingFireCount}, pendingHit={pendingHitCount}, delayTicks={delayTicks}";
        return true;
    }

    private bool MPTestTryResolvePendingCombatObjects(
        out NetworkObject attackerObject,
        out NetworkObject targetObject,
        out string reason)
    {
        attackerObject = null;
        targetObject = null;
        reason = null;

        PlayerManager fallbackPlayer = null;
        foreach (var player in UnityEngine.Object.FindObjectsOfType<PlayerManager>())
        {
            if (player == null ||
                player.Runner != Runner ||
                player.Object == null ||
                !player.Object.IsValid)
            {
                continue;
            }

            if (fallbackPlayer == null)
            {
                fallbackPlayer = player;
            }
            if (player.Object.HasStateAuthority)
            {
                attackerObject = player.Object;
                targetObject = player.Object;
                break;
            }
        }

        if (targetObject == null && fallbackPlayer != null)
        {
            attackerObject = fallbackPlayer.Object;
            targetObject = fallbackPlayer.Object;
        }

        if (targetObject == null)
        {
            reason = "no durable PlayerManager target found for pending combat injection";
            return false;
        }

        return true;
    }

    public int MPTestClearInjectedPendingCombatLoad()
    {
        int cleared = 0;
        foreach (int sequence in _mpTestInjectedPendingFireSequences)
        {
            ClearPendingFireSnapshot(sequence);
            RemovePendingFireFromBuckets(sequence);
            cleared++;
        }

        foreach (int sequence in _mpTestInjectedPendingHitSequences)
        {
            ClearPendingHitSnapshot(sequence);
            RemovePendingHitFromBuckets(sequence);
            cleared++;
        }

        _mpTestInjectedPendingFireSequences.Clear();
        _mpTestInjectedPendingHitSequences.Clear();
        return cleared;
    }

    private void RemovePendingFireFromBuckets(int sequence)
    {
        if (_fireBuckets == null)
        {
            return;
        }

        foreach (var bucket in _fireBuckets)
        {
            bucket?.RemoveAll(item => item.SnapshotSequence == sequence);
        }
    }

    private void RemovePendingHitFromBuckets(int sequence)
    {
        if (_hitBuckets == null)
        {
            return;
        }

        foreach (var bucket in _hitBuckets)
        {
            bucket?.RemoveAll(item => item.SnapshotSequence == sequence);
        }
    }
}
#endif
