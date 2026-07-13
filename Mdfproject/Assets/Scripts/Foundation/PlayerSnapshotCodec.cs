using System;

/// <summary>
/// Pure packing rules shared by player snapshots and host-migration payloads.
/// This assembly deliberately has no Unity or Fusion dependency.
/// </summary>
public static class PlayerSnapshotCodec
{
    public const int MinPlayerId = -1;
    public const int MaxPlayerId = 0xFFFE;
    public const int MaxPackedPlayerId = 0xFFFF;

    public static bool IsSha256Hex(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length != 64)
        {
            return false;
        }

        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
            {
                return false;
            }
        }

        return true;
    }

    public static int PackShopMeta(int starLevel, bool sold)
    {
        return Clamp(starLevel, 0, 255) | ((sold ? 1 : 0) << 8);
    }

    public static int UnpackShopStarLevel(int packedMeta) => packedMeta & 0xFF;

    public static bool UnpackShopSold(int packedMeta) => ((packedMeta >> 8) & 0x1) != 0;

    public static int PackMonsterCounts(int remainingCount, int maxCount, bool isBoss)
    {
        return Clamp(remainingCount, 0, 0x7FFF)
            | (Clamp(maxCount, 0, 0x7FFF) << 15)
            | ((isBoss ? 1 : 0) << 30);
    }

    public static int PackPlayerIds(int targetPlayerId, int originPlayerId)
    {
        return PackPlayerId(targetPlayerId) | (PackPlayerId(originPlayerId) << 16);
    }

    public static int PackPlayerId(int playerId)
    {
        if (!TryPackPlayerId(playerId, out int packedPlayerId))
        {
            throw new ArgumentOutOfRangeException(
                nameof(playerId),
                playerId,
                $"Player id must be in [{MinPlayerId}, {MaxPlayerId}].");
        }

        return packedPlayerId;
    }

    public static bool TryPackPlayerId(int playerId, out int packedPlayerId)
    {
        if (playerId < MinPlayerId || playerId > MaxPlayerId)
        {
            packedPlayerId = 0;
            return false;
        }

        packedPlayerId = playerId + 1;
        return true;
    }

    public static int UnpackPlayerId(int packedPlayerId)
    {
        if (packedPlayerId < 0 || packedPlayerId > MaxPackedPlayerId)
        {
            throw new ArgumentOutOfRangeException(nameof(packedPlayerId));
        }

        return packedPlayerId - 1;
    }

    public static int PackCell(int x, int y)
    {
        if (!TryPackCell(x, y, out int packedCell))
        {
            throw new ArgumentOutOfRangeException(
                nameof(x),
                $"Cell coordinates must fit signed 16-bit values. x={x}, y={y}");
        }

        return packedCell;
    }

    public static bool TryPackCell(int x, int y, out int packedCell)
    {
        if (x < short.MinValue || x > short.MaxValue || y < short.MinValue || y > short.MaxValue)
        {
            packedCell = 0;
            return false;
        }

        packedCell = (x & 0xFFFF) | (y << 16);
        return true;
    }

    public static void UnpackCell(int packedCell, out int x, out int y)
    {
        x = (short)(packedCell & 0xFFFF);
        y = (short)((packedCell >> 16) & 0xFFFF);
    }

    public static int PackHealthAndRevision(float currentHealth, float maxHealth, int revision)
    {
        if (!TryPackHealthAndRevision(currentHealth, maxHealth, revision, out int packedHealthAndRevision))
        {
            throw new ArgumentOutOfRangeException(
                nameof(revision),
                revision,
                $"Revision must be in [0, {ushort.MaxValue}].");
        }

        return packedHealthAndRevision;
    }

    public static bool TryPackHealthAndRevision(
        float currentHealth,
        float maxHealth,
        int revision,
        out int packedHealthAndRevision)
    {
        if (revision < 0 || revision > ushort.MaxValue)
        {
            packedHealthAndRevision = 0;
            return false;
        }

        float normalized = maxHealth > 0f ? Clamp01(currentHealth / maxHealth) : 0f;
        int quantizedHealth = Clamp((int)Math.Round(normalized * ushort.MaxValue), 0, ushort.MaxValue);
        packedHealthAndRevision = quantizedHealth | (revision << 16);
        return true;
    }

    public static void UnpackHealthAndRevision(
        int packedHealthAndRevision,
        float maxHealth,
        out float currentHealth,
        out int revision)
    {
        int quantizedHealth = packedHealthAndRevision & 0xFFFF;
        revision = (packedHealthAndRevision >> 16) & 0xFFFF;
        currentHealth = Math.Max(0f, maxHealth) * (quantizedHealth / (float)ushort.MaxValue);
    }

    private static float Clamp01(float value)
    {
        if (value < 0f) return 0f;
        return value > 1f ? 1f : value;
    }

    private static int Clamp(int value, int min, int max)
    {
        if (value < min) return min;
        return value > max ? max : value;
    }
}
