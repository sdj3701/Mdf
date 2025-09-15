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
    public static event Action<int, int> OnUnitPurchaseSuccess; // playerID, slotIndex
    public static void TriggerUnitPurchaseSuccess(int playerID, int slotIndex) => OnUnitPurchaseSuccess?.Invoke(playerID, slotIndex);

    public static event Action<PlayerManager> OnShopRefreshed;
    public static void TriggerShopRefreshed(PlayerManager owner) => OnShopRefreshed?.Invoke(owner);

    public static event Action<int, Vector3Int> OnWallPlaced;
    public static void TriggerWallPlaced(int playerID, Vector3Int gridPosition) => OnWallPlaced?.Invoke(playerID, gridPosition);

    public static event Action<int, Vector3Int> OnWallRemoved;
    public static void TriggerWallRemoved(int playerID, Vector3Int gridPosition) => OnWallRemoved?.Invoke(playerID, gridPosition);
}
