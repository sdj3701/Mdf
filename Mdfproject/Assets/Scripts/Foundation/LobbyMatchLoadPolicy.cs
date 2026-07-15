using System;
using System.Collections.Generic;

public enum LobbyMatchLoadingState
{
    Idle = 0,
    Warming = 1,
    Ready = 2,
    Failed = 3
}

public readonly struct LobbyMatchPeerLoadState
{
    public LobbyMatchPeerLoadState(int authorityId, int revision, LobbyMatchLoadingState state)
    {
        AuthorityId = authorityId;
        Revision = revision;
        State = state;
    }

    public int AuthorityId { get; }
    public int Revision { get; }
    public LobbyMatchLoadingState State { get; }
}

/// <summary>
/// Pure policy for the lobby prewarm acknowledgement gate. Fusion objects keep ownership of the
/// replicated fields; this class makes stale/foreign ACK and roster rules independently testable.
/// </summary>
public static class LobbyMatchLoadPolicy
{
    public static bool CanRecordAcknowledgement(
        bool hasStateAuthority,
        bool hasValidNetworkObject,
        int owningAuthorityId,
        int sourceAuthorityId,
        int acknowledgementRevision,
        int currentRevision,
        LobbyMatchLoadingState currentState)
    {
        return hasStateAuthority
               && hasValidNetworkObject
               && owningAuthorityId >= 0
               && sourceAuthorityId == owningAuthorityId
               && acknowledgementRevision > 0
               && acknowledgementRevision == currentRevision
               && currentState == LobbyMatchLoadingState.Warming;
    }

    public static bool IsReady(int expectedRevision, int actualRevision, LobbyMatchLoadingState state) =>
        expectedRevision > 0 && expectedRevision == actualRevision && state == LobbyMatchLoadingState.Ready;

    public static bool HasSameRoster(
        IReadOnlyList<int> expectedAuthorityIds,
        IReadOnlyList<int> currentAuthorityIds)
    {
        if (expectedAuthorityIds == null || currentAuthorityIds == null ||
            expectedAuthorityIds.Count != currentAuthorityIds.Count)
        {
            return false;
        }

        for (int i = 0; i < expectedAuthorityIds.Count; i++)
        {
            if (expectedAuthorityIds[i] != currentAuthorityIds[i])
            {
                return false;
            }
        }

        return expectedAuthorityIds.Count > 0;
    }

    public static bool CanLoadScene(
        int expectedRevision,
        IReadOnlyList<int> expectedAuthorityIds,
        IReadOnlyList<LobbyMatchPeerLoadState> peers)
    {
        if (expectedRevision <= 0 || expectedAuthorityIds == null || peers == null ||
            expectedAuthorityIds.Count == 0 || expectedAuthorityIds.Count != peers.Count)
        {
            return false;
        }

        for (int i = 0; i < peers.Count; i++)
        {
            LobbyMatchPeerLoadState peer = peers[i];
            if (peer.AuthorityId != expectedAuthorityIds[i] ||
                !IsReady(expectedRevision, peer.Revision, peer.State))
            {
                return false;
            }
        }

        return true;
    }

    public static int NextRevision(IEnumerable<int> currentRevisions)
    {
        int maximum = 0;
        if (currentRevisions != null)
        {
            foreach (int revision in currentRevisions)
            {
                maximum = Math.Max(maximum, revision);
            }
        }

        return maximum >= int.MaxValue ? 1 : maximum + 1;
    }
}
