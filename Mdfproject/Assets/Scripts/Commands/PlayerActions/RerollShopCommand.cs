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
        if (player.TryGetShopSnapshot(
                out string[] names,
                out int[] stars,
                out _,
                out int revision,
                out int round))
        {
            player.RPC_SyncShopItems(names, stars, revision, round);
        }
        else
        {
            Debug.LogError($"[RerollShopCommand] Authoritative shop snapshot unavailable for P{PlayerId}.");
        }
        
        // Debug.Log($"[RerollShopCommand] Player {PlayerId}: 리롤 완료, {items.Count}개 아이템 동기화");
    }
}
