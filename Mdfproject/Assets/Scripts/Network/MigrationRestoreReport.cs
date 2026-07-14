using System;
using Fusion;
using UnityEngine;

[Serializable]
public readonly struct MigrationRestoreReport
{
    public MigrationRestoreReport(
        string scope,
        int captured,
        int restored,
        int skipped,
        int failed,
        string failureReason = null)
    {
        Scope = scope ?? string.Empty;
        Captured = Mathf.Max(0, captured);
        Restored = Mathf.Max(0, restored);
        Skipped = Mathf.Max(0, skipped);
        Failed = Mathf.Max(0, failed);
        FailureReason = failureReason ?? string.Empty;
    }

    public string Scope { get; }
    public int Captured { get; }
    public int Restored { get; }
    public int Skipped { get; }
    public int Failed { get; }
    public string FailureReason { get; }
    public int Accounted => Restored + Skipped + Failed;
    public int Missing => Mathf.Max(0, Captured - Accounted);
    // Over-accounting is a schema/restore bug, not a successful terminal state. Treating
    // it as success can hide duplicate restores just as easily as under-accounting hides
    // missing state.
    public bool IsTerminal => Accounted == Captured;
    public bool Succeeded => IsTerminal && Failed == 0;

    public static MigrationRestoreReport Empty(string scope)
    {
        return new MigrationRestoreReport(scope, 0, 0, 0, 0);
    }

    public static MigrationRestoreReport FailedScope(string scope, int captured, string reason)
    {
        int normalizedCaptured = Mathf.Max(1, captured);
        return new MigrationRestoreReport(scope, normalizedCaptured, 0, 0, normalizedCaptured, reason);
    }

    public MigrationRestoreReport Combine(MigrationRestoreReport other, string combinedScope = null)
    {
        string reason;
        if (string.IsNullOrWhiteSpace(FailureReason))
        {
            reason = other.FailureReason;
        }
        else if (string.IsNullOrWhiteSpace(other.FailureReason))
        {
            reason = FailureReason;
        }
        else
        {
            reason = $"{FailureReason}|{other.FailureReason}";
        }

        return new MigrationRestoreReport(
            combinedScope ?? Scope,
            Captured + other.Captured,
            Restored + other.Restored,
            Skipped + other.Skipped,
            Failed + other.Failed,
            reason);
    }

    public override string ToString()
    {
        return $"scope={Scope},captured={Captured},restored={Restored},skipped={Skipped},failed={Failed},missing={Missing},terminal={IsTerminal},success={Succeeded},reason={FailureReason}";
    }
}

[Serializable]
public struct FieldUnitMigrationSnapshot
{
    public bool HasRuntimeState;
    public uint NetworkIdRaw;
    public UnitData UnitDataRef;
    public string UnitDataKey;
    public int StarLevel;
    public Vector3Int Position;
    public float CurrentHealth;
    public float MaxHealth;
    public float CurrentMana;
    public float MaxMana;
    public SkillActivationType ActivationMode;
    public bool HasActivationMode;
    public float AttackCooldownRemaining;
    public bool IsDead;
    public bool WasSkillCasting;
    public float SkillCastLockRemaining;
    public bool SkillCapacityBackpressurePending;
    public bool BasicAttackCapacityBackpressurePending;
    public bool BasicAttackDebtHasPayload;
    public NetworkId BasicAttackDebtTargetId;
    public uint BasicAttackDebtTargetIdRaw;
    public Vector3 BasicAttackDebtFirePosition;
    public float BasicAttackDebtDamage;
    public DamageType BasicAttackDebtDamageType;
    public float BasicAttackDebtProjectileSpeed;
    public float BasicAttackDebtSplashRadius;
    public int BasicAttackDebtEnemyLayerMask;
    public float BasicAttackDebtFireDelaySeconds;
    public bool BasicAttackDebtIsRanged;
    public bool BasicAttackDebtEmitVfx;
    public bool BasicAttackDebtCooldownCommitted;
    public int PendingZonePulseDebtToken;
    public int PendingZonePulseNextEffectIndex;
    public bool BerserkModeActive;
}
