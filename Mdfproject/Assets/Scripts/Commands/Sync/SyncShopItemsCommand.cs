// Assets/Scripts/Commands/Sync/SyncShopItemsCommand.cs

using UnityEngine;
using Cysharp.Threading.Tasks;

/// <summary>
/// 서버에서 생성한 상점 아이템을 클라이언트에 동기화하는 커맨드
/// </summary>
public class SyncShopItemsCommand : ICommand
{
    public int PlayerId { get; set; }
    public string[] UnitDataNames { get; private set; }
    public int[] StarLevels { get; private set; }

    public SyncShopItemsCommand(int playerId, string[] unitDataNames, int[] starLevels)
    {
        PlayerId = playerId;
        UnitDataNames = unitDataNames ?? System.Array.Empty<string>();
        StarLevels = starLevels ?? System.Array.Empty<int>();
    }

    public async void Execute()
    {
        var gm = GameManagers.Instance;
        if (gm == null) return;

        // 서버는 이미 상점 아이템을 가지고 있으므로 무시
        if (gm.Object != null && gm.Object.HasStateAuthority) return;

        var player = gm.GetPlayer(PlayerId);
        if (player?.shopManager != null)
        {
            await player.shopManager.SetShopItemsFromServerAsync(UnitDataNames, StarLevels);
            Debug.Log($"<color=cyan>[SyncShopItemsCommand] Player {PlayerId}: {UnitDataNames.Length}개 상점 아이템 동기화 완료</color>");
        }
    }
}
