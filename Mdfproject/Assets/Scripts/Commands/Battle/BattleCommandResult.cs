public struct BattleCommandResult
{
    public bool Success { get; private set; }
    public string ErrorCode { get; private set; }
    public string Message { get; private set; }
    public CommandType CommandType { get; private set; }
    public int PlayerId { get; private set; }
    public int OpponentPlayerId { get; private set; }
    public CommandExecutionScope Scope { get; private set; }
    public string Source { get; private set; }
    public int Sequence { get; private set; }

    public static BattleCommandResult Accepted(
        CommandType commandType,
        int playerId,
        string message = null,
        int opponentPlayerId = -1,
        CommandExecutionScope scope = CommandExecutionScope.ServerAuthorityOnly,
        string source = null,
        int sequence = 0)
    {
        return new BattleCommandResult
        {
            Success = true,
            ErrorCode = null,
            Message = string.IsNullOrWhiteSpace(message) ? "accepted" : message,
            CommandType = commandType,
            PlayerId = playerId,
            OpponentPlayerId = opponentPlayerId,
            Scope = scope,
            Source = source,
            Sequence = sequence
        };
    }

    public static BattleCommandResult Executed(
        CommandType commandType,
        int playerId,
        string message = null,
        int opponentPlayerId = -1,
        CommandExecutionScope scope = CommandExecutionScope.ServerAuthorityOnly,
        string source = null,
        int sequence = 0)
    {
        return new BattleCommandResult
        {
            Success = true,
            ErrorCode = null,
            Message = string.IsNullOrWhiteSpace(message) ? "executed" : message,
            CommandType = commandType,
            PlayerId = playerId,
            OpponentPlayerId = opponentPlayerId,
            Scope = scope,
            Source = source,
            Sequence = sequence
        };
    }

    public static BattleCommandResult Rejected(
        CommandType commandType,
        int playerId,
        string errorCode,
        string message = null,
        int opponentPlayerId = -1,
        CommandExecutionScope scope = CommandExecutionScope.ClientRequest,
        string source = null,
        int sequence = 0)
    {
        return new BattleCommandResult
        {
            Success = false,
            ErrorCode = string.IsNullOrWhiteSpace(errorCode) ? "battle_command_rejected" : errorCode,
            Message = string.IsNullOrWhiteSpace(message) ? errorCode : message,
            CommandType = commandType,
            PlayerId = playerId,
            OpponentPlayerId = opponentPlayerId,
            Scope = scope,
            Source = source,
            Sequence = sequence
        };
    }
}
