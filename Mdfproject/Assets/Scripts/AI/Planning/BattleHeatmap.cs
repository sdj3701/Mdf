using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public sealed class BattleHeatmap
{
    private const float PositionBucketScale = 10f;

    public PlayerManager Attacker { get; }
    public PlayerManager Defender { get; }
    public FieldManager DefenderField { get; }
    public IReadOnlyList<UnitSample> DefenderUnits { get; }
    public IReadOnlyList<MonsterSample> AlliedMonsters { get; }
    public bool HasGoalPosition { get; }
    public Vector3 GoalPosition { get; }

    public BattleHeatmap(
        PlayerManager attacker,
        PlayerManager defender,
        FieldManager defenderField,
        IEnumerable<UnitSample> defenderUnits,
        IEnumerable<MonsterSample> alliedMonsters,
        bool hasGoalPosition,
        Vector3 goalPosition)
    {
        Attacker = attacker;
        Defender = defender;
        DefenderField = defenderField;
        DefenderUnits = (defenderUnits ?? Enumerable.Empty<UnitSample>())
            .Where(sample => sample.IsValid)
            .OrderBy(sample => sample.StableKey, StringComparer.Ordinal)
            .ThenBy(sample => Bucket(sample.Position.x))
            .ThenBy(sample => Bucket(sample.Position.z))
            .ToArray();
        AlliedMonsters = (alliedMonsters ?? Enumerable.Empty<MonsterSample>())
            .Where(sample => sample.IsValid)
            .OrderBy(sample => sample.StableKey, StringComparer.Ordinal)
            .ThenBy(sample => Bucket(sample.Position.x))
            .ThenBy(sample => Bucket(sample.Position.z))
            .ToArray();
        HasGoalPosition = hasGoalPosition;
        GoalPosition = goalPosition;
    }

    public static BattleHeatmap Create(PlayerManager attacker, PlayerManager defender)
    {
        FieldManager defenderField = defender != null ? defender.fieldManager : null;
        Vector3 goalPosition = defender != null && defender.goalTransform != null
            ? defender.goalTransform.position
            : Vector3.zero;
        bool hasGoal = defender != null && defender.goalTransform != null;

        return new BattleHeatmap(
            attacker,
            defender,
            defenderField,
            CollectDefenderUnits(defender, defenderField),
            CollectAlliedMonstersOnDefenderField(defender, defenderField),
            hasGoal,
            goalPosition);
    }

    public IReadOnlyList<Vector3> BuildOffensiveCandidatePositions(float radius)
    {
        var candidates = new List<Vector3>();
        AddSamplePositions(candidates, DefenderUnits.Select(sample => sample.Position));
        AddClusterCenters(candidates, DefenderUnits.Select(sample => sample.Position), radius);
        AddEngagementCenters(candidates, radius);
        AddSamplePositions(candidates, AlliedMonsters.Select(sample => sample.Position));
        return StableDistinctPositions(candidates);
    }

    public IReadOnlyList<Vector3> BuildBuffCandidatePositions(float radius)
    {
        var candidates = new List<Vector3>();
        AddSamplePositions(candidates, AlliedMonsters.Select(sample => sample.Position));
        AddClusterCenters(candidates, AlliedMonsters.Select(sample => sample.Position), radius);
        AddEngagementCenters(candidates, radius);
        if (HasGoalPosition)
        {
            candidates.Add(GoalPosition);
        }

        return StableDistinctPositions(candidates);
    }

    public IReadOnlyList<UnitSample> DefenderUnitsInRadius(Vector3 position, float radius)
    {
        float radiusSq = radius * radius;
        return DefenderUnits.Where(sample => FlatDistanceSq(sample.Position, position) <= radiusSq).ToArray();
    }

    public IReadOnlyList<MonsterSample> AlliedMonstersInRadius(Vector3 position, float radius)
    {
        float radiusSq = radius * radius;
        return AlliedMonsters.Where(sample => FlatDistanceSq(sample.Position, position) <= radiusSq).ToArray();
    }

    public int CountEngagementsNear(Vector3 position, float radius)
    {
        if (DefenderUnits.Count == 0 || AlliedMonsters.Count == 0)
        {
            return 0;
        }

        float targetRadiusSq = radius * radius;
        float engagementRadius = Mathf.Max(1.75f, radius * 0.75f);
        float engagementRadiusSq = engagementRadius * engagementRadius;
        int count = 0;

        foreach (var monster in AlliedMonsters)
        {
            if (FlatDistanceSq(monster.Position, position) > targetRadiusSq)
            {
                continue;
            }

            if (DefenderUnits.Any(unit => FlatDistanceSq(unit.Position, monster.Position) <= engagementRadiusSq))
            {
                count++;
            }
        }

        return count;
    }

    public int CountAlliedMonstersNear(Vector3 position, float radius)
    {
        return AlliedMonstersInRadius(position, Mathf.Max(1f, radius)).Count;
    }

    public float GoalProximityScore(Vector3 position, float radius)
    {
        if (!HasGoalPosition)
        {
            return 0f;
        }

        float distance = Mathf.Sqrt(FlatDistanceSq(position, GoalPosition));
        float window = Mathf.Max(1f, radius * 3f);
        return Mathf.Clamp01(1f - (distance / window));
    }

    public bool IsWithinTotalFieldBounds(Vector3 position)
    {
        return DefenderField == null || IsWithinTotalFieldBounds(DefenderField, position);
    }

    public static bool IsWithinTotalFieldBounds(FieldManager field, Vector3 position)
    {
        if (field == null || !IsFinite(position))
        {
            return false;
        }

        Vector3 origin = field.TotalGridOrigin;
        Vector2Int size = field.TotalGridSize;
        float cellSize = field.cellSize;
        float epsilon = Mathf.Max(0.01f, cellSize * 0.05f);
        float minX = origin.x - epsilon;
        float minZ = origin.z - epsilon;
        float maxX = origin.x + size.x * cellSize + epsilon;
        float maxZ = origin.z + size.y * cellSize + epsilon;

        return position.x >= minX &&
               position.x <= maxX &&
               position.z >= minZ &&
               position.z <= maxZ;
    }

    public static bool IsFinite(Vector3 position)
    {
        return !float.IsNaN(position.x) &&
               !float.IsNaN(position.y) &&
               !float.IsNaN(position.z) &&
               !float.IsInfinity(position.x) &&
               !float.IsInfinity(position.y) &&
               !float.IsInfinity(position.z);
    }

    public static int Bucket(float value)
    {
        return Mathf.RoundToInt(value * PositionBucketScale);
    }

    public static float FlatDistanceSq(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return dx * dx + dz * dz;
    }

    private static IEnumerable<UnitSample> CollectDefenderUnits(PlayerManager defender, FieldManager field)
    {
        IEnumerable<Unit> source = field != null
            ? field.GetAlliedUnitsOnField()
            : defender != null
                ? defender.ownedUnits
                : Enumerable.Empty<Unit>();

        foreach (var unit in source ?? Enumerable.Empty<Unit>())
        {
            if (unit == null || unit.Data == null || unit.IsDead || !unit.gameObject.activeInHierarchy)
            {
                continue;
            }

            yield return UnitSample.FromUnit(unit, field);
        }
    }

    private static IEnumerable<MonsterSample> CollectAlliedMonstersOnDefenderField(PlayerManager defender, FieldManager defenderField)
    {
        var seen = new HashSet<Monster>();

        Transform monsterParent = defender != null && defender.monsterSpawner != null
            ? defender.monsterSpawner.monsterParent
            : null;
        if (monsterParent != null)
        {
            foreach (var monster in monsterParent.GetComponentsInChildren<Monster>(true))
            {
                if (TryAddMonster(monster, defenderField, seen, out var sample))
                {
                    yield return sample;
                }
            }
        }

        foreach (var monster in UnityEngine.Object.FindObjectsOfType<Monster>())
        {
            if (TryAddMonster(monster, defenderField, seen, out var sample))
            {
                yield return sample;
            }
        }
    }

    private static bool TryAddMonster(
        Monster monster,
        FieldManager defenderField,
        HashSet<Monster> seen,
        out MonsterSample sample)
    {
        sample = default;
        if (monster == null ||
            seen.Contains(monster) ||
            monster.Data == null ||
            monster.CurrentHealth <= 0f ||
            !monster.gameObject.activeInHierarchy)
        {
            return false;
        }

        Vector3 position = ResolveObjectCenter(monster.gameObject);
        if (defenderField != null && !IsWithinTotalFieldBounds(defenderField, position))
        {
            return false;
        }

        seen.Add(monster);
        sample = MonsterSample.FromMonster(monster, position);
        return true;
    }

    private void AddEngagementCenters(List<Vector3> candidates, float radius)
    {
        if (DefenderUnits.Count == 0 || AlliedMonsters.Count == 0)
        {
            return;
        }

        float engagementRadiusSq = Mathf.Max(1.75f, radius * 0.75f);
        engagementRadiusSq *= engagementRadiusSq;

        foreach (var monster in AlliedMonsters)
        {
            foreach (var unit in DefenderUnits)
            {
                if (FlatDistanceSq(monster.Position, unit.Position) <= engagementRadiusSq)
                {
                    candidates.Add(Average(monster.Position, unit.Position));
                }
            }
        }
    }

    private static void AddSamplePositions(List<Vector3> candidates, IEnumerable<Vector3> positions)
    {
        foreach (var position in positions)
        {
            if (IsFinite(position))
            {
                candidates.Add(position);
            }
        }
    }

    private static void AddClusterCenters(List<Vector3> candidates, IEnumerable<Vector3> positions, float radius)
    {
        var source = positions.Where(IsFinite).ToArray();
        if (source.Length < 2)
        {
            return;
        }

        float radiusSq = radius * radius;
        for (int i = 0; i < source.Length; i++)
        {
            var nearby = source.Where(position => FlatDistanceSq(position, source[i]) <= radiusSq).ToArray();
            if (nearby.Length < 2)
            {
                continue;
            }

            Vector3 sum = Vector3.zero;
            foreach (var position in nearby)
            {
                sum += position;
            }

            candidates.Add(sum / nearby.Length);
        }
    }

    private static IReadOnlyList<Vector3> StableDistinctPositions(IEnumerable<Vector3> positions)
    {
        var result = new List<Vector3>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var position in positions.Where(IsFinite)
                     .OrderBy(position => Bucket(position.x))
                     .ThenBy(position => Bucket(position.z)))
        {
            string key = $"{Bucket(position.x)}:{Bucket(position.z)}";
            if (seen.Add(key))
            {
                result.Add(position);
            }
        }

        return result;
    }

    private static Vector3 Average(Vector3 a, Vector3 b)
    {
        return new Vector3((a.x + b.x) * 0.5f, (a.y + b.y) * 0.5f, (a.z + b.z) * 0.5f);
    }

    private static Vector3 ResolveObjectCenter(GameObject target)
    {
        if (target == null)
        {
            return Vector3.zero;
        }

        var collider = target.GetComponentInChildren<Collider>();
        if (collider != null)
        {
            return collider.bounds.center;
        }

        var renderer = target.GetComponentInChildren<Renderer>();
        return renderer != null ? renderer.bounds.center : target.transform.position;
    }

    public readonly struct UnitSample
    {
        public readonly Unit Unit;
        public readonly Vector3 Position;
        public readonly string StableKey;
        public readonly float Value;
        public readonly float HealthRatio;
        public readonly bool IsRanged;
        public readonly bool IsLowHealth;
        public readonly bool IsHighValue;

        public bool IsValid => !string.IsNullOrEmpty(StableKey) && IsFinite(Position);

        public UnitSample(
            Unit unit,
            Vector3 position,
            string stableKey,
            float value,
            float healthRatio,
            bool isRanged,
            bool isLowHealth,
            bool isHighValue)
        {
            Unit = unit;
            Position = position;
            StableKey = stableKey;
            Value = value;
            HealthRatio = healthRatio;
            IsRanged = isRanged;
            IsLowHealth = isLowHealth;
            IsHighValue = isHighValue;
        }

        public static UnitSample FromUnit(Unit unit, FieldManager field)
        {
            Vector3 position = field != null && field.GetUnitPosition(unit).HasValue
                ? field.GridToWorld(field.GetUnitPosition(unit).Value, checkForWall: true)
                : ResolveObjectCenter(unit != null ? unit.gameObject : null);
            UnitData data = unit != null ? unit.Data : null;
            int star = unit != null ? Mathf.Max(1, unit.starLevel) : 1;
            float healthRatio = unit != null && unit.MaxHealth > 0f ? unit.CurrentHealth / unit.MaxHealth : 1f;
            float value = CalculateUnitValue(data, star, field, unit);
            string key = data != null
                ? $"{data.unitName};star={star};type={data.unitType}"
                : "unit=unknown";

            return new UnitSample(
                unit,
                position,
                key,
                value,
                healthRatio,
                data != null && data.unitType == UnitType.Ranged,
                healthRatio <= 0.35f,
                value >= 9f);
        }

        private static float CalculateUnitValue(UnitData data, int star, FieldManager field, Unit unit)
        {
            int sellPrice = field != null && unit != null ? field.GetSellPrice(unit) : 0;
            if (sellPrice > 0)
            {
                return sellPrice;
            }

            int cost = data != null ? Mathf.Max(1, data.cost) : 1;
            return cost * Mathf.Pow(2f, Mathf.Max(0, star - 1));
        }
    }

    public readonly struct MonsterSample
    {
        public readonly Monster Monster;
        public readonly Vector3 Position;
        public readonly string StableKey;
        public readonly float Value;
        public readonly float HealthRatio;
        public readonly bool IsBoss;
        public readonly bool IsDestroyer;
        public readonly bool IsTank;
        public readonly bool IsNearGoal;

        public bool IsValid => !string.IsNullOrEmpty(StableKey) && IsFinite(Position);

        public MonsterSample(
            Monster monster,
            Vector3 position,
            string stableKey,
            float value,
            float healthRatio,
            bool isBoss,
            bool isDestroyer,
            bool isTank,
            bool isNearGoal)
        {
            Monster = monster;
            Position = position;
            StableKey = stableKey;
            Value = value;
            HealthRatio = healthRatio;
            IsBoss = isBoss;
            IsDestroyer = isDestroyer;
            IsTank = isTank;
            IsNearGoal = isNearGoal;
        }

        public static MonsterSample FromMonster(Monster monster, Vector3 position)
        {
            MonsterData data = monster != null ? monster.Data : null;
            float maxHealth = monster != null ? monster.MaxHealth : data != null ? data.maxHealth : 0f;
            float currentHealth = monster != null ? monster.CurrentHealth : maxHealth;
            float healthRatio = maxHealth > 0f ? currentHealth / maxHealth : 1f;
            bool isBoss = monster != null && monster.SnapshotIsBoss;
            bool isDestroyer = data != null && (data.traits & MonsterTraits.Destroyer) != 0;
            bool isTank = maxHealth >= 250f || (data != null && data.defense + data.magicResistance >= 20f);
            string key = data != null
                ? $"{data.monsterName};type={data.monsterType};traits={data.traits};boss={isBoss}"
                : "monster=unknown";

            return new MonsterSample(
                monster,
                position,
                key,
                CalculateMonsterValue(data, maxHealth, isBoss, isDestroyer, isTank),
                healthRatio,
                isBoss,
                isDestroyer,
                isTank,
                false);
        }

        private static float CalculateMonsterValue(
            MonsterData data,
            float maxHealth,
            bool isBoss,
            bool isDestroyer,
            bool isTank)
        {
            float value = data != null
                ? data.attackDamage + data.attackSpeed * 5f + maxHealth * 0.05f + data.moveSpeed * 3f
                : 1f;
            if (isBoss) value += 80f;
            if (isDestroyer) value += 35f;
            if (isTank) value += 20f;
            return value;
        }
    }
}
