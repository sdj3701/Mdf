using UnityEngine;

public class PlaceWallCommand : ICommand
{
    public int PlayerId { get; set; }
    private Vector3Int _position;

    public PlaceWallCommand(int playerId, Vector3Int position)
    {
        this.PlayerId = playerId;
        this._position = position;
    }

    public void Execute()
    {
        var player = GameManagers.Instance.GetPlayer(PlayerId);
        if (player == null) return;

        if (player.TryUseWall())
        {
            if (player.fieldManager != null)
            {
                player.fieldManager.CreateWallAt(_position);
                GameEvents.TriggerWallPlaced(player.playerId, _position);
            }
        }
    }
}
