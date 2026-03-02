using System.Linq;
using UnityEngine;

public class RerollShopCommand : ICommand
{
    public int PlayerId { get; set; }

    public RerollShopCommand(int playerId)
    {
        this.PlayerId = playerId;
    }

    public void Execute()
    {
        var gm = GameManagers.Instance;
        if (gm == null || gm.Runner == null) return;
        
        // 서버에서만 실행 (중요!)
        if (!gm.Runner.IsServer)
        {
            // Debug.Log($"[RerollShopCommand] Client에서 무시됨. Player={PlayerId}");
            return;
        }
        
        var player = gm.GetPlayer(PlayerId);
        if (player == null || player.shopManager == null) return;
        
        // 서버에서 리롤 실행
        player.shopManager.Reroll();
        
        // RPC로 모든 클라이언트에 상점 아이템 동기화
        var items = player.shopManager.GetCurrentShopItems();
        string[] names = items.Select(i => i.UnitData?.name ?? "").ToArray();
        int[] stars = items.Select(i => i.StarLevel).ToArray();
        player.RPC_SyncShopItems(names, stars);
        
        // Debug.Log($"[RerollShopCommand] Player {PlayerId}: 리롤 완료, {items.Count}개 아이템 동기화");
    }
}
