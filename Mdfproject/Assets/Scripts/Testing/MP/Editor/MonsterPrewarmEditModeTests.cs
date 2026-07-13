#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using Fusion;
using NUnit.Framework;
using UnityEngine;
using Assert = NUnit.Framework.Assert;

public sealed class MonsterPrewarmEditModeTests
{
    [Test]
    public void PooledNetworkObjectProviderSupportsExplicitPrewarm()
    {
        GameObject runnerObject = new GameObject("PoolTestRunner");
        GameObject prefabObject = new GameObject("PoolTestMonsterPrefab");
        try
        {
            NetworkRunner runner = runnerObject.AddComponent<NetworkRunner>();
            PooledNetworkObjectProvider provider = runnerObject.AddComponent<PooledNetworkObjectProvider>();
            NetworkObject prefab = prefabObject.AddComponent<NetworkObject>();
            provider.SetMaxPoolCount(2);

            int created = provider.PrewarmPrefab(runner, prefab, default, 5);

            Assert.That(created, Is.EqualTo(2));
            Assert.That(provider.GetFreeCount(default(NetworkPrefabId)), Is.EqualTo(2));
            Assert.That(provider.PrewarmPrefab(runner, prefab, default, 5), Is.Zero);
            Assert.That(runnerObject.transform.Find("[PooledNetworkObjects]"), Is.Not.Null);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(prefabObject);
            UnityEngine.Object.DestroyImmediate(runnerObject);
        }
    }

    [Test]
    public void PooledNetworkObjectProviderDetachesRentedInstanceFromTransformedPoolRoot()
    {
        GameObject runnerObject = new GameObject("TransformedPoolTestRunner");
        GameObject prefabObject = new GameObject("TransformedPoolTestPrefab");
        NetworkObject rented = null;
        try
        {
            runnerObject.transform.SetPositionAndRotation(
                new Vector3(-2.3834f, 0f, 2.8259f),
                Quaternion.Euler(0f, 17f, 0f));
            NetworkRunner runner = runnerObject.AddComponent<NetworkRunner>();
            PooledNetworkObjectProvider provider = runnerObject.AddComponent<PooledNetworkObjectProvider>();
            NetworkObject prefab = prefabObject.AddComponent<NetworkObject>();
            prefabObject.transform.localPosition = new Vector3(0.25f, 0.5f, -0.75f);
            prefabObject.transform.localRotation = Quaternion.Euler(0f, 25f, 0f);
            prefabObject.transform.localScale = new Vector3(1.25f, 1.5f, 0.75f);

            NetworkPrefabId prefabId = default;
            Assert.That(provider.PrewarmPrefab(runner, prefab, prefabId, 1), Is.EqualTo(1));

            MethodInfo acquire = typeof(PooledNetworkObjectProvider).GetMethod(
                "InstantiatePrefab",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(acquire, Is.Not.Null);
            rented = acquire.Invoke(provider, new object[] { runner, prefab, prefabId }) as NetworkObject;

            Assert.That(rented, Is.Not.Null);
            Assert.That(rented.transform.parent, Is.Null,
                "Fusion must receive a root object, never a child of the transformed pool root");
            Assert.That(rented.transform.position, Is.EqualTo(prefab.transform.position));
            Assert.That(Quaternion.Angle(rented.transform.rotation, prefab.transform.rotation), Is.LessThan(0.001f));
            Assert.That(rented.transform.localScale, Is.EqualTo(prefab.transform.localScale));
            Assert.That(rented.gameObject.activeSelf, Is.True);
        }
        finally
        {
            if (rented != null)
            {
                UnityEngine.Object.DestroyImmediate(rented.gameObject);
            }
            UnityEngine.Object.DestroyImmediate(prefabObject);
            UnityEngine.Object.DestroyImmediate(runnerObject);
        }
    }

    [Test]
    public void BoundedUnityObjectPoolPrunesDestroyedFrontAndKeepsConstantCapacity()
    {
        var pool = new BoundedUnityObjectPool<GameObject>(2);
        GameObject first = new GameObject("First");
        GameObject second = new GameObject("Second");
        GameObject replacement = new GameObject("Replacement");
        try
        {
            Assert.That(pool.TryReturn(first), Is.True);
            Assert.That(pool.TryReturn(first), Is.False, "duplicate returns must not create duplicate queue entries");
            Assert.That(pool.TryReturn(second), Is.True);
            Assert.That(pool.TryReturn(replacement), Is.False, "capacity must be strict");

            UnityEngine.Object.DestroyImmediate(first);
            Assert.That(pool.Count, Is.EqualTo(1), "destroyed front entries should be pruned amortized O(1)");
            Assert.That(pool.TryReturn(replacement), Is.True);
            Assert.That(pool.Count, Is.EqualTo(2));

            Assert.That(pool.TryRent(out GameObject rented), Is.True);
            Assert.That(rented, Is.SameAs(second));
        }
        finally
        {
            if (second != null) UnityEngine.Object.DestroyImmediate(second);
            if (replacement != null) UnityEngine.Object.DestroyImmediate(replacement);
        }
    }

    [Test]
    public void VfxPoolRejectsDoubleReturnAndNeverRentsTheSameInstanceTwice()
    {
        GameObject root = new GameObject("VfxPoolDoubleReturnTest");
        GameObject prefab = new GameObject("VfxPoolPrefab");
        try
        {
            VfxPoolManager pool = root.AddComponent<VfxPoolManager>();
            pool.Prewarm(prefab, 1);

            GameObject original = pool.Spawn(prefab, Vector3.zero, Quaternion.identity);
            Assert.That(original, Is.Not.Null);
            pool.Despawn(original);
            pool.Despawn(original);

            GameObject firstRent = pool.Spawn(prefab, Vector3.zero, Quaternion.identity);
            GameObject secondRent = pool.Spawn(prefab, Vector3.one, Quaternion.identity);

            Assert.That(firstRent, Is.SameAs(original));
            Assert.That(secondRent, Is.Not.Null);
            Assert.That(secondRent, Is.Not.SameAs(firstRent));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(prefab);
        }
    }

    [Test]
    public void NormalPrewarmDemandUsesConcurrentAttackersBlackMagicAndSoftCap()
    {
        Assert.That(
            MonsterSpawner.EstimateNormalPrewarmTarget(
                activePlayerCount: 4,
                maximumBlackMagic: 10,
                minimumBlackMagicCost: 3,
                perPrefabCap: 32),
            Is.EqualTo(8),
            "two concurrent attackers can each spend ten black magic on four cheapest summons");

        Assert.That(
            MonsterSpawner.EstimateNormalPrewarmTarget(2, 20, 5, 32),
            Is.EqualTo(4));
        Assert.That(
            MonsterSpawner.EstimateNormalPrewarmTarget(4, 70, 3, 32),
            Is.EqualTo(32),
            "large augment budgets must stay below the per-prefab memory cap");
        Assert.That(
            MonsterSpawner.EstimateNormalPrewarmTarget(4, 70, 3, 7),
            Is.EqualTo(7),
            "the configured cap must remain authoritative even for extreme demand");
        Assert.That(
            MonsterSpawner.EstimateNormalPrewarmTarget(4, 10, 3, 32, waveCountPerBattleForPrefab: 2),
            Is.EqualTo(12),
            "base-wave occupancy and black-magic summons share one absolute network pool target");
    }

    [Test]
    public void PreparePrewarmsExplicitWaveAndWaitsForValidatedClientCompletion()
    {
        string spawner = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/Game/Monsters/MonsterSpawner.cs");
        string gameManagers = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/Managers/GameManagers.cs");
        string player = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/Managers/PlayerManager.cs");

        Assert.That(spawner, Does.Contain("EstimateNormalPrewarmTarget("));
        Assert.That(spawner, Does.Contain("maximumBlackMagic"));
        Assert.That(spawner, Does.Contain("minimumNormalCost"));
        Assert.That(spawner, Does.Contain("waveCountPerBattleForPrefab"));
        Assert.That(spawner, Does.Contain("ResolveWaveCountForPrefab"));
        Assert.That(spawner, Does.Contain("maxNormalPrewarmCountPerMonsterPrefab = 32"));
        Assert.That(spawner, Does.Not.Contain("prewarmCountPerMonsterPrefab = 12"));
        Assert.That(spawner, Does.Contain("MonsterPrewarmReport"));
        Assert.That(spawner, Does.Contain("Monster prewarm completed with failures"));

        int explicitWaveIndex = gameManagers.IndexOf(
            "GetWaveForRound(currentRound)",
            StringComparison.Ordinal);
        int wavePrewarmIndex = gameManagers.IndexOf(
            "PrewarmWaveAsync(",
            explicitWaveIndex,
            StringComparison.Ordinal);
        int remoteWaitIndex = gameManagers.IndexOf(
            "await WaitForRemoteMonsterPrewarmAcksAsync(preparePrewarmRound)",
            wavePrewarmIndex,
            StringComparison.Ordinal);
        int timerIndex = gameManagers.IndexOf(
            "phaseTimer = TickTimer.CreateFromSeconds",
            remoteWaitIndex,
            StringComparison.Ordinal);
        Assert.That(explicitWaveIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(wavePrewarmIndex, Is.GreaterThan(explicitWaveIndex));
        Assert.That(remoteWaitIndex, Is.GreaterThan(wavePrewarmIndex));
        Assert.That(timerIndex, Is.GreaterThan(remoteWaitIndex));
        Assert.That(gameManagers, Does.Contain("LocalMonsterPrewarmTimeoutSeconds = 20f"));
        Assert.That(gameManagers, Does.Contain("RemoteMonsterPrewarmAckTimeoutSeconds = 22f"));
        Assert.That(gameManagers, Does.Contain("Application.isBatchMode"));
        Assert.That(gameManagers, Does.Contain("Prepare will continue"));
        Assert.That(gameManagers, Does.Contain("currentRound != preparePrewarmRound"));
        Assert.That(gameManagers, Does.Contain("CancellationTokenSource.CreateLinkedTokenSource"));
        Assert.That(gameManagers, Does.Contain("timeoutCts.CancelAfter"));
        Assert.That(gameManagers, Does.Not.Contain("prewarmTask.Timeout"),
            "the timeout must cancel the source operation, not only abandon its await");

        int applyIndex = player.IndexOf(
            "ApplyAttackMonsterPoolEntries(revision, syncedPool, schedulePrewarm: false)",
            StringComparison.Ordinal);
        int awaitedClientPrewarmIndex = player.IndexOf(
            "await PrewarmAppliedAttackMonsterPoolAsync(",
            applyIndex,
            StringComparison.Ordinal);
        int reportIndex = player.IndexOf(
            "RPC_ReportMonsterPrewarmComplete(",
            awaitedClientPrewarmIndex,
            StringComparison.Ordinal);
        Assert.That(applyIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(awaitedClientPrewarmIndex, Is.GreaterThan(applyIndex));
        Assert.That(reportIndex, Is.GreaterThan(awaitedClientPrewarmIndex));
        Assert.That(player, Does.Contain("Object.InputAuthority != info.Source"),
            "State Authority must validate the reporting peer against durable player ownership");
        Assert.That(player, Does.Contain("RpcSources.All, RpcTargets.StateAuthority"));
        Assert.That(player, Does.Contain("int prepareRound"),
            "the client must use the authority-stamped round for its next-wave warmup");
        Assert.That(player, Does.Contain("BeginAttackMonsterPrewarmGeneration"));
        Assert.That(player, Does.Contain("ThrowIfAttackMonsterPrewarmGenerationStale"));

        int addressableAwait = spawner.IndexOf(
            "await AssetLoader.LoadAssetAsync<GameObject>",
            StringComparison.Ordinal);
        int postAddressableCancellation = spawner.IndexOf(
            "cancellationToken.ThrowIfCancellationRequested()",
            addressableAwait,
            StringComparison.Ordinal);
        int presentationAwait = spawner.IndexOf(
            "await FirstSpawnPresentationPrewarmer.WarmPrefabAsync",
            postAddressableCancellation,
            StringComparison.Ordinal);
        int postPresentationCancellation = spawner.IndexOf(
            "cancellationToken.ThrowIfCancellationRequested()",
            presentationAwait,
            StringComparison.Ordinal);
        int providerPrewarm = spawner.IndexOf(
            "provider.PrewarmPrefab",
            postPresentationCancellation,
            StringComparison.Ordinal);
        Assert.That(addressableAwait, Is.GreaterThanOrEqualTo(0));
        Assert.That(postAddressableCancellation, Is.GreaterThan(addressableAwait));
        Assert.That(presentationAwait, Is.GreaterThan(postAddressableCancellation));
        Assert.That(postPresentationCancellation, Is.GreaterThan(presentationAwait));
        Assert.That(providerPrewarm, Is.GreaterThan(postPresentationCancellation));
        Assert.That(spawner, Does.Contain("cancellationToken: GetBattleCancellationToken(battleGeneration)"));
    }

    [Test]
    public void MonsterSpawnPathsRequestPrewarmBeforeRuntimeSpawn()
    {
        string spawner = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/Game/Monsters/MonsterSpawner.cs");
        string player = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/Managers/PlayerManager.cs");
        string command = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/Commands/Battle/BattleSpawnMonsterCommand.cs");

        Assert.That(spawner, Does.Contain("await PrewarmWaveAsync("));
        Assert.That(spawner, Does.Contain("await PrewarmAttackMonsterPoolAsync("));
        Assert.That(player, Does.Contain("PrewarmAttackMonsterPoolIfPossible(\"RefreshAttackMonsterPool\")"));
        Assert.That(player, Does.Contain("PrewarmAttackMonsterPoolIfPossible(\"ApplyAttackMonsterPoolEntries\")"));

        int prewarmIndex = command.IndexOf("PrewarmMonsterDataAsync", StringComparison.Ordinal);
        int consumeIndex = command.IndexOf("TryReserveBattleSpawnResource", StringComparison.Ordinal);
        Assert.That(prewarmIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(consumeIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(consumeIndex, Is.LessThan(prewarmIndex), "pool/black-magic resource must be reserved before the async prewarm");
    }

    [Test]
    public void MonsterSpawnSnapsPooledNetworkTransformBeforePathing()
    {
        string spawner = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/Game/Monsters/MonsterSpawner.cs");
        string monster = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/Game/Monsters/Monster.cs");

        Assert.That(spawner, Does.Contain("SnapSpawnTransform(monsterGO, adjustedSpawnPos"));
        Assert.That(spawner, Does.Contain("networkTransform.Teleport(position, rotation)"));
        Assert.That(spawner, Does.Contain("bottom < 0f ? -bottom : 0f"));
        Assert.That(spawner, Does.Contain("GetPlayerIdForLog(_playerManager)"));
        Assert.That(spawner, Does.Not.Contain("_playerManager?.playerId"));
        Assert.That(monster, Does.Contain("ResetTransientRuntimeStateForReuse();"));
        Assert.That(monster, Does.Contain("StopAllCoroutines();"));
    }
}
#endif
