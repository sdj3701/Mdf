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
        public int PackedUpgradeState;
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
        return PlayerSnapshotCodec.IsSha256Hex(tokenHash);
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
            out int[] revisions,
            out int[] levels,
            out int[] upgradeInvestments);
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
                if (!PlayerSnapshotCodec.TryPackCell(x, y, out row.PackedCell)
                    || !PlayerSnapshotCodec.TryPackHealthAndRevision(
                        currentHealth[i],
                        maxHealth[i],
                        revisions[i],
                        out row.PackedHealthAndRevision)
                    || !PlayerSnapshotCodec.TryPackWallUpgradeState(
                        levels[i],
                        upgradeInvestments[i],
                        out row.PackedUpgradeState))
                {
                    WallHealthMigrationOverflow = true;
                    Debug.LogError($"[PlayerManager] Durable wall row out of range ({context}) P{playerId}: cell=({x},{y}), revision={revisions[i]}, level={levels[i]}, invested={upgradeInvestments[i]}");
                    return false;
                }
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
        if (ShopSnapshotOverflow) reasons.Add("shop");
        if (PresentedAugmentSnapshotOverflow) reasons.Add("presented_augments");
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
        int level,
        int upgradeInvestment,
        string context)
    {
        if (Object == null || !Object.IsValid || !Object.HasStateAuthority ||
            WallHealthMigrationRevision <= 0 || maxHealth <= 0f)
        {
            return PublishDestructibleWallHealthMigrationStateFromAuthority($"delta_fallback:{context}");
        }

        if (!PlayerSnapshotCodec.TryPackCell(position.x, position.y, out int packedCell)
            || !PlayerSnapshotCodec.TryPackHealthAndRevision(
                currentHealth,
                maxHealth,
                revision,
                out int packedHealthAndRevision)
            || !PlayerSnapshotCodec.TryPackWallUpgradeState(
                level,
                upgradeInvestment,
                out int packedUpgradeState))
        {
            WallHealthMigrationOverflow = true;
            Debug.LogError($"[PlayerManager] Durable wall HP delta out of range ({context}) P{playerId}: cell={position}, revision={revision}");
            return false;
        }

        int count = Mathf.Clamp(WallHealthMigrationCount, 0, WALL_HEALTH_MIGRATION_CAPACITY);
        for (int i = 0; i < count; i++)
        {
            WallHealthMigrationRow row = WallHealthMigrationRows.Get(i);
            if (row.PackedCell != packedCell)
            {
                continue;
            }

            row.PackedHealthAndRevision = packedHealthAndRevision;
            row.PackedUpgradeState = packedUpgradeState;
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
        out int[] revisions,
        out int[] levels,
        out int[] upgradeInvestments)
    {
        flatPositions = Array.Empty<int>();
        currentHealth = Array.Empty<float>();
        maxHealth = Array.Empty<float>();
        revisions = Array.Empty<int>();
        levels = Array.Empty<int>();
        upgradeInvestments = Array.Empty<int>();
        if (Object == null || !Object.IsValid || WallHealthMigrationRevision <= 0)
        {
            return false;
        }

        int count = Mathf.Clamp(WallHealthMigrationCount, 0, WALL_HEALTH_MIGRATION_CAPACITY);
        flatPositions = new int[count * 2];
        currentHealth = new float[count];
        maxHealth = new float[count];
        revisions = new int[count];
        levels = new int[count];
        upgradeInvestments = new int[count];
        for (int i = 0; i < count; i++)
        {
            WallHealthMigrationRow row = WallHealthMigrationRows.Get(i);
            PlayerSnapshotCodec.UnpackCell(row.PackedCell, out int x, out int y);
            DestructibleWall wall = fieldManager != null ? fieldManager.GetWallAt(new Vector3Int(x, y, 0)) : null;
            PlayerSnapshotCodec.UnpackWallUpgradeState(
                row.PackedUpgradeState,
                out int resolvedLevel,
                out int resolvedUpgradeInvestment);
            if (resolvedLevel <= 0)
            {
                resolvedLevel = 1;
            }
            float resolvedMaxHealth = 1f;
            if (fieldManager != null
                && fieldManager.TryResolveDestructibleWallMaxHealth(
                    resolvedLevel,
                    out float configuredMaxHealth))
            {
                resolvedMaxHealth = configuredMaxHealth;
            }
            else if (wall != null)
            {
                WallProgressionData progression = wall.ProgressionData;
                resolvedMaxHealth = progression != null && progression.TryGetLevel(resolvedLevel, out WallLevelData levelData)
                    ? levelData.MaxHealth
                    : wall.MaxHealth;
            }
            PlayerSnapshotCodec.UnpackHealthAndRevision(
                row.PackedHealthAndRevision,
                resolvedMaxHealth,
                out float resolvedCurrentHealth,
                out int revision);
            flatPositions[i * 2] = x;
            flatPositions[i * 2 + 1] = y;
            maxHealth[i] = resolvedMaxHealth;
            currentHealth[i] = resolvedCurrentHealth;
            revisions[i] = revision;
            levels[i] = resolvedLevel;
            upgradeInvestments[i] = resolvedUpgradeInvestment;
        }

        return true;
    }

    public MigrationRestoreReport RestoreAugmentGameplayStateAfterHostMigration(
        string[] chosenNames,
        string[] activeMonsterSummonNames,
        string[] ownedBossNames,
        string context)
    {
        string scope = $"augment_gameplay:P{playerId}";
        int captured = 3
            + (chosenNames?.Length ?? 0)
            + (activeMonsterSummonNames?.Length ?? 0)
            + (ownedBossNames?.Length ?? 0);
        if (Object == null || !Object.IsValid || !Object.HasStateAuthority)
        {
            return MigrationRestoreReport.FailedScope(scope, captured, "state_authority_required");
        }

        if (!TryResolveAugmentMigrationList(chosenNames, out List<AugmentData> restoredChosen, out string failureReason)
            || !TryResolveAugmentMigrationList(activeMonsterSummonNames, out List<AugmentData> restoredActive, out failureReason)
            || !TryResolveAugmentMigrationList(ownedBossNames, out List<AugmentData> restoredBosses, out failureReason))
        {
            Debug.LogError($"[PlayerManager] HostMigration augment gameplay restore failed ({context}) P{playerId}: {failureReason}");
            return MigrationRestoreReport.FailedScope(scope, captured, failureReason);
        }

        List<AugmentData> previousChosen = chosenAugments;
        List<AugmentData> previousActive = _activeMonsterSummonAugments;
        List<AugmentData> previousBosses = _ownedBossAugments;
        int previousCount = AugmentRuntimeMigrationCount;
        int previousRevision = AugmentRuntimeMigrationRevision;
        bool previousOverflow = AugmentRuntimeMigrationOverflow;
        var previousRows = new AugmentRuntimeMigrationRow[AUGMENT_RUNTIME_MIGRATION_CAPACITY];
        for (int i = 0; i < AUGMENT_RUNTIME_MIGRATION_CAPACITY; i++)
        {
            previousRows[i] = AugmentRuntimeMigrationRows.Get(i);
        }

        // Do not replay one-shot effects (gold, walls, scroll grants). Exact durable gameplay
        // collections are replaced from the authority snapshot instead.
        chosenAugments = restoredChosen;
        _activeMonsterSummonAugments = restoredActive;
        _ownedBossAugments = restoredBosses;
        if (!PublishAugmentRuntimeMigrationStateFromAuthority($"restore:{context}"))
        {
            RollBackAugmentGameplayMigrationState(
                previousChosen,
                previousActive,
                previousBosses,
                previousRows,
                previousCount,
                previousRevision,
                previousOverflow);
            return MigrationRestoreReport.FailedScope(scope, captured, "runtime_snapshot_publish_failed");
        }

        string[] expectedChosen = BuildAugmentMigrationNames(restoredChosen);
        string[] expectedActive = BuildAugmentMigrationNames(restoredActive);
        string[] expectedBosses = BuildAugmentMigrationNames(restoredBosses);
        bool exact = TryGetSelectedAugmentSnapshot(
                         out string[] selectedSnapshot,
                         out string selectedSnapshotFailure)
                     && selectedSnapshot.SequenceEqual(expectedChosen, StringComparer.Ordinal)
                     && BuildAugmentMigrationNames(chosenAugments).SequenceEqual(expectedChosen, StringComparer.Ordinal)
                     && GetRuntimeAugmentMigrationNames(AUGMENT_RUNTIME_ACTIVE_SUMMON, Array.Empty<AugmentData>())
                         .SequenceEqual(expectedActive, StringComparer.Ordinal)
                     && GetRuntimeAugmentMigrationNames(AUGMENT_RUNTIME_OWNED_BOSS, Array.Empty<AugmentData>())
                         .SequenceEqual(expectedBosses, StringComparer.Ordinal);
        if (!exact)
        {
            RollBackAugmentGameplayMigrationState(
                previousChosen,
                previousActive,
                previousBosses,
                previousRows,
                previousCount,
                previousRevision,
                previousOverflow);
            string reason = string.IsNullOrWhiteSpace(selectedSnapshotFailure)
                ? "augment_gameplay_post_restore_mismatch"
                : $"selected_snapshot_invalid:{selectedSnapshotFailure}";
            return MigrationRestoreReport.FailedScope(scope, captured, reason);
        }

        Debug.Log($"[PlayerManager] HostMigration augment gameplay restore complete ({context}) P{playerId} chosen={chosenAugments.Count} activeSummon={_activeMonsterSummonAugments.Count} ownedBoss={_ownedBossAugments.Count}");
        return new MigrationRestoreReport(scope, captured, captured, 0, 0);
    }

    private void RollBackAugmentGameplayMigrationState(
        List<AugmentData> previousChosen,
        List<AugmentData> previousActive,
        List<AugmentData> previousBosses,
        AugmentRuntimeMigrationRow[] previousRows,
        int previousCount,
        int previousRevision,
        bool previousOverflow)
    {
        chosenAugments = previousChosen;
        _activeMonsterSummonAugments = previousActive;
        _ownedBossAugments = previousBosses;
        for (int i = 0; i < AUGMENT_RUNTIME_MIGRATION_CAPACITY; i++)
        {
            AugmentRuntimeMigrationRows.Set(i, previousRows[i]);
        }
        AugmentRuntimeMigrationCount = previousCount;
        AugmentRuntimeMigrationRevision = previousRevision;
        AugmentRuntimeMigrationOverflow = previousOverflow;
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

            string contentId = ResolveLoadedAugmentContentIdByStableId(row.AugmentId);
            if (string.IsNullOrWhiteSpace(contentId))
            {
                Debug.LogError($"[PlayerManager] Durable augment snapshot id could not be resolved. P{playerId}, state={state}, id={row.AugmentId}");
                names.Add($"__unresolved_augment_id_{row.AugmentId}");
                continue;
            }
            int repetitions = Mathf.Max(1, row.Count);
            for (int repetition = 0; repetition < repetitions; repetition++)
            {
                names.Add(contentId);
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

            int augmentId = StableAugmentSnapshotId(augment.ContentId);
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
        if (names == null)
        {
            failureReason = "augment_reference_array_null";
            return false;
        }

        int entryIndex = 0;
        foreach (string rawName in names)
        {
            string name = rawName?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                failureReason = $"augment_reference_empty:{entryIndex}";
                return false;
            }

            if (!TryResolveAugmentContentIdReference(
                    name,
                    out string contentId,
                    out string resolveFailureReason))
            {
                failureReason = $"augment_reference_invalid:{name}:{resolveFailureReason}";
                return false;
            }

            AugmentData augment = augmentManager != null
                ? augmentManager.FindAugmentByContentId(contentId)
                : null;
            if (augment == null)
            {
                augment = Resources.FindObjectsOfTypeAll<AugmentData>()
                    .FirstOrDefault(candidate => candidate != null
                        && string.Equals(candidate.ContentId, contentId, StringComparison.Ordinal));
            }

            if (augment == null)
            {
                failureReason = $"augment_not_loaded:{contentId}";
                return false;
            }

            resolved.Add(augment);
            entryIndex++;
        }

        return true;
    }

    private static string[] BuildAugmentMigrationNames(IEnumerable<AugmentData> augments)
    {
        return (augments ?? Enumerable.Empty<AugmentData>())
            .Where(augment => augment != null)
            .Select(augment => augment.ContentId)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToArray();
    }
}
