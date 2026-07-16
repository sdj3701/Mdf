/// <summary>
/// Pure decisions used by the replicated match-flow shell. Fusion timers and state mutation
/// remain in GameManagers; display and transition gating rules can be tested without a runner.
/// </summary>
public static class MatchFlowPolicy
{
    public static float ResolveDisplayedPhaseTime(float phaseRemaining, bool sequenceTransitioning)
    {
        if (sequenceTransitioning)
        {
            return 0f;
        }

        // Matches Mathf.Max(0f, value), including NaN and signed-zero behavior,
        // without taking a UnityEngine dependency in the Foundation assembly.
        return 0f > phaseRemaining ? 0f : phaseRemaining;
    }

    public static bool ShouldIgnoreTransitionRequest(
        bool isGameOver,
        bool targetsCurrentState,
        bool sequenceTransitioning)
    {
        return isGameOver || targetsCurrentState || sequenceTransitioning;
    }

    public static bool ShouldWaitForCombatDebt(bool leavingBattle, bool hasUnresolvedCombatDebt)
    {
        return leavingBattle && hasUnresolvedCombatDebt;
    }

    public static int ResolvePendingCombatDebt(bool waitForCombatDebt, int dueDebtCount)
    {
        return waitForCombatDebt ? dueDebtCount : 0;
    }

    public static bool ShouldCompleteImmediately(bool waitForCombatDebt, float delaySeconds)
    {
        return !waitForCombatDebt && delaySeconds <= 0f;
    }
}
