// Assets/Scripts/Commands/Sync/SyncShopItemsCommand.cs

using UnityEngine;
using Cysharp.Threading.Tasks;

/// <summary>
/// 서버에서 생성한 상점 아이템을 클라이언트에 동기화하는 커맨드입니다.
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

    private async UniTask<PlayerManager> WaitForPlayerAsync(GameManagers gm)
    {
        const float timeoutSeconds = 6f;
        float waited = 0f;

        while (waited < timeoutSeconds)
        {
            var player = gm.GetPlayer(PlayerId);
            if (player != null)
            {
                return player;
            }

            await UniTask.Delay(100);
            waited += 0.1f;
        }

        return null;
    }

    public async void Execute()
    {
        var gm = GameManagers.Instance;
        if (gm == null) return;

        // 서버는 이미 원본 데이터를 가지고 있으므로 클라이언트에서만 적용
        if (gm.Object != null && gm.Object.HasStateAuthority) return;

        var player = await WaitForPlayerAsync(gm);
        if (player == null)
        {
            // Debug.LogWarning($"[SyncShopItemsCommand] Player {PlayerId} not ready. Sync skipped.");
            return;
        }

        if (player.shopManager == null)
        {
            player.shopManager = player.GetComponentInChildren<ShopManager>(true);
        }

        if (player.shopManager == null)
        {
            // Debug.LogWarning($"[SyncShopItemsCommand] Player {PlayerId} shopManager is null. Sync skipped.");
            return;
        }

        if (player.shopManager.playerManager == null)
        {
            player.shopManager.playerManager = player;
        }

        await player.shopManager.SetShopItemsFromServerAsync(UnitDataNames, StarLevels);
        // Debug.Log($"<color=cyan>[SyncShopItemsCommand] Player {PlayerId}: {UnitDataNames.Length}개 상점 아이템 동기화 완료</color>");
    }
}
