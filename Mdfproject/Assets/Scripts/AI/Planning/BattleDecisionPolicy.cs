using System.Collections.Generic;
using System.Linq;
using AI.BehaviorTree.Nodes.Actions;
using UnityEngine;

public sealed class BattleDecisionPolicy : IMdfDecisionPolicy
{
    private const float DefaultBattleActionCooldown = 0.75f;
    private const float MinimumBattleCommandLeadTime = 1.25f;

    private readonly ScrollTargetEvaluator _scrollTargetEvaluator = new ScrollTargetEvaluator();
    private readonly DefenderSkillPolicy _defenderSkillPolicy = new DefenderSkillPolicy();
    private readonly Dictionary<int, float> _nextBattleDecisionAt = new Dictionary<int, float>();

    public bool TryChoose(MdfDecisionContext context, out MdfDecision decision)
    {
        decision = null;
        if (context == null || context.Actor == null || context.GameManagers == null)
        {
            return false;
        }

        if (!context.IsBattlePhase)
        {
            decision = MdfDecision.Observe(context, "observe_non_battle");
            LogDecision(decision, "info");
            return false;
        }

        if (context.PhaseTimerRemaining <= MinimumBattleCommandLeadTime)
        {
            decision = MdfDecision.Observe(context, "battle_phase_ending");
            Arm(context.PlayerId);
            LogDecision(decision, "info");
            return false;
        }

        if (!IsReady(context.PlayerId))
        {
            decision = MdfDecision.Observe(context, "battle_decision_cooldown");
            return false;
        }

        if (context.IsCurrentBattleAttacker)
        {
            if (TryChooseScroll(context, out decision) || TryChooseSpawn(context, out decision))
            {
                Arm(context.PlayerId);
                LogDecision(decision, "pass");
                return true;
            }
        }

        if (context.IsCurrentBattleDefender && TryChooseDefenderSkill(context, out decision))
        {
            Arm(context.PlayerId);
            LogDecision(decision, "pass");
            return true;
        }

        decision = MdfDecision.Observe(context, "no_legal_battle_decision");
        LogDecision(decision, "info");
        return false;
    }

    private bool TryChooseScroll(MdfDecisionContext context, out MdfDecision decision)
    {
        decision = null;
        var actor = context.Actor;
        var opponent = context.Opponent;
        if (actor == null || opponent == null || actor.OwnedScrolls == null || actor.OwnedScrolls.Count == 0)
        {
            return false;
        }

        ScrollCandidate best = default(ScrollCandidate);
        bool hasBest = false;
        var heatmap = BattleHeatmap.Create(actor, opponent);
        for (int slot = 0; slot < actor.OwnedScrolls.Count; slot++)
        {
            var scroll = actor.OwnedScrolls[slot];
            if (scroll == null || !scroll.canAiUse)
            {
                continue;
            }

            if (!_scrollTargetEvaluator.TryFindBestTarget(scroll, heatmap, out var result))
            {
                continue;
            }

            if (!hasBest || result.Score > best.Score)
            {
                best = new ScrollCandidate
                {
                    Slot = slot,
                    Scroll = scroll,
                    Target = result.GameplayPosition,
                    Score = result.Score,
                    Reason = result.Reason,
                    Fields = result.JournalFields
                };
                hasBest = true;
            }
        }

        if (!hasBest)
        {
            return false;
        }

        var command = new UseMagicScrollCommand(
            actor.playerId,
            best.Slot,
            best.Target,
            "battle_decision_policy_scroll",
            actor.AppliedOwnedMagicScrollRevision);

        var fields = best.Fields != null
            ? new Dictionary<string, object>(best.Fields)
            : new Dictionary<string, object>();
        fields["scrollSlot"] = best.Slot;
        fields["scroll"] = best.Scroll != null ? best.Scroll.name : "unknown";

        decision = MdfDecision.ForUseMagicScroll(
            context,
            command,
            best.Target,
            best.Reason,
            best.Score,
            fields);
        return true;
    }

    private bool TryChooseSpawn(MdfDecisionContext context, out MdfDecision decision)
    {
        decision = null;
        var attacker = context.Actor;
        var defender = context.Opponent;
        if (attacker == null ||
            defender == null ||
            defender.fieldManager == null ||
            attacker.AttackMonsterPool == null ||
            attacker.AttackMonsterPool.Count == 0 ||
            attacker.HasPendingAttackMonsterPoolCommand)
        {
            return false;
        }

        if (!attacker.HasAppliedCurrentAttackMonsterPoolSnapshot)
        {
            attacker.RPC_RequestSyncData();
            return false;
        }

        var strategy = new AIAttackStrategy(
            defender.fieldManager,
            attacker,
            ResolveSpawnAreaLayer(attacker));
        var plan = strategy.BuildSpawnPlan(attacker.AttackMonsterPool.ToList());
        var order = plan.Phases
            .SelectMany(phase => phase.Orders)
            .FirstOrDefault(candidate => candidate != null &&
                                         candidate.PoolEntry != null &&
                                         !candidate.PoolEntry.IsEmpty &&
                                         candidate.Count > 0);
        if (order == null)
        {
            return false;
        }

        int poolSlotIndex = attacker.AttackMonsterPool.IndexOf(order.PoolEntry);
        if (poolSlotIndex < 0)
        {
            return false;
        }

        var command = new BattleSpawnMonsterCommand(
            attacker.playerId,
            defender.playerId,
            poolSlotIndex,
            order.SpawnPosition,
            1,
            "battle_decision_policy_spawn",
            attacker.AppliedAttackMonsterPoolRevision);

        decision = MdfDecision.ForBattleSpawnMonster(
            context,
            command,
            order.SpawnPosition,
            "ai_spawn_plan_step",
            Mathf.Max(1f, order.Count),
            new Dictionary<string, object>
            {
                { "poolSlotIndex", poolSlotIndex },
                { "plannedCount", order.Count },
                { "monster", order.PoolEntry.MonsterData != null ? order.PoolEntry.MonsterData.name : "unknown" },
                { "defenderPlayerId", defender.playerId }
            });
        return true;
    }

    private bool TryChooseDefenderSkill(MdfDecisionContext context, out MdfDecision decision)
    {
        decision = null;
        if (!_defenderSkillPolicy.TryChoose(context.Actor, context.Opponent, out var defenderDecision) ||
            !defenderDecision.HasCommand)
        {
            return false;
        }

        decision = MdfDecision.ForCommand(
            context,
            defenderDecision.Command,
            CommandType.ActivateSkill,
            defenderDecision.Reason,
            $"unit={defenderDecision.UnitNetworkId}",
            defenderDecision.Score,
            defenderDecision.JournalFields);
        return true;
    }

    private static LayerMask ResolveSpawnAreaLayer(PlayerManager attacker)
    {
        var attackSequenceManager = attacker != null ? attacker.GetComponent<AttackSequenceManager>() : null;
        return attackSequenceManager != null ? attackSequenceManager.SpawnAreaLayer : default;
    }

    private bool IsReady(int playerId)
    {
        return playerId < 0 ||
               !_nextBattleDecisionAt.TryGetValue(playerId, out float next) ||
               Time.time >= next;
    }

    private void Arm(int playerId)
    {
        if (playerId >= 0)
        {
            _nextBattleDecisionAt[playerId] = Time.time + DefaultBattleActionCooldown;
        }
    }

    private static void LogDecision(MdfDecision decision, string result)
    {
        MPTestLogger.Log(
            "battle_decision_policy",
            result,
            decision != null ? decision.CommandTypeName : "unknown",
            decision != null ? decision.Reason : null,
            MdfDecisionJournalFields.Build(decision, null, "policy", BattleCommandResult.Accepted(
                decision != null ? decision.CommandType : CommandType.RequestSyncData,
                decision != null ? decision.PlayerId : -1)));
    }

    private struct ScrollCandidate
    {
        public int Slot;
        public MagicScrollData Scroll;
        public Vector3 Target;
        public float Score;
        public string Reason;
        public IReadOnlyDictionary<string, object> Fields;
    }
}
