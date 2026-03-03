using Fusion;
using UnityEngine;

public partial class GameManagers
{
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
}
