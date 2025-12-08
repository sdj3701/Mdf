using UnityEngine;

public class MoveUnitCommand : ICommand
{
    public int PlayerId { get; set; }
    public Vector3Int From { get; private set; }
    public Vector3Int To { get; private set; }

    public MoveUnitCommand(int playerId, Vector3Int from, Vector3Int to)
    {
        this.PlayerId = playerId;
        this.From = from;
        this.To = to;
    }

    public void Execute()
    {
        var gm = GameManagers.Instance;
        bool isClient = gm != null && gm.Runner != null && gm.Runner.IsRunning && !gm.Runner.IsServer;
        if (isClient)
        {
            Debug.Log($"<color=#3399FF>[ClientFlow] Execute MoveUnit {From} -> {To} (Player {PlayerId})</color>");
        }
        var player = GameManagers.Instance.GetPlayer(PlayerId);
        if (player == null) return;

        player.fieldManager.MoveUnit(From, To);
    }
}
