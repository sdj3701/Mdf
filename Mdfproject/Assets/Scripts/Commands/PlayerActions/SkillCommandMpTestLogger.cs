using System.Collections.Generic;

public static class SkillCommandMpTestLogger
{
    public static void Request(ActivateSkillCommand command, CommandExecutionScope scope, string source)
    {
        if (command == null)
        {
            return;
        }

        Log(
            "skill_command_request",
            "pass",
            BattleCommandResult.Accepted(CommandType.ActivateSkill, command.PlayerId, "request", -1, scope, source),
            command.UnitNetworkId,
            "unknown");
    }

    public static void Request(int playerId, uint unitNetworkId, CommandExecutionScope scope, string source)
    {
        Log(
            "skill_command_request",
            "pass",
            BattleCommandResult.Accepted(CommandType.ActivateSkill, playerId, "request", -1, scope, source),
            unitNetworkId,
            "unknown");
    }

    public static void Accepted(BattleCommandResult result, uint unitNetworkId, string skillName)
    {
        Log("skill_command_accepted", "pass", result, unitNetworkId, skillName);
    }

    public static void Rejected(BattleCommandResult result, uint unitNetworkId, string skillName)
    {
        Log("skill_command_rejected", "fail", result, unitNetworkId, skillName);
    }

    public static void Skipped(BattleCommandResult result, uint unitNetworkId, string skillName)
    {
        Log("skill_command_skipped", "info", result, unitNetworkId, skillName);
    }

    public static void Executed(BattleCommandResult result, uint unitNetworkId, string skillName)
    {
        Log("skill_command_executed", "pass", result, unitNetworkId, skillName);
    }

    private static void Log(
        string phase,
        string mpResult,
        BattleCommandResult result,
        uint unitNetworkId,
        string skillName)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (!MPTestCommandLine.IsEnabled)
        {
            return;
        }

        var fields = new Dictionary<string, object>
        {
            { "commandType", CommandType.ActivateSkill.ToString() },
            { "playerId", result.PlayerId },
            { "unitNetworkId", unitNetworkId },
            { "skill", string.IsNullOrWhiteSpace(skillName) ? "unknown" : skillName },
            { "scope", result.Scope.ToString() },
            { "success", result.Success },
            { "errorCode", string.IsNullOrWhiteSpace(result.ErrorCode) ? "none" : result.ErrorCode },
            { "sequence", result.Sequence }
        };

        if (!string.IsNullOrWhiteSpace(result.Source))
        {
            fields["source"] = result.Source;
        }

        string code = string.IsNullOrWhiteSpace(result.ErrorCode)
            ? CommandType.ActivateSkill.ToString()
            : result.ErrorCode;
        MPTestLogger.Log(phase, mpResult, code, result.Message, fields);
#endif
    }
}
