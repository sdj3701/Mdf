// Assets/Scripts/Managers/GameEvents.cs
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 게임의 모든 주요 이벤트를 중앙에서 관리하는 정적 클래스입니다.
/// </summary>
public static class GameEvents
{
    // --- 게임 매니저 준비 완료 이벤트 ---
    public static event Action OnGameManagersReady;
    public static void TriggerGameManagersReady() => OnGameManagersReady?.Invoke();

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

    /// <summary>
    /// 벽이 파괴(전투 중 몬스터/유닛에 의해)되었을 때
    /// 몬스터들이 경로를 재탐색하기 위해 사용
    /// </summary>
    public static event Action<Vector3Int, FieldManager> OnWallDestroyed;
    public static void TriggerWallDestroyed(Vector3Int gridPosition, FieldManager field) => OnWallDestroyed?.Invoke(gridPosition, field);

    // --- 공격 시퀀스 이벤트 ---
    /// <summary>
    /// 공격 몬스터 풀이 변경되었을 때 (UI 갱신용)
    /// </summary>
    public static event Action<int, List<MonsterPoolEntry>> OnMonsterPoolChanged;
    public static void TriggerMonsterPoolChanged(int playerID, List<MonsterPoolEntry> pool)
    {
        var handlers = OnMonsterPoolChanged;
        if (handlers == null)
        {
            return;
        }

        foreach (Action<int, List<MonsterPoolEntry>> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(playerID, pool);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GameEvents] OnMonsterPoolChanged 핸들러 예외: {handler.Method.DeclaringType?.Name}.{handler.Method.Name}");
                Debug.LogException(ex);
            }
        }
    }

    /// <summary>
    /// 전투 시퀀스가 시작되었을 때 (isAttacking: true면 공격, false면 수비)
    /// </summary>
    public static event Action<bool> OnBattleSequenceStarted;
    public static void TriggerBattleSequenceStarted(bool isAttacking) => OnBattleSequenceStarted?.Invoke(isAttacking);

    // ========== Host Migration 이벤트 ==========
    
    /// <summary>
    /// Host Migration 시작 시 발생 - UI 잠금 등 준비 작업용
    /// </summary>
    public static event Action OnHostMigrationStarted;
    public static void TriggerHostMigrationStarted() => OnHostMigrationStarted?.Invoke();
    
    /// <summary>
    /// Host Migration 완료 시 발생
    /// </summary>
    /// <param name="isNewHost">true면 새 Host가 됨, false면 일반 클라이언트</param>
    public static event Action<bool> OnHostMigrationCompleted;
    public static void TriggerHostMigrationCompleted(bool isNewHost) => OnHostMigrationCompleted?.Invoke(isNewHost);
    
    /// <summary>
    /// Host Migration 후 게임 상태 복원 완료 시 발생
    /// UI 갱신, 타이머 재시작 등 후처리용
    /// </summary>
    public static event Action<GameManagers.GameState> OnGameStateRestored;
    public static void TriggerGameStateRestored(GameManagers.GameState state) => OnGameStateRestored?.Invoke(state);
}
