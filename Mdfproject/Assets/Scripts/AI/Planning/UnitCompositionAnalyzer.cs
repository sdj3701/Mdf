using System;
using System.Linq;

public static class UnitCompositionAnalyzer
{
    public static PrepareArmyComposition AnalyzePlayer(PlayerManager player)
    {
        var composition = new PrepareArmyComposition("field");
        var field = player != null ? player.fieldManager : null;
        if (field == null)
        {
            return composition;
        }

        var units = field.GetAlliedUnitsOnField();
        if (units == null)
        {
            return composition;
        }

        foreach (var unit in units.Where(unit => unit != null))
        {
            composition.AddUnit(unit.Data, unit.starLevel);
        }

        return composition;
    }

    public static PrepareUnitRole Classify(UnitData unitData)
    {
        if (unitData == null)
        {
            return PrepareUnitRole.Unknown;
        }

        if (AppearsToBeHealer(unitData))
        {
            return PrepareUnitRole.Healer;
        }

        switch (unitData.unitType)
        {
            case UnitType.Melee:
                return PrepareUnitRole.Melee;
            case UnitType.Ranged:
                return PrepareUnitRole.RangedDps;
            default:
                return PrepareUnitRole.Unknown;
        }
    }

    public static string RoleName(PrepareUnitRole role)
    {
        switch (role)
        {
            case PrepareUnitRole.Melee:
                return "Melee";
            case PrepareUnitRole.RangedDps:
                return "RangedDps";
            case PrepareUnitRole.Healer:
                return "Healer";
            default:
                return "Unknown";
        }
    }

    public static string StableUnitKey(UnitData unitData)
    {
        if (unitData == null)
        {
            return "unknown-unit";
        }

        if (!string.IsNullOrWhiteSpace(unitData.name))
        {
            return unitData.name.Trim();
        }

        if (!string.IsNullOrWhiteSpace(unitData.unitName))
        {
            return unitData.unitName.Trim();
        }

        return "unnamed-unit";
    }

    public static bool AppearsToBeHealer(UnitData unitData)
    {
        if (unitData == null)
        {
            return false;
        }

        if (ContainsHealToken(unitData.name) || ContainsHealToken(unitData.unitName))
        {
            return true;
        }

        var skills = unitData.skillsByStarLevel;
        if (skills == null)
        {
            return false;
        }

        return skills.Any(ContainsHealToken);
    }

    private static bool ContainsHealToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string normalized = value.Trim().ToLowerInvariant();
        return normalized.Contains("skill_heal") ||
               normalized.Contains("heal") ||
               normalized.Contains("healer") ||
               normalized.Contains("cleric");
    }
}
