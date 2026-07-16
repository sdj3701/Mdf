using System.Security.Cryptography;
using System.Text;
using Fusion;

public struct SurvivorBossReplicatedRow : INetworkStruct
{
    public int DataKeyHash;
    public int State;
    public int BossUniqueId;
    public int OriginPlayerId;
    public int TargetPlayerId;
    public float RemainingHP;
    public float MaxHP;
    public NetworkBool Invaded;
}

public partial class GameManagers
{
    private const int SURVIVOR_BOSS_PAYLOAD_CAPACITY = 32;
    [Networked] private int SurvivorBossSnapshotRevision { get; set; }
    [Networked] private int SurvivorBossSnapshotPendingCount { get; set; }
    [Networked] private int SurvivorBossSnapshotAssignmentCount { get; set; }
    [Networked] private NetworkString<_64> SurvivorBossSnapshotPendingHashHex { get; set; }
    [Networked] private NetworkString<_64> SurvivorBossSnapshotAssignmentHashHex { get; set; }
    [Networked] private int SurvivorBossPayloadCount { get; set; }
    [Networked] private int SurvivorBossPayloadNextUniqueId { get; set; }
    [Networked] private NetworkBool SurvivorBossPayloadOverflow { get; set; }
    [Networked, Capacity(SURVIVOR_BOSS_PAYLOAD_CAPACITY)]
    private NetworkArray<SurvivorBossReplicatedRow> SurvivorBossPayloadRows { get; }

    private void PublishSurvivorBossSnapshotFromManager(SurvivorBossManager manager, string reason)
    {
        if (Object == null || !Object.IsValid || !Object.HasStateAuthority || manager == null)
        {
            return;
        }

        manager.CaptureStableSnapshot(
            out string pendingSnapshot,
            out string assignmentSnapshot,
            out int pendingCount,
            out int assignmentCount);

        SurvivorBossSnapshotPendingCount = pendingCount;
        SurvivorBossSnapshotAssignmentCount = assignmentCount;
        SurvivorBossSnapshotPendingHashHex = HashStableStringHex(pendingSnapshot);
        SurvivorBossSnapshotAssignmentHashHex = HashStableStringHex(assignmentSnapshot);

        SurvivorBossReplicatedRow[] payloadRows = manager.CaptureDurableReplicatedRows(out int nextBossUniqueId);
        int payloadCount = System.Math.Min(payloadRows?.Length ?? 0, SURVIVOR_BOSS_PAYLOAD_CAPACITY);
        for (int i = 0; i < SURVIVOR_BOSS_PAYLOAD_CAPACITY; i++)
        {
            SurvivorBossPayloadRows.Set(i, i < payloadCount ? payloadRows[i] : default);
        }
        SurvivorBossPayloadCount = payloadCount;
        SurvivorBossPayloadNextUniqueId = nextBossUniqueId;
        SurvivorBossPayloadOverflow = (payloadRows?.Length ?? 0) > SURVIVOR_BOSS_PAYLOAD_CAPACITY;
        if (SurvivorBossPayloadOverflow)
        {
            UnityEngine.Debug.LogError($"[GameManagers] Survivor boss durable payload overflow. count={payloadRows.Length}, capacity={SURVIVOR_BOSS_PAYLOAD_CAPACITY}, reason={reason}");
        }
        SurvivorBossSnapshotRevision++;
    }

    public bool CaptureSurvivorBossPayloadForMigration(
        out SurvivorBossReplicatedRow[] rows,
        out int nextBossUniqueId,
        out bool overflow)
    {
        int count = UnityEngine.Mathf.Clamp(SurvivorBossPayloadCount, 0, SURVIVOR_BOSS_PAYLOAD_CAPACITY);
        rows = new SurvivorBossReplicatedRow[count];
        for (int i = 0; i < count; i++)
        {
            rows[i] = SurvivorBossPayloadRows.Get(i);
        }

        nextBossUniqueId = SurvivorBossPayloadNextUniqueId;
        overflow = SurvivorBossPayloadOverflow;
        return !overflow;
    }

    public void CaptureSurvivorBossSnapshotHashesForState(
        out string pendingHash,
        out string assignmentHash,
        out int pendingCount,
        out int assignmentCount)
    {
        pendingCount = SurvivorBossSnapshotPendingCount;
        assignmentCount = SurvivorBossSnapshotAssignmentCount;
        pendingHash = FormatHash(SurvivorBossSnapshotPendingHashHex.ToString());
        assignmentHash = FormatHash(SurvivorBossSnapshotAssignmentHashHex.ToString());
    }

    private static string HashStableStringHex(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        using (var sha = SHA256.Create())
        {
            byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(value));
            var builder = new StringBuilder(64);
            for (int i = 0; i < bytes.Length; i++)
            {
                builder.Append(bytes[i].ToString("x2"));
            }

            return builder.ToString();
        }
    }

    private static string FormatHash(string hashHex)
    {
        return string.IsNullOrWhiteSpace(hashHex) ? "unknown" : "sha256:" + hashHex;
    }
}
