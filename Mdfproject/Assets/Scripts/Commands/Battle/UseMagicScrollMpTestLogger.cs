using System.Collections.Generic;
using UnityEngine;

public static class UseMagicScrollMpTestLogger
{
    public static void Request(UseMagicScrollCommand command)
    {
        Log("scroll_request", "pass", BattleCommandResult.Accepted(
            CommandType.UseMagicScroll,
            command.CasterPlayerId,
            "request",
            -1,
            command.Scope,
            command.SourceReason), command, 0, null);
    }

    public static void Accepted(BattleCommandResult result, UseMagicScrollCommand command, string scrollName)
    {
        Log("scroll_accepted", "pass", result, command, 0, scrollName);
    }

    public static void Rejected(BattleCommandResult result, UseMagicScrollCommand command)
    {
        Log("scroll_rejected", "fail", result, command, 0, null);
    }

    public static void EffectApplied(BattleCommandResult result, UseMagicScrollCommand command, int targetCount, string scrollName)
    {
        Log("scroll_effect_applied", "pass", result, command, targetCount, scrollName);
    }

    public static void Presentation(int casterPlayerId, string scrollDataName, Vector3 position)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (!MPTestCommandLine.IsEnabled)
        {
            return;
        }

        var fields = new Dictionary<string, object>
        {
            { "commandType", CommandType.UseMagicScroll.ToString() },
            { "casterPlayerId", casterPlayerId },
            { "scrollDataName", string.IsNullOrWhiteSpace(scrollDataName) ? "unknown" : scrollDataName },
            { "targetX", position.x },
            { "targetY", position.y },
            { "targetZ", position.z }
        };

        MPTestLogger.Log("scroll_presentation", "pass", CommandType.UseMagicScroll.ToString(), "presentation", fields);
#endif
    }

    private static void Log(
        string phase,
        string result,
        BattleCommandResult commandResult,
        UseMagicScrollCommand command,
        int targetCount,
        string scrollName)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (!MPTestCommandLine.IsEnabled)
        {
            return;
        }

        Vector3 pos = command.TargetWorldPosition;
        var fields = new Dictionary<string, object>
        {
            { "commandType", CommandType.UseMagicScroll.ToString() },
            { "casterPlayerId", command.CasterPlayerId },
            { "scrollSlotIndex", command.ScrollSlotIndex },
            { "observedOwnedMagicScrollRevision", command.ObservedOwnedMagicScrollRevision },
            { "scope", command.Scope.ToString() },
            { "success", commandResult.Success },
            { "errorCode", string.IsNullOrWhiteSpace(commandResult.ErrorCode) ? "none" : commandResult.ErrorCode },
            { "sequence", commandResult.Sequence },
            { "targetCount", targetCount },
            { "targetX", pos.x },
            { "targetY", pos.y },
            { "targetZ", pos.z }
        };

        if (!string.IsNullOrWhiteSpace(command.SourceReason))
        {
            fields["source"] = command.SourceReason;
        }

        if (!string.IsNullOrWhiteSpace(scrollName))
        {
            fields["scrollDataName"] = scrollName;
        }

        string code = string.IsNullOrWhiteSpace(commandResult.ErrorCode)
            ? CommandType.UseMagicScroll.ToString()
            : commandResult.ErrorCode;
        MPTestLogger.Log(phase, result, code, commandResult.Message, fields);
#endif
    }
}
