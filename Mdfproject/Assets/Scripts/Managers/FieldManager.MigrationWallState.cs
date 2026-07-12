using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

public partial class FieldManager
{
    public bool TryGetDestructibleWallMigrationSnapshot(
        out int[] flatPositions,
        out float[] currentHealth,
        out float[] maxHealth,
        out int[] revisions)
    {
        if (playerManager != null
            && playerManager.TryGetDurableDestructibleWallMigrationSnapshot(
                out flatPositions,
                out currentHealth,
                out maxHealth,
                out revisions))
        {
            return true;
        }

        return CaptureLocalDestructibleWallMigrationSnapshot(
            out flatPositions,
            out currentHealth,
            out maxHealth,
            out revisions);
    }

    public bool CaptureLocalDestructibleWallMigrationSnapshot(
        out int[] flatPositions,
        out float[] currentHealth,
        out float[] maxHealth,
        out int[] revisions)
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
        for (int i = 0; i < entries.Length; i++)
        {
            flatPositions[i * 2] = entries[i].Key.x;
            flatPositions[i * 2 + 1] = entries[i].Key.y;
            currentHealth[i] = entries[i].Value.CurrentHealth;
            maxHealth[i] = entries[i].Value.MaxHealth;
            revisions[i] = entries[i].Value.StateRevision;
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
            context);
    }

    public bool RestoreDestructibleWallHealthAfterHostMigration(
        int[] flatPositions,
        float[] currentHealth,
        float[] maxHealth,
        int[] revisions,
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
        if ((flatPositions?.Length ?? 0) % 2 != 0
            || positionCount != healthCount
            || positionCount != maxHealthCount
            || positionCount != revisionCount)
        {
            failedCount = Math.Max(positionCount, Math.Max(healthCount, Math.Max(maxHealthCount, revisionCount)));
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
            out int[] revisions);
        var parts = new List<string>(currentHealth.Length);
        for (int i = 0; i < currentHealth.Length; i++)
        {
            parts.Add(string.Join(",",
                flatPositions[i * 2].ToString(CultureInfo.InvariantCulture),
                flatPositions[i * 2 + 1].ToString(CultureInfo.InvariantCulture),
                currentHealth[i].ToString("R", CultureInfo.InvariantCulture),
                maxHealth[i].ToString("R", CultureInfo.InvariantCulture),
                revisions[i].ToString(CultureInfo.InvariantCulture)));
        }
        return string.Join("|", parts);
    }
}
