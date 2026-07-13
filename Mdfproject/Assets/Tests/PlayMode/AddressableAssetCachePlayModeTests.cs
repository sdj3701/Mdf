using System;
using System.Collections;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using MDF.Runtime.Assets;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MDF.Tests.PlayMode
{
    public sealed class AddressableAssetCachePlayModeTests
    {
        [Test]
        public void AddressableLeaseConcurrentDisposeInvokesReleaseExactlyOnce()
        {
            ConstructorInfo constructor = typeof(AddressableAssetLease<object>).GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new[] { typeof(object), typeof(Action) },
                null);
            Assert.That(constructor, Is.Not.Null);

            int releaseCount = 0;
            var lease = (AddressableAssetLease<object>)constructor.Invoke(new object[]
            {
                new object(),
                (Action)(() => Interlocked.Increment(ref releaseCount))
            });

            Parallel.For(0, 64, _ => lease.Dispose());

            Assert.That(releaseCount, Is.EqualTo(1),
                "A lease must never decrement shared cache retention more than once.");
        }

        [UnityTest]
        public IEnumerator AssetRetentionStateTracksOnlyExplicitLeaseLifetimeAcrossFrames()
        {
            var retention = new AssetRetentionState();

            Assert.That(retention.LeaseCount, Is.EqualTo(0));
            Assert.That(retention.IsRetained, Is.False);

            retention.AcquireLease();
            retention.AcquireLease();
            yield return null;

            Assert.That(Application.isPlaying, Is.True);
            Assert.That(retention.LeaseCount, Is.EqualTo(2));
            Assert.That(retention.IsRetained, Is.True);

            Assert.That(retention.ReleaseLease(), Is.True);
            yield return null;

            Assert.That(retention.LeaseCount, Is.EqualTo(1));
            Assert.That(retention.IsRetained, Is.True);

            Assert.That(retention.ReleaseLease(), Is.True);
            Assert.That(retention.ReleaseLease(), Is.False);
            yield return null;

            Assert.That(retention.LeaseCount, Is.EqualTo(0));
            Assert.That(retention.IsRetained, Is.False);

            retention.AcquireLease();
            retention.Reset();
            yield return null;

            Assert.That(retention.LeaseCount, Is.EqualTo(0));
            Assert.That(retention.IsRetained, Is.False);
        }

        [UnityTest]
        public IEnumerator AddressableOwnerCollapsesDuplicateOwnershipAndReleasesOnDispose()
        {
            const string address = "BaseProjectile";
            int initialEntryCount = AddressableAssetCache.CachedEntryCount;
            int initialLoadCount = AddressableAssetCache.LoadOperationCount;
            var owner = new AddressableAssetOwner();
            GameObject[] assets = null;

            UniTask<GameObject>[] requests =
            {
                owner.LoadAsync<GameObject>(address),
                owner.LoadAsync<GameObject>(address)
            };
            yield return UniTask.WhenAll(requests).ToCoroutine(result => assets = result);

            Assert.That(assets, Is.Not.Null);
            Assert.That(assets[0], Is.SameAs(assets[1]));
            Assert.That(owner.RetainedAssetCount, Is.EqualTo(1));
            Assert.That(AddressableAssetCache.LoadOperationCount - initialLoadCount, Is.EqualTo(1));
            Assert.That(AddressableAssetCache.CachedEntryCount, Is.EqualTo(initialEntryCount + 1));

            owner.Dispose();
            yield return null;

            Assert.That(owner.IsDisposed, Is.True);
            Assert.That(AddressableAssetCache.GetCached<GameObject>(address), Is.Null);
            Assert.That(AddressableAssetCache.CachedEntryCount, Is.EqualTo(initialEntryCount));
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

        [UnityTest]
        public IEnumerator AddressableInstanceLeaseReleasesPrefabWhenInstanceIsDestroyed()
        {
            const string address = "BaseProjectile";
            int initialEntryCount = AddressableAssetCache.CachedEntryCount;
            AddressableAssetLease<GameObject> lease = null;
            yield return AddressableAssetCache.AcquireAsync<GameObject>(address)
                .ToCoroutine(result => lease = result);

            Assert.That(lease, Is.Not.Null);
            Assert.That(AddressableAssetCache.CachedEntryCount, Is.EqualTo(initialEntryCount + 1));

            var instance = new GameObject("AddressableInstanceLeaseTest");
            instance.AddComponent<AddressableInstanceLease>().Initialize(lease);
            UnityEngine.Object.Destroy(instance);
            yield return null;

            Assert.That(AddressableAssetCache.GetCached<GameObject>(address), Is.Null);
            Assert.That(AddressableAssetCache.CachedEntryCount, Is.EqualTo(initialEntryCount));
        }
    }
}
