using System;
using System.Collections.Generic;
using Fusion;
using UnityEngine;

public partial class CombatScheduler
{
    private const int MaxActiveStatusEffects = CombatSchedulerCapacityConfig.StatusEffectCapacity;
    private const int StatusSourceNetworkObject = 1;
    private const int StatusSourceMagicScroll = 2;
    private const int StatusSourceTransient = 3;

    [Networked] public int StatusEffectSequence { get; private set; }
    [Networked, Capacity(MaxActiveStatusEffects)] private NetworkArray<StatusEffectEntry> StatusEffects { get; }

    private struct StatusEffectEntry : INetworkStruct
    {
        public int Sequence;
        public NetworkId TargetId;
        public NetworkId CasterId;
        public int SourceKey;
        public int AppliedTick;
        public int ExpireTick;
        public int NextTick;
        public int TickIntervalTicks;
        public int DamagePerTick;
        public int SlowMultiplier;
        public int PackedMeta;

        public int SourceKind => PackedMeta & 0xF;
        public int Type => (PackedMeta >> 4) & 0xFF;
        public int DamageType => (PackedMeta >> 12) & 0xFF;
        public int Flags => PackedMeta >> 20;
    }

    public struct StatusEffectMigrationSnapshot
    {
        public int Sequence;
        public NetworkId TargetId;
        public NetworkId CasterId;
        public int SourceKind;
        public int SourceKey;
        public int Type;
        public int AppliedTick;
        public int ExpireTick;
        public int NextTick;
        public int TickIntervalTicks;
        public int DamagePerTick;
        public int DamageType;
        public int SlowMultiplier;
        public int Flags;
    }

    public delegate bool MigrationNetworkIdResolver(
        NetworkId capturedNetworkId,
        out NetworkId actualNetworkId);

    public bool IsStatusEffectSchedulerActive =>
        Runner != null &&
        Runner.IsRunning &&
        Object != null &&
        Object.IsValid;

    public int ActiveStatusEffectCount
    {
        get => GetCurrentStatusEffectCount();
    }

    public bool CanApplyStatusEffectBatch(
        IReadOnlyList<BuffManager> targets,
        StatusEffectType type,
        float duration,
        GameObject caster,
        float tickInterval = 0f,
        float damagePerTick = 0f,
        float slowMultiplier = 1f,
        DamageType damageType = DamageType.Physical)
    {
        if (!IsStatusEffectSchedulerActive || !Object.HasStateAuthority || targets == null || duration <= 0f ||
            !EnsureLocalSchedulerState())
        {
            return false;
        }

        int now = Runner.Tick;
        int durationTicks = SecondsToTicksCeil(duration);
        int expireTick = now + Mathf.Max(1, durationTicks);
        int packedDamage = PackFloat(damagePerTick);
        int packedSlow = PackFloat(Mathf.Max(0f, slowMultiplier));
        ResolveStatusSource(
            caster,
            StatusEffectSequence + 1,
            out NetworkId casterId,
            out int sourceKind,
            out int sourceKey);

        int availableSlots = MaxActiveStatusEffects - _statusSlotIndex.ActiveCount - GetPreflightStatusReservations();
        int requiredSlots = 0;
        var visitedTargets = new HashSet<uint>();
        for (int i = 0; i < targets.Count; i++)
        {
            if (!TryResolveNetworkObject(targets[i], out NetworkObject targetObject) ||
                !visitedTargets.Add(targetObject.Id.Raw))
            {
                continue;
            }

            if (sourceKind == StatusSourceNetworkObject &&
                FindMatchingStatusSlot(targetObject.Id, casterId, sourceKind, sourceKey, type) >= 0)
            {
                continue;
            }

            if (requiredSlots < availableSlots)
            {
                requiredSlots++;
                continue;
            }

            if (TryCoalesceBooleanStatusAtCapacity(
                    targetObject.Id,
                    type,
                    expireTick,
                    packedDamage,
                    packedSlow,
                    out _))
            {
                continue;
            }

            RecordCapacityRecovery(CapacityRecoveryKind.StatusBackpressure);
            return false;
        }

        ReservePreflightStatusSlots(requiredSlots);
        return true;
    }

    public int GetActiveStatusEffectCountFor(BuffManager target)
    {
        if (!IsStatusEffectSchedulerActive || !TryResolveNetworkObject(target, out var targetObject))
        {
            return 0;
        }

        uint targetRaw = targetObject.Id.Raw;
        if (CanUseAuthorityLocalState &&
            _statusSlotsByTarget.TryGetValue(targetRaw, out CombatSchedulerSlotMask indexedMask))
        {
            return indexedMask.Count(MaxActiveStatusEffects);
        }

        int count = 0;
        for (int i = 0; i < MaxActiveStatusEffects; i++)
        {
            StatusEffectEntry entry = StatusEffects[i];
            if (entry.Sequence > 0 && entry.TargetId.Raw == targetRaw)
            {
                count++;
            }
        }

        return count;
    }

    public bool ApplyStatusEffect(
        BuffManager target,
        StatusEffectType type,
        float duration,
        GameObject caster,
        float tickInterval = 0f,
        float damagePerTick = 0f,
        float slowMultiplier = 1f,
        DamageType damageType = DamageType.Physical)
    {
        if (!IsStatusEffectSchedulerActive || !Object.HasStateAuthority || target == null || duration <= 0f ||
            !EnsureLocalSchedulerState())
        {
            return false;
        }

        if (!TryResolveNetworkObject(target, out var targetObject))
        {
            return false;
        }

        int now = Runner.Tick;
        int durationTicks = SecondsToTicksCeil(duration);
        int tickIntervalTicks = SecondsToTicksCeil(tickInterval);
        int sequenceHint = StatusEffectSequence + 1;
        ResolveStatusSource(caster, sequenceHint, out NetworkId casterId, out int sourceKind, out int sourceKey);

        int existingSlot = sourceKind == StatusSourceNetworkObject
            ? FindMatchingStatusSlot(targetObject.Id, casterId, sourceKind, sourceKey, type)
            : -1;

        int expireTick = now + Mathf.Max(1, durationTicks);
        if (existingSlot >= 0)
        {
            StatusEffectEntry existing = StatusEffects[existingSlot];
            StatusEffectEntry previous = existing;
            existing.ExpireTick = Mathf.Max(existing.ExpireTick, expireTick);
            StatusEffects.Set(existingSlot, existing);
            NoteStatusSlotUpdated(previous, existing);
            RefreshStatusCacheForTarget(targetObject.Id);
            return true;
        }

        int emptySlot = FindEmptyStatusSlot();
        if (emptySlot < 0)
        {
            int packedDamagePerTick = PackFloat(damagePerTick);
            int packedSlowMultiplier = PackFloat(Mathf.Max(0f, slowMultiplier));
            if (TryCoalesceBooleanStatusAtCapacity(
                    targetObject.Id,
                    type,
                    expireTick,
                    packedDamagePerTick,
                    packedSlowMultiplier,
                    out int coalescedSlot))
            {
                StatusEffectEntry existing = StatusEffects[coalescedSlot];
                StatusEffectEntry previous = existing;
                existing.ExpireTick = Mathf.Max(existing.ExpireTick, expireTick);
                StatusEffects.Set(coalescedSlot, existing);
                NoteStatusSlotUpdated(previous, existing);
                RefreshStatusCacheForTarget(targetObject.Id);
                RecordCapacityRecovery(CapacityRecoveryKind.StatusCoalesce);
                return true;
            }

            RecordCapacityRecovery(CapacityRecoveryKind.StatusBackpressure);
            return false;
        }

        int nextSeq = StatusEffectSequence + 1;
        StatusEffectSequence = nextSeq;
        if (sourceKind != StatusSourceNetworkObject)
        {
            sourceKey = nextSeq;
        }

        var entry = new StatusEffectEntry
        {
            Sequence = nextSeq,
            TargetId = targetObject.Id,
            CasterId = casterId,
            SourceKey = sourceKey,
            AppliedTick = now,
            ExpireTick = expireTick,
            NextTick = tickIntervalTicks > 0 && damagePerTick > 0f ? now + tickIntervalTicks : 0,
            TickIntervalTicks = tickIntervalTicks,
            DamagePerTick = PackFloat(damagePerTick),
            SlowMultiplier = PackFloat(Mathf.Max(0f, slowMultiplier)),
            PackedMeta = PackStatusMeta(sourceKind, (int)type, (int)damageType, 0)
        };
        StatusEffects.Set(emptySlot, entry);
        CommitStatusSlot(emptySlot, entry);

        RefreshStatusCacheForTarget(targetObject.Id);
        RefreshNetworkBudgetPeaks();
        return true;
    }

    public bool ClearStatusEffectsForTarget(BuffManager target, string reason = null)
    {
        if (!IsStatusEffectSchedulerActive || !Object.HasStateAuthority || target == null ||
            !EnsureLocalSchedulerState())
        {
            return false;
        }

        if (!TryResolveNetworkObject(target, out var targetObject))
        {
            return false;
        }

        ClearStatusEffectsForTarget(targetObject.Id, reason);
        return true;
    }

    public void ClearStatusEffectsForTarget(NetworkObject target, string reason = null)
    {
        if (target == null || !target.IsValid)
        {
            return;
        }

        ClearStatusEffectsForTarget(target.Id, reason);
    }

    public bool ClearStatusEffectType(BuffManager target, StatusEffectType type, string reason = null)
    {
        if (!IsStatusEffectSchedulerActive || !Object.HasStateAuthority || target == null ||
            !EnsureLocalSchedulerState())
        {
            return false;
        }

        if (!TryResolveNetworkObject(target, out var targetObject))
        {
            return false;
        }

        uint targetRaw = targetObject.Id.Raw;
        bool changed = false;
        if (!TryGetTargetMask(_statusSlotsByTarget, targetObject.Id, out CombatSchedulerSlotMask mask))
        {
            RefreshStatusCacheForTarget(targetObject.Id);
            return false;
        }

        for (int slot = 0; slot < MaxActiveStatusEffects; slot++)
        {
            if (!mask.Contains(slot))
            {
                continue;
            }

            StatusEffectEntry entry = StatusEffects[slot];
            if (entry.Sequence > 0 && entry.TargetId.Raw == targetRaw && entry.Type == (int)type)
            {
                ClearStatusEffectSlot(slot, entry);
                changed = true;
            }
        }

        RefreshStatusCacheForTarget(targetObject.Id);
        return changed;
    }

    public void ClearAllStatusEffects(string reason = null)
    {
        if (!IsStatusEffectSchedulerActive || !Object.HasStateAuthority || !EnsureLocalSchedulerState())
        {
            return;
        }

        int tokenCount = CaptureStatusSlotTokens();
        for (int i = 0; i < tokenCount; i++)
        {
            LocalSlotToken token = _statusSlotScratch[i];
            StatusEffectEntry entry = StatusEffects[token.Slot];
            if (entry.Sequence == token.Sequence)
            {
                ClearStatusEffectSlot(token.Slot, entry);
            }
        }

        RefreshAllStatusCaches();
    }

    public IEnumerable<string> BuildActiveStatusSnapshotParts(Func<GameObject, string> targetKeyBuilder)
    {
        if (!IsStatusEffectSchedulerActive)
        {
            yield break;
        }

        for (int i = 0; i < MaxActiveStatusEffects; i++)
        {
            StatusEffectEntry entry = StatusEffects[i];
            if (entry.Sequence <= 0)
            {
                continue;
            }

            NetworkObject targetObject = ResolveNetworkObject(entry.TargetId);
            string targetKey = targetObject != null && targetKeyBuilder != null
                ? targetKeyBuilder(targetObject.gameObject)
                : $"targetId={entry.TargetId.Raw}";
            int slowBucket = entry.SlowMultiplier;
            yield return
                $"{targetKey};seq={entry.Sequence};type={(StatusEffectType)entry.Type};sourceKind={entry.SourceKind};sourceKey={entry.SourceKey};caster={entry.CasterId.Raw};applied={entry.AppliedTick};expire={entry.ExpireTick};next={entry.NextTick};tick={entry.TickIntervalTicks};damage={entry.DamagePerTick};slow={slowBucket};damageType={(DamageType)entry.DamageType}";
        }
    }

    public int CaptureStatusEffectsForMigration(List<StatusEffectMigrationSnapshot> snapshots)
    {
        if (!IsStatusEffectSchedulerActive || snapshots == null)
        {
            return 0;
        }

        snapshots.Clear();
        for (int i = 0; i < MaxActiveStatusEffects; i++)
        {
            StatusEffectEntry entry = StatusEffects[i];
            if (entry.Sequence <= 0)
            {
                continue;
            }

            snapshots.Add(new StatusEffectMigrationSnapshot
            {
                Sequence = entry.Sequence,
                TargetId = entry.TargetId,
                CasterId = entry.CasterId,
                SourceKind = entry.SourceKind,
                SourceKey = entry.SourceKey,
                Type = entry.Type,
                AppliedTick = entry.AppliedTick,
                ExpireTick = entry.ExpireTick,
                NextTick = entry.NextTick,
                TickIntervalTicks = entry.TickIntervalTicks,
                DamagePerTick = entry.DamagePerTick,
                DamageType = entry.DamageType,
                SlowMultiplier = entry.SlowMultiplier,
                Flags = entry.Flags
            });
        }

        return snapshots.Count;
    }

    public int RestoreStatusEffectsFromMigration(IReadOnlyList<StatusEffectMigrationSnapshot> snapshots, string reason = null)
    {
        return RestoreStatusEffectsFromMigrationWithReport(snapshots, reason).Restored;
    }

    public MigrationRestoreReport RestoreStatusEffectsFromMigrationWithReport(
        IReadOnlyList<StatusEffectMigrationSnapshot> snapshots,
        string reason = null,
        MigrationNetworkIdResolver remapResolver = null)
    {
        const string scope = "combat_status_effects";
        if (snapshots == null)
        {
            return MigrationRestoreReport.FailedScope(scope, 1, "status_snapshot_missing");
        }

        int captured = snapshots.Count;
        if (captured == 0)
        {
            return MigrationRestoreReport.Empty(scope);
        }

        if (!IsStatusEffectSchedulerActive || !Object.HasStateAuthority || !EnsureLocalSchedulerState())
        {
            return MigrationRestoreReport.FailedScope(scope, captured, "status_scheduler_not_ready");
        }

        int existingTokenCount = CaptureStatusSlotTokens();
        for (int i = 0; i < existingTokenCount; i++)
        {
            LocalSlotToken token = _statusSlotScratch[i];
            StatusEffectEntry entry = StatusEffects[token.Slot];
            if (entry.Sequence == token.Sequence)
            {
                ClearStatusEffectSlot(token.Slot, entry);
            }
        }

        int restored = 0;
        int skipped = 0;
        int failed = 0;
        int maxSequence = StatusEffectSequence;
        int changedTargetCount = 0;
        int now = Runner.Tick;

        for (int i = 0; i < snapshots.Count; i++)
        {
            StatusEffectMigrationSnapshot snapshot = snapshots[i];
            if (snapshot.Sequence <= 0 || snapshot.TargetId.Raw == 0)
            {
                failed++;
                continue;
            }

            maxSequence = Mathf.Max(maxSequence, snapshot.Sequence);

            if (snapshot.ExpireTick <= now)
            {
                skipped++;
                continue;
            }

            NetworkId targetId = ResolveMigrationNetworkId(snapshot.TargetId, remapResolver);
            NetworkId casterId = ResolveMigrationNetworkId(snapshot.CasterId, remapResolver);
            NetworkObject targetObject = ResolveNetworkObject(targetId);
            if (targetObject == null || IsStatusTargetDead(targetObject))
            {
                skipped++;
                continue;
            }

            int slot = FindEmptyStatusSlot();
            if (slot < 0)
            {
                Debug.LogWarning($"[CombatScheduler.StatusEffects] Migration restore capacity exceeded. capacity={MaxActiveStatusEffects}, requested={snapshots.Count}, reason={reason}");
                failed++;
                continue;
            }

            var entry = new StatusEffectEntry
            {
                Sequence = snapshot.Sequence,
                TargetId = targetId,
                CasterId = casterId,
                SourceKey = snapshot.SourceKey,
                AppliedTick = snapshot.AppliedTick,
                ExpireTick = snapshot.ExpireTick,
                NextTick = snapshot.NextTick,
                TickIntervalTicks = snapshot.TickIntervalTicks,
                DamagePerTick = snapshot.DamagePerTick,
                SlowMultiplier = snapshot.SlowMultiplier,
                PackedMeta = PackStatusMeta(snapshot.SourceKind, snapshot.Type, snapshot.DamageType, snapshot.Flags)
            };
            StatusEffects.Set(slot, entry);
            CommitStatusSlot(slot, entry);

            maxSequence = Mathf.Max(maxSequence, snapshot.Sequence);
            changedTargetCount = AddChangedTarget(_statusChangedTargetScratch, changedTargetCount, targetId);
            restored++;
        }

        StatusEffectSequence = maxSequence;
        RefreshAllStatusCaches();
        for (int i = 0; i < changedTargetCount; i++)
        {
            RefreshStatusCacheForTarget(_statusChangedTargetScratch[i]);
        }

        string failureReason = failed == 0 ? string.Empty : "combat_status_restore_incomplete";
        var report = new MigrationRestoreReport(
            scope,
            captured,
            restored,
            skipped,
            failed,
            failureReason);
        Debug.Log($"[CombatScheduler.StatusEffects] Migration restore complete. {report}, context={reason}");
        return report;
    }

    internal static NetworkId ResolveMigrationNetworkId(
        NetworkId capturedNetworkId,
        MigrationNetworkIdResolver remapResolver)
    {
        if (capturedNetworkId.Raw != 0 &&
            remapResolver != null &&
            remapResolver(capturedNetworkId, out NetworkId remappedNetworkId) &&
            remappedNetworkId.Raw != 0)
        {
            return remappedNetworkId;
        }

        return capturedNetworkId;
    }

    private void ProcessDueStatusEffects()
    {
        if (!IsStatusEffectSchedulerActive || !Object.HasStateAuthority)
        {
            return;
        }

        int now = Runner.Tick;
        if (_statusNextTickDirty)
        {
            RecalculateNextStatusWorkTick();
        }
        if (now < _nextStatusWorkTick)
        {
            return;
        }

        int changedTargetCount = 0;
        int tokenCount = CaptureStatusSlotTokens();
        for (int i = 0; i < tokenCount; i++)
        {
            LocalSlotToken token = _statusSlotScratch[i];
            int slot = token.Slot;
            StatusEffectEntry entry = StatusEffects[slot];
            if (entry.Sequence <= 0 || entry.Sequence != token.Sequence)
            {
                continue;
            }

            NetworkObject targetObject = ResolveNetworkObject(entry.TargetId);
            if (targetObject == null || IsStatusTargetDead(targetObject))
            {
                ClearStatusEffectSlot(slot, entry);
                changedTargetCount = AddChangedTarget(_statusChangedTargetScratch, changedTargetCount, entry.TargetId);
                continue;
            }

            if (entry.ExpireTick <= now)
            {
                ClearStatusEffectSlot(slot, entry);
                changedTargetCount = AddChangedTarget(_statusChangedTargetScratch, changedTargetCount, entry.TargetId);
                continue;
            }

            if (entry.TickIntervalTicks > 0 &&
                entry.DamagePerTick > 0 &&
                entry.NextTick > 0 &&
                entry.NextTick <= now)
            {
                ApplyStatusDotDamage(entry, targetObject);
                if (StatusEffects[slot].Sequence != entry.Sequence)
                {
                    changedTargetCount = AddChangedTarget(_statusChangedTargetScratch, changedTargetCount, entry.TargetId);
                    continue;
                }

                if (IsStatusTargetDead(targetObject))
                {
                    ClearStatusEffectSlot(slot, entry);
                    changedTargetCount = AddChangedTarget(_statusChangedTargetScratch, changedTargetCount, entry.TargetId);
                    continue;
                }

                StatusEffectEntry previous = entry;
                entry.NextTick = now + Mathf.Max(1, entry.TickIntervalTicks);
                StatusEffects.Set(slot, entry);
                NoteStatusSlotUpdated(previous, entry);
            }
        }

        for (int i = 0; i < changedTargetCount; i++)
        {
            RefreshStatusCacheForTarget(_statusChangedTargetScratch[i]);
        }
        RecalculateNextStatusWorkTick();
    }

    private void RebuildStatusCachesFromNetworkEntries()
    {
        if (!IsStatusEffectSchedulerActive)
        {
            return;
        }

        RefreshAllStatusCaches();

        int targetCount = 0;
        for (int i = 0; i < _statusSlotIndex.ActiveCount; i++)
        {
            int slot = _statusSlotIndex.GetActiveSlot(i);
            StatusEffectEntry entry = StatusEffects[slot];
            if (entry.Sequence > 0)
            {
                targetCount = AddChangedTarget(_statusChangedTargetScratch, targetCount, entry.TargetId);
            }
        }

        for (int i = 0; i < targetCount; i++)
        {
            RefreshStatusCacheForTarget(_statusChangedTargetScratch[i]);
        }
    }

    private void ClearStatusEffectsForTarget(NetworkId targetId, string reason)
    {
        if (!EnsureLocalSchedulerState() ||
            !TryGetTargetMask(_statusSlotsByTarget, targetId, out CombatSchedulerSlotMask mask))
        {
            RefreshStatusCacheForTarget(targetId);
            return;
        }

        uint targetRaw = targetId.Raw;
        for (int slot = 0; slot < MaxActiveStatusEffects; slot++)
        {
            if (!mask.Contains(slot))
            {
                continue;
            }

            StatusEffectEntry entry = StatusEffects[slot];
            if (entry.Sequence > 0 && entry.TargetId.Raw == targetRaw)
            {
                ClearStatusEffectSlot(slot, entry);
            }
        }

        RefreshStatusCacheForTarget(targetId);
    }

    private void ClearStatusEffectSlot(int slot, StatusEffectEntry entry)
    {
        if (slot < 0 || slot >= MaxActiveStatusEffects)
        {
            return;
        }

        if (StatusEffects[slot].Sequence == entry.Sequence)
        {
            StatusEffects.Set(slot, default);
            ReleaseStatusSlot(slot, entry);
        }
    }

    private int FindMatchingStatusSlot(NetworkId targetId, NetworkId casterId, int sourceKind, int sourceKey, StatusEffectType type)
    {
        uint targetRaw = targetId.Raw;
        uint casterRaw = casterId.Raw;
        if (!EnsureLocalSchedulerState())
        {
            return -1;
        }

        for (int i = 0; i < _statusSlotIndex.ActiveCount; i++)
        {
            int slot = _statusSlotIndex.GetActiveSlot(i);
            StatusEffectEntry entry = StatusEffects[slot];
            if (entry.Sequence <= 0)
            {
                continue;
            }

            if (entry.TargetId.Raw == targetRaw &&
                entry.CasterId.Raw == casterRaw &&
                entry.SourceKind == sourceKind &&
                entry.SourceKey == sourceKey &&
                entry.Type == (int)type)
            {
                return slot;
            }
        }

        return -1;
    }

    private int FindEmptyStatusSlot()
    {
        if (!EnsureLocalSchedulerState())
        {
            return -1;
        }

        return _statusSlotIndex.TryRentLowest(out int slot) ? slot : -1;
    }

    /// <summary>
    /// Boolean control states (stun/root/silence and non-damaging markers) combine by logical OR.
    /// Extending an existing equal target/type interval therefore preserves their gameplay
    /// semantics. Damage-over-time and slow magnitudes deliberately remain independent because
    /// coalescing those without per-stack expiry data would change damage or movement speed.
    /// </summary>
    private bool TryCoalesceBooleanStatusAtCapacity(
        NetworkId targetId,
        StatusEffectType type,
        int expireTick,
        int damagePerTick,
        int slowMultiplier,
        out int slot)
    {
        slot = -1;
        bool hasDamageMagnitude = damagePerTick > 0;
        bool hasSlowMagnitude = slowMultiplier > 0 && slowMultiplier < FixedPointScale;
        if (hasDamageMagnitude || hasSlowMagnitude || !EnsureLocalSchedulerState())
        {
            return false;
        }

        uint targetRaw = targetId.Raw;
        for (int i = 0; i < _statusSlotIndex.ActiveCount; i++)
        {
            int candidateSlot = _statusSlotIndex.GetActiveSlot(i);
            StatusEffectEntry candidate = StatusEffects[candidateSlot];
            if (candidate.Sequence <= 0 ||
                candidate.TargetId.Raw != targetRaw ||
                candidate.Type != (int)type ||
                candidate.DamagePerTick > 0 ||
                (candidate.SlowMultiplier > 0 && candidate.SlowMultiplier < FixedPointScale))
            {
                continue;
            }

            // Lowest sequence is stable even when the active-slot free-list was rebuilt.
            if (slot < 0 || candidate.Sequence < StatusEffects[slot].Sequence)
            {
                slot = candidateSlot;
            }
        }

        return slot >= 0 && expireTick > 0;
    }

    private static int PackStatusMeta(int sourceKind, int type, int damageType, int flags)
    {
        return (sourceKind & 0xF) |
               ((type & 0xFF) << 4) |
               ((damageType & 0xFF) << 12) |
               (flags << 20);
    }

    private void RefreshStatusCacheForTarget(NetworkId targetId)
    {
        NetworkObject targetObject = ResolveNetworkObject(targetId);
        if (targetObject == null)
        {
            return;
        }

        BuffManager buffManager = targetObject.GetComponent<BuffManager>() ?? targetObject.GetComponentInChildren<BuffManager>();
        if (buffManager == null)
        {
            return;
        }

        StatusEffectType flags = StatusEffectType.None;
        float slowMultiplier = 1f;
        uint targetRaw = targetId.Raw;
        if (CanUseAuthorityLocalState &&
            TryGetTargetMask(_statusSlotsByTarget, targetId, out CombatSchedulerSlotMask mask))
        {
            for (int slot = 0; slot < MaxActiveStatusEffects; slot++)
            {
                if (!mask.Contains(slot))
                {
                    continue;
                }

                StatusEffectEntry entry = StatusEffects[slot];
                if (entry.Sequence <= 0 || entry.TargetId.Raw != targetRaw)
                {
                    continue;
                }

                flags |= (StatusEffectType)entry.Type;
                float entrySlow = UnpackFloat(entry.SlowMultiplier);
                if (entrySlow > 0f && entrySlow < 1f)
                {
                    slowMultiplier *= entrySlow;
                }
            }
        }
        else if (!CanUseAuthorityLocalState)
        {
            for (int i = 0; i < MaxActiveStatusEffects; i++)
            {
                StatusEffectEntry entry = StatusEffects[i];
                if (entry.Sequence <= 0 || entry.TargetId.Raw != targetRaw)
                {
                    continue;
                }

                flags |= (StatusEffectType)entry.Type;
                float entrySlow = UnpackFloat(entry.SlowMultiplier);
                if (entrySlow > 0f && entrySlow < 1f)
                {
                    slowMultiplier *= entrySlow;
                }
            }
        }

        buffManager.ApplyStatusSchedulerCache(flags, Mathf.Max(0.1f, slowMultiplier));
    }

    private void RefreshAllStatusCaches()
    {
        foreach (var buffManager in UnityEngine.Object.FindObjectsOfType<BuffManager>())
        {
            if (buffManager != null)
            {
                buffManager.ApplyStatusSchedulerCache(StatusEffectType.None, 1f);
            }
        }
    }

    private void ApplyStatusDotDamage(StatusEffectEntry entry, NetworkObject targetObject)
    {
        if (targetObject == null)
        {
            return;
        }

        IEnemy enemy = targetObject.GetComponent<IEnemy>();
        if (enemy != null)
        {
            float damage = UnpackFloat(entry.DamagePerTick);
            DamageType damageType = (DamageType)entry.DamageType;
            if (!IsStatusTargetDead(targetObject))
            {
                enemy.TakeDamage(damage, damageType);
            }
        }
    }

    private static bool IsStatusTargetDead(NetworkObject targetObject)
    {
        if (targetObject == null)
        {
            return true;
        }

        IHealth health = targetObject.GetComponent<IHealth>();
        return health != null && health.CurrentHealth <= 0f;
    }

    private static void ResolveStatusSource(
        GameObject caster,
        int sequenceHint,
        out NetworkId casterId,
        out int sourceKind,
        out int sourceKey)
    {
        casterId = default;
        sourceKind = StatusSourceTransient;
        sourceKey = sequenceHint;

        if (caster == null)
        {
            return;
        }

        NetworkObject casterObject = caster.GetComponentInParent<NetworkObject>();
        if (casterObject != null && casterObject.IsValid)
        {
            casterId = casterObject.Id;
            sourceKind = StatusSourceNetworkObject;
            sourceKey = 0;
            return;
        }

        if (caster.GetComponent<ScrollCaster>() != null)
        {
            sourceKind = StatusSourceMagicScroll;
            sourceKey = sequenceHint;
        }
    }

    private static bool TryResolveNetworkObject(BuffManager target, out NetworkObject networkObject)
    {
        networkObject = null;
        if (target == null)
        {
            return false;
        }

        networkObject = target.GetComponentInParent<NetworkObject>();
        return networkObject != null && networkObject.IsValid;
    }
}
