using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Fusion;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class MPTestStateSnapshot
{
    private const string Unknown = "unknown";

    public static Snapshot Capture(string role = null, string caseName = null, string session = null)
    {
        var options = MPTestCommandLine.GetOptions();
        var errors = new List<string>();
        var networkManager = NetworkManager.Instance;
        var runner = networkManager != null ? networkManager._runner : null;
        var gameManagers = GameManagers.Instance;

        var snapshot = new Snapshot
        {
            Version = 1,
            Role = string.IsNullOrWhiteSpace(role) ? options.SafeRole : role,
            CaseName = string.IsNullOrWhiteSpace(caseName) ? options.CaseName : caseName,
            Session = ResolveSession(session, options, networkManager, runner),
            Scene = SceneManager.GetActiveScene().name,
            TimestampUtc = DateTime.UtcNow.ToString("o"),
            Runner = CaptureRunner(runner, networkManager),
            Game = CaptureGame(gameManagers, errors),
            Players = CapturePlayers(gameManagers, runner, options, errors),
            Objects = CaptureObjects(),
            Effects = CaptureEffects(),
            Commands = CaptureCommands(gameManagers),
            HostMigration = CaptureHostMigration(),
            Test = CaptureTest(),
            Errors = errors
        };

        return snapshot;
    }

    public static string ToJson(Snapshot snapshot, bool indented = false)
    {
        return JsonConvert.SerializeObject(snapshot, indented ? Formatting.Indented : Formatting.None);
    }

    public static string HashStableString(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return Unknown;
        }

        using (var sha = SHA256.Create())
        {
            byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(value));
            var builder = new StringBuilder("sha256:");
            for (int i = 0; i < bytes.Length; i++)
            {
                builder.Append(bytes[i].ToString("x2"));
            }

            return builder.ToString();
        }
    }

    public static string HashStableParts(IEnumerable<string> parts)
    {
        return HashStableString(string.Join("|", (parts ?? Array.Empty<string>()).OrderBy(part => part, StringComparer.Ordinal)));
    }

    private static RunnerSnapshot CaptureRunner(NetworkRunner runner, NetworkManager networkManager)
    {
        int activePlayerCount = 0;
        if (runner != null)
        {
            foreach (var ignored in runner.ActivePlayers)
            {
                activePlayerCount++;
            }
        }

        return new RunnerSnapshot
        {
            IsRunning = runner != null && runner.IsRunning,
            GameMode = runner != null ? runner.GameMode.ToString() : null,
            IsServer = runner != null && runner.IsServer,
            IsClient = runner != null && runner.IsClient,
            Tick = runner != null ? runner.Tick.Raw : 0,
            ActivePlayerCount = activePlayerCount,
            MaxPlayers = networkManager != null ? ReadMaxPlayers(networkManager) : 0,
            LocalPlayerRef = runner != null ? runner.LocalPlayer.ToString() : null
        };
    }

    private static string ResolveSession(string explicitSession, MPTestCommandLine.Options options, NetworkManager networkManager, NetworkRunner runner)
    {
        if (!string.IsNullOrWhiteSpace(explicitSession))
        {
            return explicitSession;
        }

        try
        {
            if (runner != null && runner.IsRunning && runner.SessionInfo != null && !string.IsNullOrWhiteSpace(runner.SessionInfo.Name))
            {
                return runner.SessionInfo.Name;
            }
        }
        catch
        {
        }

        try
        {
            if (networkManager != null && !string.IsNullOrWhiteSpace(networkManager.GetRoomNameInput()))
            {
                return networkManager.GetRoomNameInput();
            }
        }
        catch
        {
        }

        return options.Session;
    }

    private static GameSnapshot CaptureGame(GameManagers gameManagers, List<string> errors)
    {
        string battleOpponentsSnapshot = null;
        string matchFirstAttackerSnapshot = null;
        string battleActiveSnapshot = null;
        string survivorBossPendingSnapshot = null;
        string survivorBossAssignmentSnapshot = null;
        string survivorBossPendingHash = null;
        string survivorBossAssignmentHash = null;
        bool survivorBossHashCaptured = false;
        int? survivorBossPendingCount = null;
        int? survivorBossAssignmentCount = null;
        int firstAttackerPlayerId = -1;

        if (gameManagers != null)
        {
            try
            {
                gameManagers.CaptureBattleSnapshotForMigration(
                    out battleOpponentsSnapshot,
                    out matchFirstAttackerSnapshot,
                    out firstAttackerPlayerId);
                battleActiveSnapshot = gameManagers.CaptureBattleActiveSnapshot();
            }
            catch (Exception ex)
            {
                errors.Add("game.battleSnapshot:" + ex.GetType().Name);
            }
        }

        if (gameManagers != null)
        {
            try
            {
                gameManagers.CaptureSurvivorBossSnapshotHashesForState(
                    out survivorBossPendingHash,
                    out survivorBossAssignmentHash,
                    out int pendingCount,
                    out int assignmentCount);
                survivorBossPendingCount = pendingCount;
                survivorBossAssignmentCount = assignmentCount;
                survivorBossHashCaptured = true;
            }
            catch (Exception ex)
            {
                errors.Add("game.survivorBossSnapshot:" + ex.GetType().Name);
            }
        }
        else
        {
            var survivorBossManager = SurvivorBossManager.Instance;
            if (survivorBossManager != null)
            {
                try
                {
                    survivorBossManager.CaptureStableSnapshot(
                        out survivorBossPendingSnapshot,
                        out survivorBossAssignmentSnapshot,
                        out int pendingCount,
                        out int assignmentCount);
                    survivorBossPendingCount = pendingCount;
                    survivorBossAssignmentCount = assignmentCount;
                }
                catch (Exception ex)
                {
                    errors.Add("game.survivorBossSnapshot:" + ex.GetType().Name);
                }
            }
        }

        return new GameSnapshot
        {
            HasGameManagers = gameManagers != null,
            CurrentState = gameManagers != null ? SafeString(() => gameManagers.GetGameState().ToString(), Unknown) : null,
            BattlePhase = gameManagers != null ? ResolveBattlePhase(gameManagers) : "None",
            CurrentRound = gameManagers != null ? SafeInt(() => gameManagers.currentRound, 0) : 0,
            PhaseTimerRemaining = gameManagers != null ? SafeFloat(() => gameManagers.currentPhaseTimer, 0f) : 0f,
            FirstAttackerPlayerId = gameManagers != null ? firstAttackerPlayerId : -1,
            BattleOpponentsHash = HashStableString(battleOpponentsSnapshot),
            MatchFirstAttackerHash = HashStableString(matchFirstAttackerSnapshot),
            BattleActiveHash = HashStableString(battleActiveSnapshot),
            SurvivorBossPendingHash = survivorBossHashCaptured ? survivorBossPendingHash : HashStableString(survivorBossPendingSnapshot),
            SurvivorBossAssignmentHash = survivorBossHashCaptured ? survivorBossAssignmentHash : HashStableString(survivorBossAssignmentSnapshot),
            SurvivorBossPendingCount = survivorBossPendingCount,
            SurvivorBossAssignmentCount = survivorBossAssignmentCount
        };
    }

    private static string ResolveBattlePhase(GameManagers gameManagers)
    {
        var state = SafeRef(() => gameManagers.GetGameState(), GameManagers.GameState.Setup);
        return state == GameManagers.GameState.Battle1 || state == GameManagers.GameState.Battle2
            ? state.ToString()
            : "None";
    }

    private static PlayerSnapshot[] CapturePlayers(
        GameManagers gameManagers,
        NetworkRunner runner,
        MPTestCommandLine.Options options,
        List<string> errors)
    {
        var players = new List<PlayerManager>();
        if (gameManagers != null)
        {
            try
            {
                players.AddRange(gameManagers.AllPlayers.Where(player => player != null));
            }
            catch (Exception ex)
            {
                errors.Add("players.allPlayers:" + ex.GetType().Name);
            }
        }

        if (players.Count == 0)
        {
            players.AddRange(UnityEngine.Object.FindObjectsOfType<PlayerManager>().Where(player => player != null));
        }

        return players
            .Distinct()
            .OrderBy(player => SafeInt(() => player.playerId, int.MaxValue))
            .ThenBy(player => SafeString(() => player.name, string.Empty), StringComparer.Ordinal)
            .Select(player =>
            {
                try
                {
                    return CapturePlayer(player, runner, options, errors);
                }
                catch (Exception ex)
                {
                    errors.Add($"player.capture:{SafeInt(() => player.playerId, -1)}:{ex.GetType().Name}");
                    return null;
                }
            })
            .Where(snapshot => snapshot != null)
            .ToArray();
    }

    private static PlayerSnapshot CapturePlayer(
        PlayerManager player,
        NetworkRunner runner,
        MPTestCommandLine.Options options,
        List<string> errors)
    {
        int playerId = SafeInt(() => player.playerId, -1);
        var networkObject = SafeRef(() => player.Object, null);
        bool networkObjectValid = networkObject != null && SafeBool(() => networkObject.IsValid, false);
        string networkId = networkObjectValid ? SafeString(() => networkObject.Id.ToString(), null) : null;
        string playerRef = networkObjectValid ? SafeString(() => networkObject.InputAuthority.ToString(), null) : null;
        bool isLocal = networkObjectValid && SafeBool(() => networkObject.HasInputAuthority, false);

        return new PlayerSnapshot
        {
            PlayerId = playerId,
            NetworkId = networkId,
            PlayerRef = playerRef,
            ConnectionTokenHash = isLocal ? options.ConnectionTokenHash : Unknown,
            HasInputAuthority = isLocal,
            HasStateAuthority = networkObjectValid && SafeBool(() => networkObject.HasStateAuthority, false),
            IsLocal = isLocal,
            IsAI = ComponentRegistry.Has<AIPlayerController>(playerId.ToString()),
            IsConnected = runner == null || (networkObjectValid && PlayerRefIsConnected(runner, networkObject)),
            Health = SafeInt(player.GetHealth, 0),
            Gold = SafeInt(player.GetGold, 0),
            WallCount = SafeInt(player.GetWallCount, 0),
            IsActivelyFighting = SafeBool(() => player.IsActivelyFighting, false),
            IsAttackerInCurrentBattle = SafeBool(() => player.IsAttackerInCurrentBattle, false),
            AttackMonsterPoolHash = CaptureAttackMonsterPoolHash(player),
            OwnedScrollsHash = CaptureOwnedScrollsHash(player),
            OwnedScrollRevision = SafeInt(() => player.OwnedMagicScrollRevision, 0),
            ManualSkillReadyHash = CaptureManualSkillReadyHash(player),
            Shop = CaptureShop(player),
            Augment = CaptureAugment(player),
            Field = CaptureField(player, errors),
            Monsters = CaptureMonsters(player),
            Ai = CaptureAi(playerId, player)
        };
    }

    private static string CaptureManualSkillReadyHash(PlayerManager player)
    {
        var managers = SafeRef(() => GameManagers.Instance, null);
        var state = managers != null
            ? SafeRef(() => managers.GetGameState(), GameManagers.GameState.Setup)
            : GameManagers.GameState.Setup;
        if (state != GameManagers.GameState.Battle1 && state != GameManagers.GameState.Battle2)
        {
            return Unknown;
        }

        var field = SafeRef(() => player.fieldManager, null);
        if (field == null)
        {
            return Unknown;
        }

        var units = SafeRef(() => field.GetAlliedUnitsOnField(), null);
        if (units == null || units.Count == 0)
        {
            return HashStableParts(new[] { "manualSkillReady=empty" });
        }

        var parts = new List<string>();
        foreach (var unit in units.Where(unit => unit != null))
        {
            if (!SafeBool(() => unit.HasConfiguredSkill, false))
            {
                continue;
            }

            var skillData = SafeRef(() => unit.LoadedSkillData, null);
            bool strategic = SafeBool(() => unit.IsManualOrAiStrategicSkill(skillData), false);
            if (!strategic)
            {
                continue;
            }

            string configuredSkillKey = null;
            SafeBool(() => unit.TryGetConfiguredSkillKey(out configuredSkillKey), false);
            string skillKey = skillData != null
                ? BuildScriptableObjectKey(skillData, SafeString(() => skillData.skillName, string.Empty))
                : (!string.IsNullOrWhiteSpace(configuredSkillKey) ? configuredSkillKey.Trim() : "unloaded");
            bool isDead = SafeBool(() => unit.IsDead, false);
            bool isCasting = SafeBool(() => unit.IsSkillCastingActive, false);
            bool canUseByStatus = SafeBool(() => unit.CanUseSkillByStatus, false);
            bool manaFull = SafeBool(() => unit.IsSkillManaFull, false);
            float currentMana = SafeFloat(() => unit.SkillCurrentMana, 0f);
            float maxMana = SafeFloat(() => unit.SkillMaxMana, 0f);
            int manaBucket = BuildManaBucket(currentMana, maxMana);
            int targetCount = skillData != null ? SafeInt(() => unit.CountSkillTargets(skillData), 0) : -1;
            bool targetsAvailable = skillData != null && SafeBool(() => unit.HasSkillTargetsAvailable(skillData), false);
            bool ready = !isDead && !isCasting && canUseByStatus && manaFull && targetsAvailable;
            var pos = SafeRef(() => field.GetUnitPosition(unit), (Vector3Int?)null);
            string posText = pos.HasValue ? $"{pos.Value.x},{pos.Value.y},{pos.Value.z}" : "unknown-pos";
            parts.Add(
                $"unit={BuildUnitDataKey(unit)};pos={posText};star={SafeInt(() => unit.starLevel, 0)};skill={skillKey};activation={SafeString(() => unit.currentSkillActivationType.ToString(), Unknown)};aiStrategic={SafeBool(() => skillData != null && skillData.canAiUseStrategically, false)};ready={ready};manaFull={manaFull};manaBucket={manaBucket};status={canUseByStatus};casting={isCasting};dead={isDead};targets={targetCount}");
        }

        return HashStableParts(parts.Count > 0 ? parts : new[] { "manualSkillReady=empty" });
    }

    private static bool PlayerRefIsConnected(NetworkRunner runner, NetworkObject networkObject)
    {
        if (runner == null || networkObject == null || !SafeBool(() => networkObject.IsValid, false))
        {
            return false;
        }

        var inputAuthority = SafeRef(() => networkObject.InputAuthority, PlayerRef.None);
        PlayerRef[] activePlayers;
        try
        {
            activePlayers = runner.ActivePlayers.ToArray();
        }
        catch
        {
            return false;
        }

        foreach (var active in activePlayers)
        {
            if (active == inputAuthority)
            {
                return true;
            }
        }

        return false;
    }

    private static ShopSnapshot CaptureShop(PlayerManager player)
    {
        string[] unitKeys = Array.Empty<string>();
        int[] starLevels = Array.Empty<int>();
        bool[] soldFlags = Array.Empty<bool>();
        int revision = 0;
        int round = 0;
        bool hasSnapshot = SafeBool(
            () => player.TryGetShopSnapshot(out unitKeys, out starLevels, out soldFlags, out revision, out round),
            false);

        if (hasSnapshot)
        {
            var parts = new List<string>();
            for (int i = 0; i < unitKeys.Length; i++)
            {
                string key = unitKeys[i] ?? string.Empty;
                int star = i < starLevels.Length ? starLevels[i] : 0;
                bool sold = i < soldFlags.Length && soldFlags[i];
                parts.Add($"{i}:{key}:{star}:{sold}");
            }

            return new ShopSnapshot
            {
                Available = true,
                Revision = revision,
                Round = round,
                Count = unitKeys.Length,
                ItemsHash = HashStableParts(parts)
            };
        }

        return new ShopSnapshot
        {
            Available = false,
            Revision = null,
            Round = null,
            Count = 0,
            ItemsHash = Unknown
        };
    }

    private static string CaptureAttackMonsterPoolHash(PlayerManager player)
    {
        int revision = 0;
        string[] monsterDataNames = null;
        int[] remainingCounts = null;
        int[] maxCounts = null;
        int[] isBossValues = null;
        int[] bossUniqueIds = null;
        int[] targetPlayerIds = null;
        int[] originPlayerIds = null;
        bool hasSnapshot = false;
        try
        {
            hasSnapshot = player.TryGetAttackMonsterPoolSnapshotForComparison(
                out revision,
                out monsterDataNames,
                out remainingCounts,
                out maxCounts,
                out isBossValues,
                out bossUniqueIds,
                out targetPlayerIds,
                out originPlayerIds);
        }
        catch (Exception)
        {
            hasSnapshot = false;
        }

        if (!hasSnapshot || monsterDataNames == null || monsterDataNames.Length == 0)
        {
            return HashStableParts(new[]
            {
                hasSnapshot ? "pool=empty" : "pool=null",
                $"revision={SafeInt(() => player.AttackMonsterPoolRevision, 0)}"
            });
        }

        var parts = monsterDataNames.Select((monsterDataName, index) =>
        {
            if (string.IsNullOrWhiteSpace(monsterDataName))
            {
                return $"{index}:null";
            }

            return $"{index}:type={monsterDataName.Trim()};remaining={ReadArrayValue(remainingCounts, index, 0)};max={ReadArrayValue(maxCounts, index, 0)};boss={ReadArrayValue(isBossValues, index, 0) != 0};bossId={ReadArrayValue(bossUniqueIds, index, -1)};target={ReadArrayValue(targetPlayerIds, index, -1)};origin={ReadArrayValue(originPlayerIds, index, -1)}";
        }).Concat(new[] { $"revision={revision}" });
        return HashStableParts(parts);
    }

    private static int ReadArrayValue(int[] values, int index, int fallback)
    {
        return values != null && index >= 0 && index < values.Length ? values[index] : fallback;
    }

    private static string CaptureOwnedScrollsHash(PlayerManager player)
    {
        var scrolls = SafeRef(() => player.OwnedScrolls, null);
        int revision = SafeInt(() => player.OwnedMagicScrollRevision, 0);
        if (scrolls == null || scrolls.Count == 0)
        {
            return HashStableParts(new[]
            {
                scrolls == null ? "scrolls=null" : "scrolls=empty",
                $"revision={revision}"
            });
        }

        var parts = scrolls.Select((scroll, index) =>
        {
            if (scroll == null)
            {
                return $"{index}:null";
            }

            string scrollKey = BuildScriptableObjectKey(scroll, SafeString(() => scroll.scrollName, string.Empty));
            string skillKey = BuildScriptableObjectKey(scroll.skillData, SafeString(() => scroll.skillData.skillName, string.Empty));
            return $"{index}:scroll={scrollKey};asset={scroll.name};skill={skillKey};revision={revision}";
        });

        return HashStableParts(parts);
    }

    private static AugmentSnapshot CaptureAugment(PlayerManager player)
    {
        var presented = SafeRef(() => player.augmentManager != null ? player.augmentManager.GetPresentedAugments() : null, null);
        var chosen = SafeRef(() => player.chosenAugments, null);
        var localPresentedParts = presented != null && presented.Count > 0
            ? presented.Select(augment => augment != null ? augment.augmentName ?? string.Empty : "null")
            : Array.Empty<string>() as IEnumerable<string>;
        var networkPresentedParts = SafeRef(() => player.GetPresentedAugmentSnapshotNames(), null) ?? Array.Empty<string>();
        var localSelectedParts = chosen != null && chosen.Count > 0
            ? chosen.Select(augment => augment != null ? augment.augmentName ?? string.Empty : "null")
            : Array.Empty<string>() as IEnumerable<string>;
        var networkSelectedParts = SafeRef(() => player.GetSelectedAugmentSnapshotNames(), null) ?? Array.Empty<string>();
        var selectedParts = networkSelectedParts.Any() ? networkSelectedParts : localSelectedParts;
        var presentedParts = networkPresentedParts.Any()
            ? networkPresentedParts
            : (networkSelectedParts.Any() ? Array.Empty<string>() : localPresentedParts);

        int presentedCount = presentedParts.Count();
        int selectedCount = selectedParts.Count();
        var activeEffectParts = BuildAugmentActiveEffectParts(player).ToArray();
        var activeTargetParts = BuildAugmentActiveTargetParts(player).ToArray();
        return new AugmentSnapshot
        {
            Available = presentedCount > 0 || selectedCount > 0,
            SelectedCount = selectedCount,
            PresentedCount = presentedCount,
            PresentedHash = presentedCount > 0 ? HashStableParts(presentedParts) : Unknown,
            SelectedHash = selectedCount > 0 ? HashStableParts(selectedParts) : Unknown,
            ActiveEffectCount = activeEffectParts.Length,
            ActiveTargetCount = activeTargetParts.Length,
            ActiveEffectHash = activeEffectParts.Length > 0 ? HashStableParts(activeEffectParts) : Unknown,
            ActiveTargetHash = activeTargetParts.Length > 0 ? HashStableParts(activeTargetParts) : Unknown
        };
    }

    private static IEnumerable<string> BuildAugmentActiveEffectParts(PlayerManager player)
    {
        int playerId = SafeInt(() => player.playerId, -1);
        foreach (var augment in EnumerateSelectedAugmentsForSnapshot(player))
        {
            yield return $"selected;owner={playerId};{BuildAugmentEffectPart(augment)}";
        }

        var scrolls = SafeRef(() => player.OwnedScrolls, null);
        if (scrolls != null)
        {
            foreach (var scroll in scrolls.Where(scroll => scroll != null))
            {
                yield return $"scroll;owner={playerId};name={BuildScriptableObjectKey(scroll, SafeString(() => scroll.scrollName, string.Empty))}";
            }
        }

        int attackDamageBucket = Mathf.RoundToInt(SafeFloat(() => player.GetSnapshotPermanentAttackDamagePercent(), 0f) * 1000f);
        int attackSpeedBucket = Mathf.RoundToInt(SafeFloat(() => player.GetSnapshotPermanentAttackSpeedPercent(), 0f) * 1000f);
        if (attackDamageBucket != 0 || attackSpeedBucket != 0)
        {
            yield return $"permanentStats;owner={playerId};attackDamagePermille={attackDamageBucket};attackSpeedPermille={attackSpeedBucket}";
        }
    }

    private static IEnumerable<string> BuildAugmentActiveTargetParts(PlayerManager player)
    {
        int playerId = SafeInt(() => player.playerId, -1);
        foreach (var augment in EnumerateSelectedAugmentsForSnapshot(player))
        {
            yield return $"selected;owner={playerId};augment={SafeString(() => augment.augmentName, string.Empty)};target={ResolveAugmentTargetPart(player, augment)}";
        }

    }

    private static IEnumerable<AugmentData> EnumerateSelectedAugmentsForSnapshot(PlayerManager player)
    {
        var chosen = SafeRef(() => player.chosenAugments, null);
        if (chosen != null && chosen.Any(augment => augment != null))
        {
            foreach (var augment in chosen.Where(augment => augment != null))
            {
                yield return augment;
            }

            yield break;
        }

        var names = SafeRef(() => player.GetSelectedAugmentSnapshotNames(), null);
        if (names == null || names.Length == 0 || player.augmentManager == null)
        {
            yield break;
        }

        foreach (string augmentName in names.Where(name => !string.IsNullOrWhiteSpace(name)))
        {
            AugmentData augment = SafeRef(() => player.augmentManager.FindAugmentByName(augmentName), null);
            if (augment != null)
            {
                yield return augment;
            }
        }
    }

    private static string BuildAugmentEffectPart(AugmentData augment)
    {
        string name = SafeString(() => augment.augmentName, string.Empty);
        int valuePermille = Mathf.RoundToInt(SafeFloat(() => augment.value, 0f) * 1000f);
        return $"name={name};tier={SafeString(() => augment.tier.ToString(), Unknown)};effect={SafeString(() => augment.effectType.ToString(), Unknown)};targetType={SafeString(() => augment.targetType.ToString(), Unknown)};valuePermille={valuePermille};payload={BuildAugmentPayloadPart(augment)}";
    }

    private static string BuildAugmentPayloadPart(AugmentData augment)
    {
        if (augment == null)
        {
            return "null";
        }

        if (augment.effectType == EffectType.SpawnMonsterOnEnemyField)
        {
            if (augment.isBossSummon)
            {
                return "boss=" + BuildMonsterDataKey(augment.bossMonsterData);
            }

            var entries = augment.monsterSpawnEntries ?? new List<MonsterSpawnEntry>();
            return "monsters=" + string.Join(",", entries
                .Where(entry => entry != null && entry.monsterData != null && entry.count > 0)
                .Select(entry => $"{BuildMonsterDataKey(entry.monsterData)}x{entry.count}")
                .OrderBy(part => part, StringComparer.Ordinal));
        }

        if (augment.effectType == EffectType.GrantMagicScroll)
        {
            return "scroll=" + BuildScriptableObjectKey(augment.magicScrollData, augment.magicScrollData != null ? augment.magicScrollData.scrollName : string.Empty);
        }

        return "none";
    }

    private static string ResolveAugmentTargetPart(PlayerManager player, AugmentData augment)
    {
        if (augment == null)
        {
            return "unknown";
        }

        if (augment.targetType == TargetType.Player)
        {
            return "player:" + SafeInt(() => player.playerId, -1);
        }

        int playerId = SafeInt(() => player.playerId, -1);
        var gameManagers = GameManagers.Instance;
        int opponentId = -1;
        if (gameManagers != null && SafeBool(() => gameManagers.TryGetBattleOpponentSnapshot(playerId, out opponentId), false))
        {
            return "opponent:" + opponentId;
        }

        return "opponent:unmapped";
    }

    private static FieldSnapshot CaptureField(PlayerManager player, List<string> errors)
    {
        var field = SafeRef(() => player.fieldManager, null);
        if (field == null)
        {
            return new FieldSnapshot
            {
                Ready = false,
                GridHash = Unknown,
                PlacedUnitCount = 0,
                PlacedUnitsHash = Unknown,
                PlacedUnitParts = Array.Empty<string>(),
                DestructibleWallCount = null,
                PermanentWallCount = null,
                WallHash = Unknown,
                PathReady = false,
                GoalReady = SafeBool(() => player.goalTransform != null, false)
            };
        }

        string wallCells = SafeString(field.BuildWallCellHash, string.Empty);
        string[] wallParts = string.IsNullOrEmpty(wallCells)
            ? Array.Empty<string>()
            : wallCells.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);

        List<Unit> units = new List<Unit>();
        try
        {
            units.AddRange(field.GetAlliedUnitsOnField().Where(unit => unit != null));
        }
        catch (Exception ex)
        {
            errors.Add($"player.{SafeInt(() => player.playerId, -1)}.field.units:{ex.GetType().Name}");
        }

        var unitParts = units
            .Select(unit => BuildUnitPart(field, unit))
            .OrderBy(part => part, StringComparer.Ordinal)
            .ToArray();

        return new FieldSnapshot
        {
            Ready = SafeBool(
                () => player.IsReadyForPlayerActions ||
                      (player.playerId >= 0 && player.fieldManager != null && player.astarGrid != null && player.goalTransform != null),
                false),
            GridHash = HashStableString(SafeString(
                () => $"{field.gridSize.x}x{field.gridSize.y}|cell={field.cellSize:F3}|origin={field.gridOrigin.x:F3},{field.gridOrigin.y:F3},{field.gridOrigin.z:F3}",
                Unknown)),
            PlacedUnitCount = units.Count,
            PlacedUnitsHash = HashStableParts(unitParts),
            PlacedUnitParts = unitParts,
            DestructibleWallCount = wallParts.Count(part => part.StartsWith("D", StringComparison.Ordinal)),
            PermanentWallCount = wallParts.Count(part => part.StartsWith("P", StringComparison.Ordinal)),
            WallHash = HashStableString(wallCells),
            PathReady = SafeBool(() => player.astarGrid != null, false),
            GoalReady = SafeBool(() => player.goalTransform != null, false)
        };
    }

    private static string BuildUnitPart(FieldManager field, Unit unit)
    {
        var pos = SafeRef(() => field.GetUnitPosition(unit), (Vector3Int?)null);
        string posText = pos.HasValue ? $"{pos.Value.x},{pos.Value.y},{pos.Value.z}" : "unknown-pos";
        string dataName = SafeRef(() => unit.Data, null) != null ? SafeString(() => unit.Data.name, "unknown-data") : "unknown-data";
        int starLevel = SafeInt(() => unit.starLevel, 0);
        if (ShouldIncludeUnitPositionInSnapshot())
        {
            return $"{posText}:{dataName}:star={starLevel}";
        }

        return $"{dataName}:star={starLevel}";
    }

    private static bool ShouldIncludeUnitPositionInSnapshot()
    {
        var managers = SafeRef(() => GameManagers.Instance, null);
        var state = managers != null
            ? SafeRef(() => managers.GetGameState(), GameManagers.GameState.Setup)
            : GameManagers.GameState.Setup;
        return state == GameManagers.GameState.Battle1 || state == GameManagers.GameState.Battle2;
    }

    private static MonsterSnapshot CaptureMonsters(PlayerManager player)
    {
        var spawner = SafeRef(() => player.monsterSpawner, null);
        if (spawner == null)
        {
            return new MonsterSnapshot
            {
                Ready = false,
                AliveCount = 0,
                LivingHash = Unknown,
                TypeHash = Unknown,
                TypeCountHpHash = Unknown,
                OwnerOriginHash = Unknown,
                TargetPlayerHash = Unknown,
                HpBucketHash = Unknown,
                BossPoolIdentityHash = Unknown,
                AutoSpawnRunning = null
            };
        }

        var monsters = CollectLivingMonsters(player, spawner).ToArray();
        var livingParts = monsters.Select(BuildMonsterLivingPart).ToArray();
        var typeParts = monsters
            .Select(BuildMonsterTypePart)
            .GroupBy(part => part, StringComparer.Ordinal)
            .Select(group => $"{group.Key}:count={group.Count()}")
            .ToArray();
        var typeCountParts = monsters
            .Select(BuildMonsterTypeHpBucketPart)
            .GroupBy(part => part, StringComparer.Ordinal)
            .Select(group => $"{group.Key}:count={group.Count()}")
            .ToArray();
        var hpBucketParts = monsters
            .Select(BuildMonsterHpBucketPart)
            .GroupBy(part => part, StringComparer.Ordinal)
            .Select(group => $"{group.Key}:count={group.Count()}")
            .ToArray();
        var ownerOriginParts = monsters
            .Select(monster => BuildMonsterOwnerOriginPart(player, monster))
            .ToArray();
        var targetParts = monsters
            .Select(monster => BuildMonsterTargetPart(player, monster))
            .ToArray();
        var bossPoolIdentityParts = monsters
            .Where(monster => SafeBool(() => monster.SnapshotIsBoss, false))
            .Select(BuildMonsterBossPoolIdentityPart)
            .ToArray();

        return new MonsterSnapshot
        {
            Ready = spawner.IsRuntimeReady(out _),
            AliveCount = monsters.Length,
            LivingHash = livingParts.Length > 0 ? HashStableParts(livingParts) : Unknown,
            TypeHash = typeParts.Length > 0 ? HashStableParts(typeParts) : Unknown,
            TypeCountHpHash = typeCountParts.Length > 0 ? HashStableParts(typeCountParts) : Unknown,
            OwnerOriginHash = ownerOriginParts.Length > 0 ? HashStableParts(ownerOriginParts) : Unknown,
            TargetPlayerHash = targetParts.Length > 0 ? HashStableParts(targetParts) : Unknown,
            HpBucketHash = hpBucketParts.Length > 0 ? HashStableParts(hpBucketParts) : Unknown,
            BossPoolIdentityHash = bossPoolIdentityParts.Length > 0 ? HashStableParts(bossPoolIdentityParts) : Unknown,
            AutoSpawnRunning = null
        };
    }

    private static IEnumerable<Monster> CollectLivingMonsters(PlayerManager player, MonsterSpawner spawner)
    {
        int playerId = SafeInt(() => player.playerId, -1);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        if (spawner?.monsterParent != null)
        {
            foreach (Transform child in spawner.monsterParent)
            {
                var monster = child != null ? child.GetComponent<Monster>() : null;
                if (!ShouldIncludeMonsterForPlayer(monster, playerId, allowUnknownOwner: true))
                {
                    continue;
                }

                if (seen.Add(BuildMonsterStableIdentity(monster)))
                {
                    yield return monster;
                }
            }
        }

        foreach (var monster in UnityEngine.Object.FindObjectsOfType<Monster>())
        {
            if (!ShouldIncludeMonsterForPlayer(monster, playerId, allowUnknownOwner: false))
            {
                continue;
            }

            if (seen.Add(BuildMonsterStableIdentity(monster)))
            {
                yield return monster;
            }
        }
    }

    private static bool ShouldIncludeMonsterForPlayer(Monster monster, int playerId, bool allowUnknownOwner)
    {
        if (monster == null || !SafeBool(() => monster.gameObject.activeInHierarchy, false))
        {
            return false;
        }

        var networkObject = SafeRef(() => monster.Object, null);
        if (networkObject == null || !SafeBool(() => networkObject.IsValid, false))
        {
            return false;
        }

        if (SafeFloat(() => monster.NetworkedHP, 0f) <= 0f)
        {
            return false;
        }

        int ownerId = SafeInt(() => monster.SnapshotOwnerPlayerId, -1);
        if (ownerId >= 0)
        {
            return ownerId == playerId;
        }

        return allowUnknownOwner;
    }

    private static string BuildMonsterStableIdentity(Monster monster)
    {
        return SafeString(() => monster.Object.Id.ToString(), "no-network");
    }

    private static string BuildMonsterLivingPart(Monster monster)
    {
        string networkId = SafeString(() => monster.Object.Id.ToString(), "no-network");
        return $"{BuildMonsterTypeHpBucketPart(monster)};net={networkId}";
    }

    private static string BuildMonsterTypePart(Monster monster)
    {
        bool isBoss = SafeBool(() => monster.SnapshotIsBoss, false);
        return $"type={BuildMonsterDataKey(monster)};monsterType={BuildMonsterTypeName(monster)};traits={BuildMonsterTraitsName(monster)};boss={isBoss}";
    }

    private static string BuildMonsterTypeHpBucketPart(Monster monster)
    {
        float hp = SafeFloat(() => monster.NetworkedHP, 0f);
        float maxHp = SafeFloat(() => monster.NetworkedMaxHP, 0f);
        bool isBoss = SafeBool(() => monster.SnapshotIsBoss, false);
        int bossUniqueId = isBoss ? SafeInt(() => monster.SnapshotBossUniqueId, -1) : -1;
        int bossOriginId = isBoss ? SafeInt(() => monster.SnapshotBossOriginPlayerId, -1) : -1;
        int hpBucket = BuildHpBucket(hp, maxHp);
        int maxHpBucket = Mathf.Max(0, Mathf.RoundToInt(maxHp / 10f));
        return $"type={BuildMonsterDataKey(monster)};monsterType={BuildMonsterTypeName(monster)};traits={BuildMonsterTraitsName(monster)};boss={isBoss};bossId={bossUniqueId};bossOrigin={bossOriginId};hpBucket={hpBucket};maxHpBucket={maxHpBucket}";
    }

    private static string BuildMonsterHpBucketPart(Monster monster)
    {
        float hp = SafeFloat(() => monster.NetworkedHP, 0f);
        float maxHp = SafeFloat(() => monster.NetworkedMaxHP, 0f);
        int hpBucket = BuildHpBucket(hp, maxHp);
        int maxHpBucket = Mathf.Max(0, Mathf.RoundToInt(maxHp / 10f));
        return $"type={BuildMonsterDataKey(monster)};hpBucket={hpBucket};maxHpBucket={maxHpBucket}";
    }

    private static string BuildMonsterOwnerOriginPart(PlayerManager fieldOwner, Monster monster)
    {
        int fieldOwnerId = SafeInt(() => fieldOwner.playerId, -1);
        int ownerId = SafeInt(() => monster.SnapshotOwnerPlayerId, -1);
        bool isBoss = SafeBool(() => monster.SnapshotIsBoss, false);
        int bossOriginId = isBoss ? SafeInt(() => monster.SnapshotBossOriginPlayerId, -1) : -1;
        return $"type={BuildMonsterDataKey(monster)};fieldOwner={fieldOwnerId};owner={ownerId};boss={isBoss};bossOrigin={bossOriginId}";
    }

    private static string BuildMonsterTargetPart(PlayerManager fieldOwner, Monster monster)
    {
        int defenderId = SafeInt(() => fieldOwner.playerId, -1);
        int opponentId = -1;
        var gameManagers = GameManagers.Instance;
        if (gameManagers != null)
        {
            SafeBool(() => gameManagers.TryGetBattleOpponentSnapshot(defenderId, out opponentId), false);
        }
        bool defenderIsAttacker = SafeBool(() => fieldOwner.IsAttackerInCurrentBattle, false);
        int attackerId = defenderIsAttacker ? defenderId : opponentId;
        bool isBoss = SafeBool(() => monster.SnapshotIsBoss, false);
        int bossUniqueId = isBoss ? SafeInt(() => monster.SnapshotBossUniqueId, -1) : -1;
        return $"type={BuildMonsterDataKey(monster)};defender={defenderId};attacker={attackerId};defenderIsAttacker={defenderIsAttacker};boss={isBoss};bossId={bossUniqueId}";
    }

    private static string BuildMonsterBossPoolIdentityPart(Monster monster)
    {
        return $"type={BuildMonsterDataKey(monster)};bossId={SafeInt(() => monster.SnapshotBossUniqueId, -1)};bossOrigin={SafeInt(() => monster.SnapshotBossOriginPlayerId, -1)};owner={SafeInt(() => monster.SnapshotOwnerPlayerId, -1)}";
    }

    private static int BuildHpBucket(float currentHp, float maxHp)
    {
        if (maxHp <= 0f)
        {
            return Mathf.Max(0, Mathf.RoundToInt(currentHp / 10f));
        }

        float ratio = Mathf.Clamp01(currentHp / maxHp);
        return Mathf.Clamp(Mathf.FloorToInt(ratio * 10f), 0, 10);
    }

    private static string BuildMonsterDataKey(MonsterData data)
    {
        if (data == null)
        {
            return "null";
        }

        return BuildScriptableObjectKey(data, data.monsterName);
    }

    private static string BuildMonsterDataKey(Monster monster)
    {
        string key = SafeString(() => monster.SnapshotMonsterDataKey, string.Empty);
        if (!string.IsNullOrWhiteSpace(key))
        {
            return key.Trim();
        }

        return BuildMonsterDataKey(SafeRef(() => monster.Data, null));
    }

    private static string BuildMonsterTypeName(Monster monster)
    {
        string value = SafeString(() => monster.SnapshotMonsterTypeName, Unknown);
        return string.IsNullOrWhiteSpace(value) ? Unknown : value.Trim();
    }

    private static string BuildMonsterTraitsName(Monster monster)
    {
        string value = SafeString(() => monster.SnapshotMonsterTraitsName, Unknown);
        return string.IsNullOrWhiteSpace(value) ? Unknown : value.Trim();
    }

    private static string BuildScriptableObjectKey(ScriptableObject asset, string preferredName)
    {
        if (!string.IsNullOrWhiteSpace(preferredName))
        {
            return preferredName.Trim();
        }

        return asset != null && !string.IsNullOrWhiteSpace(asset.name) ? asset.name.Trim() : "null";
    }

    private static string BuildUnitDataKey(Unit unit)
    {
        var data = SafeRef(() => unit.Data, null);
        return BuildScriptableObjectKey(data, SafeString(() => data.unitName, string.Empty));
    }

    private static int BuildManaBucket(float currentMana, float maxMana)
    {
        if (maxMana <= 0f)
        {
            return Mathf.Max(0, Mathf.RoundToInt(currentMana));
        }

        float ratio = Mathf.Clamp01(currentMana / maxMana);
        return Mathf.Clamp(Mathf.FloorToInt(ratio * 10f), 0, 10);
    }

    private static AiSnapshot CaptureAi(int playerId, PlayerManager player)
    {
        bool registered = ComponentRegistry.Has<AIPlayerController>(playerId.ToString());
        return new AiSnapshot
        {
            ControllerRegistered = registered,
            PrepareReady = registered ? player.mazeConstructionComplete && player.unitPurchaseComplete : (bool?)null,
            CombatReady = registered ? player.monsterSpawner != null : (bool?)null
        };
    }

    private static ObjectsSnapshot CaptureObjects()
    {
        return new ObjectsSnapshot
        {
            NetworkObjectCount = UnityEngine.Object.FindObjectsOfType<NetworkObject>().Length,
            PlayerManagerCount = UnityEngine.Object.FindObjectsOfType<PlayerManager>().Length,
            UnitCount = UnityEngine.Object.FindObjectsOfType<Unit>().Length,
            MonsterCount = UnityEngine.Object.FindObjectsOfType<Monster>().Length,
            WallCount = UnityEngine.Object.FindObjectsOfType<DestructibleWall>().Length
        };
    }

    private static EffectsSnapshot CaptureEffects()
    {
        var buffParts = new List<string>();
        var statusParts = new List<string>();
        int activeBuffCount = 0;
        int activeStatusCount = 0;

        foreach (var buffManager in UnityEngine.Object.FindObjectsOfType<BuffManager>())
        {
            if (buffManager == null)
            {
                continue;
            }

            string targetKey = BuildEffectTargetKey(buffManager.gameObject);
            activeBuffCount += SafeInt(() => buffManager.ActiveBuffCount, 0);
            activeStatusCount += SafeInt(() => buffManager.ActiveStatusEffectCount, 0);
            buffParts.AddRange(SafeRef(() => buffManager.BuildActiveBuffSnapshotParts(targetKey), Enumerable.Empty<string>()));
            statusParts.AddRange(SafeRef(() => buffManager.BuildActiveStatusSnapshotParts(targetKey), Enumerable.Empty<string>()));
        }

        var zoneParts = new List<string>();
        foreach (var zone in UnityEngine.Object.FindObjectsOfType<ZoneController>())
        {
            if (zone != null && SafeBool(() => zone.IsSnapshotActive, false))
            {
                zoneParts.Add(SafeString(zone.BuildSnapshotPart, Unknown));
            }
        }

        return new EffectsSnapshot
        {
            ActiveBuffCount = activeBuffCount,
            ActiveStatusCount = activeStatusCount,
            ZoneCount = zoneParts.Count,
            ActiveBuffHash = HashStableParts(buffParts.Count > 0 ? buffParts : new[] { "activeBuffs=empty" }),
            ActiveStatusHash = HashStableParts(statusParts.Count > 0 ? statusParts : new[] { "activeStatuses=empty" }),
            ZoneHash = HashStableParts(zoneParts.Count > 0 ? zoneParts : new[] { "zones=empty" })
        };
    }

    private static string BuildEffectTargetKey(GameObject target)
    {
        if (target == null)
        {
            return "target=null";
        }

        if (target.TryGetComponent<Unit>(out var unit))
        {
            int ownerId = SafeInt(() => unit.Owner.playerId, -1);
            string unitKey = BuildScriptableObjectKey(unit.Data, SafeString(() => unit.Data.unitName, string.Empty));
            int star = SafeInt(() => unit.starLevel, 0);
            return $"unit;owner={ownerId};data={unitKey};star={star};{BuildUnitTargetLocationPart(unit)}";
        }

        if (target.TryGetComponent<Monster>(out var monster))
        {
            int ownerId = SafeInt(() => monster.SnapshotOwnerPlayerId, -1);
            string monsterKey = BuildScriptableObjectKey(monster.Data, SafeString(() => monster.Data.monsterName, string.Empty));
            bool isBoss = SafeBool(() => monster.SnapshotIsBoss, false);
            int bossId = SafeInt(() => monster.SnapshotBossUniqueId, -1);
            int bossOriginId = SafeInt(() => monster.SnapshotBossOriginPlayerId, -1);
            int hpBucket = BuildHpBucket(SafeFloat(() => monster.NetworkedHP, 0f), SafeFloat(() => monster.NetworkedMaxHP, 0f));
            return $"monster;owner={ownerId};data={monsterKey};boss={isBoss};bossId={bossId};bossOrigin={bossOriginId};hpBucket={hpBucket};{BuildMonsterTargetLocationPart(monster, ownerId)}";
        }

        if (target.TryGetComponent<DestructibleWall>(out var wall))
        {
            var grid = SafeRef(() => wall.GridPosition, new Vector3Int(int.MinValue, int.MinValue, int.MinValue));
            int ownerId = SafeInt(() => wall.OwnerFieldManager.playerManager.playerId, -1);
            return $"wall;owner={ownerId};grid={grid.x},{grid.y},{grid.z};hpBucket={BuildHpBucket(SafeFloat(() => wall.CurrentHealth, 0f), SafeFloat(() => wall.MaxHealth, 0f))}";
        }

        return $"object;components={BuildComponentTypePart(target)};{BuildWorldPositionBucket(SafeRef(() => target.transform.position, Vector3.zero))}";
    }

    private static string BuildUnitTargetLocationPart(Unit unit)
    {
        var owner = SafeRef(() => unit.Owner, null);
        var field = owner != null ? SafeRef(() => owner.fieldManager, null) : null;
        if (field != null)
        {
            var placedCell = SafeRef(() => field.GetUnitPosition(unit), (Vector3Int?)null);
            if (placedCell.HasValue)
            {
                return $"grid={placedCell.Value.x},{placedCell.Value.y},{placedCell.Value.z}";
            }

            var worldCell = SafeRef(() => field.WorldToGridInt(unit.transform.position), new Vector3Int(int.MinValue, int.MinValue, int.MinValue));
            if (worldCell.x != int.MinValue)
            {
                return $"grid={worldCell.x},{worldCell.y},{worldCell.z}";
            }
        }

        return BuildWorldPositionBucket(SafeRef(() => unit.transform.position, Vector3.zero));
    }

    private static string BuildMonsterTargetLocationPart(Monster monster, int ownerId)
    {
        var gameManagers = GameManagers.Instance;
        var owner = gameManagers != null ? SafeRef(() => gameManagers.GetPlayer(ownerId), null) : null;
        var field = owner != null ? SafeRef(() => owner.fieldManager, null) : null;
        if (field != null)
        {
            var navigationCell = SafeRef(() => field.WorldToNavigationCell(monster.transform.position), new Vector2Int(int.MinValue, int.MinValue));
            if (navigationCell.x != int.MinValue)
            {
                return $"nav={navigationCell.x},{navigationCell.y}";
            }
        }

        return BuildWorldPositionBucket(SafeRef(() => monster.transform.position, Vector3.zero));
    }

    private static string BuildWorldPositionBucket(Vector3 position)
    {
        if (float.IsNaN(position.x) || float.IsNaN(position.y) || float.IsNaN(position.z) ||
            float.IsInfinity(position.x) || float.IsInfinity(position.y) || float.IsInfinity(position.z))
        {
            return "world=invalid";
        }

        const float bucketSize = 1f;
        int x = Mathf.FloorToInt(position.x / bucketSize);
        int y = Mathf.FloorToInt(position.y / bucketSize);
        int z = Mathf.FloorToInt(position.z / bucketSize);
        return $"world={x},{y},{z}";
    }

    private static string BuildComponentTypePart(GameObject target)
    {
        var components = SafeRef(() => target.GetComponents<Component>(), Array.Empty<Component>());
        return string.Join(",", components
            .Where(component => component != null)
            .Select(component => component.GetType().Name)
            .OrderBy(name => name, StringComparer.Ordinal));
    }

    private static CommandsSnapshot CaptureCommands(GameManagers gameManagers)
    {
        return new CommandsSnapshot
        {
            LastSequence = BattleCommandTelemetry.AcceptedBattleCommandSeq > 0
                ? BattleCommandTelemetry.AcceptedBattleCommandSeq
                : (int?)null,
            QueueDepth = TryGetCommandQueueDepth(gameManagers),
            LastCommand = string.IsNullOrWhiteSpace(BattleCommandTelemetry.LastCommand)
                ? Unknown
                : BattleCommandTelemetry.LastCommand,
            AcceptedBattleCommandSeq = BattleCommandTelemetry.AcceptedBattleCommandSeq,
            SpawnMonsterSeq = BattleCommandTelemetry.SpawnMonsterSeq,
            UseMagicScrollSeq = BattleCommandTelemetry.UseMagicScrollSeq,
            ActivateSkillSeq = BattleCommandTelemetry.ActivateSkillSeq,
            RejectedBattleCommandCount = BattleCommandTelemetry.RejectedBattleCommandCount
        };
    }

    private static int ReadMaxPlayers(NetworkManager networkManager)
    {
        var field = typeof(NetworkManager).GetField("maxSessionPlayers", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        if (field == null)
        {
            return 0;
        }

        object value = field.GetValue(networkManager);
        return value is int maxPlayers ? maxPlayers : 0;
    }

    private static int? TryGetCommandQueueDepth(GameManagers gameManagers)
    {
        if (gameManagers == null || gameManagers.CommandProcessor == null)
        {
            return null;
        }

        var field = typeof(CommandProcessor).GetField("_commandQueue", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var queue = field != null ? field.GetValue(gameManagers.CommandProcessor) as System.Collections.ICollection : null;
        return queue != null ? queue.Count : (int?)null;
    }

    private static HostMigrationSnapshot CaptureHostMigration()
    {
        var handler = HostMigrationHandler.Instance;
        return new HostMigrationSnapshot
        {
            HandlerExists = handler != null,
            IsMigrating = handler != null && handler.IsMigrating,
            RecoverySucceeded = handler != null ? handler.MigrationRecoverySucceeded : (bool?)null,
            AiTakeoverReady = handler != null ? handler.IsAiTakeoverReady : (bool?)null,
            LastEvent = string.IsNullOrWhiteSpace(MPTestHostMigrationEvents.LastEvent) ? Unknown : MPTestHostMigrationEvents.LastEvent,
            EventCount = MPTestHostMigrationEvents.EventCount,
            OnHostMigrationCount = MPTestHostMigrationEvents.OnHostMigrationCount,
            NonNullTokenCount = MPTestHostMigrationEvents.NonNullTokenCount,
            ResumeCount = MPTestHostMigrationEvents.ResumeCount,
            StartGameSuccessCount = MPTestHostMigrationEvents.StartGameSuccessCount,
            CompleteCount = MPTestHostMigrationEvents.CompleteCount,
            FailureCount = MPTestHostMigrationEvents.FailureCount
        };
    }

    private static TestSnapshot CaptureTest()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        var driver = MPTestHumanBotDriver.Instance;
        if (driver != null)
        {
            var status = driver.Status;
            return new TestSnapshot
            {
                Bot = new BotSnapshot
                {
                    Enabled = status.Enabled,
                    Running = status.Running,
                    Persona = status.Persona,
                    CommandsIssued = status.CommandsIssued,
                    LastDecision = status.LastDecision,
                    LastCommandType = status.LastCommandType,
                    LastError = status.LastError,
                    JournalPath = status.JournalPath,
                    PlayerId = status.PlayerId,
                    HasLocalInputAuthority = status.HasLocalInputAuthority,
                    StopReason = status.StopReason
                },
                RandomOutcomes = new RandomOutcomesSnapshot()
            };
        }
#endif

        var options = MPTestCommandLine.GetOptions();
        return new TestSnapshot
        {
            Bot = new BotSnapshot
            {
                Enabled = options.Enabled && options.HumanBot,
                Running = false,
                Persona = options.BotPersona,
                CommandsIssued = 0,
                LastDecision = null,
                LastCommandType = null,
                LastError = null,
                JournalPath = options.BotRecordJournal,
                PlayerId = -1,
                HasLocalInputAuthority = false,
                StopReason = null
            },
            RandomOutcomes = new RandomOutcomesSnapshot()
        };
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

    private static T SafeRef<T>(Func<T> getter, T fallback)
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

    private static float SafeFloat(Func<float> getter, float fallback)
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

    private static bool SafeBool(Func<bool> getter, bool fallback)
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

    private static string SafeString(Func<string> getter, string fallback)
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

    [Serializable]
    public sealed class Snapshot
    {
        [JsonProperty("version")] public int Version;
        [JsonProperty("role")] public string Role;
        [JsonProperty("caseName")] public string CaseName;
        [JsonProperty("session")] public string Session;
        [JsonProperty("scene")] public string Scene;
        [JsonProperty("timestampUtc")] public string TimestampUtc;
        [JsonProperty("runner")] public RunnerSnapshot Runner;
        [JsonProperty("game")] public GameSnapshot Game;
        [JsonProperty("players")] public PlayerSnapshot[] Players;
        [JsonProperty("objects")] public ObjectsSnapshot Objects;
        [JsonProperty("effects")] public EffectsSnapshot Effects;
        [JsonProperty("commands")] public CommandsSnapshot Commands;
        [JsonProperty("hostMigration")] public HostMigrationSnapshot HostMigration;
        [JsonProperty("test")] public TestSnapshot Test;
        [JsonProperty("errors")] public List<string> Errors;
    }

    [Serializable]
    public sealed class RunnerSnapshot
    {
        [JsonProperty("isRunning")] public bool IsRunning;
        [JsonProperty("gameMode")] public string GameMode;
        [JsonProperty("isServer")] public bool IsServer;
        [JsonProperty("isClient")] public bool IsClient;
        [JsonProperty("tick")] public int Tick;
        [JsonProperty("activePlayerCount")] public int ActivePlayerCount;
        [JsonProperty("maxPlayers")] public int MaxPlayers;
        [JsonProperty("localPlayerRef")] public string LocalPlayerRef;
    }

    [Serializable]
    public sealed class GameSnapshot
    {
        [JsonProperty("hasGameManagers")] public bool HasGameManagers;
        [JsonProperty("currentState")] public string CurrentState;
        [JsonProperty("battlePhase")] public string BattlePhase;
        [JsonProperty("currentRound")] public int CurrentRound;
        [JsonProperty("phaseTimerRemaining")] public float PhaseTimerRemaining;
        [JsonProperty("firstAttackerPlayerId")] public int FirstAttackerPlayerId;
        [JsonProperty("battleOpponentsHash")] public string BattleOpponentsHash;
        [JsonProperty("matchFirstAttackerHash")] public string MatchFirstAttackerHash;
        [JsonProperty("battleActiveHash")] public string BattleActiveHash;
        [JsonProperty("survivorBossPendingHash")] public string SurvivorBossPendingHash;
        [JsonProperty("survivorBossAssignmentHash")] public string SurvivorBossAssignmentHash;
        [JsonProperty("survivorBossPendingCount")] public int? SurvivorBossPendingCount;
        [JsonProperty("survivorBossAssignmentCount")] public int? SurvivorBossAssignmentCount;
    }

    [Serializable]
    public sealed class PlayerSnapshot
    {
        [JsonProperty("playerId")] public int PlayerId;
        [JsonProperty("networkId")] public string NetworkId;
        [JsonProperty("playerRef")] public string PlayerRef;
        [JsonProperty("connectionTokenHash")] public string ConnectionTokenHash;
        [JsonProperty("hasInputAuthority")] public bool HasInputAuthority;
        [JsonProperty("hasStateAuthority")] public bool HasStateAuthority;
        [JsonProperty("isLocal")] public bool IsLocal;
        [JsonProperty("isAI")] public bool IsAI;
        [JsonProperty("isConnected")] public bool IsConnected;
        [JsonProperty("health")] public int Health;
        [JsonProperty("gold")] public int Gold;
        [JsonProperty("wallCount")] public int WallCount;
        [JsonProperty("isActivelyFighting")] public bool IsActivelyFighting;
        [JsonProperty("isAttackerInCurrentBattle")] public bool IsAttackerInCurrentBattle;
        [JsonProperty("attackMonsterPoolHash")] public string AttackMonsterPoolHash;
        [JsonProperty("ownedScrollsHash")] public string OwnedScrollsHash;
        [JsonProperty("ownedScrollRevision")] public int OwnedScrollRevision;
        [JsonProperty("manualSkillReadyHash")] public string ManualSkillReadyHash;
        [JsonProperty("shop")] public ShopSnapshot Shop;
        [JsonProperty("augment")] public AugmentSnapshot Augment;
        [JsonProperty("field")] public FieldSnapshot Field;
        [JsonProperty("monsters")] public MonsterSnapshot Monsters;
        [JsonProperty("ai")] public AiSnapshot Ai;
    }

    [Serializable]
    public sealed class ShopSnapshot
    {
        [JsonProperty("available")] public bool Available;
        [JsonProperty("revision")] public int? Revision;
        [JsonProperty("round")] public int? Round;
        [JsonProperty("count")] public int Count;
        [JsonProperty("itemsHash")] public string ItemsHash;
    }

    [Serializable]
    public sealed class AugmentSnapshot
    {
        [JsonProperty("available")] public bool Available;
        [JsonProperty("selectedCount")] public int SelectedCount;
        [JsonProperty("presentedCount")] public int PresentedCount;
        [JsonProperty("presentedHash")] public string PresentedHash;
        [JsonProperty("selectedHash")] public string SelectedHash;
        [JsonProperty("activeEffectCount")] public int ActiveEffectCount;
        [JsonProperty("activeTargetCount")] public int ActiveTargetCount;
        [JsonProperty("activeEffectHash")] public string ActiveEffectHash;
        [JsonProperty("activeTargetHash")] public string ActiveTargetHash;
    }

    [Serializable]
    public sealed class FieldSnapshot
    {
        [JsonProperty("ready")] public bool Ready;
        [JsonProperty("gridHash")] public string GridHash;
        [JsonProperty("placedUnitCount")] public int PlacedUnitCount;
        [JsonProperty("placedUnitsHash")] public string PlacedUnitsHash;
        [JsonProperty("placedUnitParts")] public string[] PlacedUnitParts;
        [JsonProperty("destructibleWallCount")] public int? DestructibleWallCount;
        [JsonProperty("permanentWallCount")] public int? PermanentWallCount;
        [JsonProperty("wallHash")] public string WallHash;
        [JsonProperty("pathReady")] public bool PathReady;
        [JsonProperty("goalReady")] public bool GoalReady;
    }

    [Serializable]
    public sealed class MonsterSnapshot
    {
        [JsonProperty("ready")] public bool Ready;
        [JsonProperty("aliveCount")] public int AliveCount;
        [JsonProperty("livingHash")] public string LivingHash;
        [JsonProperty("typeHash")] public string TypeHash;
        [JsonProperty("typeCountHpHash")] public string TypeCountHpHash;
        [JsonProperty("ownerOriginHash")] public string OwnerOriginHash;
        [JsonProperty("targetPlayerHash")] public string TargetPlayerHash;
        [JsonProperty("hpBucketHash")] public string HpBucketHash;
        [JsonProperty("bossPoolIdentityHash")] public string BossPoolIdentityHash;
        [JsonProperty("autoSpawnRunning")] public bool? AutoSpawnRunning;
    }

    [Serializable]
    public sealed class AiSnapshot
    {
        [JsonProperty("controllerRegistered")] public bool ControllerRegistered;
        [JsonProperty("prepareReady")] public bool? PrepareReady;
        [JsonProperty("combatReady")] public bool? CombatReady;
    }

    [Serializable]
    public sealed class ObjectsSnapshot
    {
        [JsonProperty("networkObjectCount")] public int NetworkObjectCount;
        [JsonProperty("playerManagerCount")] public int PlayerManagerCount;
        [JsonProperty("unitCount")] public int UnitCount;
        [JsonProperty("monsterCount")] public int MonsterCount;
        [JsonProperty("wallCount")] public int WallCount;
    }

    [Serializable]
    public sealed class EffectsSnapshot
    {
        [JsonProperty("activeBuffCount")] public int ActiveBuffCount;
        [JsonProperty("activeStatusCount")] public int ActiveStatusCount;
        [JsonProperty("zoneCount")] public int ZoneCount;
        [JsonProperty("activeBuffHash")] public string ActiveBuffHash;
        [JsonProperty("activeStatusHash")] public string ActiveStatusHash;
        [JsonProperty("zoneHash")] public string ZoneHash;
    }

    [Serializable]
    public sealed class CommandsSnapshot
    {
        [JsonProperty("lastSequence")] public int? LastSequence;
        [JsonProperty("queueDepth")] public int? QueueDepth;
        [JsonProperty("lastCommand")] public string LastCommand;
        [JsonProperty("acceptedBattleCommandSeq")] public int AcceptedBattleCommandSeq;
        [JsonProperty("spawnMonsterSeq")] public int SpawnMonsterSeq;
        [JsonProperty("useMagicScrollSeq")] public int UseMagicScrollSeq;
        [JsonProperty("activateSkillSeq")] public int ActivateSkillSeq;
        [JsonProperty("rejectedBattleCommandCount")] public int RejectedBattleCommandCount;
    }

    [Serializable]
    public sealed class HostMigrationSnapshot
    {
        [JsonProperty("handlerExists")] public bool HandlerExists;
        [JsonProperty("isMigrating")] public bool IsMigrating;
        [JsonProperty("recoverySucceeded")] public bool? RecoverySucceeded;
        [JsonProperty("aiTakeoverReady")] public bool? AiTakeoverReady;
        [JsonProperty("lastEvent")] public string LastEvent;
        [JsonProperty("eventCount")] public int EventCount;
        [JsonProperty("onHostMigrationCount")] public int OnHostMigrationCount;
        [JsonProperty("nonNullTokenCount")] public int NonNullTokenCount;
        [JsonProperty("resumeCount")] public int ResumeCount;
        [JsonProperty("startGameSuccessCount")] public int StartGameSuccessCount;
        [JsonProperty("completeCount")] public int CompleteCount;
        [JsonProperty("failureCount")] public int FailureCount;
    }

    [Serializable]
    public sealed class TestSnapshot
    {
        [JsonProperty("bot")] public BotSnapshot Bot;
        [JsonProperty("randomOutcomes")] public RandomOutcomesSnapshot RandomOutcomes;
    }

    [Serializable]
    public sealed class BotSnapshot
    {
        [JsonProperty("enabled")] public bool Enabled;
        [JsonProperty("running")] public bool Running;
        [JsonProperty("persona")] public string Persona;
        [JsonProperty("commandsIssued")] public int CommandsIssued;
        [JsonProperty("lastDecision")] public string LastDecision;
        [JsonProperty("lastCommandType")] public string LastCommandType;
        [JsonProperty("lastError")] public string LastError;
        [JsonProperty("journalPath")] public string JournalPath;
        [JsonProperty("playerId")] public int PlayerId;
        [JsonProperty("hasLocalInputAuthority")] public bool HasLocalInputAuthority;
        [JsonProperty("stopReason")] public string StopReason;
    }

    [Serializable]
    public sealed class RandomOutcomesSnapshot
    {
        [JsonProperty("journalPath")] public string JournalPath;
        [JsonProperty("lastCategory")] public string LastCategory;
        [JsonProperty("lastPlayerId")] public int? LastPlayerId;
        [JsonProperty("lastHash")] public string LastHash;
        [JsonProperty("lastRevision")] public int? LastRevision;
    }
}
