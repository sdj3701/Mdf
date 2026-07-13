using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace MDF.Runtime.Assets
{
    /// <summary>
    /// Keeps an Addressables asset alive until the owner disposes the lease.
    /// </summary>
    public sealed class AddressableAssetLease<T> : IDisposable where T : class
    {
        private Action _release;

        internal AddressableAssetLease(T asset, Action release)
        {
            Asset = asset;
            _release = release;
        }

        public T Asset { get; }

        public void Dispose()
        {
            Action release = Interlocked.Exchange(ref _release, null);
            release?.Invoke();
        }
    }

    /// <summary>
    /// Type-safe, single-flight Addressables cache with explicit lease ownership.
    /// </summary>
    public static class AddressableAssetCache
    {
        private readonly struct CacheKey : IEquatable<CacheKey>
        {
            public CacheKey(string address, Type assetType)
            {
                Address = address;
                AssetType = assetType;
            }

            public string Address { get; }
            public Type AssetType { get; }

            public bool Equals(CacheKey other)
            {
                return string.Equals(Address, other.Address, StringComparison.Ordinal) && AssetType == other.AssetType;
            }

            public override bool Equals(object obj)
            {
                return obj is CacheKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return ((Address != null ? StringComparer.Ordinal.GetHashCode(Address) : 0) * 397) ^
                           (AssetType != null ? AssetType.GetHashCode() : 0);
                }
            }
        }

        private sealed class CacheEntry
        {
            public CacheKey Key;
            public AsyncOperationHandle Handle;
            public bool HasHandle;
            public bool IsLoading;
            public readonly AssetRetentionState Retention = new AssetRetentionState();
            public object Asset;
            public UniTaskCompletionSource<object> Completion;
        }

        private static readonly Dictionary<CacheKey, CacheEntry> Entries =
            new Dictionary<CacheKey, CacheEntry>();

        private static int _loadOperationCount;
        public static int CachedEntryCount => Entries.Count;
        public static int LoadOperationCount => _loadOperationCount;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForSubsystemRegistration()
        {
            ReleaseAllImmediately();
            _loadOperationCount = 0;
        }

        public static async UniTask<AddressableAssetLease<T>> AcquireAsync<T>(string address) where T : class
        {
            CacheEntry entry = GetOrCreateEntry<T>(address);

            try
            {
                T asset = (T)await entry.Completion.Task;
                return new AddressableAssetLease<T>(asset, () => ReleaseLease(entry));
            }
            catch
            {
                ReleaseLease(entry);
                throw;
            }
        }

        public static T GetCached<T>(string address) where T : class
        {
            if (string.IsNullOrWhiteSpace(address))
            {
                return null;
            }

            var key = new CacheKey(address.Trim(), typeof(T));
            if (!Entries.TryGetValue(key, out CacheEntry entry) || entry.Asset == null)
            {
                return null;
            }

            if (entry.Asset is UnityEngine.Object unityObject && unityObject == null)
            {
                return null;
            }

            return entry.Asset as T;
        }

        private static CacheEntry GetOrCreateEntry<T>(string address) where T : class
        {
            if (string.IsNullOrWhiteSpace(address))
            {
                throw new ArgumentException("Addressables address must not be empty.", nameof(address));
            }

            string normalizedAddress = address.Trim();
            var key = new CacheKey(normalizedAddress, typeof(T));
            if (Entries.TryGetValue(key, out CacheEntry existing))
            {
                if (!existing.IsLoading && !IsAssetAlive(existing.Asset))
                {
                    RemoveAndRelease(existing);
                }
                else
                {
                    existing.Retention.AcquireLease();
                    return existing;
                }
            }

            var entry = new CacheEntry
            {
                Key = key,
                IsLoading = true,
                Completion = new UniTaskCompletionSource<object>()
            };

            entry.Retention.AcquireLease();

            Entries.Add(key, entry);
            LoadEntryAsync<T>(entry).Forget();
            return entry;
        }

        private static bool IsAssetAlive(object asset)
        {
            if (asset == null)
            {
                return false;
            }

            return !(asset is UnityEngine.Object unityObject) || unityObject != null;
        }

        private static async UniTask LoadEntryAsync<T>(CacheEntry entry) where T : class
        {
            try
            {
                AsyncOperationHandle<T> handle = Addressables.LoadAssetAsync<T>(entry.Key.Address);
                entry.Handle = handle;
                entry.HasHandle = true;
                _loadOperationCount++;

                T asset = await handle.Task;
                if (handle.Status != AsyncOperationStatus.Succeeded || asset == null)
                {
                    throw handle.OperationException ??
                          new InvalidOperationException($"Addressables load returned no asset: {entry.Key.Address}");
                }

                entry.Asset = asset;
                entry.IsLoading = false;
                TryReleaseUnused(entry);
                entry.Completion.TrySetResult(asset);
            }
            catch (Exception exception)
            {
                entry.IsLoading = false;
                entry.Completion.TrySetException(exception);
                RemoveAndRelease(entry);
            }
        }

        private static void ReleaseLease(CacheEntry entry)
        {
            entry.Retention.ReleaseLease();

            TryReleaseUnused(entry);
        }

        private static void TryReleaseUnused(CacheEntry entry)
        {
            if (entry.IsLoading || entry.Retention.IsRetained)
            {
                return;
            }

            RemoveAndRelease(entry);
        }

        private static void RemoveAndRelease(CacheEntry entry)
        {
            if (Entries.TryGetValue(entry.Key, out CacheEntry current) && ReferenceEquals(current, entry))
            {
                Entries.Remove(entry.Key);
            }

            if (entry.HasHandle && entry.Handle.IsValid())
            {
                Addressables.Release(entry.Handle);
            }

            entry.HasHandle = false;
            entry.Asset = null;
        }

        private static void ReleaseAllImmediately()
        {
            var snapshot = new List<CacheEntry>(Entries.Values);
            Entries.Clear();

            for (int i = 0; i < snapshot.Count; i++)
            {
                CacheEntry entry = snapshot[i];
                if (entry.HasHandle && entry.Handle.IsValid())
                {
                    Addressables.Release(entry.Handle);
                }

                entry.HasHandle = false;
                entry.Asset = null;
                entry.Retention.Reset();
            }
        }
    }
}
