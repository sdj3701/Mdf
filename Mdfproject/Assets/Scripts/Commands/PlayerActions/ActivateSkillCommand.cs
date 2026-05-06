using Fusion;
using System.Linq;

public class ActivateSkillCommand : ICommand
{
    private const CommandType Type = CommandType.ActivateSkill;

    public int PlayerId { get; set; }
    public uint UnitNetworkId { get; private set; }

    public ActivateSkillCommand(int playerId, uint unitNetworkId)
    {
        PlayerId = playerId;
        UnitNetworkId = unitNetworkId;
    }

    public void Execute()
    {
        var gm = GameManagers.Instance;
        if (gm == null || gm.Runner == null)
        {
            return;
        }

        if (gm.Object != null && !gm.Object.HasStateAuthority)
        {
            return;
        }

        const CommandExecutionScope scope = CommandExecutionScope.ServerAuthorityOnly;
        const string source = "command_stream";
        SkillCommandMpTestLogger.Request(this, scope, source);

        if (!TryValidate(
                gm,
                PlayerId,
                UnitNetworkId,
                scope,
                source,
                requireStateAuthority: true,
                out Unit unit,
                out SkillData skillData,
                out BattleCommandResult validationResult))
        {
            var rejected = RecordRejected(validationResult, source);
            SkillCommandMpTestLogger.Rejected(rejected, UnitNetworkId, ResolveSkillName(skillData));
            SyncTelemetryToClients();
            return;
        }

        int acceptedSequence = BattleCommandTelemetry.RecordAccepted(Type);
        var accepted = BattleCommandResult.Accepted(
            Type,
            PlayerId,
            "activate_skill_validated",
            -1,
            scope,
            source,
            acceptedSequence);
        SkillCommandMpTestLogger.Accepted(accepted, UnitNetworkId, ResolveSkillName(skillData));

        unit.ActivateSkill();

        int executedSequence = BattleCommandTelemetry.RecordActivateSkillExecuted();
        var executed = BattleCommandResult.Executed(
            Type,
            PlayerId,
            $"activate_skill_executed;unit={UnitNetworkId};skill={ResolveSkillName(skillData)}",
            -1,
            scope,
            source,
            executedSequence);
        SkillCommandMpTestLogger.Executed(executed, UnitNetworkId, ResolveSkillName(skillData));
        SyncTelemetryToClients();
    }

    public static bool TryValidate(
        GameManagers gm,
        int playerId,
        uint unitNetworkId,
        CommandExecutionScope scope,
        string source,
        bool requireStateAuthority,
        out Unit unit,
        out SkillData skillData,
        out BattleCommandResult result)
    {
        unit = null;
        skillData = null;

        if (!BattleCommandValidator.ResolveActorPlayer(
                gm,
                playerId,
                Type,
                out PlayerManager player,
                out result,
                scope,
                source))
        {
            return false;
        }

        if (!BattleCommandValidator.IsBattlePhase(gm))
        {
            result = BattleCommandResult.Rejected(Type, playerId, "command_requires_battle_phase", null, -1, scope, source);
            return false;
        }

        if (unitNetworkId == 0)
        {
            result = BattleCommandResult.Rejected(Type, playerId, "skill_unit_network_id_invalid", null, -1, scope, source);
            return false;
        }

        if (gm.Runner == null)
        {
            result = BattleCommandResult.Rejected(Type, playerId, "runner_not_ready", null, -1, scope, source);
            return false;
        }

        if (!TryResolveUnit(gm.Runner, unitNetworkId, out unit))
        {
            result = BattleCommandResult.Rejected(Type, playerId, "skill_unit_not_found", null, -1, scope, source);
            return false;
        }

        if (unit.Object == null || !unit.Object.IsValid || unit.Object.Id.Raw != unitNetworkId)
        {
            result = BattleCommandResult.Rejected(Type, playerId, "skill_unit_network_id_invalid", null, -1, scope, source);
            return false;
        }

        if (requireStateAuthority && unit.Object != null && unit.Runner != null && unit.Runner.IsRunning && !unit.Object.HasStateAuthority)
        {
            result = BattleCommandResult.Rejected(Type, playerId, "skill_unit_state_authority_required", null, -1, scope, source);
            return false;
        }

        if (!UnitBelongsToPlayer(unit, player))
        {
            result = BattleCommandResult.Rejected(Type, playerId, "skill_unit_not_owned", null, -1, scope, source);
            return false;
        }

        if (unit.IsDead || unit.CurrentHealth <= 0f)
        {
            result = BattleCommandResult.Rejected(Type, playerId, "skill_unit_dead", null, -1, scope, source);
            return false;
        }

        if (!unit.IsInCombatPhase)
        {
            result = BattleCommandResult.Rejected(Type, playerId, "skill_unit_not_in_combat", null, -1, scope, source);
            return false;
        }

        if (!unit.HasConfiguredSkill)
        {
            result = BattleCommandResult.Rejected(Type, playerId, "skill_not_configured", null, -1, scope, source);
            return false;
        }

        skillData = unit.LoadedSkillData;
        if (skillData == null)
        {
            string message = unit.TryGetConfiguredSkillKey(out string skillKey)
                ? $"skillKey={skillKey}"
                : null;
            result = BattleCommandResult.Rejected(Type, playerId, "skill_data_not_loaded", message, -1, scope, source);
            return false;
        }

        if (!unit.IsManualOrAiStrategicSkill(skillData))
        {
            result = BattleCommandResult.Rejected(Type, playerId, "skill_not_manual_or_ai_strategic", null, -1, scope, source);
            return false;
        }

        if (unit.IsSkillCastingActive)
        {
            result = BattleCommandResult.Rejected(Type, playerId, "skill_unit_already_casting", null, -1, scope, source);
            return false;
        }

        if (!unit.CanUseSkillByStatus)
        {
            result = BattleCommandResult.Rejected(Type, playerId, "skill_unit_disabled_or_silenced", null, -1, scope, source);
            return false;
        }

        if (!unit.IsSkillManaFull || unit.SkillCurrentMana < skillData.manaCost)
        {
            string message = $"mana={unit.SkillCurrentMana:F1}/{unit.SkillMaxMana:F1};cost={skillData.manaCost}";
            result = BattleCommandResult.Rejected(Type, playerId, "skill_mana_not_ready", message, -1, scope, source);
            return false;
        }

        if (skillData.targetingStrategy == null || skillData.effects == null || skillData.effects.Count == 0)
        {
            result = BattleCommandResult.Rejected(Type, playerId, "skill_data_incomplete", null, -1, scope, source);
            return false;
        }

        if (!unit.HasSkillTargetsAvailable(skillData))
        {
            result = BattleCommandResult.Rejected(Type, playerId, "skill_target_unavailable", null, -1, scope, source);
            return false;
        }

        result = BattleCommandResult.Accepted(Type, playerId, "activate_skill_validated", -1, scope, source);
        return true;
    }

    private static bool TryResolveUnit(NetworkRunner runner, uint unitNetworkId, out Unit unit)
    {
        unit = null;
        if (runner == null)
        {
            return false;
        }

        foreach (var networkObject in runner.GetAllNetworkObjects())
        {
            if (networkObject == null || networkObject.Id.Raw != unitNetworkId)
            {
                continue;
            }

            unit = networkObject.GetComponent<Unit>();
            return unit != null;
        }

        return false;
    }

    private static bool UnitBelongsToPlayer(Unit unit, PlayerManager player)
    {
        if (unit == null || player == null)
        {
            return false;
        }

        if (unit.Owner == player)
        {
            return true;
        }

        return player.fieldManager != null &&
               player.fieldManager.GetAlliedUnitsOnField().Contains(unit);
    }

    private static BattleCommandResult RecordRejected(BattleCommandResult validationResult, string source)
    {
        int sequence = BattleCommandTelemetry.RecordRejected(Type);
        return BattleCommandResult.Rejected(
            Type,
            validationResult.PlayerId,
            validationResult.ErrorCode,
            validationResult.Message,
            validationResult.OpponentPlayerId,
            validationResult.Scope,
            string.IsNullOrWhiteSpace(validationResult.Source) ? source : validationResult.Source,
            sequence);
    }

    private static string ResolveSkillName(SkillData skillData)
    {
        if (skillData == null)
        {
            return "unknown";
        }

        return !string.IsNullOrWhiteSpace(skillData.skillName)
            ? skillData.skillName
            : skillData.name;
    }

    private static void SyncTelemetryToClients()
    {
        var gm = GameManagers.Instance;
        if (gm != null)
        {
            gm.SyncBattleCommandTelemetryToClientsIfAuthoritative();
        }
    }
}
