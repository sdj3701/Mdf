using System;
using System.Collections.Generic;

/// <summary>
/// Authority-only lobby cache for demon selections. Unlike kings, demon choices are never
/// mirrored into replicated NetworkPlayer state, so remote lobby clients cannot inspect them.
/// </summary>
public sealed class LobbyDemonSelectionSessionCache
{
    private readonly Dictionary<int, int> _selectionByPlayerRefId = new Dictionary<int, int>();
    private readonly Dictionary<string, int> _selectionByTokenHash =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    public bool Remember(int playerRefId, string tokenHash, int selectionHash)
    {
        if (!DemonSelectionCatalog.IsAllowedHash(selectionHash))
        {
            return false;
        }

        int canonicalHash = DemonSelectionCatalog.NormalizeOrDefaultHash(selectionHash);
        if (playerRefId >= 0)
        {
            _selectionByPlayerRefId[playerRefId] = canonicalHash;
        }

        if (PlayerSnapshotCodec.IsSha256Hex(tokenHash))
        {
            _selectionByTokenHash[tokenHash] = canonicalHash;
        }

        return playerRefId >= 0 || PlayerSnapshotCodec.IsSha256Hex(tokenHash);
    }

    public void PrepareJoinedPlayer(int playerRefId, string tokenHash)
    {
        if (playerRefId < 0)
        {
            return;
        }

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

    public void ClearPlayerRefs()
    {
        _selectionByPlayerRefId.Clear();
    }

    public bool TryResolve(int playerRefId, string tokenHash, out int selectionHash)
    {
        if (PlayerSnapshotCodec.IsSha256Hex(tokenHash))
        {
            if (_selectionByTokenHash.TryGetValue(tokenHash, out selectionHash)
                && DemonSelectionCatalog.IsAllowedHash(selectionHash))
            {
                return true;
            }

            selectionHash = 0;
            return false;
        }

        if (playerRefId >= 0
            && _selectionByPlayerRefId.TryGetValue(playerRefId, out selectionHash)
            && DemonSelectionCatalog.IsAllowedHash(selectionHash))
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

        int canonicalHash = DemonSelectionCatalog.NormalizeOrDefaultHash(requestedSelectionHash);
        Remember(playerRefId, tokenHash, canonicalHash);
        return canonicalHash;
    }

    public void Clear()
    {
        _selectionByPlayerRefId.Clear();
        _selectionByTokenHash.Clear();
    }
}
