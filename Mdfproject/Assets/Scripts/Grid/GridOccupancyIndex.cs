using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Explicit owner for one field's cell-to-object mapping. It implements IDictionary so existing
/// authority and migration code can move to the service without changing mutation ordering.
/// </summary>
public sealed class GridOccupancyIndex<T> : IDictionary<Vector3Int, T>
{
    private readonly Dictionary<Vector3Int, T> _entries;

    public GridOccupancyIndex()
    {
        _entries = new Dictionary<Vector3Int, T>();
    }

    public GridOccupancyIndex(IEnumerable<KeyValuePair<Vector3Int, T>> entries)
    {
        _entries = entries != null
            ? new Dictionary<Vector3Int, T>(entries)
            : new Dictionary<Vector3Int, T>();
    }

    public T this[Vector3Int key]
    {
        get => _entries[key];
        set => _entries[key] = value;
    }

    public ICollection<Vector3Int> Keys => _entries.Keys;
    public ICollection<T> Values => _entries.Values;
    public int Count => _entries.Count;
    public bool IsReadOnly => false;

    public void Add(Vector3Int key, T value) => _entries.Add(key, value);
    public bool ContainsKey(Vector3Int key) => _entries.ContainsKey(key);
    public bool ContainsValue(T value) => _entries.ContainsValue(value);
    public bool Remove(Vector3Int key) => _entries.Remove(key);
    public bool TryGetValue(Vector3Int key, out T value) => _entries.TryGetValue(key, out value);
    public void Add(KeyValuePair<Vector3Int, T> item) =>
        ((ICollection<KeyValuePair<Vector3Int, T>>)_entries).Add(item);
    public void Clear() => _entries.Clear();
    public bool Contains(KeyValuePair<Vector3Int, T> item) =>
        ((ICollection<KeyValuePair<Vector3Int, T>>)_entries).Contains(item);
    public void CopyTo(KeyValuePair<Vector3Int, T>[] array, int arrayIndex) =>
        ((ICollection<KeyValuePair<Vector3Int, T>>)_entries).CopyTo(array, arrayIndex);
    public bool Remove(KeyValuePair<Vector3Int, T> item) =>
        ((ICollection<KeyValuePair<Vector3Int, T>>)_entries).Remove(item);
    public IEnumerator<KeyValuePair<Vector3Int, T>> GetEnumerator() => _entries.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
