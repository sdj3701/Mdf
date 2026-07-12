using System.Collections;
using Cysharp.Threading.Tasks;
using MDF.Runtime.Assets;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MDF.Tests.PlayMode
{
    public sealed class AddressableAssetCachePlayModeTests
    {
        [UnityTest]
        public IEnumerator AssetRetentionStateTracksPinAndLeaseLifetimeAcrossFrames()
        {
            var retention = new AssetRetentionState();

            Assert.That(retention.IsPinned, Is.False);
            Assert.That(retention.LeaseCount, Is.EqualTo(0));
            Assert.That(retention.IsRetained, Is.False);

            retention.Pin();
            retention.AcquireLease();
            retention.AcquireLease();
            yield return null;

            Assert.That(Application.isPlaying, Is.True);
            Assert.That(retention.IsPinned, Is.True);
            Assert.That(retention.LeaseCount, Is.EqualTo(2));
            Assert.That(retention.IsRetained, Is.True);

            retention.Unpin();
            Assert.That(retention.ReleaseLease(), Is.True);
            yield return null;

            Assert.That(retention.IsPinned, Is.False);
            Assert.That(retention.LeaseCount, Is.EqualTo(1));
            Assert.That(retention.IsRetained, Is.True);

            Assert.That(retention.ReleaseLease(), Is.True);
            Assert.That(retention.ReleaseLease(), Is.False);
            yield return null;

            Assert.That(retention.LeaseCount, Is.EqualTo(0));
            Assert.That(retention.IsRetained, Is.False);

            retention.Pin();
            retention.AcquireLease();
            retention.Reset();
            yield return null;

            Assert.That(retention.IsPinned, Is.False);
            Assert.That(retention.LeaseCount, Is.EqualTo(0));
            Assert.That(retention.IsRetained, Is.False);
        }

        [UnityTest]
        public IEnumerator AddressableCacheSharesConcurrentLoadAndReleasesAfterLastLease()
        {
            const string address = "BaseProjectile";
            int initialEntryCount = AddressableAssetCache.CachedEntryCount;
            int initialLoadCount = AddressableAssetCache.LoadOperationCount;
            AddressableAssetLease<GameObject>[] leases = null;

            UniTask<AddressableAssetLease<GameObject>>[] requests =
            {
                AddressableAssetCache.AcquireAsync<GameObject>(address),
                AddressableAssetCache.AcquireAsync<GameObject>(address)
            };
            yield return UniTask.WhenAll(requests).ToCoroutine(result => leases = result);

            Assert.That(leases, Is.Not.Null);
            Assert.That(leases.Length, Is.EqualTo(2));
            Assert.That(leases[0].Asset, Is.Not.Null);
            Assert.That(leases[1].Asset, Is.SameAs(leases[0].Asset));
            Assert.That(AddressableAssetCache.LoadOperationCount - initialLoadCount, Is.EqualTo(1));
            Assert.That(AddressableAssetCache.CachedEntryCount, Is.EqualTo(initialEntryCount + 1));

            leases[0].Dispose();
            Assert.That(AddressableAssetCache.GetCached<GameObject>(address), Is.Not.Null);
            leases[1].Dispose();
            yield return null;

            Assert.That(AddressableAssetCache.GetCached<GameObject>(address), Is.Null);
            Assert.That(AddressableAssetCache.CachedEntryCount, Is.EqualTo(initialEntryCount));
        }
    }
}
