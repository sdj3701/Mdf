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
        Assert.That(spawner, Does.Contain("allowNearbyCellFallback: false"));
        Assert.That(spawner, Does.Contain("if (!allowNearbyCellFallback)"));
        Assert.That(spawner, Does.Contain("ResolveSeparatedSpawnPosition"),
            "non-strategic/base-wave spawns must retain their existing separation policy");
        Assert.That(battleCommand.IndexOf("TryConsumeMonsterPoolSlot", System.StringComparison.Ordinal),
            Is.LessThan(battleCommand.IndexOf("PrewarmMonsterDataAsync", System.StringComparison.Ordinal)));
        Assert.That(battleCommand.IndexOf("_reservedPoolRevision = _validatedAttacker.AttackMonsterPoolRevision", System.StringComparison.Ordinal),
            Is.LessThan(battleCommand.IndexOf("PrewarmMonsterDataAsync", System.StringComparison.Ordinal)));
        Assert.That(battleCommand.IndexOf("TryResolveExactBattleSpawnPosition", StringComparison.Ordinal),
            Is.LessThan(battleCommand.IndexOf("TryConsumeMonsterPoolSlot", StringComparison.Ordinal)),
            "authority must reject an invalid exact spawn cell before reserving the pool slot");
        Assert.That(battleCommand, Does.Contain("SpawnMonsterAtExactPositionAsync"));
        Assert.That(battleCommand, Does.Contain("spawn_cell_changed_during_await"));
        Assert.That(monster, Does.Contain("IsSpawnLifecycleCurrent"));
        Assert.That(monster, Does.Contain("public override void Despawned"));
        Assert.That(monster, Does.Contain("GameEvents.OnWallDestroyed -= OnWallDestroyed"));
    }

    [Test]
    public void ExactBattleSpawnResolutionKeepsTheClickedNavigationCell()
    {
        var fieldObject = new GameObject("ExactBattleSpawnField");
        try
        {
            FieldManager field = fieldObject.AddComponent<FieldManager>();
            field.gridOrigin = Vector3.zero;
            field.cellSize = 1f;
            field.gridSize = new Vector2Int(10, 9);

            Vector3 requested = new Vector3(-0.2f, 0.25f, 0.2f);
            Assert.That(
                BattleCommandValidator.TryResolveExactBattleSpawnPosition(
                    field,
                    requested,
                    out Vector2Int navigationCell,
                    out Vector3 exactPosition,
                    out string errorCode),
                Is.True,
                errorCode);
            Assert.That(navigationCell, Is.EqualTo(new Vector2Int(1, 2)));
            Assert.That(exactPosition, Is.EqualTo(new Vector3(-0.5f, 0.25f, 0.5f)));
            Assert.That(field.WorldToNavigationCell(requested), Is.EqualTo(navigationCell));
            Assert.That(field.WorldToNavigationCell(exactPosition), Is.EqualTo(navigationCell));

            Vector3 outside = new Vector3(field.TotalGridOrigin.x - 0.1f, 0f, 0f);
            Assert.That(
                BattleCommandValidator.TryResolveExactBattleSpawnPosition(
                    field,
                    outside,
                    out _,
                    out _,
                    out errorCode),
                Is.False);
            Assert.That(errorCode, Is.EqualTo("spawn_cell_outside_navigation_grid"));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(fieldObject);
        }
    }

    [Test]
    public void ExactBattleSpawnResolutionRejectsWallAndLivingMonsterCells()
    {
        var fieldObject = new GameObject("BlockedExactBattleSpawnField");
        try
        {
            FieldManager field = fieldObject.AddComponent<FieldManager>();
            field.gridOrigin = Vector3.zero;
            field.cellSize = 1f;
            field.gridSize = new Vector2Int(10, 9);

            FieldInfo permanentWallsField = typeof(FieldManager).GetField(
                "placedPermanentWalls",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(permanentWallsField, Is.Not.Null);
            var permanentWalls = (Dictionary<Vector3Int, GameObject>)permanentWallsField.GetValue(field);
            var wall = new GameObject("AuthorityWallAtSpawnCell");
            wall.transform.SetParent(fieldObject.transform);
            permanentWalls[new Vector3Int(0, 0, 0)] = wall;

            Assert.That(
                BattleCommandValidator.TryResolveExactBattleSpawnPosition(
                    field,
                    new Vector3(0.5f, 0f, 0.5f),
                    out _,
                    out _,
                    out string errorCode),
                Is.False);
            Assert.That(errorCode, Is.EqualTo("spawn_cell_blocked_by_wall"));

            PlayerManager player = fieldObject.AddComponent<PlayerManager>();
            MonsterSpawner spawner = fieldObject.AddComponent<MonsterSpawner>();
            var monsterParentObject = new GameObject("DefenderMonsterParent");
            monsterParentObject.transform.SetParent(fieldObject.transform);
            field.playerManager = player;
            player.fieldManager = field;
            player.monsterSpawner = spawner;
            spawner.monsterParent = monsterParentObject.transform;

            var monsterObject = new GameObject("LivingMonsterInClickedCell");
            monsterObject.transform.SetParent(monsterParentObject.transform);
            monsterObject.transform.position = new Vector3(-0.5f, 0f, 0.5f);
            Monster monster = monsterObject.AddComponent<Monster>();
            monster.currentHP = 10f;

            Assert.That(
                BattleCommandValidator.TryResolveExactBattleSpawnPosition(
                    field,
                    new Vector3(-0.2f, 0f, 0.2f),
                    out _,
                    out _,
                    out errorCode),
                Is.False);
            Assert.That(errorCode, Is.EqualTo("spawn_cell_occupied"));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(fieldObject);
        }
    }

    [Test]
    public void StrategicBattleSpawnUsesStrictSpawnerWithoutNearbyFallback()
    {
        Assert.That(
            MdfCompiledCodePolicy.ReferencesMethod(
                typeof(BattleSpawnMonsterCommand),
                typeof(BattleCommandValidator),
                nameof(BattleCommandValidator.TryResolveExactBattleSpawnPosition)),
            Is.True);
        Assert.That(
            MdfCompiledCodePolicy.ReferencesMethod(
                typeof(BattleSpawnMonsterCommand),
                typeof(BattleCommandValidator),
                nameof(BattleCommandValidator.TryValidateBattleSpawnPath)),
            Is.True);
        Assert.That(
            MdfCompiledCodePolicy.ReferencesMethod(
                typeof(BattleSpawnMonsterCommand),
                typeof(MonsterSpawner),
                nameof(MonsterSpawner.SpawnMonsterAtExactPositionAsync)),
            Is.True);
        Assert.That(
            MdfCompiledCodePolicy.ReferencesMethod(
                typeof(BattleSpawnMonsterCommand),
                typeof(MonsterSpawner),
                nameof(MonsterSpawner.SpawnMonsterAtPositionAsync)),
            Is.False,
            "strategic command execution must not enter the nearby-cell fallback API directly");

        MethodInfo exactSpawner = typeof(MonsterSpawner).GetMethod(
            nameof(MonsterSpawner.SpawnMonsterAtExactPositionAsync),
            BindingFlags.Instance | BindingFlags.Public);
        Assert.That(exactSpawner, Is.Not.Null);
        Assert.That(
            MdfCompiledCodePolicy.ReferencesMethod(
                exactSpawner,
                typeof(MonsterSpawner),
                nameof(MonsterSpawner.SpawnMonsterAtPositionAsync)),
            Is.True);
        Assert.That(MdfCompiledCodePolicy.ContainsStringLiteral(typeof(BattleCommandValidator), "spawn_cell_blocked_by_wall"), Is.True);
        Assert.That(MdfCompiledCodePolicy.ContainsStringLiteral(typeof(BattleCommandValidator), "spawn_cell_occupied"), Is.True);
        Assert.That(MdfCompiledCodePolicy.ContainsStringLiteral(typeof(BattleCommandValidator), "spawn_cell_path_unavailable"), Is.True);
    }
}
