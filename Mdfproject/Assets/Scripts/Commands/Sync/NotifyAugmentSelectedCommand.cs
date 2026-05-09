// Assets/Scripts/Commands/Sync/NotifyAugmentSelectedCommand.cs

using UnityEngine;

/// <summary>
/// 증강 선택을 클라이언트에 알리는 커맨드
/// </summary>
public class NotifyAugmentSelectedCommand : ICommand
{
    public int PlayerId { get; set; }
    public string AugmentName { get; private set; }

    public NotifyAugmentSelectedCommand(int playerId, string augmentName)
    {
        PlayerId = playerId;
        AugmentName = augmentName ?? string.Empty;
    }

    public void Execute()
    {
        var gm = GameManagers.Instance;
        if (gm == null) return;

        var player = gm.GetPlayer(PlayerId);
        if (player?.augmentManager == null) return;

        var chosenAugment = player.augmentManager.FindAugmentByName(AugmentName);
        if (chosenAugment == null)
        {
            // Debug.LogWarning($"[NotifyAugmentSelectedCommand] 증강 '{AugmentName}'을 찾을 수 없습니다.");
            return;
        }

        bool isServer = gm.Runner != null && gm.Runner.IsServer;

        GameEvents.TriggerAugmentApplied(player, chosenAugment);
        // Debug.Log($"<color=green>[NotifyAugmentSelectedCommand] Player {PlayerId}: '{AugmentName}' 선택 알림</color>");
    }
}
