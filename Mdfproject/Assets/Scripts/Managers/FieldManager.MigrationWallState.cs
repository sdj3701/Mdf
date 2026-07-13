using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

public partial class FieldManager
{
    public bool TryResolveDestructibleWallMaxHealth(int level, out float maxHealth)
    {
        maxHealth = 0f;
        if (level < 1 || destructibleWallPrefab == null)
        {
            return false;
        }

        DestructibleWall configuredWall = destructibleWallPrefab.GetComponent<DestructibleWall>();
        WallProgressionData progression = configuredWall != null ? configuredWall.ProgressionData : null;
        if (progression != null
            && progression.TryGetLevel(level, out WallLevelData levelData)
            && levelData != null
            && levelData.MaxHealth > 0f)
        {
            maxHealth = levelData.MaxHealth;
            return true;
        }

        // Retain a level-one fallback for older prefabs that have not yet adopted progression
        // assets. Higher levels must resolve through explicit data rather than local wall state.
        if (level == 1 && configuredWall != null && configuredWall.MaxHealth > 0f)
        {
            maxHealth = configuredWall.MaxHealth;
            return true;
        }

        return false;
    }

    public bool TryGetDestructibleWallMigrationSnapshot(
        out int[] flatPositions,
        out float[] currentHealth,
        out float[] maxHealth,
        out int[] revisions,
        out int[] levels,
        out int[] upgradeInvestments)
    {
        if (playerManager != null
            && playerManager.TryGetDurableDestructibleWallMigrationSnapshot(
                out flatPositions,
                out currentHealth,
                out maxHealth,
                out revisions,
                out levels,
                out upgradeInvestments))
        {
            return true;
        }

        return CaptureLocalDestructibleWallMigrationSnapshot(
            out flatPositions,
            out currentHealth,
            out maxHealth,
            out revisions,
            out levels,
            out upgradeInvestments);
    }

    public bool CaptureLocalDestructibleWallMigrationSnapshot(
        out int[] flatPositions,
        out float[] currentHealth,
        out float[] maxHealth,
        out int[] revisions,
        out int[] levels,
        out int[] upgradeInvestments)
    {
        RebuildWallMapsAfterMigration("FieldManager.CaptureDestructibleWallHealth", false, out _);
        var entries = placedWalls
            .Where(entry => entry.Value != null && entry.Value.gameObject.activeInHierarchy)
            .OrderBy(entry => entry.Key.x)
            .ThenBy(entry => entry.Key.y)
            .ThenBy(entry => entry.Key.z)
            .ToArray();

        flatPositions = new int[entries.Length * 2];
        currentHealth = new float[entries.Length];
        maxHealth = new float[entries.Length];
        revisions = new int[entries.Length];
        levels = new int[entries.Length];
        upgradeInvestments = new int[entries.Length];
        for (int i = 0; i < entries.Length; i++)
        {
            flatPositions[i * 2] = entries[i].Key.x;
            flatPositions[i * 2 + 1] = entries[i].Key.y;
            currentHealth[i] = entries[i].Value.CurrentHealth;
            maxHealth[i] = entries[i].Value.MaxHealth;
            revisions[i] = entries[i].Value.StateRevision;
            levels[i] = entries[i].Value.CurrentLevel;
            upgradeInvestments[i] = entries[i].Value.InvestedUpgradeGold;
        }

        return true;
    }

    public void PublishDestructibleWallHealthMigrationState(string context)
    {
        playerManager?.PublishDestructibleWallHealthMigrationStateFromAuthority(context);
    }

    public void PublishDestructibleWallHealthDelta(DestructibleWall wall, string context)
    {
        if (wall == null)
        {
            return;
        }

        playerManager?.PublishDestructibleWallHealthDeltaFromAuthority(
            wall.GridPosition,
            wall.CurrentHealth,
            wall.MaxHealth,
            wall.StateRevision,
            wall.CurrentLevel,
            wall.InvestedUpgradeGold,
            context);
    }

    public bool RestoreDestructibleWallHealthAfterHostMigration(
        int[] flatPositions,
        float[] currentHealth,
        float[] maxHealth,
        int[] revisions,
        int[] levels,
        int[] upgradeInvestments,
        string context,
        out int restoredCount,
        out int failedCount)
    {
        restoredCount = 0;
        failedCount = 0;
        int positionCount = (flatPositions?.Length ?? 0) / 2;
        int healthCount = currentHealth?.Length ?? 0;
        int maxHealthCount = maxHealth?.Length ?? 0;
        int revisionCount = revisions?.Length ?? 0;
        int levelCount = levels?.Length ?? 0;
        int investmentCount = upgradeInvestments?.Length ?? 0;
        if ((flatPositions?.Length ?? 0) % 2 != 0
            || positionCount != healthCount
            || positionCount != maxHealthCount
            || positionCount != revisionCount
            || positionCount != levelCount
            || positionCount != investmentCount)
        {
            failedCount = Math.Max(
                Math.Max(positionCount, healthCount),
                Math.Max(Math.Max(maxHealthCount, revisionCount), Math.Max(levelCount, investmentCount)));
            return false;
        }

        if (playerManager != null
            && playerManager.Object != null
            && playerManager.Object.IsValid
            && !playerManager.Object.HasStateAuthority)
        {
            failedCount = positionCount;
            return false;
        }

        RebuildWallMapsAfterMigration($"FieldManager.RestoreDestructibleWallHealth.Pre.{context}", false, out _);
        for (int i = 0; i < positionCount; i++)
        {
            var position = new Vector3Int(flatPositions[i * 2], flatPositions[i * 2 + 1], 0);
            if (!IsValidGridPosition(position)
                || !placedWalls.TryGetValue(position, out DestructibleWall wall)
                || wall == null
                || !wall.RestoreProgressionAfterMigration(levels[i], upgradeInvestments[i])
                || !wall.RestoreHealthAfterMigration(currentHealth[i], maxHealth[i], revisions[i]))
            {
                failedCount++;
                continue;
            }

            restoredCount++;
        }

        bool success = failedCount == 0 && restoredCount == positionCount;
        if (success)
        {
            PublishDestructibleWallHealthMigrationState($"restore:{context}");
        }
        if (!success)
        {
            Debug.LogError($"[WallFlow-Migration] destructible wall HP restore failed. owner={BuildWallOwnerTag()}, context={context}, restored={restoredCount}/{positionCount}, failed={failedCount}");
        }
        return success;
    }

    public string BuildDestructibleWallHealthSnapshot()
    {
        TryGetDestructibleWallMigrationSnapshot(
            out int[] flatPositions,
            out float[] currentHealth,
            out float[] maxHealth,
            out int[] revisions,
            out int[] levels,
            out int[] upgradeInvestments);
        var parts = new List<string>(currentHealth.Length);
        for (int i = 0; i < currentHealth.Length; i++)
        {
            parts.Add(string.Join(",",
                flatPositions[i * 2].ToString(CultureInfo.InvariantCulture),
                flatPositions[i * 2 + 1].ToString(CultureInfo.InvariantCulture),
                currentHealth[i].ToString("R", CultureInfo.InvariantCulture),
                maxHealth[i].ToString("R", CultureInfo.InvariantCulture),
                revisions[i].ToString(CultureInfo.InvariantCulture),
                levels[i].ToString(CultureInfo.InvariantCulture),
                upgradeInvestments[i].ToString(CultureInfo.InvariantCulture)));
        }
        return string.Join("|", parts);
    }
}
