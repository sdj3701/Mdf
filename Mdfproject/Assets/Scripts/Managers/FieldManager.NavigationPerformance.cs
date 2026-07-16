using System;
using System.Collections.Generic;
using Fusion;
using UnityEngine;

public partial class FieldManager
{
    private int _wallTopologyRevision = 1;
    private FieldBattleOccupancyRegistry _battleOccupancy;

    /// <summary>
    /// Local-only revision for pathfinding caches. Durable wall dictionaries remain the source of truth.
    /// </summary>
    public int WallTopologyRevision => _wallTopologyRevision;

    private void MarkWallTopologyChanged()
    {
        unchecked
        {
            _wallTopologyRevision++;
            if (_wallTopologyRevision == int.MinValue)
            {
                _wallTopologyRevision = 1;
            }
        }
    }

    private FieldBattleOccupancyRegistry BattleOccupancy
    {
        get
        {
            if (_battleOccupancy == null)
            {
                _battleOccupancy = new FieldBattleOccupancyRegistry(this);
            }

            return _battleOccupancy;
        }
    }

    public bool TryGetBattleOccupancyRegistry(out FieldBattleOccupancyRegistry registry)
    {
        registry = null;
        if (playerManager == null)
        {
            return false;
        }

        EnsureCombatTargetRegistryReady();
        registry = BattleOccupancy;
        return true;
    }

    public void UpdateCombatMonsterOccupancy(Monster monster)
    {
        if (monster == null)
        {
            return;
        }

        BattleOccupancy.UpdateMonsterCell(monster);
    }

    public void UpdateCombatUnitOccupancy(Unit unit)
    {
        if (unit == null)
        {
            return;
        }

        _battleOccupancy?.UpdateUnitCell(unit);
    }

    public bool TryGetBlockingUnitFromOccupancy(
        Monster monster,
        Vector3 monsterPosition,
        float detectionRadius,
        float blockDistance,
        out Unit unit,
        out int visitedCandidates)
    {
        unit = null;
        visitedCandidates = 0;
        if (monster == null || playerManager == null)
        {
            return false;
        }

        EnsureCombatTargetRegistryReady();
        BattleOccupancy.TryFindBlockingUnit(
            monster,
            monsterPosition,
            detectionRadius,
            blockDistance,
            out unit,
            out visitedCandidates);
        return true;
    }

    private void RebuildBattleOccupancyRegistry(Transform monsterParent)
    {
        BattleOccupancy.RebuildUnits(placedUnits.Values);

        if (monsterParent == null)
        {
            BattleOccupancy.RebuildMonsters(null);
            return;
        }

        Monster[] monsters = monsterParent.GetComponentsInChildren<Monster>(true);
        BattleOccupancy.RebuildMonsters(monsters);
    }

    private void DisposeBattleOccupancyRegistry()
    {
        _battleOccupancy?.Clear();
        _battleOccupancy = null;
    }
}

/// <summary>
/// Local, rebuildable cell occupancy used for exact-cell validation and melee contact checks.
/// Networked objects and the FieldManager dictionaries remain authoritative.
/// </summary>
public sealed class FieldBattleOccupancyRegistry
{
    private const float DistanceTieEpsilon = 0.0001f;
    private const int InitialBlockerCandidateCapacity = 8;

    private readonly FieldManager _field;
    private readonly Dictionary<Vector2Int, List<Unit>> _unitsByCell =
        new Dictionary<Vector2Int, List<Unit>>();
    private readonly Dictionary<Vector2Int, List<Monster>> _monstersByCell =
        new Dictionary<Vector2Int, List<Monster>>();
    private readonly Dictionary<int, Vector2Int> _unitCells =
        new Dictionary<int, Vector2Int>();
    private readonly Dictionary<int, Vector2Int> _monsterCells =
        new Dictionary<int, Vector2Int>();
    private int _unitRevision = 1;

    public int RegisteredUnitCount => _unitCells.Count;
    public int RegisteredMonsterCount => _monsterCells.Count;
    public int UnitRevision => _unitRevision;

    public FieldBattleOccupancyRegistry(FieldManager field)
    {
        _field = field;
    }

    public void RegisterUnit(Unit unit)
    {
        if (!IsLivingUnit(unit))
        {
            UnregisterUnit(unit);
            return;
        }

        UpdateUnitCell(unit);
    }

    public void UnregisterUnit(Unit unit)
    {
        if (unit == null)
        {
            return;
        }

        int instanceId = unit.GetInstanceID();
        if (_unitCells.TryGetValue(instanceId, out Vector2Int cell))
        {
            RemoveFromCell(_unitsByCell, cell, unit);
            _unitCells.Remove(instanceId);
            MarkUnitRevisionChanged();
        }
    }

    public void RegisterMonster(Monster monster)
    {
        if (!IsLivingMonster(monster))
        {
            UnregisterMonster(monster);
            return;
        }

        UpdateMonsterCell(monster);
    }

    public void UnregisterMonster(Monster monster)
    {
        if (monster == null)
        {
            return;
        }

        int instanceId = monster.GetInstanceID();
        if (_monsterCells.TryGetValue(instanceId, out Vector2Int cell))
        {
            RemoveFromCell(_monstersByCell, cell, monster);
            _monsterCells.Remove(instanceId);
        }
    }

    public bool UpdateUnitCell(Unit unit)
    {
        if (unit == null)
        {
            return false;
        }

        if (!IsLivingUnit(unit))
        {
            int registeredId = unit.GetInstanceID();
            if (!_unitCells.TryGetValue(registeredId, out Vector2Int registeredCell))
            {
                return false;
            }

            RemoveFromCell(_unitsByCell, registeredCell, unit);
            _unitCells.Remove(registeredId);
            MarkUnitRevisionChanged();
            return true;
        }

        int instanceId = unit.GetInstanceID();
        Vector2Int nextCell = ToNavigationCell(unit.transform.position);
        if (_unitCells.TryGetValue(instanceId, out Vector2Int previousCell))
        {
            if (previousCell == nextCell)
            {
                return false;
            }

            RemoveFromCell(_unitsByCell, previousCell, unit);
        }

        _unitCells[instanceId] = nextCell;
        AddToCell(_unitsByCell, nextCell, unit);
        MarkUnitRevisionChanged();
        return true;
    }

    public bool UpdateMonsterCell(Monster monster)
    {
        return UpdateCell(monster, _monsterCells, _monstersByCell);
    }

    public void RebuildUnits(IEnumerable<Unit> units)
    {
        _unitsByCell.Clear();
        _unitCells.Clear();
        MarkUnitRevisionChanged();
        if (units == null)
        {
            return;
        }

        foreach (Unit unit in units)
        {
            RegisterUnit(unit);
        }
    }

    public void RebuildMonsters(IEnumerable<Monster> monsters)
    {
        _monstersByCell.Clear();
        _monsterCells.Clear();
        if (monsters == null)
        {
            return;
        }

        foreach (Monster monster in monsters)
        {
            RegisterMonster(monster);
        }
    }

    public bool HasLivingMonsterAtCell(Vector2Int navigationCell)
    {
        if (!_monstersByCell.TryGetValue(navigationCell, out List<Monster> monsters))
        {
            return false;
        }

        bool found = false;
        for (int i = monsters.Count - 1; i >= 0; i--)
        {
            Monster monster = monsters[i];
            if (!IsLivingMonster(monster))
            {
                RemoveMonsterEntryAt(monsters, i, monster);
                continue;
            }

            Vector2Int currentCell = ToNavigationCell(monster.transform.position);
            if (currentCell != navigationCell)
            {
                MoveMonsterEntry(monsters, i, monster, currentCell);
                continue;
            }

            found = true;
        }

        if (monsters.Count == 0)
        {
            _monstersByCell.Remove(navigationCell);
        }

        return found;
    }

    public bool TryFindBlockingUnit(
        Monster monster,
        Vector3 monsterPosition,
        float detectionRadius,
        float blockDistance,
        out Unit bestUnit)
    {
        return TryFindBlockingUnit(
            monster,
            monsterPosition,
            detectionRadius,
            blockDistance,
            out bestUnit,
            out _);
    }

    public bool TryFindBlockingUnit(
        Monster monster,
        Vector3 monsterPosition,
        float detectionRadius,
        float blockDistance,
        out Unit bestUnit,
        out int visitedCandidates)
    {
        bestUnit = null;
        visitedCandidates = 0;
        if (monster == null || blockDistance < 0f)
        {
            return false;
        }

        Vector2Int center = ToNavigationCell(monsterPosition);
        float cellSize = _field != null ? Mathf.Max(0.0001f, _field.cellSize) : 1f;
        int cellRadius = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(detectionRadius, blockDistance) / cellSize));
        float blockDistanceSqr = blockDistance * blockDistance;
        float bestDistanceSqr = float.MaxValue;
        ulong bestStableOrder = ulong.MaxValue;

        for (int x = -cellRadius; x <= cellRadius; x++)
        {
            for (int y = -cellRadius; y <= cellRadius; y++)
            {
                Vector2Int cell = new Vector2Int(center.x + x, center.y + y);
                if (!_unitsByCell.TryGetValue(cell, out List<Unit> units))
                {
                    continue;
                }

                for (int i = units.Count - 1; i >= 0; i--)
                {
                    Unit candidate = units[i];
                    visitedCandidates++;
                    if (!IsLivingUnit(candidate))
                    {
                        RemoveUnitEntryAt(units, i, candidate);
                        continue;
                    }

                    Vector2Int currentCell = ToNavigationCell(candidate.transform.position);
                    if (currentCell != cell)
                    {
                        MoveUnitEntry(units, i, candidate, currentCell);
                        continue;
                    }

                    // Missing block data, a non-melee role, or a temporarily full block
                    // capacity makes the unit ineligible for this probe only. It remains a
                    // live occupant and must be reconsidered as soon as capacity is released.
                    if (!IsBlockCandidate(candidate))
                    {
                        continue;
                    }

                    float distanceSqr = (candidate.transform.position - monsterPosition).sqrMagnitude;
                    if (distanceSqr > blockDistanceSqr)
                    {
                        continue;
                    }

                    ulong stableOrder = GetStableOrder(candidate);
                    if (bestUnit == null || distanceSqr < bestDistanceSqr - DistanceTieEpsilon ||
                        Mathf.Abs(distanceSqr - bestDistanceSqr) <= DistanceTieEpsilon && stableOrder < bestStableOrder)
                    {
                        bestUnit = candidate;
                        bestDistanceSqr = distanceSqr;
                        bestStableOrder = stableOrder;
                    }
                }

                if (units.Count == 0)
                {
                    _unitsByCell.Remove(cell);
                }
            }
        }

        return bestUnit != null;
    }

    /// <summary>
    /// Copies living melee blockers from the current/nearby navigation cells into a caller-owned
    /// reusable array. Transient block capacity is deliberately not part of this cache; callers
    /// must evaluate IsBlockingFull when they attempt to block.
    /// </summary>
    public int CopyNearbyBlockerCandidates(
        Vector2Int center,
        float blockDistance,
        ref Unit[] candidates)
    {
        if (candidates == null || candidates.Length == 0)
        {
            candidates = new Unit[InitialBlockerCandidateCapacity];
        }

        int count = 0;
        float cellSize = _field != null ? Mathf.Max(0.0001f, _field.cellSize) : 1f;
        int cellRadius = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(0f, blockDistance) / cellSize));

        for (int x = -cellRadius; x <= cellRadius; x++)
        {
            for (int y = -cellRadius; y <= cellRadius; y++)
            {
                Vector2Int cell = new Vector2Int(center.x + x, center.y + y);
                if (!_unitsByCell.TryGetValue(cell, out List<Unit> units))
                {
                    continue;
                }

                for (int i = units.Count - 1; i >= 0; i--)
                {
                    Unit candidate = units[i];
                    if (!IsLivingUnit(candidate))
                    {
                        RemoveUnitEntryAt(units, i, candidate);
                        continue;
                    }

                    Vector2Int currentCell = ToNavigationCell(candidate.transform.position);
                    if (currentCell != cell)
                    {
                        MoveUnitEntry(units, i, candidate, currentCell);
                        if (!IsCellWithinRadius(center, currentCell, cellRadius) ||
                            !IsCacheableBlockCandidate(candidate) ||
                            ContainsCandidate(candidates, count, candidate))
                        {
                            continue;
                        }

                        EnsureCandidateCapacity(ref candidates, count + 1);
                        candidates[count++] = candidate;
                        continue;
                    }

                    if (!IsCacheableBlockCandidate(candidate) ||
                        ContainsCandidate(candidates, count, candidate))
                    {
                        continue;
                    }

                    EnsureCandidateCapacity(ref candidates, count + 1);
                    candidates[count++] = candidate;
                }

                if (units.Count == 0)
                {
                    _unitsByCell.Remove(cell);
                }
            }
        }

        return count;
    }

    public void Clear()
    {
        bool hadUnits = _unitCells.Count > 0 || _unitsByCell.Count > 0;
        _unitsByCell.Clear();
        _monstersByCell.Clear();
        _unitCells.Clear();
        _monsterCells.Clear();
        if (hadUnits)
        {
            MarkUnitRevisionChanged();
        }
    }

    private bool UpdateCell<T>(
        T actor,
        Dictionary<int, Vector2Int> actorCells,
        Dictionary<Vector2Int, List<T>> actorsByCell)
        where T : Component
    {
        if (actor == null)
        {
            return false;
        }

        int instanceId = actor.GetInstanceID();
        Vector2Int nextCell = ToNavigationCell(actor.transform.position);
        if (actorCells.TryGetValue(instanceId, out Vector2Int previousCell))
        {
            if (previousCell == nextCell)
            {
                return false;
            }

            RemoveFromCell(actorsByCell, previousCell, actor);
        }

        actorCells[instanceId] = nextCell;
        AddToCell(actorsByCell, nextCell, actor);
        return true;
    }

    private Vector2Int ToNavigationCell(Vector3 worldPosition)
    {
        if (_field != null)
        {
            return _field.WorldToNavigationCell(worldPosition);
        }

        return new Vector2Int(Mathf.FloorToInt(worldPosition.x), Mathf.FloorToInt(worldPosition.z));
    }

    private static void AddToCell<T>(Dictionary<Vector2Int, List<T>> map, Vector2Int cell, T actor)
    {
        if (!map.TryGetValue(cell, out List<T> actors))
        {
            actors = new List<T>(2);
            map.Add(cell, actors);
        }

        if (!actors.Contains(actor))
        {
            actors.Add(actor);
        }
    }

    private static void RemoveFromCell<T>(Dictionary<Vector2Int, List<T>> map, Vector2Int cell, T actor)
    {
        if (!map.TryGetValue(cell, out List<T> actors))
        {
            return;
        }

        actors.Remove(actor);
        if (actors.Count == 0)
        {
            map.Remove(cell);
        }
    }

    private void RemoveMonsterEntryAt(List<Monster> monsters, int index, Monster monster)
    {
        if (monster != null)
        {
            _monsterCells.Remove(monster.GetInstanceID());
        }

        monsters.RemoveAt(index);
    }

    private void MoveMonsterEntry(List<Monster> monsters, int index, Monster monster, Vector2Int nextCell)
    {
        monsters.RemoveAt(index);
        _monsterCells[monster.GetInstanceID()] = nextCell;
        AddToCell(_monstersByCell, nextCell, monster);
    }

    private void RemoveUnitEntryAt(List<Unit> units, int index, Unit unit)
    {
        if (unit != null)
        {
            _unitCells.Remove(unit.GetInstanceID());
        }

        units.RemoveAt(index);
        MarkUnitRevisionChanged();
    }

    private void MoveUnitEntry(List<Unit> units, int index, Unit unit, Vector2Int nextCell)
    {
        units.RemoveAt(index);
        _unitCells[unit.GetInstanceID()] = nextCell;
        AddToCell(_unitsByCell, nextCell, unit);
        MarkUnitRevisionChanged();
    }

    private static bool IsLivingUnit(Unit unit)
    {
        return unit != null &&
               unit.gameObject.activeInHierarchy &&
               !unit.IsDead &&
               unit.CurrentHealth > 0f;
    }

    private static bool IsBlockCandidate(Unit unit)
    {
        return IsCacheableBlockCandidate(unit) &&
               !unit.IsBlockingFull();
    }

    internal static bool IsCacheableBlockCandidate(Unit unit)
    {
        return IsLivingUnit(unit) &&
               unit.Data != null &&
               unit.Data.blockCount > 0 &&
               unit.Data.unitType == UnitType.Melee;
    }

    private static bool IsLivingMonster(Monster monster)
    {
        if (monster == null || !monster.gameObject.activeInHierarchy || monster.CurrentHealth <= 0f)
        {
            return false;
        }

        NetworkObject networkObject = monster.Object;
        return networkObject == null || networkObject.IsValid;
    }

    internal static ulong GetStableOrder(NetworkBehaviour actor)
    {
        if (actor != null && actor.Object != null && actor.Object.IsValid)
        {
            return actor.Object.Id.Raw;
        }

        return (1UL << 32) + unchecked((uint)(actor != null ? actor.GetInstanceID() : int.MaxValue));
    }

    private void MarkUnitRevisionChanged()
    {
        unchecked
        {
            _unitRevision++;
            if (_unitRevision == int.MinValue)
            {
                _unitRevision = 1;
            }
        }
    }

    private static bool IsCellWithinRadius(Vector2Int center, Vector2Int cell, int radius)
    {
        return Mathf.Abs(cell.x - center.x) <= radius && Mathf.Abs(cell.y - center.y) <= radius;
    }

    private static bool ContainsCandidate(Unit[] candidates, int count, Unit candidate)
    {
        for (int i = 0; i < count; i++)
        {
            if (candidates[i] == candidate)
            {
                return true;
            }
        }

        return false;
    }

    private static void EnsureCandidateCapacity(ref Unit[] candidates, int required)
    {
        if (required <= candidates.Length)
        {
            return;
        }

        int nextCapacity = Mathf.Max(required, candidates.Length * 2);
        Array.Resize(ref candidates, nextCapacity);
    }
}

/// <summary>
/// Per-monster, local-only blocker cache. Dictionary work happens only when the monster crosses
/// a navigation cell or the unit occupancy revision changes; distance and transient capacity are
/// still evaluated every movement frame.
/// </summary>
public sealed class MonsterBlockerCandidateCache
{
    private const int InitialCapacity = 8;
    private const float DistanceTieEpsilon = 0.0001f;

    private Unit[] _candidates = new Unit[InitialCapacity];
    private int _candidateCount;
    private FieldBattleOccupancyRegistry _registry;
    private Vector2Int _navigationCell;
    private float _blockDistance;
    private int _unitRevision = int.MinValue;
    private bool _hasNavigationCell;

    public int CandidateCount => _candidateCount;
    public int CachedUnitRevision => _unitRevision;

    public bool RefreshIfNeeded(
        FieldBattleOccupancyRegistry registry,
        Vector2Int navigationCell,
        float blockDistance)
    {
        if (registry == null)
        {
            Clear();
            return false;
        }

        if (_registry == registry &&
            _hasNavigationCell &&
            _navigationCell == navigationCell &&
            Mathf.Approximately(_blockDistance, blockDistance) &&
            _unitRevision == registry.UnitRevision)
        {
            return false;
        }

        int previousCount = _candidateCount;
        _candidateCount = registry.CopyNearbyBlockerCandidates(
            navigationCell,
            blockDistance,
            ref _candidates);
        for (int i = _candidateCount; i < previousCount; i++)
        {
            _candidates[i] = null;
        }

        _registry = registry;
        _navigationCell = navigationCell;
        _blockDistance = blockDistance;
        _unitRevision = registry.UnitRevision;
        _hasNavigationCell = true;
        return true;
    }

    public bool TrySelectBest(
        Vector3 monsterPosition,
        float blockDistance,
        out Unit bestUnit,
        out int visitedCandidates)
    {
        bestUnit = null;
        visitedCandidates = _candidateCount;
        float blockDistanceSqr = blockDistance * blockDistance;
        float bestDistanceSqr = float.MaxValue;
        ulong bestStableOrder = ulong.MaxValue;

        for (int i = 0; i < _candidateCount; i++)
        {
            Unit candidate = _candidates[i];
            if (!FieldBattleOccupancyRegistry.IsCacheableBlockCandidate(candidate) ||
                candidate.IsBlockingFull())
            {
                continue;
            }

            float distanceSqr = (candidate.transform.position - monsterPosition).sqrMagnitude;
            if (distanceSqr > blockDistanceSqr)
            {
                continue;
            }

            ulong stableOrder = FieldBattleOccupancyRegistry.GetStableOrder(candidate);
            if (bestUnit == null ||
                distanceSqr < bestDistanceSqr - DistanceTieEpsilon ||
                Mathf.Abs(distanceSqr - bestDistanceSqr) <= DistanceTieEpsilon &&
                stableOrder < bestStableOrder)
            {
                bestUnit = candidate;
                bestDistanceSqr = distanceSqr;
                bestStableOrder = stableOrder;
            }
        }

        return bestUnit != null;
    }

    public void Clear()
    {
        for (int i = 0; i < _candidateCount; i++)
        {
            _candidates[i] = null;
        }

        _candidateCount = 0;
        _registry = null;
        _navigationCell = default;
        _blockDistance = 0f;
        _unitRevision = int.MinValue;
        _hasNavigationCell = false;
    }
}
