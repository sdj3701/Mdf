using System.Collections.Generic;
using Fusion;
using UnityEngine;

public partial class CombatScheduler
{
    private const int MaxActiveZones = 16;

    [Networked] public int ZoneSequence { get; private set; }
    [Networked, Capacity(MaxActiveZones)] private NetworkArray<ZoneEntry> Zones { get; }

    private readonly Dictionary<int, ZonePayload> _zonePayloads = new Dictionary<int, ZonePayload>();

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

        int slot = FindEmptyZoneSlot();
        if (slot < 0)
        {
            RecordNetworkBudgetDrop(NetworkBudgetDropKind.Zone);
            Debug.LogWarning($"[CombatScheduler.Zones] Active zone capacity exceeded. capacity={MaxActiveZones}, effect={effect.name}");
            return false;
        }

        int now = Runner.Tick;
        int durationTicks = SecondsToTicksCeil(effect.zoneDuration);
        int tickIntervalTicks = Mathf.Max(1, SecondsToTicksCeil(effect.tickInterval));
        int nextSeq = ZoneSequence + 1;
        ZoneSequence = nextSeq;
        sequence = nextSeq;

        ResolveStatusSource(caster, nextSeq, out NetworkId casterId, out int sourceKind, out int sourceKey);
        Vector3 position = caster.transform.position;
        var entry = new ZoneEntry
        {
            Sequence = nextSeq,
            CasterId = casterId,
            SourceKey = sourceKey,
            ZoneEffectHash = StableObjectHash(effect),
            TargetingHash = StableObjectHash(targetingStrategy),
            PositionX = PackFloat(position.x),
            PositionY = PackFloat(position.y),
            PositionZ = PackFloat(position.z),
            Range = PackFloat(skillRange),
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
                $"zoneSeq={entry.Sequence};effect={entry.ZoneEffectHash};targeting={entry.TargetingHash};sourceKind={entry.SourceKind};sourceKey={entry.SourceKey};caster={entry.CasterId.Raw};range={entry.Range};tick={entry.TickIntervalTicks};applied={entry.AppliedTick};expire={entry.ExpireTick};next={entry.NextTick};x={Mathf.RoundToInt(pos.x * 10f)};z={Mathf.RoundToInt(pos.z * 10f)}";
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
                Flags = entry.Flags
            });
        }

        return snapshots.Count;
    }

    public int RestoreZonesFromMigration(IReadOnlyList<ZoneMigrationSnapshot> snapshots, string reason = null)
    {
        if (!IsZoneSchedulerActive || !Object.HasStateAuthority || snapshots == null || snapshots.Count == 0 ||
            !EnsureLocalSchedulerState())
        {
            return 0;
        }

        ClearAllScheduledZones(reason);

        int now = Runner.Tick;
        int restored = 0;
        int maxSequence = ZoneSequence;
        for (int i = 0; i < snapshots.Count; i++)
        {
            ZoneMigrationSnapshot snapshot = snapshots[i];
            if (snapshot.Sequence <= 0 || snapshot.ExpireTick <= now)
            {
                continue;
            }

            int slot = FindEmptyZoneSlot();
            if (slot < 0)
            {
                Debug.LogWarning($"[CombatScheduler.Zones] Migration restore capacity exceeded. capacity={MaxActiveZones}, requested={snapshots.Count}, reason={reason}");
                break;
            }

            var entry = new ZoneEntry
            {
                Sequence = snapshot.Sequence,
                CasterId = snapshot.CasterId,
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
                PackedMeta = PackZoneMeta(snapshot.SourceKind, snapshot.Flags)
            };
            Zones.Set(slot, entry);
            CommitZoneSlot(slot, entry);
            maxSequence = Mathf.Max(maxSequence, snapshot.Sequence);
            restored++;
        }

        ZoneSequence = maxSequence;
        RebuildZonePayloadsFromNetworkEntries();
        Debug.Log($"[CombatScheduler.Zones] Migration restore complete. restored={restored}, cached={snapshots.Count}, reason={reason}");
        return restored;
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
            return;
        }

        int tokenCount = CaptureZoneSlotTokens();
        for (int i = 0; i < tokenCount; i++)
        {
            LocalSlotToken token = _zoneSlotScratch[i];
            int slot = token.Slot;
            ZoneEntry entry = Zones[slot];
            if (entry.Sequence <= 0 || entry.Sequence != token.Sequence)
            {
                continue;
            }

            if (entry.ExpireTick <= now)
            {
                ClearZoneSlot(slot, entry, "expired");
                continue;
            }

            if (entry.NextTick > now)
            {
                continue;
            }

            ApplyZoneTick(entry);
            ZoneEntry current = Zones[slot];
            if (current.Sequence != entry.Sequence)
            {
                continue;
            }

            ZoneEntry previous = entry;
            entry.NextTick = now + Mathf.Max(1, entry.TickIntervalTicks);
            Zones.Set(slot, entry);
            NoteZoneSlotUpdated(previous, entry);
        }

        RecalculateNextZoneWorkTick();
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

    private void ApplyZoneTick(ZoneEntry entry)
    {
        if (!TryResolveZonePayload(entry, out ZonePayload payload) ||
            payload.Effect == null ||
            payload.TargetingStrategy == null ||
            payload.Effect.effectsPerTick == null ||
            payload.Effect.effectsPerTick.Count == 0)
        {
            return;
        }

        GameObject caster = ResolveZoneCaster(entry, payload);
        if (caster == null)
        {
            return;
        }

        Vector3 position = UnpackVector(entry.PositionX, entry.PositionY, entry.PositionZ);
        float range = UnpackFloat(entry.Range);
        List<GameObject> targets = payload.TargetingStrategy.FindTargets(caster, position, range);
        if (targets == null || targets.Count == 0)
        {
            return;
        }

        foreach (var effect in payload.Effect.effectsPerTick)
        {
            if (effect != null)
            {
                effect.ApplyEffect(payload.Runner != null ? payload.Runner : this, caster, targets, range, payload.TargetingStrategy);
            }
        }
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

    private void ClearZoneSlot(int slot, ZoneEntry entry, string reason)
    {
        if (slot < 0 || slot >= MaxActiveZones || Zones[slot].Sequence != entry.Sequence)
        {
            return;
        }

        Zones.Set(slot, default);
        ReleaseZoneSlot(slot, entry);
        DestroyZoneController(entry.Sequence);
        _zonePayloads.Remove(entry.Sequence);
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
