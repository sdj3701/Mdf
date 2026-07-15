using UnityEngine;
using Cysharp.Threading.Tasks;
using System.Threading;

public class BuyUnitCommand : ICommand, IAsyncCommand
{
    public int PlayerId { get; set; }
    public int ShopSlotIndex { get; private set; }

    public BuyUnitCommand(int playerId, int shopSlotIndex)
    {
        this.PlayerId = playerId;
        this.ShopSlotIndex = shopSlotIndex;
    }

    public void Execute()
    {
        ExecuteAsync(CancellationToken.None).Forget();
    }

    public async UniTask<CommandExecutionResult> ExecuteAsync(CancellationToken cancellationToken)
    {
        var gm = GameManagers.Instance;
        if (gm == null || gm.Runner == null || !gm.Runner.IsServer)
        {
            return CommandExecutionResult.Completed();
        }

        var player = gm.GetPlayer(PlayerId);
        if (player == null || player.shopManager == null)
        {
            return CommandExecutionResult.Failed("player_or_shop_missing");
        }

        cancellationToken.ThrowIfCancellationRequested();
        PlayerManager.PurchaseUnitResult result = await player.TryPurchaseShopUnitAsync(ShopSlotIndex, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (!result.Succeeded)
        {
            string reason = string.IsNullOrEmpty(result.FailureReason)
                ? "purchase_failed"
                : result.FailureReason;
            gm.NotifyPurchaseFailed(PlayerId, ShopSlotIndex, reason);
            return CommandExecutionResult.Failed(reason);
        }

        // The sold flag lives in the durable Networked shop snapshot, while each peer also
        // keeps a non-networked ShopManager cache for presentation. Reconcile that cache after
        // every committed purchase just as reroll already does; the success notification below
        // remains the immediate UI event and is idempotent with this snapshot application.
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
            Debug.LogError($"[BuyUnitCommand] Authoritative shop snapshot unavailable after purchase for P{PlayerId}.");
        }

        gm.NotifyPurchaseSucceeded(PlayerId, ShopSlotIndex);
        return CommandExecutionResult.Completed();
    }
}
