#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using Fusion;
using UnityEngine;

public partial class CombatScheduler
{
    public struct MPTestCapacityRecoveryProbeReport
    {
        public bool Success;
        public int UnitCommitCount;
        public int UnitCooldownCommitCount;
        public int UnitManaCommitCount;
        public int UnitExpectedManaCommitCount;
        public int MonsterCommitCount;
        public int PendingHitBackpressureCount;
        public int PendingHitDropCount;
        public int InitialPendingHitCount;
        public int FinalPendingHitCount;
    }

    public MPTestCapacityRecoveryProbeReport LastMPTestCapacityRecoveryProbeReport { get; private set; }
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

        NetworkBudgetReport startReport = GetNetworkBudgetReport();
        int startPendingFireDrops = startReport.PendingFireCapacityDrops;
        int startPendingHitDrops = startReport.PendingHitCapacityDrops;
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

        int fireFallbacks = report.PendingFireCapacityFallbacks - startReport.PendingFireCapacityFallbacks;
        int hitFallbacks = report.PendingHitCapacityFallbacks - startReport.PendingHitCapacityFallbacks;
        reason = $"injected pendingFire={pendingFireCount}, pendingHit={pendingHitCount}, delayTicks={delayTicks}, fireFallbacks={fireFallbacks}, hitFallbacks={hitFallbacks}, drops=0";
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

    public bool MPTestRunCapacityRecoveryProbe(out string reason)
    {
        reason = null;
        LastMPTestCapacityRecoveryProbeReport = default;
        if (!MPTestCommandLine.IsEnabled)
        {
            reason = "missing --mpTest";
            return false;
        }

        if (MPTestCommandLine.IsGameFlowFrozen)
        {
            reason = "capacity recovery probe requires unfrozen game flow";
            return false;
        }

        if (Runner == null || !Runner.IsRunning || Object == null || !Object.IsValid ||
            !Object.HasStateAuthority || !EnsureLocalSchedulerState())
        {
            reason = "scheduler authority is not ready";
            return false;
        }

        if (!MPTestTryResolveCapacityProbeActors(out Unit unit, out Monster monster, out reason))
        {
            return false;
        }

        MPTestClearInjectedPendingCombatLoad();
        NetworkBudgetReport startReport = GetNetworkBudgetReport();
        int initialPendingHits = startReport.CurrentPendingHitActive;
        if (initialPendingHits >= PendingHitCapacity)
        {
            reason = "pending hit queue was already full before the probe";
            return false;
        }

        bool unitProbeStarted = false;
        bool monsterProbeStarted = false;
        try
        {
            if (!MPTestFillPendingHitCapacity(monster.Object, 3600, out reason))
            {
                return false;
            }

            int unitRejectedSequence = PendingHitSequence;
            unitProbeStarted = unit.MPTestBeginBasicAttackCapacityRecoveryProbe(this, monster.Object, out reason);
            if (!unitProbeStarted || PendingHitSequence != unitRejectedSequence ||
                !unit.MPTestHasBasicAttackCapacityDebt)
            {
                reason ??= "unit source did not preserve a rejected capacity debt";
                return false;
            }

            if (!MPTestReleaseOneInjectedPendingHit())
            {
                reason = "failed to release one synthetic hit for the unit retry";
                return false;
            }

            int unitCommitSequence = PendingHitSequence;
            if (!unit.MPTestResumeBasicAttackCapacityRecoveryProbe(out reason) ||
                PendingHitSequence != unitCommitSequence + 1 ||
                unit.MPTestCapacityRecoveryCommitCount != 1 ||
                unit.MPTestCapacityRecoveryCooldownCount != 1 ||
                unit.MPTestCapacityRecoveryManaCount !=
                    (unit.MPTestCapacityRecoveryManaApplicable ? 1 : 0))
            {
                reason ??= "unit retry did not commit exactly once with one cooldown/mana side effect";
                return false;
            }
            _mpTestInjectedPendingHitSequences.Add(PendingHitSequence);
            unit.MPTestEndBasicAttackCapacityRecoveryProbe();
            unitProbeStarted = false;
            MPTestClearInjectedPendingCombatLoad();

            if (!MPTestFillPendingHitCapacity(unit.Object, 3600, out reason))
            {
                return false;
            }

            int monsterRejectedSequence = PendingHitSequence;
            monsterProbeStarted = monster.MPTestBeginBasicAttackCapacityRecoveryProbe(this, unit.Object, out reason);
            if (!monsterProbeStarted || PendingHitSequence != monsterRejectedSequence ||
                !monster.MPTestHasBasicAttackCapacityDebt)
            {
                reason ??= "monster source did not preserve a rejected capacity debt";
                return false;
            }

            if (!MPTestReleaseOneInjectedPendingHit())
            {
                reason = "failed to release one synthetic hit for the monster retry";
                return false;
            }

            int monsterCommitSequence = PendingHitSequence;
            if (!monster.MPTestResumeBasicAttackCapacityRecoveryProbe(this, unit.Object, out reason) ||
                PendingHitSequence != monsterCommitSequence + 1 ||
                monster.MPTestCapacityRecoveryCommitCount != 1)
            {
                reason ??= "monster retry did not commit exactly once";
                return false;
            }
            _mpTestInjectedPendingHitSequences.Add(PendingHitSequence);
            monster.MPTestEndBasicAttackCapacityRecoveryProbe();
            monsterProbeStarted = false;
            MPTestClearInjectedPendingCombatLoad();

            NetworkBudgetReport finalReport = GetNetworkBudgetReport();
            if (finalReport.CurrentPendingHitActive != initialPendingHits ||
                finalReport.PendingFireCapacityDrops != startReport.PendingFireCapacityDrops ||
                finalReport.PendingHitCapacityDrops != startReport.PendingHitCapacityDrops ||
                finalReport.ZoneCapacityDrops != startReport.ZoneCapacityDrops ||
                finalReport.ZoneDebtTerminalFailures != startReport.ZoneDebtTerminalFailures)
            {
                reason = "capacity recovery probe changed durable queue baseline or recorded a true drop";
                return false;
            }

            int recoveredRejects = finalReport.PendingHitCapacityFallbacks -
                                   startReport.PendingHitCapacityFallbacks;
            if (recoveredRejects < 2)
            {
                reason = $"expected at least two visible pending-hit backpressures, actual={recoveredRejects}";
                return false;
            }

            LastMPTestCapacityRecoveryProbeReport = new MPTestCapacityRecoveryProbeReport
            {
                Success = true,
                UnitCommitCount = unit.MPTestCapacityRecoveryCommitCount,
                UnitCooldownCommitCount = unit.MPTestCapacityRecoveryCooldownCount,
                UnitManaCommitCount = unit.MPTestCapacityRecoveryManaCount,
                UnitExpectedManaCommitCount = unit.MPTestCapacityRecoveryManaApplicable ? 1 : 0,
                MonsterCommitCount = monster.MPTestCapacityRecoveryCommitCount,
                PendingHitBackpressureCount = recoveredRejects,
                PendingHitDropCount = finalReport.PendingHitCapacityDrops - startReport.PendingHitCapacityDrops,
                InitialPendingHitCount = initialPendingHits,
                FinalPendingHitCount = finalReport.CurrentPendingHitActive
            };

            reason =
                $"unit=exactly_once,cooldown=1,mana={(unit.MPTestCapacityRecoveryManaApplicable ? 1 : 0)}," +
                $"monster=exactly_once,recoveredRejects={recoveredRejects},drops=0,baseline={initialPendingHits}";
            return true;
        }
        finally
        {
            if (unitProbeStarted)
            {
                unit.MPTestEndBasicAttackCapacityRecoveryProbe();
            }
            if (monsterProbeStarted)
            {
                monster.MPTestEndBasicAttackCapacityRecoveryProbe();
            }
            MPTestClearInjectedPendingCombatLoad();
        }
    }

    private bool MPTestTryResolveCapacityProbeActors(
        out Unit unit,
        out Monster monster,
        out string reason)
    {
        unit = null;
        monster = null;
        reason = null;
        Unit[] units = UnityEngine.Object.FindObjectsOfType<Unit>();
        for (int i = 0; i < units.Length; i++)
        {
            Unit candidate = units[i];
            if (candidate != null && candidate.Object != null && candidate.Object.IsValid &&
                candidate.Object.HasStateAuthority && candidate.Runner == Runner && !candidate.IsDead)
            {
                unit = candidate;
                break;
            }
        }

        Monster[] monsters = UnityEngine.Object.FindObjectsOfType<Monster>();
        for (int i = 0; i < monsters.Length; i++)
        {
            Monster candidate = monsters[i];
            if (candidate != null && candidate.Object != null && candidate.Object.IsValid &&
                candidate.Object.HasStateAuthority && candidate.Runner == Runner && candidate.CurrentHealth > 0f)
            {
                monster = candidate;
                break;
            }
        }

        if (unit == null || monster == null)
        {
            reason = $"capacity probe requires one live authority Unit and Monster; unit={unit != null},monster={monster != null}";
            return false;
        }

        return true;
    }

    private bool MPTestFillPendingHitCapacity(
        NetworkObject targetObject,
        int delayTicks,
        out string reason)
    {
        reason = null;
        int missing = PendingHitCapacity - GetNetworkBudgetReport().CurrentPendingHitActive;
        if (missing <= 0)
        {
            reason = "no free hit slot was available for synthetic fill";
            return false;
        }

        int hitTick = Runner.Tick + Mathf.Max(2, delayTicks);
        for (int i = 0; i < missing; i++)
        {
            if (!EnqueuePendingHit(new PendingHit
                {
                    TargetId = targetObject.Id,
                    ImpactPosition = targetObject.transform.position,
                    Damage = 0f,
                    DamageType = default,
                    HitTick = hitTick,
                    SplashRadius = 0f,
                    EnemyLayerMask = default
                }))
            {
                reason = $"synthetic hit fill failed at {i}/{missing}";
                return false;
            }

            _mpTestInjectedPendingHitSequences.Add(PendingHitSequence);
        }

        return GetNetworkBudgetReport().CurrentPendingHitActive == PendingHitCapacity;
    }

    private bool MPTestReleaseOneInjectedPendingHit()
    {
        int sequence = int.MaxValue;
        foreach (int candidate in _mpTestInjectedPendingHitSequences)
        {
            if (candidate < sequence)
            {
                sequence = candidate;
            }
        }

        if (sequence == int.MaxValue)
        {
            return false;
        }

        ClearPendingHitSnapshot(sequence);
        RemovePendingHitFromBuckets(sequence);
        _mpTestInjectedPendingHitSequences.Remove(sequence);
        return true;
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
