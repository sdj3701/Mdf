using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public sealed class NavigationPerformanceEditModeTests
{
    private static readonly FieldInfo UnitDataField = typeof(Unit).GetField(
        "unitData",
        BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo BlockedMonstersField = typeof(Unit).GetField(
        "blockedMonsters",
        BindingFlags.Instance | BindingFlags.NonPublic);

    private readonly List<Object> _createdObjects = new List<Object>();

    [TearDown]
    public void TearDown()
    {
        for (int i = _createdObjects.Count - 1; i >= 0; i--)
        {
            if (_createdObjects[i] != null)
            {
                Object.DestroyImmediate(_createdObjects[i]);
            }
        }

        _createdObjects.Clear();
    }

    [Test]
    public void AstarCachePreservesLegacyTieBreakAndInvalidatesOnWallRevision()
    {
        GameObject fieldObject = Track(new GameObject("NavigationCacheField"));
        FieldManager field = fieldObject.AddComponent<FieldManager>();
        GameObject gridObject = Track(new GameObject("NavigationCacheGrid"));
        AstarGrid grid = gridObject.AddComponent<AstarGrid>();
        grid.useFieldManagerGrid = false;
        grid.fieldManager = field;
        grid.gridSize = new Vector2Int(3, 3);
        grid.startOffset = Vector2Int.zero;
        grid.wallLayers = 0;
        grid.breakableWallLayer = 0;
        grid.allowDiagonal = false;
        grid.dontCrossCorner = true;
        grid.showDebugInfo = false;
        grid.Initialize();

        Assert.That(grid.FindPath(Vector2Int.zero, new Vector2Int(2, 2)), Is.True);
        List<AstarNode> firstPath = grid.FinalPath;
        Assert.That(firstPath, Has.Count.EqualTo(5));
        Assert.That(new Vector2Int(firstPath[1].x, firstPath[1].y), Is.EqualTo(new Vector2Int(0, 1)),
            "equal F/H must retain the legacy north-before-east insertion tie-break");

        Assert.That(grid.FindPath(Vector2Int.zero, new Vector2Int(2, 2)), Is.True);
        Assert.That(grid.FinalPath, Is.SameAs(firstPath));
        Assert.That(ReadCounter(grid, "_pathCacheHitCount"), Is.EqualTo(1));
        Assert.That(ReadCounter(grid, "_wallTopologyRefreshCount"), Is.EqualTo(1));

        MethodInfo markTopologyChanged = typeof(FieldManager).GetMethod(
            "MarkWallTopologyChanged",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(markTopologyChanged, Is.Not.Null);
        markTopologyChanged.Invoke(field, null);

        Assert.That(grid.FindPath(Vector2Int.zero, new Vector2Int(2, 2)), Is.True);
        Assert.That(grid.FinalPath, Is.Not.SameAs(firstPath));
        Assert.That(ReadCounter(grid, "_pathCacheHitCount"), Is.EqualTo(1));
        Assert.That(ReadCounter(grid, "_wallTopologyRefreshCount"), Is.EqualTo(2));
    }

    [Test]
    public void OccupancyRegistryTracksMonsterCellMovesAndFindsNearbyMeleeBlocker()
    {
        GameObject fieldObject = Track(new GameObject("OccupancyField"));
        FieldManager field = fieldObject.AddComponent<FieldManager>();
        field.gridOrigin = Vector3.zero;
        field.cellSize = 1f;
        field.gridSize = new Vector2Int(10, 9);
        var registry = new FieldBattleOccupancyRegistry(field);

        GameObject monsterObject = Track(new GameObject("OccupancyMonster"));
        monsterObject.transform.position = new Vector3(-0.5f, 0f, 0.5f);
        Monster monster = monsterObject.AddComponent<Monster>();
        monster.currentHP = 100f;
        Vector2Int firstCell = field.WorldToNavigationCell(monsterObject.transform.position);
        registry.RegisterMonster(monster);
        Assert.That(registry.HasLivingMonsterAtCell(firstCell), Is.True);

        monsterObject.transform.position = new Vector3(0.5f, 0f, 0.5f);
        Vector2Int secondCell = field.WorldToNavigationCell(monsterObject.transform.position);
        Assert.That(registry.UpdateMonsterCell(monster), Is.True);
        Assert.That(registry.HasLivingMonsterAtCell(firstCell), Is.False);
        Assert.That(registry.HasLivingMonsterAtCell(secondCell), Is.True);

        GameObject unitObject = Track(new GameObject("OccupancyBlocker"));
        unitObject.transform.position = new Vector3(0.2f, 0f, 0.5f);
        Unit unit = unitObject.AddComponent<Unit>();
        UnitData unitData = Track(ScriptableObject.CreateInstance<UnitData>());
        unitData.unitType = UnitType.Melee;
        unitData.blockCount = 1;
        unitData.baseHealth = 100f;
        UnitDataField.SetValue(unit, unitData);
        unit.maxHP = 100f;
        unit.currentHP = 100f;
        registry.RegisterUnit(unit);

        Assert.That(
            registry.TryFindBlockingUnit(monster, monsterObject.transform.position, 0.6f, 0.6f, out Unit blocker),
            Is.True);
        Assert.That(blocker, Is.SameAs(unit));

        // A diagonal cell entry can be outside the exact 0.6 block distance and move inside it
        // later without crossing another cell. The registry must support both probes.
        unitObject.transform.position = new Vector3(0.5f, 0f, 0.5f);
        registry.UpdateUnitCell(unit);
        Assert.That(
            registry.TryFindBlockingUnit(monster, Vector3.zero, 0.8f, 0.6f, out _),
            Is.False);
        Assert.That(
            registry.TryFindBlockingUnit(monster, new Vector3(0.1f, 0f, 0.1f), 0.8f, 0.6f, out blocker),
            Is.True);
        Assert.That(blocker, Is.SameAs(unit));

        // Full block capacity is transient eligibility, not occupancy lifetime. A query while
        // full must not evict the unit, so releasing capacity makes it selectable immediately.
        GameObject occupyingMonsterObject = Track(new GameObject("OccupancyExistingBlockedMonster"));
        Monster occupyingMonster = occupyingMonsterObject.AddComponent<Monster>();
        var blockedMonsters = (List<Monster>)BlockedMonstersField.GetValue(unit);
        blockedMonsters.Add(occupyingMonster);
        Assert.That(unit.IsBlockingFull(), Is.True);
        Assert.That(
            registry.TryFindBlockingUnit(monster, unitObject.transform.position, 0.6f, 0.6f, out _),
            Is.False);
        Assert.That(registry.RegisteredUnitCount, Is.EqualTo(1));

        blockedMonsters.Remove(occupyingMonster);
        Assert.That(
            registry.TryFindBlockingUnit(monster, unitObject.transform.position, 0.6f, 0.6f, out blocker),
            Is.True);
        Assert.That(blocker, Is.SameAs(unit));

        registry.UnregisterMonster(monster);
        Assert.That(registry.HasLivingMonsterAtCell(secondCell), Is.False);
    }

    [Test]
    public void BlockerCandidateCacheReevaluatesDiagonalApproachInsideSameCell()
    {
        FieldManager field = CreateField("DiagonalCacheField");
        var registry = new FieldBattleOccupancyRegistry(field);
        Unit unit = CreateMeleeUnit("DiagonalCacheUnit", new Vector3(0.5f, 0f, 0.5f));
        registry.RegisterUnit(unit);

        Vector3 firstMonsterPosition = Vector3.zero;
        Vector3 secondMonsterPosition = new Vector3(0.1f, 0f, 0.1f);
        Vector2Int navigationCell = field.WorldToNavigationCell(firstMonsterPosition);
        Assert.That(field.WorldToNavigationCell(secondMonsterPosition), Is.EqualTo(navigationCell));

        var cache = new MonsterBlockerCandidateCache();
        Assert.That(cache.RefreshIfNeeded(registry, navigationCell, 0.6f), Is.True);
        Assert.That(cache.TrySelectBest(firstMonsterPosition, 0.6f, out _, out _), Is.False);
        Assert.That(cache.RefreshIfNeeded(registry, navigationCell, 0.6f), Is.False,
            "same-cell movement must reuse the candidate array");
        Assert.That(cache.TrySelectBest(secondMonsterPosition, 0.6f, out Unit selected, out _), Is.True);
        Assert.That(selected, Is.SameAs(unit));
    }

    [Test]
    public void BlockerCandidateCacheKeepsTransientlyFullUnitUntilCapacityReleases()
    {
        FieldManager field = CreateField("TransientCapacityField");
        var registry = new FieldBattleOccupancyRegistry(field);
        Unit unit = CreateMeleeUnit("TransientCapacityUnit", new Vector3(0.2f, 0f, 0.2f));
        registry.RegisterUnit(unit);

        Vector3 monsterPosition = Vector3.zero;
        Vector2Int navigationCell = field.WorldToNavigationCell(monsterPosition);
        var cache = new MonsterBlockerCandidateCache();
        Assert.That(cache.RefreshIfNeeded(registry, navigationCell, 0.6f), Is.True);
        int revisionBeforeCapacityChange = registry.UnitRevision;

        Monster occupyingMonster = Track(new GameObject("TransientCapacityOccupant")).AddComponent<Monster>();
        var blockedMonsters = (List<Monster>)BlockedMonstersField.GetValue(unit);
        blockedMonsters.Add(occupyingMonster);
        Assert.That(cache.TrySelectBest(monsterPosition, 0.6f, out _, out _), Is.False);
        Assert.That(registry.UnitRevision, Is.EqualTo(revisionBeforeCapacityChange));
        Assert.That(cache.RefreshIfNeeded(registry, navigationCell, 0.6f), Is.False);

        blockedMonsters.Remove(occupyingMonster);
        Assert.That(cache.TrySelectBest(monsterPosition, 0.6f, out Unit selected, out _), Is.True,
            "capacity release must be visible without rebuilding the cell cache");
        Assert.That(selected, Is.SameAs(unit));
    }

    [Test]
    public void BlockerCandidateCacheRefreshesWhenUnitOccupancyRevisionChanges()
    {
        FieldManager field = CreateField("RevisionCacheField");
        var registry = new FieldBattleOccupancyRegistry(field);
        Unit first = CreateMeleeUnit("RevisionFirst", new Vector3(0.5f, 0f, 0f));
        registry.RegisterUnit(first);

        Vector3 monsterPosition = Vector3.zero;
        Vector2Int navigationCell = field.WorldToNavigationCell(monsterPosition);
        var cache = new MonsterBlockerCandidateCache();
        Assert.That(cache.RefreshIfNeeded(registry, navigationCell, 0.6f), Is.True);
        Assert.That(cache.CandidateCount, Is.EqualTo(1));
        int firstRevision = registry.UnitRevision;

        Unit closer = CreateMeleeUnit("RevisionCloser", new Vector3(0.1f, 0f, 0f));
        registry.RegisterUnit(closer);
        Assert.That(registry.UnitRevision, Is.GreaterThan(firstRevision));
        Assert.That(cache.RefreshIfNeeded(registry, navigationCell, 0.6f), Is.True);
        Assert.That(cache.TrySelectBest(monsterPosition, 0.6f, out Unit selected, out _), Is.True);
        Assert.That(selected, Is.SameAs(closer));

        int registeredRevision = registry.UnitRevision;
        closer.transform.position = new Vector3(2.1f, 0f, 0f);
        Assert.That(registry.UpdateUnitCell(closer), Is.True);
        Assert.That(registry.UnitRevision, Is.GreaterThan(registeredRevision));
        Assert.That(cache.RefreshIfNeeded(registry, navigationCell, 0.6f), Is.True);
        Assert.That(cache.TrySelectBest(monsterPosition, 0.6f, out selected, out _), Is.True);
        Assert.That(selected, Is.SameAs(first));

        int movedRevision = registry.UnitRevision;
        registry.UnregisterUnit(first);
        Assert.That(registry.UnitRevision, Is.GreaterThan(movedRevision));
        Assert.That(cache.RefreshIfNeeded(registry, navigationCell, 0.6f), Is.True);
        Assert.That(cache.TrySelectBest(monsterPosition, 0.6f, out _, out _), Is.False);
    }

    [Test]
    public void BlockerCandidateCacheUsesStableOrderForEquidistantUnits()
    {
        FieldManager field = CreateField("DeterministicBlockerField");
        var registry = new FieldBattleOccupancyRegistry(field);
        Unit left = CreateMeleeUnit("DeterministicLeft", new Vector3(-0.25f, 0f, 0f));
        Unit right = CreateMeleeUnit("DeterministicRight", new Vector3(0.25f, 0f, 0f));

        // Register in the opposite order of the stable key so list/dictionary traversal cannot
        // accidentally define the result.
        ulong leftOrder = ReadStableOrder(left);
        ulong rightOrder = ReadStableOrder(right);
        Unit expected = leftOrder < rightOrder ? left : right;
        Unit other = expected == left ? right : left;
        other.transform.position = new Vector3(-0.25f, 0f, 0f);
        expected.transform.position = new Vector3(0.25f, 0f, 0f);
        Assert.That(
            field.WorldToNavigationCell(other.transform.position).x,
            Is.LessThan(field.WorldToNavigationCell(expected.transform.position).x),
            "the non-winning unit must be visited first so the tie-break changes the result");
        registry.RegisterUnit(other);
        registry.RegisterUnit(expected);

        Vector3 monsterPosition = Vector3.zero;
        var cache = new MonsterBlockerCandidateCache();
        Assert.That(
            cache.RefreshIfNeeded(registry, field.WorldToNavigationCell(monsterPosition), 0.6f),
            Is.True);

        for (int i = 0; i < 3; i++)
        {
            Assert.That(cache.TrySelectBest(monsterPosition, 0.6f, out Unit selected, out _), Is.True);
            Assert.That(selected, Is.SameAs(expected));
        }
    }

    [Test]
    public void InitializedCombatFieldAlwaysProvidesLocalOccupancyRegistry()
    {
        FieldManager field = CreateField("InitializedOccupancyField");
        GameObject ownerObject = Track(new GameObject("InitializedOccupancyOwner"));
        PlayerManager owner = ownerObject.AddComponent<PlayerManager>();
        owner.fieldManager = field;
        field.playerManager = owner;

        Assert.That(field.TryGetBattleOccupancyRegistry(out FieldBattleOccupancyRegistry registry), Is.True);
        Assert.That(registry, Is.Not.Null);
    }

    private static long ReadCounter(AstarGrid grid, string fieldName)
    {
        FieldInfo field = typeof(AstarGrid).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null);
        return (long)field.GetValue(grid);
    }

    private static ulong ReadStableOrder(Unit unit)
    {
        MethodInfo method = typeof(FieldBattleOccupancyRegistry).GetMethod(
            "GetStableOrder",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null);
        return (ulong)method.Invoke(null, new object[] { unit });
    }

    private FieldManager CreateField(string name)
    {
        GameObject fieldObject = Track(new GameObject(name));
        FieldManager field = fieldObject.AddComponent<FieldManager>();
        field.gridOrigin = Vector3.zero;
        field.cellSize = 1f;
        field.gridSize = new Vector2Int(10, 9);
        return field;
    }

    private Unit CreateMeleeUnit(string name, Vector3 position)
    {
        GameObject unitObject = Track(new GameObject(name));
        unitObject.transform.position = position;
        Unit unit = unitObject.AddComponent<Unit>();
        UnitData unitData = Track(ScriptableObject.CreateInstance<UnitData>());
        unitData.unitType = UnitType.Melee;
        unitData.blockCount = 1;
        unitData.baseHealth = 100f;
        UnitDataField.SetValue(unit, unitData);
        unit.maxHP = 100f;
        unit.currentHP = 100f;
        return unit;
    }

    private T Track<T>(T created) where T : Object
    {
        _createdObjects.Add(created);
        return created;
    }
}
