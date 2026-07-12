using System;
using System.Collections.Generic;
using System.Linq;
using Fusion;
using UnityEngine;

public partial class PlayerManager
{
    private const int AUGMENT_RUNTIME_MIGRATION_CAPACITY = 64;
    private const int WALL_HEALTH_MIGRATION_CAPACITY = 96;
    private const int AUGMENT_RUNTIME_ACTIVE_SUMMON = 1;
    private const int AUGMENT_RUNTIME_OWNED_BOSS = 2;

    private struct AugmentRuntimeMigrationRow : INetworkStruct
    {
        public int AugmentId;
        public int State;
        public int Count;
    }

    private struct WallHealthMigrationRow : INetworkStruct
    {
        public int PackedCell;
        public int PackedHealthAndRevision;
    }

    [Networked] private NetworkString<_64> DurableConnectionTokenHashHex { get; set; }
    [Networked, Capacity(AUGMENT_RUNTIME_MIGRATION_CAPACITY)] private NetworkArray<AugmentRuntimeMigrationRow> AugmentRuntimeMigrationRows { get; }
    [Networked] private int AugmentRuntimeMigrationCount { get; set; }
    [Networked] private int AugmentRuntimeMigrationRevision { get; set; }
    [Networked] private NetworkBool AugmentRuntimeMigrationOverflow { get; set; }
    [Networked, Capacity(WALL_HEALTH_MIGRATION_CAPACITY)] private NetworkArray<WallHealthMigrationRow> WallHealthMigrationRows { get; }
    [Networked] private int WallHealthMigrationCount { get; set; }
    [Networked] private int WallHealthMigrationRevision { get; set; }
    [Networked] private NetworkBool WallHealthMigrationOverflow { get; set; }
    private string _pendingDurableConnectionTokenHash = string.Empty;

    public string GetDurableConnectionTokenHash()
    {
        if (Object != null && Object.IsValid)
        {
            return DurableConnectionTokenHashHex.ToString();
        }

        return _pendingDurableConnectionTokenHash ?? string.Empty;
    }

    public bool TrySetDurableConnectionTokenHashFromAuthority(string tokenHash)
    {
        tokenHash = tokenHash?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!IsValidDurableConnectionTokenHash(tokenHash))
        {
            return false;
        }

        _pendingDurableConnectionTokenHash = tokenHash;
        if (Object == null || !Object.IsValid)
        {
            return true;
        }

        if (!Object.HasStateAuthority)
        {
            return false;
        }

        DurableConnectionTokenHashHex = tokenHash;
        return true;
    }

    private void ApplyPendingDurableConnectionTokenHashOnSpawn()
    {
        if (Object != null
            && Object.IsValid
            && Object.HasStateAuthority
            && IsValidDurableConnectionTokenHash(_pendingDurableConnectionTokenHash))
        {
            DurableConnectionTokenHashHex = _pendingDurableConnectionTokenHash;
        }
    }

    public static bool IsValidDurableConnectionTokenHash(string tokenHash)
    {
        if (string.IsNullOrWhiteSpace(tokenHash) || tokenHash.Length != 64)
        {
            return false;
        }

        for (int i = 0; i < tokenHash.Length; i++)
        {
            char value = tokenHash[i];
            bool isHex = (value >= '0' && value <= '9')
                || (value >= 'a' && value <= 'f')
                || (value >= 'A' && value <= 'F');
            if (!isHex)
            {
                return false;
            }
        }

        return true;
    }

    public string[] GetChosenAugmentMigrationNames()
    {
        if (SelectedAugmentSnapshotOverflow)
        {
            return new[] { "__selected_augment_snapshot_overflow" };
        }
        if (Object != null && Object.IsValid && SelectedAugmentSnapshotCount > 0)
        {
            return GetSelectedAugmentSnapshotNames();
        }

        return BuildAugmentMigrationNames(chosenAugments);
    }

    public string[] GetActiveMonsterSummonAugmentMigrationNames() =>
        GetRuntimeAugmentMigrationNames(AUGMENT_RUNTIME_ACTIVE_SUMMON, _activeMonsterSummonAugments);

    public string[] GetOwnedBossAugmentMigrationNames() =>
        GetRuntimeAugmentMigrationNames(AUGMENT_RUNTIME_OWNED_BOSS, _ownedBossAugments);

    public bool PublishAugmentRuntimeMigrationStateFromAuthority(string context)
    {
        if (Object == null || !Object.IsValid || !Object.HasStateAuthority)
        {
            return false;
        }

        var rows = new List<AugmentRuntimeMigrationRow>();
        AppendRuntimeAugmentRows(rows, _activeMonsterSummonAugments, AUGMENT_RUNTIME_ACTIVE_SUMMON);
        AppendRuntimeAugmentRows(rows, _ownedBossAugments, AUGMENT_RUNTIME_OWNED_BOSS);
        if (rows.Count > AUGMENT_RUNTIME_MIGRATION_CAPACITY)
        {
            AugmentRuntimeMigrationOverflow = true;
            Debug.LogError($"[PlayerManager] Durable augment runtime snapshot overflow ({context}) P{playerId}: {rows.Count}/{AUGMENT_RUNTIME_MIGRATION_CAPACITY}");
            return false;
        }

        for (int i = 0; i < AUGMENT_RUNTIME_MIGRATION_CAPACITY; i++)
        {
            AugmentRuntimeMigrationRows.Set(i, i < rows.Count ? rows[i] : default);
        }

        AugmentRuntimeMigrationCount = rows.Count;
        AugmentRuntimeMigrationRevision = Math.Max(1, AugmentRuntimeMigrationRevision + 1);
        AugmentRuntimeMigrationOverflow = false;
        return true;
    }

    public bool PublishDestructibleWallHealthMigrationStateFromAuthority(string context)
    {
        if (Object == null || !Object.IsValid || !Object.HasStateAuthority || fieldManager == null)
        {
            return false;
        }

        fieldManager.CaptureLocalDestructibleWallMigrationSnapshot(
            out int[] flatPositions,
            out float[] currentHealth,
            out float[] maxHealth,
            out int[] revisions);
        int count = currentHealth?.Length ?? 0;
        if (count > WALL_HEALTH_MIGRATION_CAPACITY)
        {
            WallHealthMigrationOverflow = true;
            Debug.LogError($"[PlayerManager] Durable wall HP snapshot overflow ({context}) P{playerId}: {count}/{WALL_HEALTH_MIGRATION_CAPACITY}");
            return false;
        }

        for (int i = 0; i < WALL_HEALTH_MIGRATION_CAPACITY; i++)
        {
            WallHealthMigrationRow row = default;
            if (i < count)
            {
                int x = flatPositions[i * 2];
                int y = flatPositions[i * 2 + 1];
                float denominator = maxHealth[i];
                float normalized = denominator > 0f ? Mathf.Clamp01(currentHealth[i] / denominator) : 0f;
                int quantizedHealth = Mathf.Clamp(Mathf.RoundToInt(normalized * ushort.MaxValue), 0, ushort.MaxValue);
                int revision = Mathf.Clamp(revisions[i], 0, ushort.MaxValue);
                row.PackedCell = (x & 0xFFFF) | (y << 16);
                row.PackedHealthAndRevision = quantizedHealth | (revision << 16);
            }
            WallHealthMigrationRows.Set(i, row);
        }

        WallHealthMigrationCount = count;
        WallHealthMigrationRevision = Math.Max(1, WallHealthMigrationRevision + 1);
        WallHealthMigrationOverflow = false;
        return true;
    }

    public bool HasDurableMigrationPayloadOverflow(out string reason)
    {
        var reasons = new List<string>();
        if (SelectedAugmentSnapshotOverflow) reasons.Add("selected_augments");
        if (AugmentRuntimeMigrationOverflow) reasons.Add("runtime_augments");
        if (WallHealthMigrationOverflow) reasons.Add("wall_health");
        reason = string.Join(",", reasons);
        return reasons.Count > 0;
    }

    public bool PublishDestructibleWallHealthDeltaFromAuthority(
        Vector3Int position,
        float currentHealth,
        float maxHealth,
        int revision,
        string context)
    {
        if (Object == null || !Object.IsValid || !Object.HasStateAuthority ||
            WallHealthMigrationRevision <= 0 || maxHealth <= 0f)
        {
            return PublishDestructibleWallHealthMigrationStateFromAuthority($"delta_fallback:{context}");
        }

        int packedCell = (position.x & 0xFFFF) | (position.y << 16);
        int count = Mathf.Clamp(WallHealthMigrationCount, 0, WALL_HEALTH_MIGRATION_CAPACITY);
        for (int i = 0; i < count; i++)
        {
            WallHealthMigrationRow row = WallHealthMigrationRows.Get(i);
            if (row.PackedCell != packedCell)
            {
                continue;
            }

            float normalized = Mathf.Clamp01(currentHealth / maxHealth);
            int quantizedHealth = Mathf.Clamp(Mathf.RoundToInt(normalized * ushort.MaxValue), 0, ushort.MaxValue);
            int packedRevision = Mathf.Clamp(revision, 0, ushort.MaxValue);
            row.PackedHealthAndRevision = quantizedHealth | (packedRevision << 16);
            WallHealthMigrationRows.Set(i, row);
            WallHealthMigrationRevision = Math.Max(1, WallHealthMigrationRevision + 1);
            return true;
        }

        return PublishDestructibleWallHealthMigrationStateFromAuthority($"delta_missing_cell:{context}");
    }

    public bool TryGetDurableDestructibleWallMigrationSnapshot(
        out int[] flatPositions,
        out float[] currentHealth,
        out float[] maxHealth,
        out int[] revisions)
    {
        flatPositions = Array.Empty<int>();
        currentHealth = Array.Empty<float>();
        maxHealth = Array.Empty<float>();
        revisions = Array.Empty<int>();
        if (Object == null || !Object.IsValid || WallHealthMigrationRevision <= 0)
        {
            return false;
        }

        int count = Mathf.Clamp(WallHealthMigrationCount, 0, WALL_HEALTH_MIGRATION_CAPACITY);
        flatPositions = new int[count * 2];
        currentHealth = new float[count];
        maxHealth = new float[count];
        revisions = new int[count];
        for (int i = 0; i < count; i++)
        {
            WallHealthMigrationRow row = WallHealthMigrationRows.Get(i);
            int x = (short)(row.PackedCell & 0xFFFF);
            int y = (short)((row.PackedCell >> 16) & 0xFFFF);
            int quantizedHealth = row.PackedHealthAndRevision & 0xFFFF;
            int revision = (row.PackedHealthAndRevision >> 16) & 0xFFFF;
            DestructibleWall wall = fieldManager != null ? fieldManager.GetWallAt(new Vector3Int(x, y, 0)) : null;
            float resolvedMaxHealth = wall != null ? wall.MaxHealth : 1f;
            flatPositions[i * 2] = x;
            flatPositions[i * 2 + 1] = y;
            maxHealth[i] = resolvedMaxHealth;
            currentHealth[i] = resolvedMaxHealth * (quantizedHealth / (float)ushort.MaxValue);
            revisions[i] = revision;
        }

        return true;
    }

    public bool RestoreAugmentGameplayStateAfterHostMigration(
        string[] chosenNames,
        string[] activeMonsterSummonNames,
        string[] ownedBossNames,
        string context,
        out string failureReason)
    {
        failureReason = string.Empty;
        if (Object == null || !Object.IsValid || !Object.HasStateAuthority)
        {
            failureReason = "state_authority_required";
            return false;
        }

        if (!TryResolveAugmentMigrationList(chosenNames, out List<AugmentData> restoredChosen, out failureReason)
            || !TryResolveAugmentMigrationList(activeMonsterSummonNames, out List<AugmentData> restoredActive, out failureReason)
            || !TryResolveAugmentMigrationList(ownedBossNames, out List<AugmentData> restoredBosses, out failureReason))
        {
            Debug.LogError($"[PlayerManager] HostMigration augment gameplay restore failed ({context}) P{playerId}: {failureReason}");
            return false;
        }

        // Do not replay one-shot effects (gold, walls, scroll grants). Exact durable gameplay
        // collections are replaced from the authority snapshot instead.
        chosenAugments = restoredChosen;
        _activeMonsterSummonAugments = restoredActive;
        _ownedBossAugments = restoredBosses;
        if (!PublishAugmentRuntimeMigrationStateFromAuthority($"restore:{context}"))
        {
            failureReason = "runtime_snapshot_publish_failed";
            return false;
        }
        Debug.Log($"[PlayerManager] HostMigration augment gameplay restore complete ({context}) P{playerId} chosen={chosenAugments.Count} activeSummon={_activeMonsterSummonAugments.Count} ownedBoss={_ownedBossAugments.Count}");
        return true;
    }

    private string[] GetRuntimeAugmentMigrationNames(int state, IEnumerable<AugmentData> fallback)
    {
        if (Object == null || !Object.IsValid || AugmentRuntimeMigrationRevision <= 0)
        {
            return BuildAugmentMigrationNames(fallback);
        }

        if (AugmentRuntimeMigrationOverflow)
        {
            return new[] { "__runtime_augment_snapshot_overflow" };
        }

        int count = Mathf.Clamp(AugmentRuntimeMigrationCount, 0, AUGMENT_RUNTIME_MIGRATION_CAPACITY);
        var names = new List<string>();
        for (int i = 0; i < count; i++)
        {
            AugmentRuntimeMigrationRow row = AugmentRuntimeMigrationRows.Get(i);
            if (row.State != state)
            {
                continue;
            }

            string name = ResolveLoadedAugmentNameByStableId(row.AugmentId);
            if (string.IsNullOrWhiteSpace(name))
            {
                Debug.LogError($"[PlayerManager] Durable augment snapshot id could not be resolved. P{playerId}, state={state}, id={row.AugmentId}");
                names.Add($"__unresolved_augment_id_{row.AugmentId}");
                continue;
            }
            int repetitions = Mathf.Max(1, row.Count);
            for (int repetition = 0; repetition < repetitions; repetition++)
            {
                names.Add(name);
            }
        }

        return names.ToArray();
    }

    private static void AppendRuntimeAugmentRows(
        List<AugmentRuntimeMigrationRow> rows,
        IEnumerable<AugmentData> augments,
        int state)
    {
        foreach (AugmentData augment in augments ?? Enumerable.Empty<AugmentData>())
        {
            if (augment == null)
            {
                continue;
            }

            string name = !string.IsNullOrWhiteSpace(augment.augmentName) ? augment.augmentName.Trim() : augment.name;
            int augmentId = StableAugmentSnapshotId(name);
            if (augmentId != 0)
            {
                int existingIndex = rows.FindIndex(row => row.AugmentId == augmentId && row.State == state);
                if (existingIndex >= 0)
                {
                    AugmentRuntimeMigrationRow existing = rows[existingIndex];
                    existing.Count = Mathf.Max(1, existing.Count) + 1;
                    rows[existingIndex] = existing;
                }
                else
                {
                    rows.Add(new AugmentRuntimeMigrationRow { AugmentId = augmentId, State = state, Count = 1 });
                }
            }
        }
    }

    private bool TryResolveAugmentMigrationList(
        IEnumerable<string> names,
        out List<AugmentData> resolved,
        out string failureReason)
    {
        resolved = new List<AugmentData>();
        failureReason = string.Empty;
        foreach (string rawName in names ?? Array.Empty<string>())
        {
            string name = rawName?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            AugmentData augment = augmentManager != null ? augmentManager.FindAugmentByName(name) : null;
            if (augment == null)
            {
                augment = Resources.FindObjectsOfTypeAll<AugmentData>()
                    .FirstOrDefault(candidate => candidate != null
                        && (string.Equals(candidate.augmentName, name, StringComparison.Ordinal)
                            || string.Equals(candidate.name, name, StringComparison.Ordinal)));
            }

            if (augment == null)
            {
                failureReason = $"augment_not_loaded:{name}";
                return false;
            }

            resolved.Add(augment);
        }

        return true;
    }

    private static string[] BuildAugmentMigrationNames(IEnumerable<AugmentData> augments)
    {
        return (augments ?? Enumerable.Empty<AugmentData>())
            .Where(augment => augment != null)
            .Select(augment => !string.IsNullOrWhiteSpace(augment.augmentName)
                ? augment.augmentName.Trim()
                : augment.name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToArray();
    }
}
