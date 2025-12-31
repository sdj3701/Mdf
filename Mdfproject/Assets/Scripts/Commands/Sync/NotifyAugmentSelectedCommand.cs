// Assets/Scripts/Commands/Sync/NotifyAugmentSelectedCommand.cs

using UnityEngine;
using System.Linq;

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
        if (player != null)
        {
            // 증강 데이터 찾기
            var augments = player.augmentManager?.GetPresentedAugments();
            AugmentData chosenAugment = augments?.FirstOrDefault(a => a?.augmentName == AugmentName);

            // 이벤트 트리거 (UI 닫기 등)
            if (chosenAugment != null)
            {
                GameEvents.TriggerAugmentApplied(player, chosenAugment);
                Debug.Log($"<color=green>[NotifyAugmentSelectedCommand] Player {PlayerId}: '{AugmentName}' 선택 알림</color>");
            }
        }
    }
}
