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

    public static event Action<PlayerManager, AugmentData> OnAugmentApplied;
    public static void TriggerAugmentApplied(PlayerManager localPlayer, AugmentData chosenAugment) => OnAugmentApplied?.Invoke(localPlayer, chosenAugment);

    // --- 상점 및 배치 이벤트 ---
    // [수정됨] 구매 '요청'이 아닌 '성공 결과'를 알리는 이벤트. UI 업데이트 등 후처리에 사용됩니다.
    public static event Action<int, ShopItem, int> OnUnitPurchaseSucceeded; // playerID, 구매한 아이템, 상점 슬롯 인덱스
    public static void TriggerUnitPurchaseSucceeded(int playerID, ShopItem item, int slotIndex) => OnUnitPurchaseSucceeded?.Invoke(playerID, item, slotIndex);

    public static event Action<int, string> OnPurchaseFailed; // playerID, 실패 사유
    public static void TriggerPurchaseFailed(int playerID, string reason) => OnPurchaseFailed?.Invoke(playerID, reason);

    public static event Action<PlayerManager> OnShopRefreshed;
    public static void TriggerShopRefreshed(PlayerManager owner) => OnShopRefreshed?.Invoke(owner);

    public static event Action<int, Vector3Int> OnWallPlacementSucceeded;
    public static void TriggerWallPlacementSucceeded(int playerID, Vector3Int gridPosition) => OnWallPlacementSucceeded?.Invoke(playerID, gridPosition);

    public static event Action<int, Vector3Int> OnWallRemovalSucceeded;
    public static void TriggerWallRemovalSucceeded(int playerID, Vector3Int gridPosition) => OnWallRemovalSucceeded?.Invoke(playerID, gridPosition);
}
