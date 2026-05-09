using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public sealed class ScrollTargetEvaluator
{
    private const float VisualYOffset = 0.75f;

    public bool TryFindBestTarget(
        PlayerManager attacker,
        PlayerManager defender,
        MagicScrollData scroll,
        out ScrollTargetResult result)
    {
        return TryFindBestTarget(scroll, BattleHeatmap.Create(attacker, defender), out result);
    }

    public bool TryFindBestTarget(
        MagicScrollData scroll,
        BattleHeatmap heatmap,
        out ScrollTargetResult result)
    {
        if (!CanEvaluate(scroll, heatmap, out string failureReason))
        {
            result = ScrollTargetResult.Failure(scroll, failureReason);
            LogEvaluated(result, heatmap);
            return false;
        }

        result = scroll.tacticalRole == MagicScrollTacticalRole.Buff ||
                 scroll.tacticalRole == MagicScrollTacticalRole.Heal
            ? EvaluateBuffTarget(scroll, heatmap)
            : EvaluateDamageOrDebuffTarget(scroll, heatmap);

        if (!result.Success || result.Score < scroll.aiMinValue)
        {
            string reason = result.Success
                ? $"below_min_value score={result.Score:F1} min={scroll.aiMinValue:F1}"
                : result.Reason;
            result = result.WithFailure(reason);
            LogEvaluated(result, heatmap);
            return false;
        }

        LogEvaluated(result, heatmap);
        LogSelected(result);
        return true;
    }

    public ScrollTargetResult EvaluateDamageOrDebuffTarget(MagicScrollData scroll, BattleHeatmap heatmap)
    {
        float radius = ResolveRadius(scroll);
        var candidates = heatmap.BuildOffensiveCandidatePositions(radius);
        if (candidates.Count == 0)
        {
            return ScrollTargetResult.Failure(scroll, "no_offensive_candidates");
        }

        ScrollTargetResult best = ScrollTargetResult.Failure(scroll, "no_scored_offensive_candidate");
        foreach (var position in candidates)
        {
            if (!heatmap.IsWithinTotalFieldBounds(position))
            {
                continue;
            }

            var units = heatmap.DefenderUnitsInRadius(position, radius);
            var monsters = heatmap.AlliedMonstersInRadius(position, radius);
            int engagementCount = heatmap.CountEngagementsNear(position, radius);
            int alliedNearbyCount = monsters.Count;

            if (scroll.targetDomain == MagicScrollTargetDomain.EnemyUnits && units.Count == 0)
            {
                continue;
            }

            if (scroll.targetDomain == MagicScrollTargetDomain.EnemyUnitsNearAlliedMonsters &&
                (units.Count == 0 || (engagementCount == 0 && alliedNearbyCount == 0)))
            {
                continue;
            }

            float defenderUnitValue = units.Sum(unit => unit.Value);
            int lowHpCount = units.Count(unit => unit.IsLowHealth);
            int highValueCount = units.Count(unit => unit.IsHighValue);
            int rangedCount = units.Count(unit => unit.IsRanged);
            float emptyPenalty = units.Count == 0 ? 100f : 0f;
            float score =
                units.Count * 100f +
                defenderUnitValue * 15f +
                engagementCount * 35f +
                alliedNearbyCount * 10f +
                lowHpCount * 20f +
                highValueCount * 10f +
                rangedCount * 5f -
                emptyPenalty;

            var candidate = ScrollTargetResult.SuccessResult(
                scroll,
                position,
                position + Vector3.up * VisualYOffset,
                score,
                $"offensive units={units.Count} monsters={alliedNearbyCount} engagements={engagementCount} lowHp={lowHpCount}",
                BuildJournalFields(
                    scroll,
                    radius,
                    units.Count,
                    alliedNearbyCount,
                    engagementCount,
                    highValueCount,
                    lowHpCount,
                    score));

            if (!best.Success || candidate.Score > best.Score)
            {
                best = candidate;
            }
        }

        return best;
    }

    public ScrollTargetResult EvaluateBuffTarget(MagicScrollData scroll, BattleHeatmap heatmap)
    {
        float radius = ResolveRadius(scroll);
        var candidates = heatmap.BuildBuffCandidatePositions(radius);
        if (candidates.Count == 0)
        {
            return ScrollTargetResult.Failure(scroll, "no_buff_candidates");
        }

        ScrollTargetResult best = ScrollTargetResult.Failure(scroll, "no_scored_buff_candidate");
        foreach (var position in candidates)
        {
            if (!heatmap.IsWithinTotalFieldBounds(position))
            {
                continue;
            }

            var monsters = heatmap.AlliedMonstersInRadius(position, radius);
            if (scroll.targetDomain == MagicScrollTargetDomain.AlliedMonsters && monsters.Count == 0)
            {
                continue;
            }

            int bossCount = monsters.Count(monster => monster.IsBoss);
            int destroyerCount = monsters.Count(monster => monster.IsDestroyer);
            int tankCount = monsters.Count(monster => monster.IsTank);
            int injuredCount = monsters.Count(monster => monster.HealthRatio <= 0.65f);
            int engagedCount = heatmap.CountEngagementsNear(position, radius);
            float monsterValue = monsters.Sum(monster => monster.Value);
            float nearGoalScore = heatmap.GoalProximityScore(position, radius);
            float score =
                monsters.Count * 100f +
                monsterValue * 3f +
                bossCount * 80f +
                destroyerCount * 50f +
                tankCount * 30f +
                engagedCount * 25f +
                nearGoalScore * 20f +
                injuredCount * 30f;

            var candidate = ScrollTargetResult.SuccessResult(
                scroll,
                position,
                position + Vector3.up * VisualYOffset,
                score,
                $"buff monsters={monsters.Count} boss={bossCount} destroyer={destroyerCount} tank={tankCount} engaged={engagedCount}",
                BuildJournalFields(
                    scroll,
                    radius,
                    0,
                    monsters.Count,
                    engagedCount,
                    bossCount + destroyerCount + tankCount,
                    injuredCount,
                    score));

            if (!best.Success || candidate.Score > best.Score)
            {
                best = candidate;
            }
        }

        return best;
    }

    private static bool CanEvaluate(MagicScrollData scroll, BattleHeatmap heatmap, out string reason)
    {
        if (scroll == null)
        {
            reason = "scroll_null";
            return false;
        }

        if (!scroll.canAiUse)
        {
            reason = "scroll_ai_disabled";
            return false;
        }

        if (scroll.skillData == null)
        {
            reason = "scroll_skill_missing";
            return false;
        }

        if (scroll.skillData.range <= 0f)
        {
            reason = "scroll_range_invalid";
            return false;
        }

        if (heatmap == null)
        {
            reason = "heatmap_null";
            return false;
        }

        if (scroll.tacticalRole == MagicScrollTacticalRole.Utility)
        {
            reason = "utility_scroll_requires_explicit_policy";
            return false;
        }

        reason = null;
        return true;
    }

    private static float ResolveRadius(MagicScrollData scroll)
    {
        return scroll != null && scroll.skillData != null
            ? Mathf.Max(0.5f, scroll.skillData.range)
            : 0.5f;
    }

    private static Dictionary<string, object> BuildJournalFields(
        MagicScrollData scroll,
        float radius,
        int defenderUnits,
        int alliedMonsters,
        int engagements,
        int highValue,
        int lowHpOrInjured,
        float score)
    {
        return new Dictionary<string, object>
        {
            { "scroll", ScrollKey(scroll) },
            { "role", scroll != null ? scroll.tacticalRole.ToString() : "unknown" },
            { "targetDomain", scroll != null ? scroll.targetDomain.ToString() : "unknown" },
            { "radius", radius.ToString("F2") },
            { "score", score.ToString("F1") },
            { "defenderUnits", defenderUnits },
            { "alliedMonsters", alliedMonsters },
            { "engagements", engagements },
            { "highValue", highValue },
            { "lowHpOrInjured", lowHpOrInjured }
        };
    }

    private static void LogEvaluated(ScrollTargetResult result, BattleHeatmap heatmap)
    {
        var fields = result.JournalFields != null
            ? new Dictionary<string, object>(result.JournalFields)
            : new Dictionary<string, object>();
        fields["attackerPlayerId"] = heatmap != null && heatmap.Attacker != null ? heatmap.Attacker.playerId : -1;
        fields["defenderPlayerId"] = heatmap != null && heatmap.Defender != null ? heatmap.Defender.playerId : -1;
        fields["x"] = BattleHeatmap.Bucket(result.GameplayPosition.x);
        fields["z"] = BattleHeatmap.Bucket(result.GameplayPosition.z);

        MPTestLogger.Log(
            "scroll_target_evaluated",
            result.Success ? "pass" : "info",
            ScrollKey(result.Scroll),
            result.Reason,
            fields);
    }

    private static void LogSelected(ScrollTargetResult result)
    {
        var fields = result.JournalFields != null
            ? new Dictionary<string, object>(result.JournalFields)
            : new Dictionary<string, object>();
        fields["x"] = BattleHeatmap.Bucket(result.GameplayPosition.x);
        fields["z"] = BattleHeatmap.Bucket(result.GameplayPosition.z);

        MPTestLogger.Log(
            "scroll_target_selected",
            "pass",
            ScrollKey(result.Scroll),
            result.Reason,
            fields);
    }

    private static string ScrollKey(MagicScrollData scroll)
    {
        if (scroll == null)
        {
            return "scroll_null";
        }

        return !string.IsNullOrWhiteSpace(scroll.scrollName)
            ? scroll.scrollName
            : scroll.name;
    }
}

public readonly struct ScrollTargetResult
{
    public readonly bool Success;
    public readonly MagicScrollData Scroll;
    public readonly Vector3 GameplayPosition;
    public readonly Vector3 VisualPosition;
    public readonly float Score;
    public readonly string Reason;
    public readonly IReadOnlyDictionary<string, object> JournalFields;

    private ScrollTargetResult(
        bool success,
        MagicScrollData scroll,
        Vector3 gameplayPosition,
        Vector3 visualPosition,
        float score,
        string reason,
        IReadOnlyDictionary<string, object> journalFields)
    {
        Success = success;
        Scroll = scroll;
        GameplayPosition = gameplayPosition;
        VisualPosition = visualPosition;
        Score = score;
        Reason = reason;
        JournalFields = journalFields;
    }

    public static ScrollTargetResult SuccessResult(
        MagicScrollData scroll,
        Vector3 gameplayPosition,
        Vector3 visualPosition,
        float score,
        string reason,
        IReadOnlyDictionary<string, object> journalFields)
    {
        return new ScrollTargetResult(true, scroll, gameplayPosition, visualPosition, score, reason, journalFields);
    }

    public static ScrollTargetResult Failure(MagicScrollData scroll, string reason)
    {
        return new ScrollTargetResult(false, scroll, Vector3.zero, Vector3.zero, 0f, reason, null);
    }

    public ScrollTargetResult WithFailure(string reason)
    {
        return new ScrollTargetResult(false, Scroll, GameplayPosition, VisualPosition, Score, reason, JournalFields);
    }
}
