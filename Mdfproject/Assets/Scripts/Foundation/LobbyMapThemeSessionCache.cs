using System;
using System.Collections.Generic;

/// <summary>
/// Scene-independent cache for each player's cosmetic map theme. PlayerRef is a runner-local
/// fast path; the durable token carries the choice across reconnect, scene load, and host change.
/// </summary>
public sealed class LobbyMapThemeSessionCache
{
    private readonly Dictionary<int, int> _themeByPlayerRefId = new Dictionary<int, int>();
    private readonly Dictionary<string, int> _themeByTokenHash =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    public int Count => _themeByTokenHash.Count;

    public bool Remember(int playerRefId, string tokenHash, int themeId)
    {
        if (!MapThemeCatalog.IsAllowed(themeId))
        {
            return false;
        }

        themeId = MapThemeCatalog.NormalizeOrDefault(themeId);
        if (playerRefId >= 0)
        {
            _themeByPlayerRefId[playerRefId] = themeId;
        }

        if (PlayerSnapshotCodec.IsSha256Hex(tokenHash))
        {
            _themeByTokenHash[tokenHash] = themeId;
        }

        return playerRefId >= 0 || PlayerSnapshotCodec.IsSha256Hex(tokenHash);
    }

    public void PrepareJoinedPlayer(int playerRefId, string tokenHash)
    {
        if (playerRefId < 0)
        {
            return;
        }

        _themeByPlayerRefId.Remove(playerRefId);
        if (PlayerSnapshotCodec.IsSha256Hex(tokenHash)
            && _themeByTokenHash.TryGetValue(tokenHash, out int themeId))
        {
            _themeByPlayerRefId[playerRefId] = themeId;
        }
    }

    public void ForgetPlayerRef(int playerRefId)
    {
        if (playerRefId >= 0)
        {
            _themeByPlayerRefId.Remove(playerRefId);
        }
    }

    public void ClearPlayerRefs()
    {
        _themeByPlayerRefId.Clear();
    }

    public bool TryResolve(int playerRefId, string tokenHash, out int themeId)
    {
        bool hasDurableToken = PlayerSnapshotCodec.IsSha256Hex(tokenHash);
        if (hasDurableToken)
        {
            if (_themeByTokenHash.TryGetValue(tokenHash, out themeId)
                && MapThemeCatalog.IsAllowed(themeId))
            {
                return true;
            }

            // A valid durable identity must never fall back to a runner-local PlayerRef.
            themeId = 0;
            return false;
        }

        if (playerRefId >= 0
            && _themeByPlayerRefId.TryGetValue(playerRefId, out themeId)
            && MapThemeCatalog.IsAllowed(themeId))
        {
            return true;
        }

        themeId = 0;
        return false;
    }

    public int ResolveInitialSelection(int playerRefId, string tokenHash, int requestedThemeId)
    {
        if (TryResolve(playerRefId, tokenHash, out int rememberedThemeId))
        {
            return rememberedThemeId;
        }

        int normalizedThemeId = MapThemeCatalog.NormalizeOrDefault(requestedThemeId);
        Remember(playerRefId, tokenHash, normalizedThemeId);
        return normalizedThemeId;
    }

    public void Clear()
    {
        _themeByPlayerRefId.Clear();
        _themeByTokenHash.Clear();
    }
}
