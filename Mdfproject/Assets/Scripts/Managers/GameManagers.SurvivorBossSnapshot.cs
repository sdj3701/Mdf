using System.Security.Cryptography;
using System.Text;
using Fusion;

public partial class GameManagers
{
    [Networked] private int SurvivorBossSnapshotRevision { get; set; }
    [Networked] private int SurvivorBossSnapshotPendingCount { get; set; }
    [Networked] private int SurvivorBossSnapshotAssignmentCount { get; set; }
    [Networked] private NetworkString<_64> SurvivorBossSnapshotPendingHashHex { get; set; }
    [Networked] private NetworkString<_64> SurvivorBossSnapshotAssignmentHashHex { get; set; }

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
        SurvivorBossSnapshotRevision++;
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
