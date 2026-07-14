using System.Collections.Generic;
using Fusion;
using UnityEngine;

public partial class CombatScheduler
{
    private const int MaxActiveZones = CombatSchedulerCapacityConfig.ZoneCapacity;
    private const int ZoneFlagTargetBatchInProgress = 1;
    private const int MaxTransientZonePayloadResolveRetries = 120;

    [Networked] public int ZoneSequence { get; private set; }
    [Networked] public int ZonePulseDebtSequence { get; private set; }
    [Networked, Capacity(MaxActiveZones)] private NetworkArray<ZoneEntry> Zones { get; }
    [Networked] private NetworkBool ZonePulseBackpressureActive { get; set; }
    [Networked] private NetworkBool ZoneDebtTerminalFailureActive { get; set; }
    [Networked] private int ZoneDebtTerminalFailureSequence { get; set; }
    [Networked] private int ZoneDebtTerminalFailureKindValue { get; set; }

    private readonly Dictionary<int, ZonePayload> _zonePayloads = new Dictionary<int, ZonePayload>();
    private readonly List<NetworkObject> _zoneTickTargetScratch = new List<NetworkObject>(64);
    private readonly HashSet<uint> _zoneTickTargetIdScratch = new HashSet<uint>();
    private readonly List<GameObject> _zoneSingleTargetScratch = new List<GameObject>(1);

    private struct ZoneEntry : INetworkStruct
    {
        public int Sequence;
        public NetworkId CasterId;
        public int SourceKey;
        public int ZoneEffectHash;
        public int TargetingHash;
        public int PositionX;
        public int PositionY;
        public int PositionZ;
        public int Range;
        public int AppliedTick;
        public int ExpireTick;
        public int NextTick;
        public int TickIntervalTicks;
        public int ActivePulseToken;
        public int PendingTargetCount;
        public int PayloadResolveRetryCount;
        public int PackedMeta;

        public int SourceKind => PackedMeta & 0xF;
        public int Flags => PackedMeta >> 4;
    }

    private sealed class ZonePayload
    {
        public ZoneEffect Effect;
        public TargetingStrategy TargetingStrategy;
        public GameObject Caster;
        public MonoBehaviour Runner;
        public ZoneController Controller;
    }

    private enum ZoneTickApplyResult
    {
        Applied,
        Backpressured,
        TransientUnavailable,
        TerminalFailure
    }

    private enum ZoneDebtTerminalFailureKind
    {
        None,
        PayloadReferenceCorrupt,
        EffectListLost,
        PresentationSourceLost,
        TargetMarkerConflict,
        EffectCursorCorrupt,
        EffectCommitRejectedAfterPreflight,
        EffectCursorCommitFailed
    }

    public struct ZoneMigrationSnapshot
    {
        public int Sequence;
        public NetworkId CasterId;
        public int SourceKind;
        public int SourceKey;
        public int ZoneEffectHash;
        public int TargetingHash;
        public int PositionX;
        public int PositionY;
        public int PositionZ;
        public int Range;
        public int AppliedTick;
        public int ExpireTick;
        public int NextTick;
        public int TickIntervalTicks;
        public int ActivePulseToken;
        public int PendingTargetCount;
        public int PayloadResolveRetryCount;
        public int Flags;
    }

    public bool IsZoneSchedulerActive =>
        Runner != null &&
        Runner.IsRunning &&
        Object != null &&
        Object.IsValid;

    public int ActiveZoneCount
    {
        get => GetCurrentZoneCount();
    }

    public bool IsZonePulseBackpressured => ZonePulseBackpressureActive;

    public bool HasZoneDebtTerminalFailure => ZoneDebtTerminalFailureActive;

    public bool TryGetZoneDebtTerminalFailure(out string reason)
    {
        if (!ZoneDebtTerminalFailureActive)
        {
            reason = null;
            return false;
        }

        reason = $"zone_debt_terminal_failure:sequence={ZoneDebtTerminalFailureSequence},kind={(ZoneDebtTerminalFailureKind)ZoneDebtTerminalFailureKindValue}";
        return true;
    }

    public bool ValidateMaterializedZonePulseDebt(out string reason)
    {
        reason = null;
        if (!IsZoneSchedulerActive || !EnsureLocalSchedulerState())
        {
            reason = "zone_scheduler_not_ready";
            return false;
        }

        if (TryGetZoneDebtTerminalFailure(out reason))
        {
            return false;
        }

        int activePulseToken = 0;
        int expectedTargetCount = 0;
        int activePulseCount = 0;
        for (int i = 0; i < _zoneSlotIndex.ActiveCount; i++)
        {
            ZoneEntry entry = Zones[_zoneSlotIndex.GetActiveSlot(i)];
            if (entry.Sequence <= 0 || (entry.Flags & ZoneFlagTargetBatchInProgress) == 0)
            {
                continue;
            }

            activePulseCount++;
            activePulseToken = entry.ActivePulseToken;
            expectedTargetCount = entry.PendingTargetCount;
            if (!TryResolveZonePayload(entry, out ZonePayload payload) ||
                payload?.Effect == null ||
                payload.Effect.effectsPerTick == null ||
                payload.Effect.effectsPerTick.Count == 0 ||
                payload.TargetingStrategy == null)
            {
                reason = $"materialized_payload_unavailable:zone={entry.Sequence},pulse={entry.ActivePulseToken}";
                return false;
            }
        }

        if (activePulseCount > 1)
        {
            reason = $"multiple_materialized_pulses:{activePulseCount}";
            return false;
        }

        int actualTargetCount = 0;
        Unit[] units = UnityEngine.Object.FindObjectsOfType<Unit>();
        for (int i = 0; i < units.Length; i++)
        {
            Unit unit = units[i];
            NetworkObject targetObject = unit != null ? unit.Object : null;
            if (targetObject == null || !targetObject.IsValid || targetObject.Runner != Runner)
            {
                continue;
            }

            int marker = unit.PendingZonePulseDebtToken;
            if (marker <= 0)
            {
                continue;
            }

            if (activePulseCount != 1 || marker != activePulseToken)
            {
                reason = $"orphan_unit_marker:target={targetObject.Id.Raw},marker={marker},active={activePulseToken}";
                return false;
            }


            int nextEffectIndex = unit.PendingZonePulseNextEffectIndex;
            ZoneEntry activeEntry = FindZoneEntryByPulseToken(activePulseToken);
            if (!IsZoneEffectCursorValid(activeEntry, nextEffectIndex))
            {
                reason = $"unit_effect_cursor_invalid:target={targetObject.Id.Raw},cursor={nextEffectIndex}";
                return false;
            }

            actualTargetCount++;
        }

        Monster[] monsters = UnityEngine.Object.FindObjectsOfType<Monster>();
        for (int i = 0; i < monsters.Length; i++)
        {
            Monster monster = monsters[i];
            NetworkObject targetObject = monster != null ? monster.Object : null;
            if (targetObject == null || !targetObject.IsValid || targetObject.Runner != Runner)
            {
                continue;
            }

            int marker = monster.PendingZonePulseDebtToken;
            if (marker <= 0)
            {
                continue;
            }

            if (activePulseCount != 1 || marker != activePulseToken)
            {
                reason = $"orphan_monster_marker:target={targetObject.Id.Raw},marker={marker},active={activePulseToken}";
                return false;
            }


            int nextEffectIndex = monster.PendingZonePulseNextEffectIndex;
            ZoneEntry activeEntry = FindZoneEntryByPulseToken(activePulseToken);
            if (!IsZoneEffectCursorValid(activeEntry, nextEffectIndex))
            {
                reason = $"monster_effect_cursor_invalid:target={targetObject.Id.Raw},cursor={nextEffectIndex}";
                return false;
            }

            actualTargetCount++;
        }

        if (activePulseCount == 0)
        {
            return true;
        }

        if (activePulseToken <= 0 || expectedTargetCount < 0 || actualTargetCount != expectedTargetCount)
        {
            reason = $"materialized_target_count_mismatch:token={activePulseToken},expected={expectedTargetCount},actual={actualTargetCount}";
            return false;
        }

        return true;
    }

    public void NotifyZonePulseTargetInvalidated(int pulseToken, NetworkId targetId, string reason = null)
    {
        if (pulseToken <= 0 || !IsZoneSchedulerActive || Object == null || !Object.HasStateAuthority ||
            !EnsureLocalSchedulerState())
        {
            return;
        }

        for (int i = 0; i < _zoneSlotIndex.ActiveCount; i++)
        {
            int slot = _zoneSlotIndex.GetActiveSlot(i);
            ZoneEntry entry = Zones[slot];
            if (entry.Sequence <= 0 || entry.ActivePulseToken != pulseToken || entry.PendingTargetCount <= 0)
            {
                continue;
            }

            ZoneEntry previous = entry;
            entry.PendingTargetCount--;
            Zones.Set(slot, entry);
            NoteZoneSlotUpdated(previous, entry);
            RecordZoneDebtTerminalTargetInvalidations(1);
            Debug.Log(
                $"[CombatScheduler.Zones] Materialized pulse target became terminal. " +
                $"zoneSeq={entry.Sequence},pulse={pulseToken},target={targetId.Raw},remaining={entry.PendingTargetCount},reason={reason}");
            return;
        }
    }

    public bool HasUnresolvedDueZoneDebt(out int dueTickCount)
    {
        dueTickCount = 0;
        if (!IsZoneSchedulerActive || !EnsureLocalSchedulerState())
        {
            return false;
        }

        int now = Runner.Tick;
        for (int i = 0; i < _zoneSlotIndex.ActiveCount; i++)
        {
            int slot = _zoneSlotIndex.GetActiveSlot(i);
            ZoneEntry entry = Zones[slot];
            if (entry.Sequence <= 0)
            {
                continue;
            }

            int entryDueTicks = CountDueZoneTicksAtPhaseTransition(
                entry.NextTick,
                entry.ExpireTick,
                entry.TickIntervalTicks,
                now);
            if (entryDueTicks >= int.MaxValue - dueTickCount)
            {
                dueTickCount = int.MaxValue;
                return true;
            }

            dueTickCount += entryDueTicks;
        }

        return dueTickCount > 0;
    }

    public bool CanScheduleZone(
        ZoneEffect effect,
        GameObject caster,
        float skillRange,
        TargetingStrategy targetingStrategy)
    {
        if (!IsZoneSchedulerActive || !Object.HasStateAuthority || effect == null || caster == null ||
            !EnsureLocalSchedulerState())
        {
            return false;
        }

        if (_zoneSlotIndex.FreeCount - GetPreflightZoneReservations() > 0)
        {
            ReservePreflightZoneSlots(1);
            return true;
        }

        RecordCapacityRecovery(CapacityRecoveryKind.ZoneBackpressure);
        return false;
    }

    public bool TryScheduleZone(
        ZoneEffect effect,
        GameObject caster,
        MonoBehaviour effectRunner,
        float skillRange,
        TargetingStrategy targetingStrategy,
        out int sequence)
    {
        sequence = 0;
        if (!IsZoneSchedulerActive || !Object.HasStateAuthority || effect == null || caster == null ||
            !EnsureLocalSchedulerState())
        {
            return false;
        }

        int now = Runner.Tick;
        int durationTicks = SecondsToTicksCeil(effect.zoneDuration);
        int tickIntervalTicks = Mathf.Max(1, SecondsToTicksCeil(effect.tickInterval));
        int nextSeq = ZoneSequence + 1;
        ResolveStatusSource(caster, nextSeq, out NetworkId casterId, out int sourceKind, out int sourceKey);
        Vector3 position = caster.transform.position;
        int zoneEffectHash = StableObjectHash(effect);
        int targetingHash = StableObjectHash(targetingStrategy);
        int packedX = PackFloat(position.x);
        int packedY = PackFloat(position.y);
        int packedZ = PackFloat(position.z);
        int packedRange = PackFloat(skillRange);
        int slot = FindEmptyZoneSlot();
        if (slot < 0)
        {
            RecordCapacityRecovery(CapacityRecoveryKind.ZoneBackpressure);
            return false;
        }

        ZoneSequence = nextSeq;
        sequence = nextSeq;

        var entry = new ZoneEntry
        {
            Sequence = nextSeq,
            CasterId = casterId,
            SourceKey = sourceKey,
            ZoneEffectHash = zoneEffectHash,
            TargetingHash = targetingHash,
            PositionX = packedX,
            PositionY = packedY,
            PositionZ = packedZ,
            Range = packedRange,
            AppliedTick = now,
            ExpireTick = now + Mathf.Max(1, durationTicks),
            NextTick = now,
            TickIntervalTicks = tickIntervalTicks,
            PackedMeta = PackZoneMeta(sourceKind, 0)
        };

        Zones.Set(slot, entry);
        CommitZoneSlot(slot, entry);
        _zonePayloads[nextSeq] = new ZonePayload
        {
            Effect = effect,
            TargetingStrategy = targetingStrategy,
            Caster = caster,
            Runner = effectRunner != null ? effectRunner : this,
            Controller = CreateZoneController(effect, caster, effectRunner, skillRange, targetingStrategy, nextSeq, position)
        };

        RefreshNetworkBudgetPeaks();
        return true;
    }

    public void ClearAllScheduledZones(string reason = null)
    {
        if (!IsZoneSchedulerActive || !Object.HasStateAuthority || !EnsureLocalSchedulerState())
        {
            return;
        }

        int tokenCount = CaptureZoneSlotTokens();
        for (int i = 0; i < tokenCount; i++)
        {
            LocalSlotToken token = _zoneSlotScratch[i];
            ZoneEntry entry = Zones[token.Slot];
            if (entry.Sequence == token.Sequence)
            {
                ClearZoneSlot(token.Slot, entry, reason);
            }
        }
    }

    public void ClearScheduledZone(int sequence, string reason = null)
    {
        if (!IsZoneSchedulerActive || !Object.HasStateAuthority || sequence <= 0 ||
            !EnsureLocalSchedulerState())
        {
            return;
        }

        if (_zoneSlotBySequence.TryGetValue(sequence, out int indexedSlot) &&
            indexedSlot >= 0 && indexedSlot < MaxActiveZones)
        {
            ZoneEntry entry = Zones[indexedSlot];
            if (entry.Sequence == sequence)
            {
                ClearZoneSlot(indexedSlot, entry, reason);
                return;
            }
        }

        for (int i = 0; i < MaxActiveZones; i++)
        {
            ZoneEntry entry = Zones[i];
            if (entry.Sequence == sequence)
            {
                _zoneSlotBySequence[sequence] = i;
                ClearZoneSlot(i, entry, reason);
                return;
            }
        }
    }

    public IEnumerable<string> BuildActiveZoneSnapshotParts()
    {
        if (!IsZoneSchedulerActive)
        {
            yield break;
        }

        for (int i = 0; i < MaxActiveZones; i++)
        {
            ZoneEntry entry = Zones[i];
            if (entry.Sequence <= 0)
            {
                continue;
            }

            Vector3 pos = UnpackVector(entry.PositionX, entry.PositionY, entry.PositionZ);
            yield return
                $"zoneSeq={entry.Sequence};effect={entry.ZoneEffectHash};targeting={entry.TargetingHash};sourceKind={entry.SourceKind};sourceKey={entry.SourceKey};caster={entry.CasterId.Raw};range={entry.Range};tick={entry.TickIntervalTicks};applied={entry.AppliedTick};expire={entry.ExpireTick};next={entry.NextTick};pulse={entry.ActivePulseToken};pendingTargets={entry.PendingTargetCount};payloadRetries={entry.PayloadResolveRetryCount};x={Mathf.RoundToInt(pos.x * 10f)};z={Mathf.RoundToInt(pos.z * 10f)}";
        }
    }

    public int CaptureZonesForMigration(List<ZoneMigrationSnapshot> snapshots)
    {
        if (!IsZoneSchedulerActive || snapshots == null)
        {
            return 0;
        }

        snapshots.Clear();
        for (int i = 0; i < MaxActiveZones; i++)
        {
            ZoneEntry entry = Zones[i];
            if (entry.Sequence <= 0)
            {
                continue;
            }

            snapshots.Add(new ZoneMigrationSnapshot
            {
                Sequence = entry.Sequence,
                CasterId = entry.CasterId,
                SourceKind = entry.SourceKind,
                SourceKey = entry.SourceKey,
                ZoneEffectHash = entry.ZoneEffectHash,
                TargetingHash = entry.TargetingHash,
                PositionX = entry.PositionX,
                PositionY = entry.PositionY,
                PositionZ = entry.PositionZ,
                Range = entry.Range,
                AppliedTick = entry.AppliedTick,
                ExpireTick = entry.ExpireTick,
                NextTick = entry.NextTick,
                TickIntervalTicks = entry.TickIntervalTicks,
                ActivePulseToken = entry.ActivePulseToken,
                PendingTargetCount = entry.PendingTargetCount,
                PayloadResolveRetryCount = entry.PayloadResolveRetryCount,
                Flags = entry.Flags
            });
        }

        return snapshots.Count;
    }

    public int RestoreZonesFromMigration(IReadOnlyList<ZoneMigrationSnapshot> snapshots, string reason = null)
    {
        return RestoreZonesFromMigrationWithReport(snapshots, reason).Restored;
    }

    public MigrationRestoreReport RestoreZonesFromMigrationWithReport(
        IReadOnlyList<ZoneMigrationSnapshot> snapshots,
        string reason = null,
        MigrationNetworkIdResolver remapResolver = null)
    {
        const string scope = "combat_zones";
        if (snapshots == null)
        {
            return MigrationRestoreReport.FailedScope(scope, 1, "zone_snapshot_missing");
        }

        int captured = snapshots.Count;
        if (captured == 0)
        {
            return MigrationRestoreReport.Empty(scope);
        }

        if (!IsZoneSchedulerActive || !Object.HasStateAuthority || !EnsureLocalSchedulerState())
        {
            return MigrationRestoreReport.FailedScope(scope, captured, "zone_scheduler_not_ready");
        }

        int now = Runner.Tick;
        var preservedPulseTokens = new HashSet<int>();
        for (int i = 0; i < snapshots.Count; i++)
        {
            ZoneMigrationSnapshot snapshot = snapshots[i];
            int pulseToken = snapshot.ActivePulseToken;
            if (snapshot.Sequence > 0 &&
                !IsZoneMigrationSnapshotTerminal(snapshot, now) &&
                pulseToken > 0)
            {
                preservedPulseTokens.Add(pulseToken);
            }
        }

        ClearAllScheduledZonesPreservingMaterializedPulses(reason, preservedPulseTokens);

        int restored = 0;
        int skipped = 0;
        int failed = 0;
        int maxSequence = ZoneSequence;
        int maxPulseToken = ZonePulseDebtSequence;
        var restoredSequences = new HashSet<int>();
        for (int i = 0; i < snapshots.Count; i++)
        {
            ZoneMigrationSnapshot snapshot = snapshots[i];
            if (snapshot.Sequence <= 0)
            {
                failed++;
                ClearMaterializedZonePulseTargets(snapshot.ActivePulseToken);
                continue;
            }

            maxSequence = Mathf.Max(maxSequence, snapshot.Sequence);
            maxPulseToken = Mathf.Max(maxPulseToken, snapshot.ActivePulseToken);
            if (IsZoneMigrationSnapshotTerminal(snapshot, now))
            {
                skipped++;
                continue;
            }

            if (!restoredSequences.Add(snapshot.Sequence))
            {
                failed++;
                ClearMaterializedZonePulseTargets(snapshot.ActivePulseToken);
                continue;
            }

            if (_zoneSlotIndex.FreeCount <= 0)
            {
                Debug.LogWarning($"[CombatScheduler.Zones] Migration restore capacity exceeded. capacity={MaxActiveZones}, requested={snapshots.Count}, reason={reason}");
                ClearMaterializedZonePulseTargets(snapshot.ActivePulseToken);
                failed++;
                continue;
            }

            NetworkId casterId = ResolveMigrationNetworkId(snapshot.CasterId, remapResolver);
            var entry = new ZoneEntry
            {
                Sequence = snapshot.Sequence,
                CasterId = casterId,
                SourceKey = snapshot.SourceKey,
                ZoneEffectHash = snapshot.ZoneEffectHash,
                TargetingHash = snapshot.TargetingHash,
                PositionX = snapshot.PositionX,
                PositionY = snapshot.PositionY,
                PositionZ = snapshot.PositionZ,
                Range = snapshot.Range,
                AppliedTick = snapshot.AppliedTick,
                ExpireTick = snapshot.ExpireTick,
                NextTick = snapshot.NextTick,
                TickIntervalTicks = Mathf.Max(1, snapshot.TickIntervalTicks),
                ActivePulseToken = snapshot.ActivePulseToken,
                PendingTargetCount = Mathf.Max(0, snapshot.PendingTargetCount),
                PayloadResolveRetryCount = Mathf.Max(0, snapshot.PayloadResolveRetryCount),
                PackedMeta = PackZoneMeta(snapshot.SourceKind, snapshot.Flags)
            };

            if (!TryResolveZonePayload(entry, out ZonePayload payload) ||
                payload?.Effect == null ||
                payload.TargetingStrategy == null ||
                payload.Caster == null ||
                payload.Controller == null)
            {
                DestroyZoneController(snapshot.Sequence);
                _zonePayloads.Remove(snapshot.Sequence);
                ClearMaterializedZonePulseTargets(snapshot.ActivePulseToken);
                failed++;
                Debug.LogWarning($"[CombatScheduler.Zones] Migration restore rejected unresolved payload. sequence={snapshot.Sequence}, effect={snapshot.ZoneEffectHash}, targeting={snapshot.TargetingHash}, capturedCaster={snapshot.CasterId.Raw}, actualCaster={casterId.Raw}, reason={reason}");
                continue;
            }

            int slot = FindEmptyZoneSlot();
            if (slot < 0)
            {
                DestroyZoneController(snapshot.Sequence);
                _zonePayloads.Remove(snapshot.Sequence);
                ClearMaterializedZonePulseTargets(snapshot.ActivePulseToken);
                failed++;
                Debug.LogWarning($"[CombatScheduler.Zones] Migration restore could not reserve a zone slot. sequence={snapshot.Sequence}, reason={reason}");
                continue;
            }

            Zones.Set(slot, entry);
            CommitZoneSlot(slot, entry);
            restored++;
        }

        ZoneSequence = maxSequence;
        ZonePulseDebtSequence = maxPulseToken;
        RebuildZonePayloadsFromNetworkEntries();
        string failureReason = failed == 0 ? string.Empty : "combat_zone_restore_incomplete";
        var report = new MigrationRestoreReport(
            scope,
            captured,
            restored,
            skipped,
            failed,
            failureReason);
        Debug.Log($"[CombatScheduler.Zones] Migration restore complete. {report}, context={reason}");
        return report;
    }

    private void ClearAllScheduledZonesPreservingMaterializedPulses(
        string reason,
        HashSet<int> preservedPulseTokens)
    {
        int tokenCount = CaptureZoneSlotTokens();
        for (int i = 0; i < tokenCount; i++)
        {
            LocalSlotToken token = _zoneSlotScratch[i];
            ZoneEntry entry = Zones[token.Slot];
            if (entry.Sequence != token.Sequence)
            {
                continue;
            }

            bool preservePulseTargets = entry.ActivePulseToken > 0 &&
                                        preservedPulseTokens != null &&
                                        preservedPulseTokens.Contains(entry.ActivePulseToken);
            ClearZoneSlot(token.Slot, entry, reason, preservePulseTargets);
        }
    }

    public void CloseAllScheduledZonesForPhaseTransition(string reason = null)
    {
        if (!IsZoneSchedulerActive || !Object.HasStateAuthority || !EnsureLocalSchedulerState())
        {
            return;
        }

        int now = Runner.Tick;
        int tokenCount = CaptureZoneSlotTokens();
        for (int i = 0; i < tokenCount; i++)
        {
            LocalSlotToken token = _zoneSlotScratch[i];
            ZoneEntry entry = Zones[token.Slot];
            if (entry.Sequence == token.Sequence)
            {
                CloseScheduledZoneForPhaseTransition(token.Slot, entry, now, reason);
            }
        }

        RecalculateNextZoneWorkTick();
    }

    public void CloseScheduledZoneForPhaseTransition(int sequence, string reason = null)
    {
        if (!IsZoneSchedulerActive || !Object.HasStateAuthority || sequence <= 0 ||
            !EnsureLocalSchedulerState())
        {
            return;
        }

        if (_zoneSlotBySequence.TryGetValue(sequence, out int indexedSlot) &&
            indexedSlot >= 0 && indexedSlot < MaxActiveZones)
        {
            ZoneEntry indexedEntry = Zones[indexedSlot];
            if (indexedEntry.Sequence == sequence)
            {
                CloseScheduledZoneForPhaseTransition(indexedSlot, indexedEntry, Runner.Tick, reason);
                RecalculateNextZoneWorkTick();
                return;
            }
        }

        for (int slot = 0; slot < MaxActiveZones; slot++)
        {
            ZoneEntry entry = Zones[slot];
            if (entry.Sequence != sequence)
            {
                continue;
            }

            CloseScheduledZoneForPhaseTransition(slot, entry, Runner.Tick, reason);
            RecalculateNextZoneWorkTick();
            return;
        }
    }

    public static bool IsZoneMigrationSnapshotTerminal(ZoneMigrationSnapshot snapshot, int nowTick)
    {
        return snapshot.ExpireTick <= nowTick &&
               snapshot.NextTick >= snapshot.ExpireTick;
    }

    private void ProcessDueZones()
    {
        if (!IsZoneSchedulerActive || !Object.HasStateAuthority)
        {
            return;
        }

        int now = Runner.Tick;
        if (_zoneNextTickDirty)
        {
            RecalculateNextZoneWorkTick();
        }
        if (now < _nextZoneWorkTick)
        {
            ZonePulseBackpressureActive = false;
            return;
        }

        int tokenCount = CaptureZoneSlotTokens();
        SortZoneTokensByDueTick(tokenCount);
        bool blocked = false;
        for (int i = 0; i < tokenCount; i++)
        {
            LocalSlotToken token = _zoneSlotScratch[i];
            int slot = token.Slot;
            ZoneEntry entry = Zones[slot];
            if (entry.Sequence <= 0 || entry.Sequence != token.Sequence)
            {
                continue;
            }

            // A due tick may have been delayed by downstream backpressure. Do not discard
            // scheduled ticks merely because wall-clock time reached the zone expiry; drain every
            // tick whose original schedule is still inside [AppliedTick, ExpireTick).
            if (!HasScheduledZoneTickBeforeExpiry(entry.NextTick, entry.ExpireTick))
            {
                ClearZoneSlot(slot, entry, "expired");
                continue;
            }

            if (entry.NextTick > now)
            {
                continue;
            }

            ZoneEntry previous = entry;
            ZoneTickApplyResult applyResult = ApplyZoneTick(ref entry);
            if (applyResult == ZoneTickApplyResult.TransientUnavailable)
            {
                if (Zones[slot].Sequence == entry.Sequence)
                {
                    Zones.Set(slot, entry);
                    NoteZoneSlotUpdated(previous, entry);
                }

                // Discovery and asset rebinding can be transient (especially during migration).
                // Never turn an inability to prove terminal invalidation into destructive cleanup.
                // Keep the replicated tick, token, markers and pending count unchanged for retry.
                blocked = true;
                break;
            }


            if (applyResult == ZoneTickApplyResult.TerminalFailure)
            {
                ClearZoneSlot(slot, entry, "terminal_zone_debt_failure");
                blocked = false;
                break;
            }

            if (applyResult == ZoneTickApplyResult.Backpressured)
            {
                if (Zones[slot].Sequence == entry.Sequence)
                {
                    Zones.Set(slot, entry);
                    NoteZoneSlotUpdated(previous, entry);
                }

                // Keep NextTick and the per-target cursor unchanged. The replicated zone entry is
                // the durable debt record and resumes after the last committed NetworkId. Stop at
                // the earliest blocked pulse so later zones cannot starve it.
                blocked = true;
                break;
            }
            ZoneEntry current = Zones[slot];
            if (current.Sequence != entry.Sequence)
            {
                continue;
            }

            // Advance from the intended due tick rather than from 'now'. If capacity pressure
            // delayed this tick, subsequent missed ticks remain due and are drained deterministically
            // one per authority tick instead of being silently skipped.
            entry.NextTick = AdvanceScheduledZoneTick(entry.NextTick, entry.TickIntervalTicks);
            Zones.Set(slot, entry);
            NoteZoneSlotUpdated(previous, entry);
        }

        RecalculateNextZoneWorkTick();
        ZonePulseBackpressureActive = blocked || HasOverdueZonePulse(now);
    }

    private void SortZoneTokensByDueTick(int tokenCount)
    {
        for (int i = 1; i < tokenCount; i++)
        {
            LocalSlotToken candidate = _zoneSlotScratch[i];
            ZoneEntry candidateEntry = Zones[candidate.Slot];
            int insertAt = i;
            while (insertAt > 0)
            {
                LocalSlotToken previousToken = _zoneSlotScratch[insertAt - 1];
                ZoneEntry previousEntry = Zones[previousToken.Slot];
                if (!CombatSchedulerCapacityConfig.IsEarlier(
                        candidateEntry.NextTick,
                        candidate.Sequence,
                        previousEntry.NextTick,
                        previousToken.Sequence))
                {
                    break;
                }

                _zoneSlotScratch[insertAt] = previousToken;
                insertAt--;
            }

            _zoneSlotScratch[insertAt] = candidate;
        }
    }

    private bool HasOverdueZonePulse(int now)
    {
        for (int i = 0; i < _zoneSlotIndex.ActiveCount; i++)
        {
            ZoneEntry entry = Zones[_zoneSlotIndex.GetActiveSlot(i)];
            if (entry.Sequence > 0 &&
                HasScheduledZoneTickBeforeExpiry(entry.NextTick, entry.ExpireTick) &&
                entry.NextTick < now)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasScheduledZoneTickBeforeExpiry(int nextTick, int expireTick)
    {
        return nextTick < expireTick;
    }

    private static bool HasDueZoneDebtAtPhaseTransition(int nextTick, int expireTick, int nowTick)
    {
        return HasScheduledZoneTickBeforeExpiry(nextTick, expireTick) && nextTick <= nowTick;
    }

    private static int CountDueZoneTicksAtPhaseTransition(
        int nextTick,
        int expireTick,
        int intervalTicks,
        int nowTick)
    {
        if (!HasDueZoneDebtAtPhaseTransition(nextTick, expireTick, nowTick))
        {
            return 0;
        }

        long lastDueTick = System.Math.Min((long)nowTick, (long)expireTick - 1L);
        long count = 1L + (lastDueTick - nextTick) / Mathf.Max(1, intervalTicks);
        return count >= int.MaxValue ? int.MaxValue : (int)count;
    }

    private static int AdvanceScheduledZoneTick(int intendedTick, int intervalTicks)
    {
        return intendedTick + Mathf.Max(1, intervalTicks);
    }

    private void CloseScheduledZoneForPhaseTransition(
        int slot,
        ZoneEntry entry,
        int now,
        string reason)
    {
        int dueTicks = CountDueZoneTicksAtPhaseTransition(
            entry.NextTick,
            entry.ExpireTick,
            entry.TickIntervalTicks,
            now);
        if (dueTicks > 0)
        {
            // This is a defensive invariant breach. GameManagers must hold the old battle state
            // until every already-due pulse drains. Never hide an accidental bypass as a normal
            // phase cancellation.
            RecordZoneDueDebtPhaseCancellation(dueTicks);
            RecordNetworkBudgetDrop(NetworkBudgetDropKind.Zone);
            Debug.LogError(
                $"[CombatScheduler.Zones] Phase transition bypassed unresolved due zone debt. " +
                $"sequence={entry.Sequence},lostTicks={dueTicks},next={entry.NextTick}," +
                $"expire={entry.ExpireTick},now={now},reason={reason}");
        }

        // Future pulses are battle-scoped and are intentionally cancelled at the boundary.
        ClearZoneSlot(slot, Zones[slot], reason);
    }

    private void RebuildZonePayloadsFromNetworkEntries()
    {
        if (!IsZoneSchedulerActive)
        {
            return;
        }

        var activeSequences = new HashSet<int>();
        for (int i = 0; i < MaxActiveZones; i++)
        {
            ZoneEntry entry = Zones[i];
            if (entry.Sequence <= 0)
            {
                continue;
            }

            activeSequences.Add(entry.Sequence);
            if (_zonePayloads.ContainsKey(entry.Sequence))
            {
                continue;
            }

            TryResolveZonePayload(entry, out _);
        }

        var stale = new List<int>();
        foreach (int sequence in _zonePayloads.Keys)
        {
            if (!activeSequences.Contains(sequence))
            {
                stale.Add(sequence);
            }
        }

        for (int i = 0; i < stale.Count; i++)
        {
            DestroyZoneController(stale[i]);
            _zonePayloads.Remove(stale[i]);
        }
    }

    private ZoneTickApplyResult ApplyZoneTick(ref ZoneEntry entry)
    {
        if (!TryResolveZonePayload(entry, out ZonePayload payload))
        {
            entry.PayloadResolveRetryCount++;
            if (entry.PayloadResolveRetryCount >= MaxTransientZonePayloadResolveRetries)
            {
                return FailZoneTickTerminal(
                    entry,
                    ZoneDebtTerminalFailureKind.PayloadReferenceCorrupt,
                    $"payload rebind did not recover after {entry.PayloadResolveRetryCount} retries");
            }
            return ZoneTickApplyResult.TransientUnavailable;
        }

        entry.PayloadResolveRetryCount = 0;

        if (payload == null || payload.Effect == null || payload.TargetingStrategy == null)
        {
            return FailZoneTickTerminal(
                entry,
                ZoneDebtTerminalFailureKind.PayloadReferenceCorrupt,
                "resolved payload has missing effect or targeting reference");
        }

        bool batchInProgress = (entry.Flags & ZoneFlagTargetBatchInProgress) != 0;
        if (payload.Effect.effectsPerTick == null || payload.Effect.effectsPerTick.Count == 0)
        {
            if (batchInProgress)
            {
                Debug.LogError(
                    $"[CombatScheduler.Zones] Materialized pulse payload lost its effect list. " +
                    $"zoneSeq={entry.Sequence},pulse={entry.ActivePulseToken},remaining={entry.PendingTargetCount}");
                return FailZoneTickTerminal(
                    entry,
                    ZoneDebtTerminalFailureKind.EffectListLost,
                    "materialized pulse lost effectsPerTick");
            }

            ResetZoneTargetBatch(ref entry);
            return ZoneTickApplyResult.Applied;
        }

        GameObject caster = ResolveZoneCaster(entry, payload);
        if (caster == null)
        {
            // Once membership is materialized, target selection no longer needs the original
            // caster. Keep an already-created zone controller as the stable effect source so a
            // caster death cannot erase a due pulse.
            caster = payload.Controller != null
                ? payload.Controller.gameObject
                : null;
            if (caster == null)
            {
                Debug.LogError(
                    $"[CombatScheduler.Zones] Zone caster/presentation source unavailable. " +
                    $"zoneSeq={entry.Sequence},pulse={entry.ActivePulseToken},remaining={entry.PendingTargetCount}");
                return FailZoneTickTerminal(
                    entry,
                    ZoneDebtTerminalFailureKind.PresentationSourceLost,
                    "zone caster and controller are both unavailable");
            }
        }

        if (!batchInProgress)
        {
            Vector3 position = UnpackVector(entry.PositionX, entry.PositionY, entry.PositionZ);
            float initialRange = UnpackFloat(entry.Range);
            List<GameObject> initialTargets = payload.TargetingStrategy.FindTargets(caster, position, initialRange);
            CollectInitialZoneTickTargets(initialTargets);
            if (_zoneTickTargetScratch.Count == 0)
            {
                ResetZoneTargetBatch(ref entry);
                return ZoneTickApplyResult.Applied;
            }

            int pulseToken = AllocateZonePulseDebtToken();
            int markedCount = 0;
            for (int i = 0; i < _zoneTickTargetScratch.Count; i++)
            {
                if (TryMarkZonePulseTarget(_zoneTickTargetScratch[i], pulseToken))
                {
                    markedCount++;
                }
            }

            if (markedCount != _zoneTickTargetScratch.Count)
            {
                for (int i = 0; i < _zoneTickTargetScratch.Count; i++)
                {
                    ClearZonePulseTarget(_zoneTickTargetScratch[i], pulseToken);
                }

                Debug.LogError(
                    $"[CombatScheduler.Zones] Failed to atomically materialize pulse targets. " +
                    $"zoneSeq={entry.Sequence},pulse={pulseToken},targets={_zoneTickTargetScratch.Count},marked={markedCount}");
                return FailZoneTickTerminal(
                    entry,
                    ZoneDebtTerminalFailureKind.TargetMarkerConflict,
                    $"atomic marker admission failed targets={_zoneTickTargetScratch.Count},marked={markedCount}");
            }

            entry.ActivePulseToken = pulseToken;
            entry.PendingTargetCount = markedCount;
            entry.PackedMeta = PackZoneMeta(entry.SourceKind, entry.Flags | ZoneFlagTargetBatchInProgress);
        }

        CollectMaterializedZoneTickTargets(entry.ActivePulseToken);
        if (!IsMaterializedZoneTargetSetComplete(
                entry.PendingTargetCount,
                _zoneTickTargetScratch.Count))
        {
            // Never interpret a discovery gap as death. Host migration/rebinding can temporarily
            // hide otherwise valid NetworkObjects. Only the explicit StateAuthority lifecycle
            // callback is allowed to decrement PendingTargetCount.
            return ZoneTickApplyResult.TransientUnavailable;
        }

        if (entry.PendingTargetCount <= 0)
        {
            ResetZoneTargetBatch(ref entry);
            return ZoneTickApplyResult.Applied;
        }

        float range = UnpackFloat(entry.Range);
        MonoBehaviour effectRunner = payload.Runner != null ? payload.Runner : this;
        for (int targetIndex = 0; targetIndex < _zoneTickTargetScratch.Count; targetIndex++)
        {
            NetworkObject targetObject = _zoneTickTargetScratch[targetIndex];
            if (targetObject == null || !targetObject.IsValid || targetObject.Runner != Runner)
            {
                ClearZonePulseTarget(targetObject, entry.ActivePulseToken);
                entry.PendingTargetCount = Mathf.Max(0, entry.PendingTargetCount - 1);
                RecordZoneDebtTerminalTargetInvalidations(1);
                continue;
            }

            _zoneSingleTargetScratch.Clear();
            _zoneSingleTargetScratch.Add(targetObject.gameObject);
            int nextEffectIndex = GetZonePulseNextEffectIndex(targetObject, entry.ActivePulseToken);
            if (nextEffectIndex < 0 || nextEffectIndex > payload.Effect.effectsPerTick.Count)
            {
                return FailZoneTickTerminal(
                    entry,
                    ZoneDebtTerminalFailureKind.EffectCursorCorrupt,
                    $"target={targetObject.Id.Raw},cursor={nextEffectIndex},effects={payload.Effect.effectsPerTick.Count}");
            }

            if (!SkillEffect.CanApplyEffectsFromIndex(
                    payload.Effect.effectsPerTick,
                    nextEffectIndex,
                    1,
                    effectRunner,
                    caster,
                    _zoneSingleTargetScratch,
                    range,
                    payload.TargetingStrategy))
            {
                return ZoneTickApplyResult.Backpressured;
            }

            for (int effectIndex = nextEffectIndex;
                 effectIndex < payload.Effect.effectsPerTick.Count;
                 effectIndex++)
            {
                SkillEffect effect = payload.Effect.effectsPerTick[effectIndex];
                if (effect != null && !effect.TryApplyEffect(
                        effectRunner,
                        caster,
                        _zoneSingleTargetScratch,
                        range,
                        payload.TargetingStrategy))
                {
                    Debug.LogError(
                        $"[CombatScheduler.Zones] Capacity preflight drifted before per-target apply. " +
                        $"zoneSeq={entry.Sequence},target={targetObject.Id.Raw},effect={effect.name}");
                    // TryApplyEffect(false) is required to be non-committing. Effects already
                    // committed for this target have their replicated cursor advanced below, so
                    // even this terminal invariant failure cannot replay the applied prefix.
                    return FailZoneTickTerminal(
                        entry,
                        ZoneDebtTerminalFailureKind.EffectCommitRejectedAfterPreflight,
                        $"target={targetObject.Id.Raw},effectIndex={effectIndex},effect={effect.name}");
                }

                if (!TryAdvanceZonePulseEffect(
                        targetObject,
                        entry.ActivePulseToken,
                        effectIndex + 1))
                {
                    return FailZoneTickTerminal(
                        entry,
                        ZoneDebtTerminalFailureKind.EffectCursorCommitFailed,
                        $"target={targetObject.Id.Raw},next={effectIndex + 1}");
                }
            }

            ClearZonePulseTarget(targetObject, entry.ActivePulseToken);
            entry.PendingTargetCount = Mathf.Max(0, entry.PendingTargetCount - 1);
        }

        if (entry.PendingTargetCount > 0)
        {
            return ZoneTickApplyResult.Backpressured;
        }

        ResetZoneTargetBatch(ref entry);
        return ZoneTickApplyResult.Applied;
    }

    private void CollectInitialZoneTickTargets(List<GameObject> targets)
    {
        _zoneTickTargetScratch.Clear();
        _zoneTickTargetIdScratch.Clear();
        if (targets == null)
        {
            return;
        }

        for (int i = 0; i < targets.Count; i++)
        {
            GameObject target = targets[i];
            NetworkObject targetObject = target != null ? target.GetComponentInParent<NetworkObject>() : null;
            if (targetObject == null || !targetObject.IsValid || targetObject.Runner != Runner ||
                !CanTrackZonePulseTarget(targetObject))
            {
                continue;
            }

            uint raw = targetObject.Id.Raw;
            if (raw == 0 || !_zoneTickTargetIdScratch.Add(raw))
            {
                continue;
            }

            _zoneTickTargetScratch.Add(targetObject);
        }

        _zoneTickTargetScratch.Sort(CompareNetworkObjectIds);
    }

    private void CollectMaterializedZoneTickTargets(int pulseToken)
    {
        _zoneTickTargetScratch.Clear();
        _zoneTickTargetIdScratch.Clear();
        if (pulseToken <= 0)
        {
            return;
        }

        GameManagers gameManagers = GameManagers.Instance;
        bool usedRegistry = false;
        if (gameManagers != null)
        {
            foreach (PlayerManager player in gameManagers.AllPlayers)
            {
                FieldManager field = player != null ? player.fieldManager : null;
                if (field == null || !field.TryGetCombatTargetRegistry(out FieldCombatTargetRegistry registry))
                {
                    continue;
                }

                usedRegistry = true;
                registry.CollectPendingZonePulseTargets(
                    pulseToken,
                    Runner,
                    _zoneTickTargetScratch,
                    _zoneTickTargetIdScratch);
            }
        }

        // Editor-only component tests may not construct GameManagers/fields. Production retries
        // never use a scene-wide scan; migration validation has its own one-shot audit path.
        if (!usedRegistry && !Application.isPlaying)
        {
            CollectMaterializedZoneTickTargetsFromScene(pulseToken);
        }

        _zoneTickTargetScratch.Sort(CompareNetworkObjectIds);
    }

    internal static bool IsMaterializedZoneTargetSetComplete(int pendingTargetCount, int discoveredTargetCount)
    {
        return pendingTargetCount <= 0 || discoveredTargetCount >= pendingTargetCount;
    }

    private void CollectMaterializedZoneTickTargetsFromScene(int pulseToken)
    {
        Unit[] units = UnityEngine.Object.FindObjectsOfType<Unit>();
        for (int i = 0; i < units.Length; i++)
        {
            Unit unit = units[i];
            NetworkObject targetObject = unit != null ? unit.Object : null;
            if (targetObject != null && targetObject.IsValid && targetObject.Runner == Runner &&
                unit.PendingZonePulseDebtToken == pulseToken &&
                _zoneTickTargetIdScratch.Add(targetObject.Id.Raw))
            {
                _zoneTickTargetScratch.Add(targetObject);
            }
        }

        Monster[] monsters = UnityEngine.Object.FindObjectsOfType<Monster>();
        for (int i = 0; i < monsters.Length; i++)
        {
            Monster monster = monsters[i];
            NetworkObject targetObject = monster != null ? monster.Object : null;
            if (targetObject != null && targetObject.IsValid && targetObject.Runner == Runner &&
                monster.PendingZonePulseDebtToken == pulseToken &&
                _zoneTickTargetIdScratch.Add(targetObject.Id.Raw))
            {
                _zoneTickTargetScratch.Add(targetObject);
            }
        }
    }

    private int AllocateZonePulseDebtToken()
    {
        int next = ZonePulseDebtSequence == int.MaxValue ? 1 : ZonePulseDebtSequence + 1;
        ZonePulseDebtSequence = Mathf.Max(1, next);
        return ZonePulseDebtSequence;
    }

    private static bool TryMarkZonePulseTarget(NetworkObject targetObject, int pulseToken)
    {
        if (targetObject == null || !targetObject.IsValid || pulseToken <= 0)
        {
            return false;
        }

        Unit unit = targetObject.GetComponent<Unit>();
        if (unit != null)
        {
            return unit.TryMarkPendingZonePulseDebt(pulseToken);
        }

        Monster monster = targetObject.GetComponent<Monster>();
        return monster != null && monster.TryMarkPendingZonePulseDebt(pulseToken);
    }

    private static bool CanTrackZonePulseTarget(NetworkObject targetObject)
    {
        return targetObject != null &&
               (targetObject.GetComponent<Unit>() != null || targetObject.GetComponent<Monster>() != null);
    }

    private static int GetZonePulseNextEffectIndex(NetworkObject targetObject, int pulseToken)
    {
        if (targetObject == null || pulseToken <= 0)
        {
            return -1;
        }

        Unit unit = targetObject.GetComponent<Unit>();
        if (unit != null)
        {
            return unit.PendingZonePulseDebtToken == pulseToken
                ? unit.PendingZonePulseNextEffectIndex
                : -1;
        }

        Monster monster = targetObject.GetComponent<Monster>();
        return monster != null && monster.PendingZonePulseDebtToken == pulseToken
            ? monster.PendingZonePulseNextEffectIndex
            : -1;
    }

    private static bool TryAdvanceZonePulseEffect(
        NetworkObject targetObject,
        int pulseToken,
        int nextEffectIndex)
    {
        if (targetObject == null || pulseToken <= 0)
        {
            return false;
        }

        Unit unit = targetObject.GetComponent<Unit>();
        if (unit != null)
        {
            return unit.TryAdvancePendingZonePulseEffect(pulseToken, nextEffectIndex);
        }

        Monster monster = targetObject.GetComponent<Monster>();
        return monster != null && monster.TryAdvancePendingZonePulseEffect(pulseToken, nextEffectIndex);
    }

    private static void ClearZonePulseTarget(NetworkObject targetObject, int pulseToken)
    {
        if (targetObject == null || pulseToken <= 0)
        {
            return;
        }

        Unit unit = targetObject.GetComponent<Unit>();
        if (unit != null)
        {
            unit.ClearPendingZonePulseDebt(pulseToken);
            return;
        }

        targetObject.GetComponent<Monster>()?.ClearPendingZonePulseDebt(pulseToken);
    }

    private static int CompareNetworkObjectIds(NetworkObject left, NetworkObject right)
    {
        uint leftRaw = left != null && left.IsValid ? left.Id.Raw : 0u;
        uint rightRaw = right != null && right.IsValid ? right.Id.Raw : 0u;
        return leftRaw.CompareTo(rightRaw);
    }

    private static void ResetZoneTargetBatch(ref ZoneEntry entry)
    {
        int sourceKind = entry.SourceKind;
        int flags = entry.Flags & ~ZoneFlagTargetBatchInProgress;
        entry.ActivePulseToken = 0;
        entry.PendingTargetCount = 0;
        entry.PackedMeta = PackZoneMeta(sourceKind, flags);
    }

    private ZoneEntry FindZoneEntryByPulseToken(int pulseToken)
    {
        if (pulseToken <= 0)
        {
            return default;
        }

        for (int i = 0; i < _zoneSlotIndex.ActiveCount; i++)
        {
            ZoneEntry entry = Zones[_zoneSlotIndex.GetActiveSlot(i)];
            if (entry.ActivePulseToken == pulseToken)
            {
                return entry;
            }
        }

        return default;
    }

    private bool IsZoneEffectCursorValid(ZoneEntry entry, int nextEffectIndex)
    {
        if (entry.Sequence <= 0 || nextEffectIndex < 0 ||
            !TryResolveZonePayload(entry, out ZonePayload payload) ||
            payload?.Effect?.effectsPerTick == null)
        {
            return false;
        }

        return nextEffectIndex <= payload.Effect.effectsPerTick.Count;
    }

    private ZoneTickApplyResult FailZoneTickTerminal(
        ZoneEntry entry,
        ZoneDebtTerminalFailureKind kind,
        string detail)
    {
        if (!ZoneDebtTerminalFailureActive)
        {
            ZoneDebtTerminalFailureActive = true;
            ZoneDebtTerminalFailureSequence = entry.Sequence;
            ZoneDebtTerminalFailureKindValue = (int)kind;
            RecordZoneDebtTerminalFailure();
            RecordNetworkBudgetDrop(NetworkBudgetDropKind.Zone);
        }

        Debug.LogError(
            $"[CombatScheduler.Zones] Terminal zone debt failure; combat transition will safe-stop. " +
            $"zoneSeq={entry.Sequence},pulse={entry.ActivePulseToken},kind={kind},detail={detail}");
        return ZoneTickApplyResult.TerminalFailure;
    }

    private bool TryResolveZonePayload(ZoneEntry entry, out ZonePayload payload)
    {
        if (_zonePayloads.TryGetValue(entry.Sequence, out payload) && payload != null)
        {
            return true;
        }

        ZoneEffect effect = ResolveLoadedAssetByHash<ZoneEffect>(entry.ZoneEffectHash);
        TargetingStrategy targeting = ResolveLoadedAssetByHash<TargetingStrategy>(entry.TargetingHash);
        GameObject caster = ResolveZoneCaster(entry, null);
        if (effect == null || targeting == null || caster == null)
        {
            payload = null;
            return false;
        }

        Vector3 position = UnpackVector(entry.PositionX, entry.PositionY, entry.PositionZ);
        float range = UnpackFloat(entry.Range);
        payload = new ZonePayload
        {
            Effect = effect,
            TargetingStrategy = targeting,
            Caster = caster,
            Runner = this,
            Controller = CreateZoneController(effect, caster, this, range, targeting, entry.Sequence, position)
        };
        _zonePayloads[entry.Sequence] = payload;
        return true;
    }

    private GameObject ResolveZoneCaster(ZoneEntry entry, ZonePayload payload)
    {
        if (payload?.Caster != null)
        {
            return payload.Caster;
        }

        NetworkObject casterObject = ResolveNetworkObject(entry.CasterId);
        return casterObject != null ? casterObject.gameObject : null;
    }

    private ZoneController CreateZoneController(
        ZoneEffect effect,
        GameObject caster,
        MonoBehaviour effectRunner,
        float skillRange,
        TargetingStrategy targetingStrategy,
        int schedulerSequence,
        Vector3 position)
    {
        GameObject zoneObject = effect.zonePrefab != null
            ? UnityEngine.Object.Instantiate(effect.zonePrefab, position, Quaternion.identity)
            : new GameObject($"Zone_{effect.name}");
        zoneObject.transform.position = position;

        var controller = zoneObject.GetComponent<ZoneController>();
        if (controller == null)
        {
            controller = zoneObject.AddComponent<ZoneController>();
        }

        controller.Initialize(effect, caster, effectRunner != null ? effectRunner : this, skillRange, targetingStrategy, schedulerSequence);
        return controller;
    }

    private void ClearZoneSlot(
        int slot,
        ZoneEntry entry,
        string reason,
        bool preserveMaterializedPulseTargets = false)
    {
        if (slot < 0 || slot >= MaxActiveZones || Zones[slot].Sequence != entry.Sequence)
        {
            return;
        }

        if (entry.ActivePulseToken > 0 && !preserveMaterializedPulseTargets)
        {
            ClearMaterializedZonePulseTargets(entry.ActivePulseToken);
        }

        Zones.Set(slot, default);
        ReleaseZoneSlot(slot, entry);
        DestroyZoneController(entry.Sequence);
        _zonePayloads.Remove(entry.Sequence);
        if (_zoneSlotIndex.ActiveCount == 0)
        {
            ZonePulseBackpressureActive = false;
        }
    }

    private void ClearMaterializedZonePulseTargets(int pulseToken)
    {
        if (pulseToken <= 0)
        {
            return;
        }

        CollectMaterializedZoneTickTargets(pulseToken);
        // Clearing a terminal/cancelled materialized pulse is rare and correctness-critical.
        // Include a one-shot scene audit so a transient registry gap cannot leave orphan markers.
        CollectMaterializedZoneTickTargetsFromScene(pulseToken);
        _zoneTickTargetScratch.Sort(CompareNetworkObjectIds);
        for (int i = 0; i < _zoneTickTargetScratch.Count; i++)
        {
            ClearZonePulseTarget(_zoneTickTargetScratch[i], pulseToken);
        }
    }

    private void DestroyZoneController(int sequence)
    {
        if (!_zonePayloads.TryGetValue(sequence, out ZonePayload payload) || payload?.Controller == null)
        {
            return;
        }

        payload.Controller.DestroyScheduledZone();
    }

    private int FindEmptyZoneSlot()
    {
        if (!EnsureLocalSchedulerState())
        {
            return -1;
        }

        return _zoneSlotIndex.TryRentLowest(out int slot) ? slot : -1;
    }

    private static int PackZoneMeta(int sourceKind, int flags)
    {
        return (sourceKind & 0xF) | (flags << 4);
    }

    private static int StableObjectHash(UnityEngine.Object obj)
    {
        return obj != null ? Animator.StringToHash(obj.name) : 0;
    }

    private static T ResolveLoadedAssetByHash<T>(int hash) where T : UnityEngine.Object
    {
        if (hash == 0)
        {
            return null;
        }

        T[] loaded = Resources.FindObjectsOfTypeAll<T>();
        for (int i = 0; i < loaded.Length; i++)
        {
            T asset = loaded[i];
            if (asset != null && StableObjectHash(asset) == hash)
            {
                return asset;
            }
        }

        return null;
    }
}
