using UnityEngine;

public class RemoveWallCommand : ICommand
{
    public int PlayerId { get; set; }
    private Vector3Int _position;

    public RemoveWallCommand(int playerId, Vector3Int position)
    {
        this.PlayerId = playerId;
        this._position = position;
    }

    public void Execute()
    {
        var player = GameManagers.Instance.GetPlayer(PlayerId);
        if (player == null) return;
        
        if (player.fieldManager != null && player.fieldManager.GetWallAt(_position) != null)
        {
            player.fieldManager.RemoveWallAt(_position);
            player.ReturnWall();
            GameEvents.TriggerWallRemovalSucceeded(player.playerId, _position);
        }
    }
}
