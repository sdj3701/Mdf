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

        var player = gm.GetPlayer(PlayerId);
        if (player == null)
        {
            Debug.LogError($"[RemoveWallCommand] Player not found for PlayerId {PlayerId}");
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

        if (fm.GetWallAt(Position) == null)
        {
            Debug.LogWarning($"[RemoveWallCommand] No destructible wall at {Position} for Player {PlayerId}");
            return;
        }

        fm.RemoveWallAt(Position);
        if (fm.GetWallAt(Position) == null)
        {
            player.ReturnWall();
            gm.NotifyWallRemovalSucceeded(player.playerId, Position.x, Position.y);
            Debug.Log($"[RemoveWallCommand] SUCCESS player={player.playerId}, pos={Position}");
        }
        else
        {
            Debug.LogError($"[RemoveWallCommand] RemoveWallAt failed at {Position} for Player {PlayerId}. Wall stock not refunded.");
        }
    }
}
