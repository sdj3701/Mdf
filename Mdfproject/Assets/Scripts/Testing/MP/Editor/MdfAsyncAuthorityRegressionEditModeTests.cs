using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

public class MdfAsyncAuthorityRegressionEditModeTests
{
    [Test]
    public void PooledUnitRejectsAsyncStampCapturedByPreviousLifecycleOrData()
    {
        var unitObject = new GameObject("UnitAsyncLifecycleGuardTest");
        UnitData firstData = ScriptableObject.CreateInstance<UnitData>();
        UnitData secondData = ScriptableObject.CreateInstance<UnitData>();
        try
        {
            Unit unit = unitObject.AddComponent<Unit>();
            FieldInfo dataField = typeof(Unit).GetField("unitData", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo generationField = typeof(Unit).GetField(
                "_combatTargetLifecycleGeneration",
                BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo capture = typeof(Unit).GetMethod(
                "CaptureAsyncLifecycle",
                BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo isCurrent = typeof(Unit).GetMethod(
                "IsAsyncLifecycleCurrent",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(dataField, Is.Not.Null);
            Assert.That(generationField, Is.Not.Null);
            Assert.That(capture, Is.Not.Null);
            Assert.That(isCurrent, Is.Not.Null);

            dataField.SetValue(unit, firstData);
            object firstLifecycleStamp = capture.Invoke(unit, null);
            Assert.That((bool)isCurrent.Invoke(unit, new[] { firstLifecycleStamp }), Is.True);

            dataField.SetValue(unit, secondData);
            Assert.That((bool)isCurrent.Invoke(unit, new[] { firstLifecycleStamp }), Is.False,
                "A result loaded for the previous pooled UnitData must not commit into the reused unit.");

            object secondLifecycleStamp = capture.Invoke(unit, null);
            int generation = (int)generationField.GetValue(unit);
            generationField.SetValue(unit, generation + 1);
            Assert.That((bool)isCurrent.Invoke(unit, new[] { secondLifecycleStamp }), Is.False,
                "Despawn/respawn generation changes must invalidate every previous async result.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(firstData);
            UnityEngine.Object.DestroyImmediate(secondData);
            UnityEngine.Object.DestroyImmediate(unitObject);
        }
    }

    [Test]
    public void CommandQueueAwaitsAsyncCommandsInSerializedFifoWorker()
    {
        MethodInfo asyncContract = typeof(IAsyncCommand).GetMethod(nameof(IAsyncCommand.ExecuteAsync));
        Assert.That(asyncContract, Is.Not.Null);
        Assert.That(asyncContract.ReturnType, Is.EqualTo(typeof(UniTask<CommandExecutionResult>)));
        Assert.That(asyncContract.GetParameters(), Has.Length.EqualTo(1));
        Assert.That(asyncContract.GetParameters()[0].ParameterType, Is.EqualTo(typeof(CancellationToken)));

        FieldInfo queue = typeof(CommandProcessor).GetField("_commandQueue", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(queue, Is.Not.Null);
        Assert.That(queue.FieldType.IsGenericType, Is.True);
        Assert.That(queue.FieldType.GetGenericTypeDefinition(), Is.EqualTo(typeof(Queue<>)));

        MethodInfo enqueue = typeof(CommandProcessor).GetMethod(nameof(CommandProcessor.ReceiveAndEnqueueCommand));
        Assert.That(enqueue, Is.Not.Null);
        Assert.That(enqueue.ReturnType, Is.EqualTo(typeof(void)), "the public enqueue boundary must stay synchronous and non-async-void");
        Assert.That(CommandProcessor.ShouldBroadcastCommandToClients((CommandType)100), Is.True);
        Assert.That(CommandProcessor.ShouldBroadcastCommandToClients((CommandType)299), Is.True);
        Assert.That(CommandProcessor.ShouldBroadcastCommandToClients((CommandType)99), Is.False);
        Assert.That(CommandProcessor.ShouldBroadcastCommandToClients((CommandType)300), Is.False);
    }

    [Test]
    public void PreviouslyAsyncVoidCommandsUseAwaitableContract()
    {
        Type[] commandTypes =
        {
            typeof(BuyUnitCommand),
            typeof(PlaceUnitCommand),
            typeof(RegisterUnitAtCommand),
            typeof(SyncAugmentsCommand),
            typeof(SyncShopItemsCommand)
        };

        foreach (Type commandType in commandTypes)
        {
            Assert.That(typeof(IAsyncCommand).IsAssignableFrom(commandType), Is.True, commandType.FullName);
            MethodInfo method = commandType.GetMethod(nameof(IAsyncCommand.ExecuteAsync), new[] { typeof(CancellationToken) });
            Assert.That(method, Is.Not.Null, commandType.FullName);
            Assert.That(method.ReturnType, Is.EqualTo(typeof(UniTask<CommandExecutionResult>)), commandType.FullName);

            MethodInfo execute = commandType.GetMethod(nameof(ICommand.Execute), Type.EmptyTypes);
            Assert.That(execute, Is.Not.Null, commandType.FullName);
            Assert.That(execute.ReturnType, Is.EqualTo(typeof(void)), commandType.FullName);
            Assert.That(execute.GetCustomAttribute<System.Runtime.CompilerServices.AsyncStateMachineAttribute>(), Is.Null,
                $"{commandType.Name}.Execute must not compile as async void");
        }
    }

    [Test]
    public void SkillActivationModeIsAuthorityCommandAndNetworkedState()
    {
        Assert.That(Enum.IsDefined(typeof(CommandType), nameof(CommandType.SetSkillActivationMode)), Is.True);
        Assert.That(typeof(SetSkillActivationModeCommand).GetInterfaces(), Does.Contain(typeof(ICommand)));

        MethodInfo validator = typeof(PlayerCommandRequestValidator).GetMethod(
            "ValidateSetSkillActivationModeRequest",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(validator, Is.Not.Null);
        MethodInfo dispatch = typeof(PlayerCommandRequestValidator).GetMethod(
            nameof(PlayerCommandRequestValidator.Validate),
            BindingFlags.Instance | BindingFlags.Public);
        Assert.That(dispatch, Is.Not.Null);

        PropertyInfo networkedMode = typeof(Unit).GetProperty(
            "NetworkedSkillActivationType",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(networkedMode, Is.Not.Null);
        Assert.That(networkedMode.GetCustomAttribute<Fusion.NetworkedAttribute>(), Is.Not.Null);

        MethodInfo authoritativeSetter = typeof(Unit).GetMethod(nameof(Unit.TrySetSkillActivationModeAuthoritative));
        Assert.That(authoritativeSetter, Is.Not.Null);
        Assert.That(authoritativeSetter.ReturnType, Is.EqualTo(typeof(bool)));

        FieldInfo selectedUnit = typeof(UnitDetailPanelController).GetField("currentUnit", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(selectedUnit, Is.Not.Null, "UI must keep a selected unit reference and submit the authority command through its request path");
    }

    [Test]
    public void BattleAndMonsterAwaitsAreGenerationGuarded()
    {
        string battleCommand = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/Commands/Battle/BattleSpawnMonsterCommand.cs");
        string spawner = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/Game/Monsters/MonsterSpawner.cs");
        string monster = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/Game/Monsters/Monster.cs");

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
