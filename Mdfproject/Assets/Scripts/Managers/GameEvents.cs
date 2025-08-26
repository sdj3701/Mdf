// Assets/Scripts/Managers/GameEvents.cs
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 게임의 모든 주요 이벤트를 중앙에서 관리하는 정적 클래스입니다.
/// </summary>
public static class GameEvents
{
    // --- 게임 흐름 이벤트 ---
    public static event Action<GameManagers.GameState> OnGameStateChanged;
    public static void TriggerGameStateChanged(GameManagers.GameState newState) => OnGameStateChanged?.Invoke(newState);

    public static event Action<int> OnRoundStart;
    public static void TriggerRoundStart(int roundNumber) => OnRoundStart?.Invoke(roundNumber);

    // --- 플레이어 상태 이벤트 ---
    public static event Action<int, int, int> OnPlayerStatsChanged;
    public static void TriggerPlayerStatsChanged(int playerID, int newHealth, int newGold) => OnPlayerStatsChanged?.Invoke(playerID, newHealth, newGold);

    public static event Action<int, int> OnPlayerWallCountChanged;
    public static void TriggerPlayerWallCountChanged(int playerID, int newWallCount) => OnPlayerWallCountChanged?.Invoke(playerID, newWallCount);

    // --- 증강(Augment) 관련 이벤트 ---
    public static event Action<PlayerManager, List<AugmentData>> OnAugmentPhaseStart;
    public static void TriggerAugmentPhaseStart(PlayerManager localPlayer, List<AugmentData> augments) => OnAugmentPhaseStart?.Invoke(localPlayer, augments);

    public static event Action<PlayerManager, AugmentData> OnAugmentSelected;
    public static void TriggerAugmentSelected(PlayerManager localPlayer, AugmentData chosenAugment) => OnAugmentSelected?.Invoke(localPlayer, chosenAugment);

    // --- 상점 및 배치 이벤트 ---
    public static event Action<PlayerManager, UnitData, int> OnUnitPurchased;
    public static void TriggerUnitPurchased(PlayerManager localPlayer, UnitData unitData, int starLevel) => OnUnitPurchased?.Invoke(localPlayer, unitData, starLevel);

    public static event Action<int, Vector3Int> OnWallPlaced;
    public static void TriggerWallPlaced(int playerID, Vector3Int gridPosition) => OnWallPlaced?.Invoke(playerID, gridPosition);

    public static event Action<int, Vector3Int> OnWallRemoved;
    public static void TriggerWallRemoved(int playerID, Vector3Int gridPosition) => OnWallRemoved?.Invoke(playerID, gridPosition);
    
    // --- [신규] 배치 모드 요청 이벤트 ---

    /// <summary>
    /// UI 등에서 특정 배치 모드(유닛, 벽)로 진입을 요청할 때 발생합니다.
    /// </summary>
    /// <param name="mode">요청하는 배치 모드</param>
    /// <param name="unitPrefab">유닛 배치일 경우, 배치할 유닛의 프리팹</param>
    public static event Action<PlacementMode, GameObject> OnPlacementModeEnterRequested;
    public static void TriggerPlacementModeEnterRequested(PlacementMode mode, GameObject unitPrefab = null) => OnPlacementModeEnterRequested?.Invoke(mode, unitPrefab);

    /// <summary>
    /// UI 등에서 모든 배치 모드를 취소(종료)할 것을 요청할 때 발생합니다.
    /// </summary>
    public static event Action OnPlacementModeExitRequested;
    public static void TriggerPlacementModeExitRequested() => OnPlacementModeExitRequested?.Invoke();
}