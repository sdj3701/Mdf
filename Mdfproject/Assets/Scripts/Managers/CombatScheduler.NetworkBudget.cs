using Fusion;
using UnityEngine;

public partial class CombatScheduler
{
    private int _statusCapacityDrops;
    private int _statBuffCapacityDrops;
    private int _zoneCapacityDrops;
    private int _pendingFireCapacityDrops;
    private int _pendingHitCapacityDrops;
    private int _presentationEventDrops;
    private int _statusCapacityCoalesces;
    private int _statBuffCapacityCoalesces;
    private int _zoneCapacityCoalesces;
    private int _pendingFireCapacityFallbacks;
    private int _pendingHitCapacityFallbacks;
    private int _statusCapacityBackpressures;
    private int _statBuffCapacityBackpressures;
    private int _zoneCapacityBackpressures;
    private int _zoneDueDebtPhaseCancellations;
    private int _zoneDebtTerminalTargetInvalidations;
    private int _zoneDebtTerminalFailures;

    private int _maxPendingFireActive;
    private int _maxPendingHitActive;
    private int _maxActiveStatusEffects;
    private int _maxActiveStatBuffs;
    private int _maxActiveZones;
    private int _maxProjectileVfxEventsPerTick;
    private int _maxBasicAttackVfxEventsPerTick;
    private int _budgetTick = int.MinValue;
    private int _projectileVfxEventsThisTick;
    private int _basicAttackVfxEventsThisTick;
    private bool _effectCapacityPreflightActive;
    private int _preflightStatusReservations;
    private int _preflightStatBuffReservations;
    private int _preflightZoneReservations;

    public bool TryBeginEffectCapacityPreflight()
    {
        if (_effectCapacityPreflightActive)
        {
            return false;
        }

        _effectCapacityPreflightActive = true;
        _preflightStatusReservations = 0;
        _preflightStatBuffReservations = 0;
        _preflightZoneReservations = 0;
        return true;
    }

    public void EndEffectCapacityPreflight()
    {
        _effectCapacityPreflightActive = false;
        _preflightStatusReservations = 0;
        _preflightStatBuffReservations = 0;
        _preflightZoneReservations = 0;
    }

    private int GetPreflightStatusReservations() =>
        _effectCapacityPreflightActive ? _preflightStatusReservations : 0;

    private int GetPreflightStatBuffReservations() =>
        _effectCapacityPreflightActive ? _preflightStatBuffReservations : 0;

    private int GetPreflightZoneReservations() =>
        _effectCapacityPreflightActive ? _preflightZoneReservations : 0;

    private void ReservePreflightStatusSlots(int count)
    {
        if (_effectCapacityPreflightActive)
        {
            _preflightStatusReservations += Mathf.Max(0, count);
        }
    }

    private void ReservePreflightStatBuffSlots(int count)
    {
        if (_effectCapacityPreflightActive)
        {
            _preflightStatBuffReservations += Mathf.Max(0, count);
        }
    }

    private void ReservePreflightZoneSlots(int count)
    {
        if (_effectCapacityPreflightActive)
        {
            _preflightZoneReservations += Mathf.Max(0, count);
        }
    }

    public struct NetworkBudgetReport
    {
        public int StatusCapacityDrops;
        public int StatBuffCapacityDrops;
        public int ZoneCapacityDrops;
        public int PendingFireCapacityDrops;
        public int PendingHitCapacityDrops;
        public int PresentationEventDrops;
        public int StatusCapacityCoalesces;
        public int StatBuffCapacityCoalesces;
        public int ZoneCapacityCoalesces;
        public int PendingFireCapacityFallbacks;
        public int PendingHitCapacityFallbacks;
        public int StatusCapacityBackpressures;
        public int StatBuffCapacityBackpressures;
        public int ZoneCapacityBackpressures;
        public int ZoneDueDebtPhaseCancellations;
        public int ZoneDebtTerminalTargetInvalidations;
        public int ZoneDebtTerminalFailures;
        public int CurrentPendingFireActive;
        public int CurrentPendingHitActive;
        public int CurrentActiveStatusEffects;
        public int CurrentActiveStatBuffs;
        public int CurrentActiveZones;
        public int MaxPendingFireActive;
        public int MaxPendingHitActive;
        public int MaxActiveStatusEffects;
        public int MaxActiveStatBuffs;
        public int MaxActiveZones;
        public int MaxProjectileVfxEventsPerTick;
        public int MaxBasicAttackVfxEventsPerTick;
        public int PendingFireCapacity;
        public int PendingHitCapacity;
        public int EventBufferCapacity;
        public int StatusCapacity;
        public int StatBuffCapacity;
        public int ZoneCapacity;
    }

    public NetworkBudgetReport GetNetworkBudgetReport()
    {
        RefreshNetworkBudgetPeaks();
        return new NetworkBudgetReport
        {
            StatusCapacityDrops = _statusCapacityDrops,
            StatBuffCapacityDrops = _statBuffCapacityDrops,
            ZoneCapacityDrops = _zoneCapacityDrops,
            PendingFireCapacityDrops = _pendingFireCapacityDrops,
            PendingHitCapacityDrops = _pendingHitCapacityDrops,
            PresentationEventDrops = _presentationEventDrops,
            StatusCapacityCoalesces = _statusCapacityCoalesces,
            StatBuffCapacityCoalesces = _statBuffCapacityCoalesces,
            ZoneCapacityCoalesces = _zoneCapacityCoalesces,
            PendingFireCapacityFallbacks = _pendingFireCapacityFallbacks,
            PendingHitCapacityFallbacks = _pendingHitCapacityFallbacks,
            StatusCapacityBackpressures = _statusCapacityBackpressures,
            StatBuffCapacityBackpressures = _statBuffCapacityBackpressures,
            ZoneCapacityBackpressures = _zoneCapacityBackpressures,
            ZoneDueDebtPhaseCancellations = _zoneDueDebtPhaseCancellations,
            ZoneDebtTerminalTargetInvalidations = _zoneDebtTerminalTargetInvalidations,
            ZoneDebtTerminalFailures = _zoneDebtTerminalFailures,
            CurrentPendingFireActive = CountPendingFireSnapshots(),
            CurrentPendingHitActive = CountPendingHitSnapshots(),
            CurrentActiveStatusEffects = ActiveStatusEffectCount,
            CurrentActiveStatBuffs = ActiveStatBuffCount,
            CurrentActiveZones = ActiveZoneCount,
            MaxPendingFireActive = _maxPendingFireActive,
            MaxPendingHitActive = _maxPendingHitActive,
            MaxActiveStatusEffects = _maxActiveStatusEffects,
            MaxActiveStatBuffs = _maxActiveStatBuffs,
            MaxActiveZones = _maxActiveZones,
            MaxProjectileVfxEventsPerTick = _maxProjectileVfxEventsPerTick,
            MaxBasicAttackVfxEventsPerTick = _maxBasicAttackVfxEventsPerTick,
            PendingFireCapacity = PendingFireCapacity,
            PendingHitCapacity = PendingHitCapacity,
            EventBufferCapacity = ProjectileEventBufferCapacity,
            StatusCapacity = MaxActiveStatusEffects,
            StatBuffCapacity = MaxActiveStatBuffs,
            ZoneCapacity = MaxActiveZones
        };
    }

    private void RefreshNetworkBudgetPeaks()
    {
        int pendingFireCount = CountPendingFireSnapshots();
        int pendingHitCount = CountPendingHitSnapshots();
        int statusCount = GetCurrentStatusEffectCount();
        int statBuffCount = GetCurrentStatBuffCount();
        int zoneCount = GetCurrentZoneCount();
        _maxPendingFireActive = Mathf.Max(_maxPendingFireActive, pendingFireCount);
        _maxPendingHitActive = Mathf.Max(_maxPendingHitActive, pendingHitCount);
        _maxActiveStatusEffects = Mathf.Max(_maxActiveStatusEffects, statusCount);
        _maxActiveStatBuffs = Mathf.Max(_maxActiveStatBuffs, statBuffCount);
        _maxActiveZones = Mathf.Max(_maxActiveZones, zoneCount);
    }

    private void RecordNetworkBudgetDrop(NetworkBudgetDropKind kind)
    {
        switch (kind)
        {
            case NetworkBudgetDropKind.Status:
                _statusCapacityDrops++;
                break;
            case NetworkBudgetDropKind.StatBuff:
                _statBuffCapacityDrops++;
                break;
            case NetworkBudgetDropKind.Zone:
                _zoneCapacityDrops++;
                break;
            case NetworkBudgetDropKind.PendingFire:
                _pendingFireCapacityDrops++;
                break;
            case NetworkBudgetDropKind.PendingHit:
                _pendingHitCapacityDrops++;
                break;
            case NetworkBudgetDropKind.PresentationEvent:
                _presentationEventDrops++;
                break;
        }
    }

    private void RecordCapacityRecovery(CapacityRecoveryKind kind)
    {
        switch (kind)
        {
            case CapacityRecoveryKind.StatusCoalesce:
                _statusCapacityCoalesces++;
                break;
            case CapacityRecoveryKind.StatBuffCoalesce:
                _statBuffCapacityCoalesces++;
                break;
            case CapacityRecoveryKind.ZoneCoalesce:
                _zoneCapacityCoalesces++;
                break;
            case CapacityRecoveryKind.PendingFireBackpressure:
                _pendingFireCapacityFallbacks++;
                break;
            case CapacityRecoveryKind.PendingHitBackpressure:
                _pendingHitCapacityFallbacks++;
                break;
            case CapacityRecoveryKind.StatusBackpressure:
                _statusCapacityBackpressures++;
                break;
            case CapacityRecoveryKind.StatBuffBackpressure:
                _statBuffCapacityBackpressures++;
                break;
            case CapacityRecoveryKind.ZoneBackpressure:
                _zoneCapacityBackpressures++;
                break;
        }
    }

    private void TrackProjectileVfxEventWrite()
    {
        ResetNetworkBudgetTickCountersIfNeeded();
        _projectileVfxEventsThisTick++;
        _maxProjectileVfxEventsPerTick = Mathf.Max(_maxProjectileVfxEventsPerTick, _projectileVfxEventsThisTick);
    }

    private void TrackBasicAttackVfxEventWrite()
    {
        ResetNetworkBudgetTickCountersIfNeeded();
        _basicAttackVfxEventsThisTick++;
        _maxBasicAttackVfxEventsPerTick = Mathf.Max(_maxBasicAttackVfxEventsPerTick, _basicAttackVfxEventsThisTick);
    }

    private void ResetNetworkBudgetTickCountersIfNeeded()
    {
        int tick = Runner != null ? Runner.Tick.Raw : 0;
        if (_budgetTick == tick)
        {
            return;
        }

        _budgetTick = tick;
        _projectileVfxEventsThisTick = 0;
        _basicAttackVfxEventsThisTick = 0;
    }

    private int CountPendingFireSnapshots()
    {
        if (CanUseAuthorityLocalState)
        {
            return _currentPendingFireActive;
        }

        if (!IsSchedulerNetworkReady())
        {
            return 0;
        }

        int count = 0;
        for (int i = 0; i < PendingFireCapacity; i++)
        {
            if (PendingFireSnapshots[i].Sequence > 0)
            {
                count++;
            }
        }

        return count;
    }

    private int CountPendingHitSnapshots()
    {
        if (CanUseAuthorityLocalState)
        {
            return _currentPendingHitActive;
        }

        if (!IsSchedulerNetworkReady())
        {
            return 0;
        }

        int count = 0;
        for (int i = 0; i < PendingHitCapacity; i++)
        {
            if (PendingHitSnapshots[i].Sequence > 0)
            {
                count++;
            }
        }

        return count;
    }

    private bool IsSchedulerNetworkReady()
    {
        return Runner != null &&
               Runner.IsRunning &&
               Object != null &&
               Object.IsValid;
    }

    private enum NetworkBudgetDropKind
    {
        Status,
        StatBuff,
        Zone,
        PendingFire,
        PendingHit,
        PresentationEvent
    }

    private void RecordZoneDueDebtPhaseCancellation(int cancelledTickCount)
    {
        _zoneDueDebtPhaseCancellations += Mathf.Max(0, cancelledTickCount);
    }

    private void RecordZoneDebtTerminalTargetInvalidations(int invalidatedTargetCount)
    {
        _zoneDebtTerminalTargetInvalidations += Mathf.Max(0, invalidatedTargetCount);
    }

    private void RecordZoneDebtTerminalFailure()
    {
        _zoneDebtTerminalFailures++;
    }

    private enum CapacityRecoveryKind
    {
        StatusCoalesce,
        StatBuffCoalesce,
        ZoneCoalesce,
        PendingFireBackpressure,
        PendingHitBackpressure,
        StatusBackpressure,
        StatBuffBackpressure,
        ZoneBackpressure
    }
}
