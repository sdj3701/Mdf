using System;
using System.Collections.Generic;
using System.Linq;
using AI.BehaviorTree;
using UnityEngine;

public sealed class PrepareDecisionPolicy : IMdfDecisionPolicy
{
    private const int MinSoldSlotsBeforeReroll = 3;
    private const float BuyDecisionScoreThreshold = 15f;
    private const float HighValuePurchaseScoreThreshold = 18f;

    private delegate bool CommandChooser(MdfDecisionContext context, PrepareArmyComposition composition, out MdfDecision decision);

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
            decision = MdfDecision.Observe(context, "passive_observe", BuildPrepareJournalFields(context, UnitCompositionAnalyzer.AnalyzePlayer(context.Actor), null, null));
            LogDecision(decision, "info");
            return false;
        }

        var composition = UnitCompositionAnalyzer.AnalyzePlayer(context.Actor);
        foreach (var chooser in GetChooserOrder(composition))
        {
            if (chooser(context, composition, out decision))
            {
                LogDecision(decision, "pass");
                return true;
            }
        }

        var rerollGate = EvaluateRerollGate(context, composition, null);
        decision = MdfDecision.Observe(context, "no_legal_prepare_command", BuildPrepareJournalFields(context, composition, null, rerollGate));
        LogDecision(decision, "info");
        return false;
    }

    private IEnumerable<CommandChooser> GetChooserOrder(PrepareArmyComposition composition)
    {
        yield return TryChooseAugment;

        switch (_persona)
        {
            case "maze":
                if (ShouldPrioritizeBuyBeforeWall(composition, _persona))
                {
                    yield return TryChooseBuy;
                    yield return TryChooseMove;
                    yield return TryChooseWall;
                }
                else
                {
                    yield return TryChooseWall;
                    yield return TryChooseBuy;
                    yield return TryChooseMove;
                }
                yield return TryChooseReroll;
                break;
            case "shop":
                yield return TryChooseBuy;
                yield return TryChooseMove;
                yield return TryChooseWall;
                yield return TryChooseReroll;
                break;
            case "unit":
                yield return TryChooseBuy;
                yield return TryChooseMove;
                yield return TryChooseWall;
                yield return TryChooseReroll;
                break;
            default:
                yield return TryChooseBuy;
                yield return TryChooseMove;
                yield return TryChooseWall;
                yield return TryChooseReroll;
                break;
        }
    }

    private bool TryChooseAugment(MdfDecisionContext context, PrepareArmyComposition composition, out MdfDecision decision)
    {
        decision = null;
        var player = context.Actor;
        var presentedSnapshot = player.GetPresentedAugmentSnapshotNames();
        if (presentedSnapshot == null || presentedSnapshot.Length == 0)
        {
            return false;
        }

        var augments = player.augmentManager != null ? player.augmentManager.GetPresentedAugments() : null;
        if (augments == null || augments.Count == 0)
        {
            return false;
        }

        int selectableCount = Mathf.Min(augments.Count, presentedSnapshot.Length);
        if (selectableCount <= 0)
        {
            return false;
        }

        int index = Mathf.Clamp(PickAugmentIndex(selectableCount), 0, selectableCount - 1);
        if (_preferScrollAugment)
        {
            int scrollIndex = augments.Take(selectableCount).ToList().FindIndex(augment =>
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
            MergeFields(BuildPrepareJournalFields(context, composition, null, null), new Dictionary<string, object>
            {
                { "augmentIndex", index },
                { "augment", selectedAugment != null ? selectedAugment.name : "unknown" },
                { "effectType", selectedAugment != null ? selectedAugment.effectType.ToString() : "unknown" },
                { "presentedSnapshotCount", presentedSnapshot.Length },
                { "preferScrollAugment", _preferScrollAugment }
            }));
        return true;
    }

    private bool TryChooseWall(MdfDecisionContext context, PrepareArmyComposition composition, out MdfDecision decision)
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

        if (!ShouldAllowWallFocus(composition))
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
            MergeFields(BuildPrepareJournalFields(context, composition, null, null), new Dictionary<string, object>
            {
                { "x", position.x },
                { "y", position.y },
                { "buyBeforeWallRequired", ShouldPrioritizeBuyBeforeWall(composition, _persona) }
            }));
        return true;
    }

    private bool TryChooseBuy(MdfDecisionContext context, PrepareArmyComposition composition, out MdfDecision decision)
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
        float bestScore = float.MinValue;
        PrepareShopDecisionScore bestBreakdown = null;
        var shopItems = player.shopManager.GetCurrentShopItems();
        for (int slot = 0; slot < shopItems.Count; slot++)
        {
            if (IsShopSlotSoldForPolicy(player, slot))
            {
                continue;
            }

            var item = shopItems[slot];
            if (item.UnitData == null || gold < item.CalculatedCost)
            {
                continue;
            }

            var score = ScoreShopItem(player, item, slot, composition);
            if (score.FinalScore > bestScore)
            {
                bestScore = score.FinalScore;
                bestSlot = slot;
                bestBreakdown = score;
            }
        }

        if (bestSlot < 0 || bestScore < BuyDecisionScoreThreshold)
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
            BuildPrepareJournalFields(context, composition, bestBreakdown, null));
        return true;
    }

    private bool TryChooseMove(MdfDecisionContext context, PrepareArmyComposition composition, out MdfDecision decision)
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
                MergeFields(BuildPrepareJournalFields(context, composition, null, null), new Dictionary<string, object>
                {
                    { "from", $"{from.Value.x},{from.Value.y}" },
                    { "to", $"{to.Value.x},{to.Value.y}" },
                    { "unit", unit.Data.unitName }
                }));
            return true;
        }

        return false;
    }

    private bool TryChooseReroll(MdfDecisionContext context, PrepareArmyComposition composition, out MdfDecision decision)
    {
        decision = null;
        var gate = EvaluateRerollGate(context, composition, null);
        var player = context.Actor;
        if (!gate.CanReroll)
        {
            return false;
        }

        if (context.IsServerAi && !AIPacer.Ready(player.playerId, AIPacer.CatReroll))
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
            "reroll_gate_passed_late_shop_action",
            $"cost={gate.RerollCost};sold={gate.SoldSlotCount}/{gate.ShopSlotCount}",
            20f,
            BuildPrepareJournalFields(context, composition, null, gate));
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

    private PrepareShopDecisionScore ScoreShopItem(
        PlayerManager player,
        ShopItem item,
        int shopSlot,
        PrepareArmyComposition composition)
    {
        int gold = player != null ? player.GetGold() : 0;
        return ScoreShopItemForTest(item, composition, gold, _persona, shopSlot);
    }

    public static PrepareShopDecisionScore ScoreShopItemForTest(
        ShopItem item,
        PrepareArmyComposition composition,
        int playerGold,
        string persona = "balanced",
        int shopSlot = -1)
    {
        composition = composition ?? new PrepareArmyComposition("field");
        var unitData = item.UnitData;
        PrepareUnitRole role = UnitCompositionAnalyzer.Classify(unitData);
        int starLevel = Mathf.Max(1, item.StarLevel);
        int calculatedCost = item.CalculatedCost;

        var score = new PrepareShopDecisionScore
        {
            UnitKey = UnitCompositionAnalyzer.StableUnitKey(unitData),
            UnitRole = role,
            ShopSlot = shopSlot,
            StarLevel = starLevel,
            Cost = calculatedCost,
            MatchingSameUnitSameStarCount = composition.CountMatchingSameUnitSameStar(unitData, starLevel)
        };

        if (unitData != null)
        {
            score.BaseQualityScore =
                starLevel * 10f +
                unitData.cost +
                unitData.baseHealth * 0.01f +
                unitData.baseAttackDamage * 0.05f +
                unitData.attackRange * 0.5f +
                unitData.attackSpeed;
        }

        int roleDeficit = composition.DeficitForRole(role);
        int totalDistance = composition.CompositionDistanceToTarget;
        score.RoleDeficitBonus = roleDeficit > 0 ? roleDeficit * 18f + totalDistance * 2f : 0f;

        int projectedOverTarget = composition.ProjectedOverTargetForRole(role);
        score.RoleOverTargetPenalty = projectedOverTarget > 0 ? -12f * projectedOverTarget : 0f;

        if (score.MatchingSameUnitSameStarCount >= 2)
        {
            score.MergeBonus = 90f;
        }
        else if (score.MatchingSameUnitSameStarCount == 1)
        {
            score.MergeBonus = 24f;
        }

        string normalizedPersona = NormalizePersona(persona);
        if (normalizedPersona == "unit")
        {
            score.PersonaBonus = role == PrepareUnitRole.RangedDps ? 3f : 2f;
        }
        else if (normalizedPersona == "shop")
        {
            score.PersonaBonus = 2f;
        }
        else if (normalizedPersona == "maze" && !composition.HasMinimumArmyCore)
        {
            score.PersonaBonus = 4f;
        }

        int goldAfter = playerGold - calculatedCost;
        int desiredReserve = composition.HasMinimumArmyCore ? 2 : 0;
        score.GoldReservePenalty = goldAfter < desiredReserve
            ? -(desiredReserve - goldAfter) * 3f
            : Mathf.Clamp(goldAfter, 0, 10) * 0.1f;

        score.FinalScore =
            score.BaseQualityScore +
            score.RoleDeficitBonus +
            score.RoleOverTargetPenalty +
            score.MergeBonus +
            score.PersonaBonus +
            score.GoldReservePenalty;

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

    private PrepareRerollGateResult EvaluateRerollGate(
        MdfDecisionContext context,
        PrepareArmyComposition composition,
        PrepareShopDecisionScore knownBestAffordablePurchase)
    {
        var player = context != null ? context.Actor : null;
        var shop = player != null ? player.shopManager : null;
        int gold = player != null ? player.GetGold() : 0;
        int rerollCost = shop != null ? shop.GetRerollCost() : 0;
        int shopSlotCount = GetShopSlotCountForPolicy(player);
        int soldSlotCount = GetSoldSlotCountForPolicy(player);
        var bestPurchase = knownBestAffordablePurchase ?? FindBestAffordablePurchase(player, composition);

        return EvaluateRerollGateForTest(
            shopReady: shop != null && shop.IsDatabaseLoaded,
            isPreparePhase: context != null && context.GameState == GameManagers.GameState.Prepare,
            playerReady: player != null && player.IsReadyForPlayerActions,
            gold: gold,
            rerollCost: rerollCost,
            shopSlotCount: shopSlotCount,
            soldSlotCount: soldSlotCount,
            bestAffordablePurchaseScore: bestPurchase != null ? bestPurchase.FinalScore : float.MinValue,
            bestAffordableUnitKey: bestPurchase != null ? bestPurchase.UnitKey : null);
    }

    public static PrepareRerollGateResult EvaluateRerollGateForTest(
        bool shopReady,
        bool isPreparePhase,
        bool playerReady,
        int gold,
        int rerollCost,
        int shopSlotCount,
        int soldSlotCount,
        float bestAffordablePurchaseScore = float.MinValue,
        string bestAffordableUnitKey = null)
    {
        int normalizedShopSlots = Mathf.Max(0, shopSlotCount);
        int normalizedSoldSlots = Mathf.Clamp(soldSlotCount, 0, normalizedShopSlots);
        var result = new PrepareRerollGateResult
        {
            CanReroll = false,
            Reason = "unknown",
            SoldSlotCount = normalizedSoldSlots,
            ShopSlotCount = normalizedShopSlots,
            UnsoldSlotCount = Mathf.Max(0, normalizedShopSlots - normalizedSoldSlots),
            Gold = gold,
            RerollCost = Mathf.Max(0, rerollCost),
            BestAffordablePurchaseScore = bestAffordablePurchaseScore > float.MinValue / 2f ? bestAffordablePurchaseScore : 0f,
            BestAffordableUnitKey = bestAffordableUnitKey
        };

        if (!shopReady)
        {
            result.Reason = "shop_not_ready";
            return result;
        }

        if (!isPreparePhase)
        {
            result.Reason = "not_prepare_phase";
            return result;
        }

        if (!playerReady)
        {
            result.Reason = "player_not_ready_for_actions";
            return result;
        }

        if (normalizedShopSlots <= 0)
        {
            result.Reason = "shop_empty";
            return result;
        }

        if (gold < result.RerollCost)
        {
            result.Reason = "insufficient_gold_for_reroll";
            return result;
        }

        if (normalizedSoldSlots < MinSoldSlotsBeforeReroll)
        {
            result.Reason = "sold_slots_below_3";
            return result;
        }

        if (bestAffordablePurchaseScore >= HighValuePurchaseScoreThreshold)
        {
            result.Reason = "high_value_affordable_purchase_remaining";
            return result;
        }

        result.CanReroll = true;
        result.Reason = "gate_passed";
        return result;
    }

    private PrepareShopDecisionScore FindBestAffordablePurchase(PlayerManager player, PrepareArmyComposition composition)
    {
        if (player == null || player.shopManager == null || !player.shopManager.IsDatabaseLoaded)
        {
            return null;
        }

        int gold = player.GetGold();
        PrepareShopDecisionScore best = null;
        var shopItems = player.shopManager.GetCurrentShopItems();
        for (int slot = 0; slot < shopItems.Count; slot++)
        {
            if (IsShopSlotSoldForPolicy(player, slot))
            {
                continue;
            }

            var item = shopItems[slot];
            if (item.UnitData == null || gold < item.CalculatedCost)
            {
                continue;
            }

            var score = ScoreShopItem(player, item, slot, composition);
            if (best == null || score.FinalScore > best.FinalScore)
            {
                best = score;
            }
        }

        return best;
    }

    private static int GetShopSlotCountForPolicy(PlayerManager player)
    {
        if (player == null)
        {
            return 0;
        }

        if (player.TryGetShopSnapshot(out string[] unitKeys, out _, out _, out _, out _) && unitKeys != null && unitKeys.Length > 0)
        {
            return unitKeys.Length;
        }

        return player.shopManager != null ? player.shopManager.GetShopSlotCount() : 0;
    }

    private static int GetSoldSlotCountForPolicy(PlayerManager player)
    {
        if (player == null)
        {
            return 0;
        }

        if (player.TryGetShopSnapshot(out string[] unitKeys, out _, out bool[] soldFlags, out _, out _) &&
            unitKeys != null &&
            soldFlags != null)
        {
            int count = 0;
            int limit = Mathf.Min(unitKeys.Length, soldFlags.Length);
            for (int i = 0; i < limit; i++)
            {
                if (soldFlags[i])
                {
                    count++;
                }
            }

            return count;
        }

        return player.shopManager != null ? player.shopManager.GetSoldSlotCount() : 0;
    }

    private static bool IsShopSlotSoldForPolicy(PlayerManager player, int slot)
    {
        if (player == null || slot < 0)
        {
            return true;
        }

        if (player.TryGetShopSnapshot(out string[] unitKeys, out _, out bool[] soldFlags, out _, out _) &&
            unitKeys != null &&
            slot < unitKeys.Length)
        {
            return soldFlags != null && slot < soldFlags.Length && soldFlags[slot];
        }

        return player.shopManager == null || player.shopManager.IsSlotSold(slot);
    }

    public static bool ShouldPrioritizeBuyBeforeWall(PrepareArmyComposition composition, string persona)
    {
        if (composition == null)
        {
            return true;
        }

        string normalizedPersona = NormalizePersona(persona);
        if (normalizedPersona != "maze")
        {
            return true;
        }

        return !composition.HasMinimumArmyCore ||
               composition.FieldUnitCount < PrepareArmyComposition.MinimumCoreUnitCount ||
               composition.MeleeCount <= 0 ||
               composition.RangedDpsCount + composition.HealerCount <= 0;
    }

    private static bool ShouldAllowWallFocus(PrepareArmyComposition composition)
    {
        if (composition == null || !composition.HasMinimumArmyCore)
        {
            return false;
        }

        return composition.FieldUnitCount >= PrepareArmyComposition.TargetTotalUnits &&
               composition.CompositionDistanceToTarget == 0;
    }

    private static Dictionary<string, object> BuildPrepareJournalFields(
        MdfDecisionContext context,
        PrepareArmyComposition composition,
        PrepareShopDecisionScore buyScore,
        PrepareRerollGateResult rerollGate)
    {
        var fields = composition != null
            ? composition.ToJournalFields()
            : new PrepareArmyComposition("field").ToJournalFields();

        var player = context != null ? context.Actor : null;
        var shop = player != null ? player.shopManager : null;
        if (shop != null)
        {
            int shopSlotCount = GetShopSlotCountForPolicy(player);
            int soldSlotCount = GetSoldSlotCountForPolicy(player);
            fields["soldSlotCount"] = soldSlotCount;
            fields["shopSlotCount"] = shopSlotCount;
            fields["unsoldSlotCount"] = Mathf.Max(0, shopSlotCount - soldSlotCount);
        }
        else
        {
            fields["soldSlotCount"] = 0;
            fields["shopSlotCount"] = 0;
            fields["unsoldSlotCount"] = 0;
        }

        if (buyScore != null)
        {
            MergeFields(fields, buyScore.ToJournalFields());
        }
        else
        {
            fields["matchingSameUnitSameStarCount"] = 0;
            fields["compositionScore"] = "0.00";
            fields["mergeBonus"] = "0.00";
            fields["rolePenalty"] = "0.00";
            fields["goldReservePenalty"] = "0.00";
        }

        if (rerollGate != null)
        {
            MergeFields(fields, rerollGate.ToJournalFields());
        }
        else
        {
            fields["rerollGateReason"] = "not_evaluated";
        }

        return fields;
    }

    private static Dictionary<string, object> MergeFields(
        Dictionary<string, object> target,
        IReadOnlyDictionary<string, object> source)
    {
        target = target ?? new Dictionary<string, object>();
        if (source == null)
        {
            return target;
        }

        foreach (var pair in source)
        {
            target[pair.Key] = pair.Value;
        }

        return target;
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
