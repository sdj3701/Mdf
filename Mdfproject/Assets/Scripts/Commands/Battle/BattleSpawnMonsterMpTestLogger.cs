using System.Collections.Generic;
using UnityEngine;

public static class BattleSpawnMonsterMpTestLogger
{
    public static void Request(BattleSpawnMonsterCommand command)
    {
        Log("battle_spawn_request", "pass", BattleCommandResult.Accepted(
            CommandType.BattleSpawnMonster,
            command.AttackerPlayerId,
            "request",
            command.DefenderPlayerId,
            command.Scope,
            command.SourceReason), command);
    }

    public static void Accepted(BattleCommandResult result, BattleSpawnMonsterCommand command)
    {
        Log("battle_spawn_accepted", "pass", result, command);
    }

    public static void Rejected(BattleCommandResult result, BattleSpawnMonsterCommand command)
    {
        Log("battle_spawn_rejected", "fail", result, command);
    }

    public static void Executed(BattleCommandResult result, BattleSpawnMonsterCommand command)
    {
        Log("battle_spawn_executed", "pass", result, command);
    }

    private static void Log(string phase, string result, BattleCommandResult commandResult, BattleSpawnMonsterCommand command)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (!MPTestCommandLine.IsEnabled)
        {
            return;
        }

        Vector3 pos = command.SpawnWorldPosition;
        var fields = new Dictionary<string, object>
        {
            { "commandType", CommandType.BattleSpawnMonster.ToString() },
            { "attackerPlayerId", command.AttackerPlayerId },
            { "defenderPlayerId", command.DefenderPlayerId },
            { "poolSlotIndex", command.PoolSlotIndex },
            { "observedAttackMonsterPoolRevision", command.ObservedAttackMonsterPoolRevision },
            { "count", command.Count },
            { "scope", command.Scope.ToString() },
            { "success", commandResult.Success },
            { "errorCode", string.IsNullOrWhiteSpace(commandResult.ErrorCode) ? "none" : commandResult.ErrorCode },
            { "sequence", commandResult.Sequence },
            { "spawnX", pos.x },
            { "spawnY", pos.y },
            { "spawnZ", pos.z }
        };

        if (!string.IsNullOrWhiteSpace(command.SourceReason))
        {
            fields["source"] = command.SourceReason;
        }

        string code = string.IsNullOrWhiteSpace(commandResult.ErrorCode)
            ? CommandType.BattleSpawnMonster.ToString()
            : commandResult.ErrorCode;
        MPTestLogger.Log(phase, result, code, commandResult.Message, fields);
#endif
    }
}
