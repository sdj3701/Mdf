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

        if (!string.IsNullOrEmpty(expectedScene) && !MPTestSceneAliases.Matches(snapshot.Scene, expectedScene))
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

            if (player.BlackMagicCurrent < 0 || player.BlackMagicMaximum < 0 || player.BlackMagicMaxBonus < 0)
            {
                result.AddError(
                    $"player.{player.PlayerId}.blackMagic_negative current={player.BlackMagicCurrent} maximum={player.BlackMagicMaximum} bonus={player.BlackMagicMaxBonus}");
            }

            if (player.BlackMagicCurrent > player.BlackMagicMaximum)
            {
                result.AddError(
                    $"player.{player.PlayerId}.blackMagic_exceeds_maximum current={player.BlackMagicCurrent} maximum={player.BlackMagicMaximum}");
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
        CompareScene(result, "scene", expected.Scene, actual.Scene);

        if (expected.Game != null && actual.Game != null)
        {
            CompareEqual(result, "game.currentState", expected.Game.CurrentState, actual.Game.CurrentState);
            CompareEqual(result, "game.battlePhase", expected.Game.BattlePhase, actual.Game.BattlePhase);
            CompareEqual(result, "game.currentRound", expected.Game.CurrentRound, actual.Game.CurrentRound);
            bool inBattle = IsBattlePhase(expected.Game.BattlePhase) || IsBattlePhase(actual.Game.BattlePhase);
            CompareKnownOrRequired(result, "game.battleOpponentsHash", expected.Game.BattleOpponentsHash, actual.Game.BattleOpponentsHash, inBattle);
            CompareKnownOrRequired(result, "game.matchFirstAttackerHash", expected.Game.MatchFirstAttackerHash, actual.Game.MatchFirstAttackerHash, inBattle);
            CompareKnownOrRequired(result, "game.battleActiveHash", expected.Game.BattleActiveHash, actual.Game.BattleActiveHash, inBattle);
            ComparePresenceCount(result, "game.survivorBossPendingCount", expected.Game.SurvivorBossPendingCount, actual.Game.SurvivorBossPendingCount);
            ComparePresenceCount(result, "game.survivorBossAssignmentCount", expected.Game.SurvivorBossAssignmentCount, actual.Game.SurvivorBossAssignmentCount);
            CompareKnownOrRequired(
                result,
                "game.survivorBossPendingHash",
                expected.Game.SurvivorBossPendingHash,
                actual.Game.SurvivorBossPendingHash,
                CountPositive(expected.Game.SurvivorBossPendingCount) || CountPositive(actual.Game.SurvivorBossPendingCount));
            CompareKnownOrRequired(
                result,
                "game.survivorBossAssignmentHash",
                expected.Game.SurvivorBossAssignmentHash,
                actual.Game.SurvivorBossAssignmentHash,
                CountPositive(expected.Game.SurvivorBossAssignmentCount) || CountPositive(actual.Game.SurvivorBossAssignmentCount));
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
            CompareEqual(result, $"player.{left.PlayerId}.blackMagicCurrent", left.BlackMagicCurrent, right.BlackMagicCurrent);
            CompareEqual(result, $"player.{left.PlayerId}.blackMagicMaximum", left.BlackMagicMaximum, right.BlackMagicMaximum);
            CompareEqual(result, $"player.{left.PlayerId}.blackMagicMaxBonus", left.BlackMagicMaxBonus, right.BlackMagicMaxBonus);
            CompareEqual(result, $"player.{left.PlayerId}.blackMagicRevision", left.BlackMagicRevision, right.BlackMagicRevision);
            CompareEqual(result, $"player.{left.PlayerId}.blackMagicSequenceId", left.BlackMagicSequenceId, right.BlackMagicSequenceId);
            CompareKnownOrMissing(result, $"player.{left.PlayerId}.attackMonsterPoolHash", left.AttackMonsterPoolHash, right.AttackMonsterPoolHash);
            CompareKnownOrMissing(result, $"player.{left.PlayerId}.ownedScrollsHash", left.OwnedScrollsHash, right.OwnedScrollsHash);
            CompareEqual(result, $"player.{left.PlayerId}.ownedScrollRevision", left.OwnedScrollRevision, right.OwnedScrollRevision);
            CompareKnownOrMissing(result, $"player.{left.PlayerId}.manualSkillReadyHash", left.ManualSkillReadyHash, right.ManualSkillReadyHash);
            CompareShop(result, left.PlayerId, left.Shop, right.Shop);
            CompareAugment(result, left.PlayerId, left.Augment, right.Augment);
            CompareField(result, left.PlayerId, left.Field, right.Field);
            CompareMonsters(result, left.PlayerId, left.Monsters, right.Monsters);
            CompareEqual(result, $"player.{left.PlayerId}.isAI", left.IsAI, right.IsAI);
            CompareEqual(result, $"player.{left.PlayerId}.isActivelyFighting", left.IsActivelyFighting, right.IsActivelyFighting);
            CompareEqual(result, $"player.{left.PlayerId}.isAttackerInCurrentBattle", left.IsAttackerInCurrentBattle, right.IsAttackerInCurrentBattle);
        }

        CompareNullable(result, "commands.lastSequence", expected.Commands?.LastSequence, actual.Commands?.LastSequence);
        CompareNullable(result, "commands.queueDepth", expected.Commands?.QueueDepth, actual.Commands?.QueueDepth);
        bool hasCommandCounters = HasAnyCommandCounter(expected.Commands) || HasAnyCommandCounter(actual.Commands);
        CompareKnownOrRequired(result, "commands.lastCommand", expected.Commands?.LastCommand, actual.Commands?.LastCommand, hasCommandCounters);
        CompareEqual(result, "commands.acceptedBattleCommandSeq", expected.Commands?.AcceptedBattleCommandSeq ?? 0, actual.Commands?.AcceptedBattleCommandSeq ?? 0);
        CompareEqual(result, "commands.spawnMonsterSeq", expected.Commands?.SpawnMonsterSeq ?? 0, actual.Commands?.SpawnMonsterSeq ?? 0);
        CompareEqual(result, "commands.useMagicScrollSeq", expected.Commands?.UseMagicScrollSeq ?? 0, actual.Commands?.UseMagicScrollSeq ?? 0);
        CompareEqual(result, "commands.activateSkillSeq", expected.Commands?.ActivateSkillSeq ?? 0, actual.Commands?.ActivateSkillSeq ?? 0);
        CompareEqual(result, "commands.rejectedBattleCommandCount", expected.Commands?.RejectedBattleCommandCount ?? 0, actual.Commands?.RejectedBattleCommandCount ?? 0);
        CompareEffects(result, expected.Effects, actual.Effects);

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
        CompareEqual(result, $"player.{playerId}.augment.activeEffectCount", expected.ActiveEffectCount, actual.ActiveEffectCount);
        CompareEqual(result, $"player.{playerId}.augment.activeTargetCount", expected.ActiveTargetCount, actual.ActiveTargetCount);
        CompareKnownHash(result, $"player.{playerId}.augment.activeEffectHash", expected.ActiveEffectHash, actual.ActiveEffectHash);
        CompareKnownHash(result, $"player.{playerId}.augment.activeTargetHash", expected.ActiveTargetHash, actual.ActiveTargetHash);
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
        CompareEqual(result, $"player.{playerId}.field.aliveUnitCount", expected.AliveUnitCount, actual.AliveUnitCount);
        CompareEqual(result, $"player.{playerId}.field.deadUnitCount", expected.DeadUnitCount, actual.DeadUnitCount);
        CompareKnownHash(result, $"player.{playerId}.field.deadUnitsHash", expected.DeadUnitsHash, actual.DeadUnitsHash);
        CompareKnownHash(result, $"player.{playerId}.field.placedUnitsHash", expected.PlacedUnitsHash, actual.PlacedUnitsHash);
        CompareNullable(result, $"player.{playerId}.field.destructibleWallCount", expected.DestructibleWallCount, actual.DestructibleWallCount);
        CompareNullable(result, $"player.{playerId}.field.permanentWallCount", expected.PermanentWallCount, actual.PermanentWallCount);
        CompareKnownHash(result, $"player.{playerId}.field.wallHash", expected.WallHash, actual.WallHash);
        CompareKnownOrRequired(
            result,
            $"player.{playerId}.field.destructibleWallHealthHash",
            expected.DestructibleWallHealthHash,
            actual.DestructibleWallHealthHash,
            CountPositive(expected.DestructibleWallCount) || CountPositive(actual.DestructibleWallCount));
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
        CompareKnownOrMissing(result, $"player.{playerId}.monsters.typeHash", expected.TypeHash, actual.TypeHash);
        CompareKnownOrMissing(result, $"player.{playerId}.monsters.typeCountHpHash", expected.TypeCountHpHash, actual.TypeCountHpHash);
        CompareKnownOrMissing(result, $"player.{playerId}.monsters.ownerOriginHash", expected.OwnerOriginHash, actual.OwnerOriginHash);
        CompareKnownOrMissing(result, $"player.{playerId}.monsters.targetPlayerHash", expected.TargetPlayerHash, actual.TargetPlayerHash);
        CompareKnownOrMissing(result, $"player.{playerId}.monsters.hpBucketHash", expected.HpBucketHash, actual.HpBucketHash);
        CompareKnownOrMissing(result, $"player.{playerId}.monsters.bossPoolIdentityHash", expected.BossPoolIdentityHash, actual.BossPoolIdentityHash);
    }

    private static void CompareEffects(
        AssertionResult result,
        MPTestStateSnapshot.EffectsSnapshot expected,
        MPTestStateSnapshot.EffectsSnapshot actual)
    {
        expected = NormalizeEffects(expected);
        actual = NormalizeEffects(actual);

        CompareEqual(result, "effects.activeBuffCount", expected.ActiveBuffCount, actual.ActiveBuffCount);
        CompareEqual(result, "effects.activeStatusCount", expected.ActiveStatusCount, actual.ActiveStatusCount);
        CompareEqual(result, "effects.zoneCount", expected.ZoneCount, actual.ZoneCount);
        CompareKnownOrMissing(result, "effects.activeBuffHash", expected.ActiveBuffHash, actual.ActiveBuffHash);
        CompareKnownOrMissing(result, "effects.activeStatusHash", expected.ActiveStatusHash, actual.ActiveStatusHash);
        CompareKnownOrMissing(result, "effects.zoneHash", expected.ZoneHash, actual.ZoneHash);
    }

    private static MPTestStateSnapshot.EffectsSnapshot NormalizeEffects(MPTestStateSnapshot.EffectsSnapshot effects)
    {
        return effects ?? new MPTestStateSnapshot.EffectsSnapshot
        {
            ActiveBuffCount = 0,
            ActiveStatusCount = 0,
            ZoneCount = 0,
            ActiveBuffHash = Unknown,
            ActiveStatusHash = Unknown,
            ZoneHash = Unknown
        };
    }

    private static void CompareEqual<T>(AssertionResult result, string field, T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            result.AddError($"{field} expected={expected} actual={actual}");
        }
    }

    private static void CompareScene(AssertionResult result, string field, string expected, string actual)
    {
        if (!MPTestSceneAliases.Matches(actual, expected))
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

    private static void ComparePresenceCount(AssertionResult result, string field, int? expected, int? actual)
    {
        if (expected.HasValue && actual.HasValue)
        {
            if (expected.Value != actual.Value)
            {
                result.AddError($"{field} expected={expected.Value} actual={actual.Value}");
            }

            return;
        }

        if (expected.GetValueOrDefault() > 0 || actual.GetValueOrDefault() > 0)
        {
            result.AddError($"{field} expected={FormatNullable(expected)} actual={FormatNullable(actual)}");
        }
    }

    private static string FormatNullable(int? value)
    {
        return value.HasValue ? value.Value.ToString() : "unknown";
    }

    private static bool IsBattlePhase(string value)
    {
        return string.Equals(value, "Battle1", StringComparison.Ordinal) ||
               string.Equals(value, "Battle2", StringComparison.Ordinal);
    }

    private static bool CountPositive(int? value)
    {
        return value.HasValue && value.Value > 0;
    }

    private static bool HasAnyCommandCounter(MPTestStateSnapshot.CommandsSnapshot commands)
    {
        return commands != null &&
               (commands.AcceptedBattleCommandSeq > 0 ||
                commands.SpawnMonsterSeq > 0 ||
                commands.UseMagicScrollSeq > 0 ||
                commands.ActivateSkillSeq > 0 ||
                commands.RejectedBattleCommandCount > 0);
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

    private static void CompareKnownOrMissing(AssertionResult result, string field, string expected, string actual)
    {
        bool expectedUnknown = string.IsNullOrEmpty(expected) || expected == Unknown;
        bool actualUnknown = string.IsNullOrEmpty(actual) || actual == Unknown;
        if (expectedUnknown && actualUnknown)
        {
            return;
        }

        if (expectedUnknown || actualUnknown)
        {
            result.AddError($"{field} expected={FormatKnown(expected, expectedUnknown)} actual={FormatKnown(actual, actualUnknown)}");
            return;
        }

        if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            result.AddError($"{field} expected={expected} actual={actual}");
        }
    }

    private static void CompareKnownOrRequired(AssertionResult result, string field, string expected, string actual, bool required)
    {
        if (required)
        {
            CompareRequiredKnown(result, field, expected, actual);
            return;
        }

        CompareKnown(result, field, expected, actual);
    }

    private static void CompareRequiredKnown(AssertionResult result, string field, string expected, string actual)
    {
        bool expectedUnknown = string.IsNullOrEmpty(expected) || expected == Unknown;
        bool actualUnknown = string.IsNullOrEmpty(actual) || actual == Unknown;
        if (expectedUnknown || actualUnknown)
        {
            result.AddError($"{field} expected={FormatKnown(expected, expectedUnknown)} actual={FormatKnown(actual, actualUnknown)}");
            return;
        }

        if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            result.AddError($"{field} expected={expected} actual={actual}");
        }
    }

    private static string FormatKnown(string value, bool unknown)
    {
        return unknown ? Unknown : value;
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
