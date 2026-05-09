using Cysharp.Threading.Tasks;
using Fusion;
using UnityEngine;

public sealed class UseMagicScrollCommand
{
    private const CommandType Type = CommandType.UseMagicScroll;

    private PlayerManager _validatedCaster;
    private PlayerManager _validatedOpponent;
    private FieldManager _validatedTargetField;
    private MagicScrollData _validatedScroll;
    private bool _rejectedLogged;

    public int CasterPlayerId { get; }
    public int ScrollSlotIndex { get; }
    public Vector3 TargetWorldPosition { get; }
    public string SourceReason { get; }
    public int ObservedOwnedMagicScrollRevision { get; }
    public CommandExecutionScope Scope { get; private set; }

    public UseMagicScrollCommand(
        int casterPlayerId,
        int scrollSlotIndex,
        Vector3 targetWorldPosition,
        string sourceReason = null,
        int observedOwnedMagicScrollRevision = -1)
    {
        CasterPlayerId = casterPlayerId;
        ScrollSlotIndex = scrollSlotIndex;
        TargetWorldPosition = targetWorldPosition;
        SourceReason = sourceReason;
        ObservedOwnedMagicScrollRevision = observedOwnedMagicScrollRevision;
    }

    public async UniTask<BattleCommandResult> ExecuteAsync(
        CommandExecutionScope scope,
        PlayerRef requestSource = default)
    {
        Scope = scope;
        _rejectedLogged = false;
        UseMagicScrollMpTestLogger.Request(this);

        BattleCommandResult result = await ServerBattleCommandExecutor.TryExecuteAsync(
            Type,
            CasterPlayerId,
            scope,
            SourceReason,
            () => Validate(scope, requestSource),
            ExecuteValidatedAsync);

        if (!result.Success && !_rejectedLogged)
        {
            UseMagicScrollMpTestLogger.Rejected(result, this);
        }

        SyncTelemetryToClients();
        return result;
    }

    private BattleCommandResult Validate(CommandExecutionScope scope, PlayerRef requestSource)
    {
        _validatedCaster = null;
        _validatedOpponent = null;
        _validatedTargetField = null;
        _validatedScroll = null;

        var gm = GameManagers.Instance;
        if (!BattleCommandValidator.ResolveActorPlayer(
                gm,
                CasterPlayerId,
                Type,
                out _validatedCaster,
                out BattleCommandResult result,
                scope,
                SourceReason))
        {
            return Reject(result.ErrorCode, result.Message, result.OpponentPlayerId, scope);
        }

        if (!BattleCommandValidator.ResolveOpponent(
                gm,
                _validatedCaster,
                -1,
                Type,
                out _validatedOpponent,
                out result,
                scope,
                SourceReason))
        {
            return Reject(result.ErrorCode, result.Message, result.OpponentPlayerId, scope);
        }

        if (!BattleCommandValidator.IsCurrentBattleAttacker(_validatedCaster))
        {
            return Reject("caster_not_current_battle_attacker", null, _validatedOpponent != null ? _validatedOpponent.playerId : -1, scope);
        }

        if (!BattleCommandValidator.IsCurrentBattleDefender(_validatedOpponent))
        {
            return Reject("opponent_not_current_battle_defender", null, _validatedOpponent != null ? _validatedOpponent.playerId : -1, scope);
        }

        if (scope == CommandExecutionScope.ClientRequest)
        {
            if (!BattleCommandValidator.IsAuthorizedClientSource(_validatedCaster, requestSource))
            {
                return Reject("rpc_source_not_caster_input_authority", null, _validatedOpponent != null ? _validatedOpponent.playerId : -1, scope);
            }

            if (ObservedOwnedMagicScrollRevision < 0)
            {
                _validatedCaster.ResendOwnedMagicScrollsToClientsIfAuthoritative();
                return Reject("owned_scroll_revision_required", null, _validatedOpponent != null ? _validatedOpponent.playerId : -1, scope);
            }

            if (ObservedOwnedMagicScrollRevision != _validatedCaster.OwnedMagicScrollRevision)
            {
                _validatedCaster.ResendOwnedMagicScrollsToClientsIfAuthoritative();
                return Reject(
                    "owned_scroll_revision_mismatch",
                    $"observed={ObservedOwnedMagicScrollRevision},authority={_validatedCaster.OwnedMagicScrollRevision}",
                    _validatedOpponent != null ? _validatedOpponent.playerId : -1,
                    scope);
            }
        }
        else if (!BattleCommandValidator.IsServerAiOrTestAuthority(_validatedCaster, scope))
        {
            return Reject("server_ai_or_test_authority_required", null, _validatedOpponent != null ? _validatedOpponent.playerId : -1, scope);
        }

        _validatedTargetField = _validatedOpponent?.fieldManager;
        if (_validatedTargetField == null)
        {
            return Reject("target_field_missing", null, _validatedOpponent != null ? _validatedOpponent.playerId : -1, scope);
        }

        if (!BattleCommandValidator.IsFiniteTargetPosition(TargetWorldPosition))
        {
            return Reject("scroll_target_not_finite", null, _validatedOpponent.playerId, scope);
        }

        if (!_validatedCaster.TryGetMagicScrollAtSlot(ScrollSlotIndex, out _validatedScroll, out string slotReason))
        {
            return Reject(slotReason, null, _validatedOpponent.playerId, scope);
        }

        if (_validatedScroll.skillData == null)
        {
            return Reject("scroll_skill_data_missing", null, _validatedOpponent.playerId, scope);
        }

        if (!BattleCommandValidator.IsInsideScrollTargetDomain(_validatedScroll, _validatedTargetField, TargetWorldPosition))
        {
            return Reject("scroll_target_outside_battle_domain", null, _validatedOpponent.playerId, scope);
        }

        int sequence = BattleCommandTelemetry.RecordAccepted(Type);
        var accepted = BattleCommandResult.Accepted(
            Type,
            CasterPlayerId,
            "use_magic_scroll_validated",
            _validatedOpponent.playerId,
            scope,
            SourceReason,
            sequence);
        UseMagicScrollMpTestLogger.Accepted(accepted, this, _validatedScroll.name);
        return accepted;
    }

    private UniTask<BattleCommandResult> ExecuteValidatedAsync()
    {
        if (_validatedCaster == null || _validatedOpponent == null || _validatedTargetField == null || _validatedScroll == null)
        {
            return UniTask.FromResult(Reject("use_magic_scroll_validation_state_missing", null, -1, Scope));
        }

        if (!_validatedCaster.TryConsumeMagicScrollSlot(ScrollSlotIndex, out MagicScrollData consumedScroll, out string consumeReason))
        {
            return UniTask.FromResult(Reject(consumeReason, null, _validatedOpponent.playerId, Scope));
        }

        var casterGO = new GameObject($"ScrollCasterAuthority_{CasterPlayerId}_{consumedScroll.name}");
        casterGO.transform.position = TargetWorldPosition;

        var caster = casterGO.AddComponent<ScrollCaster>();
        caster.Initialize();
        bool applied = caster.CastGameplay(consumedScroll.skillData, out int targetCount);
        if (!applied)
        {
            _validatedCaster.TryRefundMagicScrollSlot(ScrollSlotIndex, consumedScroll);
            return UniTask.FromResult(Reject("scroll_gameplay_effect_failed", null, _validatedOpponent.playerId, Scope));
        }

        int sequence = BattleCommandTelemetry.RecordUseMagicScrollExecuted();
        var result = BattleCommandResult.Executed(
            Type,
            CasterPlayerId,
            $"scroll_effect_applied;targets={targetCount}",
            _validatedOpponent.playerId,
            Scope,
            SourceReason,
            sequence);
        UseMagicScrollMpTestLogger.EffectApplied(result, this, targetCount, consumedScroll.name);

        GameManagers.Instance?.RPC_BroadcastMagicScrollUsed(CasterPlayerId, consumedScroll.name, TargetWorldPosition);
        return UniTask.FromResult(result);
    }

    private BattleCommandResult Reject(
        string errorCode,
        string message,
        int opponentPlayerId,
        CommandExecutionScope scope)
    {
        int sequence = BattleCommandTelemetry.RecordRejected(Type);
        var result = BattleCommandResult.Rejected(
            Type,
            CasterPlayerId,
            errorCode,
            message,
            opponentPlayerId,
            scope,
            SourceReason,
            sequence);
        UseMagicScrollMpTestLogger.Rejected(result, this);
        _rejectedLogged = true;
        return result;
    }

    private static void SyncTelemetryToClients()
    {
        var gm = GameManagers.Instance;
        if (gm != null)
        {
            gm.SyncBattleCommandTelemetryToClientsIfAuthoritative();
        }
    }
}
