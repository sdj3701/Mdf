using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Owns local scroll collection invariants. Network authority, revisioning,
/// loading and RPC publication intentionally remain in PlayerManager.
/// </summary>
public sealed class PlayerMagicScrollInventory
{
    private readonly OrderedInventory<MagicScrollData> _inventory =
        new OrderedInventory<MagicScrollData>(IsValid, HasSameIdentity);

    public IReadOnlyList<MagicScrollData> Items => _inventory.Items;

    public int Count => _inventory.Count;

    public bool TryAdd(MagicScrollData scroll)
    {
        return _inventory.TryAdd(scroll);
    }

    public int FindSlot(MagicScrollData scroll)
    {
        return _inventory.FindIndex(scroll);
    }

    public bool TryGetAt(int slotIndex, out MagicScrollData scroll, out string reason)
    {
        scroll = null;
        reason = null;
        if (slotIndex < 0 || slotIndex >= _inventory.Count)
        {
            reason = "scroll_slot_out_of_range";
            return false;
        }

        if (!_inventory.TryGetAt(slotIndex, out scroll))
        {
            reason = "scroll_slot_empty";
            return false;
        }

        return true;
    }

    public bool TryConsume(MagicScrollData scroll, out int consumedSlot)
    {
        return _inventory.TryRemove(scroll, out consumedSlot);
    }

    public bool TryConsumeAt(int slotIndex, out MagicScrollData scroll, out string reason)
    {
        if (!TryGetAt(slotIndex, out scroll, out reason))
        {
            return false;
        }

        return _inventory.TryRemoveAt(slotIndex, out scroll);
    }

    public bool TryRefundAt(int slotIndex, MagicScrollData scroll)
    {
        return _inventory.TryInsertAt(slotIndex, scroll);
    }

    public void Replace(IEnumerable<MagicScrollData> scrolls)
    {
        _inventory.Replace(scrolls);
    }

    public string[] BuildAssetNames()
    {
        return _inventory.Items
            .Where(IsValid)
            .Select(scroll => scroll.name ?? string.Empty)
            .ToArray();
    }

    public string[] BuildContentIds()
    {
        return _inventory.Items
            .Where(IsValid)
            .Select(scroll => scroll.ContentId)
            .ToArray();
    }

    public MagicScrollData[] BuildValidSnapshot()
    {
        return _inventory.Items
            .Where(IsValid)
            .ToArray();
    }

    /// <summary>
    /// Resolves a migration identity without ever selecting the first ambiguous legacy name.
    /// A present contentId is authoritative; the asset name is consulted only for old snapshots
    /// that did not capture a durable identity.
    /// </summary>
    public static bool TryResolveSnapshotIdentity(
        IEnumerable<MagicScrollData> candidates,
        string contentId,
        string legacyAssetName,
        out MagicScrollData resolved,
        out string failureReason)
    {
        resolved = null;
        failureReason = string.Empty;
        var uniqueCandidates = new HashSet<MagicScrollData>(
            candidates?.Where(IsValid) ?? Enumerable.Empty<MagicScrollData>());
        string normalizedContentId = StableDataKeyUtility.NormalizeContentId(contentId);
        if (!string.IsNullOrEmpty(normalizedContentId))
        {
            MagicScrollData[] matches = uniqueCandidates
                .Where(candidate => string.Equals(candidate.ContentId, normalizedContentId, StringComparison.Ordinal))
                .ToArray();
            if (matches.Length == 1)
            {
                resolved = matches[0];
                return true;
            }

            failureReason = matches.Length > 1
                ? $"scroll_content_id_ambiguous:{normalizedContentId}"
                : $"scroll_content_id_unresolved:{normalizedContentId}";
            return false;
        }

        string normalizedLegacyName = StableDataKeyUtility.NormalizeKey(legacyAssetName);
        if (string.IsNullOrEmpty(normalizedLegacyName))
        {
            failureReason = "scroll_identity_missing";
            return false;
        }

        MagicScrollData[] legacyMatches = uniqueCandidates
            .Where(candidate => string.Equals(
                StableDataKeyUtility.NormalizeKey(candidate.name),
                normalizedLegacyName,
                StringComparison.Ordinal))
            .ToArray();
        if (legacyMatches.Length == 1)
        {
            resolved = legacyMatches[0];
            return true;
        }

        failureReason = legacyMatches.Length > 1
            ? $"scroll_legacy_name_ambiguous:{normalizedLegacyName}"
            : $"scroll_legacy_name_unresolved:{normalizedLegacyName}";
        return false;
    }

    private static bool IsValid(MagicScrollData scroll)
    {
        return scroll != null;
    }

    private static bool HasSameIdentity(MagicScrollData left, MagicScrollData right)
    {
        if (left == right)
        {
            return true;
        }

        if (left == null || right == null)
        {
            return false;
        }

        string leftContentId = left.ContentId;
        string rightContentId = right.ContentId;
        if (!string.IsNullOrEmpty(leftContentId) && !string.IsNullOrEmpty(rightContentId))
        {
            return string.Equals(leftContentId, rightContentId, StringComparison.Ordinal);
        }

        return string.Equals(
            StableDataKeyUtility.NormalizeKey(left.name),
            StableDataKeyUtility.NormalizeKey(right.name),
            StringComparison.Ordinal);
    }
}
