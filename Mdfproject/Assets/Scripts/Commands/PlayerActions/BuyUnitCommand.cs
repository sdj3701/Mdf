using UnityEngine;
using AI.BehaviorTree.Nodes.Actions;
public class BuyUnitCommand : ICommand
{
    public int PlayerId { get; set; }
    public int ShopSlotIndex { get; private set; }

    public BuyUnitCommand(int playerId, int shopSlotIndex)
    {
        this.PlayerId = playerId;
        this.ShopSlotIndex = shopSlotIndex;
    }

    public void Execute()
    {
        var gm = GameManagers.Instance;
        if (gm == null || gm.Runner == null || !gm.Runner.IsServer)
        {
            // Debug.Log($"[BuyUnitCommand] Ignored on non-server peer. Player={PlayerId}, Slot={ShopSlotIndex}");
            return;
        }

        var player = gm.GetPlayer(PlayerId);
        if (player == null || player.shopManager == null) return;

        var shopItems = player.shopManager.GetCurrentShopItems();
        if (ShopSlotIndex < 0 || ShopSlotIndex >= shopItems.Count) return;

        var itemToBuy = shopItems[ShopSlotIndex];
        

        // 기존 PlayerManager의 구매 로직을 이곳으로 가져옵니다.
        if (player.SpendGold(itemToBuy.CalculatedCost))
        {
            
            player.AddUnit(itemToBuy.UnitData, itemToBuy.StarLevel);

            // 상점의 상태를 갱신합니다.
            player.shopManager.MarkSlotAsPurchased(ShopSlotIndex);

            // ⭐ 서버에서 모든 피어에게 '구매 성공'을 네트워크로 알립니다 (Command Pattern 사용)
            gm.NotifyPurchaseSucceeded(PlayerId, ShopSlotIndex);
        }
        else
        {
            // Debug.Log($"Player {PlayerId}: 골드가 부족하여 구매에 실패했습니다.");
            // (선택적) 골드 부족 이벤트 발생
            GameEvents.TriggerPurchaseFailed(PlayerId, "골드 부족");
        }
    }
}
