// Assets/Scripts/Managers/GameStateManager.cs
// [State Machine Pattern]
// 게임 상태 전환을 관리하는 전용 클래스
// - 상태 전환 규칙 정의
// - Host Migration 후 상태 복원 지원

using System;
using System.Collections.Generic;
using UnityEngine;
using static GameManagers;

/// <summary>
/// [State Machine Pattern]
/// 게임 상태 전환을 캡슐화하여 관리하는 클래스
/// 
/// 사용법:
/// 1. GameManagers.Spawned()에서 초기화
/// 2. TryTransition()으로 상태 전환 시도
/// 3. OnStateTransition 이벤트로 상태 변경 감지
/// 4. Host Migration 후 ForceRestoreState()로 상태 복원
/// </summary>
public class GameStateManager
{
    // 상태 전환 규칙 정의 (from -> to 가능한 상태 목록)
    private readonly Dictionary<GameState, List<GameState>> _validTransitions;
    
    // 상태별 기본 지속 시간 (초)
    private readonly Dictionary<GameState, Func<float>> _phaseDurations;
    
    // 현재 상태
    public GameState CurrentState { get; private set; }
    
    /// <summary>
    /// 상태 전환 시 발생하는 이벤트
    /// </summary>
    /// <param name="oldState">이전 상태</param>
    /// <param name="newState">새 상태</param>
    public event Action<GameState, GameState> OnStateTransition;
    
    /// <summary>
    /// GameStateManager 초기화
    /// </summary>
    /// <param name="initialState">초기 상태 (보통 Setup)</param>
    /// <param name="getDurations">상태별 시간 설정 가져오기 함수 (prepareTime, combatTime)</param>
    public GameStateManager(GameState initialState, Func<(float prepare, float combat)> getDurations)
    {
        CurrentState = initialState;
        
        // 전환 규칙 초기화
        _validTransitions = new Dictionary<GameState, List<GameState>>
        {
            { GameState.Setup, new List<GameState> { GameState.DataLoading } },
            { GameState.DataLoading, new List<GameState> { GameState.Prepare } },
            { GameState.Prepare, new List<GameState> { GameState.Battle1 } },
            { GameState.Battle1, new List<GameState> { GameState.Battle2 } },
            { GameState.Battle2, new List<GameState> { GameState.Prepare, GameState.GameOver } },
            { GameState.GameOver, new List<GameState>() }  // 종료 상태 - 전환 없음
        };
        
        // 상태별 기본 시간 설정
        _phaseDurations = new Dictionary<GameState, Func<float>>
        {
            { GameState.Prepare, () => getDurations().prepare },
            { GameState.Battle1, () => getDurations().combat },
            { GameState.Battle2, () => getDurations().combat }
        };
    }
    
    /// <summary>
    /// 상태 전환을 시도합니다.
    /// </summary>
    /// <param name="newState">전환할 상태</param>
    /// <returns>성공 여부</returns>
    public bool TryTransition(GameState newState)
    {
        if (!IsValidTransition(CurrentState, newState))
        {
            Debug.LogWarning($"[GameStateManager] 잘못된 전환 시도: {CurrentState} → {newState}");
            return false;
        }
        
        var oldState = CurrentState;
        CurrentState = newState;
        
        Debug.Log($"<color=green>[GameStateManager] 상태 전환: {oldState} → {newState}</color>");
        
        // 이벤트 발생
        OnStateTransition?.Invoke(oldState, newState);
        
        return true;
    }
    
    /// <summary>
    /// 유효한 상태 전환인지 검사합니다.
    /// </summary>
    public bool IsValidTransition(GameState from, GameState to)
    {
        if (!_validTransitions.TryGetValue(from, out var validTargets))
            return false;
        return validTargets.Contains(to);
    }
    
    /// <summary>
    /// 다음 전환 가능한 상태들을 반환합니다.
    /// </summary>
    public List<GameState> GetValidNextStates()
    {
        return _validTransitions.TryGetValue(CurrentState, out var states) 
            ? new List<GameState>(states) 
            : new List<GameState>();
    }
    
    /// <summary>
    /// 현재 상태의 기본 지속 시간을 반환합니다.
    /// </summary>
    /// <returns>초 단위 시간, 없으면 0</returns>
    public float GetPhaseDuration(GameState? state = null)
    {
        var targetState = state ?? CurrentState;
        return _phaseDurations.TryGetValue(targetState, out var getDuration) 
            ? getDuration() 
            : 0f;
    }
    
    /// <summary>
    /// [Host Migration용] 상태를 강제로 설정하고 이벤트를 발생시킵니다.
    /// 일반 게임 흐름에서는 TryTransition() 사용을 권장합니다.
    /// </summary>
    /// <param name="state">복원할 상태</param>
    public void ForceRestoreState(GameState state)
    {
        var oldState = CurrentState;
        CurrentState = state;
        
        Debug.Log($"<color=yellow>[GameStateManager] 상태 강제 복원: {oldState} → {state}</color>");
        
        // 복원 시에도 이벤트 발생 (UI 갱신 등을 위해)
        OnStateTransition?.Invoke(oldState, state);
    }
    
    /// <summary>
    /// 현재 상태가 전투 상태인지 확인합니다.
    /// </summary>
    public bool IsInBattle => CurrentState == GameState.Battle1 || CurrentState == GameState.Battle2;
    
    /// <summary>
    /// 게임이 종료 상태인지 확인합니다.
    /// </summary>
    public bool IsGameOver => CurrentState == GameState.GameOver;
}
