#if UNITY_EDITOR
using System.Collections;
using System.Reflection;
using Cysharp.Threading.Tasks;
using Fusion;
using MDF.Runtime.Assets;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using NUnitAssert = NUnit.Framework.Assert;

public sealed class RuntimeLifecycleRegressionEditModeTests
{
    [Test]
    public void SequenceTransitionDelayNeverReplacesExpiredPhaseCountdown()
    {
        MethodInfo resolver = typeof(GameManagers).GetMethod(
            "ResolveDisplayedPhaseTime",
            BindingFlags.Static | BindingFlags.NonPublic);
        NUnitAssert.That(resolver, Is.Not.Null);

        NUnitAssert.That((float)resolver.Invoke(null, new object[] { 18.25f, false }), Is.EqualTo(18.25f));
        NUnitAssert.That((float)resolver.Invoke(null, new object[] { -0.1f, false }), Is.Zero);
        NUnitAssert.That((float)resolver.Invoke(null, new object[] { 2.75f, true }), Is.Zero,
            "A pending sequence transition must hold the visible phase timer at zero, not show its 2-3 second delay.");

        MethodInfo legacyUpdate = typeof(PhaseTimerUI).GetMethod(
            "Update",
            BindingFlags.Instance | BindingFlags.NonPublic);
        MethodInfo toolkitUpdate = typeof(GamePrepareUIToolkitController).GetMethod(
            "UpdateRoundTimerLabel",
            BindingFlags.Instance | BindingFlags.NonPublic);
        NUnitAssert.That(legacyUpdate, Is.Not.Null);
        NUnitAssert.That(toolkitUpdate, Is.Not.Null);
        NUnitAssert.That(MdfCompiledCodePolicy.ReferencesMethod(
            legacyUpdate,
            typeof(GameManagers),
            "get_currentDisplayedPhaseTimer"), Is.True);
        NUnitAssert.That(MdfCompiledCodePolicy.ReferencesMethod(
            toolkitUpdate,
            typeof(GameManagers),
            "get_currentDisplayedPhaseTimer"), Is.True);
        NUnitAssert.That(MdfCompiledCodePolicy.ReferencesMethod(
            legacyUpdate,
            typeof(GameManagers),
            "get_currentSequenceTransitionTimer"), Is.False);
        NUnitAssert.That(MdfCompiledCodePolicy.ReferencesMethod(
            toolkitUpdate,
            typeof(GameManagers),
            "get_currentSequenceTransitionTimer"), Is.False);
    }

    [Test]
    public void PhaseBoundariesAreImmediateAndBattleReadinessRetriesStayInsideTransition()
    {
        const BindingFlags instanceMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        MethodInfo retry = typeof(GameManagers).GetMethod(
            "RearmBattleTransitionRetryTimer",
            instanceMembers);
        MethodInfo startBattleOne = typeof(GameManagers).GetMethod("StartBattle1Phase", instanceMembers);
        MethodInfo startBattleTwo = typeof(GameManagers).GetMethod("StartBattle2Phase", instanceMembers);
        MethodInfo complete = typeof(GameManagers).GetMethod("CompleteSequenceTransition", instanceMembers);

        NUnitAssert.That(retry, Is.Not.Null);
        NUnitAssert.That(startBattleOne?.ReturnType, Is.EqualTo(typeof(bool)));
        NUnitAssert.That(startBattleTwo?.ReturnType, Is.EqualTo(typeof(bool)));
        NUnitAssert.That(complete, Is.Not.Null);
        NUnitAssert.That(
            MdfCompiledCodePolicy.ReferencesMethod(retry, typeof(GameManagers), "set_sequenceTransitionTimer"),
            Is.True);
        NUnitAssert.That(
            MdfCompiledCodePolicy.ReferencesMethod(retry, typeof(GameManagers), "set_phaseTimer"),
            Is.True,
            "Retry must explicitly keep the expired phase timer at None.");
        NUnitAssert.That(
            MdfCompiledCodePolicy.ReferencesField(retry, typeof(GameManagers), "_sequenceTransitionCompletionStarted"),
            Is.True,
            "Retry must re-arm sequence completion instead of ending the transition.");

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/GameManagers.prefab");
        NUnitAssert.That(prefab, Is.Not.Null);
        GameManagers configured = prefab.GetComponent<GameManagers>();
        NUnitAssert.That(configured, Is.Not.Null);
        NUnitAssert.That(configured.sequenceTransitionDelaySeconds, Is.Zero,
            "A normal 0-second phase boundary must switch immediately; retry delays remain internal only.");
    }

    [TestCase(GameManagers.GameState.Prepare, GameManagers.GameState.Battle1)]
    [TestCase(GameManagers.GameState.Battle1, GameManagers.GameState.Battle2)]
    [TestCase(GameManagers.GameState.Battle2, GameManagers.GameState.Prepare)]
    public void ExpiredPhaseSelectsExactlyOneBoundaryTarget(
        GameManagers.GameState current,
        GameManagers.GameState expected)
    {
        NUnitAssert.That(ResolveExpiredPhaseTarget(current, false, false), Is.EqualTo(expected));
        NUnitAssert.That(ResolveExpiredPhaseTarget(current, true, false), Is.Null,
            "Once a sequence transition is pending, the same expired phase cannot schedule it again.");
    }

    [Test]
    public void BattleTwoRoundTransitionCannotBeScheduledTwice()
    {
        NUnitAssert.That(
            ResolveExpiredPhaseTarget(GameManagers.GameState.Battle2, false, true),
            Is.Null);
        NUnitAssert.That(
            ResolveExpiredPhaseTarget(GameManagers.GameState.Setup, false, false),
            Is.Null);
        NUnitAssert.That(
            ResolveExpiredPhaseTarget(GameManagers.GameState.DataLoading, false, false),
            Is.Null);
        NUnitAssert.That(
            ResolveExpiredPhaseTarget(GameManagers.GameState.GameOver, false, false),
            Is.Null);
    }

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

    [UnityTest]
    public IEnumerator TimedSkillVfxReturnsToPoolAndReusesTheSameInstance()
    {
        var managerObject = new GameObject("TimedSkillVfxPoolTest");
        var prefab = new GameObject("TimedSkillVfxPrefab");

        try
        {
            VfxPoolManager manager = managerObject.AddComponent<VfxPoolManager>();
            MethodInfo awake = typeof(VfxPoolManager).GetMethod(
                "Awake",
                BindingFlags.Instance | BindingFlags.NonPublic);
            NUnitAssert.That(awake, Is.Not.Null);
            awake.Invoke(manager, null);
            NUnitAssert.That(VfxPoolManager.Instance, Is.SameAs(manager));

            GameObject first = VfxPoolManager.SpawnTimed(
                prefab,
                Vector3.one,
                Quaternion.identity,
                0f);
            NUnitAssert.That(first, Is.Not.Null);
            NUnitAssert.That(first.activeSelf, Is.True);
            NUnitAssert.That(first.GetComponent<PooledObject>(), Is.Not.Null);
            NUnitAssert.That(first.GetComponent<VFXAutoDestroy>(), Is.Not.Null);

            yield return null;
            yield return null;

            NUnitAssert.That(first.activeSelf, Is.False,
                "A completed timed VFX must return to the pool instead of being destroyed.");

            GameObject reused = VfxPoolManager.SpawnTimed(
                prefab,
                Vector3.zero,
                Quaternion.identity,
                10f);
            NUnitAssert.That(reused, Is.SameAs(first));
            NUnitAssert.That(reused.activeSelf, Is.True);

            reused.GetComponent<PooledObject>().ReturnToPool();
            NUnitAssert.That(reused.activeSelf, Is.False);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(managerObject);
            UnityEngine.Object.DestroyImmediate(prefab);
        }
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

    private static object ResolveExpiredPhaseTarget(
        GameManagers.GameState current,
        bool sequenceTransitioning,
        bool roundTransitioning)
    {
        MethodInfo resolver = typeof(GameManagers).GetMethod(
            "ResolveExpiredPhaseTransitionTarget",
            BindingFlags.Static | BindingFlags.NonPublic);
        NUnitAssert.That(resolver, Is.Not.Null);
        return resolver.Invoke(null, new object[] { current, sequenceTransitioning, roundTransitioning });
    }
}
#endif
