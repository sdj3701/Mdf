using Fusion;
using UnityEngine;

public partial class PlayerManager
{
    private const int FallbackBaseBlackMagicMaximum = 10;
    private const int FallbackBlackMagicMaximumPerRound = 2;

    [Networked] public int BlackMagicCurrent { get; private set; }
    [Networked] public int BlackMagicMaximum { get; private set; }
    [Networked] public int BlackMagicMaxBonus { get; private set; }
    [Networked] public int BlackMagicRevision { get; private set; }
    [Networked] public int BlackMagicSequenceId { get; private set; }

    private int _lastPublishedBlackMagicRevision = -1;
    private int _lastAppliedBlackMagicCurrent;
    private int _lastAppliedBlackMagicMaximum;
    private int _lastAppliedBlackMagicMaxBonus;
    private int _lastAppliedBlackMagicRevision = -1;
    private int _lastAppliedBlackMagicSequenceId;
    private int _nextBattleSpawnReservationId;
    private int _activeBattleSpawnReservationId = -1;
    private int _activeBattleSpawnSequenceId = -1;
    private int _activeBattleSpawnPoolSlot = -1;
    private MonsterData _activeBattleSpawnMonsterData;

    public int AppliedBlackMagicCurrent => HasBlackMagicStateAuthority
        ? BlackMagicCurrent
        : _lastAppliedBlackMagicRevision >= BlackMagicRevision ? _lastAppliedBlackMagicCurrent : BlackMagicCurrent;
    public int AppliedBlackMagicMaximum => HasBlackMagicStateAuthority
        ? BlackMagicMaximum
        : _lastAppliedBlackMagicRevision >= BlackMagicRevision ? _lastAppliedBlackMagicMaximum : BlackMagicMaximum;
    public int AppliedBlackMagicMaxBonus => HasBlackMagicStateAuthority
        ? BlackMagicMaxBonus
        : _lastAppliedBlackMagicRevision >= BlackMagicRevision ? _lastAppliedBlackMagicMaxBonus : BlackMagicMaxBonus;
    public int AppliedBlackMagicRevision => HasBlackMagicStateAuthority
        ? BlackMagicRevision
        : Mathf.Max(_lastAppliedBlackMagicRevision, BlackMagicRevision);
    public int AppliedBlackMagicSequenceId => HasBlackMagicStateAuthority
        ? BlackMagicSequenceId
        : _lastAppliedBlackMagicRevision >= BlackMagicRevision ? _lastAppliedBlackMagicSequenceId : BlackMagicSequenceId;

    private bool HasBlackMagicStateAuthority => Object != null && Object.IsValid && Object.HasStateAuthority;

    public readonly struct BattleSpawnResourceReservation
    {
        public int Id { get; }
        public int SequenceId { get; }
        public int PoolSlotIndex { get; }
        public MonsterData MonsterData { get; }
        public bool IsBoss { get; }
        public int BlackMagicCost { get; }
        public int BlackMagicRevisionAfterReserve { get; }
        public int PoolRevisionAfterReserve { get; }
        public int RemainingCountAfterReserve { get; }

        public bool IsValid => Id > 0 && MonsterData != null;

        public BattleSpawnResourceReservation(
            int id,
            int sequenceId,
            int poolSlotIndex,
            MonsterData monsterData,
            bool isBoss,
            int blackMagicCost,
            int blackMagicRevisionAfterReserve,
            int poolRevisionAfterReserve,
            int remainingCountAfterReserve)
        {
            Id = id;
            SequenceId = sequenceId;
            PoolSlotIndex = poolSlotIndex;
            MonsterData = monsterData;
            IsBoss = isBoss;
            BlackMagicCost = blackMagicCost;
            BlackMagicRevisionAfterReserve = blackMagicRevisionAfterReserve;
            PoolRevisionAfterReserve = poolRevisionAfterReserve;
            RemainingCountAfterReserve = remainingCountAfterReserve;
        }
    }

    public static int CalculateBlackMagicSequenceId(int round, GameManagers.GameState state)
    {
        int phase = state == GameManagers.GameState.Battle1
            ? 1
            : state == GameManagers.GameState.Battle2 ? 2 : 0;
        return phase == 0 ? -1 : Mathf.Max(1, round) * 10 + phase;
    }

    public static int CalculateBlackMagicMaximum(int round, int baseMaximum, int perRoundGrowth, int personalBonus)
    {
        return Mathf.Max(0, baseMaximum)
             + Mathf.Max(0, Mathf.Max(1, round) - 1) * Mathf.Max(0, perRoundGrowth)
             + Mathf.Max(0, personalBonus);
    }

    public bool BeginAttackSequenceBlackMagic(int round, GameManagers.GameState state)
    {
        if (!HasStateAuthorityOrNoNetwork())
        {
            return false;
        }

        int sequenceId = CalculateBlackMagicSequenceId(round, state);
        if (sequenceId < 0 || BlackMagicSequenceId == sequenceId)
        {
            return false;
        }

        WaveDatabase waveDatabase = AddressablesManager.Instance?.WaveDatabase;
        int maximum = waveDatabase != null
            ? waveDatabase.GetBlackMagicMaximumForRound(round, BlackMagicMaxBonus)
            : CalculateBlackMagicMaximum(
                round,
                FallbackBaseBlackMagicMaximum,
                FallbackBlackMagicMaximumPerRound,
                BlackMagicMaxBonus);

        BlackMagicSequenceId = sequenceId;
        BlackMagicMaximum = maximum;
        BlackMagicCurrent = maximum;
        BlackMagicRevision++;
        ClearActiveBattleSpawnReservation();
        PublishBlackMagicChanged();
        return true;
    }

    public void AddBlackMagicMaximumBonus(int amount)
    {
        if (amount <= 0 || !HasStateAuthorityOrNoNetwork())
        {
            return;
        }

        BlackMagicMaxBonus = Mathf.Max(0, BlackMagicMaxBonus + amount);
        BlackMagicRevision++;
        PublishBlackMagicChanged();
    }

    public bool CanAffordAttackMonster(MonsterPoolEntry entry)
    {
        if (entry == null || entry.MonsterData == null || entry.IsEmpty)
        {
            return false;
        }

        return entry.IsBoss || AppliedBlackMagicCurrent >= Mathf.Max(0, entry.MonsterData.blackMagicCost);
    }

    public bool TryReserveBattleSpawnResource(
        int poolSlotIndex,
        int expectedPoolRevision,
        int expectedBlackMagicRevision,
        out BattleSpawnResourceReservation reservation,
        out string reason)
    {
        reservation = default;
        reason = null;

        if (!HasStateAuthorityOrNoNetwork())
        {
            reason = "battle_spawn_resource_authority_required";
            return false;
        }

        if (_activeBattleSpawnReservationId > 0)
        {
            reason = "battle_spawn_resource_reservation_busy";
            return false;
        }

        if (expectedPoolRevision != AttackMonsterPoolRevision)
        {
            reason = "attack_pool_revision_changed_before_reserve";
            return false;
        }

        if (expectedBlackMagicRevision != BlackMagicRevision)
        {
            reason = "black_magic_revision_changed_before_reserve";
            return false;
        }

        if (AttackMonsterPool == null || poolSlotIndex < 0 || poolSlotIndex >= AttackMonsterPool.Count)
        {
            reason = "pool_slot_out_of_range_before_reserve";
            return false;
        }

        MonsterPoolEntry entry = AttackMonsterPool[poolSlotIndex];
        if (entry == null || entry.MonsterData == null || entry.IsEmpty)
        {
            reason = "pool_slot_empty_before_reserve";
            return false;
        }

        int cost = entry.IsBoss ? 0 : Mathf.Max(0, entry.MonsterData.blackMagicCost);
        if (!entry.IsBoss && BlackMagicCurrent < cost)
        {
            reason = "black_magic_insufficient";
            return false;
        }

        int reservationId = ++_nextBattleSpawnReservationId;
        if (reservationId <= 0)
        {
            _nextBattleSpawnReservationId = 1;
            reservationId = 1;
        }

        _activeBattleSpawnReservationId = reservationId;
        _activeBattleSpawnSequenceId = BlackMagicSequenceId;
        _activeBattleSpawnPoolSlot = poolSlotIndex;
        _activeBattleSpawnMonsterData = entry.MonsterData;

        reservation = new BattleSpawnResourceReservation(
            reservationId,
            BlackMagicSequenceId,
            poolSlotIndex,
            entry.MonsterData,
            entry.IsBoss,
            cost,
            BlackMagicRevision,
            AttackMonsterPoolRevision,
            entry.RemainingCount);
        return true;
    }

    public bool IsBattleSpawnReservationCurrent(BattleSpawnResourceReservation reservation)
    {
        if (!reservation.IsValid ||
            _activeBattleSpawnReservationId != reservation.Id ||
            _activeBattleSpawnSequenceId != reservation.SequenceId ||
            _activeBattleSpawnPoolSlot != reservation.PoolSlotIndex ||
            _activeBattleSpawnMonsterData != reservation.MonsterData ||
            BlackMagicSequenceId != reservation.SequenceId ||
            AttackMonsterPool == null ||
            reservation.PoolSlotIndex < 0 ||
            reservation.PoolSlotIndex >= AttackMonsterPool.Count)
        {
            return false;
        }

        MonsterPoolEntry entry = AttackMonsterPool[reservation.PoolSlotIndex];
        if (entry == null || entry.MonsterData != reservation.MonsterData)
        {
            return false;
        }

        return reservation.IsBoss
            ? AttackMonsterPoolRevision == reservation.PoolRevisionAfterReserve &&
              entry.RemainingCount == reservation.RemainingCountAfterReserve
            : BlackMagicCurrent >= reservation.BlackMagicCost;
    }

    public bool CommitBattleSpawnReservation(BattleSpawnResourceReservation reservation)
    {
        if (!IsBattleSpawnReservationCurrent(reservation))
        {
            return false;
        }

        MonsterPoolEntry entry = AttackMonsterPool[reservation.PoolSlotIndex];
        if (reservation.IsBoss)
        {
            if (!entry.TryConsume())
            {
                return false;
            }

            SyncAttackMonsterPoolToClientsIfAuthoritative();
        }
        else
        {
            if (BlackMagicCurrent < reservation.BlackMagicCost)
            {
                return false;
            }

            BlackMagicCurrent -= reservation.BlackMagicCost;
            BlackMagicRevision++;
            PublishBlackMagicChanged();
        }

        ClearActiveBattleSpawnReservation();
        return true;
    }

    public bool TryRefundBattleSpawnReservation(BattleSpawnResourceReservation reservation)
    {
        if (!reservation.IsValid || _activeBattleSpawnReservationId != reservation.Id)
        {
            return false;
        }

        // Reservation is provisional: resources are committed only after a successful spawn.
        // Failure therefore rolls back by clearing the reservation without mutating public state.
        ClearActiveBattleSpawnReservation();
        return true;
    }

    public bool RestoreBlackMagicAfterHostMigration(
        int current,
        int maximum,
        int maxBonus,
        int revision,
        int sequenceId,
        string context)
    {
        if (Object == null || !Object.IsValid || !Object.HasStateAuthority)
        {
            return false;
        }

        BlackMagicMaximum = Mathf.Max(0, maximum);
        BlackMagicCurrent = Mathf.Clamp(current, 0, BlackMagicMaximum);
        BlackMagicMaxBonus = Mathf.Max(0, maxBonus);
        BlackMagicRevision = Mathf.Max(0, revision);
        BlackMagicSequenceId = sequenceId;
        ClearActiveBattleSpawnReservation();
        PublishBlackMagicChanged();
        Debug.Log($"[PlayerManager] HostMigration black magic restore complete ({context}) P{playerId} current={BlackMagicCurrent}/{BlackMagicMaximum} bonus={BlackMagicMaxBonus} revision={BlackMagicRevision} sequence={BlackMagicSequenceId}");
        return BlackMagicCurrent == Mathf.Clamp(current, 0, Mathf.Max(0, maximum))
            && BlackMagicMaximum == Mathf.Max(0, maximum)
            && BlackMagicMaxBonus == Mathf.Max(0, maxBonus)
            && BlackMagicRevision == Mathf.Max(0, revision)
            && BlackMagicSequenceId == sequenceId;
    }

    public void ApplyBlackMagicPresentationSnapshot(
        int current,
        int maximum,
        int maxBonus,
        int revision,
        int sequenceId)
    {
        if (HasBlackMagicStateAuthority || revision < _lastAppliedBlackMagicRevision)
        {
            return;
        }

        _lastAppliedBlackMagicMaximum = Mathf.Max(0, maximum);
        _lastAppliedBlackMagicCurrent = Mathf.Clamp(current, 0, _lastAppliedBlackMagicMaximum);
        _lastAppliedBlackMagicMaxBonus = Mathf.Max(0, maxBonus);
        _lastAppliedBlackMagicRevision = Mathf.Max(0, revision);
        _lastAppliedBlackMagicSequenceId = sequenceId;
        GameEvents.TriggerBlackMagicChanged(
            playerId,
            _lastAppliedBlackMagicCurrent,
            _lastAppliedBlackMagicMaximum,
            _lastAppliedBlackMagicMaxBonus,
            _lastAppliedBlackMagicRevision);
    }

    private void PublishBlackMagicChanged()
    {
        _lastPublishedBlackMagicRevision = BlackMagicRevision;
        GameEvents.TriggerBlackMagicChanged(
            playerId,
            BlackMagicCurrent,
            BlackMagicMaximum,
            BlackMagicMaxBonus,
            BlackMagicRevision);
    }

    private void PublishBlackMagicChangedFromRenderIfNeeded()
    {
        if (!HasBlackMagicStateAuthority && BlackMagicRevision < _lastAppliedBlackMagicRevision)
        {
            return;
        }

        if (!HasBlackMagicStateAuthority && BlackMagicRevision >= _lastAppliedBlackMagicRevision)
        {
            _lastAppliedBlackMagicCurrent = BlackMagicCurrent;
            _lastAppliedBlackMagicMaximum = BlackMagicMaximum;
            _lastAppliedBlackMagicMaxBonus = BlackMagicMaxBonus;
            _lastAppliedBlackMagicRevision = BlackMagicRevision;
            _lastAppliedBlackMagicSequenceId = BlackMagicSequenceId;
        }

        if (_lastPublishedBlackMagicRevision != BlackMagicRevision)
        {
            PublishBlackMagicChanged();
        }
    }

    private void ClearActiveBattleSpawnReservation()
    {
        _activeBattleSpawnReservationId = -1;
        _activeBattleSpawnSequenceId = -1;
        _activeBattleSpawnPoolSlot = -1;
        _activeBattleSpawnMonsterData = null;
    }
}
