using Fusion;

/// <summary>
/// Applies a unit's automatic/manual skill policy on State Authority. The
/// resulting mode is replicated by Unit.NetworkedSkillActivationType.
/// </summary>
public sealed class SetSkillActivationModeCommand : ICommand
{
    public int PlayerId { get; set; }
    public uint UnitNetworkId { get; }
    public SkillActivationType Mode { get; }

    public SetSkillActivationModeCommand(
        int playerId,
        uint unitNetworkId,
        SkillActivationType mode)
    {
        PlayerId = playerId;
        UnitNetworkId = unitNetworkId;
        Mode = mode;
    }

    public void Execute()
    {
        GameManagers gm = GameManagers.Instance;
        if (!TryValidate(gm, PlayerId, UnitNetworkId, Mode, out Unit unit, out _))
        {
            return;
        }

        unit.TrySetSkillActivationModeAuthoritative(Mode);
    }

    public static bool TryValidate(
        GameManagers gm,
        int playerId,
        uint unitNetworkId,
        SkillActivationType mode,
        out Unit unit,
        out string reason)
    {
        unit = null;
        reason = null;

        if (gm == null || gm.Runner == null || !gm.Runner.IsRunning ||
            gm.Object == null || !gm.Object.HasStateAuthority)
        {
            reason = "game_managers_not_authoritative";
            return false;
        }

        if (gm.IsSequenceTransitioning)
        {
            reason = "command_blocked_during_sequence_transition";
            return false;
        }

        if (gm.currentState != GameManagers.GameState.Prepare &&
            gm.currentState != GameManagers.GameState.Battle1 &&
            gm.currentState != GameManagers.GameState.Battle2)
        {
            reason = "skill_mode_requires_prepare_or_battle_phase";
            return false;
        }

        if (mode != SkillActivationType.Automatic && mode != SkillActivationType.Manual)
        {
            reason = "invalid_skill_activation_mode";
            return false;
        }

        if (unitNetworkId == 0)
        {
            reason = "missing_unit_network_id";
            return false;
        }

        PlayerManager player = gm.GetPlayer(playerId);
        if (player == null)
        {
            reason = "player_not_found";
            return false;
        }

        foreach (NetworkObject networkObject in gm.Runner.GetAllNetworkObjects())
        {
            if (networkObject == null || !networkObject.IsValid || networkObject.Id.Raw != unitNetworkId)
            {
                continue;
            }

            unit = networkObject.GetComponent<Unit>();
            break;
        }

        if (unit == null || unit.Object == null || !unit.Object.IsValid)
        {
            reason = "unit_not_found";
            return false;
        }

        if (!PlayerManager.IsUnitOwnedByPlayerForCommand(player, unit))
        {
            reason = "unit_not_owned_by_player";
            return false;
        }

        if (!unit.HasConfiguredSkill)
        {
            reason = "unit_has_no_configured_skill";
            return false;
        }

        return true;
    }
}
