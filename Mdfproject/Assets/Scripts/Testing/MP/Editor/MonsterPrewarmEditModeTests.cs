#if UNITY_EDITOR
using System;
using System.IO;
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
    public void MonsterSpawnPathsRequestPrewarmBeforeRuntimeSpawn()
    {
        string spawner = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/Game/Monsters/MonsterSpawner.cs");
        string player = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/Managers/PlayerManager.cs");
        string command = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/Commands/Battle/BattleSpawnMonsterCommand.cs");

        Assert.That(spawner, Does.Contain("await PrewarmWaveAsync(waveData"));
        Assert.That(spawner, Does.Contain("await PrewarmAttackMonsterPoolAsync(pool"));
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
