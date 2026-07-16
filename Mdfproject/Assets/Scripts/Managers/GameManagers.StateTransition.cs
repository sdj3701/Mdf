using Fusion;
using Cysharp.Threading.Tasks;
using UnityEngine;

public partial class GameManagers
{
    private const float CombatExitDebtRetrySeconds = 0.1f;
    private bool _sequenceTransitionCompletionStarted;
    [Networked] private NetworkBool SequenceTransitionSideEffectsApplied { get; set; }
    [Networked] private NetworkBool CombatExitDebtGateActive { get; set; }
    [Networked] private int CombatExitDebtPendingCount { get; set; }
    [Networked] private NetworkBool CombatExitDebtTerminalFailureSafeStop { get; set; }

    public bool IsCombatExitDebtGateActive => CombatExitDebtGateActive;
    public int UnresolvedCombatExitDebtCount => CombatExitDebtPendingCount;
    public bool IsCombatExitDebtTerminalFailureSafeStopped => CombatExitDebtTerminalFailureSafeStop;

    /// <summary>
    /// Host Migration snapshot gap을 줄이기 위해 중요 전환 직전 수동 snapshot push를 시도합니다.
    /// </summary>
    private void TryPushMigrationSnapshotForCriticalTransition(string reason)
    {
        if (Runner == null || Object == null || !Object.HasStateAuthority)
        {
            return;
        }

        HostMigrationHandler.Instance?.TryPushHostMigrationSnapshot(Runner, reason);
    }

    /// <summary>
    /// 상태 전이를 단일 경로로 관리합니다.
    /// </summary>
    /// <param name="nextState">전이할 상태</param>
    /// <param name="reason">전이 이유(로그)</param>
    private void TransitionToState(GameState nextState, string reason)
    {
        var prevState = currentState;
        bool changed = prevState != nextState;

        if (changed)
        {
            currentState = nextState;
            // Debug.Log($"[GameManagers] State Transition: {prevState} -> {nextState} ({reason})");
        }

    }

    private void TransitionToSetupState(string reason)
    {
        TransitionToState(GameState.Setup, reason);
    }

    private void TransitionToPrepareState(string reason)
    {
        TransitionToState(GameState.Prepare, reason);
    }

    private void TransitionToBattle1State(string reason)
    {
        TransitionToState(GameState.Battle1, reason);
    }

    private void TransitionToBattle2State(string reason)
    {
        TransitionToState(GameState.Battle2, reason);
    }

    private void TransitionToGameOverState(string reason)
    {
        TransitionToState(GameState.GameOver, reason);
    }

    private float GetSequenceTransitionDelaySeconds()
    {
        return Mathf.Max(0f, sequenceTransitionDelaySeconds);
    }

    internal static float ResolveDisplayedPhaseTime(float phaseRemaining, bool sequenceTransitioning)
    {
        return MatchFlowPolicy.ResolveDisplayedPhaseTime(phaseRemaining, sequenceTransitioning);
    }

    internal static GameState? ResolveExpiredPhaseTransitionTarget(
        GameState state,
        bool sequenceTransitioning,
        bool roundTransitioning)
    {
        if (sequenceTransitioning)
        {
            return null;
        }

        switch (state)
        {
            case GameState.Prepare:
                return GameState.Battle1;
            case GameState.Battle1:
                return GameState.Battle2;
            case GameState.Battle2:
                return roundTransitioning ? (GameState?)null : GameState.Prepare;
            default:
                return null;
        }
    }

    private void BeginSequenceTransition(GameState nextState, string reason)
    {
        if (Runner == null || Object == null || !Object.HasStateAuthority)
        {
            return;
        }

        if (MatchFlowPolicy.ShouldIgnoreTransitionRequest(
                currentState == GameState.GameOver,
                currentState == nextState,
                IsSequenceTransitioning))
        {
            return;
        }

        GameState fromState = currentState;
        if (IsBattleSequenceState(fromState) &&
            TryGetCombatExitTerminalFailure(out string terminalFailureReason))
        {
            phaseTimer = TickTimer.None;
            TransitionFromState = fromState;
            TransitionToStateTarget = nextState;
            SequenceTransitionSideEffectsApplied = false;
            CombatExitDebtGateActive = false;
            CombatExitDebtPendingCount = 0;
            sequenceTransitionTimer = TickTimer.None;
            IsSequenceTransitioning = true;
            _sequenceTransitionCompletionStarted = false;
            EnterCombatExitTerminalFailureSafeStop(fromState, nextState, terminalFailureReason);
            return;
        }

        float delaySeconds = GetSequenceTransitionDelaySeconds();
        int dueDebtCount = 0;
        bool leavingBattle = IsBattleSequenceState(fromState);
        bool hasUnresolvedCombatDebt = leavingBattle &&
                                       TryGetUnresolvedCombatExitDebt(out dueDebtCount);
        bool waitForCombatDebt = MatchFlowPolicy.ShouldWaitForCombatDebt(
            leavingBattle,
            hasUnresolvedCombatDebt);

        phaseTimer = TickTimer.None;
        TransitionFromState = fromState;
        TransitionToStateTarget = nextState;
        SequenceTransitionSideEffectsApplied = false;
        CombatExitDebtTerminalFailureSafeStop = false;
        CombatExitDebtGateActive = waitForCombatDebt;
        CombatExitDebtPendingCount = MatchFlowPolicy.ResolvePendingCombatDebt(
            waitForCombatDebt,
            dueDebtCount);
        sequenceTransitionTimer = waitForCombatDebt
            ? TickTimer.CreateFromSeconds(Runner, CombatExitDebtRetrySeconds)
            : delaySeconds > 0f
                ? TickTimer.CreateFromSeconds(Runner, delaySeconds)
                : TickTimer.None;
        IsSequenceTransitioning = true;
        _sequenceTransitionCompletionStarted = false;

        if (!waitForCombatDebt)
        {
            ApplySequenceTransitionStartSideEffects(fromState, nextState, reason);
            SequenceTransitionSideEffectsApplied = true;
        }
        else
        {
            Debug.LogWarning(
                $"[GameManagers] Sequence transition is waiting for due zone debt. " +
                $"from={fromState},to={nextState},dueTicks={dueDebtCount},reason={reason}");
        }
        Debug.Log($"[GameManagers] Sequence transition started: {fromState} -> {nextState}, delay={delaySeconds:F1}s, reason={reason}");

        if (MatchFlowPolicy.ShouldCompleteImmediately(waitForCombatDebt, delaySeconds))
        {
            CompleteSequenceTransition();
        }
    }

    private void CompleteSequenceTransition()
    {
        if (Runner == null || Object == null || !Object.HasStateAuthority)
        {
            return;
        }

        if (!IsSequenceTransitioning || _sequenceTransitionCompletionStarted)
        {
            return;
        }

        GameState fromState = TransitionFromState;
        GameState targetState = TransitionToStateTarget;

        if (IsBattleSequenceState(fromState) &&
            TryGetCombatExitTerminalFailure(out string terminalFailureReason))
        {
            EnterCombatExitTerminalFailureSafeStop(fromState, targetState, terminalFailureReason);
            return;
        }

        if (!SequenceTransitionSideEffectsApplied)
        {
            if (TryGetUnresolvedCombatExitDebt(out int dueDebtCount))
            {
                CombatExitDebtGateActive = true;
                CombatExitDebtPendingCount = Mathf.Max(1, dueDebtCount);
                sequenceTransitionTimer = TickTimer.CreateFromSeconds(Runner, CombatExitDebtRetrySeconds);
                return;
            }

            CombatExitDebtGateActive = false;
            CombatExitDebtPendingCount = 0;
            ApplySequenceTransitionStartSideEffects(fromState, targetState, "CombatExitDebtDrained");
            SequenceTransitionSideEffectsApplied = true;

            float delaySeconds = GetSequenceTransitionDelaySeconds();
            if (delaySeconds > 0f)
            {
                sequenceTransitionTimer = TickTimer.CreateFromSeconds(Runner, delaySeconds);
                return;
            }
        }

        _sequenceTransitionCompletionStarted = true;

        if (targetState == GameState.Prepare)
        {
            RunLifecycleTask(
                CompletePrepareSequenceTransitionAsync(fromState, targetState),
                "SequenceTransition/CompletePrepare");
            return;
        }

        bool transitionCompleted = true;
        try
        {
            switch (targetState)
            {
                case GameState.Battle1:
                    transitionCompleted = StartBattle1Phase();
                    break;
                case GameState.Battle2:
                    transitionCompleted = StartBattle2Phase();
                    break;
                case GameState.GameOver:
                    TransitionToGameOverState("SequenceTransition");
                    break;
                default:
                    TransitionToState(targetState, "SequenceTransition");
                    break;
            }
        }
        finally
        {
            if (transitionCompleted)
            {
                EndSequenceTransition(fromState, targetState);
            }
        }
    }

    private async UniTask CompletePrepareSequenceTransitionAsync(GameState fromState, GameState targetState)
    {
        try
        {
            await StartNextRound();
        }
        finally
        {
            EndSequenceTransition(fromState, targetState);
        }
    }

    private void EndSequenceTransition(GameState fromState, GameState targetState)
    {
        sequenceTransitionTimer = TickTimer.None;
        CombatExitDebtGateActive = false;
        CombatExitDebtPendingCount = 0;
        CombatExitDebtTerminalFailureSafeStop = false;
        SequenceTransitionSideEffectsApplied = false;
        IsSequenceTransitioning = false;
        _sequenceTransitionCompletionStarted = false;

        if (targetState == GameState.Prepare)
        {
            isTransitioningRound = false;
        }

        Debug.Log($"[GameManagers] Sequence transition ended: {fromState} -> {targetState}, state={currentState}");
    }

    private void ApplySequenceTransitionStartSideEffects(GameState fromState, GameState targetState, string reason)
    {
        if (fromState == GameState.Battle1 && targetState == GameState.Battle2)
        {
            CleanupCombatForSequenceTransition(reason);
        }
        else if (fromState == GameState.Battle2 && targetState == GameState.Prepare)
        {
            CleanupCombatForSequenceTransition(reason);
            RespawnUnitsForSequenceTransition(reason);
            CameraManager.Instance?.ReturnToOwnField();
        }
    }

    private void CleanupCombatForSequenceTransition(string reason)
    {
        foreach (var player in AllPlayers)
        {
            if (player == null)
            {
                continue;
            }

            player.SetFightingState(false);
            player.monsterSpawner?.OnCombatPhaseEnded();

            var attackSeqMgr = player.GetComponent<AttackSequenceManager>();
            if (attackSeqMgr != null)
            {
                attackSeqMgr.EndAttackSequence();
            }
        }
    }

    private static bool IsBattleSequenceState(GameState state)
    {
        return state == GameState.Battle1 || state == GameState.Battle2;
    }

    private static bool TryGetUnresolvedCombatExitDebt(out int dueTickCount)
    {
        CombatScheduler scheduler = CombatScheduler.Instance;
        if (scheduler == null)
        {
            dueTickCount = 0;
            return false;
        }

        return scheduler.HasUnresolvedDueZoneDebt(out dueTickCount);
    }

    private static bool TryGetCombatExitTerminalFailure(out string reason)
    {
        CombatScheduler scheduler = CombatScheduler.Instance;
        if (scheduler == null)
        {
            reason = null;
            return false;
        }

        return scheduler.TryGetZoneDebtTerminalFailure(out reason);
    }

    private void EnterCombatExitTerminalFailureSafeStop(
        GameState fromState,
        GameState targetState,
        string reason)
    {
        sequenceTransitionTimer = TickTimer.None;
        CombatExitDebtGateActive = false;
        CombatExitDebtPendingCount = 0;
        if (!CombatExitDebtTerminalFailureSafeStop)
        {
            Debug.LogError(
                $"[GameManagers] Combat exit entered terminal safe-stop. " +
                $"from={fromState},to={targetState},reason={reason}");
        }

        CombatExitDebtTerminalFailureSafeStop = true;
    }

    private void RespawnUnitsForSequenceTransition(string reason)
    {
        foreach (var player in AllPlayers)
        {
            player?.fieldManager?.RespawnAllUnits();
            player?.fieldManager?.BroadcastAuthoritativeUnitRoster("SequenceTransition.RespawnUnits");
        }
    }

    private void HandleNetworkSequenceTransitionChange()
    {
        if (!_isSpawned)
        {
            return;
        }

        if (IsSequenceTransitioning)
        {
            float durationSeconds = GetSequenceTransitionDelaySeconds();
            GameEvents.TriggerGameStateTransitionStarted(
                TransitionFromState,
                TransitionToStateTarget,
                durationSeconds);
            RunLifecycleTask(
                BattleTransitionUI.PlaySequenceTransitionAsync(
                    TransitionFromState,
                    TransitionToStateTarget,
                    durationSeconds),
                "SequenceTransition/PlayTransitionUI");
        }
        else
        {
            GameEvents.TriggerGameStateTransitionEnded(
                TransitionFromState,
                TransitionToStateTarget);
        }
    }
}
