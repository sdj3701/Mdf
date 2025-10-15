using UnityEngine;

public class PlaceWallCommand : ICommand
{
    public int PlayerId { get; set; }
    public Vector3Int Position { get; private set; }

    public PlaceWallCommand(int playerId, Vector3Int position)
    {
        this.PlayerId = playerId;
        this.Position = position;
    }

    public void Execute()
    {
        var player = GameManagers.Instance.GetPlayer(PlayerId);
        if (player == null)
        {
            Debug.LogError($"[PlaceWallCommand] Player not found for PlayerId {PlayerId}");
            return;
        }

        var fm = player.fieldManager;
        if (fm == null)
        {
            Debug.LogError($"[PlaceWallCommand] FieldManager is null for Player {PlayerId}");
            return;
        }

        if (!fm.IsValidGridPosition(Position))
        {
            Debug.LogWarning($"[PlaceWallCommand] Invalid grid position {Position} for Player {PlayerId}");
            return;
        }

        if (fm.GetWallAt(Position) != null)
        {
            Debug.LogWarning($"[PlaceWallCommand] Wall already exists at {Position} for Player {PlayerId}");
            return;
        }

        if (!player.TryUseWall())
        {
            Debug.LogWarning($"[PlaceWallCommand] No wall stock left for Player {PlayerId}");
            return;
        }

        fm.CreateWallAt(Position);

        if (fm.GetWallAt(Position) != null)
        {
            GameEvents.TriggerWallPlacementSucceeded(player.playerId, Position);
        }
        else
        {
            Debug.LogError($"[PlaceWallCommand] CreateWallAt failed at {Position} for Player {PlayerId}. Refunding.");
            player.ReturnWall();
        }
    }
}
