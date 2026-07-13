using UnityEngine;

public class PlaceWallCommand : ICommand
{
    public int PlayerId { get; set; }
    public Vector3Int Position { get; private set; }
    public WallPlacementKind Kind { get; private set; }

    public PlaceWallCommand(int playerId, Vector3Int position, WallPlacementKind kind = WallPlacementKind.Destructible)
    {
        PlayerId = playerId;
        Position = position;
        Kind = kind;
    }

    public void Execute()
    {
        var gm = GameManagers.Instance;
        if (gm == null || gm.Runner == null || !gm.Runner.IsServer)
        {
            Debug.Log($"[PlaceWallCommand] Ignored on non-server peer. Player={PlayerId}, Pos={Position}");
            return;
        }
        if (gm.Object == null || !gm.Object.IsValid || !gm.Object.HasStateAuthority ||
            gm.currentState != GameManagers.GameState.Prepare || gm.IsSequenceTransitioning)
        {
            Debug.LogWarning($"[PlaceWallCommand] Rejected outside authoritative stable Prepare. Player={PlayerId}, Pos={Position}");
            return;
        }

        var player = gm.GetPlayer(PlayerId);
        if (player == null)
        {
            Debug.LogError($"[PlaceWallCommand] Player not found for PlayerId {PlayerId}");
            return;
        }
        if (player.Object == null || !player.Object.IsValid || !player.Object.HasStateAuthority)
        {
            return;
        }

        var fm = player.fieldManager;
        if (fm == null)
        {
            Debug.LogError($"[PlaceWallCommand] FieldManager is null for Player {PlayerId}");
            return;
        }

        if (!player.IsReadyForPlayerActions)
        {
            Debug.LogWarning($"[PlaceWallCommand] Player {PlayerId} is not ready for placement commands.");
            return;
        }

        if (fm.playerManager == null || fm.playerManager != player)
        {
            Debug.LogError($"[PlaceWallCommand] Field ownership mismatch. playerId={PlayerId}");
            return;
        }

        int fieldOwnerId = -1;
        if (fm.playerManager != null)
        {
            try
            {
                fieldOwnerId = fm.playerManager.playerId;
            }
            catch (System.InvalidOperationException)
            {
                fieldOwnerId = -1;
            }
        }

        bool hasInputAuthority = player.Object != null && player.Object.IsValid && player.Object.HasInputAuthority;
        string inputAuthority = player.Object != null && player.Object.IsValid ? player.Object.InputAuthority.ToString() : "invalid";
        Debug.Log($"[PlaceWallCommand] Execute request. cmdPlayer={PlayerId}, resolvedPlayer={player.playerId}, fieldOwner={fieldOwnerId}, pos={Position}, hasInputAuthority={hasInputAuthority}, inputAuthority={inputAuthority}");

        if (!fm.IsValidGridPosition(Position))
        {
            Debug.LogWarning($"[PlaceWallCommand] Invalid grid position {Position} for Player {PlayerId}");
            return;
        }

        if (fm.HasWallAt(Position))
        {
            Debug.LogWarning($"[PlaceWallCommand] Wall already exists at {Position} for Player {PlayerId}");
            return;
        }

        Unit occupant = fm.GetUnitAt(Position);
        if (occupant != null)
        {
            if (!IsOwnedByPlayer(player, occupant))
            {
                Debug.LogWarning($"[PlaceWallCommand] Cannot place wall over foreign unit at {Position} for Player {PlayerId}");
                return;
            }

            if (occupant.Data == null)
            {
                Debug.LogWarning($"[PlaceWallCommand] Cannot place wall over unresolved unit at {Position} for Player {PlayerId}");
                return;
            }
        }

        Vector3Int goalCell = fm.WorldToGridInt(player.goalTransform != null ? player.goalTransform.position : Vector3.zero);
        if (Position == goalCell)
        {
            Debug.LogWarning($"[PlaceWallCommand] Cannot place wall at goal cell {Position} for Player {PlayerId}");
            return;
        }

        bool isPermanent = Kind == WallPlacementKind.Permanent;
        bool stockReserved = isPermanent
            ? player.TryUsePermanentWallPlacement()
            : player.TryUseWall();
        if (!stockReserved)
        {
            Debug.LogWarning($"[PlaceWallCommand] No {Kind} wall stock left for Player {PlayerId}");
            return;
        }

        bool created;
        if (isPermanent)
        {
            created = fm.TryCreatePlayerPlacedPermanentWallAt(Position);
        }
        else
        {
            fm.CreateWallAt(Position);
            created = fm.GetWallAt(Position) != null;
        }

        if (created)
        {
            if (isPermanent)
            {
                player.NotifyPermanentWallLayoutChanged("place_player_permanent_wall");
            }
            Debug.Log($"[PlaceWallCommand] SUCCESS player={player.playerId}, fieldOwner={fieldOwnerId}, pos={Position}, kind={Kind}");
            gm.NotifyWallPlacementSucceeded(player.playerId, Position.x, Position.y);
        }
        else
        {
            Debug.LogError($"[PlaceWallCommand] Create wall failed at {Position} for Player {PlayerId}, kind={Kind}. Refunding.");
            if (isPermanent)
            {
                player.ReturnPermanentWallPlacement();
            }
            else
            {
                player.ReturnWall();
            }
        }
    }

    private static bool IsOwnedByPlayer(PlayerManager player, Unit unit)
    {
        return PlayerManager.IsUnitOwnedByPlayerForCommand(player, unit);
    }
}
