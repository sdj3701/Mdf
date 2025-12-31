// Assets/Scripts/Commands/Sync/RequestSyncDataCommand.cs

using UnityEngine;
using System.Linq;

/// <summary>
/// 클라이언트가 서버에 데이터 동기화를 요청하는 커맨드
/// </summary>
public class RequestSyncDataCommand : ICommand
{
    public int PlayerId { get; set; }

    public RequestSyncDataCommand(int playerId)
    {
        PlayerId = playerId;
    }

    public void Execute()
    {
        var gm = GameManagers.Instance;
        if (gm == null) return;

        // 서버만 처리
        if (gm.Object == null || !gm.Object.HasStateAuthority) return;

        var player = gm.GetPlayer(PlayerId);
        if (player == null) return;

        Debug.Log($"<color=yellow>[RequestSyncDataCommand] Player {PlayerId}에게 데이터 동기화 요청 수신</color>");

        // 상점 동기화
        if (player.shopManager != null)
        {
            var items = player.shopManager.GetCurrentShopItems();
            if (items.Count > 0)
            {
                string[] shopNames = items.Select(i => i.UnitData?.name ?? "").ToArray();
                int[] shopStars = items.Select(i => i.StarLevel).ToArray();
                
                var syncShopCommand = new SyncShopItemsCommand(PlayerId, shopNames, shopStars);
                gm.CommandProcessor.RequestCommandExecution(syncShopCommand);
            }
        }

        // 증강체 동기화
        if (player.augmentManager != null)
        {
            var augments = player.augmentManager.GetPresentedAugments();
            if (augments.Count > 0)
            {
                string[] augNames = augments.Select(a => a?.augmentName ?? "").ToArray();
                
                var syncAugmentCommand = new SyncAugmentsCommand(PlayerId, augNames);
                gm.CommandProcessor.RequestCommandExecution(syncAugmentCommand);
            }
        }
    }
}
