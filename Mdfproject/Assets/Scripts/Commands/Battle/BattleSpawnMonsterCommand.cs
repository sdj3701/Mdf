using Cysharp.Threading.Tasks;
using Fusion;
using UnityEngine;

public sealed class BattleSpawnMonsterCommand
{
    private const CommandType Type = CommandType.BattleSpawnMonster;

    private PlayerManager _validatedAttacker;
    private PlayerManager _validatedDefender;
    private FieldManager _validatedDefenderField;
    private MonsterPoolEntry _validatedPoolEntry;
    private int _validatedCount;
    private bool _spawnRejectedLogged;

    public int AttackerPlayerId { get; }
    public int DefenderPlayerId { get; }
    public int PoolSlotIndex { get; }
    public Vector3 SpawnWorldPosition { get; }
    public int Count { get; }
    public string SourceReason { get; }
    public int ObservedAttackMonsterPoolRevision { get; }
    public CommandExecutionScope Scope { get; private set; }

    public BattleSpawnMonsterCommand(
        int attackerPlayerId,
        int defenderPlayerId,
        int poolSlotIndex,
        Vector3 spawnWorldPosition,
        int count = 1,
        string sourceReason = null,
        int observedAttackMonsterPoolRevision = -1)
    {
        AttackerPlayerId = attackerPlayerId;
        DefenderPlayerId = defenderPlayerId;
        PoolSlotIndex = poolSlotIndex;
        SpawnWorldPosition = spawnWorldPosition;
        Count = count;
        SourceReason = sourceReason;
        ObservedAttackMonsterPoolRevision = observedAttackMonsterPoolRevision;
    }

    public async UniTask<BattleCommandResult> ExecuteAsync(
        CommandExecutionScope scope,
        PlayerRef requestSource = default)
    {
        Scope = scope;
        _spawnRejectedLogged = false;
        BattleSpawnMonsterMpTestLogger.Request(this);

        BattleCommandResult result = await ServerBattleCommandExecutor.TryExecuteAsync(
            Type,
            AttackerPlayerId,
            scope,
            SourceReason,
            () => Validate(scope, requestSource),
            ExecuteValidatedAsync);

        if (!result.Success && !_spawnRejectedLogged)
        {
            BattleSpawnMonsterMpTestLogger.Rejected(result, this);
        }

        SyncTelemetryToClients();
        return result;
    }

    private BattleCommandResult Validate(CommandExecutionScope scope, PlayerRef requestSource)
    {
        _validatedAttacker = null;
        _validatedDefender = null;
        _validatedDefenderField = null;
        _validatedPoolEntry = null;

        var gm = GameManagers.Instance;
        if (!BattleCommandValidator.ResolveActorPlayer(
                gm,
                AttackerPlayerId,
                Type,
                out _validatedAttacker,
                out BattleCommandResult result,
                scope,
                SourceReason))
        {
            return Reject(result.ErrorCode, result.Message, result.OpponentPlayerId, scope);
        }

        if (!BattleCommandValidator.ResolveOpponent(
                gm,
                _validatedAttacker,
                DefenderPlayerId,
                Type,
                out _validatedDefender,
                out result,
                scope,
                SourceReason))
        {
            return Reject(result.ErrorCode, result.Message, result.OpponentPlayerId, scope);
        }

        if (!BattleCommandValidator.IsCurrentBattleAttacker(_validatedAttacker))
        {
            return Reject("attacker_not_current_battle_attacker", null, DefenderPlayerId, scope);
        }

        if (!BattleCommandValidator.IsCurrentBattleDefender(_validatedDefender))
        {
            return Reject("defender_not_current_battle_defender", null, DefenderPlayerId, scope);
        }

        if (scope == CommandExecutionScope.ClientRequest)
        {
            if (!BattleCommandValidator.IsAuthorizedClientSource(_validatedAttacker, requestSource))
            {
                return Reject("rpc_source_not_attacker_input_authority", null, DefenderPlayerId, scope);
            }

            if (ObservedAttackMonsterPoolRevision < 0)
            {
                _validatedAttacker.ResendAttackMonsterPoolToClientsIfAuthoritative();
                return Reject("attack_pool_revision_required", null, DefenderPlayerId, scope);
            }

            if (ObservedAttackMonsterPoolRevision != _validatedAttacker.AttackMonsterPoolRevision)
            {
                _validatedAttacker.ResendAttackMonsterPoolToClientsIfAuthoritative();
                return Reject(
                    "attack_pool_revision_mismatch",
                    $"observed={ObservedAttackMonsterPoolRevision},authority={_validatedAttacker.AttackMonsterPoolRevision}",
                    DefenderPlayerId,
                    scope);
            }
        }
        else if (!BattleCommandValidator.IsServerAiOrTestAuthority(_validatedAttacker, scope))
        {
            return Reject("server_ai_or_test_authority_required", null, DefenderPlayerId, scope);
        }

        _validatedDefenderField = _validatedDefender.fieldManager;
        if (_validatedDefenderField == null)
        {
            return Reject("defender_field_missing", null, DefenderPlayerId, scope);
        }

        if (!BattleCommandValidator.IsFiniteTargetPosition(SpawnWorldPosition))
        {
            return Reject("spawn_position_not_finite", null, DefenderPlayerId, scope);
        }

        if (!BattleCommandValidator.IsInsideBattleSpawnZone(_validatedDefenderField, SpawnWorldPosition))
        {
            return Reject("spawn_position_outside_battle_spawn_zone", null, DefenderPlayerId, scope);
        }

        if (!TryResolvePoolSlot(_validatedAttacker, out _validatedPoolEntry, out string poolReason))
        {
            return Reject(poolReason, null, DefenderPlayerId, scope);
        }

        _validatedCount = 1;
        if (Count != 1)
        {
            return Reject("battle_spawn_count_must_be_one", $"count={Count}", DefenderPlayerId, scope);
        }

        int availableCount = _validatedPoolEntry.RemainingCount;
        if (availableCount < _validatedCount)
        {
            return Reject("pool_slot_insufficient_count", $"available={availableCount},requested={_validatedCount}", DefenderPlayerId, scope);
        }

        int sequence = BattleCommandTelemetry.RecordAccepted(Type);
        var accepted = BattleCommandResult.Accepted(
            Type,
            AttackerPlayerId,
            "battle_spawn_validated",
            DefenderPlayerId,
            scope,
            SourceReason,
            sequence);
        BattleSpawnMonsterMpTestLogger.Accepted(accepted, this);
        return accepted;
    }

    private async UniTask<BattleCommandResult> ExecuteValidatedAsync()
    {
        if (_validatedAttacker == null ||
            _validatedDefender == null ||
            _validatedDefenderField == null ||
            _validatedPoolEntry == null)
        {
            return Reject("battle_spawn_validation_state_missing", null, DefenderPlayerId, Scope);
        }

        if (_validatedAttacker.monsterSpawner == null)
        {
            return Reject("attacker_monster_spawner_missing", null, DefenderPlayerId, Scope);
        }

        int spawnedCount = 0;
        for (int i = 0; i < _validatedCount; i++)
        {
            if (_validatedPoolEntry.IsEmpty)
            {
                return Reject("pool_slot_empty_before_spawn", null, DefenderPlayerId, Scope);
            }

            bool isBoss = _validatedPoolEntry.IsBoss;
            int bossUniqueId = ResolveBossUniqueId(_validatedPoolEntry);
            int originPlayerId = ResolveOriginPlayerId(_validatedPoolEntry);
            MonsterData monsterData = _validatedPoolEntry.MonsterData;

            if (!_validatedAttacker.TryConsumeMonsterPoolSlot(PoolSlotIndex))
            {
                return Reject("pool_slot_consume_failed_before_spawn", null, DefenderPlayerId, Scope);
            }

            var monster = await _validatedAttacker.monsterSpawner.SpawnMonsterAtPositionAsync(
                monsterData,
                SpawnWorldPosition,
                _validatedDefenderField,
                isBoss,
                bossUniqueId,
                originPlayerId);

            if (monster == null)
            {
                _validatedAttacker.TryRefundMonsterPoolSlot(PoolSlotIndex);
                return Reject("battle_spawn_runner_spawn_failed", null, DefenderPlayerId, Scope);
            }

            if (isBoss)
            {
                _validatedAttacker.ConsumeOwnedBoss(monsterData);
            }

            spawnedCount++;
        }

        if (spawnedCount <= 0)
        {
            return Reject("battle_spawn_no_monsters_spawned", null, DefenderPlayerId, Scope);
        }

        return Executed(spawnedCount, "battle_spawn_executed");
    }

    private bool TryResolvePoolSlot(PlayerManager attacker, out MonsterPoolEntry entry, out string reason)
    {
        entry = null;
        reason = null;
        if (attacker == null || attacker.AttackMonsterPool == null)
        {
            reason = "attack_monster_pool_missing";
            return false;
        }

        if (PoolSlotIndex < 0 || PoolSlotIndex >= attacker.AttackMonsterPool.Count)
        {
            reason = "pool_slot_out_of_range";
            return false;
        }

        entry = attacker.AttackMonsterPool[PoolSlotIndex];
        if (entry == null)
        {
            reason = "pool_slot_missing";
            return false;
        }

        if (entry.MonsterData == null)
        {
            reason = "pool_slot_monster_data_missing";
            return false;
        }

        if (entry.IsEmpty)
        {
            reason = "pool_slot_empty";
            return false;
        }

        return true;
    }

    private int ResolveBossUniqueId(MonsterPoolEntry entry)
    {
        if (entry == null || !entry.IsBoss)
        {
            return -1;
        }

        if (entry.BossUniqueId > 0)
        {
            return entry.BossUniqueId;
        }

        return SurvivorBossManager.Instance != null
            ? SurvivorBossManager.Instance.GetNextBossUniqueId()
            : -1;
    }

    private int ResolveOriginPlayerId(MonsterPoolEntry entry)
    {
        if (entry == null || !entry.IsBoss)
        {
            return -1;
        }

        return entry.OriginPlayerId >= 0 ? entry.OriginPlayerId : AttackerPlayerId;
    }

    private BattleCommandResult Executed(int spawnedCount, string message)
    {
        int sequence = BattleCommandTelemetry.RecordSpawnMonsterExecuted();
        var result = BattleCommandResult.Executed(
            Type,
            AttackerPlayerId,
            $"{message};spawned={spawnedCount}",
            DefenderPlayerId,
            Scope,
            SourceReason,
            sequence);
        BattleSpawnMonsterMpTestLogger.Executed(result, this);
        return result;
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
            AttackerPlayerId,
            errorCode,
            message,
            opponentPlayerId,
            scope,
            SourceReason,
            sequence);
        BattleSpawnMonsterMpTestLogger.Rejected(result, this);
        _spawnRejectedLogged = true;
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
