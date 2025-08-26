// Assets/Scripts/Managers/GameEvents.cs
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 게임의 모든 주요 이벤트를 중앙에서 관리하는 정적 클래스입니다.
/// 클래스 간의 직접적인 참조를 줄이고, 이벤트 기반 아키텍처를 구축하는 데 사용됩니다.
/// </summary>
public static class GameEvents
{
    // --- 게임 흐름 이벤트 ---

    /// <summary>
    /// 게임 단계(준비, 전투 등)가 변경될 때 발생합니다.
    /// GameManagers가 이벤트를 발생시키고, 여러 UI 및 관리 클래스가 이를 구독합니다.
    /// </summary>
    public static event Action<GameManagers.GameState> OnGameStateChanged;
    public static void TriggerGameStateChanged(GameManagers.GameState newState) => OnGameStateChanged?.Invoke(newState);

    /// <summary>
    /// 새로운 라운드가 시작될 때 발생합니다.
    /// </summary>
    /// <param name="roundNumber">시작되는 라운드의 번호</param>
    public static event Action<int> OnRoundStart;
    public static void TriggerRoundStart(int roundNumber) => OnRoundStart?.Invoke(roundNumber);


    // --- 플레이어 상태 이벤트 ---

    /// <summary>
    /// 특정 플레이어의 체력이나 골드가 변경될 때 발생합니다.
    /// PlayerManager가 이벤트를 발생시키고, UI 관련 클래스가 구독합니다.
    /// </summary>
    /// <param name="playerID">해당 플레이어의 ID</param>
    /// <param name="newHealth">변경된 최종 체력</param>
    /// <param name="newGold">변경된 최종 골드</param>
    public static event Action<int, int, int> OnPlayerStatsChanged;
    public static void TriggerPlayerStatsChanged(int playerID, int newHealth, int newGold) => OnPlayerStatsChanged?.Invoke(playerID, newHealth, newGold);

    /// <summary>
    /// 플레이어가 설치/제거하여 보유한 벽의 개수가 변경될 때 발생합니다.
    /// </summary>
    /// <param name="playerID">해당 플레이어의 ID</param>
    /// <param name="newWallCount">변경된 최종 벽 개수</param>
    public static event Action<int, int> OnPlayerWallCountChanged;
    public static void TriggerPlayerWallCountChanged(int playerID, int newWallCount) => OnPlayerWallCountChanged?.Invoke(playerID, newWallCount);


    // --- 증강(Augment) 관련 이벤트 ---

    /// <summary>
    /// 증강체 선택 단계가 시작될 때 발생합니다.
    /// </summary>
    /// <param name="localPlayer">증강을 선택할 로컬 플레이어</param>
    /// <param name="augments">제시된 3개의 증강 데이터 리스트</param>
    public static event Action<PlayerManager, List<AugmentData>> OnAugmentPhaseStart;
    public static void TriggerAugmentPhaseStart(PlayerManager localPlayer, List<AugmentData> augments) => OnAugmentPhaseStart?.Invoke(localPlayer, augments);

    /// <summary>
    /// 플레이어가 증강체를 선택했을 때 발생합니다. UI가 이벤트를 발생시키고, AugmentManager가 처리합니다.
    /// </summary>
    /// <param name="localPlayer">증강을 선택한 플레이어</param>
    /// <param name="chosenAugment">선택된 증강 데이터</param>
    public static event Action<PlayerManager, AugmentData> OnAugmentSelected;
    public static void TriggerAugmentSelected(PlayerManager localPlayer, AugmentData chosenAugment) => OnAugmentSelected?.Invoke(localPlayer, chosenAugment);


    // --- 상점 및 배치 이벤트 ---

    /// <summary>
    /// 상점에서 유닛 구매 버튼을 눌렀을 때 발생합니다. UI가 이벤트를 발생시키고, PlayerManager가 처리합니다.
    /// </summary>
    /// <param name="localPlayer">유닛을 구매한 플레이어</param>
    /// <param name="unitData">구매한 유닛의 데이터</param>
    /// <param name="starLevel">구매한 유닛의 성급</param>
    public static event Action<PlayerManager, UnitData, int> OnUnitPurchased;
    public static void TriggerUnitPurchased(PlayerManager localPlayer, UnitData unitData, int starLevel) => OnUnitPurchased?.Invoke(localPlayer, unitData, starLevel);
    
    /// <summary>
    /// 필드에 벽이 성공적으로 설치되었을 때 발생합니다.
    /// </summary>
    /// <param name="playerID">벽을 설치한 플레이어 ID</param>
    /// <param name="gridPosition">설치된 그리드 좌표</param>
    public static event Action<int, Vector3Int> OnWallPlaced;
    public static void TriggerWallPlaced(int playerID, Vector3Int gridPosition) => OnWallPlaced?.Invoke(playerID, gridPosition);

    /// <summary>
    /// 필드에서 벽이 성공적으로 제거되었을 때 발생합니다.
    /// </summary>
    /// <param name="playerID">벽을 제거한 플레이어 ID</param>
    /// <param name="gridPosition">제거된 그리드 좌표</param>
    public static event Action<int, Vector3Int> OnWallRemoved;
    public static void TriggerWallRemoved(int playerID, Vector3Int gridPosition) => OnWallRemoved?.Invoke(playerID, gridPosition);
}