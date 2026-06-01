#if UNITY_EDITOR
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

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

            var cell = new Vector3Int(1, 1, 0);
            unitGo.transform.position = new Vector3(1.5f, 0f, 1.5f);

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
            Assert.That(unitGo.transform.position, Is.EqualTo(new Vector3(1.5f, 2f, 1.5f)));
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

            var cell = new Vector3Int(1, 1, 0);
            unitGo.transform.position = new Vector3(1.5f, 0f, 1.5f);

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

            var wall = wallGo.AddComponent<DestructibleWall>();
            wallGo.transform.position = new Vector3(99f, 12f, -50f);

            var controller = panelGo.AddComponent<WallRemovePanelController>();
            var cell = new Vector3Int(2, 1, 0);
            SetPrivateField(controller, "_currentWall", wall);
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

    private static void SetPrivateField<TTarget, TValue>(TTarget target, string fieldName, TValue value)
    {
        var field = typeof(TTarget).GetField(fieldName, InstancePrivate);
        Assert.That(field, Is.Not.Null, fieldName);
        field.SetValue(target, value);
    }
}
#endif
