using UnityEngine;

public class SellUnitCommand : ICommand
{
    public int PlayerId { get; set; }
    public Vector3Int Position { get; private set; }

    public SellUnitCommand(int playerId, Vector3Int position)
    {
        PlayerId = playerId;
        Position = position;
    }

    public void Execute()
    {
        var gm = GameManagers.Instance;
        if (gm == null) return;

        var player = gm.GetPlayer(PlayerId);
        if (player == null || player.fieldManager == null) return;

        player.fieldManager.TrySellUnitAt(Position);
    }
}
