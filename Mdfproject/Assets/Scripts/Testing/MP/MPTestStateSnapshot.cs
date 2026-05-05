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
            Commands = CaptureCommands(gameManagers),
            HostMigration = CaptureHostMigration(),
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
        int firstAttackerPlayerId = -1;

        if (gameManagers != null)
        {
            try
            {
                gameManagers.CaptureBattleSnapshotForMigration(
                    out battleOpponentsSnapshot,
                    out matchFirstAttackerSnapshot,
                    out firstAttackerPlayerId);
            }
            catch (Exception ex)
            {
                errors.Add("game.battleSnapshot:" + ex.GetType().Name);
            }
        }

        return new GameSnapshot
        {
            HasGameManagers = gameManagers != null,
            CurrentState = gameManagers != null ? SafeString(() => gameManagers.GetGameState().ToString(), Unknown) : null,
            CurrentRound = gameManagers != null ? SafeInt(() => gameManagers.currentRound, 0) : 0,
            PhaseTimerRemaining = gameManagers != null ? SafeFloat(() => gameManagers.currentPhaseTimer, 0f) : 0f,
            FirstAttackerPlayerId = gameManagers != null ? firstAttackerPlayerId : -1,
            BattleOpponentsHash = HashStableString(battleOpponentsSnapshot),
            MatchFirstAttackerHash = HashStableString(matchFirstAttackerSnapshot)
        };
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
            Shop = CaptureShop(player),
            Field = CaptureField(player, errors),
            Monsters = CaptureMonsters(player),
            Ai = CaptureAi(playerId, player)
        };
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
        var networkObject = SafeRef(() => unit.Object, null);
        bool networkObjectValid = networkObject != null && SafeBool(() => networkObject.IsValid, false);
        string networkId = networkObjectValid ? SafeString(() => networkObject.Id.ToString(), "no-network") : "no-network";
        int starLevel = SafeInt(() => unit.starLevel, 0);
        bool isDead = SafeBool(() => unit.IsDead, false);
        int currentHealth = SafeInt(() => Mathf.RoundToInt(unit.CurrentHealth), 0);
        return $"{posText}:{dataName}:star={starLevel}:dead={isDead}:hp={currentHealth}:net={networkId}";
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
                AutoSpawnRunning = null
            };
        }

        int alive = 0;
        var parts = new List<string>();
        if (spawner.monsterParent != null)
        {
            foreach (Transform child in spawner.monsterParent)
            {
                if (child == null || !child.gameObject.activeInHierarchy)
                {
                    continue;
                }

                var monster = child.GetComponent<Monster>();
                if (monster == null || monster.Object == null || !monster.Object.IsValid || monster.NetworkedHP <= 0)
                {
                    continue;
                }

                alive++;
                string networkId = monster.Object.Id.ToString();
                parts.Add($"{networkId}:hp={Mathf.RoundToInt(monster.NetworkedHP)}");
            }
        }

        return new MonsterSnapshot
        {
            Ready = spawner.IsRuntimeReady(out _),
            AliveCount = alive,
            LivingHash = HashStableParts(parts),
            AutoSpawnRunning = null
        };
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

    private static CommandsSnapshot CaptureCommands(GameManagers gameManagers)
    {
        return new CommandsSnapshot
        {
            LastSequence = null,
            QueueDepth = TryGetCommandQueueDepth(gameManagers),
            LastCommand = Unknown
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
        [JsonProperty("commands")] public CommandsSnapshot Commands;
        [JsonProperty("hostMigration")] public HostMigrationSnapshot HostMigration;
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
        [JsonProperty("currentRound")] public int CurrentRound;
        [JsonProperty("phaseTimerRemaining")] public float PhaseTimerRemaining;
        [JsonProperty("firstAttackerPlayerId")] public int FirstAttackerPlayerId;
        [JsonProperty("battleOpponentsHash")] public string BattleOpponentsHash;
        [JsonProperty("matchFirstAttackerHash")] public string MatchFirstAttackerHash;
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
        [JsonProperty("shop")] public ShopSnapshot Shop;
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
    public sealed class FieldSnapshot
    {
        [JsonProperty("ready")] public bool Ready;
        [JsonProperty("gridHash")] public string GridHash;
        [JsonProperty("placedUnitCount")] public int PlacedUnitCount;
        [JsonProperty("placedUnitsHash")] public string PlacedUnitsHash;
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
    public sealed class CommandsSnapshot
    {
        [JsonProperty("lastSequence")] public int? LastSequence;
        [JsonProperty("queueDepth")] public int? QueueDepth;
        [JsonProperty("lastCommand")] public string LastCommand;
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
}
