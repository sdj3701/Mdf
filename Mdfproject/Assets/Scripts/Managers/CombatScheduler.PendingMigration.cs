using System.Collections.Generic;
using Fusion;
using UnityEngine;

public partial class CombatScheduler
{
    public struct PendingFireMigrationSnapshot
    {
        public int Sequence;
        public int RemainingTicks;
        public NetworkId AttackerId;
        public NetworkId TargetId;
        public Vector3 FirePosition;
        public float Damage;
        public DamageType DamageType;
        public bool IsRanged;
        public bool EmitVfx;
        public float ProjectileSpeedOverride;
        public float SplashRadius;
        public int EnemyLayerMask;
    }

    public struct PendingHitMigrationSnapshot
    {
        public int Sequence;
        public int RemainingTicks;
        public NetworkId TargetId;
        public Vector3 ImpactPosition;
        public float Damage;
        public DamageType DamageType;
        public float SplashRadius;
        public int EnemyLayerMask;
    }

    public int CapturePendingCombatForMigration(
        List<PendingFireMigrationSnapshot> fireSnapshots,
        List<PendingHitMigrationSnapshot> hitSnapshots)
    {
        fireSnapshots?.Clear();
        hitSnapshots?.Clear();
        if (!IsSchedulerNetworkReady())
        {
            return 0;
        }

        int nowTick = Runner.Tick;
        for (int i = 0; i < PendingFireCapacity; i++)
        {
            PendingFireSnapshot snapshot = PendingFireSnapshots[i];
            if (snapshot.Sequence <= 0)
            {
                continue;
            }

            fireSnapshots?.Add(new PendingFireMigrationSnapshot
            {
                Sequence = snapshot.Sequence,
                RemainingTicks = Mathf.Max(1, snapshot.FireTick - nowTick),
                AttackerId = snapshot.AttackerId,
                TargetId = snapshot.TargetId,
                FirePosition = UnpackVector(snapshot.FirePositionX, snapshot.FirePositionY, snapshot.FirePositionZ),
                Damage = UnpackFloat(snapshot.Damage),
                DamageType = (DamageType)snapshot.DamageType,
                IsRanged = snapshot.IsRanged != 0,
                EmitVfx = snapshot.EmitVfx != 0,
                ProjectileSpeedOverride = UnpackFloat(snapshot.ProjectileSpeedOverride),
                SplashRadius = UnpackFloat(snapshot.SplashRadius),
                EnemyLayerMask = snapshot.EnemyLayerMask
            });
        }

        for (int i = 0; i < PendingHitCapacity; i++)
        {
            PendingHitSnapshot snapshot = PendingHitSnapshots[i];
            if (snapshot.Sequence <= 0)
            {
                continue;
            }

            hitSnapshots?.Add(new PendingHitMigrationSnapshot
            {
                Sequence = snapshot.Sequence,
                RemainingTicks = Mathf.Max(1, snapshot.HitTick - nowTick),
                TargetId = snapshot.TargetId,
                ImpactPosition = UnpackVector(snapshot.ImpactPositionX, snapshot.ImpactPositionY, snapshot.ImpactPositionZ),
                Damage = UnpackFloat(snapshot.Damage),
                DamageType = (DamageType)snapshot.DamageType,
                SplashRadius = UnpackFloat(snapshot.SplashRadius),
                EnemyLayerMask = snapshot.EnemyLayerMask
            });
        }

        return (fireSnapshots?.Count ?? 0) + (hitSnapshots?.Count ?? 0);
    }

    public int RestorePendingCombatFromMigration(
        IReadOnlyList<PendingFireMigrationSnapshot> fireSnapshots,
        IReadOnlyList<PendingHitMigrationSnapshot> hitSnapshots,
        string context)
    {
        if (!IsSchedulerNetworkReady() || Object == null || !Object.HasStateAuthority)
        {
            return 0;
        }

        ClearAllPendingCombatSnapshots();
        int restored = 0;
        int nowTick = Runner.Tick;

        if (fireSnapshots != null)
        {
            foreach (var snapshot in fireSnapshots)
            {
                NetworkObject target = ResolveNetworkObject(snapshot.TargetId);
                if (target == null)
                {
                    Debug.LogWarning($"[CombatScheduler] Pending fire migration restore skipped missing target. context={context}, sourceSeq={snapshot.Sequence}, target={snapshot.TargetId}");
                    continue;
                }

                var pending = new PendingFire
                {
                    Attacker = ResolveNetworkObject(snapshot.AttackerId),
                    Target = target,
                    FirePosition = snapshot.FirePosition,
                    Damage = snapshot.Damage,
                    DamageType = snapshot.DamageType,
                    IsRanged = snapshot.IsRanged,
                    EmitVfx = snapshot.EmitVfx,
                    ProjectileSpeedOverride = snapshot.ProjectileSpeedOverride,
                    FireTick = nowTick + Mathf.Max(1, snapshot.RemainingTicks),
                    SplashRadius = snapshot.SplashRadius,
                    EnemyLayerMask = new LayerMask { value = snapshot.EnemyLayerMask }
                };

                if (EnqueuePendingFire(pending))
                {
                    restored++;
                }
            }
        }

        if (hitSnapshots != null)
        {
            foreach (var snapshot in hitSnapshots)
            {
                if (!Runner.TryFindObject(snapshot.TargetId, out _))
                {
                    Debug.LogWarning($"[CombatScheduler] Pending hit migration restore skipped missing target. context={context}, sourceSeq={snapshot.Sequence}, target={snapshot.TargetId}");
                    continue;
                }

                var pending = new PendingHit
                {
                    TargetId = snapshot.TargetId,
                    ImpactPosition = snapshot.ImpactPosition,
                    Damage = snapshot.Damage,
                    DamageType = snapshot.DamageType,
                    HitTick = nowTick + Mathf.Max(1, snapshot.RemainingTicks),
                    SplashRadius = snapshot.SplashRadius,
                    EnemyLayerMask = new LayerMask { value = snapshot.EnemyLayerMask }
                };

                if (EnqueuePendingHit(pending))
                {
                    restored++;
                }
            }
        }

        RefreshNetworkBudgetPeaks();
        return restored;
    }

    private void ClearAllPendingCombatSnapshots()
    {
        for (int i = 0; i < PendingFireCapacity; i++)
        {
            PendingFireSnapshots.Set(i, default);
        }

        for (int i = 0; i < PendingHitCapacity; i++)
        {
            PendingHitSnapshots.Set(i, default);
        }

        _localPendingFireSequences.Clear();
        _localPendingHitSequences.Clear();
        if (_fireBuckets != null)
        {
            foreach (var bucket in _fireBuckets)
            {
                bucket?.Clear();
            }
        }

        if (_hitBuckets != null)
        {
            foreach (var bucket in _hitBuckets)
            {
                bucket?.Clear();
            }
        }
    }
}
