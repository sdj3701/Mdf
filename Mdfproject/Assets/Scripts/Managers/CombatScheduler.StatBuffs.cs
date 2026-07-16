using System;
using System.Collections.Generic;
using Fusion;
using UnityEngine;

public partial class CombatScheduler
{
    private const int MaxActiveStatBuffs = CombatSchedulerCapacityConfig.StatBuffCapacity;

    [Networked] public int StatBuffSequence { get; private set; }
    [Networked, Capacity(MaxActiveStatBuffs)] private NetworkArray<StatBuffEntry> StatBuffs { get; }

    private struct StatBuffEntry : INetworkStruct
    {
        public int Sequence;
        public NetworkId TargetId;
        public NetworkId CasterId;
        public int SourceKey;
        public int Value;
        public int AppliedTick;
        public int ExpireTick;
        public int PackedMeta;

        public int SourceKind => PackedMeta & 0xF;
        public int StatType => (PackedMeta >> 4) & 0xFF;
        public int IsPercentage => (PackedMeta >> 12) & 0x1;
        public int Flags => PackedMeta >> 13;
    }

    public struct StatBuffMigrationSnapshot
    {
        public int Sequence;
        public NetworkId TargetId;
        public NetworkId CasterId;
        public int SourceKind;
        public int SourceKey;
        public int StatType;
        public int Value;
        public int IsPercentage;
        public int AppliedTick;
        public int ExpireTick;
        public int Flags;
    }

    public bool IsStatBuffSchedulerActive =>
        Runner != null &&
        Runner.IsRunning &&
        Object != null &&
        Object.IsValid;

    public int ActiveStatBuffCount
    {
        get => GetCurrentStatBuffCount();
    }

    public bool CanApplyStatBuffBatch(
        IReadOnlyList<BuffManager> targets,
        BuffStatEffect buffEffect,
        GameObject caster)
    {
        if (!IsStatBuffSchedulerActive || !Object.HasStateAuthority || targets == null || buffEffect == null ||
            buffEffect.duration <= 0f || !EnsureLocalSchedulerState())
        {
            return false;
        }

        int sourceKey = Animator.StringToHash(buffEffect.name);
        ResolveStatusSource(
            caster,
            StatBuffSequence + 1,
            out NetworkId casterId,
            out int sourceKind,
            out _);

        int availableSlots = MaxActiveStatBuffs - _statBuffSlotIndex.ActiveCount - GetPreflightStatBuffReservations();
        int requiredSlots = 0;
        var visitedTargets = new HashSet<uint>();
        for (int i = 0; i < targets.Count; i++)
        {
            if (!TryResolveNetworkObject(targets[i], out NetworkObject targetObject) ||
                !visitedTargets.Add(targetObject.Id.Raw))
            {
                continue;
            }

            if (FindMatchingStatBuffSlot(
                    targetObject.Id,
                    casterId,
                    sourceKind,
                    sourceKey,
                    buffEffect.statToBuff,
                    buffEffect.isPercentage) >= 0)
            {
                continue;
            }

            if (requiredSlots < availableSlots)
            {
                requiredSlots++;
                continue;
            }

            RecordCapacityRecovery(CapacityRecoveryKind.StatBuffBackpressure);
            return false;
        }

        ReservePreflightStatBuffSlots(requiredSlots);
        return true;
    }

    public int GetActiveStatBuffCountFor(BuffManager target)
    {
        if (!IsStatBuffSchedulerActive || !TryResolveNetworkObject(target, out var targetObject))
        {
            return 0;
        }

        uint targetRaw = targetObject.Id.Raw;
        if (CanUseAuthorityLocalState &&
            _statBuffSlotsByTarget.TryGetValue(targetRaw, out CombatSchedulerSlotMask indexedMask))
        {
            return indexedMask.Count(MaxActiveStatBuffs);
        }

        int count = 0;
        for (int i = 0; i < MaxActiveStatBuffs; i++)
        {
            StatBuffEntry entry = StatBuffs[i];
            if (entry.Sequence > 0 && entry.TargetId.Raw == targetRaw)
            {
                count++;
            }
        }

        return count;
    }

    public bool ApplyStatBuff(BuffManager target, BuffStatEffect buffEffect, GameObject caster)
    {
        if (buffEffect == null)
        {
            return false;
        }

        return ApplyStatBuff(
            target,
            buffEffect.statToBuff,
            buffEffect.value,
            buffEffect.isPercentage,
            buffEffect.duration,
            caster,
            Animator.StringToHash(buffEffect.name));
    }

    public bool ApplyStatBuff(
        BuffManager target,
        StatType statType,
        float value,
        bool isPercentage,
        float duration,
        GameObject caster,
        int sourceKey)
    {
        return ApplyStatBuffInternal(target, statType, value, isPercentage, duration, caster, sourceKey, 0);
    }

    private bool ApplyStatBuffInternal(
        BuffManager target,
        StatType statType,
        float value,
        bool isPercentage,
        float duration,
        GameObject caster,
        int sourceKey,
        int flags)
    {
        if (!IsStatBuffSchedulerActive || !Object.HasStateAuthority || target == null || duration <= 0f ||
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
        int sequenceHint = StatBuffSequence + 1;
        ResolveStatusSource(caster, sequenceHint, out NetworkId casterId, out int sourceKind, out _);

        int existingSlot = FindMatchingStatBuffSlot(
            targetObject.Id,
            casterId,
            sourceKind,
            sourceKey,
            statType,
            isPercentage);

        int expireTick = now + Mathf.Max(1, durationTicks);
        if (existingSlot >= 0)
        {
            StatBuffEntry existing = StatBuffs[existingSlot];
            StatBuffEntry previous = existing;
            existing.ExpireTick = Mathf.Max(existing.ExpireTick, expireTick);
            existing.Value = PackFloat(value);
            existing.PackedMeta = PackStatBuffMeta(sourceKind, (int)statType, isPercentage ? 1 : 0, flags);
            StatBuffs.Set(existingSlot, existing);
            NoteStatBuffSlotUpdated(previous, existing);
            RefreshStatBuffCacheForTarget(targetObject.Id);
            return true;
        }

        int emptySlot = FindEmptyStatBuffSlot();
        if (emptySlot < 0)
        {
            RecordCapacityRecovery(CapacityRecoveryKind.StatBuffBackpressure);
            return false;
        }

        int nextSeq = StatBuffSequence + 1;
        StatBuffSequence = nextSeq;
        var entry = new StatBuffEntry
        {
            Sequence = nextSeq,
            TargetId = targetObject.Id,
            CasterId = casterId,
            SourceKey = sourceKey,
            Value = PackFloat(value),
            AppliedTick = now,
            ExpireTick = expireTick,
            PackedMeta = PackStatBuffMeta(sourceKind, (int)statType, isPercentage ? 1 : 0, flags)
        };
        StatBuffs.Set(emptySlot, entry);
        CommitStatBuffSlot(emptySlot, entry);

        RefreshStatBuffCacheForTarget(targetObject.Id);
        RefreshNetworkBudgetPeaks();
        return true;
    }

    public bool ClearStatBuffsForTarget(BuffManager target, string reason = null)
    {
        if (!IsStatBuffSchedulerActive || !Object.HasStateAuthority || target == null ||
            !EnsureLocalSchedulerState())
        {
            return false;
        }

        if (!TryResolveNetworkObject(target, out var targetObject))
        {
            return false;
        }

        ClearStatBuffsForTarget(targetObject.Id, reason);
        return true;
    }

    public void ClearAllStatBuffs(string reason = null)
    {
        if (!IsStatBuffSchedulerActive || !Object.HasStateAuthority || !EnsureLocalSchedulerState())
        {
            return;
        }

        int tokenCount = CaptureStatBuffSlotTokens();
        for (int i = 0; i < tokenCount; i++)
        {
            LocalSlotToken token = _statBuffSlotScratch[i];
            StatBuffEntry entry = StatBuffs[token.Slot];
            if (entry.Sequence == token.Sequence)
            {
                ClearStatBuffSlot(token.Slot, entry);
            }
        }

        RefreshAllStatBuffCaches();
    }

    public IEnumerable<string> BuildActiveStatBuffSnapshotParts(Func<GameObject, string> targetKeyBuilder)
    {
        if (!IsStatBuffSchedulerActive)
        {
            yield break;
        }

        for (int i = 0; i < MaxActiveStatBuffs; i++)
        {
            StatBuffEntry entry = StatBuffs[i];
            if (entry.Sequence <= 0)
            {
                continue;
            }

            NetworkObject targetObject = ResolveNetworkObject(entry.TargetId);
            string targetKey = targetObject != null && targetKeyBuilder != null
                ? targetKeyBuilder(targetObject.gameObject)
                : $"targetId={entry.TargetId.Raw}";
            yield return
                $"{targetKey};seq={entry.Sequence};sourceKind={entry.SourceKind};sourceKey={entry.SourceKey};caster={entry.CasterId.Raw};stat={(StatType)entry.StatType};value={entry.Value};percent={entry.IsPercentage};flags={entry.Flags};applied={entry.AppliedTick};expire={entry.ExpireTick}";
        }
    }

    public int CaptureStatBuffsForMigration(List<StatBuffMigrationSnapshot> snapshots)
    {
        if (!IsStatBuffSchedulerActive || snapshots == null)
        {
            return 0;
        }

        snapshots.Clear();
        for (int i = 0; i < MaxActiveStatBuffs; i++)
        {
            StatBuffEntry entry = StatBuffs[i];
            if (entry.Sequence <= 0)
            {
                continue;
            }

            snapshots.Add(new StatBuffMigrationSnapshot
            {
                Sequence = entry.Sequence,
                TargetId = entry.TargetId,
                CasterId = entry.CasterId,
                SourceKind = entry.SourceKind,
                SourceKey = entry.SourceKey,
                StatType = entry.StatType,
                Value = entry.Value,
                IsPercentage = entry.IsPercentage,
                AppliedTick = entry.AppliedTick,
                ExpireTick = entry.ExpireTick,
                Flags = entry.Flags
            });
        }

        return snapshots.Count;
    }

    public int RestoreStatBuffsFromMigration(IReadOnlyList<StatBuffMigrationSnapshot> snapshots, string reason = null)
    {
        return RestoreStatBuffsFromMigrationWithReport(snapshots, reason).Restored;
    }

    public MigrationRestoreReport RestoreStatBuffsFromMigrationWithReport(
        IReadOnlyList<StatBuffMigrationSnapshot> snapshots,
        string reason = null,
        MigrationNetworkIdResolver remapResolver = null)
    {
        const string scope = "combat_stat_buffs";
        if (snapshots == null)
        {
            return MigrationRestoreReport.FailedScope(scope, 1, "stat_buff_snapshot_missing");
        }

        int captured = snapshots.Count;
        if (captured == 0)
        {
            return MigrationRestoreReport.Empty(scope);
        }

        if (!IsStatBuffSchedulerActive || !Object.HasStateAuthority || !EnsureLocalSchedulerState())
        {
            return MigrationRestoreReport.FailedScope(scope, captured, "stat_buff_scheduler_not_ready");
        }

        int existingTokenCount = CaptureStatBuffSlotTokens();
        for (int i = 0; i < existingTokenCount; i++)
        {
            LocalSlotToken token = _statBuffSlotScratch[i];
            StatBuffEntry entry = StatBuffs[token.Slot];
            if (entry.Sequence == token.Sequence)
            {
                ClearStatBuffSlot(token.Slot, entry);
            }
        }

        int restored = 0;
        int skipped = 0;
        int failed = 0;
        int maxSequence = StatBuffSequence;
        int now = Runner.Tick;
        int changedTargetCount = 0;

        for (int i = 0; i < snapshots.Count; i++)
        {
            StatBuffMigrationSnapshot snapshot = snapshots[i];
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

            int slot = FindEmptyStatBuffSlot();
            if (slot < 0)
            {
                Debug.LogWarning($"[CombatScheduler.StatBuffs] Migration restore capacity exceeded. capacity={MaxActiveStatBuffs}, requested={snapshots.Count}, reason={reason}");
                failed++;
                continue;
            }

            var entry = new StatBuffEntry
            {
                Sequence = snapshot.Sequence,
                TargetId = targetId,
                CasterId = casterId,
                SourceKey = snapshot.SourceKey,
                Value = snapshot.Value,
                AppliedTick = snapshot.AppliedTick,
                ExpireTick = snapshot.ExpireTick,
                PackedMeta = PackStatBuffMeta(snapshot.SourceKind, snapshot.StatType, snapshot.IsPercentage, snapshot.Flags)
            };
            StatBuffs.Set(slot, entry);
            CommitStatBuffSlot(slot, entry);

            maxSequence = Mathf.Max(maxSequence, snapshot.Sequence);
            changedTargetCount = AddChangedTarget(_statBuffChangedTargetScratch, changedTargetCount, targetId);
            restored++;
        }

        StatBuffSequence = maxSequence;
        RefreshAllStatBuffCaches();
        for (int i = 0; i < changedTargetCount; i++)
        {
            RefreshStatBuffCacheForTarget(_statBuffChangedTargetScratch[i]);
        }

        string failureReason = failed == 0 ? string.Empty : "combat_stat_buff_restore_incomplete";
        var report = new MigrationRestoreReport(
            scope,
            captured,
            restored,
            skipped,
            failed,
            failureReason);
        Debug.Log($"[CombatScheduler.StatBuffs] Migration restore complete. {report}, context={reason}");
        return report;
    }

    private void ProcessDueStatBuffs()
    {
        if (!IsStatBuffSchedulerActive || !Object.HasStateAuthority)
        {
            return;
        }

        int now = Runner.Tick;
        if (_statBuffNextTickDirty)
        {
            RecalculateNextStatBuffWorkTick();
        }
        if (now < _nextStatBuffWorkTick)
        {
            return;
        }

        int changedTargetCount = 0;
        int tokenCount = CaptureStatBuffSlotTokens();
        for (int i = 0; i < tokenCount; i++)
        {
            LocalSlotToken token = _statBuffSlotScratch[i];
            int slot = token.Slot;
            StatBuffEntry entry = StatBuffs[slot];
            if (entry.Sequence <= 0 || entry.Sequence != token.Sequence)
            {
                continue;
            }

            NetworkObject targetObject = ResolveNetworkObject(entry.TargetId);
            if (targetObject == null || IsStatusTargetDead(targetObject) || entry.ExpireTick <= now)
            {
                ClearStatBuffSlot(slot, entry);
                changedTargetCount = AddChangedTarget(_statBuffChangedTargetScratch, changedTargetCount, entry.TargetId);
            }
        }

        for (int i = 0; i < changedTargetCount; i++)
        {
            RefreshStatBuffCacheForTarget(_statBuffChangedTargetScratch[i]);
        }
        RecalculateNextStatBuffWorkTick();
    }

    private void RebuildStatBuffCachesFromNetworkEntries()
    {
        if (!IsStatBuffSchedulerActive)
        {
            return;
        }

        RefreshAllStatBuffCaches();
        int targetCount = 0;
        for (int i = 0; i < _statBuffSlotIndex.ActiveCount; i++)
        {
            int slot = _statBuffSlotIndex.GetActiveSlot(i);
            StatBuffEntry entry = StatBuffs[slot];
            if (entry.Sequence > 0)
            {
                targetCount = AddChangedTarget(_statBuffChangedTargetScratch, targetCount, entry.TargetId);
            }
        }

        for (int i = 0; i < targetCount; i++)
        {
            RefreshStatBuffCacheForTarget(_statBuffChangedTargetScratch[i]);
        }
    }

    private void ClearStatBuffsForTarget(NetworkId targetId, string reason)
    {
        if (!EnsureLocalSchedulerState() ||
            !TryGetTargetMask(_statBuffSlotsByTarget, targetId, out CombatSchedulerSlotMask mask))
        {
            RefreshStatBuffCacheForTarget(targetId);
            return;
        }

        uint targetRaw = targetId.Raw;
        for (int slot = 0; slot < MaxActiveStatBuffs; slot++)
        {
            if (!mask.Contains(slot))
            {
                continue;
            }

            StatBuffEntry entry = StatBuffs[slot];
            if (entry.Sequence > 0 && entry.TargetId.Raw == targetRaw)
            {
                ClearStatBuffSlot(slot, entry);
            }
        }

        RefreshStatBuffCacheForTarget(targetId);
    }

    private void ClearStatBuffSlot(int slot, StatBuffEntry entry)
    {
        if (slot < 0 || slot >= MaxActiveStatBuffs)
        {
            return;
        }

        if (StatBuffs[slot].Sequence == entry.Sequence)
        {
            StatBuffs.Set(slot, default);
            ReleaseStatBuffSlot(slot, entry);
        }
    }

    private int FindMatchingStatBuffSlot(
        NetworkId targetId,
        NetworkId casterId,
        int sourceKind,
        int sourceKey,
        StatType statType,
        bool isPercentage)
    {
        uint targetRaw = targetId.Raw;
        uint casterRaw = casterId.Raw;
        if (!EnsureLocalSchedulerState())
        {
            return -1;
        }

        for (int i = 0; i < _statBuffSlotIndex.ActiveCount; i++)
        {
            int slot = _statBuffSlotIndex.GetActiveSlot(i);
            StatBuffEntry entry = StatBuffs[slot];
            if (entry.Sequence <= 0)
            {
                continue;
            }

            if (entry.TargetId.Raw == targetRaw &&
                entry.CasterId.Raw == casterRaw &&
                entry.SourceKind == sourceKind &&
                entry.SourceKey == sourceKey &&
                entry.StatType == (int)statType &&
                entry.IsPercentage == (isPercentage ? 1 : 0))
            {
                return slot;
            }
        }

        return -1;
    }

    private int FindEmptyStatBuffSlot()
    {
        if (!EnsureLocalSchedulerState())
        {
            return -1;
        }

        return _statBuffSlotIndex.TryRentLowest(out int slot) ? slot : -1;
    }

    private static int PackStatBuffMeta(int sourceKind, int statType, int isPercentage, int flags)
    {
        return (sourceKind & 0xF) |
               ((statType & 0xFF) << 4) |
               ((isPercentage & 0x1) << 12) |
               (flags << 13);
    }

    private void RefreshStatBuffCacheForTarget(NetworkId targetId)
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

        float attackDamageFlatBonus = 0f;
        float attackDamagePercentBonus = 0f;
        float attackSpeedPercentBonus = 0f;
        float moveSpeedPercentBonus = 0f;

        uint targetRaw = targetId.Raw;
        if (CanUseAuthorityLocalState &&
            TryGetTargetMask(_statBuffSlotsByTarget, targetId, out CombatSchedulerSlotMask mask))
        {
            for (int slot = 0; slot < MaxActiveStatBuffs; slot++)
            {
                if (!mask.Contains(slot))
                {
                    continue;
                }

                AccumulateStatBuffCache(StatBuffs[slot], targetRaw,
                    ref attackDamageFlatBonus,
                    ref attackDamagePercentBonus,
                    ref attackSpeedPercentBonus,
                    ref moveSpeedPercentBonus);
            }
        }
        else if (!CanUseAuthorityLocalState)
        {
            for (int i = 0; i < MaxActiveStatBuffs; i++)
            {
                AccumulateStatBuffCache(StatBuffs[i], targetRaw,
                    ref attackDamageFlatBonus,
                    ref attackDamagePercentBonus,
                    ref attackSpeedPercentBonus,
                    ref moveSpeedPercentBonus);
            }
        }

        buffManager.ApplyStatBuffSchedulerCache(
            attackDamageFlatBonus,
            attackDamagePercentBonus,
            attackSpeedPercentBonus,
            Mathf.Max(0.1f, 1f + moveSpeedPercentBonus));
    }

    private static void AccumulateStatBuffCache(
        StatBuffEntry entry,
        uint targetRaw,
        ref float attackDamageFlatBonus,
        ref float attackDamagePercentBonus,
        ref float attackSpeedPercentBonus,
        ref float moveSpeedPercentBonus)
    {
        if (entry.Sequence <= 0 || entry.TargetId.Raw != targetRaw)
        {
            return;
        }

        float value = UnpackFloat(entry.Value);
        bool percent = entry.IsPercentage != 0;
        var statType = (StatType)entry.StatType;
        if (statType == StatType.AttackDamage)
        {
            if (percent)
            {
                attackDamagePercentBonus += value;
            }
            else
            {
                attackDamageFlatBonus += value;
            }
        }
        else if (statType == StatType.AttackSpeed && percent)
        {
            attackSpeedPercentBonus += value;
        }
        else if (statType == StatType.MoveSpeed && percent)
        {
            moveSpeedPercentBonus += value;
        }
    }

    private void RefreshAllStatBuffCaches()
    {
        foreach (var buffManager in UnityEngine.Object.FindObjectsOfType<BuffManager>())
        {
            if (buffManager != null)
            {
                buffManager.ApplyStatBuffSchedulerCache(0f, 0f, 0f, 1f);
            }
        }
    }
}
