using Fusion;
using UnityEngine;

/// <summary>
/// Validates client-originated player commands against authoritative runtime state.
/// It never mutates command payloads or gameplay state; the RPC entry point remains in PlayerManager.
/// </summary>
public sealed class PlayerCommandRequestValidator
{
    private readonly PlayerManager _player;

    public PlayerCommandRequestValidator(PlayerManager player)
    {
        _player = player;
    }

    public bool Validate(
        CommandType type,
        int[] intParams,
        string[] stringParams,
        Vector3[] vectorParams,
        RpcInfo info,
        out string reason)
    {
        intParams = intParams ?? System.Array.Empty<int>();
        stringParams = stringParams ?? System.Array.Empty<string>();
        vectorParams = vectorParams ?? System.Array.Empty<Vector3>();
        _ = stringParams;

        bool hasStateAuthority = _player != null
            && _player.Object != null
            && _player.Object.IsValid
            && _player.Object.HasStateAuthority;
        bool hasRpcSource = info.Source != PlayerRef.None;
        bool sourceMatches = hasStateAuthority && _player.Object.InputAuthority == info.Source;
        if (!ValidateEnvelope(
                hasStateAuthority,
                hasRpcSource,
                sourceMatches,
                _player != null && _player.IsReadyForPlayerActions,
                _player != null ? _player.playerId : -1,
                intParams,
                out reason))
        {
            return false;
        }

        GameManagers gm = GameManagers.Instance;
        if (gm == null)
        {
            gm = UnityEngine.Object.FindObjectOfType<GameManagers>();
        }

        if (gm == null || gm.Runner == null || !gm.Runner.IsServer || gm.Object == null || !gm.Object.HasStateAuthority)
        {
            reason = "game_managers_not_authoritative";
            return false;
        }

        switch (type)
        {
            case CommandType.BuyUnit:
                return ValidateBuyUnitRequest(gm, intParams, out reason);
            case CommandType.MoveUnit:
                return ValidateMoveUnitRequest(gm, vectorParams, out reason);
            case CommandType.SwapUnit:
                return ValidateSwapUnitRequest(gm, vectorParams, out reason);
            case CommandType.SellUnit:
                return ValidateSellUnitRequest(gm, vectorParams, out reason);
            case CommandType.PlaceUnit:
                reason = "place_unit_requires_authoritative_inventory";
                return false;
            case CommandType.PlaceWall:
                return ValidatePlaceWallRequest(gm, intParams, vectorParams, out reason);
            case CommandType.RemoveWall:
                return ValidateRemoveWallRequest(gm, vectorParams, out reason);
            case CommandType.RerollShop:
                return ValidateRerollShopRequest(gm, out reason);
            case CommandType.SelectAugment:
                return ValidateSelectAugmentRequest(gm, intParams, out reason);
            case CommandType.ActivateSkill:
                return ValidateActivateSkillRequest(gm, intParams, out reason);
            case CommandType.SetSkillActivationMode:
                return ValidateSetSkillActivationModeRequest(gm, intParams, out reason);
            case CommandType.RequestSyncData:
                reason = null;
                return true;
            default:
                reason = $"server_only_or_unknown_command:{type}";
                return false;
        }
    }

    public static bool ValidateEnvelope(
        bool hasStateAuthority,
        bool hasRpcSource,
        bool sourceMatchesInputAuthority,
        bool playerReady,
        int authoritativePlayerId,
        int[] intParams,
        out string reason)
    {
        if (!hasStateAuthority)
        {
            reason = "player_missing_state_authority";
            return false;
        }

        if (!hasRpcSource)
        {
            reason = "missing_rpc_source";
            return false;
        }

        if (!sourceMatchesInputAuthority)
        {
            reason = "rpc_source_not_input_authority";
            return false;
        }

        if (!playerReady)
        {
            reason = "player_not_ready";
            return false;
        }

        if (intParams == null || intParams.Length == 0)
        {
            reason = "missing_player_id";
            return false;
        }

        if (intParams[0] != authoritativePlayerId)
        {
            reason = $"player_id_mismatch:{intParams[0]}";
            return false;
        }

        reason = null;
        return true;
    }

    public static bool IsUnitOwnedByPlayer(PlayerManager player, Unit unit)
    {
        if (unit == null || player == null)
        {
            return false;
        }

        if (unit.Owner != null)
        {
            return unit.Owner == player || unit.Owner.playerId == player.playerId;
        }

        int rosterOwnerId = unit.OwnerPlayerIdForRoster;
        if (rosterOwnerId >= 0)
        {
            return rosterOwnerId == player.playerId;
        }

        return player.ownedUnits != null && player.ownedUnits.Contains(unit);
    }

    private bool ValidatePreparePhase(GameManagers gm, out string reason)
    {
        if (gm == null || gm.currentState != GameManagers.GameState.Prepare)
        {
            reason = "command_requires_prepare_phase";
            return false;
        }

        if (gm.IsSequenceTransitioning)
        {
            reason = "command_blocked_during_sequence_transition";
            return false;
        }

        reason = null;
        return true;
    }

    private bool ValidateBuyUnitRequest(GameManagers gm, int[] intParams, out string reason)
    {
        if (!ValidatePreparePhase(gm, out reason)) return false;
        if (intParams.Length < 2)
        {
            reason = "missing_shop_slot";
            return false;
        }

        if (_player.shopManager == null || !_player.shopManager.IsDatabaseLoaded)
        {
            reason = "shop_not_ready";
            return false;
        }

        int slotIndex = intParams[1];
        var items = _player.shopManager.GetCurrentShopItems();
        if (slotIndex < 0 || slotIndex >= items.Count)
        {
            reason = "shop_slot_out_of_range";
            return false;
        }

        if (_player.shopManager.IsSlotSold(slotIndex))
        {
            reason = "shop_slot_already_sold";
            return false;
        }

        if (_player.IsShopPurchaseTransactionPending(slotIndex))
        {
            reason = "shop_slot_purchase_pending";
            return false;
        }

        ShopItem item = items[slotIndex];
        if (item.UnitData == null)
        {
            reason = "shop_item_missing_unit_data";
            return false;
        }

        if (_player.GetGold() < item.CalculatedCost)
        {
            reason = "insufficient_gold";
            return false;
        }

        reason = null;
        return true;
    }

    private bool ValidateSetSkillActivationModeRequest(GameManagers gm, int[] intParams, out string reason)
    {
        if (gm == null || gm.IsSequenceTransitioning ||
            (gm.currentState != GameManagers.GameState.Prepare &&
             gm.currentState != GameManagers.GameState.Battle1 &&
             gm.currentState != GameManagers.GameState.Battle2))
        {
            reason = "skill_activation_mode_phase_invalid";
            return false;
        }

        if (intParams == null || intParams.Length < 3)
        {
            reason = "skill_activation_mode_payload_missing";
            return false;
        }

        if (intParams[0] != _player.playerId)
        {
            reason = "skill_activation_mode_player_mismatch";
            return false;
        }

        if (intParams[1] == 0)
        {
            reason = "skill_activation_mode_network_id_invalid";
            return false;
        }

        var requestedMode = (SkillActivationType)intParams[2];
        if (requestedMode != SkillActivationType.Manual && requestedMode != SkillActivationType.Automatic)
        {
            reason = "skill_activation_mode_value_invalid";
            return false;
        }

        uint requestedNetworkId = unchecked((uint)intParams[1]);
        if (!SetSkillActivationModeCommand.TryValidate(
                gm,
                _player.playerId,
                requestedNetworkId,
                requestedMode,
                out _,
                out reason))
        {
            return false;
        }

        reason = null;
        return true;
    }

    private bool ValidateMoveUnitRequest(GameManagers gm, Vector3[] vectorParams, out string reason)
    {
        if (!ValidatePreparePhase(gm, out reason)) return false;
        FieldManager field = _player.fieldManager;
        if (field == null)
        {
            reason = "field_not_ready";
            return false;
        }

        if (vectorParams.Length < 2)
        {
            reason = "missing_move_positions";
            return false;
        }

        Vector3Int from = Vector3Int.RoundToInt(vectorParams[0]);
        Vector3Int to = Vector3Int.RoundToInt(vectorParams[1]);
        if (!field.IsValidGridPosition(from) || !field.IsValidGridPosition(to))
        {
            reason = "move_position_out_of_range";
            return false;
        }

        Unit unit = field.GetUnitAt(from);
        UnitData sourceUnitData = unit != null ? unit.Data : null;
        if (unit == null && !field.HasPendingUnitAt(from))
        {
            reason = "move_source_empty";
            return false;
        }

        if (unit != null && !IsUnitOwnedByPlayer(_player, unit))
        {
            reason = "move_source_not_owned_by_player";
            return false;
        }

        if (unit == null && field.TryGetPendingUnitDataAt(from, out UnitData pendingUnitData))
        {
            sourceUnitData = pendingUnitData;
        }

        if (field.IsUnitAt(to))
        {
            reason = "move_destination_occupied";
            return false;
        }

        if (sourceUnitData == null && field.HasWallAt(to))
        {
            reason = "move_pending_unit_type_unknown_for_wall";
            return false;
        }

        if (sourceUnitData != null && sourceUnitData.unitType == UnitType.Melee && field.HasWallAt(to))
        {
            reason = "melee_unit_cannot_move_to_wall";
            return false;
        }

        reason = null;
        return true;
    }

    private bool ValidateSwapUnitRequest(GameManagers gm, Vector3[] vectorParams, out string reason)
    {
        if (!ValidatePreparePhase(gm, out reason)) return false;
        FieldManager field = _player.fieldManager;
        if (field == null)
        {
            reason = "field_not_ready";
            return false;
        }

        if (vectorParams.Length < 2)
        {
            reason = "missing_swap_positions";
            return false;
        }

        Vector3Int posA = Vector3Int.RoundToInt(vectorParams[0]);
        Vector3Int posB = Vector3Int.RoundToInt(vectorParams[1]);
        if (!field.IsValidGridPosition(posA) || !field.IsValidGridPosition(posB))
        {
            reason = "swap_position_out_of_range";
            return false;
        }

        Unit unitA = field.GetUnitAt(posA);
        Unit unitB = field.GetUnitAt(posB);
        if (unitA == null || unitB == null)
        {
            reason = "swap_requires_two_units";
            return false;
        }

        if (!IsUnitOwnedByPlayer(_player, unitA) || !IsUnitOwnedByPlayer(_player, unitB))
        {
            reason = "swap_unit_not_owned_by_player";
            return false;
        }

        if (unitA.Data == null || unitB.Data == null)
        {
            reason = "swap_unit_data_unresolved";
            return false;
        }

        if (unitA.Data.unitType == UnitType.Melee && field.HasWallAt(posB))
        {
            reason = "melee_unit_a_cannot_swap_to_wall";
            return false;
        }

        if (unitB.Data.unitType == UnitType.Melee && field.HasWallAt(posA))
        {
            reason = "melee_unit_b_cannot_swap_to_wall";
            return false;
        }

        reason = null;
        return true;
    }

    private bool ValidateSellUnitRequest(GameManagers gm, Vector3[] vectorParams, out string reason)
    {
        if (!ValidatePreparePhase(gm, out reason)) return false;
        FieldManager field = _player.fieldManager;
        if (field == null)
        {
            reason = "field_not_ready";
            return false;
        }

        if (vectorParams.Length < 1)
        {
            reason = "missing_sell_position";
            return false;
        }

        Vector3Int position = Vector3Int.RoundToInt(vectorParams[0]);
        if (!field.IsValidGridPosition(position))
        {
            reason = "sell_position_out_of_range";
            return false;
        }

        if (field.GetUnitAt(position) == null)
        {
            reason = "sell_position_empty";
            return false;
        }

        reason = null;
        return true;
    }

    private bool ValidatePlaceWallRequest(GameManagers gm, int[] intParams, Vector3[] vectorParams, out string reason)
    {
        if (!ValidatePreparePhase(gm, out reason)) return false;
        FieldManager field = _player.fieldManager;
        if (field == null)
        {
            reason = "field_not_ready";
            return false;
        }

        if (vectorParams.Length < 1)
        {
            reason = "missing_wall_position";
            return false;
        }

        Vector3Int position = Vector3Int.RoundToInt(vectorParams[0]);
        if (!field.IsValidGridPosition(position))
        {
            reason = "wall_position_out_of_range";
            return false;
        }

        if (field.HasWallAt(position))
        {
            reason = "wall_position_occupied";
            return false;
        }

        Unit occupant = field.GetUnitAt(position);
        if (occupant != null && !IsUnitOwnedByPlayer(_player, occupant))
        {
            reason = "wall_position_foreign_unit";
            return false;
        }

        if (occupant != null && occupant.Data == null)
        {
            reason = "wall_position_unresolved_unit";
            return false;
        }

        WallPlacementKind kind = intParams.Length > 1 && intParams[1] == (int)WallPlacementKind.Permanent
            ? WallPlacementKind.Permanent
            : WallPlacementKind.Destructible;
        if (intParams.Length > 1 && intParams[1] != (int)WallPlacementKind.Destructible && intParams[1] != (int)WallPlacementKind.Permanent)
        {
            reason = "invalid_wall_kind";
            return false;
        }

        bool hasStock = kind == WallPlacementKind.Permanent
            ? _player.GetPermanentWallPlacementCount() > 0
            : _player.GetWallCount() > 0;
        if (!hasStock)
        {
            reason = kind == WallPlacementKind.Permanent
                ? "insufficient_permanent_wall_stock"
                : "insufficient_wall_stock";
            return false;
        }

        if (_player.goalTransform != null && position == field.WorldToGridInt(_player.goalTransform.position))
        {
            reason = "wall_goal_cell_blocked";
            return false;
        }

        reason = null;
        return true;
    }

    private bool ValidateRemoveWallRequest(GameManagers gm, Vector3[] vectorParams, out string reason)
    {
        if (!ValidatePreparePhase(gm, out reason)) return false;
        FieldManager field = _player.fieldManager;
        if (field == null)
        {
            reason = "field_not_ready";
            return false;
        }

        if (vectorParams.Length < 1)
        {
            reason = "missing_remove_wall_position";
            return false;
        }

        Vector3Int position = Vector3Int.RoundToInt(vectorParams[0]);
        if (!field.IsValidGridPosition(position))
        {
            reason = "remove_wall_position_out_of_range";
            return false;
        }

        if (field.GetWallAt(position) == null && !field.IsPlayerPlacedPermanentWallAt(position))
        {
            reason = "remove_wall_missing";
            return false;
        }

        reason = null;
        return true;
    }

    private bool ValidateRerollShopRequest(GameManagers gm, out string reason)
    {
        if (!ValidatePreparePhase(gm, out reason)) return false;
        if (_player.shopManager == null || !_player.shopManager.IsDatabaseLoaded)
        {
            reason = "shop_not_ready";
            return false;
        }

        int cost = _player.shopManager.GetRerollCost();
        if (_player.GetGold() < cost)
        {
            reason = "insufficient_gold";
            return false;
        }

        reason = null;
        return true;
    }

    private bool ValidateSelectAugmentRequest(GameManagers gm, int[] intParams, out string reason)
    {
        if (!ValidatePreparePhase(gm, out reason)) return false;
        if (intParams.Length < 2)
        {
            reason = "missing_augment_index";
            return false;
        }

        if (_player.augmentManager == null)
        {
            reason = "augment_manager_not_ready";
            return false;
        }

        var presentedAugments = _player.augmentManager.GetPresentedAugments();
        int index = intParams[1];
        if (presentedAugments == null || index < 0 || index >= presentedAugments.Count)
        {
            reason = "augment_index_out_of_range";
            return false;
        }

        if (presentedAugments[index] == null)
        {
            reason = "augment_choice_missing";
            return false;
        }

        reason = null;
        return true;
    }

    private bool ValidateActivateSkillRequest(GameManagers gm, int[] intParams, out string reason)
    {
        if (intParams.Length < 2)
        {
            reason = "missing_skill_unit_id";
            return false;
        }

        uint unitNetworkId = (uint)intParams[1];
        const CommandExecutionScope scope = CommandExecutionScope.ClientRequest;
        const string source = "client_rpc";
        SkillCommandMpTestLogger.Request(_player.playerId, unitNetworkId, scope, source);

        if (!ActivateSkillCommand.TryValidate(
                gm,
                _player.playerId,
                unitNetworkId,
                scope,
                source,
                requireStateAuthority: true,
                out _,
                out SkillData skillData,
                out BattleCommandResult result))
        {
            reason = result.ErrorCode;
            if (ActivateSkillCommand.IsVolatileNoOp(result))
            {
                SkillCommandMpTestLogger.Skipped(result, unitNetworkId, skillData != null ? skillData.name : "unknown");
                return false;
            }

            int sequence = BattleCommandTelemetry.RecordRejected(CommandType.ActivateSkill);
            BattleCommandResult rejected = BattleCommandResult.Rejected(
                CommandType.ActivateSkill,
                result.PlayerId,
                result.ErrorCode,
                result.Message,
                result.OpponentPlayerId,
                result.Scope,
                result.Source,
                sequence);
            SkillCommandMpTestLogger.Rejected(rejected, unitNetworkId, skillData != null ? skillData.name : "unknown");
            gm?.SyncBattleCommandTelemetryToClientsIfAuthoritative();
            return false;
        }

        SkillCommandMpTestLogger.Accepted(result, unitNetworkId, skillData != null ? skillData.name : "unknown");
        reason = null;
        return true;
    }
}
