using System.Collections.Generic;

public static class BattleCommandMpTestLogger
{
    public static void Request(
        CommandType commandType,
        int playerId,
        CommandExecutionScope scope,
        string message = null,
        int opponentPlayerId = -1,
        string source = null)
    {
        Log("battle_command_request", BattleCommandResult.Accepted(commandType, playerId, message ?? "request", opponentPlayerId, scope, source));
    }

    public static void Accepted(BattleCommandResult result)
    {
        Log("battle_command_accepted", result);
    }

    public static void Rejected(BattleCommandResult result)
    {
        Log("battle_command_rejected", result);
    }

    public static void Executed(BattleCommandResult result)
    {
        Log("battle_command_executed", result);
    }

    public static void Log(string phase, BattleCommandResult result)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (!MPTestCommandLine.IsEnabled)
        {
            return;
        }

        var fields = new Dictionary<string, object>
        {
            { "commandType", result.CommandType.ToString() },
            { "playerId", result.PlayerId },
            { "opponentPlayerId", result.OpponentPlayerId },
            { "scope", result.Scope.ToString() },
            { "success", result.Success },
            { "errorCode", string.IsNullOrWhiteSpace(result.ErrorCode) ? "none" : result.ErrorCode },
            { "sequence", result.Sequence }
        };

        if (!string.IsNullOrWhiteSpace(result.Source))
        {
            fields["source"] = result.Source;
        }

        string code = string.IsNullOrWhiteSpace(result.ErrorCode) ? result.CommandType.ToString() : result.ErrorCode;
        string mpResult = result.Success ? "pass" : "fail";
        MPTestLogger.Log(phase, mpResult, code, result.Message, fields);
#endif
    }
}
