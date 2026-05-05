using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

public static class MPTestAssertions
{
    private const string Unknown = "unknown";

    public static AssertionResult AssertBasic(
        MPTestStateSnapshot.Snapshot snapshot,
        int expectedPlayers = -1,
        string expectedScene = null,
        string expectedGameState = null)
    {
        var result = new AssertionResult();
        if (snapshot == null)
        {
            result.AddError("snapshot_missing");
            return result;
        }

        if (!string.IsNullOrEmpty(expectedScene) && !string.Equals(snapshot.Scene, expectedScene, StringComparison.Ordinal))
        {
            result.AddError($"scene expected={expectedScene} actual={snapshot.Scene}");
        }

        if (snapshot.Game == null || !snapshot.Game.HasGameManagers)
        {
            result.AddError("gameManagers_missing");
        }

        if (!string.IsNullOrEmpty(expectedGameState))
        {
            string actualState = snapshot.Game != null ? snapshot.Game.CurrentState : null;
            if (!string.Equals(actualState, expectedGameState, StringComparison.OrdinalIgnoreCase))
            {
                result.AddError($"gameState expected={expectedGameState} actual={actualState ?? "missing"}");
            }
        }

        var players = snapshot.Players ?? Array.Empty<MPTestStateSnapshot.PlayerSnapshot>();
        if (expectedPlayers >= 0 && players.Length != expectedPlayers)
        {
            result.AddError($"players expected={expectedPlayers} actual={players.Length}");
        }

        var seen = new HashSet<int>();
        foreach (var player in players)
        {
            if (player.PlayerId < 0)
            {
                result.AddError($"playerId_invalid {player.PlayerId}");
            }

            if (!seen.Add(player.PlayerId))
            {
                result.AddError($"playerId_duplicate {player.PlayerId}");
            }

            if (player.Field == null || !player.Field.Ready)
            {
                result.AddError($"player.{player.PlayerId}.field_not_ready");
            }

            if (player.Health < 0)
            {
                result.AddError($"player.{player.PlayerId}.health_negative {player.Health}");
            }

            if (player.Gold < 0)
            {
                result.AddError($"player.{player.PlayerId}.gold_negative {player.Gold}");
            }

            if (player.WallCount < 0)
            {
                result.AddError($"player.{player.PlayerId}.wallCount_negative {player.WallCount}");
            }
        }

        if (snapshot.Errors != null && snapshot.Errors.Count > 0)
        {
            foreach (string error in snapshot.Errors)
            {
                result.AddWarning("snapshot_error " + error);
            }
        }

        return result;
    }

    public static AssertionResult CompareDurable(
        MPTestStateSnapshot.Snapshot expected,
        MPTestStateSnapshot.Snapshot actual)
    {
        var result = new AssertionResult();
        if (expected == null || actual == null)
        {
            result.AddError("snapshot_compare_missing");
            return result;
        }

        CompareEqual(result, "session", expected.Session, actual.Session);
        CompareEqual(result, "scene", expected.Scene, actual.Scene);

        if (expected.Game != null && actual.Game != null)
        {
            CompareEqual(result, "game.currentState", expected.Game.CurrentState, actual.Game.CurrentState);
            CompareEqual(result, "game.currentRound", expected.Game.CurrentRound, actual.Game.CurrentRound);
            CompareKnownHash(result, "game.battleOpponentsHash", expected.Game.BattleOpponentsHash, actual.Game.BattleOpponentsHash);
            CompareKnownHash(result, "game.matchFirstAttackerHash", expected.Game.MatchFirstAttackerHash, actual.Game.MatchFirstAttackerHash);
        }
        else
        {
            result.AddError("game_missing_for_compare");
        }

        var expectedPlayers = (expected.Players ?? Array.Empty<MPTestStateSnapshot.PlayerSnapshot>())
            .OrderBy(player => player.PlayerId)
            .ToArray();
        var actualPlayers = (actual.Players ?? Array.Empty<MPTestStateSnapshot.PlayerSnapshot>())
            .OrderBy(player => player.PlayerId)
            .ToArray();

        CompareEqual(result, "players.count", expectedPlayers.Length, actualPlayers.Length);
        var actualById = actualPlayers.ToDictionary(player => player.PlayerId, player => player);
        foreach (var left in expectedPlayers)
        {
            if (!actualById.TryGetValue(left.PlayerId, out var right))
            {
                result.AddError($"player.{left.PlayerId}.missing");
                continue;
            }

            CompareEqual(result, $"player.{left.PlayerId}.health", left.Health, right.Health);
            CompareEqual(result, $"player.{left.PlayerId}.gold", left.Gold, right.Gold);
            CompareEqual(result, $"player.{left.PlayerId}.wallCount", left.WallCount, right.WallCount);
            CompareShop(result, left.PlayerId, left.Shop, right.Shop);
            CompareAugment(result, left.PlayerId, left.Augment, right.Augment);
            CompareField(result, left.PlayerId, left.Field, right.Field);
            CompareMonsters(result, left.PlayerId, left.Monsters, right.Monsters);
            CompareEqual(result, $"player.{left.PlayerId}.isAI", left.IsAI, right.IsAI);
        }

        CompareNullable(result, "commands.lastSequence", expected.Commands?.LastSequence, actual.Commands?.LastSequence);
        CompareNullable(result, "commands.queueDepth", expected.Commands?.QueueDepth, actual.Commands?.QueueDepth);
        CompareKnown(result, "commands.lastCommand", expected.Commands?.LastCommand, actual.Commands?.LastCommand);

        return result;
    }

    public static AssertionResult FromJsonBasic(string json, int expectedPlayers = -1, string expectedScene = null, string expectedGameState = null)
    {
        return AssertBasic(JsonConvert.DeserializeObject<MPTestStateSnapshot.Snapshot>(json), expectedPlayers, expectedScene, expectedGameState);
    }

    public static AssertionResult CompareJsonDurable(string expectedJson, string actualJson)
    {
        var expected = JsonConvert.DeserializeObject<MPTestStateSnapshot.Snapshot>(expectedJson);
        var actual = JsonConvert.DeserializeObject<MPTestStateSnapshot.Snapshot>(actualJson);
        return CompareDurable(expected, actual);
    }

    private static void CompareShop(
        AssertionResult result,
        int playerId,
        MPTestStateSnapshot.ShopSnapshot expected,
        MPTestStateSnapshot.ShopSnapshot actual)
    {
        if (expected == null || actual == null)
        {
            result.AddError($"player.{playerId}.shop_missing");
            return;
        }

        CompareEqual(result, $"player.{playerId}.shop.available", expected.Available, actual.Available);
        CompareNullable(result, $"player.{playerId}.shop.revision", expected.Revision, actual.Revision);
        CompareNullable(result, $"player.{playerId}.shop.round", expected.Round, actual.Round);
        CompareEqual(result, $"player.{playerId}.shop.count", expected.Count, actual.Count);
        CompareKnownHash(result, $"player.{playerId}.shop.itemsHash", expected.ItemsHash, actual.ItemsHash);
    }

    private static void CompareAugment(
        AssertionResult result,
        int playerId,
        MPTestStateSnapshot.AugmentSnapshot expected,
        MPTestStateSnapshot.AugmentSnapshot actual)
    {
        if (expected == null || actual == null)
        {
            result.AddError($"player.{playerId}.augment_missing");
            return;
        }

        CompareEqual(result, $"player.{playerId}.augment.available", expected.Available, actual.Available);
        CompareEqual(result, $"player.{playerId}.augment.selectedCount", expected.SelectedCount, actual.SelectedCount);
        CompareEqual(result, $"player.{playerId}.augment.presentedCount", expected.PresentedCount, actual.PresentedCount);
        CompareKnownHash(result, $"player.{playerId}.augment.presentedHash", expected.PresentedHash, actual.PresentedHash);
        CompareKnownHash(result, $"player.{playerId}.augment.selectedHash", expected.SelectedHash, actual.SelectedHash);
    }

    private static void CompareField(
        AssertionResult result,
        int playerId,
        MPTestStateSnapshot.FieldSnapshot expected,
        MPTestStateSnapshot.FieldSnapshot actual)
    {
        if (expected == null || actual == null)
        {
            result.AddError($"player.{playerId}.field_missing");
            return;
        }

        CompareEqual(result, $"player.{playerId}.field.ready", expected.Ready, actual.Ready);
        CompareKnownHash(result, $"player.{playerId}.field.gridHash", expected.GridHash, actual.GridHash);
        CompareEqual(result, $"player.{playerId}.field.placedUnitCount", expected.PlacedUnitCount, actual.PlacedUnitCount);
        CompareKnownHash(result, $"player.{playerId}.field.placedUnitsHash", expected.PlacedUnitsHash, actual.PlacedUnitsHash);
        CompareNullable(result, $"player.{playerId}.field.destructibleWallCount", expected.DestructibleWallCount, actual.DestructibleWallCount);
        CompareNullable(result, $"player.{playerId}.field.permanentWallCount", expected.PermanentWallCount, actual.PermanentWallCount);
        CompareKnownHash(result, $"player.{playerId}.field.wallHash", expected.WallHash, actual.WallHash);
        CompareEqual(result, $"player.{playerId}.field.pathReady", expected.PathReady, actual.PathReady);
        CompareEqual(result, $"player.{playerId}.field.goalReady", expected.GoalReady, actual.GoalReady);
    }

    private static void CompareMonsters(
        AssertionResult result,
        int playerId,
        MPTestStateSnapshot.MonsterSnapshot expected,
        MPTestStateSnapshot.MonsterSnapshot actual)
    {
        if (expected == null || actual == null)
        {
            result.AddError($"player.{playerId}.monsters_missing");
            return;
        }

        CompareEqual(result, $"player.{playerId}.monsters.ready", expected.Ready, actual.Ready);
        CompareEqual(result, $"player.{playerId}.monsters.aliveCount", expected.AliveCount, actual.AliveCount);
        CompareKnownHash(result, $"player.{playerId}.monsters.livingHash", expected.LivingHash, actual.LivingHash);
    }

    private static void CompareEqual<T>(AssertionResult result, string field, T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            result.AddError($"{field} expected={expected} actual={actual}");
        }
    }

    private static void CompareNullable(AssertionResult result, string field, int? expected, int? actual)
    {
        if (expected.HasValue && actual.HasValue && expected.Value != actual.Value)
        {
            result.AddError($"{field} expected={expected.Value} actual={actual.Value}");
        }
    }

    private static void CompareKnownHash(AssertionResult result, string field, string expected, string actual)
    {
        CompareKnown(result, field, expected, actual);
    }

    private static void CompareKnown(AssertionResult result, string field, string expected, string actual)
    {
        if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(actual) || expected == Unknown || actual == Unknown)
        {
            return;
        }

        if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            result.AddError($"{field} expected={expected} actual={actual}");
        }
    }

    [Serializable]
    public sealed class AssertionResult
    {
        [JsonProperty("success")] public bool Success => Errors.Count == 0;
        [JsonProperty("errors")] public List<string> Errors = new List<string>();
        [JsonProperty("warnings")] public List<string> Warnings = new List<string>();

        public void AddError(string error)
        {
            Errors.Add(error);
        }

        public void AddWarning(string warning)
        {
            Warnings.Add(warning);
        }
    }
}
