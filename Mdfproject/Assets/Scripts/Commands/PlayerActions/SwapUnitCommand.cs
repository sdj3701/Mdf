using UnityEngine;

public class SwapUnitCommand : ICommand
{
    public int PlayerId { get; set; }
    public Vector3Int PosA { get; private set; }
    public Vector3Int PosB { get; private set; }

    public SwapUnitCommand(int playerId, Vector3Int posA, Vector3Int posB)
    {
        this.PlayerId = playerId;
        this.PosA = posA;
        this.PosB = posB;
    }

    public void Execute()
    {
        var gm = GameManagers.Instance;
        bool isClient = gm != null && gm.Runner != null && gm.Runner.IsRunning && !gm.Runner.IsServer;
        if (isClient)
        {
            Debug.Log($"<color=#3399FF>[ClientFlow] Execute SwapUnit {PosA} <-> {PosB} (Player {PlayerId})</color>");
        }
        var player = GameManagers.Instance.GetPlayer(PlayerId);
        if (player == null) return;
        var fm = player.fieldManager;
        if (fm == null) return;

        var unitA = fm.GetUnitAt(PosA);
        var unitB = fm.GetUnitAt(PosB);
        if (unitA == null || unitB == null) return;

        bool destWallForA = fm.HasWallAt(PosB);
        bool destWallForB = fm.HasWallAt(PosA);

        if (unitA.Data.unitType == UnitType.Melee && destWallForA) return;
        if (unitB.Data.unitType == UnitType.Melee && destWallForB) return;

        if (!fm.IsValidGridPosition(PosA) || !fm.IsValidGridPosition(PosB)) return;

        fm.SwapUnits(PosA, PosB);
    }
}
