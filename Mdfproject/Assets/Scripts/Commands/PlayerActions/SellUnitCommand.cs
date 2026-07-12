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
        if (gm == null || gm.Runner == null || !gm.Runner.IsServer || gm.Object == null ||
            !gm.Object.IsValid || !gm.Object.HasStateAuthority ||
            gm.currentState != GameManagers.GameState.Prepare || gm.IsSequenceTransitioning) return;

        var player = gm.GetPlayer(PlayerId);
        if (player == null || player.fieldManager == null || player.Object == null ||
            !player.Object.IsValid || !player.Object.HasStateAuthority) return;

        player.fieldManager.TrySellUnitAt(Position);
    }
}
