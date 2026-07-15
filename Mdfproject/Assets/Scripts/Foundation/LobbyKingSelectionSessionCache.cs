using System;
using System.Collections.Generic;

/// <summary>
/// Scene-independent session cache for authoritative lobby king selections.
/// PlayerRef is the fast path inside one runner; the durable token hash carries the
/// selection across reconnects, PlayerRef reuse, lobby scene unload, and host migration.
/// </summary>
public sealed class LobbyKingSelectionSessionCache
{
    private readonly Dictionary<int, int> _selectionByPlayerRefId = new Dictionary<int, int>();
    private readonly Dictionary<string, int> _selectionByTokenHash =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    public int Count => _selectionByTokenHash.Count;

    public bool Remember(int playerRefId, string tokenHash, int selectionHash)
    {
        if (!KingSelectionCatalog.IsAllowedHash(selectionHash))
        {
            return false;
        }

        selectionHash = KingSelectionCatalog.NormalizeOrDefaultHash(selectionHash);

        if (playerRefId >= 0)
        {
            _selectionByPlayerRefId[playerRefId] = selectionHash;
        }

        if (PlayerSnapshotCodec.IsSha256Hex(tokenHash))
        {
            _selectionByTokenHash[tokenHash] = selectionHash;
        }

        return playerRefId >= 0 || PlayerSnapshotCodec.IsSha256Hex(tokenHash);
    }

    public void PrepareJoinedPlayer(int playerRefId, string tokenHash)
    {
        if (playerRefId < 0)
        {
            return;
        }

        // PlayerRef values can be reused. Never inherit the previous connection's value.
        _selectionByPlayerRefId.Remove(playerRefId);
        if (PlayerSnapshotCodec.IsSha256Hex(tokenHash)
            && _selectionByTokenHash.TryGetValue(tokenHash, out int selectionHash))
        {
            _selectionByPlayerRefId[playerRefId] = selectionHash;
        }
    }

    public void ForgetPlayerRef(int playerRefId)
    {
        if (playerRefId >= 0)
        {
            _selectionByPlayerRefId.Remove(playerRefId);
        }
    }

    public bool TryResolve(int playerRefId, string tokenHash, out int selectionHash)
    {
        if (playerRefId >= 0
            && _selectionByPlayerRefId.TryGetValue(playerRefId, out selectionHash)
            && KingSelectionCatalog.IsAllowedHash(selectionHash))
        {
            return true;
        }

        if (PlayerSnapshotCodec.IsSha256Hex(tokenHash)
            && _selectionByTokenHash.TryGetValue(tokenHash, out selectionHash)
            && KingSelectionCatalog.IsAllowedHash(selectionHash))
        {
            return true;
        }

        selectionHash = 0;
        return false;
    }

    public int ResolveInitialSelection(int playerRefId, string tokenHash, int requestedSelectionHash)
    {
        if (TryResolve(playerRefId, tokenHash, out int rememberedSelectionHash))
        {
            return rememberedSelectionHash;
        }

        int normalizedSelectionHash = KingSelectionCatalog.NormalizeOrDefaultHash(requestedSelectionHash);
        Remember(playerRefId, tokenHash, normalizedSelectionHash);
        return normalizedSelectionHash;
    }

    public void Clear()
    {
        _selectionByPlayerRefId.Clear();
        _selectionByTokenHash.Clear();
    }
}
