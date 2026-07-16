using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Bounded FIFO storage for pooled Unity objects.
/// Return, membership, and count operations are O(1); destroyed entries are discarded lazily on rent.
/// </summary>
public sealed class BoundedUnityObjectPool<T> where T : UnityEngine.Object
{
    private readonly struct Entry
    {
        public readonly T Item;
        public readonly int InstanceId;

        public Entry(T item, int instanceId)
        {
            Item = item;
            InstanceId = instanceId;
        }
    }

    private readonly Queue<Entry> _entries;
    private readonly HashSet<int> _instanceIds;

    public int Count
    {
        get
        {
            PruneDestroyedFront();
            return _entries.Count;
        }
    }
    public int Capacity { get; private set; }

    public BoundedUnityObjectPool(int capacity)
    {
        Capacity = Mathf.Max(1, capacity);
        _entries = new Queue<Entry>(Capacity);
        _instanceIds = new HashSet<int>();
    }

    public bool Contains(T item)
    {
        return item != null && _instanceIds.Contains(item.GetInstanceID());
    }

    public bool TryReturn(T item)
    {
        PruneDestroyedFront();
        if (item == null || _entries.Count >= Capacity)
        {
            return false;
        }

        int instanceId = item.GetInstanceID();
        if (!_instanceIds.Add(instanceId))
        {
            return false;
        }

        _entries.Enqueue(new Entry(item, instanceId));
        return true;
    }

    public bool TryRent(out T item)
    {
        while (_entries.Count > 0)
        {
            Entry entry = _entries.Dequeue();
            _instanceIds.Remove(entry.InstanceId);
            if (entry.Item == null)
            {
                continue;
            }

            item = entry.Item;
            return true;
        }

        item = null;
        return false;
    }

    public void SetCapacity(int capacity, Action<T> onOverflow = null)
    {
        Capacity = Mathf.Max(1, capacity);
        while (_entries.Count > Capacity)
        {
            Entry entry = _entries.Dequeue();
            _instanceIds.Remove(entry.InstanceId);
            if (entry.Item != null)
            {
                onOverflow?.Invoke(entry.Item);
            }
        }
    }

    public void Drain(Action<T> onItem)
    {
        while (_entries.Count > 0)
        {
            Entry entry = _entries.Dequeue();
            _instanceIds.Remove(entry.InstanceId);
            if (entry.Item != null)
            {
                onItem?.Invoke(entry.Item);
            }
        }
    }

    private void PruneDestroyedFront()
    {
        while (_entries.Count > 0 && _entries.Peek().Item == null)
        {
            Entry destroyed = _entries.Dequeue();
            _instanceIds.Remove(destroyed.InstanceId);
        }
    }
}
