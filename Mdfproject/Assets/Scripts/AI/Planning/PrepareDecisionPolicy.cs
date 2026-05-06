using System;
using System.Collections.Generic;
using System.Linq;
using AI.BehaviorTree;
using UnityEngine;

public sealed class PrepareDecisionPolicy : IMdfDecisionPolicy
{
    private delegate bool CommandChooser(MdfDecisionContext context, out MdfDecision decision);

    private readonly string _persona;
    private readonly System.Random _random;
    private readonly bool _preferScrollAugment;
    private readonly Dictionary<int, WallPlanCache> _wallPlans = new Dictionary<int, WallPlanCache>();

    public PrepareDecisionPolicy(string persona = "balanced", int seed = 0, bool preferScrollAugment = false)
    {
        _persona = NormalizePersona(persona);
        _random = new System.Random(seed);
        _preferScrollAugment = preferScrollAugment;
    }

    public bool TryChoose(MdfDecisionContext context, out MdfDecision decision)
    {
        decision = null;
        if (context == null || context.Actor == null || context.GameManagers == null)
        {
            return false;
        }

        if (context.GameState != GameManagers.GameState.Prepare)
        {
            decision = MdfDecision.Observe(context, "observe_non_prepare");
            LogDecision(decision, "info");
            return false;
        }

        if (_persona == "passive")
        {
            decision = MdfDecision.Observe(context, "passive_observe");
            LogDecision(decision, "info");
            return false;
        }

        foreach (var chooser in GetChooserOrder())
        {
            if (chooser(context, out decision))
            {
                LogDecision(decision, "pass");
                return true;
            }
        }

        decision = MdfDecision.Observe(context, "no_legal_prepare_command");
        LogDecision(decision, "info");
        return false;
    }

    private IEnumerable<CommandChooser> GetChooserOrder()
    {
        yield return TryChooseAugment;

        switch (_persona)
        {
            case "maze":
                yield return TryChooseWall;
                yield return TryChooseBuy;
                yield return TryChooseMove;
                yield return TryChooseReroll;
                break;
            case "shop":
                yield return TryChooseBuy;
                yield return TryChooseReroll;
                yield return TryChooseMove;
                yield return TryChooseWall;
                break;
            case "unit":
                yield return TryChooseBuy;
                yield return TryChooseMove;
                yield return TryChooseReroll;
                yield return TryChooseWall;
                break;
            default:
                yield return TryChooseWall;
                yield return TryChooseBuy;
                yield return TryChooseMove;
                yield return TryChooseReroll;
                break;
        }
    }

    private bool TryChooseAugment(MdfDecisionContext context, out MdfDecision decision)
    {
        decision = null;
        var player = context.Actor;
        var augments = player.augmentManager != null ? player.augmentManager.GetPresentedAugments() : null;
        if (augments == null || augments.Count == 0)
        {
            return false;
        }

        int index = Mathf.Clamp(PickAugmentIndex(augments.Count), 0, augments.Count - 1);
        if (_preferScrollAugment)
        {
            int scrollIndex = augments.FindIndex(augment =>
                augment != null &&
                augment.effectType == EffectType.GrantMagicScroll &&
                augment.magicScrollData != null &&
                augment.magicScrollData.canAiUse);
            if (scrollIndex >= 0)
            {
                index = scrollIndex;
            }
        }

        var selectedAugment = augments[index];
        decision = MdfDecision.ForCommand(
            context,
            new SelectAugmentCommand(player.playerId, index),
            CommandType.SelectAugment,
            "presented_augment_available",
            $"index={index}",
            100f,
            new Dictionary<string, object>
            {
                { "augmentIndex", index },
                { "augment", selectedAugment != null ? selectedAugment.name : "unknown" },
                { "effectType", selectedAugment != null ? selectedAugment.effectType.ToString() : "unknown" },
                { "preferScrollAugment", _preferScrollAugment }
            });
        return true;
    }

    private bool TryChooseWall(MdfDecisionContext context, out MdfDecision decision)
    {
        decision = null;
        var player = context.Actor;
        if (player.GetWallCount() <= 0 || player.fieldManager == null || !player.IsReadyForPlayerActions)
        {
            return false;
        }

        if (context.IsServerAi && !AIPacer.Ready(player.playerId, AIPacer.CatWall))
        {
            return false;
        }

        if (!TryGetNextWallPosition(player, out var position))
        {
            return false;
        }

        if (context.IsServerAi)
        {
            AIPacer.Arm(player.playerId, AIPacer.CatWall, 0.35f, 0.75f);
        }

        decision = MdfDecision.ForCommand(
            context,
            new PlaceWallCommand(player.playerId, position),
            CommandType.PlaceWall,
            "maze_policy_next_wall",
            $"{position.x},{position.y},{position.z}",
            80f,
            new Dictionary<string, object>
            {
                { "x", position.x },
                { "y", position.y }
            });
        return true;
    }

    private bool TryChooseBuy(MdfDecisionContext context, out MdfDecision decision)
    {
        decision = null;
        var player = context.Actor;
        if (player.shopManager == null || !player.shopManager.IsDatabaseLoaded)
        {
            return false;
        }

        if (context.IsServerAi && !AIPacer.Ready(player.playerId, AIPacer.CatBuy))
        {
            return false;
        }

        int gold = player.GetGold();
        int bestSlot = -1;
        float bestScore = 0f;
        foreach (var pair in player.shopManager.GetAvailableShopItems())
        {
            var item = pair.Value;
            if (item.UnitData == null || gold < item.CalculatedCost)
            {
                continue;
            }

            float score = ScoreShopItem(player, item);
            if (score > bestScore)
            {
                bestScore = score;
                bestSlot = pair.Key;
            }
        }

        if (bestSlot < 0 || bestScore <= 0.1f)
        {
            return false;
        }

        if (context.IsServerAi)
        {
            AIPacer.Arm(player.playerId, AIPacer.CatBuy, 0.5f, 1.0f);
        }

        decision = MdfDecision.ForCommand(
            context,
            new BuyUnitCommand(player.playerId, bestSlot),
            CommandType.BuyUnit,
            "best_affordable_score",
            $"slot={bestSlot};score={bestScore:F2}",
            bestScore,
            new Dictionary<string, object>
            {
                { "shopSlot", bestSlot },
                { "score", bestScore.ToString("F2") }
            });
        return true;
    }

    private bool TryChooseMove(MdfDecisionContext context, out MdfDecision decision)
    {
        decision = null;
        var player = context.Actor;
        var field = player.fieldManager;
        if (field == null || player.astarGrid == null || player.goalTransform == null)
        {
            return false;
        }

        if (context.IsServerAi && !AIPacer.Ready(player.playerId, AIPacer.CatMove))
        {
            return false;
        }

        var units = field.GetAlliedUnitsOnField()
            .Where(unit => unit != null && unit.Data != null)
            .OrderBy(unit => unit.Data.unitType == UnitType.Ranged ? 0 : 1)
            .ThenBy(unit => unit.Data.unitName)
            .ToArray();

        foreach (var unit in units)
        {
            Vector3Int? from = field.GetUnitPosition(unit);
            if (!from.HasValue)
            {
                continue;
            }

            Vector3Int? to = field.FindBestSpotForAI(unit.Data, null, units.ToList(), null, from.Value);
            if (!to.HasValue || to.Value == from.Value || !field.IsValidGridPosition(to.Value))
            {
                continue;
            }

            if (field.GetUnitAt(to.Value) != null)
            {
                continue;
            }

            if (unit.Data.unitType == UnitType.Melee && field.HasWallAt(to.Value))
            {
                continue;
            }

            if (context.IsServerAi)
            {
                AIPacer.Arm(player.playerId, AIPacer.CatMove, 0.4f, 0.9f);
            }

            decision = MdfDecision.ForCommand(
                context,
                new MoveUnitCommand(player.playerId, from.Value, to.Value),
                CommandType.MoveUnit,
                "best_unit_reposition",
                $"{from.Value.x},{from.Value.y}->{to.Value.x},{to.Value.y}",
                50f,
                new Dictionary<string, object>
                {
                    { "from", $"{from.Value.x},{from.Value.y}" },
                    { "to", $"{to.Value.x},{to.Value.y}" },
                    { "unit", unit.Data.unitName }
                });
            return true;
        }

        return false;
    }

    private bool TryChooseReroll(MdfDecisionContext context, out MdfDecision decision)
    {
        decision = null;
        var player = context.Actor;
        if (player.shopManager == null || !player.shopManager.IsDatabaseLoaded)
        {
            return false;
        }

        if (context.IsServerAi && !AIPacer.Ready(player.playerId, AIPacer.CatReroll))
        {
            return false;
        }

        int cost = player.shopManager.GetRerollCost();
        if (player.GetGold() < cost)
        {
            return false;
        }

        if (context.IsServerAi)
        {
            AIPacer.Arm(player.playerId, AIPacer.CatReroll, 0.8f, 1.5f);
        }

        decision = MdfDecision.ForCommand(
            context,
            new RerollShopCommand(player.playerId),
            CommandType.RerollShop,
            "no_better_purchase_gold_allows_reroll",
            $"cost={cost}",
            20f,
            new Dictionary<string, object> { { "cost", cost } });
        return true;
    }

    private int PickAugmentIndex(int count)
    {
        if (count <= 1)
        {
            return 0;
        }

        return _persona == "balanced" || _persona == "maze" ? 0 : _random.Next(count);
    }

    private float ScoreShopItem(PlayerManager player, ShopItem item)
    {
        float score = item.StarLevel * 10f;
        if (item.UnitData != null)
        {
            score += item.UnitData.cost;
            score += item.UnitData.baseAttackDamage * 0.05f;
            score += item.UnitData.attackRange * 0.5f;
            score += item.UnitData.attackSpeed;
            if (_persona == "unit")
            {
                score += item.UnitData.unitType == UnitType.Ranged ? 2f : 1f;
            }
        }

        int goldAfter = player.GetGold() - item.CalculatedCost;
        score += Mathf.Clamp(goldAfter, 0, 10) * 0.1f;
        return score;
    }

    private bool TryGetNextWallPosition(PlayerManager player, out Vector3Int position)
    {
        position = default(Vector3Int);
        var field = player.fieldManager;
        if (field == null)
        {
            return false;
        }

        var plan = GetWallPlan(player);
        foreach (var candidate in plan)
        {
            if (!IsLegalWallCandidate(player, candidate))
            {
                continue;
            }

            position = candidate;
            return true;
        }

        for (int y = 0; y < field.gridSize.y; y++)
        {
            for (int x = 0; x < field.gridSize.x; x++)
            {
                var candidate = new Vector3Int(x, y, 0);
                if (!IsLegalWallCandidate(player, candidate))
                {
                    continue;
                }

                position = candidate;
                return true;
            }
        }

        return false;
    }

    private IReadOnlyList<Vector3Int> GetWallPlan(PlayerManager player)
    {
        var field = player.fieldManager;
        string signature = SafeWallSignature(field);
        int playerId = player.playerId;
        if (_wallPlans.TryGetValue(playerId, out var cached) && cached.Signature == signature)
        {
            return cached.Order;
        }

        var order = new List<Vector3Int>();
        try
        {
            var result = MazePlanner.PlanWalls(field, player);
            if (result != null && result.BuildOrder != null)
            {
                order.AddRange(result.BuildOrder);
            }
        }
        catch (Exception ex)
        {
            MPTestLogger.Fail("prepare_decision_policy", "maze_plan_failed", ex.GetType().Name, new Dictionary<string, object>
            {
                { "playerId", playerId }
            });
        }

        _wallPlans[playerId] = new WallPlanCache
        {
            Signature = signature,
            Order = order
        };
        return order;
    }

    private bool IsLegalWallCandidate(PlayerManager player, Vector3Int candidate)
    {
        var field = player.fieldManager;
        if (field == null || !field.IsValidGridPosition(candidate) || field.HasWallAt(candidate))
        {
            return false;
        }

        if (field.GetUnitAt(candidate) != null)
        {
            return false;
        }

        Vector3Int goalCell = field.WorldToGridInt(player.goalTransform != null ? player.goalTransform.position : Vector3.zero);
        return candidate != goalCell;
    }

    private static string SafeWallSignature(FieldManager field)
    {
        if (field == null)
        {
            return "no-field";
        }

        try
        {
            return field.BuildWallCellHash();
        }
        catch
        {
            return "wall-signature-error";
        }
    }

    private static string NormalizePersona(string persona)
    {
        switch ((persona ?? "balanced").Trim().ToLowerInvariant())
        {
            case "maze":
            case "shop":
            case "unit":
            case "passive":
                return persona.Trim().ToLowerInvariant();
            default:
                return "balanced";
        }
    }

    private static void LogDecision(MdfDecision decision, string result)
    {
        MPTestLogger.Log(
            "prepare_decision_policy",
            result,
            decision != null ? decision.CommandTypeName : "unknown",
            decision != null ? decision.Reason : null,
            MdfDecisionJournalFields.Build(decision, null, "policy", BattleCommandResult.Accepted(
                decision != null ? decision.CommandType : CommandType.RequestSyncData,
                decision != null ? decision.PlayerId : -1)));
    }

    private sealed class WallPlanCache
    {
        public string Signature;
        public List<Vector3Int> Order;
    }
}
