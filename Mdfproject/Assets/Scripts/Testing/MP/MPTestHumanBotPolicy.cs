#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public sealed class MPTestHumanBotPolicy
{
    private delegate bool CommandChooser(PlayerManager player, GameManagers gm, out Decision decision);

    private readonly MPTestBotPersona _persona;
    private readonly System.Random _random;
    private readonly Dictionary<int, WallPlanCache> _wallPlans = new Dictionary<int, WallPlanCache>();

    public MPTestHumanBotPolicy(MPTestBotPersona persona, int seed)
    {
        _persona = persona;
        _random = new System.Random(seed);
    }

    public bool TryChoose(PlayerManager player, out Decision decision)
    {
        decision = null;
        var gm = GameManagers.Instance;
        if (gm == null || player == null)
        {
            return false;
        }

        if (gm.GetGameState() != GameManagers.GameState.Prepare)
        {
            decision = Decision.Observe(player, gm, "observe_non_prepare");
            return false;
        }

        if (_persona == MPTestBotPersona.Passive)
        {
            decision = Decision.Observe(player, gm, "passive_observe");
            return false;
        }

        foreach (var chooser in GetChooserOrder())
        {
            if (chooser(player, gm, out decision))
            {
                return true;
            }
        }

        decision = Decision.Observe(player, gm, "no_legal_prepare_command");
        return false;
    }

    private IEnumerable<CommandChooser> GetChooserOrder()
    {
        yield return TryChooseAugment;

        switch (_persona)
        {
            case MPTestBotPersona.Maze:
                yield return TryChooseWall;
                yield return TryChooseBuy;
                yield return TryChooseMove;
                yield return TryChooseReroll;
                break;
            case MPTestBotPersona.Shop:
                yield return TryChooseBuy;
                yield return TryChooseReroll;
                yield return TryChooseMove;
                yield return TryChooseWall;
                break;
            case MPTestBotPersona.Unit:
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

    private bool TryChooseAugment(PlayerManager player, GameManagers gm, out Decision decision)
    {
        decision = null;
        var augments = player.augmentManager != null ? player.augmentManager.GetPresentedAugments() : null;
        if (augments == null || augments.Count == 0)
        {
            return false;
        }

        int index = Mathf.Clamp(PickAugmentIndex(augments.Count), 0, augments.Count - 1);
        decision = Decision.ForCommand(
            player,
            gm,
            new SelectAugmentCommand(player.playerId, index),
            "SelectAugment",
            "presented_augment_available",
            $"index={index}");
        return true;
    }

    private bool TryChooseWall(PlayerManager player, GameManagers gm, out Decision decision)
    {
        decision = null;
        if (player.GetWallCount() <= 0 || player.fieldManager == null || !player.IsReadyForPlayerActions)
        {
            return false;
        }

        if (!TryGetNextWallPosition(player, out var position))
        {
            return false;
        }

        decision = Decision.ForCommand(
            player,
            gm,
            new PlaceWallCommand(player.playerId, position),
            "PlaceWall",
            "maze_policy_next_wall",
            $"{position.x},{position.y},{position.z}");
        return true;
    }

    private bool TryChooseBuy(PlayerManager player, GameManagers gm, out Decision decision)
    {
        decision = null;
        if (player.shopManager == null || !player.shopManager.IsDatabaseLoaded)
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

        decision = Decision.ForCommand(
            player,
            gm,
            new BuyUnitCommand(player.playerId, bestSlot),
            "BuyUnit",
            "best_affordable_score",
            $"slot={bestSlot};score={bestScore:F2}");
        return true;
    }

    private bool TryChooseMove(PlayerManager player, GameManagers gm, out Decision decision)
    {
        decision = null;
        var field = player.fieldManager;
        if (field == null || player.astarGrid == null || player.goalTransform == null)
        {
            return false;
        }

        var units = field.GetAlliedUnitsOnField()
            .Where(unit => unit != null && unit.Data != null)
            .OrderBy(unit => unit.Data.unitType == UnitType.Ranged ? 0 : 1)
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

            decision = Decision.ForCommand(
                player,
                gm,
                new MoveUnitCommand(player.playerId, from.Value, to.Value),
                "MoveUnit",
                "best_unit_reposition",
                $"{from.Value.x},{from.Value.y}->{to.Value.x},{to.Value.y}");
            return true;
        }

        return false;
    }

    private bool TryChooseReroll(PlayerManager player, GameManagers gm, out Decision decision)
    {
        decision = null;
        if (player.shopManager == null || !player.shopManager.IsDatabaseLoaded)
        {
            return false;
        }

        int cost = player.shopManager.GetRerollCost();
        if (player.GetGold() < cost)
        {
            return false;
        }

        decision = Decision.ForCommand(
            player,
            gm,
            new RerollShopCommand(player.playerId),
            "RerollShop",
            "no_better_purchase_gold_allows_reroll",
            $"cost={cost}");
        return true;
    }

    private int PickAugmentIndex(int count)
    {
        if (count <= 1)
        {
            return 0;
        }

        return _persona == MPTestBotPersona.Balanced ? 0 : _random.Next(count);
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
            if (_persona == MPTestBotPersona.Unit)
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
        position = default;
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
            MPTestLogger.Fail("human_bot_policy", "maze_plan_failed", ex.GetType().Name, new Dictionary<string, object>
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

    private sealed class WallPlanCache
    {
        public string Signature;
        public List<Vector3Int> Order;
    }

    public sealed class Decision
    {
        public ICommand Command;
        public string CommandType;
        public string Reason;
        public string Target;
        public int PlayerId;
        public string GameState;
        public int Round;
        public object Observed;

        public static Decision ForCommand(PlayerManager player, GameManagers gm, ICommand command, string commandType, string reason, string target)
        {
            return new Decision
            {
                Command = command,
                CommandType = commandType,
                Reason = reason,
                Target = target,
                PlayerId = player != null ? player.playerId : -1,
                GameState = gm != null ? gm.GetGameState().ToString() : "unknown",
                Round = gm != null ? gm.currentRound : 0,
                Observed = BuildObserved(player)
            };
        }

        public static Decision Observe(PlayerManager player, GameManagers gm, string reason)
        {
            return ForCommand(player, gm, null, "Observe", reason, null);
        }

        private static object BuildObserved(PlayerManager player)
        {
            if (player == null)
            {
                return null;
            }

            var shop = CaptureShopObserved(player);
            return new
            {
                gold = SafeInt(player.GetGold, 0),
                wallCount = SafeInt(player.GetWallCount, 0),
                shopRevision = shop.revision,
                shopItemsHash = shop.itemsHash,
                wallHash = player.fieldManager != null ? MPTestStateSnapshot.HashStableString(MPTestHumanBotPolicy.SafeWallSignature(player.fieldManager)) : "unknown",
                presentedAugmentsHash = CapturePresentedAugmentHash(player)
            };
        }

        private static (int revision, string itemsHash) CaptureShopObserved(PlayerManager player)
        {
            string[] unitKeys;
            int[] starLevels;
            bool[] soldFlags;
            int revision;
            int round;
            if (player.TryGetShopSnapshot(out unitKeys, out starLevels, out soldFlags, out revision, out round))
            {
                var parts = new List<string>();
                for (int i = 0; i < unitKeys.Length; i++)
                {
                    int star = i < starLevels.Length ? starLevels[i] : 0;
                    bool sold = i < soldFlags.Length && soldFlags[i];
                    parts.Add($"{i}:{unitKeys[i]}:{star}:{sold}");
                }

                return (revision, MPTestStateSnapshot.HashStableParts(parts));
            }

            return (0, "unknown");
        }

        private static string CapturePresentedAugmentHash(PlayerManager player)
        {
            var augments = player.augmentManager != null ? player.augmentManager.GetPresentedAugments() : null;
            if (augments == null || augments.Count == 0)
            {
                return "unknown";
            }

            return MPTestStateSnapshot.HashStableParts(augments.Select(a => a != null ? a.augmentName : "null"));
        }

        private static int SafeInt(Func<int> getter, int fallback)
        {
            try
            {
                return getter();
            }
            catch
            {
                return fallback;
            }
        }
    }
}
#endif
