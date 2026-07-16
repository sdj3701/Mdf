using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

/// <summary>
/// Owns ordered collection invariants without knowing about Unity assets, networking or UI.
/// Feature-specific inventories provide validity and identity rules at composition time.
/// </summary>
public sealed class OrderedInventory<T> where T : class
{
    private readonly List<T> _items = new List<T>();
    private readonly ReadOnlyCollection<T> _readOnlyItems;
    private readonly Predicate<T> _isValid;
    private readonly Func<T, T, bool> _identityEquals;

    public OrderedInventory(Predicate<T> isValid, Func<T, T, bool> identityEquals)
    {
        _isValid = isValid ?? throw new ArgumentNullException(nameof(isValid));
        _identityEquals = identityEquals ?? throw new ArgumentNullException(nameof(identityEquals));
        _readOnlyItems = _items.AsReadOnly();
    }

    public IReadOnlyList<T> Items => _readOnlyItems;

    public int Count => _items.Count;

    public bool TryAdd(T item)
    {
        if (!_isValid(item))
        {
            return false;
        }

        _items.Add(item);
        return true;
    }

    public int FindIndex(T item)
    {
        if (!_isValid(item))
        {
            return -1;
        }

        for (int i = 0; i < _items.Count; i++)
        {
            if (_identityEquals(_items[i], item))
            {
                return i;
            }
        }

        return -1;
    }

    public bool TryGetAt(int index, out T item)
    {
        if (index < 0 || index >= _items.Count)
        {
            item = null;
            return false;
        }

        item = _items[index];
        return _isValid(item);
    }

    public bool TryRemove(T item, out int removedIndex)
    {
        removedIndex = FindIndex(item);
        if (removedIndex < 0)
        {
            return false;
        }

        _items.RemoveAt(removedIndex);
        return true;
    }

    public bool TryRemoveAt(int index, out T item)
    {
        if (!TryGetAt(index, out item))
        {
            return false;
        }

        _items.RemoveAt(index);
        return true;
    }

    public bool TryInsertAt(int index, T item)
    {
        if (!_isValid(item))
        {
            return false;
        }

        int insertIndex = Math.Max(0, Math.Min(index, _items.Count));
        _items.Insert(insertIndex, item);
        return true;
    }

    public void Replace(IEnumerable<T> items)
    {
        _items.Clear();
        if (items == null)
        {
            return;
        }

        foreach (T item in items)
        {
            if (_isValid(item))
            {
                _items.Add(item);
            }
        }
    }
}
