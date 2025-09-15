public class SelectAugmentCommand : ICommand
{
    public int PlayerId { get; set; }
    private int _augmentIndex; // 0, 1, 2 중 선택

    public SelectAugmentCommand(int playerId, int augmentIndex)
    {
        this.PlayerId = playerId;
        this._augmentIndex = augmentIndex;
    }

    public void Execute()
    {
        var player = GameManagers.Instance.GetPlayer(PlayerId);
        if (player == null || player.augmentManager == null) return;
        
        var presentedAugments = player.augmentManager.GetPresentedAugments();
        if (_augmentIndex >= 0 && _augmentIndex < presentedAugments.Count)
        {
            var chosenAugment = presentedAugments[_augmentIndex];
            // AugmentManager의 로직을 직접 호출하여 증강을 선택하고 적용합니다.
            player.augmentManager.SelectAndApplyAugment(chosenAugment);
        }
    }
}
