using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public sealed class PrepareDecisionPolicy : IMdfDecisionPolicy
{
    private const int MinSoldSlotsBeforeReroll = 3;
    private const float BuyDecisionScoreThreshold = 15f;
    private const float HighValuePurchaseScoreThreshold = 18f;
    private const int MinimumRepairReserveWalls = 1;

    private enum PrepareRoutineStage
    {
        Shopping,
        Maze,
        Placement
    }

    private delegate bool CommandChooser(MdfDecisionContext context, PrepareArmyComposition composition, out MdfDecision decision);

    private readonly string _persona;
    private readonly System.Random _random;
    private readonly bool _preferScrollAugment;
    private readonly Dictionary<int, WallPlanCache> _wallPlans = new Dictionary<int, WallPlanCache>();
    private readonly Dictionary<string, int> _lastMoveRoundByUnitKey = new Dictionary<string, int>();
    private readonly Dictionary<string, int> _wallUnblockMoveRoundByUnitKey = new Dictionary<string, int>();
    private readonly Dictionary<string, int> _pendingMoveSourceRoundByCellKey = new Dictionary<string, int>();
    private readonly Dictionary<string, int> _pendingMoveTargetRoundByCellKey = new Dictionary<string, int>();
    private readonly Dictionary<string, int> _pendingWallRoundByCellKey = new Dictionary<string, int>();
    private readonly HashSet<string> _builtWallCellKeys = new HashSet<string>();
    private readonly Dictionary<string, int> _pendingBuyRoundBySlotKey = new Dictionary<string, int>();
    private readonly Dictionary<string, PrepareRoutineStage> _prepareStageByPlayerRound = new Dictionary<string, PrepareRoutineStage>();

    public PrepareDecisionPolicy(string persona = "balanced", int seed = 0, bool preferScrollAugment = false)
    {
        _persona = NormalizePersona(persona);
        _random = new System.Random(seed);
        _preferScrollAugment = preferScrollAugment;
    }

    public PrepareDecisionPolicy(MdfBotProfile profile)
        : this(
            profile != null ? profile.Persona : MdfBotProfile.DefaultPersona,
            profile != null ? profile.Seed : 0,
            profile != null && profile.PreferScrollAugment)
    {
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
        foreach (var chooser in GetChooserOrder(context, composition))
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

    private IEnumerable<CommandChooser> GetChooserOrder(MdfDecisionContext context, PrepareArmyComposition composition)
    {
        yield return TryChooseAugment;

        var player = context != null ? context.Actor : null;
        int round = context != null && context.GameManagers != null ? context.GameManagers.currentRound : 0;
        PrunePrepareRoutineStageMemory(round);
        var stage = GetPrepareRoutineStage(player, round);

        if (stage == PrepareRoutineStage.Shopping)
        {
            yield return TryChooseBuy;
            yield return TryChooseReroll;
            yield return TryChooseMoveBlockingWall;
            yield return TryChooseWall;
            yield return TryChooseMoveFromDefaultArea;
            yield return TryChooseMove;
            yield break;
        }

        if (stage == PrepareRoutineStage.Maze)
        {
            yield return TryChooseMoveBlockingWall;
            yield return TryChooseWall;
            yield return TryChooseMoveFromDefaultArea;
            yield return TryChooseMove;
            yield break;
        }

        yield return TryChooseMoveFromDefaultArea;
        yield return TryChooseMove;
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
                augment.TryGetMagicScroll(out MagicScrollData scroll) &&
                scroll.canAiUse);
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
                { "effectType", selectedAugment != null && selectedAugment.GetEffect(0) != null ? selectedAugment.GetEffect(0).effectType.ToString() : "unknown" },
                { "presentedSnapshotCount", presentedSnapshot.Length },
                { "preferScrollAugment", _preferScrollAugment }
            }));
        return true;
    }

    private bool TryChooseWall(MdfDecisionContext context, PrepareArmyComposition composition, out MdfDecision decision)
    {
        decision = null;
        var player = context.Actor;
        if (GetTotalWallStock(player) <= 0 || player.fieldManager == null || !player.IsReadyForPlayerActions)
        {
            return false;
        }

        int round = context.GameManagers != null ? context.GameManagers.currentRound : 0;
        PruneWallMemory(round);

        if (!ShouldAllowWallFocus(player, composition, round))
        {
            return false;
        }

        bool isRepair = TryGetRepairWallPosition(player, round, out var position);
        if (!isRepair && !TryGetNextWallPosition(player, round, out position))
        {
            return false;
        }

        RememberPrepareRoutineStage(player, round, PrepareRoutineStage.Maze);
        RememberWallCandidate(player, position, round);
        decision = MdfDecision.ForCommand(
            context,
            new PlaceWallCommand(
                player.playerId,
                position,
                player.GetPermanentWallPlacementCount() > 0
                    ? WallPlacementKind.Permanent
                    : WallPlacementKind.Destructible),
            CommandType.PlaceWall,
            isRepair ? "maze_policy_repair_missing_wall" : "maze_policy_next_wall",
            $"{position.x},{position.y},{position.z}",
            isRepair ? 90f : 80f,
            MergeFields(BuildPrepareJournalFields(context, composition, null, null), new Dictionary<string, object>
            {
                { "x", position.x },
                { "y", position.y },
                { "buyBeforeWallRequired", ShouldPrioritizeBuyBeforeWall(composition, _persona) },
                { "wallControlFirst", ShouldPrioritizeWallControl(player, composition, round) },
                { "persistentWallBlueprint", true },
                { "repairMissingMazeWall", isRepair },
                { "prepareRoutineStage", PrepareRoutineStage.Maze.ToString() },
                { "pendingWallSuppression", true },
                { "wallKind", player.GetPermanentWallPlacementCount() > 0 ? WallPlacementKind.Permanent.ToString() : WallPlacementKind.Destructible.ToString() }
            }));
        return true;
    }

    private bool TryChooseMoveBlockingWall(MdfDecisionContext context, PrepareArmyComposition composition, out MdfDecision decision)
    {
        decision = null;
        var player = context.Actor;
        var field = player.fieldManager;
        if (field == null || player.astarGrid == null || player.goalTransform == null)
        {
            return false;
        }

        int round = context.GameManagers != null ? context.GameManagers.currentRound : 0;
        PruneMoveMemory(round);
        PruneWallMemory(round);

        if (!TryFindBlockingWallPlanUnit(player, round, out var blocker, out var from))
        {
            return false;
        }

        if (HasWallUnblockMovedThisRound(player, blocker, round))
        {
            return false;
        }

        var units = GetPolicyUnits(player, field)
            .Where(unit => unit != null && unit.Data != null)
            .ToList();
        var monsterPath = BuildMonsterPathContext(player);
        Vector3Int? to = field.FindBestSpotForAI(blocker.Data, monsterPath, units, null, from);
        if (!IsValidMoveDestination(player, blocker, to, from, round))
        {
            to = FindFallbackUnblockDestination(player, blocker, from, round);
        }

        if (!to.HasValue)
        {
            return false;
        }

        RememberPrepareRoutineStage(player, round, PrepareRoutineStage.Maze);
        RememberWallUnblockMovedThisRound(player, blocker, round);
        RememberMoveTarget(player, to.Value, round);
        decision = MdfDecision.ForCommand(
            context,
            new MoveUnitCommand(player.playerId, from, to.Value),
            CommandType.MoveUnit,
            "move_unit_off_wall_blueprint",
            $"{from.x},{from.y}->{to.Value.x},{to.Value.y}",
            70f,
            MergeFields(BuildPrepareJournalFields(context, composition, null, null), new Dictionary<string, object>
            {
                { "from", $"{from.x},{from.y}" },
                { "to", $"{to.Value.x},{to.Value.y}" },
                { "unit", blocker.Data.unitName },
                { "blockedWallBlueprint", true },
                { "persistentWallBlueprint", true },
                { "prepareRoutineStage", PrepareRoutineStage.Maze.ToString() },
                { "pathAwarePlacement", monsterPath != null && monsterPath.Count > 0 },
                { "monsterPathCount", monsterPath != null ? monsterPath.Count : 0 },
                { "wallUnblockDoesNotConsumePlacementMove", true },
                { "pendingMoveTargetSuppression", true }
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

        int gold = player.GetGold();
        int bestSlot = -1;
        float bestScore = float.MinValue;
        PrepareShopDecisionScore bestBreakdown = null;
        var shopItems = player.shopManager.GetCurrentShopItems();
        int round = context.GameManagers != null ? context.GameManagers.currentRound : 0;
        PruneBuyMemory(round);
        for (int slot = 0; slot < shopItems.Count; slot++)
        {
            if (IsShopSlotSoldForPolicy(player, slot))
            {
                continue;
            }

            if (IsPendingBuyCandidate(player, slot, round))
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

        RememberBuyCandidate(player, bestSlot, round);
        decision = MdfDecision.ForCommand(
            context,
            new BuyUnitCommand(player.playerId, bestSlot),
            CommandType.BuyUnit,
            "best_affordable_score",
            $"slot={bestSlot};score={bestScore:F2}",
            bestScore,
            MergeFields(BuildPrepareJournalFields(context, composition, bestBreakdown, null), new Dictionary<string, object>
            {
                { "pendingBuySuppression", true }
            }));
        return true;
    }

    private bool TryChooseMove(MdfDecisionContext context, PrepareArmyComposition composition, out MdfDecision decision)
    {
        return TryChooseMoveCore(
            context,
            composition,
            false,
            "best_unit_reposition",
            50f,
            out decision);
    }

    private bool TryChooseMoveFromDefaultArea(MdfDecisionContext context, PrepareArmyComposition composition, out MdfDecision decision)
    {
        return TryChooseMoveCore(
            context,
            composition,
            true,
            "default_area_unit_reposition",
            65f,
            out decision);
    }

    private bool TryChooseMoveCore(
        MdfDecisionContext context,
        PrepareArmyComposition composition,
        bool defaultAreaOnly,
        string reason,
        float score,
        out MdfDecision decision)
    {
        decision = null;
        var player = context.Actor;
        var field = player.fieldManager;
        if (field == null || player.astarGrid == null || player.goalTransform == null)
        {
            return false;
        }

        var units = GetPolicyUnits(player, field)
            .Where(unit => unit != null && unit.Data != null)
            .OrderBy(unit => unit.Data.unitType == UnitType.Ranged ? 0 : 1)
            .ThenBy(unit => GetMoveCandidatePriority(field, unit))
            .ThenBy(unit => unit.Data.unitName)
            .ToArray();
        var monsterPath = BuildMonsterPathContext(player);
        int round = context.GameManagers != null ? context.GameManagers.currentRound : 0;
        PruneMoveMemory(round);

        if (TryChoosePendingPurchasedUnitMove(
            context,
            composition,
            field,
            units,
            monsterPath,
            defaultAreaOnly,
            reason,
            score,
            round,
            out decision))
        {
            return true;
        }

        foreach (var unit in units)
        {
            Vector3Int? from = field.GetUnitPosition(unit);
            if (!from.HasValue)
            {
                continue;
            }

            bool isDefaultArea = IsLikelyPurchaseDefaultArea(field, from.Value);
            if (defaultAreaOnly && !isDefaultArea)
            {
                continue;
            }

            if (HasMovedThisRound(player, unit, round))
            {
                continue;
            }

            Vector3Int? to = field.FindBestSpotForAI(unit.Data, monsterPath, units.ToList(), null, from.Value);
            if (!to.HasValue || to.Value == from.Value || !field.IsValidGridPosition(to.Value))
            {
                continue;
            }

            if (IsPendingMoveTarget(player, to.Value, round))
            {
                continue;
            }

            if (field.IsUnitAt(to.Value))
            {
                continue;
            }

            if (unit.Data.unitType == UnitType.Melee && field.HasWallAt(to.Value))
            {
                continue;
            }

            RememberMovedThisRound(player, unit, round);
            RememberMoveTarget(player, to.Value, round);
            decision = MdfDecision.ForCommand(
                context,
                new MoveUnitCommand(player.playerId, from.Value, to.Value),
                CommandType.MoveUnit,
                reason,
                $"{from.Value.x},{from.Value.y}->{to.Value.x},{to.Value.y}",
                score,
                MergeFields(BuildPrepareJournalFields(context, composition, null, null), new Dictionary<string, object>
                {
                    { "from", $"{from.Value.x},{from.Value.y}" },
                    { "to", $"{to.Value.x},{to.Value.y}" },
                    { "unit", unit.Data.unitName },
                    { "defaultAreaPriority", isDefaultArea },
                    { "prepareRoutineStage", PrepareRoutineStage.Placement.ToString() },
                    { "pathAwarePlacement", monsterPath != null && monsterPath.Count > 0 },
                    { "monsterPathCount", monsterPath != null ? monsterPath.Count : 0 },
                    { "moveOncePerRound", true },
                    { "pendingMoveTargetSuppression", true }
                }));
            return true;
        }

        return false;
    }

    private bool TryChoosePendingPurchasedUnitMove(
        MdfDecisionContext context,
        PrepareArmyComposition composition,
        FieldManager field,
        Unit[] units,
        List<AstarNode> monsterPath,
        bool defaultAreaOnly,
        string reason,
        float score,
        int round,
        out MdfDecision decision)
    {
        decision = null;
        var player = context.Actor;
        if (field == null)
        {
            return false;
        }

        var pendingPlacements = field.GetPendingUnitPlacements()
            .OrderBy(entry => entry.UnitData != null && entry.UnitData.unitType == UnitType.Ranged ? 0 : 1)
            .ThenBy(entry => IsLikelyPurchaseDefaultArea(field, entry.Position) ? 0 : 1)
            .ThenBy(entry => entry.UnitData != null ? entry.UnitData.unitName : string.Empty)
            .ToList();

        foreach (var pending in pendingPlacements)
        {
            var from = pending.Position;
            var unitData = pending.UnitData;
            if (unitData == null)
            {
                continue;
            }

            bool isDefaultArea = IsLikelyPurchaseDefaultArea(field, from);
            if (defaultAreaOnly && !isDefaultArea)
            {
                continue;
            }

            if (field.HasPendingNetworkMoveFrom(from) || IsPendingMoveSource(player, from, round))
            {
                continue;
            }

            Vector3Int? to = field.FindBestSpotForAI(unitData, monsterPath, units.ToList(), null, from);
            if (!IsValidPendingMoveDestination(player, field, unitData, to, from, round))
            {
                continue;
            }

            RememberPendingMoveSource(player, from, round);
            RememberMoveTarget(player, to.Value, round);
            decision = MdfDecision.ForCommand(
                context,
                new MoveUnitCommand(player.playerId, from, to.Value),
                CommandType.MoveUnit,
                reason,
                $"{from.x},{from.y}->{to.Value.x},{to.Value.y}",
                score,
                MergeFields(BuildPrepareJournalFields(context, composition, null, null), new Dictionary<string, object>
                {
                    { "from", $"{from.x},{from.y}" },
                    { "to", $"{to.Value.x},{to.Value.y}" },
                    { "unit", unitData.unitName },
                    { "pendingPurchasedUnit", true },
                    { "defaultAreaPriority", isDefaultArea },
                    { "prepareRoutineStage", PrepareRoutineStage.Placement.ToString() },
                    { "pathAwarePlacement", monsterPath != null && monsterPath.Count > 0 },
                    { "monsterPathCount", monsterPath != null ? monsterPath.Count : 0 },
                    { "pendingMoveSourceSuppression", true },
                    { "pendingMoveTargetSuppression", true }
                }));
            return true;
        }

        return false;
    }

    private bool IsValidPendingMoveDestination(
        PlayerManager player,
        FieldManager field,
        UnitData unitData,
        Vector3Int? to,
        Vector3Int from,
        int round)
    {
        if (player == null || field == null || unitData == null || !to.HasValue)
        {
            return false;
        }

        if (to.Value == from || !field.IsValidGridPosition(to.Value))
        {
            return false;
        }

        if (IsPendingMoveTarget(player, to.Value, round))
        {
            return false;
        }

        if (field.IsUnitAt(to.Value))
        {
            return false;
        }

        if (unitData.unitType == UnitType.Melee && field.HasWallAt(to.Value))
        {
            return false;
        }

        return true;
    }

    private static int GetMoveCandidatePriority(FieldManager field, Unit unit)
    {
        if (field == null || unit == null)
        {
            return 100;
        }

        var position = field.GetUnitPosition(unit);
        if (!position.HasValue)
        {
            return 90;
        }

        if (IsLikelyPurchaseDefaultArea(field, position.Value))
        {
            return 0;
        }

        return unit.Data != null && unit.Data.unitType == UnitType.Ranged ? 10 : 20;
    }

    private static List<Unit> GetPolicyUnits(PlayerManager player, FieldManager field)
    {
        var result = new List<Unit>();
        var seen = new HashSet<Unit>();
        if (field != null)
        {
            foreach (var unit in field.GetAlliedUnitsOnField())
            {
                AddPolicyUnit(player, field, unit, result, seen);
            }
        }

        if (player != null && player.ownedUnits != null && field != null)
        {
            foreach (var unit in player.ownedUnits)
            {
                AddPolicyUnit(player, field, unit, result, seen);
            }
        }

        return result;
    }

    private static void AddPolicyUnit(
        PlayerManager player,
        FieldManager field,
        Unit unit,
        List<Unit> result,
        HashSet<Unit> seen)
    {
        if (unit == null || result == null || seen == null || !seen.Add(unit))
        {
            return;
        }

        if (!IsOwnedByPlayer(player, unit))
        {
            return;
        }

        if (field != null && !field.GetUnitPosition(unit).HasValue)
        {
            return;
        }

        result.Add(unit);
    }

    private static bool IsOwnedByPlayer(PlayerManager player, Unit unit)
    {
        return PlayerManager.IsUnitOwnedByPlayerForCommand(player, unit);
    }

    private static bool IsLikelyPurchaseDefaultArea(FieldManager field, Vector3Int cell)
    {
        if (field == null)
        {
            return false;
        }

        BoundsInt bounds = field.GetMapBounds();
        int minX = bounds.xMin;
        int minY = bounds.yMin;
        int maxX = bounds.xMax - 1;
        int maxY = bounds.yMax - 1;
        return cell.x <= minX + 1 ||
               cell.y <= minY + 1 ||
               cell.x >= maxX - 1 ||
               cell.y >= maxY - 1;
    }

    private static List<AstarNode> BuildMonsterPathContext(PlayerManager player)
    {
        var field = player != null ? player.fieldManager : null;
        var grid = player != null ? player.astarGrid : null;
        var goal = player != null ? player.goalTransform : null;
        if (field == null || grid == null || goal == null)
        {
            return null;
        }

        var startPositions = GetMonsterPathStartPositions(field);
        if (startPositions.Count == 0)
        {
            return null;
        }

        var disabledColliders = new List<Collider>();
        try
        {
            foreach (var unit in GetPolicyUnits(player, field))
            {
                if (unit == null)
                {
                    continue;
                }

                var collider = unit.GetComponentInChildren<Collider>();
                if (collider != null && collider.enabled)
                {
                    disabledColliders.Add(collider);
                    collider.enabled = false;
                }
            }

            Vector2Int goalPos = field.WorldToNavigationCell(goal.position);
            var innerPathByCell = new Dictionary<Vector2Int, AstarNode>();
            foreach (var startPos in startPositions)
            {
                if (!grid.FindPath(startPos, goalPos, ignoreWalls: false) ||
                    grid.FinalPath == null ||
                    grid.FinalPath.Count == 0)
                {
                    continue;
                }

                foreach (var node in field.ConvertNavigationPathToInnerField(grid.FinalPath))
                {
                    if (node == null)
                    {
                        continue;
                    }

                    var key = new Vector2Int(node.x, node.y);
                    if (!innerPathByCell.ContainsKey(key))
                    {
                        innerPathByCell[key] = node;
                    }
                }
            }

            return innerPathByCell.Count > 0
                ? innerPathByCell.Values.ToList()
                : null;
        }
        catch (Exception ex)
        {
            MPTestLogger.Log("prepare_decision_policy", "info", "path_context_unavailable", ex.GetType().Name, new Dictionary<string, object>
            {
                { "playerId", player != null ? player.playerId : -1 }
            });
            return null;
        }
        finally
        {
            foreach (var collider in disabledColliders)
            {
                if (collider != null)
                {
                    collider.enabled = true;
                }
            }
        }
    }

    private static List<Vector2Int> GetMonsterPathStartPositions(FieldManager field)
    {
        var starts = new List<Vector2Int>();
        if (field == null)
        {
            return starts;
        }

        if (field.TryGetSingleOpenEntryNavigationCell(out var singleEntry))
        {
            starts.Add(singleEntry);
            return starts;
        }

        foreach (var gap in field.GetOpenBorderGaps())
        {
            starts.Add(field.InnerCellToNavigationCell(gap));
        }

        return starts;
    }

    private bool TryChooseReroll(MdfDecisionContext context, PrepareArmyComposition composition, out MdfDecision decision)
    {
        decision = null;
        var gate = EvaluateRerollGate(context, composition, null);
        var player = context.Actor;
        int round = context.GameManagers != null ? context.GameManagers.currentRound : 0;
        if (!gate.CanReroll)
        {
            RememberPrepareRoutineStage(player, round, PrepareRoutineStage.Maze);
            return false;
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

    private bool TryGetNextWallPosition(PlayerManager player, int round, out Vector3Int position)
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
            if (IsPendingWallCandidate(player, candidate, round))
            {
                continue;
            }

            bool wasBuilt = HasBuiltWallCandidate(player, candidate);
            bool canSpend = wasBuilt
                ? GetTotalWallStock(player) > 0
                : GetTotalWallStock(player) > GetWallBuildReserve(player);
            if (!canSpend)
            {
                continue;
            }

            var blockingUnit = field.GetUnitAt(candidate);
            if (blockingUnit != null && IsBorderGapCandidate(field, candidate))
            {
                return false;
            }

            if (!IsLegalWallCandidate(player, candidate, round))
            {
                continue;
            }

            position = candidate;
            return true;
        }

        return false;
    }

    private bool TryGetRepairWallPosition(PlayerManager player, int round, out Vector3Int position)
    {
        position = default(Vector3Int);
        var field = player != null ? player.fieldManager : null;
        if (field == null || round < 2 || GetTotalWallStock(player) <= 0 || !HasRecordedBuiltWallCandidate(player))
        {
            return false;
        }

        string prefix = $"{player.playerId}:";
        var repairCandidates = _builtWallCellKeys
            .Where(key => key.StartsWith(prefix, StringComparison.Ordinal))
            .Select(ParseWallMemoryCell)
            .Where(candidate => candidate.HasValue)
            .Select(candidate => candidate.Value)
            .OrderBy(candidate => field.HasWallAt(candidate) ? 1 : 0)
            .ThenBy(candidate => candidate.x)
            .ThenBy(candidate => candidate.y)
            .ThenBy(candidate => candidate.z);

        foreach (var candidate in repairCandidates)
        {
            if (field.HasWallAt(candidate) ||
                IsPendingWallCandidate(player, candidate, round) ||
                !IsLegalWallCandidate(player, candidate, round))
            {
                continue;
            }

            position = candidate;
            return true;
        }

        return false;
    }

    private static Vector3Int? ParseWallMemoryCell(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return null;
        }

        int colonIndex = key.IndexOf(':');
        if (colonIndex < 0 || colonIndex + 1 >= key.Length)
        {
            return null;
        }

        string[] parts = key.Substring(colonIndex + 1).Split(',');
        if (parts.Length != 3 ||
            !int.TryParse(parts[0], out int x) ||
            !int.TryParse(parts[1], out int y) ||
            !int.TryParse(parts[2], out int z))
        {
            return null;
        }

        return new Vector3Int(x, y, z);
    }

    private IReadOnlyList<Vector3Int> GetWallPlan(PlayerManager player)
    {
        var field = player.fieldManager;
        string signature = SafeWallSignature(field);
        int playerId = player.playerId;
        int fieldInstanceId = field != null ? field.GetInstanceID() : 0;
        if (_wallPlans.TryGetValue(playerId, out var cached) &&
            cached.FieldInstanceId == fieldInstanceId &&
            cached.Signature == signature &&
            cached.Order != null &&
            cached.Order.Count > 0)
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
            FieldInstanceId = fieldInstanceId,
            Signature = signature,
            Order = order
        };
        return order;
    }

    private bool TryFindBlockingWallPlanUnit(PlayerManager player, int round, out Unit blocker, out Vector3Int position)
    {
        blocker = null;
        position = default(Vector3Int);
        var field = player != null ? player.fieldManager : null;
        if (field == null)
        {
            return false;
        }

        foreach (var candidate in GetWallPlan(player))
        {
            bool isBorderGap = IsBorderGapCandidate(field, candidate);
            if (field.HasWallAt(candidate) ||
                IsPendingWallCandidate(player, candidate, round) ||
                (isBorderGap && WouldCloseLastOpenBorderGap(player, field, candidate, round)))
            {
                continue;
            }

            bool wasBuilt = HasBuiltWallCandidate(player, candidate);
            bool canSpend = wasBuilt
                ? GetTotalWallStock(player) > 0
                : GetTotalWallStock(player) > GetWallBuildReserve(player);
            if (!canSpend)
            {
                continue;
            }

            var unit = field.GetUnitAt(candidate);
            if (unit == null || unit.Data == null || !IsOwnedByPlayer(player, unit))
            {
                continue;
            }

            blocker = unit;
            position = candidate;
            return true;
        }

        return false;
    }

    private bool IsLegalWallCandidate(PlayerManager player, Vector3Int candidate, int round)
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

        if (WouldCloseLastOpenBorderGap(player, field, candidate, round))
        {
            return false;
        }

        return !field.IsGoalCell(candidate);
    }

    private bool WouldCloseLastOpenBorderGap(PlayerManager player, FieldManager field, Vector3Int candidate, int round)
    {
        if (field == null)
        {
            return false;
        }

        if (!IsBorderGapCandidate(field, candidate))
        {
            return false;
        }

        var openGaps = field.GetOpenBorderGaps();
        if (!openGaps.Any(gap => gap.x == candidate.x && gap.y == candidate.y))
        {
            return false;
        }

        int pendingGapClosures = 0;
        foreach (var openGap in openGaps)
        {
            if (openGap.x == candidate.x && openGap.y == candidate.y)
            {
                continue;
            }

            if (IsPendingWallCandidate(player, openGap, round))
            {
                pendingGapClosures++;
            }
        }

        int remainingOpenGapsAfterCandidate = openGaps.Count - pendingGapClosures - 1;
        return remainingOpenGapsAfterCandidate < 1;
    }

    private static bool IsBorderGapCandidate(FieldManager field, Vector3Int candidate)
    {
        return field != null && field.GetBorderGapCells().Any(gap => gap.x == candidate.x && gap.y == candidate.y);
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

    private bool HasMovedThisRound(PlayerManager player, Unit unit, int round)
    {
        if (player == null || unit == null || round <= 0)
        {
            return false;
        }

        return _lastMoveRoundByUnitKey.TryGetValue(BuildMoveMemoryKey(player, unit), out int lastRound) &&
               lastRound == round;
    }

    private void RememberMovedThisRound(PlayerManager player, Unit unit, int round)
    {
        if (player == null || unit == null || round <= 0)
        {
            return;
        }

        _lastMoveRoundByUnitKey[BuildMoveMemoryKey(player, unit)] = round;
    }

    private bool HasWallUnblockMovedThisRound(PlayerManager player, Unit unit, int round)
    {
        if (player == null || unit == null || round <= 0)
        {
            return false;
        }

        return _wallUnblockMoveRoundByUnitKey.TryGetValue(BuildMoveMemoryKey(player, unit), out int lastRound) &&
               lastRound == round;
    }

    private void RememberWallUnblockMovedThisRound(PlayerManager player, Unit unit, int round)
    {
        if (player == null || unit == null || round <= 0)
        {
            return;
        }

        _wallUnblockMoveRoundByUnitKey[BuildMoveMemoryKey(player, unit)] = round;
    }

    private void PruneMoveMemory(int round)
    {
        if (round <= 0)
        {
            return;
        }

        if (_lastMoveRoundByUnitKey.Count > 0)
        {
            var staleKeys = _lastMoveRoundByUnitKey
                .Where(pair => pair.Value < round)
                .Select(pair => pair.Key)
                .ToArray();
            foreach (var key in staleKeys)
            {
                _lastMoveRoundByUnitKey.Remove(key);
            }
        }

        if (_wallUnblockMoveRoundByUnitKey.Count > 0)
        {
            var staleKeys = _wallUnblockMoveRoundByUnitKey
                .Where(pair => pair.Value < round)
                .Select(pair => pair.Key)
                .ToArray();
            foreach (var key in staleKeys)
            {
                _wallUnblockMoveRoundByUnitKey.Remove(key);
            }
        }

        if (_pendingMoveSourceRoundByCellKey.Count > 0)
        {
            var staleSources = _pendingMoveSourceRoundByCellKey
                .Where(pair => pair.Value < round)
                .Select(pair => pair.Key)
                .ToArray();
            foreach (var key in staleSources)
            {
                _pendingMoveSourceRoundByCellKey.Remove(key);
            }
        }

        if (_pendingMoveTargetRoundByCellKey.Count > 0)
        {
            var staleTargets = _pendingMoveTargetRoundByCellKey
                .Where(pair => pair.Value < round)
                .Select(pair => pair.Key)
                .ToArray();
            foreach (var key in staleTargets)
            {
                _pendingMoveTargetRoundByCellKey.Remove(key);
            }
        }
    }

    private static string BuildMoveMemoryKey(PlayerManager player, Unit unit)
    {
        int playerId = player != null ? player.playerId : -1;
        int unitId = unit != null ? unit.GetInstanceID() : 0;
        return $"{playerId}:{unitId}";
    }

    private bool IsPendingMoveSource(PlayerManager player, Vector3Int candidate, int round)
    {
        if (player == null || round <= 0)
        {
            return false;
        }

        return _pendingMoveSourceRoundByCellKey.TryGetValue(BuildCellMemoryKey(player, candidate), out int pendingRound) &&
               pendingRound == round;
    }

    private void RememberPendingMoveSource(PlayerManager player, Vector3Int candidate, int round)
    {
        if (player == null || round <= 0)
        {
            return;
        }

        _pendingMoveSourceRoundByCellKey[BuildCellMemoryKey(player, candidate)] = round;
    }

    private bool IsPendingMoveTarget(PlayerManager player, Vector3Int candidate, int round)
    {
        if (player == null || round <= 0)
        {
            return false;
        }

        return _pendingMoveTargetRoundByCellKey.TryGetValue(BuildCellMemoryKey(player, candidate), out int pendingRound) &&
               pendingRound == round;
    }

    private void RememberMoveTarget(PlayerManager player, Vector3Int candidate, int round)
    {
        if (player == null || round <= 0)
        {
            return;
        }

        _pendingMoveTargetRoundByCellKey[BuildCellMemoryKey(player, candidate)] = round;
    }

    private bool IsPendingWallCandidate(PlayerManager player, Vector3Int candidate, int round)
    {
        if (player == null || round <= 0)
        {
            return false;
        }

        return _pendingWallRoundByCellKey.TryGetValue(BuildCellMemoryKey(player, candidate), out int pendingRound) &&
               pendingRound == round;
    }

    private void RememberWallCandidate(PlayerManager player, Vector3Int candidate, int round)
    {
        if (player == null || round <= 0)
        {
            return;
        }

        string key = BuildCellMemoryKey(player, candidate);
        _pendingWallRoundByCellKey[key] = round;
        _builtWallCellKeys.Add(key);
    }

    private void PruneWallMemory(int round)
    {
        if (round <= 0 || _pendingWallRoundByCellKey.Count == 0)
        {
            return;
        }

        var staleKeys = _pendingWallRoundByCellKey
            .Where(pair => pair.Value < round)
            .Select(pair => pair.Key)
            .ToArray();
        foreach (var key in staleKeys)
        {
            _pendingWallRoundByCellKey.Remove(key);
        }
    }

    private static string BuildCellMemoryKey(PlayerManager player, Vector3Int candidate)
    {
        int playerId = player != null ? player.playerId : -1;
        return $"{playerId}:{candidate.x},{candidate.y},{candidate.z}";
    }

    private bool HasBuiltWallCandidate(PlayerManager player, Vector3Int candidate)
    {
        if (player == null)
        {
            return false;
        }

        return _builtWallCellKeys.Contains(BuildCellMemoryKey(player, candidate));
    }

    private bool HasRepairableMissingWallPlan(PlayerManager player, int round)
    {
        var field = player != null ? player.fieldManager : null;
        if (field == null || round < 2 || GetTotalWallStock(player) <= 0 || !HasRecordedBuiltWallCandidate(player))
        {
            return false;
        }

        return TryGetRepairWallPosition(player, round, out _);
    }

    private bool HasRecordedBuiltWallCandidate(PlayerManager player)
    {
        if (player == null || _builtWallCellKeys.Count == 0)
        {
            return false;
        }

        string prefix = $"{player.playerId}:";
        return _builtWallCellKeys.Any(key => key.StartsWith(prefix, StringComparison.Ordinal));
    }

    private static int GetWallBuildReserve(PlayerManager player)
    {
        return Mathf.Max(MinimumRepairReserveWalls, player != null ? player.GetWallReserveK() : 0);
    }

    private static int GetTotalWallStock(PlayerManager player)
    {
        return player == null
            ? 0
            : Mathf.Max(0, player.GetWallCount()) + Mathf.Max(0, player.GetPermanentWallPlacementCount());
    }

    private bool IsValidMoveDestination(PlayerManager player, Unit unit, Vector3Int? destination, Vector3Int from, int round)
    {
        var field = player != null ? player.fieldManager : null;
        if (field == null || unit == null || unit.Data == null || !destination.HasValue)
        {
            return false;
        }

        var to = destination.Value;
        if (to == from || !field.IsRegularUnitPlacementCell(to) || field.IsUnitAt(to))
        {
            return false;
        }

        if (unit.Data.unitType == UnitType.Melee && field.HasWallAt(to))
        {
            return false;
        }

        if (IsPendingMoveTarget(player, to, round) || IsReservedUnbuiltWallPlanCell(player, to, round))
        {
            return false;
        }

        return true;
    }

    private Vector3Int? FindFallbackUnblockDestination(PlayerManager player, Unit unit, Vector3Int from, int round)
    {
        var field = player != null ? player.fieldManager : null;
        if (field == null || unit == null || unit.Data == null)
        {
            return null;
        }

        foreach (var tile in field.GetValidPlacementTiles(unit.Data.unitType))
        {
            if (IsValidMoveDestination(player, unit, tile, from, round))
            {
                return tile;
            }
        }

        return null;
    }

    private bool IsReservedUnbuiltWallPlanCell(PlayerManager player, Vector3Int candidate, int round)
    {
        var field = player != null ? player.fieldManager : null;
        if (field == null || field.HasWallAt(candidate))
        {
            return false;
        }

        if (IsPendingWallCandidate(player, candidate, round))
        {
            return true;
        }

        return GetWallPlan(player).Any(pos => pos == candidate);
    }

    private PrepareRoutineStage GetPrepareRoutineStage(PlayerManager player, int round)
    {
        if (player == null || round <= 0)
        {
            return PrepareRoutineStage.Shopping;
        }

        return _prepareStageByPlayerRound.TryGetValue(BuildPrepareRoundMemoryKey(player, round), out var stage)
            ? stage
            : PrepareRoutineStage.Shopping;
    }

    private void RememberPrepareRoutineStage(PlayerManager player, int round, PrepareRoutineStage stage)
    {
        if (player == null || round <= 0)
        {
            return;
        }

        string key = BuildPrepareRoundMemoryKey(player, round);
        if (!_prepareStageByPlayerRound.TryGetValue(key, out var current) || stage > current)
        {
            _prepareStageByPlayerRound[key] = stage;
        }
    }

    private void PrunePrepareRoutineStageMemory(int round)
    {
        if (round <= 0 || _prepareStageByPlayerRound.Count == 0)
        {
            return;
        }

        var staleKeys = _prepareStageByPlayerRound.Keys
            .Where(key => !key.EndsWith($":round={round}", StringComparison.Ordinal))
            .ToArray();
        foreach (var key in staleKeys)
        {
            _prepareStageByPlayerRound.Remove(key);
        }
    }

    private static string BuildPrepareRoundMemoryKey(PlayerManager player, int round)
    {
        int playerId = player != null ? player.playerId : -1;
        return $"{playerId}:round={round}";
    }

    private bool IsPendingBuyCandidate(PlayerManager player, int slot, int round)
    {
        if (player == null || round <= 0 || slot < 0)
        {
            return false;
        }

        return _pendingBuyRoundBySlotKey.TryGetValue(BuildBuyMemoryKey(player, slot), out int pendingRound) &&
               pendingRound == round;
    }

    private void RememberBuyCandidate(PlayerManager player, int slot, int round)
    {
        if (player == null || round <= 0 || slot < 0)
        {
            return;
        }

        _pendingBuyRoundBySlotKey[BuildBuyMemoryKey(player, slot)] = round;
    }

    private void PruneBuyMemory(int round)
    {
        if (round <= 0 || _pendingBuyRoundBySlotKey.Count == 0)
        {
            return;
        }

        var staleKeys = _pendingBuyRoundBySlotKey
            .Where(pair => pair.Value < round)
            .Select(pair => pair.Key)
            .ToArray();
        foreach (var key in staleKeys)
        {
            _pendingBuyRoundBySlotKey.Remove(key);
        }
    }

    private static string BuildBuyMemoryKey(PlayerManager player, int slot)
    {
        int playerId = player != null ? player.playerId : -1;
        int revision = GetShopRevisionForPolicy(player);
        return $"{playerId}:shopRev={revision}:slot={slot}";
    }

    private static int GetShopRevisionForPolicy(PlayerManager player)
    {
        if (player != null &&
            player.TryGetShopSnapshot(out _, out _, out _, out int revision, out _))
        {
            return revision;
        }

        return 0;
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

    private bool ShouldAllowWallFocus(PlayerManager player, PrepareArmyComposition composition, int round)
    {
        if (player == null || GetTotalWallStock(player) <= 0 || player.fieldManager == null)
        {
            return false;
        }

        if (HasRepairableMissingWallPlan(player, round))
        {
            return true;
        }

        if (GetPrepareRoutineStage(player, round) >= PrepareRoutineStage.Maze &&
            GetTotalWallStock(player) > GetWallBuildReserve(player))
        {
            return true;
        }

        if (composition == null || !composition.HasMinimumArmyCore)
        {
            return false;
        }

        if (GetTotalWallStock(player) > GetWallBuildReserve(player))
        {
            return true;
        }

        if (composition.FieldUnitCount < PrepareArmyComposition.TargetTotalUnits)
        {
            return false;
        }

        return composition.CompositionDistanceToTarget <= 2;
    }

    private bool ShouldPrioritizeWallControl(PlayerManager player, PrepareArmyComposition composition, int round)
    {
        if (player == null || GetTotalWallStock(player) <= 0 || player.fieldManager == null)
        {
            return false;
        }

        if (HasRepairableMissingWallPlan(player, round))
        {
            return true;
        }

        if (GetTotalWallStock(player) <= GetWallBuildReserve(player) || !ShouldAllowWallFocus(player, composition, round))
        {
            return false;
        }

        try
        {
            player.fieldManager.BuildWallCellHash();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static int GetOpenBorderGapCount(FieldManager field)
    {
        return field != null ? field.GetOpenBorderGaps().Count : 0;
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
        if (!MPTestLogger.IsEnabled)
        {
            return;
        }

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
        public int FieldInstanceId;
        public string Signature;
        public List<Vector3Int> Order;
    }
}
