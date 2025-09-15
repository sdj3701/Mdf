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
        if (player == null) return;

        var shopItems = player.shopManager.GetCurrentShopItems();
        if (_shopSlotIndex >= 0 && _shopSlotIndex < shopItems.Count)
        {
            var itemToBuy = shopItems[_shopSlotIndex];
            GameEvents.TriggerUnitPurchased(player, itemToBuy.UnitData, itemToBuy.StarLevel);
        }
    }
}
