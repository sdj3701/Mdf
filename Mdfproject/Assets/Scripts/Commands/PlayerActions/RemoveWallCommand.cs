using UnityEngine;

public class RemoveWallCommand : ICommand
{
    public int PlayerId { get; set; }
    public Vector3Int Position { get; private set; }

    public RemoveWallCommand(int playerId, Vector3Int position)
    {
        this.PlayerId = playerId;
        this.Position = position;
    }

    public void Execute()
    {
        var gm = GameManagers.Instance;
        if (gm == null || gm.Runner == null || !gm.Runner.IsServer)
        {
            Debug.Log($"[RemoveWallCommand] Ignored on non-server peer. Player={PlayerId}, Pos={Position}");
            return;
        }
        if (gm.Object == null || !gm.Object.IsValid || !gm.Object.HasStateAuthority ||
            gm.currentState != GameManagers.GameState.Prepare || gm.IsSequenceTransitioning)
        {
            Debug.LogWarning($"[RemoveWallCommand] Rejected outside authoritative stable Prepare. Player={PlayerId}, Pos={Position}");
            return;
        }

        var player = gm.GetPlayer(PlayerId);
        if (player == null)
        {
            Debug.LogError($"[RemoveWallCommand] Player not found for PlayerId {PlayerId}");
            return;
        }
        if (player.Object == null || !player.Object.IsValid || !player.Object.HasStateAuthority)
        {
            return;
        }

        var fm = player.fieldManager;
        if (fm == null)
        {
            Debug.LogError($"[RemoveWallCommand] FieldManager is null for Player {PlayerId}");
            return;
        }

        if (!player.IsReadyForPlayerActions)
        {
            Debug.LogWarning($"[RemoveWallCommand] Player {PlayerId} is not ready for wall removal.");
            return;
        }

        if (fm.playerManager == null || fm.playerManager != player)
        {
            Debug.LogError($"[RemoveWallCommand] Field ownership mismatch. playerId={PlayerId}");
            return;
        }

        if (!fm.IsValidGridPosition(Position))
        {
            Debug.LogWarning($"[RemoveWallCommand] Invalid grid position {Position} for Player {PlayerId}");
            return;
        }

        DestructibleWall destructibleWall = fm.GetWallAt(Position);
        bool hasDestructibleWall = destructibleWall != null;
        bool hasPlayerPermanentWall = fm.IsPlayerPlacedPermanentWallAt(Position);
        if (!hasDestructibleWall && !hasPlayerPermanentWall)
        {
            Debug.LogWarning($"[RemoveWallCommand] No removable wall at {Position} for Player {PlayerId}");
            return;
        }

        int upgradeRefund = hasDestructibleWall
            ? Mathf.Max(0, destructibleWall.InvestedUpgradeGold)
            : 0;
        bool removed;
        if (hasPlayerPermanentWall)
        {
            removed = fm.TryRemovePlayerPlacedPermanentWallAt(Position);
        }
        else
        {
            fm.RemoveWallAt(Position);
            removed = fm.GetWallAt(Position) == null;
        }

        if (removed)
        {
            if (hasPlayerPermanentWall)
            {
                player.ReturnPermanentWallPlacement();
                player.NotifyPermanentWallLayoutChanged("remove_player_permanent_wall");
            }
            else
            {
                player.ReturnWall();
                player.AddGold(upgradeRefund);
            }
            gm.NotifyWallRemovalSucceeded(player.playerId, Position.x, Position.y);
            Debug.Log($"[RemoveWallCommand] SUCCESS player={player.playerId}, pos={Position}, kind={(hasPlayerPermanentWall ? WallPlacementKind.Permanent : WallPlacementKind.Destructible)}, upgradeRefund={upgradeRefund}");
        }
        else
        {
            Debug.LogError($"[RemoveWallCommand] Remove wall failed at {Position} for Player {PlayerId}. Wall stock not refunded.");
        }
    }
}
