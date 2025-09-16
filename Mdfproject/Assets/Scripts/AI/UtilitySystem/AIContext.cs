namespace AI.UtilitySystem
{
    // AI가 결정을 내릴 때 필요한 모든 정보(맥락)를 담는 클래스
    public class AIContext
    {
        public PlayerManager Player { get; }
        public ShopItem CurrentShopItem { get; private set; }

        public AIContext(PlayerManager player)
        {
            this.Player = player;
        }
        
        public AIContext(PlayerManager player, ShopItem shopItem)
        {
            this.Player = player;
            this.CurrentShopItem = shopItem;
        }
    }
}
