using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace MDF.Runtime.Assets
{
    /// <summary>
    /// Owns one lease per address/type pair and releases every retained Addressables handle when
    /// the gameplay or UI owner leaves its lifecycle. Concurrent requests by the same owner still
    /// share the cache load and collapse to one retained lease.
    /// </summary>
    public sealed class AddressableAssetOwner : IDisposable
    {
        private readonly struct OwnerKey : IEquatable<OwnerKey>
        {
            public OwnerKey(string address, Type assetType)
            {
                Address = address;
                AssetType = assetType;
            }

            public string Address { get; }
            public Type AssetType { get; }

            public bool Equals(OwnerKey other)
            {
                return string.Equals(Address, other.Address, StringComparison.Ordinal) && AssetType == other.AssetType;
            }

            public override bool Equals(object obj)
            {
                return obj is OwnerKey other && Equals(other);
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

        private sealed class OwnedAsset
        {
            public object Asset;
            public IDisposable Lease;
        }

        private readonly object _gate = new object();
        private readonly Dictionary<OwnerKey, OwnedAsset> _assets = new Dictionary<OwnerKey, OwnedAsset>();
        private bool _isDisposed;

        public bool IsDisposed
        {
            get
            {
                lock (_gate)
                {
                    return _isDisposed;
                }
            }
        }

        public int RetainedAssetCount
        {
            get
            {
                lock (_gate)
                {
                    return _assets.Count;
                }
            }
        }

        public async UniTask<T> LoadAsync<T>(string address) where T : class
        {
            if (string.IsNullOrWhiteSpace(address))
            {
                return null;
            }

            string normalizedAddress = address.Trim();
            var key = new OwnerKey(normalizedAddress, typeof(T));
            lock (_gate)
            {
                ThrowIfDisposed();
                if (_assets.TryGetValue(key, out OwnedAsset existing) && IsAssetAlive(existing.Asset))
                {
                    return existing.Asset as T;
                }
            }

            AddressableAssetLease<T> acquired = await AddressableAssetCache.AcquireAsync<T>(normalizedAddress);
            lock (_gate)
            {
                if (_isDisposed)
                {
                    acquired.Dispose();
                    throw new OperationCanceledException($"Addressables owner was disposed while loading '{normalizedAddress}'.");
                }

                if (_assets.TryGetValue(key, out OwnedAsset existing) && IsAssetAlive(existing.Asset))
                {
                    acquired.Dispose();
                    return existing.Asset as T;
                }

                if (existing != null)
                {
                    existing.Lease?.Dispose();
                }

                _assets[key] = new OwnedAsset
                {
                    Asset = acquired.Asset,
                    Lease = acquired
                };
                return acquired.Asset;
            }
        }

        public bool Release<T>(string address) where T : class
        {
            if (string.IsNullOrWhiteSpace(address))
            {
                return false;
            }

            OwnedAsset owned;
            lock (_gate)
            {
                var key = new OwnerKey(address.Trim(), typeof(T));
                if (!_assets.TryGetValue(key, out owned))
                {
                    return false;
                }

                _assets.Remove(key);
            }

            owned.Lease?.Dispose();
            return true;
        }

        public void Dispose()
        {
            List<OwnedAsset> snapshot;
            lock (_gate)
            {
                if (_isDisposed)
                {
                    return;
                }

                _isDisposed = true;
                snapshot = new List<OwnedAsset>(_assets.Values);
                _assets.Clear();
            }

            for (int i = 0; i < snapshot.Count; i++)
            {
                snapshot[i].Lease?.Dispose();
            }
        }

        private void ThrowIfDisposed()
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(AddressableAssetOwner));
            }
        }

        private static bool IsAssetAlive(object asset)
        {
            if (asset == null)
            {
                return false;
            }

            return !(asset is UnityEngine.Object unityObject) || unityObject != null;
        }
    }
}
