using System.IO;
using NUnit.Framework;

public class MdfAsyncAuthorityRegressionEditModeTests
{
    [Test]
    public void CommandQueueAwaitsAsyncCommandsInSerializedFifoWorker()
    {
        string contract = File.ReadAllText("Assets/Scripts/Commands/Core/ICommand.cs");
        string processor = File.ReadAllText("Assets/Scripts/Commands/Core/CommandProcessor.cs");

        Assert.That(contract, Does.Contain("interface IAsyncCommand"));
        Assert.That(contract, Does.Contain("UniTask<CommandExecutionResult> ExecuteAsync(CancellationToken cancellationToken)"));
        Assert.That(processor, Does.Contain("Queue<PendingCommand>"));
        Assert.That(processor, Does.Contain("await DeserializeCommand("));
        Assert.That(processor, Does.Contain("await asyncCommand.ExecuteAsync(cancellationToken)"));
        Assert.That(processor, Does.Not.Contain("async void ReceiveAndEnqueueCommand"));
        Assert.That(processor, Does.Not.Contain("if (command is ActivateSkillCommand)"));
        Assert.That(processor, Does.Contain("ShouldBroadcastCommandToClients"));
        Assert.That(processor, Does.Contain("return value >= 100 && value < 300"));
    }

    [Test]
    public void PreviouslyAsyncVoidCommandsUseAwaitableContract()
    {
        string[] paths =
        {
            "Assets/Scripts/Commands/PlayerActions/BuyUnitCommand.cs",
            "Assets/Scripts/Commands/PlayerActions/PlaceUnitCommand.cs",
            "Assets/Scripts/Commands/Sync/RegisterUnitAtCommand.cs",
            "Assets/Scripts/Commands/Sync/SyncAugmentsCommand.cs",
            "Assets/Scripts/Commands/Sync/SyncShopItemsCommand.cs"
        };

        foreach (string path in paths)
        {
            string source = File.ReadAllText(path);
            Assert.That(source, Does.Contain("IAsyncCommand"), path);
            Assert.That(source, Does.Contain("ExecuteAsync(CancellationToken cancellationToken)"), path);
            Assert.That(source, Does.Not.Contain("async void Execute"), path);
        }
    }

    [Test]
    public void SkillActivationModeIsAuthorityCommandAndNetworkedState()
    {
        string processor = File.ReadAllText("Assets/Scripts/Commands/Core/CommandProcessor.cs");
        string player = File.ReadAllText("Assets/Scripts/Managers/PlayerManager.cs");
        string unit = File.ReadAllText("Assets/Scripts/Game/Units/Unit.cs");
        string ui = File.ReadAllText("Assets/Scripts/UI/UnitDetailPanelController.cs");

        Assert.That(processor, Does.Contain("CommandType.SetSkillActivationMode"));
        Assert.That(player, Does.Contain("ValidateSetSkillActivationModeRequest"));
        Assert.That(unit, Does.Contain("[Networked] private SkillActivationType NetworkedSkillActivationType"));
        Assert.That(unit, Does.Contain("TrySetSkillActivationModeAuthoritative"));
        Assert.That(ui, Does.Contain("new SetSkillActivationModeCommand"));
        Assert.That(ui, Does.Not.Contain("currentUnit.currentSkillActivationType ="));
    }

    [Test]
    public void BattleAndMonsterAwaitsAreGenerationGuarded()
    {
        string battleCommand = File.ReadAllText("Assets/Scripts/Commands/Battle/BattleSpawnMonsterCommand.cs");
        string spawner = File.ReadAllText("Assets/Scripts/Game/Monsters/MonsterSpawner.cs");
        string monster = File.ReadAllText("Assets/Scripts/Game/Monsters/Monster.cs");

        Assert.That(battleCommand, Does.Contain("battle_changed_during_spawn_prewarm"));
        Assert.That(battleCommand, Does.Contain("RevalidateAfterAwait"));
        Assert.That(battleCommand, Does.Contain("battle_pool_reservation_changed_during_await"));
        Assert.That(battleCommand, Does.Contain("AttackMonsterPoolRevision != _reservedPoolRevision"));
        Assert.That(battleCommand, Does.Contain("currentEntry.RemainingCount != _reservedRemainingCount"));
        Assert.That(spawner, Does.Contain("CaptureBattleGeneration"));
        Assert.That(spawner, Does.Contain("ABORT_STALE_BATTLE_GENERATION"));
        Assert.That(spawner, Does.Contain("ReturnUnspawnedBossesForBattleSequence"));
        Assert.That(battleCommand.IndexOf("TryConsumeMonsterPoolSlot", System.StringComparison.Ordinal),
            Is.LessThan(battleCommand.IndexOf("PrewarmMonsterDataAsync", System.StringComparison.Ordinal)));
        Assert.That(battleCommand.IndexOf("_reservedPoolRevision = _validatedAttacker.AttackMonsterPoolRevision", System.StringComparison.Ordinal),
            Is.LessThan(battleCommand.IndexOf("PrewarmMonsterDataAsync", System.StringComparison.Ordinal)));
        Assert.That(monster, Does.Contain("IsSpawnLifecycleCurrent"));
        Assert.That(monster, Does.Contain("public override void Despawned"));
        Assert.That(monster, Does.Contain("GameEvents.OnWallDestroyed -= OnWallDestroyed"));
    }
}
