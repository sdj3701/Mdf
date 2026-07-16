using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public sealed class DefenderSkillPolicy
{
    public bool TryChoose(PlayerManager defender, out DefenderSkillDecision decision)
    {
        var gm = GameManagers.Instance;
        PlayerManager attacker = null;
        if (gm != null &&
            defender != null &&
            gm.TryGetBattleOpponentSnapshot(defender.playerId, out int opponentId))
        {
            attacker = gm.GetPlayer(opponentId);
        }

        return TryChoose(defender, attacker, out decision);
    }

    public bool TryChoose(PlayerManager defender, PlayerManager attacker, out DefenderSkillDecision decision)
    {
        decision = DefenderSkillDecision.Observe(
            defender != null ? defender.playerId : -1,
            "defender_missing");

        if (defender == null)
        {
            LogEvaluated(decision);
            return false;
        }

        var gm = GameManagers.Instance;
        if (gm == null || !BattleCommandValidator.IsBattlePhase(gm))
        {
            decision = DefenderSkillDecision.Observe(defender.playerId, "not_battle_phase");
            LogEvaluated(decision);
            return false;
        }

        if (!BattleCommandValidator.IsCurrentBattleDefender(defender))
        {
            decision = DefenderSkillDecision.Observe(defender.playerId, "not_current_battle_defender");
            LogEvaluated(decision);
            return false;
        }

        var heatmap = BattleHeatmap.Create(attacker, defender);
        DefenderSkillDecision best = DefenderSkillDecision.Observe(defender.playerId, "no_ready_manual_skill");
        foreach (var unit in EnumerateDefenderUnits(defender))
        {
            if (unit == null || unit.Object == null || !unit.Object.IsValid)
            {
                continue;
            }

            uint unitNetworkId = unit.Object.Id.Raw;
            if (!ActivateSkillCommand.TryValidate(
                    gm,
                    defender.playerId,
                    unitNetworkId,
                    CommandExecutionScope.ServerAuthorityOnly,
                    "defender_skill_policy",
                    requireStateAuthority: false,
                    out _,
                    out SkillData skillData,
                    out BattleCommandResult validation))
            {
                LogEvaluated(DefenderSkillDecision.Observe(
                    defender.playerId,
                    validation.ErrorCode,
                    unitNetworkId,
                    skillData,
                    validation.Message));
                continue;
            }

            int targetCount = Mathf.Max(0, unit.CountSkillTargets(skillData));
            float radius = Mathf.Max(1f, skillData.range);
            int threatCount = heatmap.CountAlliedMonstersNear(unit.transform.position, radius);
            int bossThreatCount = heatmap.AlliedMonstersInRadius(unit.transform.position, radius)
                .Count(monster => monster.IsBoss);
            float lowHealthBonus = unit.MaxHealth > 0f && unit.CurrentHealth / unit.MaxHealth <= 0.4f ? 40f : 0f;
            float highValueBonus = unit.Data != null ? Mathf.Max(0f, unit.Data.cost) * 5f : 0f;
            float nearGoalBonus = heatmap.GoalProximityScore(unit.transform.position, radius) * 30f;
            float score =
                targetCount * 100f +
                threatCount * 30f +
                bossThreatCount * 80f +
                lowHealthBonus +
                highValueBonus +
                nearGoalBonus;

            if (score < skillData.aiMinSkillValue)
            {
                LogEvaluated(DefenderSkillDecision.Observe(
                    defender.playerId,
                    $"below_min_value score={score:F1} min={skillData.aiMinSkillValue:F1}",
                    unitNetworkId,
                    skillData));
                continue;
            }

            var candidate = DefenderSkillDecision.ForCommand(
                defender.playerId,
                unitNetworkId,
                skillData,
                score,
                $"manual_skill targets={targetCount} threats={threatCount} bossThreats={bossThreatCount}",
                new Dictionary<string, object>
                {
                    { "targetCount", targetCount },
                    { "threatCount", threatCount },
                    { "bossThreatCount", bossThreatCount },
                    { "lowHealthBonus", lowHealthBonus.ToString("F1") },
                    { "highValueBonus", highValueBonus.ToString("F1") },
                    { "nearGoalBonus", nearGoalBonus.ToString("F1") }
                });
            LogEvaluated(candidate);

            if (!best.HasCommand || candidate.Score > best.Score)
            {
                best = candidate;
            }
        }

        decision = best;
        if (!decision.HasCommand)
        {
            LogEvaluated(decision);
            return false;
        }

        LogSelected(decision);
        return true;
    }

    private static IEnumerable<Unit> EnumerateDefenderUnits(PlayerManager defender)
    {
        if (defender == null)
        {
            return Enumerable.Empty<Unit>();
        }

        if (defender.fieldManager != null)
        {
            return defender.fieldManager.GetAlliedUnitsOnField()
                .Where(unit => unit != null)
                .OrderBy(UnitStableKey);
        }

        return (defender.ownedUnits ?? new List<Unit>())
            .Where(unit => unit != null)
            .OrderBy(UnitStableKey);
    }

    private static string UnitStableKey(Unit unit)
    {
        if (unit == null)
        {
            return "unit=null";
        }

        string dataName = unit.Data != null ? unit.Data.name : "unknown";
        uint networkId = unit.Object != null && unit.Object.IsValid ? unit.Object.Id.Raw : 0;
        return $"{dataName};star={unit.starLevel};net={networkId}";
    }

    private static void LogEvaluated(DefenderSkillDecision decision)
    {
        Log("defender_skill_evaluated", decision, decision.HasCommand ? "pass" : "info");
    }

    private static void LogSelected(DefenderSkillDecision decision)
    {
        Log("defender_skill_selected", decision, "pass");
    }

    private static void Log(string phase, DefenderSkillDecision decision, string result)
    {
        if (!MPTestLogger.IsEnabled)
        {
            return;
        }

        var fields = decision.JournalFields != null
            ? new Dictionary<string, object>(decision.JournalFields)
            : new Dictionary<string, object>();
        fields["playerId"] = decision.PlayerId;
        fields["unitNetworkId"] = decision.UnitNetworkId;
        fields["skill"] = decision.SkillName;
        fields["score"] = decision.Score.ToString("F1");
        fields["commandType"] = decision.CommandType;

        MPTestLogger.Log(phase, result, decision.CommandType, decision.Reason, fields);
    }
}

public readonly struct DefenderSkillDecision
{
    public readonly int PlayerId;
    public readonly uint UnitNetworkId;
    public readonly string SkillName;
    public readonly float Score;
    public readonly string Reason;
    public readonly string CommandType;
    public readonly ActivateSkillCommand Command;
    public readonly IReadOnlyDictionary<string, object> JournalFields;

    public bool HasCommand => Command != null;

    private DefenderSkillDecision(
        int playerId,
        uint unitNetworkId,
        string skillName,
        float score,
        string reason,
        ActivateSkillCommand command,
        IReadOnlyDictionary<string, object> journalFields)
    {
        PlayerId = playerId;
        UnitNetworkId = unitNetworkId;
        SkillName = string.IsNullOrWhiteSpace(skillName) ? "unknown" : skillName;
        Score = score;
        Reason = reason;
        Command = command;
        CommandType = command != null ? global::CommandType.ActivateSkill.ToString() : "Observe";
        JournalFields = journalFields;
    }

    public static DefenderSkillDecision ForCommand(
        int playerId,
        uint unitNetworkId,
        SkillData skillData,
        float score,
        string reason,
        IReadOnlyDictionary<string, object> journalFields)
    {
        string skillName = skillData != null && !string.IsNullOrWhiteSpace(skillData.skillName)
            ? skillData.skillName
            : skillData != null ? skillData.name : "unknown";
        return new DefenderSkillDecision(
            playerId,
            unitNetworkId,
            skillName,
            score,
            reason,
            new ActivateSkillCommand(playerId, unitNetworkId),
            journalFields);
    }

    public static DefenderSkillDecision Observe(
        int playerId,
        string reason,
        uint unitNetworkId = 0,
        SkillData skillData = null,
        string message = null)
    {
        string skillName = skillData != null && !string.IsNullOrWhiteSpace(skillData.skillName)
            ? skillData.skillName
            : skillData != null ? skillData.name : "unknown";
        var fields = new Dictionary<string, object>();
        if (!string.IsNullOrWhiteSpace(message))
        {
            fields["message"] = message;
        }

        return new DefenderSkillDecision(playerId, unitNetworkId, skillName, 0f, reason, null, fields);
    }
}
