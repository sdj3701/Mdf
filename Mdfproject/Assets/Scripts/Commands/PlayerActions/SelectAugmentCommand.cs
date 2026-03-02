using UnityEngine;

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
        var gm = GameManagers.Instance;
        if (gm == null || gm.Runner == null) return;
        
        // 서버에서만 실행 (중요!)
        if (!gm.Runner.IsServer)
        {
            // Debug.Log($"[SelectAugmentCommand] Client에서 무시됨. Player={PlayerId}");
            return;
        }
        
        var player = gm.GetPlayer(PlayerId);
        if (player == null || player.augmentManager == null) return;
        
        var presentedAugments = player.augmentManager.GetPresentedAugments();
        if (AugmentIndex >= 0 && AugmentIndex < presentedAugments.Count)
        {
            var chosenAugment = presentedAugments[AugmentIndex];
            // 서버에서 증강 선택 및 적용
            player.augmentManager.SelectAndApplyAugment(chosenAugment);
            
            // 모든 클라이언트에 알림 (Command Pattern 사용)
            gm.NotifyAugmentSelected(PlayerId, chosenAugment.augmentName);
            
            // Debug.Log($"[SelectAugmentCommand] Player {PlayerId}: '{chosenAugment.augmentName}' 선택 완료");
        }
    }
}
