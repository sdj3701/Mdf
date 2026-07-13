#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Fusion;
using GameCore.Enums;
using Newtonsoft.Json.Linq;
using UnityCliConnector;
using UnityCliConnector.Tools;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

[UnityCliTool(Name = "mp_start_host", Description = "Start an MDF multiplayer host from the Editor side.")]
public static class MPStartHostTool
{
    public class Parameters
    {
        [ToolParameter("Session name to create or join.", Required = true)]
        public string Session { get; set; }

        [ToolParameter("Scene to load with Fusion. Default: Game")]
        public string Scene { get; set; }

        [ToolParameter("Maximum players. Default: 2")]
        public int MaxPlayers { get; set; }

        [ToolParameter("Wait timeout in milliseconds. Default: 15000")]
        public int TimeoutMs { get; set; }
    }

    public static Task<object> HandleCommand(JObject parameters)
    {
        return MPTestUnityCliTools.StartPeer(parameters, GameMode.Host);
    }
}

[UnityCliTool(Name = "mp_join_client", Description = "Join an MDF multiplayer session as an Editor client.")]
public static class MPJoinClientTool
{
    public class Parameters
    {
        [ToolParameter("Session name to join.", Required = true)]
        public string Session { get; set; }

        [ToolParameter("Scene to load with Fusion. Default: Game")]
        public string Scene { get; set; }

        [ToolParameter("Maximum players. Default: 2")]
        public int MaxPlayers { get; set; }

        [ToolParameter("Wait timeout in milliseconds. Default: 15000")]
        public int TimeoutMs { get; set; }
    }

    public static Task<object> HandleCommand(JObject parameters)
    {
        return MPTestUnityCliTools.StartPeer(parameters, GameMode.Client);
    }
}

[UnityCliTool(Name = "mp_load_game", Description = "Load an MDF scene through NetworkManager when possible.")]
public static class MPLoadGameTool
{
    public class Parameters
    {
        [ToolParameter("Scene name. Default: Game")]
        public string Scene { get; set; }
    }

    public static object HandleCommand(JObject parameters)
    {
        var p = new ToolParams(parameters ?? new JObject());
        var scene = p.Get("scene", "Game");
        var nm = NetworkManager.Instance;
        if (nm == null)
        {
            return MPTestUnityCliTools.NotImplemented("network_manager_missing", "NetworkManager.Instance is not available. Enter Play Mode on a scene containing NetworkManager first.");
        }

        nm.LoadSceneSmart(scene);
        return new SuccessResponse("Scene load requested.", MPTestUnityCliTools.BuildState("editor", "mp_load_game"));
    }
}

[UnityCliTool(Name = "mp_dump_state", Description = "Dump an MDF Editor-side multiplayer state snapshot.")]
public static class MPDumpStateTool
{
    public class Parameters
    {
        [ToolParameter("Role label to include in the snapshot.")]
        public string Role { get; set; }

        [ToolParameter("Case name to include in the snapshot.")]
        public string CaseName { get; set; }
    }

    public static object HandleCommand(JObject parameters)
    {
        var p = new ToolParams(parameters ?? new JObject());
        return new SuccessResponse("MDF state dumped.", MPTestUnityCliTools.BuildState(p.Get("role", "editor"), p.Get("case_name", "manual")));
    }
}

[UnityCliTool(Name = "mp_assert_state", Description = "Assert basic MDF Editor-side multiplayer state.")]
public static class MPAssertStateTool
{
    public class Parameters
    {
        [ToolParameter("Expected active player count.")]
        public int ExpectedPlayers { get; set; }

        [ToolParameter("Expected active scene name.")]
        public string Scene { get; set; }

        [ToolParameter("Expected GameManagers state.")]
        public string GameState { get; set; }
    }

    public static object HandleCommand(JObject parameters)
    {
        var p = new ToolParams(parameters ?? new JObject());
        var expectedPlayers = p.GetInt("expected_players", -1).Value;
        var expectedScene = p.Get("scene");
        var expectedState = p.Get("game_state");
        var state = MPTestUnityCliTools.BuildState("editor", "mp_assert_state");
        var assertion = MPTestAssertions.AssertBasic(state, expectedPlayers, expectedScene, expectedState);

        if (!assertion.Success)
        {
            return new ErrorResponse("MDF state assertion failed.", new { assertion, state });
        }

        return new SuccessResponse("MDF state assertion passed.", new { assertion, state });
    }
}

[UnityCliTool(Name = "mp_command", Description = "Run an MDF harness command from the Editor side.")]
public static class MPCommandTool
{
    public class Parameters
    {
        [ToolParameter("Command name or type.")]
        public string Command { get; set; }

        [ToolParameter("Target durable playerId.")]
        public int PlayerId { get; set; }

        [ToolParameter("Allow-listed canonical king key, for example UnitData_King_Mage.")]
        public string KingKey { get; set; }
    }

    public static object HandleCommand(JObject parameters)
    {
        return MPTestUnityCliTools.ExecuteCommand(parameters ?? new JObject());
    }
}

[UnityCliTool(Name = "mp_screenshot", Description = "Capture an MDF Editor screenshot.")]
public static class MPScreenshotTool
{
    public class Parameters
    {
        [ToolParameter("View to capture: scene or game. Default: game")]
        public string View { get; set; }

        [ToolParameter("Output file path, absolute or relative to project root.")]
        public string OutputPath { get; set; }

        [ToolParameter("Override width.")]
        public int Width { get; set; }

        [ToolParameter("Override height.")]
        public int Height { get; set; }
    }

    public static object HandleCommand(JObject parameters)
    {
        var p = parameters ?? new JObject();
        if (p["view"] == null)
        {
            p["view"] = "game";
        }

        return EditorScreenshot.HandleCommand(p);
    }
}

[UnityCliTool(Name = "mp_stop", Description = "Stop MDF Editor multiplayer play mode and runner state.")]
public static class MPStopTool
{
    public static object HandleCommand(JObject parameters)
    {
        var runner = NetworkManager.Instance != null ? NetworkManager.Instance._runner : null;
        if (runner != null && runner.IsRunning)
        {
            _ = runner.Shutdown();
        }

        if (EditorApplication.isPlaying)
        {
            EditorApplication.isPlaying = false;
        }

        return new SuccessResponse("MDF Editor multiplayer stop requested.", MPTestUnityCliTools.BuildState("editor", "mp_stop"));
    }
}

[UnityCliTool(Name = "mp_start_prepare_smoke", Description = "Start or report prepare smoke status.")]
public static class MPStartPrepareSmokeTool
{
    public static object HandleCommand(JObject parameters)
    {
        return MPTestUnityCliTools.NotImplemented("prepare_smoke_requires_runtime", "Prepare smoke control requires Phase 5+ runtime bootstrap and Phase 6 state assertions.");
    }
}

[UnityCliTool(Name = "mp_start_battle_smoke", Description = "Start or report battle smoke status.")]
public static class MPStartBattleSmokeTool
{
    public static object HandleCommand(JObject parameters)
    {
        return MPTestUnityCliTools.NotImplemented("battle_smoke_requires_runtime", "Battle smoke control requires Phase 5+ runtime bootstrap and Phase 6 state assertions.");
    }
}

[UnityCliTool(Name = "mp_force_host_migration_probe", Description = "Run a host migration feasibility probe when supported.")]
public static class MPForceHostMigrationProbeTool
{
    public static object HandleCommand(JObject parameters)
    {
        return MPTestUnityCliTools.NotImplemented("host_migration_probe_requires_e2e", "Host migration proof requires Phase 15 controlled host drop artifacts, not a Phase 4 Editor-only stub.");
    }
}

internal static class MPTestUnityCliTools
{
    public static async Task<object> StartPeer(JObject parameters, GameMode mode)
    {
        var p = new ToolParams(parameters ?? new JObject());
        string session = p.Get("session");
        if (string.IsNullOrWhiteSpace(session))
        {
            return new ErrorResponse("'session' parameter is required.");
        }

        string scene = p.Get("scene", "Game");
        int maxPlayers = Math.Max(2, p.GetInt("max_players", 2).Value);
        int timeoutMs = Math.Max(1000, p.GetInt("timeout_ms", 15000).Value);

        var nm = NetworkManager.Instance;
        if (nm == null)
        {
            return NotImplemented("network_manager_missing", "NetworkManager.Instance is not available. Enter Play Mode on Title/MatchingLobby first.");
        }

        SetMaxPlayers(nm, maxPlayers);
        nm.SetRoomNameInput(session);

        if (nm._runner == null)
        {
            nm.JoinLobby();
            bool lobbyReady = await WaitUntil(() => NetworkManager.Instance != null && NetworkManager.Instance.State == ConnectionState.InLobby, timeoutMs);
            if (!lobbyReady)
            {
                return new ErrorResponse("Timed out waiting for Fusion lobby.", BuildState(mode.ToString().ToLowerInvariant(), "join_lobby_timeout"));
            }
        }

        if (nm.State == ConnectionState.InLobby)
        {
            nm.StartGame(mode, session, scene);
        }

        bool runnerReady = await WaitUntil(() =>
        {
            var current = NetworkManager.Instance;
            return current != null && current._runner != null && current._runner.IsRunning;
        }, timeoutMs);

        if (!runnerReady)
        {
            return new ErrorResponse("Timed out waiting for NetworkRunner to start.", BuildState(mode.ToString().ToLowerInvariant(), "runner_timeout"));
        }

        return new SuccessResponse($"{mode} start requested.", BuildState(mode.ToString().ToLowerInvariant(), "start_peer"));
    }

    public static MPTestStateSnapshot.Snapshot BuildState(string role, string caseName)
    {
        return MPTestStateSnapshot.Capture(role, caseName);
    }

    public static int GetActivePlayerCount()
    {
        var runner = NetworkManager.Instance != null ? NetworkManager.Instance._runner : null;
        if (runner != null && runner.IsRunning)
        {
            return runner.ActivePlayers.Count();
        }

        return UnityEngine.Object.FindObjectsOfType<PlayerManager>().Length;
    }

    public static ErrorResponse NotImplemented(string code, string message)
    {
        return new ErrorResponse("not_implemented", new { code, message });
    }

    public static object ExecuteCommand(JObject parameters)
    {
        string commandName = GetString(parameters, "command", GetString(parameters, "name", "unknown"));
        if (string.Equals(commandName, "reroll_shop", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(commandName, "RerollShop", StringComparison.OrdinalIgnoreCase))
        {
            return ExecuteRerollShopCommand(parameters, commandName);
        }

        if (string.Equals(commandName, "select_king", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(commandName, "SelectKing", StringComparison.OrdinalIgnoreCase))
        {
            return ExecuteSelectKingCommand(parameters, commandName);
        }

        return CommandFail("unsupported_command", "Only reroll_shop and select_king are currently supported by the Editor command harness.", new
        {
            command = commandName
        });
    }

    private static object ExecuteSelectKingCommand(JObject parameters, string commandName)
    {
        int playerId = GetInt(parameters, "player_id", GetInt(parameters, "playerId", -1));
        string requestedKey = GetString(
            parameters,
            "king_key",
            GetString(parameters, "kingKey", GetString(parameters, "unit_key", GetString(parameters, "unitKey", null))));
        if (playerId < 0)
        {
            return CommandFail(
                "invalid_player_id",
                "playerId must be >= 0.",
                new { command = commandName, playerId, kingKey = requestedKey });
        }

        KingSelectionCatalog.Entry selectedEntry = KingSelectionCatalog.Entries
            .FirstOrDefault(entry => string.Equals(
                entry.KingUnitKey,
                requestedKey,
                StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrEmpty(selectedEntry.KingUnitKey))
        {
            return CommandFail(
                "invalid_king_key",
                "kingKey must be an allow-listed canonical UnitData_King_* key.",
                new { command = commandName, playerId, kingKey = requestedKey });
        }

        if (!EditorApplication.isPlaying)
        {
            return CommandFail(
                "editor_not_in_play_mode",
                "mp_command requires the Editor to be in Play Mode.",
                new { command = commandName, playerId, kingKey = selectedEntry.KingUnitKey });
        }

        NetworkRunner runner = NetworkManager.Instance != null ? NetworkManager.Instance._runner : null;
        if (runner == null || !runner.IsRunning)
        {
            return CommandFail(
                "lobby_runner_unavailable",
                "The lobby NetworkRunner is unavailable.",
                new { command = commandName, playerId, kingKey = selectedEntry.KingUnitKey });
        }

        if (SceneManager.GetActiveScene().name != SceneDefine.JoinLobby)
        {
            return CommandFail(
                "select_king_requires_join_lobby",
                "select_king is only available in the ready lobby.",
                new { command = commandName, playerId, kingKey = selectedEntry.KingUnitKey });
        }

        List<PlayerRef> activePlayers = runner.ActivePlayers
            .OrderBy(playerRef => playerRef.PlayerId)
            .ToList();
        if (playerId >= activePlayers.Count)
        {
            return CommandFail(
                "lobby_player_unavailable",
                "The requested gameplay player slot is not active in the lobby.",
                new { command = commandName, playerId, activePlayers = activePlayers.Count });
        }

        PlayerRef targetAuthority = activePlayers[playerId];
        List<NetworkPlayer> ownedPlayers = UnityEngine.Object.FindObjectsOfType<NetworkPlayer>()
            .Where(candidate => candidate != null
                && candidate.Runner == runner
                && candidate.Object != null
                && candidate.Object.IsValid
                && candidate.Object.InputAuthority == targetAuthority
                && candidate.HasInputAuthority)
            .ToList();
        if (ownedPlayers.Count != 1)
        {
            return CommandFail(
                "select_king_requires_owning_peer",
                "Issue select_king to the peer that owns input authority for the requested player.",
                new
                {
                    command = commandName,
                    playerId,
                    playerRef = targetAuthority.ToString(),
                    kingKey = selectedEntry.KingUnitKey,
                    ownedPlayerObjects = ownedPlayers.Count
                });
        }

        NetworkPlayer networkPlayer = ownedPlayers[0];

        if (!networkPlayer.RequestKingSelection(selectedEntry.KeyHash))
        {
            return CommandFail(
                "select_king_request_rejected",
                "The owned NetworkPlayer rejected the king selection request.",
                new { command = commandName, playerId, kingKey = selectedEntry.KingUnitKey });
        }

        MPTestLogger.Log("automation_command", "complete", "select_king", null, new Dictionary<string, object>
        {
            { "playerId", playerId },
            { "playerRef", targetAuthority.ToString() },
            { "kingKey", selectedEntry.KingUnitKey },
            { "kingKeyHash", selectedEntry.KeyHash },
            { "source", "unity_cli" }
        });
        return CommandOk("king selection requested through owning input authority", new
        {
            command = "select_king",
            playerId,
            playerRef = targetAuthority.ToString(),
            kingKey = selectedEntry.KingUnitKey,
            kingKeyHash = selectedEntry.KeyHash
        });
    }

    private static object ExecuteRerollShopCommand(JObject parameters, string commandName)
    {
        int playerId = GetInt(parameters, "player_id", GetInt(parameters, "playerId", -1));
        if (playerId < 0)
        {
            return CommandFail("invalid_player_id", "playerId must be >= 0.", new { command = commandName, playerId });
        }

        if (!EditorApplication.isPlaying)
        {
            return CommandFail("editor_not_in_play_mode", "mp_command requires the Editor to be in Play Mode.", new { command = commandName, playerId });
        }

        var gameManagers = GameManagers.Instance;
        if (gameManagers == null || gameManagers.Runner == null || !gameManagers.Runner.IsRunning)
        {
            return CommandFail("game_managers_unavailable", "GameManagers runner is not available.", new { command = commandName, playerId });
        }

        if (!gameManagers.Runner.IsServer)
        {
            return CommandFail("command_requires_server_peer", "reroll_shop must be issued to the server/host peer.", new { command = commandName, playerId });
        }

        if (gameManagers.Object == null || !gameManagers.Object.IsValid || !gameManagers.Object.HasStateAuthority)
        {
            return CommandFail("command_requires_state_authority", "reroll_shop requires GameManagers State Authority.", new { command = commandName, playerId });
        }

        if (gameManagers.CommandProcessor == null)
        {
            return CommandFail("command_processor_missing", "GameManagers.CommandProcessor is not available.", new { command = commandName, playerId });
        }

        var player = gameManagers.GetPlayer(playerId);
        if (player == null || player.shopManager == null)
        {
            return CommandFail("player_or_shop_missing", "Target player or ShopManager is missing.", new { command = commandName, playerId });
        }

        if (!player.shopManager.IsDatabaseLoaded)
        {
            return CommandFail("shop_database_not_loaded", "Shop database is not loaded yet.", new { command = commandName, playerId });
        }

        int goldBefore = player.GetGold();
        int cost = player.shopManager.GetRerollCost();
        if (goldBefore < cost)
        {
            return CommandFail("insufficient_gold", "Target player does not have enough gold for reroll_shop.", new
            {
                command = commandName,
                playerId,
                goldBefore,
                cost
            });
        }

        gameManagers.CommandProcessor.RequestCommandExecution(new RerollShopCommand(playerId));
        MPTestLogger.Log("automation_command", "begin", "reroll_shop", null, new Dictionary<string, object>
        {
            { "playerId", playerId },
            { "goldBefore", goldBefore },
            { "cost", cost }
        });

        return CommandOk("command queued", new
        {
            command = "reroll_shop",
            playerId,
            goldBefore,
            cost
        });
    }

    private static object CommandOk(string message, object data = null)
    {
        return new
        {
            success = true,
            message,
            timestampUtc = DateTime.UtcNow.ToString("o"),
            data
        };
    }

    private static object CommandFail(string code, string message, object details = null)
    {
        return new
        {
            success = false,
            message,
            timestampUtc = DateTime.UtcNow.ToString("o"),
            error = new
            {
                code,
                details
            }
        };
    }

    private static string GetString(JObject body, string key, string fallback)
    {
        if (body != null && body.TryGetValue(key, StringComparison.OrdinalIgnoreCase, out JToken token))
        {
            string value = token.Type == JTokenType.Null ? null : token.ToString();
            return string.IsNullOrEmpty(value) ? fallback : value;
        }

        return fallback;
    }

    private static int GetInt(JObject body, string key, int fallback)
    {
        if (body != null && body.TryGetValue(key, StringComparison.OrdinalIgnoreCase, out JToken token) && int.TryParse(token.ToString(), out int value))
        {
            return value;
        }

        return fallback;
    }

    private static async Task<bool> WaitUntil(Func<bool> predicate, int timeoutMs)
    {
        double start = EditorApplication.timeSinceStartup;
        double timeoutSeconds = timeoutMs / 1000.0;
        while (EditorApplication.timeSinceStartup - start < timeoutSeconds)
        {
            if (predicate())
            {
                return true;
            }

            await Task.Delay(100);
        }

        return predicate();
    }

    private static void SetMaxPlayers(NetworkManager nm, int maxPlayers)
    {
        var field = typeof(NetworkManager).GetField("maxSessionPlayers", BindingFlags.Instance | BindingFlags.NonPublic);
        if (field != null)
        {
            field.SetValue(nm, Mathf.Clamp(maxPlayers, 2, 4));
        }
    }
}
#endif
