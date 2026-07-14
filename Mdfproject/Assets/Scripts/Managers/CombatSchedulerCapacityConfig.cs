/// <summary>
/// Single source of truth for CombatScheduler's fixed Fusion storage budgets.
///
/// Fixed arrays are deliberately smaller than the theoretical combat population
/// because every entry consumes Fusion snapshot words. Capacity recovery is part
/// of the supported design, not an assumption that a battle can never fill them.
/// </summary>
public static class CombatSchedulerCapacityConfig
{
    public const int PendingFireCapacity = 64;
    public const int PendingHitCapacity = 96;
    public const int StatusEffectCapacity = 40;
    public const int StatBuffCapacity = 96;
    public const int ZoneCapacity = 16;

    // GameManagers.MAX_PLAYERS and FieldManager's default 10 x 9 grid are private
    // runtime configuration, so their schema values are mirrored here and guarded
    // by EditMode tests. One goal cell per field is reserved from regular units.
    public const int MaximumPlayers = 4;
    public const int DefaultFieldGridWidth = 10;
    public const int DefaultFieldGridHeight = 9;
    public const int ReservedGoalCellsPerField = 1;
    public const int MaximumPlacedUnitsPerField =
        DefaultFieldGridWidth * DefaultFieldGridHeight - ReservedGoalCellsPerField;
    public const int MaximumPlacedUnitsAcrossMatch = MaximumPlayers * MaximumPlacedUnitsPerField;

    // Monster counts continue scaling with rounds/Black Magic and therefore have no
    // finite code-level maximum. Even the finite unit-only burst already exceeds the
    // replicated queues, proving that deterministic recovery must remain enabled.
    public static bool HasFiniteMonsterAttackBurst => false;
    public const int MinimumTheoreticalAttackBurst = MaximumPlacedUnitsAcrossMatch;
    public static bool PendingFireRecoveryRequired => MinimumTheoreticalAttackBurst > PendingFireCapacity;
    public static bool PendingHitRecoveryRequired => MinimumTheoreticalAttackBurst > PendingHitCapacity;

    public static bool ValidateConfiguration(out string reason)
    {
        if (!ValidateSupportedLoad(0, 0, 0, 0, 0, out reason))
        {
            return false;
        }

        if (!PendingFireRecoveryRequired || !PendingHitRecoveryRequired || HasFiniteMonsterAttackBurst)
        {
            reason = "fixed snapshots cannot be treated as a complete theoretical attack bound";
            return false;
        }

        reason = null;
        return true;
    }

    public static bool ValidateSupportedLoad(
        int pendingFire,
        int pendingHit,
        int statusEffects,
        int statBuffs,
        int zones,
        out string reason)
    {
        if (pendingFire < 0 || pendingHit < 0 || statusEffects < 0 || statBuffs < 0 || zones < 0)
        {
            reason = "combat scheduler load cannot be negative";
            return false;
        }

        if (pendingFire > PendingFireCapacity)
        {
            reason = $"pendingFire={pendingFire} exceeds capacity={PendingFireCapacity}";
            return false;
        }

        if (pendingHit > PendingHitCapacity)
        {
            reason = $"pendingHit={pendingHit} exceeds capacity={PendingHitCapacity}";
            return false;
        }

        if (statusEffects > StatusEffectCapacity)
        {
            reason = $"statusEffects={statusEffects} exceeds capacity={StatusEffectCapacity}";
            return false;
        }

        if (statBuffs > StatBuffCapacity)
        {
            reason = $"statBuffs={statBuffs} exceeds capacity={StatBuffCapacity}";
            return false;
        }

        if (zones > ZoneCapacity)
        {
            reason = $"zones={zones} exceeds capacity={ZoneCapacity}";
            return false;
        }

        reason = null;
        return true;
    }

    /// <summary>
    /// Stable ordering used by capacity recovery: earliest due work wins and
    /// sequence is the deterministic tie-breaker.
    /// </summary>
    public static bool IsEarlier(int candidateTick, int candidateSequence, int currentTick, int currentSequence)
    {
        return candidateTick < currentTick ||
               (candidateTick == currentTick && candidateSequence < currentSequence);
    }
}
