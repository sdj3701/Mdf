using UnityEngine;

/// <summary>
/// Shared pacing policy for strategic monster spawn requests.
/// Gameplay authority and resource validation remain in BattleSpawnMonsterCommand;
/// this policy only controls how often human, AI, and HumanBot callers may ask.
/// </summary>
public static class BattleSpawnCadence
{
    public const float PreviousHumanRepeatIntervalSeconds = 0.3f;
    public const float DelayScale = 2f / 3f;
    public const float BattleDecisionPollIntervalSeconds = 0.05f;
    public const float NonSpawnBattleActionCooldownSeconds = 0.75f;

    public static float SpawnIntervalSeconds =>
        PreviousHumanRepeatIntervalSeconds * DelayScale;

    public static float ResolveDecisionInterval(
        GameManagers.GameState state,
        float nonBattleDecisionIntervalSeconds)
    {
        return IsBattleState(state)
            ? BattleDecisionPollIntervalSeconds
            : Mathf.Max(0.05f, nonBattleDecisionIntervalSeconds);
    }

    public static float ResolvePolicyCooldown(CommandType commandType)
    {
        return commandType == CommandType.BattleSpawnMonster
            ? SpawnIntervalSeconds
            : NonSpawnBattleActionCooldownSeconds;
    }

    private static bool IsBattleState(GameManagers.GameState state)
    {
        return state == GameManagers.GameState.Battle1 ||
               state == GameManagers.GameState.Battle2;
    }
}
