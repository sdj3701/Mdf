using UnityEngine;

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
            
            // UI 갱신을 위해 성공 이벤트를 발생시킵니다.
            GameEvents.TriggerUnitPurchaseSuccess(PlayerId, _shopSlotIndex);
        }
        else
        {
            Debug.Log($"Player {PlayerId}: 골드가 부족하여 구매에 실패했습니다.");
        }
    }
}
