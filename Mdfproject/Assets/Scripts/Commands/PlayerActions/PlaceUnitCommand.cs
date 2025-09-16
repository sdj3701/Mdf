using UnityEngine;

public class PlaceUnitCommand : ICommand
{
    public int PlayerId { get; set; }
    private UnitData _unitData;
    private Vector3Int _position;

    public PlaceUnitCommand(int playerId, UnitData unitData, Vector3Int position)
    {
        this.PlayerId = playerId;
        this._unitData = unitData;
        this._position = position;
    }

    public void Execute()
    {
        var player = GameManagers.Instance.GetPlayer(PlayerId);
        if (player == null) return;

        // FieldManager의 유닛 생성 로직을 직접 호출합니다.
        if (player.fieldManager != null)
        {
            player.fieldManager.CreateUnitAt(_unitData, _position, 1);
            player.fieldManager.CheckForCombination();
        }
    }
}
