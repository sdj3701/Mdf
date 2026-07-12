#if UNITY_EDITOR
using System;
using System.IO;
using NUnit.Framework;

public sealed class MonsterPrewarmEditModeTests
{
    [Test]
    public void PooledNetworkObjectProviderSupportsExplicitPrewarm()
    {
        string source = File.ReadAllText("Assets/Scripts/Network/PooledNetworkObjectProvider.cs");

        Assert.That(source, Does.Contain("public int PrewarmPrefab(NetworkRunner runner, NetworkObject prefab, int targetFreeCount)"));
        Assert.That(source, Does.Contain("public int GetFreeCount(NetworkPrefabId prefabId)"));
        Assert.That(source, Does.Contain("GetOrCreateQueue(prefabId)"));
        Assert.That(source, Does.Contain("\"[PooledNetworkObjects]\""));
    }

    [Test]
    public void MonsterSpawnPathsRequestPrewarmBeforeRuntimeSpawn()
    {
        string spawner = File.ReadAllText("Assets/Scripts/Game/Monsters/MonsterSpawner.cs");
        string player = File.ReadAllText("Assets/Scripts/Managers/PlayerManager.cs");
        string command = File.ReadAllText("Assets/Scripts/Commands/Battle/BattleSpawnMonsterCommand.cs");

        Assert.That(spawner, Does.Contain("await PrewarmWaveAsync(waveData"));
        Assert.That(spawner, Does.Contain("await PrewarmAttackMonsterPoolAsync(pool"));
        Assert.That(player, Does.Contain("PrewarmAttackMonsterPoolIfPossible(\"RefreshAttackMonsterPool\")"));
        Assert.That(player, Does.Contain("PrewarmAttackMonsterPoolIfPossible(\"ApplyAttackMonsterPoolEntries\")"));

        int prewarmIndex = command.IndexOf("PrewarmMonsterDataAsync", StringComparison.Ordinal);
        int consumeIndex = command.IndexOf("TryConsumeMonsterPoolSlot", StringComparison.Ordinal);
        Assert.That(prewarmIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(consumeIndex, Is.LessThan(prewarmIndex), "pool slot must be reserved before the async prewarm");
    }

    [Test]
    public void MonsterSpawnSnapsPooledNetworkTransformBeforePathing()
    {
        string spawner = File.ReadAllText("Assets/Scripts/Game/Monsters/MonsterSpawner.cs");
        string monster = File.ReadAllText("Assets/Scripts/Game/Monsters/Monster.cs");

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
