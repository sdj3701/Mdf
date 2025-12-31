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
        if (player == null) return;

        var fm = player.fieldManager;
        if (fm != null && fm.GetWallAt(Position) != null)
        {
            fm.RemoveWallAt(Position);
            player.ReturnWall();
            gm.NotifyWallRemovalSucceeded(player.playerId, Position.x, Position.y);
        }
    }
}
