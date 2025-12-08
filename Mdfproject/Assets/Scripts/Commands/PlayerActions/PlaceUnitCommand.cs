using UnityEngine;

public class PlaceUnitCommand : ICommand
{
    public int PlayerId { get; set; }
    public UnitData UnitData { get; private set; }
    public Vector3Int Position { get; private set; }

    public PlaceUnitCommand(int playerId, UnitData unitData, Vector3Int position)
    {
        this.PlayerId = playerId;
        this.UnitData = unitData;
        this.Position = position;
    }

    public void Execute()
    {
        var player = GameManagers.Instance.GetPlayer(PlayerId);
        if (player == null) return;

        // FieldManager의 유닛 생성 로직을 직접 호출합니다.
        if (player.fieldManager != null)
        {
            player.fieldManager.CreateUnitAt(UnitData, Position, 1);
            player.fieldManager.CheckForCombination();
        }
    }
}
