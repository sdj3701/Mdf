using UnityEngine;
using AI.BehaviorTree.Nodes.Actions;
public class BuyUnitCommand : ICommand
{
    public int PlayerId { get; set; }
    private int _shopSlotIndex;

    public BuyUnitCommand(int playerId, int shopSlotIndex)
    {
        this.PlayerId = playerId;
        this._shopSlotIndex = shopSlotIndex;
    }

    public void Execute()
    {
        var player = GameManagers.Instance.GetPlayer(PlayerId);
        if (player == null || player.shopManager == null) return;

        var shopItems = player.shopManager.GetCurrentShopItems();
        if (_shopSlotIndex < 0 || _shopSlotIndex >= shopItems.Count) return;

        var itemToBuy = shopItems[_shopSlotIndex];

        // 기존 PlayerManager의 구매 로직을 이곳으로 가져옵니다.
        if (player.SpendGold(itemToBuy.CalculatedCost))
        {
            player.AddUnit(itemToBuy.UnitData, itemToBuy.StarLevel);

            // AI 플레이어인 경우, 배치할 유닛 목록에 추가 (임시)
            if (ComponentRegistry.Has<AIPlayerController>(PlayerId.ToString()))
            {
                PlaceBestUnitAction.AddTempUnplacedUnit(itemToBuy.UnitData);
            }

            // 상점의 상태를 갱신합니다.
            player.shopManager.MarkSlotAsPurchased(_shopSlotIndex);
            
            // ⭐ 핵심: 여기서 "결과" 이벤트를 발생시킵니다!
            GameEvents.TriggerUnitPurchaseSucceeded(PlayerId, itemToBuy, _shopSlotIndex);
        }
        else
        {
            Debug.Log($"Player {PlayerId}: 골드가 부족하여 구매에 실패했습니다.");
            // (선택적) 골드 부족 이벤트 발생
            GameEvents.TriggerPurchaseFailed(PlayerId, "골드 부족");
        }
    }
}
