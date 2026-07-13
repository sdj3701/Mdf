using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

/// <summary>
/// Owns local scroll collection invariants. Network authority, revisioning,
/// loading and RPC publication intentionally remain in PlayerManager.
/// </summary>
public sealed class PlayerMagicScrollInventory
{
    private readonly List<MagicScrollData> _items = new List<MagicScrollData>();
    private readonly ReadOnlyCollection<MagicScrollData> _readOnlyItems;

    public PlayerMagicScrollInventory()
    {
        _readOnlyItems = _items.AsReadOnly();
    }

    public IReadOnlyList<MagicScrollData> Items => _readOnlyItems;

    public int Count => _items.Count;

    public bool TryAdd(MagicScrollData scroll)
    {
        if (scroll == null)
        {
            return false;
        }

        _items.Add(scroll);
        return true;
    }

    public int FindSlot(MagicScrollData scroll)
    {
        if (scroll == null)
        {
            return -1;
        }

        for (int i = 0; i < _items.Count; i++)
        {
            MagicScrollData owned = _items[i];
            if (owned == scroll || (owned != null && owned.name == scroll.name))
            {
                return i;
            }
        }

        return -1;
    }

    public bool TryGetAt(int slotIndex, out MagicScrollData scroll, out string reason)
    {
        scroll = null;
        reason = null;
        if (slotIndex < 0 || slotIndex >= _items.Count)
        {
            reason = "scroll_slot_out_of_range";
            return false;
        }

        scroll = _items[slotIndex];
        if (scroll == null)
        {
            reason = "scroll_slot_empty";
            return false;
        }

        return true;
    }

    public bool TryConsume(MagicScrollData scroll, out int consumedSlot)
    {
        consumedSlot = FindSlot(scroll);
        if (consumedSlot < 0)
        {
            return false;
        }

        _items.RemoveAt(consumedSlot);
        return true;
    }

    public bool TryConsumeAt(int slotIndex, out MagicScrollData scroll, out string reason)
    {
        if (!TryGetAt(slotIndex, out scroll, out reason))
        {
            return false;
        }

        _items.RemoveAt(slotIndex);
        return true;
    }

    public bool TryRefundAt(int slotIndex, MagicScrollData scroll)
    {
        if (scroll == null)
        {
            return false;
        }

        int insertIndex = Math.Max(0, Math.Min(slotIndex, _items.Count));
        _items.Insert(insertIndex, scroll);
        return true;
    }

    public void Replace(IEnumerable<MagicScrollData> scrolls)
    {
        _items.Clear();
        if (scrolls == null)
        {
            return;
        }

        foreach (MagicScrollData scroll in scrolls)
        {
            if (scroll != null)
            {
                _items.Add(scroll);
            }
        }
    }

    public string[] BuildAssetNames()
    {
        return _items
            .Where(scroll => scroll != null && !string.IsNullOrWhiteSpace(scroll.name))
            .Select(scroll => scroll.name)
            .ToArray();
    }

    public MagicScrollData[] BuildValidSnapshot()
    {
        return _items
            .Where(scroll => scroll != null && !string.IsNullOrWhiteSpace(scroll.name))
            .ToArray();
    }
}
