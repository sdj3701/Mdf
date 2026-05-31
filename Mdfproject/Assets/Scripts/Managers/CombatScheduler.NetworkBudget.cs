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

    public struct NetworkBudgetReport
    {
        public int StatusCapacityDrops;
        public int StatBuffCapacityDrops;
        public int ZoneCapacityDrops;
        public int PendingFireCapacityDrops;
        public int PendingHitCapacityDrops;
        public int PresentationEventDrops;
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
        _maxPendingFireActive = Mathf.Max(_maxPendingFireActive, CountPendingFireSnapshots());
        _maxPendingHitActive = Mathf.Max(_maxPendingHitActive, CountPendingHitSnapshots());
        _maxActiveStatusEffects = Mathf.Max(_maxActiveStatusEffects, ActiveStatusEffectCount);
        _maxActiveStatBuffs = Mathf.Max(_maxActiveStatBuffs, ActiveStatBuffCount);
        _maxActiveZones = Mathf.Max(_maxActiveZones, ActiveZoneCount);
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
}
