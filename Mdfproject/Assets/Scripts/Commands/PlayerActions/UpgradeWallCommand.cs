using UnityEngine;

public sealed class UpgradeWallCommand : ICommand
{
    public int PlayerId { get; set; }
    public Vector3Int Position { get; }
    public int ExpectedCurrentLevel { get; }

    public UpgradeWallCommand(int playerId, Vector3Int position, int expectedCurrentLevel)
    {
        PlayerId = playerId;
        Position = position;
        ExpectedCurrentLevel = expectedCurrentLevel;
    }

    public void Execute()
    {
        GameManagers gm = GameManagers.Instance;
        if (!TryValidate(
                gm,
                PlayerId,
                Position,
                ExpectedCurrentLevel,
                requireStateAuthority: true,
                out PlayerManager player,
                out DestructibleWall wall,
                out int upgradeCost,
                out string reason))
        {
            Debug.LogWarning($"[UpgradeWallCommand] Rejected. player={PlayerId}, pos={Position}, expectedLevel={ExpectedCurrentLevel}, reason={reason}");
            return;
        }

        if (!player.SpendGold(upgradeCost))
        {
            Debug.LogWarning($"[UpgradeWallCommand] Gold reservation failed. player={PlayerId}, pos={Position}, cost={upgradeCost}");
            return;
        }

        if (!wall.TryUpgradeFromAuthority(ExpectedCurrentLevel, upgradeCost, out reason))
        {
            player.AddGold(upgradeCost);
            Debug.LogError($"[UpgradeWallCommand] Upgrade commit failed and gold was rolled back. player={PlayerId}, pos={Position}, cost={upgradeCost}, reason={reason}");
            return;
        }

        Debug.Log($"[UpgradeWallCommand] SUCCESS player={PlayerId}, pos={Position}, level={wall.CurrentLevel}, cost={upgradeCost}, invested={wall.InvestedUpgradeGold}");
    }

    public static bool TryValidate(
        GameManagers gm,
        int playerId,
        Vector3Int position,
        int expectedCurrentLevel,
        bool requireStateAuthority,
        out PlayerManager player,
        out DestructibleWall wall,
        out int upgradeCost,
        out string reason)
    {
        player = null;
        wall = null;
        upgradeCost = 0;
        if (gm == null || gm.Runner == null || !gm.Runner.IsServer)
        {
            reason = "wall_upgrade_server_required";
            return false;
        }
        if (requireStateAuthority && (gm.Object == null || !gm.Object.IsValid || !gm.Object.HasStateAuthority))
        {
            reason = "wall_upgrade_game_state_authority_required";
            return false;
        }
        if (gm.currentState != GameManagers.GameState.Prepare || gm.IsSequenceTransitioning)
        {
            reason = "wall_upgrade_requires_stable_prepare";
            return false;
        }

        player = gm.GetPlayer(playerId);
        if (player == null || !player.IsReadyForPlayerActions)
        {
            reason = "wall_upgrade_player_not_ready";
            return false;
        }
        if (requireStateAuthority &&
            (player.Object == null || !player.Object.IsValid || !player.Object.HasStateAuthority))
        {
            reason = "wall_upgrade_player_state_authority_required";
            return false;
        }

        FieldManager field = player.fieldManager;
        if (field == null || field.playerManager != player)
        {
            reason = "wall_upgrade_field_ownership_mismatch";
            return false;
        }
        if (!field.IsValidGridPosition(position))
        {
            reason = "wall_upgrade_position_out_of_range";
            return false;
        }

        wall = field.GetWallAt(position);
        if (wall == null || wall.OwnerFieldManager != field)
        {
            reason = "wall_upgrade_destructible_wall_missing";
            return false;
        }
        if (!wall.TryGetUpgradeQuote(expectedCurrentLevel, out _, out upgradeCost, out reason))
        {
            return false;
        }
        if (player.GetGold() < upgradeCost)
        {
            reason = "wall_upgrade_insufficient_gold";
            return false;
        }

        reason = string.Empty;
        return true;
    }
}
