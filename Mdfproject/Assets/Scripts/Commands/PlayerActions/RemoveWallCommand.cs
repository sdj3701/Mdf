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
        var player = GameManagers.Instance.GetPlayer(PlayerId);
        if (player == null) return;
        
        if (player.fieldManager != null && player.fieldManager.GetWallAt(Position) != null)
        {
            player.fieldManager.RemoveWallAt(Position);
            player.ReturnWall();
            GameEvents.TriggerWallRemovalSucceeded(player.playerId, Position);
        }
    }
}
