using UnityEngine;
using System.Collections.Generic;

public class AIPlayerController : MonoBehaviour
{
    private PlayerManager _playerManager;
    private CommandProcessor _commandProcessor;
    private float _decisionTimer = 0f;
    private float _decisionCooldown = 1.0f; // 1초마다 의사결정

    public void Initialize(PlayerManager playerManager, CommandProcessor commandProcessor)
    {
        _playerManager = playerManager;
        _commandProcessor = commandProcessor;
    }


    void Update()
    {
        if (_playerManager == null) return;

        _decisionTimer += Time.deltaTime;
        if (_decisionTimer < _decisionCooldown) return;
        _decisionTimer = 0f;

        // 게임 상태에 따라 다른 의사결정 로직 실행
        var gameState = GameManagers.Instance.GetGameState();
        switch (gameState)
        {
            case GameManagers.GameState.Prepare:
                MakePreparePhaseDecisions();
                break;
            case GameManagers.GameState.Combat:
                MakeCombatPhaseDecisions();
                break;
        }
    }

    private void MakePreparePhaseDecisions()
    {
        // 1. 증강 선택 단계인지 확인
        if (_playerManager.augmentManager.GetPresentedAugments().Count > 0)
        {
            // TODO: 어떤 증강이 좋은지 평가하는 로직
            int bestAugmentIndex = 0; // 예: 일단 첫 번째 것 선택
            _commandProcessor.ExecuteCommand(new SelectAugmentCommand(_playerManager.playerId, bestAugmentIndex));
            return; // 증강 선택 후 다른 행동은 다음 틱에
        }

        // 2. 상점 확인 및 유닛 구매 결정
        // TODO: 현재 골드, 필드 상황, 상점 목록을 보고 구매할 유닛 결정하는 로직
        int slotToBuy = DecideWhichUnitToBuy();
        if (slotToBuy != -1)
        {
            _commandProcessor.ExecuteCommand(new BuyUnitCommand(_playerManager.playerId, slotToBuy));
        }
        
        // 3. 유닛 재배치 결정
        // TODO: 현재 필드 유닛들의 배치를 평가하고 최적의 위치로 옮기는 로직
    }

    private void MakeCombatPhaseDecisions()
    {
        // TODO: 수동 스킬을 가진 유닛들을 찾고, 적절한 타이밍에 스킬 사용 결정
        // _commandProcessor.ExecuteCommand(new ActivateSkillCommand(...));
    }

    private int DecideWhichUnitToBuy()
    {
        // 상점 매니저로부터 구매 가능한 아이템 목록을 가져옵니다.
        var availableItems = _playerManager.shopManager.GetAvailableShopItems();

        // 구매 가능한 아이템 중에서 첫 번째로 살 수 있는 것을 선택합니다.
        foreach (var itemPair in availableItems)
        {
            int slotIndex = itemPair.Key;
            var item = itemPair.Value;
            
            if (_playerManager.GetGold() >= item.CalculatedCost)
            {
                return slotIndex; // 구매할 슬롯 인덱스를 반환합니다.
            }
        }
        
        return -1; // 구매할 유닛이 없으면 -1을 반환합니다.
    }
}
