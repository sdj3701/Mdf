public static class BattleCommandTelemetry
{
    public static int AcceptedBattleCommandSeq { get; private set; }
    public static int SpawnMonsterSeq { get; private set; }
    public static int UseMagicScrollSeq { get; private set; }
    public static int ActivateSkillSeq { get; private set; }
    public static int RejectedBattleCommandCount { get; private set; }
    public static string LastCommand { get; private set; } = "unknown";

    public static int RecordAccepted(CommandType commandType)
    {
        LastCommand = commandType.ToString();
        AcceptedBattleCommandSeq++;
        return AcceptedBattleCommandSeq;
    }

    public static int RecordSpawnMonsterExecuted()
    {
        LastCommand = CommandType.BattleSpawnMonster.ToString();
        SpawnMonsterSeq++;
        return SpawnMonsterSeq;
    }

    public static int RecordUseMagicScrollExecuted()
    {
        LastCommand = CommandType.UseMagicScroll.ToString();
        UseMagicScrollSeq++;
        return UseMagicScrollSeq;
    }

    public static int RecordActivateSkillExecuted()
    {
        LastCommand = CommandType.ActivateSkill.ToString();
        ActivateSkillSeq++;
        return ActivateSkillSeq;
    }

    public static int RecordRejected(CommandType commandType)
    {
        LastCommand = commandType.ToString();
        RejectedBattleCommandCount++;
        return RejectedBattleCommandCount;
    }

    public static void ApplySnapshot(
        int acceptedBattleCommandSeq,
        int spawnMonsterSeq,
        int useMagicScrollSeq,
        int activateSkillSeq,
        int rejectedBattleCommandCount,
        string lastCommand)
    {
        AcceptedBattleCommandSeq = acceptedBattleCommandSeq;
        SpawnMonsterSeq = spawnMonsterSeq;
        UseMagicScrollSeq = useMagicScrollSeq;
        ActivateSkillSeq = activateSkillSeq;
        RejectedBattleCommandCount = rejectedBattleCommandCount;
        LastCommand = string.IsNullOrWhiteSpace(lastCommand) ? "unknown" : lastCommand;
    }
}
