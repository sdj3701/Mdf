using System.Collections.Generic;

public enum PrepareUnitRole
{
    Unknown,
    Melee,
    RangedDps,
    Healer
}

public sealed class PrepareArmyComposition
{
    public const int TargetMelee = 2;
    public const int TargetRangedDps = 2;
    public const int TargetHealer = 1;
    public const int TargetTotalUnits = TargetMelee + TargetRangedDps + TargetHealer;
    public const int MinimumCoreUnitCount = 3;

    private readonly Dictionary<string, int> _unitStarCounts = new Dictionary<string, int>();

    public string Source { get; private set; }
    public int MeleeCount { get; private set; }
    public int RangedDpsCount { get; private set; }
    public int HealerCount { get; private set; }
    public int UnknownCount { get; private set; }

    public int FieldUnitCount => MeleeCount + RangedDpsCount + HealerCount + UnknownCount;

    public int CompositionDistanceToTarget =>
        System.Math.Abs(TargetMelee - MeleeCount) +
        System.Math.Abs(TargetRangedDps - RangedDpsCount) +
        System.Math.Abs(TargetHealer - HealerCount);

    public bool HasMinimumArmyCore =>
        FieldUnitCount >= MinimumCoreUnitCount &&
        MeleeCount >= 1 &&
        RangedDpsCount + HealerCount >= 1;

    public PrepareArmyComposition(string source = "field")
    {
        Source = string.IsNullOrWhiteSpace(source) ? "field" : source;
    }

    public void AddUnit(UnitData unitData, int starLevel)
    {
        PrepareUnitRole role = UnitCompositionAnalyzer.Classify(unitData);
        switch (role)
        {
            case PrepareUnitRole.Melee:
                MeleeCount++;
                break;
            case PrepareUnitRole.RangedDps:
                RangedDpsCount++;
                break;
            case PrepareUnitRole.Healer:
                HealerCount++;
                break;
            default:
                UnknownCount++;
                break;
        }

        string key = BuildUnitStarKey(unitData, starLevel);
        if (!_unitStarCounts.ContainsKey(key))
        {
            _unitStarCounts[key] = 0;
        }

        _unitStarCounts[key]++;
    }

    public int CountRole(PrepareUnitRole role)
    {
        switch (role)
        {
            case PrepareUnitRole.Melee:
                return MeleeCount;
            case PrepareUnitRole.RangedDps:
                return RangedDpsCount;
            case PrepareUnitRole.Healer:
                return HealerCount;
            default:
                return UnknownCount;
        }
    }

    public int TargetForRole(PrepareUnitRole role)
    {
        switch (role)
        {
            case PrepareUnitRole.Melee:
                return TargetMelee;
            case PrepareUnitRole.RangedDps:
                return TargetRangedDps;
            case PrepareUnitRole.Healer:
                return TargetHealer;
            default:
                return 0;
        }
    }

    public int DeficitForRole(PrepareUnitRole role)
    {
        return System.Math.Max(0, TargetForRole(role) - CountRole(role));
    }

    public int ProjectedOverTargetForRole(PrepareUnitRole role)
    {
        int target = TargetForRole(role);
        if (target <= 0)
        {
            return 0;
        }

        return System.Math.Max(0, CountRole(role) + 1 - target);
    }

    public int CountMatchingSameUnitSameStar(UnitData unitData, int starLevel)
    {
        string key = BuildUnitStarKey(unitData, starLevel);
        return _unitStarCounts.TryGetValue(key, out int count) ? count : 0;
    }

    public Dictionary<string, object> ToJournalFields()
    {
        return new Dictionary<string, object>
        {
            { "compositionSource", Source },
            { "meleeCount", MeleeCount },
            { "rangedDpsCount", RangedDpsCount },
            { "healerCount", HealerCount },
            { "unknownUnitRoleCount", UnknownCount },
            { "fieldUnitCount", FieldUnitCount },
            { "targetMelee", TargetMelee },
            { "targetRangedDps", TargetRangedDps },
            { "targetHealer", TargetHealer },
            { "compositionDistance", CompositionDistanceToTarget },
            { "hasMinimumArmyCore", HasMinimumArmyCore }
        };
    }

    public static string BuildUnitStarKey(UnitData unitData, int starLevel)
    {
        return UnitCompositionAnalyzer.StableUnitKey(unitData) + ":star=" + System.Math.Max(1, starLevel);
    }
}
