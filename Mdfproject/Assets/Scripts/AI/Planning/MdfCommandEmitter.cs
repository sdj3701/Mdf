using Cysharp.Threading.Tasks;
using Fusion;
using UnityEngine;

public interface IMdfCommandEmitter
{
    bool TryEmit(MdfDecision decision, out BattleCommandResult result);
}

public abstract class MdfCommandEmitter : IMdfCommandEmitter
{
    protected readonly GameManagers GameManagers;
    protected readonly PlayerManager Actor;
    protected readonly CommandExecutionScope Scope;
    protected readonly string Source;

    protected MdfCommandEmitter(
        GameManagers gameManagers,
        PlayerManager actor,
        CommandExecutionScope scope,
        string source)
    {
        GameManagers = gameManagers;
        Actor = actor;
        Scope = scope;
        Source = string.IsNullOrWhiteSpace(source) ? "mdf_emitter" : source;
    }

    public bool TryEmit(MdfDecision decision, out BattleCommandResult result)
    {
        if (decision == null || !decision.HasCommandPayload)
        {
            result = BattleCommandResult.Rejected(
                CommandType.RequestSyncData,
                Actor != null ? Actor.playerId : -1,
                "decision_has_no_command",
                null,
                -1,
                Scope,
                Source);
            return false;
        }

        if (GameManagers == null)
        {
            result = BattleCommandResult.Rejected(
                decision.CommandType,
                decision.PlayerId,
                "game_managers_missing",
                null,
                -1,
                Scope,
                Source);
            return false;
        }

        switch (decision.Kind)
        {
            case MdfDecisionKind.Command:
                return EmitCommandStream(decision, out result);
            case MdfDecisionKind.BattleSpawnMonster:
                return EmitBattleSpawnMonster(decision, out result);
            case MdfDecisionKind.UseMagicScroll:
                return EmitUseMagicScroll(decision, out result);
            default:
                result = BattleCommandResult.Rejected(
                    decision.CommandType,
                    decision.PlayerId,
                    "unsupported_decision_kind",
                    decision.Kind.ToString(),
                    -1,
                    Scope,
                    Source);
                return false;
        }
    }

    protected virtual bool EmitCommandStream(MdfDecision decision, out BattleCommandResult result)
    {
        if (decision.Command == null)
        {
            result = BattleCommandResult.Rejected(
                decision.CommandType,
                decision.PlayerId,
                "command_payload_missing",
                null,
                -1,
                Scope,
                Source);
            return false;
        }

        if (GameManagers.CommandProcessor == null)
        {
            result = BattleCommandResult.Rejected(
                decision.CommandType,
                decision.PlayerId,
                "command_processor_missing",
                null,
                -1,
                Scope,
                Source);
            return false;
        }

        GameManagers.CommandProcessor.RequestCommandExecution(decision.Command);
        result = BattleCommandResult.Accepted(
            decision.CommandType,
            decision.PlayerId,
            "command_stream_submitted",
            -1,
            Scope,
            Source);
        LogSubmitted(decision, result);
        return true;
    }

    protected abstract bool EmitBattleSpawnMonster(MdfDecision decision, out BattleCommandResult result);
    protected abstract bool EmitUseMagicScroll(MdfDecision decision, out BattleCommandResult result);

    protected static PlayerRef ResolveRequestSource(PlayerManager actor)
    {
        PlayerRef requestSource = actor != null && actor.Object != null && actor.Object.IsValid
            ? actor.Object.InputAuthority
            : PlayerRef.None;
        if (requestSource == PlayerRef.None && actor != null && actor.Runner != null)
        {
            requestSource = actor.Runner.LocalPlayer;
        }

        return requestSource;
    }

    protected void LogSubmitted(MdfDecision decision, BattleCommandResult result)
    {
        if (!MPTestLogger.IsEnabled)
        {
            return;
        }

        MPTestLogger.Log(
            "mdf_decision_emit",
            result.Success ? "pass" : "fail",
            decision != null ? decision.CommandTypeName : "unknown",
            result.Message,
            MdfDecisionJournalFields.Build(decision, Actor, Source, result));
    }
}

public sealed class HumanClientCommandEmitter : MdfCommandEmitter
{
    public HumanClientCommandEmitter(GameManagers gameManagers, PlayerManager actor, string source = "human_client_emitter")
        : base(gameManagers, actor, CommandExecutionScope.ClientRequest, source)
    {
    }

    protected override bool EmitBattleSpawnMonster(MdfDecision decision, out BattleCommandResult result)
    {
        if (decision.BattleSpawnMonster == null)
        {
            result = BattleCommandResult.Rejected(CommandType.BattleSpawnMonster, decision.PlayerId, "battle_spawn_payload_missing", null, -1, Scope, Source);
            return false;
        }

        if (Actor == null)
        {
            result = BattleCommandResult.Rejected(CommandType.BattleSpawnMonster, decision.PlayerId, "actor_missing", null, -1, Scope, Source);
            LogSubmitted(decision, result);
            return false;
        }

        if (!Actor.HasAppliedCurrentAttackMonsterPoolSnapshot)
        {
            Actor.RPC_RequestSyncData();
            result = BattleCommandResult.Rejected(CommandType.BattleSpawnMonster, decision.PlayerId, "attack_pool_snapshot_not_current", null, -1, Scope, Source);
            LogSubmitted(decision, result);
            return false;
        }

        if (Actor.Object != null && Actor.Object.HasStateAuthority)
        {
            GameManagers.ExecuteBattleSpawnMonsterCommandAsync(
                decision.BattleSpawnMonster,
                CommandExecutionScope.ClientRequest,
                ResolveRequestSource(Actor)).Forget();
        }
        else
        {
            GameManagers.RPC_RequestBattleSpawnMonster(
                decision.BattleSpawnMonster.AttackerPlayerId,
                decision.BattleSpawnMonster.DefenderPlayerId,
                decision.BattleSpawnMonster.PoolSlotIndex,
                decision.BattleSpawnMonster.SpawnWorldPosition,
                decision.BattleSpawnMonster.Count,
                decision.BattleSpawnMonster.ObservedAttackMonsterPoolRevision,
                decision.BattleSpawnMonster.ObservedBlackMagicRevision,
                decision.BattleSpawnMonster.SourceReason);
            Actor.MarkAttackMonsterPoolCommandSubmitted(
                decision.BattleSpawnMonster.ObservedAttackMonsterPoolRevision,
                decision.BattleSpawnMonster.ObservedBlackMagicRevision);
        }

        result = BattleCommandResult.Accepted(CommandType.BattleSpawnMonster, decision.PlayerId, "battle_spawn_submitted", decision.BattleSpawnMonster.DefenderPlayerId, Scope, Source);
        LogSubmitted(decision, result);
        return true;
    }

    protected override bool EmitUseMagicScroll(MdfDecision decision, out BattleCommandResult result)
    {
        if (decision.UseMagicScroll == null)
        {
            result = BattleCommandResult.Rejected(CommandType.UseMagicScroll, decision.PlayerId, "use_magic_scroll_payload_missing", null, -1, Scope, Source);
            return false;
        }

        if (Actor == null)
        {
            result = BattleCommandResult.Rejected(CommandType.UseMagicScroll, decision.PlayerId, "actor_missing", null, -1, Scope, Source);
            LogSubmitted(decision, result);
            return false;
        }

        if (!Actor.HasAppliedCurrentOwnedMagicScrollSnapshot)
        {
            Actor.RPC_RequestSyncData();
            result = BattleCommandResult.Rejected(CommandType.UseMagicScroll, decision.PlayerId, "owned_scroll_snapshot_not_current", null, -1, Scope, Source);
            LogSubmitted(decision, result);
            return false;
        }

        if (Actor.Object != null && Actor.Object.HasStateAuthority)
        {
            GameManagers.ExecuteUseMagicScrollCommandAsync(
                decision.UseMagicScroll,
                CommandExecutionScope.ClientRequest,
                ResolveRequestSource(Actor)).Forget();
        }
        else
        {
            GameManagers.RPC_RequestUseMagicScrollCommand(
                decision.UseMagicScroll.CasterPlayerId,
                decision.UseMagicScroll.ScrollSlotIndex,
                decision.UseMagicScroll.TargetWorldPosition,
                decision.UseMagicScroll.ObservedOwnedMagicScrollRevision,
                decision.UseMagicScroll.SourceReason);
        }

        result = BattleCommandResult.Accepted(CommandType.UseMagicScroll, decision.PlayerId, "use_magic_scroll_submitted", -1, Scope, Source);
        LogSubmitted(decision, result);
        return true;
    }
}

public sealed class ServerAiCommandEmitter : MdfCommandEmitter
{
    public ServerAiCommandEmitter(GameManagers gameManagers, PlayerManager actor, string source = "server_ai_emitter")
        : base(gameManagers, actor, CommandExecutionScope.ServerAuthorityOnly, source)
    {
    }

    protected override bool EmitBattleSpawnMonster(MdfDecision decision, out BattleCommandResult result)
    {
        if (!HasStateAuthority(out result, decision))
        {
            return false;
        }

        if (decision.BattleSpawnMonster == null)
        {
            result = BattleCommandResult.Rejected(CommandType.BattleSpawnMonster, decision.PlayerId, "battle_spawn_payload_missing", null, -1, Scope, Source);
            return false;
        }

        GameManagers.ExecuteBattleSpawnMonsterCommandAsync(
            decision.BattleSpawnMonster,
            CommandExecutionScope.ServerAuthorityOnly).Forget();
        result = BattleCommandResult.Accepted(CommandType.BattleSpawnMonster, decision.PlayerId, "battle_spawn_authority_submitted", decision.BattleSpawnMonster.DefenderPlayerId, Scope, Source);
        LogSubmitted(decision, result);
        return true;
    }

    protected override bool EmitUseMagicScroll(MdfDecision decision, out BattleCommandResult result)
    {
        if (!HasStateAuthority(out result, decision))
        {
            return false;
        }

        if (decision.UseMagicScroll == null)
        {
            result = BattleCommandResult.Rejected(CommandType.UseMagicScroll, decision.PlayerId, "use_magic_scroll_payload_missing", null, -1, Scope, Source);
            return false;
        }

        GameManagers.ExecuteUseMagicScrollCommandAsync(
            decision.UseMagicScroll,
            CommandExecutionScope.ServerAuthorityOnly).Forget();
        result = BattleCommandResult.Accepted(CommandType.UseMagicScroll, decision.PlayerId, "use_magic_scroll_authority_submitted", -1, Scope, Source);
        LogSubmitted(decision, result);
        return true;
    }

    private bool HasStateAuthority(out BattleCommandResult result, MdfDecision decision)
    {
        if (GameManagers.Object == null || !GameManagers.Object.HasStateAuthority)
        {
            result = BattleCommandResult.Rejected(decision.CommandType, decision.PlayerId, "state_authority_required", null, -1, Scope, Source);
            LogSubmitted(decision, result);
            return false;
        }

        result = default(BattleCommandResult);
        return true;
    }
}

public sealed class TestAutomationCommandEmitter : MdfCommandEmitter
{
    private readonly HumanClientCommandEmitter _clientEmitter;

    public TestAutomationCommandEmitter(GameManagers gameManagers, PlayerManager actor, string source = "test_automation_emitter")
        : base(gameManagers, actor, CommandExecutionScope.ClientRequest, source)
    {
        _clientEmitter = new HumanClientCommandEmitter(gameManagers, actor, source);
    }

    protected override bool EmitCommandStream(MdfDecision decision, out BattleCommandResult result)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (!MPTestCommandLine.IsEnabled)
        {
            result = BattleCommandResult.Rejected(decision.CommandType, decision.PlayerId, "missing_mp_test", null, -1, Scope, Source);
            return false;
        }

        return _clientEmitter.TryEmit(decision, out result);
#else
        result = BattleCommandResult.Rejected(decision.CommandType, decision.PlayerId, "test_automation_not_available", null, -1, Scope, Source);
        return false;
#endif
    }

    protected override bool EmitBattleSpawnMonster(MdfDecision decision, out BattleCommandResult result)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (!MPTestCommandLine.IsEnabled)
        {
            result = BattleCommandResult.Rejected(decision.CommandType, decision.PlayerId, "missing_mp_test", null, -1, Scope, Source);
            return false;
        }

        return _clientEmitter.TryEmit(decision, out result);
#else
        result = BattleCommandResult.Rejected(decision.CommandType, decision.PlayerId, "test_automation_not_available", null, -1, Scope, Source);
        return false;
#endif
    }

    protected override bool EmitUseMagicScroll(MdfDecision decision, out BattleCommandResult result)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (!MPTestCommandLine.IsEnabled)
        {
            result = BattleCommandResult.Rejected(decision.CommandType, decision.PlayerId, "missing_mp_test", null, -1, Scope, Source);
            return false;
        }

        return _clientEmitter.TryEmit(decision, out result);
#else
        result = BattleCommandResult.Rejected(decision.CommandType, decision.PlayerId, "test_automation_not_available", null, -1, Scope, Source);
        return false;
#endif
    }
}
