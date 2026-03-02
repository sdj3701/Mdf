using Fusion;
using UnityEngine;

public partial class GameManagers
{
    /// <summary>
    /// 상태 전이를 단일 경로로 관리합니다.
    /// </summary>
    /// <param name="nextState">전이할 상태</param>
    /// <param name="reason">전이 이유(로그)</param>
    /// <param name="raiseStateChangedEvent">상태 변경 이벤트 발행 여부</param>
    private void TransitionToState(GameState nextState, string reason, bool raiseStateChangedEvent = false)
    {
        var prevState = currentState;
        bool changed = prevState != nextState;

        if (changed)
        {
            currentState = nextState;
            // Debug.Log($"[GameManagers] State Transition: {prevState} -> {nextState} ({reason})");
        }

        if (raiseStateChangedEvent)
        {
            GameEvents.TriggerGameStateChanged(nextState);
        }
    }

    private void TransitionToSetupState(string reason)
    {
        TransitionToState(GameState.Setup, reason);
    }

    private void TransitionToPrepareState(string reason, bool raiseStateChangedEvent = true)
    {
        TransitionToState(GameState.Prepare, reason, raiseStateChangedEvent);
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
