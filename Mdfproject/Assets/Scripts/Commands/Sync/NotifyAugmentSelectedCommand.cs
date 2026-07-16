// Assets/Scripts/Commands/Sync/NotifyAugmentSelectedCommand.cs

using UnityEngine;

/// <summary>
/// 증강 선택을 클라이언트에 알리는 커맨드
/// </summary>
public class NotifyAugmentSelectedCommand : ICommand
{
    public int PlayerId { get; set; }
    public string AugmentContentId { get; private set; }

    public NotifyAugmentSelectedCommand(int playerId, string augmentContentId)
    {
        PlayerId = playerId;
        AugmentContentId = StableDataKeyUtility.NormalizeContentId(augmentContentId);
    }

    public void Execute()
    {
        var gm = GameManagers.Instance;
        if (gm == null) return;

        var player = gm.GetPlayer(PlayerId);
        if (player?.augmentManager == null) return;

        var chosenAugment = player.augmentManager.FindAugmentByContentId(AugmentContentId);
        if (chosenAugment == null)
        {
            // Debug.LogWarning($"[NotifyAugmentSelectedCommand] Unknown augment id '{AugmentContentId}'.");
            return;
        }

        player.augmentManager.ApplyAuthoritativeSelectionNotification();
        GameEvents.TriggerAugmentApplied(player, chosenAugment);
        // Debug.Log($"<color=green>[NotifyAugmentSelectedCommand] Player {PlayerId}: '{AugmentContentId}' selected</color>");
    }
}
