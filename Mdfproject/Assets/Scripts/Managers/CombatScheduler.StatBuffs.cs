using System;
using System.Collections.Generic;
using Fusion;
using UnityEngine;

public partial class CombatScheduler
{
    private const int MaxActiveStatBuffs = 32;
    private const int StatBuffSourceBerserk = 1001;

    [Networked] public int StatBuffSequence { get; private set; }
    [Networked, Capacity(MaxActiveStatBuffs)] private NetworkArray<StatBuffEntry> StatBuffs { get; }

    private struct StatBuffEntry : INetworkStruct
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
        get
        {
            if (!IsStatBuffSchedulerActive)
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < MaxActiveStatBuffs; i++)
            {
                if (StatBuffs[i].Sequence > 0)
                {
                    count++;
                }
            }

            return count;
        }
    }

    public int GetActiveStatBuffCountFor(BuffManager target)
    {
        if (!IsStatBuffSchedulerActive || !TryResolveNetworkObject(target, out var targetObject))
        {
            return 0;
        }

        uint targetRaw = targetObject.Id.Raw;
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

    public bool ApplyBerserkStatBuffs(BuffManager target, GameObject caster, bool includeMoveSpeed, float duration)
    {
        if (target == null)
        {
            return false;
        }

        bool applied = false;
        applied |= ApplyStatBuff(target, StatType.AttackDamage, 0.5f, true, duration, caster, StatBuffSourceBerserk + 1);
        applied |= ApplyStatBuff(target, StatType.AttackSpeed, 0.5f, true, duration, caster, StatBuffSourceBerserk + 2);
        if (includeMoveSpeed)
        {
            applied |= ApplyStatBuff(target, StatType.MoveSpeed, 1f, true, duration, caster, StatBuffSourceBerserk + 3);
        }

        return applied;
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
        if (!IsStatBuffSchedulerActive || !Object.HasStateAuthority || target == null || duration <= 0f)
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
            existing.ExpireTick = Mathf.Max(existing.ExpireTick, expireTick);
            existing.Value = PackFloat(value);
            StatBuffs.Set(existingSlot, existing);
            RefreshStatBuffCacheForTarget(targetObject.Id);
            return true;
        }

        int emptySlot = FindEmptyStatBuffSlot();
        if (emptySlot < 0)
        {
            Debug.LogWarning($"[CombatScheduler.StatBuffs] Active stat buff capacity exceeded. capacity={MaxActiveStatBuffs}, target={targetObject.Id}, stat={statType}");
            return true;
        }

        int nextSeq = StatBuffSequence + 1;
        StatBuffSequence = nextSeq;
        StatBuffs.Set(emptySlot, new StatBuffEntry
        {
            Sequence = nextSeq,
            TargetId = targetObject.Id,
            CasterId = casterId,
            SourceKind = sourceKind,
            SourceKey = sourceKey,
            StatType = (int)statType,
            Value = PackFloat(value),
            IsPercentage = isPercentage ? 1 : 0,
            AppliedTick = now,
            ExpireTick = expireTick,
            Flags = 0
        });

        RefreshStatBuffCacheForTarget(targetObject.Id);
        return true;
    }

    public bool ClearStatBuffsForTarget(BuffManager target, string reason = null)
    {
        if (!IsStatBuffSchedulerActive || !Object.HasStateAuthority || target == null)
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
        if (!IsStatBuffSchedulerActive || !Object.HasStateAuthority)
        {
            return;
        }

        for (int i = 0; i < MaxActiveStatBuffs; i++)
        {
            if (StatBuffs[i].Sequence > 0)
            {
                StatBuffs.Set(i, default);
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
                $"{targetKey};seq={entry.Sequence};sourceKind={entry.SourceKind};sourceKey={entry.SourceKey};caster={entry.CasterId.Raw};stat={(StatType)entry.StatType};value={entry.Value};percent={entry.IsPercentage};applied={entry.AppliedTick};expire={entry.ExpireTick}";
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
        if (!IsStatBuffSchedulerActive || !Object.HasStateAuthority || snapshots == null || snapshots.Count == 0)
        {
            return 0;
        }

        for (int i = 0; i < MaxActiveStatBuffs; i++)
        {
            if (StatBuffs[i].Sequence > 0)
            {
                StatBuffs.Set(i, default);
            }
        }

        int restored = 0;
        int maxSequence = StatBuffSequence;
        int now = Runner.Tick;
        var changedTargets = new List<NetworkId>();

        for (int i = 0; i < snapshots.Count; i++)
        {
            StatBuffMigrationSnapshot snapshot = snapshots[i];
            if (snapshot.Sequence <= 0 || snapshot.TargetId.Raw == 0 || snapshot.ExpireTick <= now)
            {
                continue;
            }

            NetworkObject targetObject = ResolveNetworkObject(snapshot.TargetId);
            if (targetObject == null || IsStatusTargetDead(targetObject))
            {
                continue;
            }

            int slot = FindEmptyStatBuffSlot();
            if (slot < 0)
            {
                Debug.LogWarning($"[CombatScheduler.StatBuffs] Migration restore capacity exceeded. capacity={MaxActiveStatBuffs}, requested={snapshots.Count}, reason={reason}");
                break;
            }

            StatBuffs.Set(slot, new StatBuffEntry
            {
                Sequence = snapshot.Sequence,
                TargetId = snapshot.TargetId,
                CasterId = snapshot.CasterId,
                SourceKind = snapshot.SourceKind,
                SourceKey = snapshot.SourceKey,
                StatType = snapshot.StatType,
                Value = snapshot.Value,
                IsPercentage = snapshot.IsPercentage,
                AppliedTick = snapshot.AppliedTick,
                ExpireTick = snapshot.ExpireTick,
                Flags = snapshot.Flags
            });

            maxSequence = Mathf.Max(maxSequence, snapshot.Sequence);
            AddChangedStatusTarget(changedTargets, snapshot.TargetId);
            restored++;
        }

        StatBuffSequence = maxSequence;
        RefreshAllStatBuffCaches();
        foreach (NetworkId targetId in changedTargets)
        {
            RefreshStatBuffCacheForTarget(targetId);
        }

        Debug.Log($"[CombatScheduler.StatBuffs] Migration restore complete. restored={restored}, cached={snapshots.Count}, reason={reason}");
        return restored;
    }

    private void ProcessDueStatBuffs()
    {
        if (!IsStatBuffSchedulerActive || !Object.HasStateAuthority)
        {
            return;
        }

        int now = Runner.Tick;
        var changedTargets = new List<NetworkId>();
        for (int i = 0; i < MaxActiveStatBuffs; i++)
        {
            StatBuffEntry entry = StatBuffs[i];
            if (entry.Sequence <= 0)
            {
                continue;
            }

            NetworkObject targetObject = ResolveNetworkObject(entry.TargetId);
            if (targetObject == null || IsStatusTargetDead(targetObject) || entry.ExpireTick <= now)
            {
                StatBuffs.Set(i, default);
                AddChangedStatusTarget(changedTargets, entry.TargetId);
            }
        }

        foreach (NetworkId targetId in changedTargets)
        {
            RefreshStatBuffCacheForTarget(targetId);
        }
    }

    private void RebuildStatBuffCachesFromNetworkEntries()
    {
        if (!IsStatBuffSchedulerActive)
        {
            return;
        }

        RefreshAllStatBuffCaches();
        var targetIds = new List<NetworkId>();
        for (int i = 0; i < MaxActiveStatBuffs; i++)
        {
            StatBuffEntry entry = StatBuffs[i];
            if (entry.Sequence > 0)
            {
                AddChangedStatusTarget(targetIds, entry.TargetId);
            }
        }

        foreach (NetworkId targetId in targetIds)
        {
            RefreshStatBuffCacheForTarget(targetId);
        }
    }

    private void ClearStatBuffsForTarget(NetworkId targetId, string reason)
    {
        uint targetRaw = targetId.Raw;
        for (int i = 0; i < MaxActiveStatBuffs; i++)
        {
            StatBuffEntry entry = StatBuffs[i];
            if (entry.Sequence > 0 && entry.TargetId.Raw == targetRaw)
            {
                StatBuffs.Set(i, default);
            }
        }

        RefreshStatBuffCacheForTarget(targetId);
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
        for (int i = 0; i < MaxActiveStatBuffs; i++)
        {
            StatBuffEntry entry = StatBuffs[i];
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
                return i;
            }
        }

        return -1;
    }

    private int FindEmptyStatBuffSlot()
    {
        for (int i = 0; i < MaxActiveStatBuffs; i++)
        {
            if (StatBuffs[i].Sequence <= 0)
            {
                return i;
            }
        }

        return -1;
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
        for (int i = 0; i < MaxActiveStatBuffs; i++)
        {
            StatBuffEntry entry = StatBuffs[i];
            if (entry.Sequence <= 0 || entry.TargetId.Raw != targetRaw)
            {
                continue;
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

        buffManager.ApplyStatBuffSchedulerCache(
            attackDamageFlatBonus,
            attackDamagePercentBonus,
            attackSpeedPercentBonus,
            Mathf.Max(0.1f, 1f + moveSpeedPercentBonus));
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
