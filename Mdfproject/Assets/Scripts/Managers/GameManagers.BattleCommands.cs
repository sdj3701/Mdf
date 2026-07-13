using Cysharp.Threading.Tasks;
using Fusion;
using UnityEngine;

public partial class GameManagers
{
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_RequestBattleSpawnMonster(
        int attackerPlayerId,
        int defenderPlayerId,
        int poolSlotIndex,
        Vector3 spawnWorldPosition,
        int count,
        int observedAttackMonsterPoolRevision,
        int observedBlackMagicRevision,
        string sourceReason,
        RpcInfo info = default)
    {
        if (Object == null || !Object.HasStateAuthority)
        {
            return;
        }

        string reason = string.IsNullOrWhiteSpace(sourceReason) ? "client_rpc" : sourceReason;
        var command = new BattleSpawnMonsterCommand(
            attackerPlayerId,
            defenderPlayerId,
            poolSlotIndex,
            spawnWorldPosition,
            count,
            reason,
            observedAttackMonsterPoolRevision,
            observedBlackMagicRevision);

        var attacker = GetPlayer(attackerPlayerId);
        if (!IsRpcSourceAuthorizedForPlayer(attacker, info.Source))
        {
            int sequence = BattleCommandTelemetry.RecordRejected(CommandType.BattleSpawnMonster);
            var rejected = BattleCommandResult.Rejected(
                CommandType.BattleSpawnMonster,
                attackerPlayerId,
                "rpc_source_not_attacker_input_authority",
                null,
                defenderPlayerId,
                CommandExecutionScope.ClientRequest,
                reason,
                sequence);
            BattleSpawnMonsterMpTestLogger.Request(command);
            BattleSpawnMonsterMpTestLogger.Rejected(rejected, command);
            SyncBattleCommandTelemetryToClientsIfAuthoritative();
            return;
        }

        RunLifecycleTask(
            command.ExecuteAsync(CommandExecutionScope.ClientRequest, info.Source),
            "RPC_RequestBattleSpawnMonster/BattleSpawnMonsterCommand");
    }

    public void SyncBattleCommandTelemetryToClientsIfAuthoritative()
    {
        if (Object == null || !Object.HasStateAuthority)
        {
            return;
        }

        RPC_SyncBattleCommandTelemetry(
            BattleCommandTelemetry.AcceptedBattleCommandSeq,
            BattleCommandTelemetry.SpawnMonsterSeq,
            BattleCommandTelemetry.UseMagicScrollSeq,
            BattleCommandTelemetry.ActivateSkillSeq,
            BattleCommandTelemetry.RejectedBattleCommandCount,
            BattleCommandTelemetry.LastCommand);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_SyncBattleCommandTelemetry(
        int acceptedBattleCommandSeq,
        int spawnMonsterSeq,
        int useMagicScrollSeq,
        int activateSkillSeq,
        int rejectedBattleCommandCount,
        string lastCommand)
    {
        BattleCommandTelemetry.ApplySnapshot(
            acceptedBattleCommandSeq,
            spawnMonsterSeq,
            useMagicScrollSeq,
            activateSkillSeq,
            rejectedBattleCommandCount,
            lastCommand);
    }

    public UniTask<BattleCommandResult> ExecuteBattleSpawnMonsterCommandAsync(
        BattleSpawnMonsterCommand command,
        CommandExecutionScope scope,
        PlayerRef requestSource = default)
    {
        if (command == null)
        {
            return UniTask.FromResult(BattleCommandResult.Rejected(
                CommandType.BattleSpawnMonster,
                -1,
                "battle_spawn_command_missing",
                null,
                -1,
                scope));
        }

        return command.ExecuteAsync(scope, requestSource);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_RequestUseMagicScrollCommand(
        int casterPlayerId,
        int scrollSlotIndex,
        Vector3 targetWorldPosition,
        int observedOwnedMagicScrollRevision,
        string sourceReason,
        RpcInfo info = default)
    {
        if (Object == null || !Object.HasStateAuthority)
        {
            return;
        }

        string reason = string.IsNullOrWhiteSpace(sourceReason) ? "client_rpc" : sourceReason;
        var command = new UseMagicScrollCommand(
            casterPlayerId,
            scrollSlotIndex,
            targetWorldPosition,
            reason,
            observedOwnedMagicScrollRevision);

        var caster = GetPlayer(casterPlayerId);
        if (!IsRpcSourceAuthorizedForPlayer(caster, info.Source))
        {
            int sequence = BattleCommandTelemetry.RecordRejected(CommandType.UseMagicScroll);
            var rejected = BattleCommandResult.Rejected(
                CommandType.UseMagicScroll,
                casterPlayerId,
                "rpc_source_not_caster_input_authority",
                null,
                -1,
                CommandExecutionScope.ClientRequest,
                reason,
                sequence);
            UseMagicScrollMpTestLogger.Request(command);
            UseMagicScrollMpTestLogger.Rejected(rejected, command);
            SyncBattleCommandTelemetryToClientsIfAuthoritative();
            return;
        }

        RunLifecycleTask(
            command.ExecuteAsync(CommandExecutionScope.ClientRequest, info.Source),
            "RPC_RequestUseMagicScrollCommand/UseMagicScrollCommand");
    }

    public UniTask<BattleCommandResult> ExecuteUseMagicScrollCommandAsync(
        UseMagicScrollCommand command,
        CommandExecutionScope scope,
        PlayerRef requestSource = default)
    {
        if (command == null)
        {
            return UniTask.FromResult(BattleCommandResult.Rejected(
                CommandType.UseMagicScroll,
                -1,
                "use_magic_scroll_command_missing",
                null,
                -1,
                scope));
        }

        return command.ExecuteAsync(scope, requestSource);
    }
}
