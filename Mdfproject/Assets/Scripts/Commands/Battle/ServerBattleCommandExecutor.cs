using System;
using Cysharp.Threading.Tasks;

public static class ServerBattleCommandExecutor
{
    public static BattleCommandResult TryExecute(
        CommandType commandType,
        int playerId,
        CommandExecutionScope scope,
        string source,
        Func<BattleCommandResult> validate,
        Func<BattleCommandResult> execute)
    {
        BattleCommandMpTestLogger.Request(commandType, playerId, scope, source, source: source);

        if (scope == CommandExecutionScope.PresentationOnly)
        {
            var rejected = BattleCommandResult.Rejected(commandType, playerId, "presentation_scope_not_executable", null, -1, scope, source);
            BattleCommandMpTestLogger.Rejected(rejected);
            return rejected;
        }

        var authorityResult = ValidateStateAuthority(commandType, playerId, scope, source);
        if (!authorityResult.Success)
        {
            BattleCommandMpTestLogger.Rejected(authorityResult);
            return authorityResult;
        }

        if (validate == null)
        {
            var rejected = BattleCommandResult.Rejected(commandType, playerId, "validation_delegate_required", null, -1, scope, source);
            BattleCommandMpTestLogger.Rejected(rejected);
            return rejected;
        }

        var validationResult = validate();
        if (!validationResult.Success)
        {
            BattleCommandMpTestLogger.Rejected(validationResult);
            return validationResult;
        }

        BattleCommandMpTestLogger.Accepted(validationResult);

        if (execute == null)
        {
            var rejected = BattleCommandResult.Rejected(commandType, playerId, "execution_delegate_required", null, -1, scope, source);
            BattleCommandMpTestLogger.Rejected(rejected);
            return rejected;
        }

        var executionResult = execute();

        if (executionResult.Success)
        {
            BattleCommandMpTestLogger.Executed(executionResult);
        }
        else
        {
            BattleCommandMpTestLogger.Rejected(executionResult);
        }

        return executionResult;
    }

    public static async UniTask<BattleCommandResult> TryExecuteAsync(
        CommandType commandType,
        int playerId,
        CommandExecutionScope scope,
        string source,
        Func<BattleCommandResult> validate,
        Func<UniTask<BattleCommandResult>> execute)
    {
        BattleCommandMpTestLogger.Request(commandType, playerId, scope, source, source: source);

        if (scope == CommandExecutionScope.PresentationOnly)
        {
            var rejected = BattleCommandResult.Rejected(commandType, playerId, "presentation_scope_not_executable", null, -1, scope, source);
            BattleCommandMpTestLogger.Rejected(rejected);
            return rejected;
        }

        var authorityResult = ValidateStateAuthority(commandType, playerId, scope, source);
        if (!authorityResult.Success)
        {
            BattleCommandMpTestLogger.Rejected(authorityResult);
            return authorityResult;
        }

        if (validate == null)
        {
            var rejected = BattleCommandResult.Rejected(commandType, playerId, "validation_delegate_required", null, -1, scope, source);
            BattleCommandMpTestLogger.Rejected(rejected);
            return rejected;
        }

        var validationResult = validate();
        if (!validationResult.Success)
        {
            BattleCommandMpTestLogger.Rejected(validationResult);
            return validationResult;
        }

        BattleCommandMpTestLogger.Accepted(validationResult);

        if (execute == null)
        {
            var rejected = BattleCommandResult.Rejected(commandType, playerId, "execution_delegate_required", null, -1, scope, source);
            BattleCommandMpTestLogger.Rejected(rejected);
            return rejected;
        }

        var executionResult = await execute();

        if (executionResult.Success)
        {
            BattleCommandMpTestLogger.Executed(executionResult);
        }
        else
        {
            BattleCommandMpTestLogger.Rejected(executionResult);
        }

        return executionResult;
    }

    public static BattleCommandResult ValidateStateAuthority(
        CommandType commandType,
        int playerId,
        CommandExecutionScope scope,
        string source = null)
    {
        var gm = GameManagers.Instance;
        if (gm == null)
        {
            return BattleCommandResult.Rejected(commandType, playerId, "game_managers_missing", null, -1, scope, source);
        }

        if (gm.Object == null || !gm.Object.HasStateAuthority)
        {
            return BattleCommandResult.Rejected(commandType, playerId, "state_authority_required", null, -1, scope, source);
        }

        if (!BattleCommandValidator.IsBattlePhase(gm))
        {
            return BattleCommandResult.Rejected(commandType, playerId, "command_requires_battle_phase", null, -1, scope, source);
        }

        return BattleCommandResult.Accepted(commandType, playerId, "state_authority_validated", -1, scope, source);
    }
}
