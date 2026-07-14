using System.Collections.Generic;
using System.Linq;
using AI.BehaviorTree.Nodes.Actions;
using UnityEngine;

public sealed class BattleDecisionPolicy : IMdfDecisionPolicy
{
    private const float MinimumBattleCommandLeadTime = 1.25f;
    private const float SpawnCellReservationSeconds = 3f;

    private readonly ScrollTargetEvaluator _scrollTargetEvaluator = new ScrollTargetEvaluator();
    private readonly DefenderSkillPolicy _defenderSkillPolicy = new DefenderSkillPolicy();
    private readonly Dictionary<int, float> _nextBattleDecisionAt = new Dictionary<int, float>();
    private readonly Dictionary<int, List<SpawnCellReservation>> _recentSpawnCells =
        new Dictionary<int, List<SpawnCellReservation>>();

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
            Arm(context.PlayerId, CommandType.RequestSyncData);
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
                Arm(context.PlayerId, decision.CommandType);
                LogDecision(decision, "pass");
                return true;
            }
        }

        if (context.IsCurrentBattleDefender && TryChooseDefenderSkill(context, out decision))
        {
            Arm(context.PlayerId, decision.CommandType);
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

        // The policy cooldown starts when a request is emitted, while the authoritative
        // cadence starts only after the asynchronous spawn transaction commits. Polling
        // this gate avoids an early duplicate request without delaying the next legal spawn.
        if (!attacker.IsBattleSpawnCadenceReady(out _))
        {
            return false;
        }

        var affordablePool = attacker.AttackMonsterPool
            .Where(attacker.CanAffordAttackMonster)
            .ToList();
        if (affordablePool.Count == 0)
        {
            return false;
        }

        HashSet<Vector2Int> reservedSpawnCells = GetActiveSpawnCellReservations(
            attacker.playerId,
            defender.playerId);
        var strategy = new AIAttackStrategy(
            defender.fieldManager,
            attacker,
            ResolveSpawnAreaLayer(attacker),
            reservedSpawnCells);
        var plan = strategy.BuildSpawnPlan(affordablePool);
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

        ReserveSpawnCells(
            attacker.playerId,
            defender.playerId,
            order.ReservedNavigationCells);

        var command = new BattleSpawnMonsterCommand(
            attacker.playerId,
            defender.playerId,
            poolSlotIndex,
            order.SpawnPosition,
            1,
            "battle_decision_policy_spawn",
            attacker.AppliedAttackMonsterPoolRevision,
            attacker.AppliedBlackMagicRevision);

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
                { "blackMagicCost", order.PoolEntry.MonsterData != null ? order.PoolEntry.MonsterData.blackMagicCost : 0 },
                { "blackMagicCurrent", attacker.AppliedBlackMagicCurrent },
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

    private void Arm(int playerId, CommandType commandType)
    {
        if (playerId >= 0)
        {
            _nextBattleDecisionAt[playerId] = Time.time +
                BattleSpawnCadence.ResolvePolicyCooldown(commandType);
        }
    }

    private HashSet<Vector2Int> GetActiveSpawnCellReservations(int attackerPlayerId, int defenderPlayerId)
    {
        if (!_recentSpawnCells.TryGetValue(attackerPlayerId, out List<SpawnCellReservation> reservations))
        {
            return null;
        }

        float now = Time.time;
        HashSet<Vector2Int> active = null;
        for (int i = reservations.Count - 1; i >= 0; i--)
        {
            SpawnCellReservation reservation = reservations[i];
            if (reservation.ExpiresAt <= now)
            {
                reservations.RemoveAt(i);
                continue;
            }

            if (reservation.DefenderPlayerId == defenderPlayerId)
            {
                if (active == null)
                {
                    active = new HashSet<Vector2Int>();
                }
                active.Add(reservation.NavigationCell);
            }
        }

        if (reservations.Count == 0)
        {
            _recentSpawnCells.Remove(attackerPlayerId);
        }
        return active;
    }

    private void ReserveSpawnCells(
        int attackerPlayerId,
        int defenderPlayerId,
        IReadOnlyList<Vector2Int> navigationCells)
    {
        if (navigationCells == null || navigationCells.Count == 0)
        {
            return;
        }

        if (!_recentSpawnCells.TryGetValue(attackerPlayerId, out List<SpawnCellReservation> reservations))
        {
            reservations = new List<SpawnCellReservation>(navigationCells.Count);
            _recentSpawnCells.Add(attackerPlayerId, reservations);
        }

        float expiresAt = Time.time + SpawnCellReservationSeconds;
        for (int cellIndex = 0; cellIndex < navigationCells.Count; cellIndex++)
        {
            Vector2Int navigationCell = navigationCells[cellIndex];
            bool refreshed = false;
            for (int reservationIndex = 0; reservationIndex < reservations.Count; reservationIndex++)
            {
                if (reservations[reservationIndex].DefenderPlayerId == defenderPlayerId &&
                    reservations[reservationIndex].NavigationCell == navigationCell)
                {
                    reservations[reservationIndex] = new SpawnCellReservation(
                        defenderPlayerId,
                        navigationCell,
                        expiresAt);
                    refreshed = true;
                    break;
                }
            }

            if (!refreshed)
            {
                reservations.Add(new SpawnCellReservation(defenderPlayerId, navigationCell, expiresAt));
            }
        }
    }

    private static void LogDecision(MdfDecision decision, string result)
    {
        if (!MPTestLogger.IsEnabled)
        {
            return;
        }

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

    private readonly struct SpawnCellReservation
    {
        public readonly int DefenderPlayerId;
        public readonly Vector2Int NavigationCell;
        public readonly float ExpiresAt;

        public SpawnCellReservation(int defenderPlayerId, Vector2Int navigationCell, float expiresAt)
        {
            DefenderPlayerId = defenderPlayerId;
            NavigationCell = navigationCell;
            ExpiresAt = expiresAt;
        }
    }
}
