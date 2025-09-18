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
        if (player == null) return;

        if (player.TryUseWall())
        {
            if (player.fieldManager != null)
            {
                player.fieldManager.CreateWallAt(Position);
                GameEvents.TriggerWallPlacementSucceeded(player.playerId, Position);
            }
        }
    }
}
