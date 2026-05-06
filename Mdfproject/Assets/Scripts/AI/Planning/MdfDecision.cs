using System.Collections.Generic;
using UnityEngine;

public enum MdfDecisionKind
{
    Observe,
    Command,
    BattleSpawnMonster,
    UseMagicScroll
}

public sealed class MdfDecision
{
    public MdfDecisionKind Kind { get; private set; }
    public ICommand Command { get; private set; }
    public BattleSpawnMonsterCommand BattleSpawnMonster { get; private set; }
    public UseMagicScrollCommand UseMagicScroll { get; private set; }
    public CommandType CommandType { get; private set; }
    public string CommandTypeName { get; private set; }
    public string Reason { get; private set; }
    public string Target { get; private set; }
    public int PlayerId { get; private set; }
    public string Persona { get; private set; }
    public string GameState { get; private set; }
    public int Round { get; private set; }
    public float Score { get; private set; }
    public object Observed { get; private set; }
    public IReadOnlyDictionary<string, object> JournalFields { get; private set; }

    public bool HasCommandPayload =>
        Command != null ||
        BattleSpawnMonster != null ||
        UseMagicScroll != null;

    private MdfDecision()
    {
    }

    public static MdfDecision Observe(MdfDecisionContext context, string reason)
    {
        return Create(
            context,
            MdfDecisionKind.Observe,
            null,
            null,
            null,
            CommandType.RequestSyncData,
            "Observe",
            reason,
            null,
            0f,
            null);
    }

    public static MdfDecision ForCommand(
        MdfDecisionContext context,
        ICommand command,
        CommandType commandType,
        string reason,
        string target,
        float score = 0f,
        IReadOnlyDictionary<string, object> journalFields = null)
    {
        return Create(
            context,
            MdfDecisionKind.Command,
            command,
            null,
            null,
            commandType,
            commandType.ToString(),
            reason,
            target,
            score,
            journalFields);
    }

    public static MdfDecision ForBattleSpawnMonster(
        MdfDecisionContext context,
        BattleSpawnMonsterCommand command,
        Vector3 target,
        string reason,
        float score = 0f,
        IReadOnlyDictionary<string, object> journalFields = null)
    {
        return Create(
            context,
            MdfDecisionKind.BattleSpawnMonster,
            null,
            command,
            null,
            CommandType.BattleSpawnMonster,
            CommandType.BattleSpawnMonster.ToString(),
            reason,
            FormatVector(target),
            score,
            journalFields);
    }

    public static MdfDecision ForUseMagicScroll(
        MdfDecisionContext context,
        UseMagicScrollCommand command,
        Vector3 target,
        string reason,
        float score = 0f,
        IReadOnlyDictionary<string, object> journalFields = null)
    {
        return Create(
            context,
            MdfDecisionKind.UseMagicScroll,
            null,
            null,
            command,
            CommandType.UseMagicScroll,
            CommandType.UseMagicScroll.ToString(),
            reason,
            FormatVector(target),
            score,
            journalFields);
    }

    private static MdfDecision Create(
        MdfDecisionContext context,
        MdfDecisionKind kind,
        ICommand command,
        BattleSpawnMonsterCommand battleSpawnMonster,
        UseMagicScrollCommand useMagicScroll,
        CommandType commandType,
        string commandTypeName,
        string reason,
        string target,
        float score,
        IReadOnlyDictionary<string, object> journalFields)
    {
        return new MdfDecision
        {
            Kind = kind,
            Command = command,
            BattleSpawnMonster = battleSpawnMonster,
            UseMagicScroll = useMagicScroll,
            CommandType = commandType,
            CommandTypeName = string.IsNullOrWhiteSpace(commandTypeName) ? "Observe" : commandTypeName,
            Reason = string.IsNullOrWhiteSpace(reason) ? "no_reason" : reason,
            Target = target,
            PlayerId = context != null && context.Actor != null ? context.Actor.playerId : -1,
            Persona = context != null ? context.Persona : "unknown",
            GameState = context != null && context.GameManagers != null ? context.GameManagers.GetGameState().ToString() : "unknown",
            Round = context != null && context.GameManagers != null ? context.GameManagers.currentRound : 0,
            Score = score,
            Observed = context != null ? context.Observed : null,
            JournalFields = journalFields
        };
    }

    private static string FormatVector(Vector3 vector)
    {
        return $"{vector.x:F2},{vector.y:F2},{vector.z:F2}";
    }
}
