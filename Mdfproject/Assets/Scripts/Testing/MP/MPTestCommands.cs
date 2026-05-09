public static class MPTestCommands
{
    public const string LobbySmoke = "lobby_smoke";
    public const string GameSmoke = "game_smoke";
    public const string PrepareSmoke = "prepare_smoke";
    public const string BattleSmoke = "battle_smoke";
    public const string HostMigration = "host_migration";

    public static bool IsKnownScenario(string scenario)
    {
        switch (scenario)
        {
            case LobbySmoke:
            case GameSmoke:
            case PrepareSmoke:
            case BattleSmoke:
            case HostMigration:
                return true;
            default:
                return false;
        }
    }
}
