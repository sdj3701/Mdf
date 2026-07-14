#if UNITY_EDITOR
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class FieldManagerUnitPresentationEditModeTests
{
    private const BindingFlags InstancePrivate = BindingFlags.Instance | BindingFlags.NonPublic;

    [Test]
    public void RepairPlacedUnitPresentationRestoresAliveVisualAndWallHeight()
    {
        var fieldGo = new GameObject("field");
        var groundGo = new GameObject("ground");
        var unitGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
        var wallGo = new GameObject("wall");
        var unitData = ScriptableObject.CreateInstance<UnitData>();

        try
        {
            var field = fieldGo.AddComponent<FieldManager>();
            field.gridOrigin = Vector3.zero;
            field.gridSize = new Vector2Int(3, 3);
            field.cellSize = 1f;
            SetPrivateField(field, "wallYOffset", 2f);
            SetPrivateField(field, "<ground3D>k__BackingField", groundGo);

            var unit = unitGo.AddComponent<Unit>();
            unitData.unitName = "test_ranged";
            unitData.unitType = UnitType.Ranged;
            SetPrivateField(unit, "unitData", unitData);

            var cell = new Vector3Int(0, 1, 0);
            unitGo.transform.position = new Vector3(0.5f, 0f, 1.5f);

            var renderer = unitGo.GetComponent<Renderer>();
            var collider = unitGo.GetComponent<Collider>();
            renderer.enabled = false;
            collider.enabled = false;

            SetPrivateField(field, "placedUnits", new Dictionary<Vector3Int, Unit>
            {
                { cell, unit }
            });
            SetPrivateField(field, "placedWalls", new Dictionary<Vector3Int, DestructibleWall>
            {
                { cell, wallGo.AddComponent<DestructibleWall>() }
            });

            field.RepairPlacedUnitPresentation("editmode-test");

            Assert.That(renderer.enabled, Is.True);
            Assert.That(collider.enabled, Is.True);
            Assert.That(unitGo.transform.position, Is.EqualTo(new Vector3(0.5f, 2f, 1.5f)));
        }
        finally
        {
            Object.DestroyImmediate(unitData);
            Object.DestroyImmediate(wallGo);
            Object.DestroyImmediate(unitGo);
            Object.DestroyImmediate(groundGo);
            Object.DestroyImmediate(fieldGo);
        }
    }

    [Test]
    public void RebuildUnitMapAfterMigrationDoesNotRepairPresentationByDefault()
    {
        var fieldGo = new GameObject("field");
        var groundGo = new GameObject("ground");
        var unitGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
        var unitData = ScriptableObject.CreateInstance<UnitData>();

        try
        {
            var field = fieldGo.AddComponent<FieldManager>();
            field.gridOrigin = Vector3.zero;
            field.gridSize = new Vector2Int(3, 3);
            field.cellSize = 1f;
            SetPrivateField(field, "<ground3D>k__BackingField", groundGo);

            var unit = unitGo.AddComponent<Unit>();
            unitData.unitName = "test_roster_read";
            unitData.unitType = UnitType.Ranged;
            SetPrivateField(unit, "unitData", unitData);

            var cell = new Vector3Int(0, 1, 0);
            unitGo.transform.position = new Vector3(0.5f, 0f, 1.5f);

            var renderer = unitGo.GetComponent<Renderer>();
            renderer.enabled = false;

            SetPrivateField(field, "placedUnits", new Dictionary<Vector3Int, Unit>
            {
                { cell, unit }
            });

            Assert.That(field.RebuildUnitMapAfterMigration("editmode-read", false, out _), Is.True);

            Assert.That(renderer.enabled, Is.False);
        }
        finally
        {
            Object.DestroyImmediate(unitData);
            Object.DestroyImmediate(unitGo);
            Object.DestroyImmediate(groundGo);
            Object.DestroyImmediate(fieldGo);
        }
    }

    [Test]
    public void WallRemovePanelAnchorsToLogicalGridCellWhenWallTransformDrifts()
    {
        var fieldGo = new GameObject("field");
        var wallGo = new GameObject("wall");
        var panelGo = new GameObject("panel");

        try
        {
            var field = fieldGo.AddComponent<FieldManager>();
            field.gridOrigin = Vector3.zero;
            field.gridSize = new Vector2Int(4, 4);
            field.cellSize = 1f;
            SetPrivateField(field, "wallYOffset", 0.4f);

            wallGo.transform.position = new Vector3(99f, 12f, -50f);

            var controller = panelGo.AddComponent<WallRemovePanelController>();
            var cell = new Vector3Int(2, 1, 0);
            SetPrivateField(controller, "_currentWall", wallGo);
            SetPrivateField(controller, "_fieldManager", field);
            SetPrivateField(controller, "_wallGridPosition", cell);

            var method = typeof(WallRemovePanelController).GetMethod("GetAnchorWorldPosition", InstancePrivate);
            Assert.That(method, Is.Not.Null);

            var anchor = (Vector3)method.Invoke(controller, null);

            Assert.That(anchor.x, Is.EqualTo(3f).Within(0.0001f));
            Assert.That(anchor.y, Is.EqualTo(2.2f).Within(0.0001f));
            Assert.That(anchor.z, Is.EqualTo(1.5f).Within(0.0001f));
        }
        finally
        {
            Object.DestroyImmediate(panelGo);
            Object.DestroyImmediate(wallGo);
            Object.DestroyImmediate(fieldGo);
        }
    }

    [Test]
    public void GoalCellIsReservedForKingAcrossPlacementCreationAiAndMigration()
    {
        var fieldGo = new GameObject("king-goal-reservation-field");
        var unitGo = new GameObject("goal-registration-probe");
        var unitData = ScriptableObject.CreateInstance<UnitData>();

        try
        {
            var field = fieldGo.AddComponent<FieldManager>();
            field.gridOrigin = Vector3.zero;
            field.gridSize = new Vector2Int(3, 3);
            field.cellSize = 1f;
            var placement = fieldGo.GetComponent<PlacementManager>();
            SetPrivateField(field, "placementManager", placement);
            SetPrivateField(placement, "fieldManager", field);
            unitData.unitName = "goal_reservation_probe";
            unitData.unitType = UnitType.Ranged;
            unitData.prefabsByStarLevel = new[] { "must_not_be_loaded" };

            Vector3Int goalCell = field.GetGoalGridPosition();
            Assert.That(goalCell, Is.EqualTo(new Vector3Int(1, 1, 0)));
            Assert.That(field.IsGoalCell(goalCell), Is.True);
            Assert.That(field.IsRegularUnitPlacementCell(goalCell), Is.False);
            Assert.That(placement.IsPositionValidForPlacement(goalCell, unitData), Is.False);

            FieldManager.UnitPlacementResult creation = field
                .TryCreateUnitAtAsync(unitData, goalCell, 1)
                .GetAwaiter()
                .GetResult();
            Assert.That(creation.Succeeded, Is.False);
            Assert.That(creation.FailureReason, Is.EqualTo("unit_goal_cell_blocked"));
            Assert.That(field.HasPendingUnitAt(goalCell), Is.False);

            List<Vector3Int> aiCandidates = field.GetValidPlacementTiles(UnitType.Melee);
            Assert.That(aiCandidates, Has.Count.EqualTo(8));
            Assert.That(aiCandidates.Contains(goalCell), Is.False);

            var unit = unitGo.AddComponent<Unit>();
            SetPrivateField(unit, "unitData", unitData);
            field.RegisterUnitAt(unit, goalCell);
            Assert.That(field.GetUnitAt(goalCell), Is.Null);

            MethodInfo legalMove = typeof(FieldManager).GetMethod(
                "IsLegalMoveDestinationForUnit",
                InstancePrivate);
            Assert.That(legalMove, Is.Not.Null);
            object[] legalMoveArgs = { unitData, goalCell, null };
            Assert.That((bool)legalMove.Invoke(field, legalMoveArgs), Is.False);
            Assert.That(legalMoveArgs[2], Is.EqualTo("unit_goal_cell_blocked"));

            LogAssert.Expect(
                LogType.Error,
                new System.Text.RegularExpressions.Regex("unit_goal_cell_blocked_in_snapshot"));
            bool restoreAccepted = field.RestoreFieldUnitsAfterHostMigration(
                new[] { unitData },
                new[] { unitData.name },
                new[] { 1 },
                new[] { goalCell.x, goalCell.y, goalCell.z },
                "goal-reservation-test");
            Assert.That(restoreAccepted, Is.False);
            Assert.That(field.IsHostMigrationUnitRestoreTerminal(out bool restoreSucceeded, out string restoreReason), Is.True);
            Assert.That(restoreSucceeded, Is.False);
            Assert.That(restoreReason, Is.EqualTo("unit_goal_cell_blocked_in_snapshot"));

            field.gridSize = Vector2Int.one;
            Assert.That(field.FindFirstEmptySlot(unitData), Is.Null);
        }
        finally
        {
            Object.DestroyImmediate(unitData);
            Object.DestroyImmediate(unitGo);
            Object.DestroyImmediate(fieldGo);
        }
    }

    [Test]
    public void GoalCellFallsBackToFieldCenterWhenGoalMarkerIsOutsideTheGrid()
    {
        var fieldGo = new GameObject("goal-fallback-field");
        var playerGo = new GameObject("goal-fallback-player");
        var goalGo = new GameObject("stale-goal-marker");

        try
        {
            var field = fieldGo.AddComponent<FieldManager>();
            field.gridOrigin = new Vector3(10f, 0f, 20f);
            field.gridSize = new Vector2Int(5, 3);
            field.cellSize = 1f;
            var player = playerGo.AddComponent<PlayerManager>();
            goalGo.transform.position = new Vector3(-100f, 0f, 500f);
            SetPrivateField(player, "<goalTransform>k__BackingField", goalGo.transform);
            field.playerManager = player;

            Assert.That(field.GetGoalGridPosition(), Is.EqualTo(new Vector3Int(2, 1, 0)));
        }
        finally
        {
            Object.DestroyImmediate(goalGo);
            Object.DestroyImmediate(playerGo);
            Object.DestroyImmediate(fieldGo);
        }
    }

    [Test]
    public void UnitMapRebuildRelocatesLegacyGoalOccupantWithoutLosingSnapshotIdentity()
    {
        var fieldGo = new GameObject("legacy-goal-rebuild-field");
        var unitGo = new GameObject("legacy-goal-unit");
        var unitData = ScriptableObject.CreateInstance<UnitData>();

        try
        {
            var field = fieldGo.AddComponent<FieldManager>();
            field.gridOrigin = Vector3.zero;
            field.gridSize = new Vector2Int(3, 3);
            field.cellSize = 1f;
            var unit = unitGo.AddComponent<Unit>();
            unitData.name = "UnitData_LegacyGoalProbe";
            unitData.unitName = "legacy_goal_probe";
            unitData.unitType = UnitType.Ranged;
            SetPrivateField(unit, "unitData", unitData);

            Vector3Int goalCell = field.GetGoalGridPosition();
            unitGo.transform.position = field.GridToWorld(goalCell);
            SetPrivateField(field, "placedUnits", new Dictionary<Vector3Int, Unit>
            {
                { goalCell, unit }
            });

            Assert.That(field.RebuildUnitMapAfterMigration("legacy-goal-test", false, out string summary), Is.True, summary);
            Assert.That(field.GetUnitAt(goalCell), Is.Null);
            Vector3Int relocatedCell = new Vector3Int(0, 0, 0);
            Assert.That(field.GetUnitAt(relocatedCell), Is.SameAs(unit));
            Assert.That(unitGo.transform.position, Is.EqualTo(field.GridToWorld(relocatedCell)));
            Assert.That(unit.Data, Is.SameAs(unitData));
            Assert.That(unit.UnitDataKeyForRoster, Is.EqualTo(unitData.name));
            Assert.That(unit.StarLevelForRoster, Is.EqualTo(1));
        }
        finally
        {
            Object.DestroyImmediate(unitData);
            Object.DestroyImmediate(unitGo);
            Object.DestroyImmediate(fieldGo);
        }
    }

    [Test]
    public void UnitMapRebuildFailsAtomicallyWhenGoalIsTheOnlyCell()
    {
        var fieldGo = new GameObject("blocked-goal-rebuild-field");
        var unitGo = new GameObject("blocked-goal-unit");
        var unitData = ScriptableObject.CreateInstance<UnitData>();

        try
        {
            var field = fieldGo.AddComponent<FieldManager>();
            field.gridOrigin = Vector3.zero;
            field.gridSize = Vector2Int.one;
            field.cellSize = 1f;
            var unit = unitGo.AddComponent<Unit>();
            unitData.unitName = "blocked_goal_probe";
            unitData.unitType = UnitType.Ranged;
            SetPrivateField(unit, "unitData", unitData);

            Vector3Int goalCell = field.GetGoalGridPosition();
            Vector3 originalWorldPosition = field.GridToWorld(goalCell);
            unitGo.transform.position = originalWorldPosition;
            SetPrivateField(field, "placedUnits", new Dictionary<Vector3Int, Unit>
            {
                { goalCell, unit }
            });

            Assert.That(field.RebuildUnitMapAfterMigration("blocked-goal-test", false, out string summary), Is.False, summary);
            Assert.That(summary, Does.Contain("unresolvedGoalConflicts=1"));
            Assert.That(field.GetUnitAt(goalCell), Is.SameAs(unit), "A failed rebuild must preserve the live roster instead of committing a partial map.");
            Assert.That(unitGo.transform.position, Is.EqualTo(originalWorldPosition));

            LogAssert.Expect(
                LogType.Error,
                new System.Text.RegularExpressions.Regex(
                    "^\\[UnitFlow-Migration\\] Refusing to capture field units because the authoritative rebuild failed:"));
            Assert.That(field.TryGetFieldUnitSnapshot(out _, out _, out _, out _), Is.False);
        }
        finally
        {
            Object.DestroyImmediate(unitData);
            Object.DestroyImmediate(unitGo);
            Object.DestroyImmediate(fieldGo);
        }
    }

    [Test]
    public void GoalReservationIsEnforcedByUiCommandsAndAuthorityValidator()
    {
        MethodInfo moveExecute = typeof(MoveUnitCommand).GetMethod("Execute", BindingFlags.Instance | BindingFlags.Public);
        MethodInfo swapExecute = typeof(SwapUnitCommand).GetMethod("Execute", BindingFlags.Instance | BindingFlags.Public);
        MethodInfo validateMove = typeof(PlayerCommandRequestValidator).GetMethod("ValidateMoveUnitRequest", InstancePrivate);
        MethodInfo validateSwap = typeof(PlayerCommandRequestValidator).GetMethod("ValidateSwapUnitRequest", InstancePrivate);

        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(
            typeof(PlacementManager), typeof(FieldManager), nameof(FieldManager.IsGoalCell)), Is.True);
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(
            moveExecute, typeof(FieldManager), nameof(FieldManager.IsGoalCell)), Is.True);
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(
            swapExecute, typeof(FieldManager), nameof(FieldManager.IsGoalCell)), Is.True);
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(
            validateMove, typeof(FieldManager), nameof(FieldManager.IsGoalCell)), Is.True);
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(
            validateSwap, typeof(FieldManager), nameof(FieldManager.IsGoalCell)), Is.True);
    }

    [Test]
    public void OnlyPlayerPlacedPermanentWallsAreExposedAsRemovableWallObjects()
    {
        var fieldGo = new GameObject("field");
        var structuralWall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        var playerWall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        var destructibleWallGo = GameObject.CreatePrimitive(PrimitiveType.Cube);

        try
        {
            var field = fieldGo.AddComponent<FieldManager>();
            var structuralCell = new Vector3Int(0, 0, 0);
            var playerCell = new Vector3Int(1, 0, 0);
            var destructibleCell = new Vector3Int(2, 0, 0);
            var destructibleWall = destructibleWallGo.AddComponent<DestructibleWall>();

            SetPrivateField(field, "placedPermanentWalls", new Dictionary<Vector3Int, GameObject>
            {
                { structuralCell, structuralWall },
                { playerCell, playerWall }
            });
            SetPrivateField(field, "placedWalls", new Dictionary<Vector3Int, DestructibleWall>
            {
                { destructibleCell, destructibleWall }
            });

            var playerPlacedCellsField = typeof(FieldManager).GetField(
                "playerPlacedPermanentWallCells",
                InstancePrivate);
            Assert.That(playerPlacedCellsField, Is.Not.Null);
            var playerPlacedCells = (HashSet<Vector3Int>)playerPlacedCellsField.GetValue(field);
            playerPlacedCells.Add(playerCell);

            Assert.That(field.GetRemovableWallObjectAt(structuralCell), Is.Null);
            Assert.That(field.HasRemovableWallAt(structuralCell), Is.False);
            Assert.That(field.GetRemovableWallObjectAt(playerCell), Is.SameAs(playerWall));
            Assert.That(field.HasRemovableWallAt(playerCell), Is.True);
            Assert.That(field.GetRemovableWallObjectAt(destructibleCell), Is.SameAs(destructibleWallGo));
            Assert.That(field.HasRemovableWallAt(destructibleCell), Is.True);
        }
        finally
        {
            Object.DestroyImmediate(destructibleWallGo);
            Object.DestroyImmediate(playerWall);
            Object.DestroyImmediate(structuralWall);
            Object.DestroyImmediate(fieldGo);
        }
    }

    [Test]
    public void WallRemovePanelRoutesBothWallKindsThroughAuthorityCommandOnly()
    {
        Assert.That(typeof(WallRemovePanelController).GetMethod(
            "Bind",
            BindingFlags.Instance | BindingFlags.Public,
            null,
            new[] { typeof(GameObject), typeof(Vector3Int), typeof(FieldManager) },
            null), Is.Not.Null);
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(
            typeof(WallRemovePanelController), typeof(FieldManager), "GetRemovableWallObjectAt"), Is.True);
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(
            typeof(WallRemovePanelController), typeof(CommandProcessor), "RequestCommandExecution"), Is.True);
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(
            typeof(WallRemovePanelController), typeof(FieldManager), "RemoveWallAt"), Is.False);
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(
            typeof(WallRemovePanelController), typeof(PlayerManager), "ReturnWall"), Is.False);
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(
            typeof(WallRemovePanelController), typeof(PlayerManager), "ReturnPermanentWallPlacement"), Is.False);
    }

    private static void SetPrivateField<TTarget, TValue>(TTarget target, string fieldName, TValue value)
    {
        var field = typeof(TTarget).GetField(fieldName, InstancePrivate);
        Assert.That(field, Is.Not.Null, fieldName);
        field.SetValue(target, value);
    }
}
#endif
