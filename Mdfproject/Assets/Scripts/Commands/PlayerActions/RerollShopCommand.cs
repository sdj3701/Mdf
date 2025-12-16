public class RerollShopCommand : ICommand
{
    public int PlayerId { get; set; }

    public RerollShopCommand(int playerId)
    {
        this.PlayerId = playerId;
    }

    public void Execute()
    {
        var player = GameManagers.Instance.GetPlayer(PlayerId);
        if (player == null) return;
        
        player.shopManager.Reroll();
    }
}
