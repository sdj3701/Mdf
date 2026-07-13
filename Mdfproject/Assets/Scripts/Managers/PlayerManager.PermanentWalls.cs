using Fusion;
using UnityEngine;

public partial class PlayerManager
{
    [Networked] public int PermanentWallPlacementCount { get; private set; }
    [Networked] public int PermanentWallStockRevision { get; private set; }
    [Networked] public int PermanentWallLayoutRevision { get; private set; }

    private void InitializePermanentWallStateOnSpawn(bool isHostMigration)
    {
        if (isHostMigration || !HasStateAuthorityOrNoNetwork())
        {
            return;
        }

        PermanentWallPlacementCount = 0;
        PermanentWallStockRevision = 0;
        PermanentWallLayoutRevision = 0;
    }

    public int GetPermanentWallPlacementCount() => Mathf.Max(0, PermanentWallPlacementCount);

    public void AddPermanentWallPlacementCount(int amount)
    {
        if (amount <= 0 || !HasStateAuthorityOrNoNetwork())
        {
            return;
        }

        PermanentWallPlacementCount = Mathf.Max(0, PermanentWallPlacementCount + amount);
        PermanentWallStockRevision++;
        PublishPermanentWallStockChangedIfOffline();
    }

    public bool TryUsePermanentWallPlacement()
    {
        if (!HasStateAuthorityOrNoNetwork() || PermanentWallPlacementCount <= 0)
        {
            return false;
        }

        PermanentWallPlacementCount--;
        PermanentWallStockRevision++;
        PublishPermanentWallStockChangedIfOffline();
        return true;
    }

    public void ReturnPermanentWallPlacement()
    {
        if (!HasStateAuthorityOrNoNetwork())
        {
            return;
        }

        PermanentWallPlacementCount++;
        PermanentWallStockRevision++;
        PublishPermanentWallStockChangedIfOffline();
    }

    public void NotifyPermanentWallLayoutChanged(string context)
    {
        if (!HasStateAuthorityOrNoNetwork())
        {
            return;
        }

        PermanentWallLayoutRevision++;
        BroadcastPermanentWallLayout(context);
    }

    public void BroadcastPermanentWallLayout(string context)
    {
        if (fieldManager == null)
        {
            return;
        }

        int[] payload = fieldManager.BuildPermanentWallSyncPayload(
            $"PlayerManager.BroadcastPermanentWallLayout.{context}");
        if (Object != null && Object.IsValid && Object.HasStateAuthority &&
            Runner != null && Runner.IsRunning && Runner.IsServer)
        {
            RPC_ApplyPermanentWalls(PermanentWallLayoutRevision, payload);
        }
    }

    public bool RestorePermanentWallStateAfterHostMigration(
        int placementCount,
        int stockRevision,
        int layoutRevision,
        string context)
    {
        if (Object == null || !Object.IsValid || !Object.HasStateAuthority)
        {
            return false;
        }

        PermanentWallPlacementCount = Mathf.Max(0, placementCount);
        PermanentWallStockRevision = Mathf.Max(0, stockRevision);
        PermanentWallLayoutRevision = Mathf.Max(0, layoutRevision);
        GameEvents.TriggerPlayerPermanentWallCountChanged(playerId, PermanentWallPlacementCount);
        Debug.Log($"[PlayerManager] HostMigration permanent wall state restored ({context}) P{playerId} stock={PermanentWallPlacementCount} stockRev={PermanentWallStockRevision} layoutRev={PermanentWallLayoutRevision}");
        return true;
    }

    private void PublishPermanentWallStockChangedIfOffline()
    {
        if (Runner == null || !Runner.IsRunning)
        {
            GameEvents.TriggerPlayerPermanentWallCountChanged(playerId, PermanentWallPlacementCount);
        }
    }
}
