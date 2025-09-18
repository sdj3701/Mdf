public class SelectAugmentCommand : ICommand
{
    public int PlayerId { get; set; }
    public int AugmentIndex { get; private set; } // 0, 1, 2 중 선택

    public SelectAugmentCommand(int playerId, int augmentIndex)
    {
        this.PlayerId = playerId;
        this.AugmentIndex = augmentIndex;
    }

    public void Execute()
    {
        var player = GameManagers.Instance.GetPlayer(PlayerId);
        if (player == null || player.augmentManager == null) return;
        
        var presentedAugments = player.augmentManager.GetPresentedAugments();
        if (AugmentIndex >= 0 && AugmentIndex < presentedAugments.Count)
        {
            var chosenAugment = presentedAugments[AugmentIndex];
            // AugmentManager의 로직을 직접 호출하여 증강을 선택하고 적용합니다.
            player.augmentManager.SelectAndApplyAugment(chosenAugment);
        }
    }
}
