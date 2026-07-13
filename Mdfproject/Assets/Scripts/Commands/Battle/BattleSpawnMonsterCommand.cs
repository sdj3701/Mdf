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
    private Vector2Int _validatedSpawnNavigationCell;
    private Vector3 _validatedSpawnWorldPosition;
    private int _validatedCount;
    private PlayerManager.BattleSpawnResourceReservation _resourceReservation;
    private bool _spawnRejectedLogged;

    public int AttackerPlayerId { get; }
    public int DefenderPlayerId { get; }
    public int PoolSlotIndex { get; }
    public Vector3 SpawnWorldPosition { get; }
    public int Count { get; }
    public string SourceReason { get; }
    public int ObservedAttackMonsterPoolRevision { get; }
    public int ObservedBlackMagicRevision { get; }
    public CommandExecutionScope Scope { get; private set; }

    public BattleSpawnMonsterCommand(
        int attackerPlayerId,
        int defenderPlayerId,
        int poolSlotIndex,
        Vector3 spawnWorldPosition,
        int count = 1,
        string sourceReason = null,
        int observedAttackMonsterPoolRevision = -1,
        int observedBlackMagicRevision = -1)
    {
        AttackerPlayerId = attackerPlayerId;
        DefenderPlayerId = defenderPlayerId;
        PoolSlotIndex = poolSlotIndex;
        SpawnWorldPosition = spawnWorldPosition;
        Count = count;
        SourceReason = sourceReason;
        ObservedAttackMonsterPoolRevision = observedAttackMonsterPoolRevision;
        ObservedBlackMagicRevision = observedBlackMagicRevision;
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
        _validatedSpawnNavigationCell = default;
        _validatedSpawnWorldPosition = default;
        _resourceReservation = default;

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

            if (ObservedBlackMagicRevision < 0)
            {
                return Reject("black_magic_revision_required", null, DefenderPlayerId, scope);
            }

            if (ObservedBlackMagicRevision != _validatedAttacker.BlackMagicRevision)
            {
                return Reject(
                    "black_magic_revision_mismatch",
                    $"observed={ObservedBlackMagicRevision},authority={_validatedAttacker.BlackMagicRevision}",
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

        if (!BattleCommandValidator.TryResolveExactBattleSpawnPosition(
                _validatedDefenderField,
                SpawnWorldPosition,
                out _validatedSpawnNavigationCell,
                out _validatedSpawnWorldPosition,
                out string spawnPositionReason))
        {
            return Reject(spawnPositionReason, null, DefenderPlayerId, scope);
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

        if (!_validatedAttacker.CanAffordAttackMonster(_validatedPoolEntry))
        {
            return Reject(
                "black_magic_insufficient",
                $"current={_validatedAttacker.BlackMagicCurrent},cost={Mathf.Max(0, _validatedPoolEntry.MonsterData.blackMagicCost)}",
                DefenderPlayerId,
                scope);
        }

        if (!BattleCommandValidator.TryValidateBattleSpawnPath(
                _validatedDefenderField,
                _validatedSpawnWorldPosition,
                _validatedPoolEntry.MonsterData,
                out string spawnPathReason))
        {
            return Reject(spawnPathReason, null, DefenderPlayerId, scope);
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

        int battleGeneration = _validatedAttacker.monsterSpawner.CaptureBattleGeneration();
        if (battleGeneration < 0)
        {
            return Reject("battle_generation_unavailable", null, DefenderPlayerId, Scope);
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

            int expectedBlackMagicRevision = ObservedBlackMagicRevision >= 0
                ? ObservedBlackMagicRevision
                : _validatedAttacker.BlackMagicRevision;
            int expectedPoolRevision = ObservedAttackMonsterPoolRevision >= 0
                ? ObservedAttackMonsterPoolRevision
                : _validatedAttacker.AttackMonsterPoolRevision;
            if (!_validatedAttacker.TryReserveBattleSpawnResource(
                    PoolSlotIndex,
                    expectedPoolRevision,
                    expectedBlackMagicRevision,
                    out _resourceReservation,
                    out string reservationReason))
            {
                return Reject(reservationReason ?? "battle_spawn_resource_reserve_failed", null, DefenderPlayerId, Scope);
            }

            bool spawnCommitted = false;
            try
            {

            await _validatedAttacker.monsterSpawner.PrewarmMonsterDataAsync(
                monsterData,
                isBoss,
                1,
                "BattleSpawnMonsterCommand");

            if (!_validatedAttacker.monsterSpawner.IsBattleGenerationCurrent(battleGeneration))
            {
                return Reject("battle_changed_during_spawn_prewarm", null, DefenderPlayerId, Scope);
            }

            if (!RevalidateAfterAwait(out string revalidationReason))
            {
                return Reject(revalidationReason, null, DefenderPlayerId, Scope);
            }

            var monster = await _validatedAttacker.monsterSpawner.SpawnMonsterAtExactPositionAsync(
                monsterData,
                _validatedSpawnWorldPosition,
                _validatedDefenderField,
                isBoss,
                bossUniqueId,
                originPlayerId,
                battleGeneration);

            if (!_validatedAttacker.monsterSpawner.IsBattleGenerationCurrent(battleGeneration))
            {
                _validatedAttacker.monsterSpawner.CleanupCanceledBattleSpawn(monster);
                return Reject("battle_changed_during_monster_spawn", null, DefenderPlayerId, Scope);
            }

            if (monster == null)
            {
                if (!BattleCommandValidator.TryResolveExactBattleSpawnPosition(
                        _validatedDefenderField,
                        SpawnWorldPosition,
                        out Vector2Int failedSpawnCell,
                        out Vector3 failedSpawnPosition,
                        out string exactSpawnFailureReason))
                {
                    return Reject(exactSpawnFailureReason, null, DefenderPlayerId, Scope);
                }

                if (failedSpawnCell != _validatedSpawnNavigationCell ||
                    (failedSpawnPosition - _validatedSpawnWorldPosition).sqrMagnitude > 0.0001f)
                {
                    return Reject("spawn_cell_changed_during_spawn", null, DefenderPlayerId, Scope);
                }

                if (!BattleCommandValidator.TryValidateBattleSpawnPath(
                        _validatedDefenderField,
                        _validatedSpawnWorldPosition,
                        monsterData,
                        out exactSpawnFailureReason))
                {
                    return Reject(exactSpawnFailureReason, null, DefenderPlayerId, Scope);
                }

                return Reject("battle_spawn_runner_spawn_failed", null, DefenderPlayerId, Scope);
            }

            if (!_validatedAttacker.CommitBattleSpawnReservation(_resourceReservation))
            {
                _validatedAttacker.monsterSpawner.CleanupCanceledBattleSpawn(monster);
                return Reject("battle_spawn_reservation_changed_before_commit", null, DefenderPlayerId, Scope);
            }

            spawnCommitted = true;

            if (isBoss)
            {
                _validatedAttacker.ConsumeOwnedBoss(monsterData);
            }

            spawnedCount++;
            }
            catch (System.Exception exception)
            {
                Debug.LogException(exception);
                return Reject("battle_spawn_exception", null, DefenderPlayerId, Scope);
            }
            finally
            {
                if (!spawnCommitted)
                {
                    _validatedAttacker.TryRefundBattleSpawnReservation(_resourceReservation);
                }
            }
        }

        if (spawnedCount <= 0)
        {
            return Reject("battle_spawn_no_monsters_spawned", null, DefenderPlayerId, Scope);
        }

        return Executed(spawnedCount, "battle_spawn_executed");
    }

    private bool RevalidateAfterAwait(out string reason)
    {
        reason = null;
        GameManagers gm = GameManagers.Instance;
        if (gm == null || gm.Object == null || !gm.Object.HasStateAuthority ||
            gm.IsSequenceTransitioning ||
            (gm.currentState != GameManagers.GameState.Battle1 && gm.currentState != GameManagers.GameState.Battle2))
        {
            reason = "battle_authority_or_phase_changed_during_await";
            return false;
        }

        if (gm.GetPlayer(AttackerPlayerId) != _validatedAttacker ||
            gm.GetPlayer(DefenderPlayerId) != _validatedDefender ||
            !BattleCommandValidator.IsCurrentBattleAttacker(_validatedAttacker) ||
            !BattleCommandValidator.IsCurrentBattleDefender(_validatedDefender))
        {
            reason = "battle_participants_changed_during_await";
            return false;
        }

        if (_validatedAttacker.AttackMonsterPool == null ||
            PoolSlotIndex < 0 ||
            PoolSlotIndex >= _validatedAttacker.AttackMonsterPool.Count)
        {
            reason = "battle_pool_slot_changed_during_await";
            return false;
        }

        MonsterPoolEntry currentEntry = _validatedAttacker.AttackMonsterPool[PoolSlotIndex];
        if (!ReferenceEquals(currentEntry, _validatedPoolEntry) ||
            currentEntry == null ||
            currentEntry.MonsterData != _validatedPoolEntry.MonsterData ||
            !_validatedAttacker.IsBattleSpawnReservationCurrent(_resourceReservation))
        {
            reason = "battle_pool_reservation_changed_during_await";
            return false;
        }

        if (!BattleCommandValidator.TryResolveExactBattleSpawnPosition(
                _validatedDefenderField,
                SpawnWorldPosition,
                out Vector2Int currentSpawnCell,
                out Vector3 currentSpawnWorldPosition,
                out reason))
        {
            return false;
        }

        if (currentSpawnCell != _validatedSpawnNavigationCell ||
            (currentSpawnWorldPosition - _validatedSpawnWorldPosition).sqrMagnitude > 0.0001f)
        {
            reason = "spawn_cell_changed_during_await";
            return false;
        }

        if (!BattleCommandValidator.TryValidateBattleSpawnPath(
                _validatedDefenderField,
                _validatedSpawnWorldPosition,
                _validatedPoolEntry.MonsterData,
                out reason))
        {
            return false;
        }

        return true;
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
