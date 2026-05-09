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

        if (gm == null || gm.GetGameState() != GameManagers.GameState.Prepare)
        {
            Reject("move_requires_prepare_phase");
            return;
        }

        if (gm.IsSequenceTransitioning)
        {
            Reject("move_blocked_during_sequence_transition");
            return;
        }

        var player = gm.GetPlayer(PlayerId);
        if (player == null || player.fieldManager == null)
        {
            Reject("move_player_or_field_missing");
            return;
        }

        FieldManager field = player.fieldManager;
        if (field.playerManager != null && field.playerManager.playerId != PlayerId)
        {
            Reject("field_ownership_mismatch");
            return;
        }

        if (!field.IsValidGridPosition(From) || !field.IsValidGridPosition(To) || From == To)
        {
            Reject("move_position_invalid");
            return;
        }

        bool hasFieldStateAuthority = player.Object != null && player.Object.HasStateAuthority;
        Unit sourceUnit = field.GetUnitAt(From);
        UnitData sourceUnitData = sourceUnit != null ? sourceUnit.Data : null;
        if (sourceUnit == null && !field.HasPendingUnitAt(From))
        {
            if (!hasFieldStateAuthority)
            {
                field.MoveUnit(From, To);
                return;
            }

            Reject("move_source_empty");
            return;
        }

        if (sourceUnit != null && !IsOwnedByPlayer(player, sourceUnit))
        {
            Reject("move_source_not_owned_by_player");
            return;
        }

        if (sourceUnit == null && field.TryGetPendingUnitDataAt(From, out var pendingUnitData))
        {
            sourceUnitData = pendingUnitData;
        }

        if (field.IsUnitAt(To))
        {
            Reject("move_destination_occupied");
            return;
        }

        if (sourceUnitData == null && field.HasWallAt(To) && hasFieldStateAuthority)
        {
            Reject("move_pending_unit_type_unknown_for_wall");
            return;
        }

        if (sourceUnitData != null && sourceUnitData.unitType == UnitType.Melee && field.HasWallAt(To))
        {
            Reject("melee_unit_cannot_move_to_wall");
            return;
        }

        field.MoveUnit(From, To);
    }

    private static bool IsOwnedByPlayer(PlayerManager player, Unit unit)
    {
        if (player == null || unit == null)
        {
            return false;
        }

        if (unit.Owner == player)
        {
            return true;
        }

        if (unit.Owner != null && unit.Owner.playerId == player.playerId)
        {
            return true;
        }

        return player.ownedUnits != null && player.ownedUnits.Contains(unit);
    }

    private void Reject(string reason)
    {
        Debug.LogWarning($"[MoveUnitCommand] rejected reason={reason} player={PlayerId} from={From} to={To}");
    }
}
