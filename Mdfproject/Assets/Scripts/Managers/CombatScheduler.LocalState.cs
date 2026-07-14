using System;
using System.Collections.Generic;
using Fusion;

internal sealed class CombatSchedulerSlotIndex
{
    private readonly int[] _activeSlots;
    private readonly int[] _freeSlots;
    private readonly int[] _activePositions;
    private readonly bool[] _freeFlags;

    public CombatSchedulerSlotIndex(int capacity)
    {
        Capacity = Math.Max(1, capacity);
        _activeSlots = new int[Capacity];
        _freeSlots = new int[Capacity];
        _activePositions = new int[Capacity];
        _freeFlags = new bool[Capacity];
        BeginRebuild();
    }

    public int Capacity { get; }
    public int ActiveCount { get; private set; }
    public int FreeCount { get; private set; }

    public void BeginRebuild()
    {
        ActiveCount = 0;
        FreeCount = 0;
        for (int i = 0; i < Capacity; i++)
        {
            _activePositions[i] = -1;
            _freeFlags[i] = false;
        }
    }

    public void AddActiveFromOrderedRebuild(int slot)
    {
        if (!IsValidSlot(slot) || _activePositions[slot] >= 0)
        {
            return;
        }

        _activePositions[slot] = ActiveCount;
        _activeSlots[ActiveCount++] = slot;
    }

    public void AddFreeFromOrderedRebuild(int slot)
    {
        if (!IsValidSlot(slot) || _freeFlags[slot])
        {
            return;
        }

        _freeFlags[slot] = true;
        _freeSlots[FreeCount++] = slot;
    }

    public bool TryRentLowest(out int slot)
    {
        if (FreeCount <= 0)
        {
            slot = -1;
            return false;
        }

        slot = _freeSlots[0];
        for (int i = 1; i < FreeCount; i++)
        {
            _freeSlots[i - 1] = _freeSlots[i];
        }

        FreeCount--;
        _freeFlags[slot] = false;
        return true;
    }

    public bool CommitRentedSlot(int slot)
    {
        if (!IsValidSlot(slot) || _activePositions[slot] >= 0)
        {
            return false;
        }

        int insert = ActiveCount;
        while (insert > 0 && _activeSlots[insert - 1] > slot)
        {
            int shiftedSlot = _activeSlots[insert - 1];
            _activeSlots[insert] = shiftedSlot;
            _activePositions[shiftedSlot] = insert;
            insert--;
        }

        _activeSlots[insert] = slot;
        _activePositions[slot] = insert;
        ActiveCount++;
        return true;
    }

    public bool ReleaseActiveSlot(int slot)
    {
        if (!IsValidSlot(slot))
        {
            return false;
        }

        int position = _activePositions[slot];
        if (position < 0)
        {
            return false;
        }

        for (int i = position + 1; i < ActiveCount; i++)
        {
            int shiftedSlot = _activeSlots[i];
            _activeSlots[i - 1] = shiftedSlot;
            _activePositions[shiftedSlot] = i - 1;
        }

        ActiveCount--;
        _activePositions[slot] = -1;
        InsertFreeSlot(slot);
        return true;
    }

    public bool IsActive(int slot)
    {
        return IsValidSlot(slot) && _activePositions[slot] >= 0;
    }

    public int GetActiveSlot(int position)
    {
        return position >= 0 && position < ActiveCount ? _activeSlots[position] : -1;
    }

    private void InsertFreeSlot(int slot)
    {
        if (_freeFlags[slot])
        {
            return;
        }

        int insert = FreeCount;
        while (insert > 0 && _freeSlots[insert - 1] > slot)
        {
            _freeSlots[insert] = _freeSlots[insert - 1];
            insert--;
        }

        _freeSlots[insert] = slot;
        _freeFlags[slot] = true;
        FreeCount++;
    }

    private bool IsValidSlot(int slot)
    {
        return slot >= 0 && slot < Capacity;
    }
}

internal struct CombatSchedulerSlotMask
{
    private ulong _low;
    private ulong _high;

    public bool IsEmpty => _low == 0UL && _high == 0UL;

    public void Add(int slot)
    {
        if (slot < 0 || slot >= 128)
        {
            return;
        }

        if (slot < 64)
        {
            _low |= 1UL << slot;
        }
        else
        {
            _high |= 1UL << (slot - 64);
        }
    }

    public void Remove(int slot)
    {
        if (slot < 0 || slot >= 128)
        {
            return;
        }

        if (slot < 64)
        {
            _low &= ~(1UL << slot);
        }
        else
        {
            _high &= ~(1UL << (slot - 64));
        }
    }

    public bool Contains(int slot)
    {
        if (slot < 0 || slot >= 128)
        {
            return false;
        }

        return slot < 64
            ? (_low & (1UL << slot)) != 0UL
            : (_high & (1UL << (slot - 64))) != 0UL;
    }

    public int Count(int capacity)
    {
        int count = 0;
        int limit = Math.Min(128, Math.Max(0, capacity));
        for (int slot = 0; slot < limit; slot++)
        {
            if (Contains(slot))
            {
                count++;
            }
        }

        return count;
    }
}

public partial class CombatScheduler
{
    private const int NoScheduledWorkTick = int.MaxValue;

    private readonly CombatSchedulerSlotIndex _pendingFireSlotIndex = new CombatSchedulerSlotIndex(PendingFireCapacity);
    private readonly CombatSchedulerSlotIndex _pendingHitSlotIndex = new CombatSchedulerSlotIndex(PendingHitCapacity);
    private readonly CombatSchedulerSlotIndex _statusSlotIndex = new CombatSchedulerSlotIndex(MaxActiveStatusEffects);
    private readonly CombatSchedulerSlotIndex _statBuffSlotIndex = new CombatSchedulerSlotIndex(MaxActiveStatBuffs);
    private readonly CombatSchedulerSlotIndex _zoneSlotIndex = new CombatSchedulerSlotIndex(MaxActiveZones);

    private readonly Dictionary<int, int> _pendingFireSlotBySequence = new Dictionary<int, int>(PendingFireCapacity);
    private readonly Dictionary<int, int> _pendingHitSlotBySequence = new Dictionary<int, int>(PendingHitCapacity);
    private readonly Dictionary<int, int> _zoneSlotBySequence = new Dictionary<int, int>(MaxActiveZones);
    private readonly Dictionary<uint, CombatSchedulerSlotMask> _statusSlotsByTarget = new Dictionary<uint, CombatSchedulerSlotMask>(MaxActiveStatusEffects);
    private readonly Dictionary<uint, CombatSchedulerSlotMask> _statBuffSlotsByTarget = new Dictionary<uint, CombatSchedulerSlotMask>(MaxActiveStatBuffs);

    private readonly LocalSlotToken[] _statusSlotScratch = new LocalSlotToken[MaxActiveStatusEffects];
    private readonly LocalSlotToken[] _statBuffSlotScratch = new LocalSlotToken[MaxActiveStatBuffs];
    private readonly LocalSlotToken[] _zoneSlotScratch = new LocalSlotToken[MaxActiveZones];
    private readonly NetworkId[] _statusChangedTargetScratch = new NetworkId[MaxActiveStatusEffects];
    private readonly NetworkId[] _statBuffChangedTargetScratch = new NetworkId[MaxActiveStatBuffs];

    private bool _localSchedulerStateReady;
    private NetworkRunner _localSchedulerRunner;
    private int _lastAuthoritySchedulerTick = int.MinValue;

    private int _currentPendingFireActive;
    private int _currentPendingHitActive;
    private int _currentActiveStatusEffects;
    private int _currentActiveStatBuffs;
    private int _currentActiveZones;

    private int _nextPendingFireTick = NoScheduledWorkTick;
    private int _nextPendingHitTick = NoScheduledWorkTick;
    private int _nextStatusWorkTick = NoScheduledWorkTick;
    private int _nextStatBuffWorkTick = NoScheduledWorkTick;
    private int _nextZoneWorkTick = NoScheduledWorkTick;
    private bool _pendingFireNextTickDirty;
    private bool _pendingHitNextTickDirty;
    private bool _statusNextTickDirty;
    private bool _statBuffNextTickDirty;
    private bool _zoneNextTickDirty;

    private struct LocalSlotToken
    {
        public int Slot;
        public int Sequence;
    }

    private bool CanUseAuthorityLocalState =>
        _localSchedulerStateReady &&
        _localSchedulerRunner == Runner &&
        Object != null &&
        Object.IsValid &&
        Object.HasStateAuthority;

    private void RebuildLocalSchedulerStateFromNetworkEntries()
    {
        if (Runner == null || Object == null || !Object.IsValid)
        {
            _localSchedulerStateReady = false;
            return;
        }

        _localSchedulerStateReady = false;
        _localSchedulerRunner = Runner;
        _lastAuthoritySchedulerTick = int.MinValue;

        InitializeHitBuckets();
        RebuildPendingBucketsFromNetworkSnapshots();
        RebuildEffectSlotIndexesFromNetworkEntries();

        _localSchedulerStateReady = true;
        RefreshNetworkBudgetPeaks();
    }

    private bool EnsureLocalSchedulerState()
    {
        if (_localSchedulerStateReady && _localSchedulerRunner == Runner)
        {
            return true;
        }

        RebuildLocalSchedulerStateFromNetworkEntries();
        return _localSchedulerStateReady;
    }

    private bool PrepareLocalSchedulerStateForAuthorityTick()
    {
        if (!EnsureLocalSchedulerState())
        {
            return false;
        }

        int now = Runner.Tick;
        if (_lastAuthoritySchedulerTick != int.MinValue && now <= _lastAuthoritySchedulerTick)
        {
            RebuildLocalSchedulerStateFromNetworkEntries();
            if (!_localSchedulerStateReady)
            {
                return false;
            }
        }

        _lastAuthoritySchedulerTick = now;
        return true;
    }

    private void RebuildEffectSlotIndexesFromNetworkEntries()
    {
        _statusSlotIndex.BeginRebuild();
        _statBuffSlotIndex.BeginRebuild();
        _zoneSlotIndex.BeginRebuild();
        _statusSlotsByTarget.Clear();
        _statBuffSlotsByTarget.Clear();
        _zoneSlotBySequence.Clear();
        _nextStatusWorkTick = NoScheduledWorkTick;
        _nextStatBuffWorkTick = NoScheduledWorkTick;
        _nextZoneWorkTick = NoScheduledWorkTick;
        _statusNextTickDirty = false;
        _statBuffNextTickDirty = false;
        _zoneNextTickDirty = false;

        for (int slot = 0; slot < MaxActiveStatusEffects; slot++)
        {
            StatusEffectEntry entry = StatusEffects[slot];
            if (entry.Sequence > 0)
            {
                _statusSlotIndex.AddActiveFromOrderedRebuild(slot);
                AddTargetSlot(_statusSlotsByTarget, entry.TargetId, slot);
                _nextStatusWorkTick = Math.Min(_nextStatusWorkTick, GetStatusWorkTick(entry));
            }
            else
            {
                _statusSlotIndex.AddFreeFromOrderedRebuild(slot);
            }
        }

        for (int slot = 0; slot < MaxActiveStatBuffs; slot++)
        {
            StatBuffEntry entry = StatBuffs[slot];
            if (entry.Sequence > 0)
            {
                _statBuffSlotIndex.AddActiveFromOrderedRebuild(slot);
                AddTargetSlot(_statBuffSlotsByTarget, entry.TargetId, slot);
                _nextStatBuffWorkTick = Math.Min(_nextStatBuffWorkTick, entry.ExpireTick);
            }
            else
            {
                _statBuffSlotIndex.AddFreeFromOrderedRebuild(slot);
            }
        }

        for (int slot = 0; slot < MaxActiveZones; slot++)
        {
            ZoneEntry entry = Zones[slot];
            if (entry.Sequence > 0)
            {
                _zoneSlotIndex.AddActiveFromOrderedRebuild(slot);
                _zoneSlotBySequence[entry.Sequence] = slot;
                _nextZoneWorkTick = Math.Min(_nextZoneWorkTick, GetZoneWorkTick(entry));
            }
            else
            {
                _zoneSlotIndex.AddFreeFromOrderedRebuild(slot);
            }
        }

        SyncCurrentCountsFromLocalIndexes();
    }

    private void SyncCurrentCountsFromLocalIndexes()
    {
        _currentPendingFireActive = _pendingFireSlotIndex.ActiveCount;
        _currentPendingHitActive = _pendingHitSlotIndex.ActiveCount;
        _currentActiveStatusEffects = _statusSlotIndex.ActiveCount;
        _currentActiveStatBuffs = _statBuffSlotIndex.ActiveCount;
        _currentActiveZones = _zoneSlotIndex.ActiveCount;
    }

    private static int GetStatusWorkTick(StatusEffectEntry entry)
    {
        int next = entry.ExpireTick > 0 ? entry.ExpireTick : NoScheduledWorkTick;
        if (entry.NextTick > 0)
        {
            next = Math.Min(next, entry.NextTick);
        }

        return next;
    }

    private static int GetZoneWorkTick(ZoneEntry entry)
    {
        int next = entry.ExpireTick > 0 ? entry.ExpireTick : NoScheduledWorkTick;
        if (entry.NextTick > 0)
        {
            next = Math.Min(next, entry.NextTick);
        }

        return next;
    }

    private static void AddTargetSlot(
        Dictionary<uint, CombatSchedulerSlotMask> slotsByTarget,
        NetworkId targetId,
        int slot)
    {
        if (targetId.Raw == 0)
        {
            return;
        }

        slotsByTarget.TryGetValue(targetId.Raw, out CombatSchedulerSlotMask mask);
        mask.Add(slot);
        slotsByTarget[targetId.Raw] = mask;
    }

    private static void RemoveTargetSlot(
        Dictionary<uint, CombatSchedulerSlotMask> slotsByTarget,
        NetworkId targetId,
        int slot)
    {
        if (targetId.Raw == 0 || !slotsByTarget.TryGetValue(targetId.Raw, out CombatSchedulerSlotMask mask))
        {
            return;
        }

        mask.Remove(slot);
        if (mask.IsEmpty)
        {
            slotsByTarget.Remove(targetId.Raw);
        }
        else
        {
            slotsByTarget[targetId.Raw] = mask;
        }
    }

    private static bool TryGetTargetMask(
        Dictionary<uint, CombatSchedulerSlotMask> slotsByTarget,
        NetworkId targetId,
        out CombatSchedulerSlotMask mask)
    {
        mask = default;
        return targetId.Raw != 0 && slotsByTarget.TryGetValue(targetId.Raw, out mask);
    }

    private int CaptureStatusSlotTokens()
    {
        int count = Math.Min(_statusSlotIndex.ActiveCount, _statusSlotScratch.Length);
        for (int i = 0; i < count; i++)
        {
            int slot = _statusSlotIndex.GetActiveSlot(i);
            _statusSlotScratch[i] = new LocalSlotToken
            {
                Slot = slot,
                Sequence = StatusEffects[slot].Sequence
            };
        }

        return count;
    }

    private int CaptureStatBuffSlotTokens()
    {
        int count = Math.Min(_statBuffSlotIndex.ActiveCount, _statBuffSlotScratch.Length);
        for (int i = 0; i < count; i++)
        {
            int slot = _statBuffSlotIndex.GetActiveSlot(i);
            _statBuffSlotScratch[i] = new LocalSlotToken
            {
                Slot = slot,
                Sequence = StatBuffs[slot].Sequence
            };
        }

        return count;
    }

    private int CaptureZoneSlotTokens()
    {
        int count = Math.Min(_zoneSlotIndex.ActiveCount, _zoneSlotScratch.Length);
        for (int i = 0; i < count; i++)
        {
            int slot = _zoneSlotIndex.GetActiveSlot(i);
            _zoneSlotScratch[i] = new LocalSlotToken
            {
                Slot = slot,
                Sequence = Zones[slot].Sequence
            };
        }

        return count;
    }

    private static int AddChangedTarget(NetworkId[] scratch, int count, NetworkId targetId)
    {
        if (targetId.Raw == 0)
        {
            return count;
        }

        for (int i = 0; i < count; i++)
        {
            if (scratch[i].Raw == targetId.Raw)
            {
                return count;
            }
        }

        if (count < scratch.Length)
        {
            scratch[count++] = targetId;
        }

        return count;
    }

    private void CommitStatusSlot(int slot, StatusEffectEntry entry)
    {
        _statusSlotIndex.CommitRentedSlot(slot);
        AddTargetSlot(_statusSlotsByTarget, entry.TargetId, slot);
        _currentActiveStatusEffects = _statusSlotIndex.ActiveCount;
        _nextStatusWorkTick = Math.Min(_nextStatusWorkTick, GetStatusWorkTick(entry));
        _maxActiveStatusEffects = Math.Max(_maxActiveStatusEffects, _currentActiveStatusEffects);
    }

    private void NoteStatusSlotUpdated(StatusEffectEntry previous, StatusEffectEntry updated)
    {
        int previousWork = GetStatusWorkTick(previous);
        int updatedWork = GetStatusWorkTick(updated);
        if (previousWork <= _nextStatusWorkTick && updatedWork > previousWork)
        {
            _statusNextTickDirty = true;
        }

        _nextStatusWorkTick = Math.Min(_nextStatusWorkTick, updatedWork);
    }

    private bool ReleaseStatusSlot(int slot, StatusEffectEntry entry)
    {
        if (!_statusSlotIndex.ReleaseActiveSlot(slot))
        {
            return false;
        }

        RemoveTargetSlot(_statusSlotsByTarget, entry.TargetId, slot);
        _currentActiveStatusEffects = _statusSlotIndex.ActiveCount;
        if (GetStatusWorkTick(entry) <= _nextStatusWorkTick)
        {
            _statusNextTickDirty = true;
        }

        return true;
    }

    private void CommitStatBuffSlot(int slot, StatBuffEntry entry)
    {
        _statBuffSlotIndex.CommitRentedSlot(slot);
        AddTargetSlot(_statBuffSlotsByTarget, entry.TargetId, slot);
        _currentActiveStatBuffs = _statBuffSlotIndex.ActiveCount;
        _nextStatBuffWorkTick = Math.Min(_nextStatBuffWorkTick, entry.ExpireTick);
        _maxActiveStatBuffs = Math.Max(_maxActiveStatBuffs, _currentActiveStatBuffs);
    }

    private void NoteStatBuffSlotUpdated(StatBuffEntry previous, StatBuffEntry updated)
    {
        if (previous.ExpireTick <= _nextStatBuffWorkTick && updated.ExpireTick > previous.ExpireTick)
        {
            _statBuffNextTickDirty = true;
        }

        _nextStatBuffWorkTick = Math.Min(_nextStatBuffWorkTick, updated.ExpireTick);
    }

    private bool ReleaseStatBuffSlot(int slot, StatBuffEntry entry)
    {
        if (!_statBuffSlotIndex.ReleaseActiveSlot(slot))
        {
            return false;
        }

        RemoveTargetSlot(_statBuffSlotsByTarget, entry.TargetId, slot);
        _currentActiveStatBuffs = _statBuffSlotIndex.ActiveCount;
        if (entry.ExpireTick <= _nextStatBuffWorkTick)
        {
            _statBuffNextTickDirty = true;
        }

        return true;
    }

    private void CommitZoneSlot(int slot, ZoneEntry entry)
    {
        _zoneSlotIndex.CommitRentedSlot(slot);
        _zoneSlotBySequence[entry.Sequence] = slot;
        _currentActiveZones = _zoneSlotIndex.ActiveCount;
        _nextZoneWorkTick = Math.Min(_nextZoneWorkTick, GetZoneWorkTick(entry));
        _maxActiveZones = Math.Max(_maxActiveZones, _currentActiveZones);
    }

    private void NoteZoneSlotUpdated(ZoneEntry previous, ZoneEntry updated)
    {
        int previousWork = GetZoneWorkTick(previous);
        int updatedWork = GetZoneWorkTick(updated);
        if (previousWork <= _nextZoneWorkTick && updatedWork > previousWork)
        {
            _zoneNextTickDirty = true;
        }

        _nextZoneWorkTick = Math.Min(_nextZoneWorkTick, updatedWork);
    }

    private bool ReleaseZoneSlot(int slot, ZoneEntry entry)
    {
        if (!_zoneSlotIndex.ReleaseActiveSlot(slot))
        {
            return false;
        }

        _zoneSlotBySequence.Remove(entry.Sequence);
        _currentActiveZones = _zoneSlotIndex.ActiveCount;
        if (GetZoneWorkTick(entry) <= _nextZoneWorkTick)
        {
            _zoneNextTickDirty = true;
        }

        return true;
    }

    private void RecalculateNextStatusWorkTick()
    {
        int next = NoScheduledWorkTick;
        for (int i = 0; i < _statusSlotIndex.ActiveCount; i++)
        {
            int slot = _statusSlotIndex.GetActiveSlot(i);
            StatusEffectEntry entry = StatusEffects[slot];
            if (entry.Sequence > 0)
            {
                next = Math.Min(next, GetStatusWorkTick(entry));
            }
        }

        _nextStatusWorkTick = next;
        _statusNextTickDirty = false;
    }

    private void RecalculateNextStatBuffWorkTick()
    {
        int next = NoScheduledWorkTick;
        for (int i = 0; i < _statBuffSlotIndex.ActiveCount; i++)
        {
            int slot = _statBuffSlotIndex.GetActiveSlot(i);
            StatBuffEntry entry = StatBuffs[slot];
            if (entry.Sequence > 0)
            {
                next = Math.Min(next, entry.ExpireTick);
            }
        }

        _nextStatBuffWorkTick = next;
        _statBuffNextTickDirty = false;
    }

    private void RecalculateNextZoneWorkTick()
    {
        int next = NoScheduledWorkTick;
        for (int i = 0; i < _zoneSlotIndex.ActiveCount; i++)
        {
            int slot = _zoneSlotIndex.GetActiveSlot(i);
            ZoneEntry entry = Zones[slot];
            if (entry.Sequence > 0)
            {
                next = Math.Min(next, GetZoneWorkTick(entry));
            }
        }

        _nextZoneWorkTick = next;
        _zoneNextTickDirty = false;
    }

    private int GetCurrentStatusEffectCount()
    {
        if (CanUseAuthorityLocalState)
        {
            return _currentActiveStatusEffects;
        }

        int count = 0;
        if (!IsStatusEffectSchedulerActive)
        {
            return count;
        }

        for (int i = 0; i < MaxActiveStatusEffects; i++)
        {
            if (StatusEffects[i].Sequence > 0)
            {
                count++;
            }
        }

        return count;
    }

    private int GetCurrentStatBuffCount()
    {
        if (CanUseAuthorityLocalState)
        {
            return _currentActiveStatBuffs;
        }

        int count = 0;
        if (!IsStatBuffSchedulerActive)
        {
            return count;
        }

        for (int i = 0; i < MaxActiveStatBuffs; i++)
        {
            if (StatBuffs[i].Sequence > 0)
            {
                count++;
            }
        }

        return count;
    }

    private int GetCurrentZoneCount()
    {
        if (CanUseAuthorityLocalState)
        {
            return _currentActiveZones;
        }

        int count = 0;
        if (!IsZoneSchedulerActive)
        {
            return count;
        }

        for (int i = 0; i < MaxActiveZones; i++)
        {
            if (Zones[i].Sequence > 0)
            {
                count++;
            }
        }

        return count;
    }

    public void NotifyTargetInvalidated(NetworkId targetId, string reason = null)
    {
        if (targetId.Raw == 0 || !EnsureLocalSchedulerState() || Object == null || !Object.HasStateAuthority)
        {
            return;
        }

        ClearStatusEffectsForTarget(targetId, reason);
        ClearStatBuffsForTarget(targetId, reason);
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    internal bool MPTestValidateLocalSchedulerState(out string reason)
    {
        reason = null;
        if (!EnsureLocalSchedulerState())
        {
            reason = "local scheduler state is not ready";
            return false;
        }

        int pendingFires = 0;
        int pendingHits = 0;
        int statuses = 0;
        int statBuffs = 0;
        int zones = 0;
        for (int i = 0; i < PendingFireCapacity; i++) pendingFires += PendingFireSnapshots[i].Sequence > 0 ? 1 : 0;
        for (int i = 0; i < PendingHitCapacity; i++) pendingHits += PendingHitSnapshots[i].Sequence > 0 ? 1 : 0;
        for (int i = 0; i < MaxActiveStatusEffects; i++) statuses += StatusEffects[i].Sequence > 0 ? 1 : 0;
        for (int i = 0; i < MaxActiveStatBuffs; i++) statBuffs += StatBuffs[i].Sequence > 0 ? 1 : 0;
        for (int i = 0; i < MaxActiveZones; i++) zones += Zones[i].Sequence > 0 ? 1 : 0;

        if (pendingFires != _currentPendingFireActive ||
            pendingHits != _currentPendingHitActive ||
            statuses != _currentActiveStatusEffects ||
            statBuffs != _currentActiveStatBuffs ||
            zones != _currentActiveZones)
        {
            reason = $"count mismatch network=({pendingFires},{pendingHits},{statuses},{statBuffs},{zones}) local=({_currentPendingFireActive},{_currentPendingHitActive},{_currentActiveStatusEffects},{_currentActiveStatBuffs},{_currentActiveZones})";
            return false;
        }

        return true;
    }
#endif
}
