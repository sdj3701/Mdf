using UnityEngine;

public class MoveUnitCommand : ICommand
{
    public int PlayerId { get; set; }
    private Vector3Int _from;
    private Vector3Int _to;

    public MoveUnitCommand(int playerId, Vector3Int from, Vector3Int to)
    {
        this.PlayerId = playerId;
        this._from = from;
        this._to = to;
    }

    public void Execute()
    {
        var player = GameManagers.Instance.GetPlayer(PlayerId);
        if (player == null) return;

        player.fieldManager.MoveUnit(_from, _to);
    }
}
