using Fusion;
using Cysharp.Threading.Tasks;
using UnityEngine;

public partial class GameManagers
{
    private bool _sequenceTransitionCompletionStarted;

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

    private void BeginSequenceTransition(GameState nextState, string reason)
    {
        if (Runner == null || Object == null || !Object.HasStateAuthority)
        {
            return;
        }

        if (currentState == GameState.GameOver || currentState == nextState || IsSequenceTransitioning)
        {
            return;
        }

        GameState fromState = currentState;
        float delaySeconds = GetSequenceTransitionDelaySeconds();

        phaseTimer = TickTimer.None;
        TransitionFromState = fromState;
        TransitionToStateTarget = nextState;
        sequenceTransitionTimer = delaySeconds > 0f
            ? TickTimer.CreateFromSeconds(Runner, delaySeconds)
            : TickTimer.None;
        IsSequenceTransitioning = true;
        _sequenceTransitionCompletionStarted = false;

        ApplySequenceTransitionStartSideEffects(fromState, nextState, reason);
        Debug.Log($"[GameManagers] Sequence transition started: {fromState} -> {nextState}, delay={delaySeconds:F1}s, reason={reason}");

        if (delaySeconds <= 0f)
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

        _sequenceTransitionCompletionStarted = true;
        GameState fromState = TransitionFromState;
        GameState targetState = TransitionToStateTarget;

        if (targetState == GameState.Prepare)
        {
            RunLifecycleTask(
                CompletePrepareSequenceTransitionAsync(fromState, targetState),
                "SequenceTransition/CompletePrepare");
            return;
        }

        try
        {
            switch (targetState)
            {
                case GameState.Battle1:
                    StartBattle1Phase();
                    break;
                case GameState.Battle2:
                    StartBattle2Phase();
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
            EndSequenceTransition(fromState, targetState);
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

        if (CameraManager.Instance != null)
        {
            CameraManager.Instance.ReturnToOwnField();
        }
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
