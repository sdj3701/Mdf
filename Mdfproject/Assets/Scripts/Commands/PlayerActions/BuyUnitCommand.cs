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

        gm.NotifyPurchaseSucceeded(PlayerId, ShopSlotIndex);
        return CommandExecutionResult.Completed();
    }
}
