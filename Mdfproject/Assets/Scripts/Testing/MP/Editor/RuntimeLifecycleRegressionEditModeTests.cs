#if UNITY_EDITOR
using System.Collections;
using System.Reflection;
using Cysharp.Threading.Tasks;
using Fusion;
using MDF.Runtime.Assets;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using NUnitAssert = NUnit.Framework.Assert;

public sealed class RuntimeLifecycleRegressionEditModeTests
{
    [UnityTest]
    public IEnumerator VfxPoolAddressableLoadSupportsConcurrentSubscribers()
    {
        const string address = "VFX_Projectile_Fire4_Projectile";
        int initialEntryCount = AddressableAssetCache.CachedEntryCount;
        int initialLoadCount = AddressableAssetCache.LoadOperationCount;
        var managerObject = new GameObject("VfxPoolSingleFlightTest");
        VfxPoolManager manager = managerObject.AddComponent<VfxPoolManager>();
        GameObject[] loaded = null;

        try
        {
            UniTask<GameObject>[] requests =
            {
                manager.LoadAddressablePrefabAsync(address),
                manager.LoadAddressablePrefabAsync(address),
                manager.LoadAddressablePrefabAsync(address)
            };

            yield return UniTask.WhenAll(requests).ToCoroutine(result => loaded = result);

            NUnitAssert.That(loaded, Has.Length.EqualTo(3));
            NUnitAssert.That(loaded[0], Is.Not.Null);
            NUnitAssert.That(loaded[1], Is.SameAs(loaded[0]));
            NUnitAssert.That(loaded[2], Is.SameAs(loaded[0]));
            NUnitAssert.That(AddressableAssetCache.LoadOperationCount - initialLoadCount, Is.EqualTo(1));
            NUnitAssert.That(AddressableAssetCache.CachedEntryCount, Is.EqualTo(initialEntryCount + 1));
        }
        finally
        {
            MethodInfo onDestroy = typeof(VfxPoolManager).GetMethod(
                "OnDestroy",
                BindingFlags.Instance | BindingFlags.NonPublic);
            NUnitAssert.That(onDestroy, Is.Not.Null);
            onDestroy.Invoke(manager, null);
            UnityEngine.Object.DestroyImmediate(managerObject);
        }

        NUnitAssert.That(AddressableAssetCache.GetCached<GameObject>(address), Is.Null);
        NUnitAssert.That(AddressableAssetCache.CachedEntryCount, Is.EqualTo(initialEntryCount));
    }

    [Test]
    public void NameBasedNetworkPrewarmSharesOnePrefabCapacityAfterIdResolution()
    {
        var runnerObject = new GameObject("NamePrewarmRunner");
        var prefabObject = new GameObject("MonsterPrewarmPrefab");

        try
        {
            NetworkRunner runner = runnerObject.AddComponent<NetworkRunner>();
            PooledNetworkObjectProvider provider = runnerObject.AddComponent<PooledNetworkObjectProvider>();
            NetworkObject prefab = prefabObject.AddComponent<NetworkObject>();
            NetworkPrefabId prefabId = default;
            provider.SetMaxPoolCount(2);

            NUnitAssert.That(provider.PrewarmPrefab(runner, prefab, 5), Is.EqualTo(2));
            NUnitAssert.That(provider.GetFreeCount(prefab), Is.EqualTo(2));

            NUnitAssert.That(provider.PrewarmPrefab(runner, prefab, prefabId, 5), Is.Zero,
                "Resolving the prefab id must promote, not duplicate, the name-path prewarm pool.");
            NUnitAssert.That(provider.GetFreeCount(prefabId), Is.EqualTo(2));
            NUnitAssert.That(provider.GetFreeCount(prefab), Is.EqualTo(2));

            NUnitAssert.That(provider.PrewarmPrefab(runner, prefab, 5), Is.Zero,
                "The MonsterSpawner name overload must use the resolved id pool after promotion.");
            NUnitAssert.That(provider.GetFreeCount(prefabId), Is.EqualTo(2));
            NUnitAssert.That(provider.GetFreeCount(prefab.name), Is.EqualTo(2));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(runnerObject);
            UnityEngine.Object.DestroyImmediate(prefabObject);
        }
    }

    [Test]
    public void StatusBarDisableEnableCycleRestoresExactlyOneVitalSubscription()
    {
        var owner = new GameObject("StatusBarOwner");
        var statusObject = new GameObject("StatusBar", typeof(RectTransform));
        var healthObject = new GameObject("Health", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var manaObject = new GameObject("Mana", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        statusObject.SetActive(false);
        statusObject.transform.SetParent(owner.transform, false);
        healthObject.transform.SetParent(statusObject.transform, false);
        manaObject.transform.SetParent(statusObject.transform, false);

        try
        {
            StatusBarLifecycleProbe probe = owner.AddComponent<StatusBarLifecycleProbe>();
            StatusBarUI statusBar = statusObject.AddComponent<StatusBarUI>();
            Image healthImage = healthObject.GetComponent<Image>();
            Image manaImage = manaObject.GetComponent<Image>();
            statusBar.healthBarImage = healthImage;
            statusBar.manaBarImage = manaImage;

            MethodInfo onEnable = typeof(StatusBarUI).GetMethod(
                "OnEnable",
                BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo onDisable = typeof(StatusBarUI).GetMethod(
                "OnDisable",
                BindingFlags.Instance | BindingFlags.NonPublic);
            NUnitAssert.That(onEnable, Is.Not.Null);
            NUnitAssert.That(onDisable, Is.Not.Null);
            onEnable.Invoke(statusBar, null);
            NUnitAssert.That(probe.HealthSubscriberCount, Is.EqualTo(1));
            NUnitAssert.That(probe.ManaSubscriberCount, Is.EqualTo(1));

            probe.RaiseHealth(75f, 100f);
            probe.RaiseMana(10f, 40f);
            NUnitAssert.That(healthImage.fillAmount, Is.EqualTo(0.75f).Within(0.001f));

            onDisable.Invoke(statusBar, null);
            NUnitAssert.That(probe.HealthSubscriberCount, Is.Zero);
            NUnitAssert.That(probe.ManaSubscriberCount, Is.Zero);
            probe.RaiseHealth(50f, 100f);
            NUnitAssert.That(healthImage.fillAmount, Is.EqualTo(0.75f).Within(0.001f));

            onEnable.Invoke(statusBar, null);
            NUnitAssert.That(probe.HealthSubscriberCount, Is.EqualTo(1));
            NUnitAssert.That(probe.ManaSubscriberCount, Is.EqualTo(1));
            NUnitAssert.That(healthImage.fillAmount, Is.EqualTo(0.5f).Within(0.001f));

            MethodInfo initialize = typeof(StatusBarUI).GetMethod(
                "Initialize",
                BindingFlags.Instance | BindingFlags.NonPublic);
            NUnitAssert.That(initialize, Is.Not.Null);
            initialize.Invoke(statusBar, null);
            initialize.Invoke(statusBar, null);
            NUnitAssert.That(probe.HealthSubscriberCount, Is.EqualTo(1));
            NUnitAssert.That(probe.ManaSubscriberCount, Is.EqualTo(1));

            probe.RaiseHealth(25f, 100f);
            NUnitAssert.That(healthImage.fillAmount, Is.EqualTo(0.25f).Within(0.001f));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(owner);
        }
    }
}
#endif
