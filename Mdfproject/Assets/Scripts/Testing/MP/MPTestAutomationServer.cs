#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Fusion;
using GameCore.Enums;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class MPTestAutomationServer : MonoBehaviour
{
    private HttpListener _listener;
    private CancellationTokenSource _cancellation;
    private MPTestCommandLine.Options _options;
    private string _automationToken;
    private bool _stopping;
    private bool _acceptingCommands = true;
    private bool _quitRequested;

    public static bool CanStart(MPTestCommandLine.Options options, out string reason)
    {
        if (!options.Enabled)
        {
            reason = "missing --mpTest";
            return false;
        }

        if (options.AutomationPort <= 0)
        {
            reason = "missing --mpAutomationPort";
            return false;
        }

        if (string.IsNullOrEmpty(options.AutomationToken))
        {
            reason = "missing --mpAutomationToken";
            return false;
        }

        reason = null;
        return true;
    }

    public void StartServer(MPTestCommandLine.Options options)
    {
        if (_listener != null)
        {
            return;
        }

        if (!CanStart(options, out string reason))
        {
            MPTestLogger.Fail("automation_server", "gate_failed", reason);
            return;
        }

        _options = options;
        _automationToken = options.AutomationToken;
        _acceptingCommands = true;
        _quitRequested = false;
        _cancellation = new CancellationTokenSource();
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://{IPAddress.Loopback}:{options.AutomationPort}/");
        _listener.Start();

        MPTestLogger.Log("automation_server", "begin", null, null, new Dictionary<string, object>
        {
            { "bind", "127.0.0.1" },
            { "port", options.AutomationPort },
            { "automationTokenHash", options.AutomationTokenHash }
        });

        Task.Run(() => ListenLoop(_cancellation.Token));
    }

    public void StopServer()
    {
        if (_stopping)
        {
            return;
        }

        _stopping = true;
        try
        {
            _cancellation?.Cancel();
            _listener?.Stop();
            _listener?.Close();
        }
        catch (Exception ex)
        {
            MPTestLogger.Fail("automation_server", "stop_error", ex.GetType().Name);
        }
        finally
        {
            _listener = null;
            _cancellation = null;
        }
    }

    private void OnDestroy()
    {
        StopServer();
    }

    private async Task ListenLoop(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _listener != null && _listener.IsListening)
        {
            HttpListenerContext context = null;
            try
            {
                context = await _listener.GetContextAsync();
                _ = Task.Run(() => HandleContext(context), cancellationToken);
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (HttpListenerException)
            {
                return;
            }
            catch (Exception ex)
            {
                MPTestLogger.Fail("automation_server", "listen_error", ex.GetType().Name);
            }
        }
    }

    private async Task HandleContext(HttpListenerContext context)
    {
        AutomationResponse response;
        int statusCode = 200;
        try
        {
            if (!IsAuthorized(context.Request))
            {
                response = AutomationResponse.Fail("unauthorized", "Missing or invalid automation token.");
                statusCode = 401;
            }
            else
            {
                response = await Route(context.Request);
                statusCode = response.Success ? 200 : 400;
            }
        }
        catch (Exception ex)
        {
            response = AutomationResponse.Fail("server_exception", ex.GetType().Name);
            statusCode = 500;
        }

        await WriteJson(context.Response, response, statusCode);
    }

    private async Task<AutomationResponse> Route(HttpListenerRequest request)
    {
        string path = request.Url.AbsolutePath.TrimEnd('/');
        if (string.IsNullOrEmpty(path))
        {
            path = "/";
        }

        if (path == "/ping")
        {
            return await RequireMethod(request, "GET", () => MainThread(() =>
            {
                var snapshot = MPTestStateSnapshot.Capture(_options.SafeRole, _options.CaseName, _options.Session);
                return AutomationResponse.Ok("ok", new
                {
                    role = _options.SafeRole,
                    session = _options.Session,
                    scene = snapshot.Scene,
                    runner = snapshot.Runner,
                    tokenHash = _options.AutomationTokenHash,
                    acceptingCommands = _acceptingCommands,
                    quitRequested = _quitRequested
                });
            }));
        }

        if (path == "/dumpState")
        {
            return await RequireMethod(request, "GET", () => MainThread(() =>
                AutomationResponse.Ok("state dumped", MPTestStateSnapshot.Capture(_options.SafeRole, _options.CaseName, _options.Session))));
        }

        if (path == "/logs/recent")
        {
            return await RequireMethod(request, "GET", () => MainThread(() =>
                AutomationResponse.Ok("recent logs", new { lines = MPTestLogger.Recent })));
        }

        if (!_acceptingCommands && path != "/ping" && path != "/quit")
        {
            return AutomationResponse.Fail("automation_shutting_down", "Automation server is shutting down.");
        }

        if (path == "/quit")
        {
            return await RequireMethod(request, "POST", () => MainThread(() =>
            {
                _quitRequested = true;
                _acceptingCommands = false;
                bool scheduled = MPTestGracefulQuit.RequestQuit(
                    _options,
                    "automation_quit",
                    0,
                    StopServer,
                    0.25f);
                return AutomationResponse.Ok("quit requested", new
                {
                    scene = SceneManager.GetActiveScene().name,
                    scheduled,
                    acceptingCommands = _acceptingCommands
                });
            }));
        }

        if (path == "/startHost")
        {
            JObject body = await ReadBody(request);
            return await RequireMethod(request, "POST", () => MainThread(() => StartPeer(body, GameMode.Host)));
        }

        if (path == "/join")
        {
            JObject body = await ReadBody(request);
            return await RequireMethod(request, "POST", () => MainThread(() => StartPeer(body, GameMode.Client)));
        }

        if (path == "/loadGame")
        {
            JObject body = await ReadBody(request);
            return await RequireMethod(request, "POST", () => MainThread(() => LoadGame(body)));
        }

        if (path == "/assertState")
        {
            JObject body = await ReadBody(request);
            return await RequireMethod(request, "POST", () => MainThread(() => AssertState(body)));
        }

        if (path == "/command")
        {
            JObject body = await ReadBody(request);
            return await RequireMethod(request, "POST", () => MainThread(() => ExecuteCommand(body)));
        }

        if (path == "/bot/start")
        {
            JObject body = await ReadBody(request);
            return await RequireMethod(request, "POST", () => MainThread(() => StartBot(body)));
        }

        if (path == "/bot/stop")
        {
            JObject body = await ReadBody(request);
            return await RequireMethod(request, "POST", () => MainThread(() => StopBot(body)));
        }

        if (path == "/bot/status")
        {
            return await RequireMethod(request, "GET", () => MainThread(BotStatus));
        }

        if (path == "/bot/journal")
        {
            return await RequireMethod(request, "GET", () => MainThread(BotJournal));
        }

        if (path == "/test/freezeGameFlow")
        {
            JObject body = await ReadBody(request);
            return await RequireMethod(request, "POST", () => MainThread(() => SetFreezeGameFlow(body)));
        }

        if (path == "/screenshot")
        {
            return await RequireMethod(request, "GET", () => MainThread(() => CaptureScreenshot(request)));
        }

        return AutomationResponse.Fail("not_found", $"Unknown endpoint {path}.");
    }

    private Task<AutomationResponse> MainThread(Func<AutomationResponse> action)
    {
        return MPTestMainThreadDispatcher.Run(action);
    }

    private async Task<AutomationResponse> RequireMethod(HttpListenerRequest request, string method, Func<Task<AutomationResponse>> action)
    {
        if (!string.Equals(request.HttpMethod, method, StringComparison.OrdinalIgnoreCase))
        {
            return AutomationResponse.Fail("method_not_allowed", $"Expected {method}.");
        }

        return await action();
    }

    private AutomationResponse StartPeer(JObject body, GameMode mode)
    {
        var networkManager = NetworkManager.Instance;
        if (networkManager == null)
        {
            return AutomationResponse.Fail("network_manager_missing", "NetworkManager.Instance is not available.");
        }

        string session = GetString(body, "session", _options.Session);
        string scene = GetString(body, "scene", _options.Scene);
        int maxPlayers = Mathf.Clamp(GetInt(body, "maxPlayers", _options.MaxPlayers), 2, 4);
        SetMaxPlayers(networkManager, maxPlayers);
        networkManager.SetRoomNameInput(session);

        if (networkManager._runner != null && networkManager._runner.IsRunning)
        {
            return AutomationResponse.Ok("runner already running", MPTestStateSnapshot.Capture(mode.ToString().ToLowerInvariant(), _options.CaseName, session));
        }

        if (networkManager.State != ConnectionState.InLobby)
        {
            networkManager.JoinLobby();
            MPTestLogger.Log("automation_start_peer", "begin", null, "join lobby requested", new Dictionary<string, object>
            {
                { "mode", mode },
                { "session", session },
                { "sceneTarget", scene }
            });

            return AutomationResponse.Ok("lobby join requested", new
            {
                mode = mode.ToString(),
                session,
                scene,
                state = networkManager.State.ToString()
            });
        }

        networkManager.StartGame(mode, session, scene);
        MPTestLogger.Log("automation_start_peer", "begin", null, "start game requested", new Dictionary<string, object>
        {
            { "mode", mode },
            { "session", session },
            { "sceneTarget", scene }
        });

        return AutomationResponse.Ok("start requested", new
        {
            mode = mode.ToString(),
            session,
            scene,
            state = networkManager.State.ToString()
        });
    }

    private AutomationResponse LoadGame(JObject body)
    {
        string scene = GetString(body, "scene", _options.Scene);
        var networkManager = NetworkManager.Instance;
        if (networkManager == null)
        {
            return AutomationResponse.Fail("network_manager_missing", "NetworkManager.Instance is not available.");
        }

        networkManager.LoadSceneSmart(scene);
        MPTestLogger.Log("automation_load_game", "begin", null, null, new Dictionary<string, object>
        {
            { "sceneTarget", scene }
        });
        return AutomationResponse.Ok("scene load requested", new { scene });
    }

    private AutomationResponse AssertState(JObject body)
    {
        int expectedPlayers = GetInt(body, "expectedPlayers", GetInt(body, "expected_players", -1));
        string expectedScene = GetString(body, "scene", null);
        string expectedState = GetString(body, "gameState", GetString(body, "game_state", null));
        var snapshot = MPTestStateSnapshot.Capture(_options.SafeRole, _options.CaseName, _options.Session);
        var assertion = MPTestAssertions.AssertBasic(snapshot, expectedPlayers, expectedScene, expectedState);
        if (!assertion.Success)
        {
            return AutomationResponse.Fail("assert_state_failed", "State assertion failed.", new { assertion, snapshot });
        }

        return AutomationResponse.Ok("state assertion passed", new { assertion, snapshot });
    }

    private AutomationResponse ExecuteCommand(JObject body)
    {
        string commandName = GetString(body, "name", "unknown");
        if (string.Equals(commandName, "reroll_shop", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(commandName, "RerollShop", StringComparison.OrdinalIgnoreCase))
        {
            return ExecuteRerollShopCommand(body, commandName);
        }

        if (string.Equals(commandName, "move_unit", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(commandName, "MoveUnit", StringComparison.OrdinalIgnoreCase))
        {
            return ExecuteMoveUnitCommand(body, commandName);
        }

        if (string.Equals(commandName, "place_wall", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(commandName, "PlaceWall", StringComparison.OrdinalIgnoreCase))
        {
            return ExecutePlaceWallCommand(body, commandName);
        }

        if (string.Equals(commandName, "remove_wall", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(commandName, "RemoveWall", StringComparison.OrdinalIgnoreCase))
        {
            return ExecuteRemoveWallCommand(body, commandName);
        }

        return AutomationResponse.Fail("unsupported_command", "Only reroll_shop, move_unit, place_wall, and remove_wall are currently supported by the runtime command harness.", new
        {
            command = commandName
        });
    }

    private AutomationResponse StartBot(JObject body)
    {
        if (!_options.Enabled)
        {
            return AutomationResponse.Fail("bot_requires_mptest", "HumanBot requires --mpTest.");
        }

        if (!_options.HumanBot)
        {
            return AutomationResponse.Fail("bot_requires_flag", "HumanBot requires --mpHumanBot.");
        }

        var driver = MPTestHumanBotDriver.Instance;
        if (driver == null)
        {
            driver = gameObject.GetComponent<MPTestHumanBotDriver>() ?? gameObject.AddComponent<MPTestHumanBotDriver>();
        }

        string persona = GetString(body, "persona", _options.BotPersona);
        int seed = GetInt(body, "seed", _options.BotSeed);
        int durationSeconds = GetInt(body, "durationSeconds", GetInt(body, "duration_seconds", _options.BotDurationSeconds));
        int stopAtRound = GetInt(body, "stopAtRound", GetInt(body, "stop_at_round", _options.BotStopAtRound));
        int maxCommands = GetInt(body, "maxCommands", GetInt(body, "max_commands", _options.BotMaxCommands));
        bool skipPrepare = GetBool(body, "skipPrepare", GetBool(body, "skip_prepare", _options.BotSkipPrepare));
        bool prepareAugmentOnly = GetBool(body, "prepareAugmentOnly", GetBool(body, "prepare_augment_only", _options.BotPrepareAugmentOnly));
        bool preferScrollAugment = GetBool(body, "preferScrollAugment", GetBool(body, "prefer_scroll_augment", _options.BotPreferScrollAugment));
        string journalPath = GetString(body, "journalPath", GetString(body, "journal_path", _options.BotRecordJournal));

        if (!driver.StartDriver(
            _options,
            out string reason,
            personaOverride: persona,
            seedOverride: seed,
            durationSecondsOverride: durationSeconds,
            stopAtRoundOverride: stopAtRound,
            maxCommandsOverride: maxCommands,
            skipPrepareOverride: skipPrepare,
            prepareAugmentOnlyOverride: prepareAugmentOnly,
            preferScrollAugmentOverride: preferScrollAugment,
            journalPathOverride: journalPath))
        {
            return AutomationResponse.Fail("bot_start_failed", reason, driver.Status);
        }

        return AutomationResponse.Ok("bot started", driver.Status);
    }

    private AutomationResponse StopBot(JObject body)
    {
        var driver = MPTestHumanBotDriver.Instance;
        if (driver == null)
        {
            return AutomationResponse.Ok("bot not present", new { enabled = false, running = false });
        }

        string reason = GetString(body, "reason", "automation_stop");
        driver.StopDriver(reason);
        return AutomationResponse.Ok("bot stopped", driver.Status);
    }

    private AutomationResponse BotStatus()
    {
        var driver = MPTestHumanBotDriver.Instance;
        if (driver == null)
        {
            return AutomationResponse.Ok("bot status", new
            {
                enabled = _options.Enabled && _options.HumanBot,
                running = false,
                persona = _options.BotPersona,
                commandsIssued = 0,
                lastDecision = (string)null,
                lastCommandType = (string)null,
                lastError = "driver_missing"
            });
        }

        return AutomationResponse.Ok("bot status", driver.Status);
    }

    private AutomationResponse BotJournal()
    {
        var driver = MPTestHumanBotDriver.Instance;
        if (driver == null)
        {
            return AutomationResponse.Fail("bot_missing", "HumanBot driver is not present.");
        }

        return AutomationResponse.Ok("bot journal", new
        {
            status = driver.Status,
            recent = driver.RecentJournal
        });
    }

    private AutomationResponse ExecuteRerollShopCommand(JObject body, string commandName)
    {
        int playerId = GetInt(body, "playerId", GetInt(body, "player_id", -1));
        if (playerId < 0)
        {
            return AutomationResponse.Fail("invalid_player_id", "playerId must be >= 0.", new { command = commandName, playerId });
        }

        var gameManagers = GameManagers.Instance;
        if (gameManagers == null || gameManagers.Runner == null || !gameManagers.Runner.IsRunning)
        {
            return AutomationResponse.Fail("game_managers_unavailable", "GameManagers runner is not available.", new { command = commandName, playerId });
        }

        if (!gameManagers.Runner.IsServer)
        {
            return AutomationResponse.Fail("command_requires_server_peer", "reroll_shop must be issued to the server/host peer.", new { command = commandName, playerId });
        }

        if (gameManagers.CommandProcessor == null)
        {
            return AutomationResponse.Fail("command_processor_missing", "GameManagers.CommandProcessor is not available.", new { command = commandName, playerId });
        }

        var player = gameManagers.GetPlayer(playerId);
        if (player == null || player.shopManager == null)
        {
            return AutomationResponse.Fail("player_or_shop_missing", "Target player or ShopManager is missing.", new { command = commandName, playerId });
        }

        if (!player.shopManager.IsDatabaseLoaded)
        {
            return AutomationResponse.Fail("shop_database_not_loaded", "Shop database is not loaded yet.", new { command = commandName, playerId });
        }

        int goldBefore = player.GetGold();
        int cost = player.shopManager.GetRerollCost();
        if (goldBefore < cost)
        {
            return AutomationResponse.Fail("insufficient_gold", "Target player does not have enough gold for reroll_shop.", new
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

        return AutomationResponse.Ok("command queued", new
        {
            command = "reroll_shop",
            playerId,
            goldBefore,
            cost
        });
    }

    private AutomationResponse ExecuteMoveUnitCommand(JObject body, string commandName)
    {
        int playerId = GetInt(body, "playerId", GetInt(body, "player_id", -1));
        if (playerId < 0)
        {
            return AutomationResponse.Fail("invalid_player_id", "playerId must be >= 0.", new { command = commandName, playerId });
        }

        var gameManagers = GameManagers.Instance;
        if (gameManagers == null || gameManagers.Runner == null || !gameManagers.Runner.IsRunning)
        {
            return AutomationResponse.Fail("game_managers_unavailable", "GameManagers runner is not available.", new { command = commandName, playerId });
        }

        if (!gameManagers.Runner.IsServer)
        {
            return AutomationResponse.Fail("command_requires_server_peer", "move_unit must be issued to the server/host peer.", new { command = commandName, playerId });
        }

        if (gameManagers.Object == null || !gameManagers.Object.HasStateAuthority)
        {
            return AutomationResponse.Fail("command_requires_state_authority", "move_unit requires GameManagers State Authority.", new { command = commandName, playerId });
        }

        if (gameManagers.GetGameState() != GameManagers.GameState.Prepare || gameManagers.IsSequenceTransitioning)
        {
            return AutomationResponse.Fail("command_requires_prepare_phase", "move_unit requires a stable Prepare phase.", new
            {
                command = commandName,
                playerId,
                state = gameManagers.GetGameState().ToString(),
                gameManagers.IsSequenceTransitioning
            });
        }

        if (gameManagers.CommandProcessor == null)
        {
            return AutomationResponse.Fail("command_processor_missing", "GameManagers.CommandProcessor is not available.", new { command = commandName, playerId });
        }

        var player = gameManagers.GetPlayer(playerId);
        if (player == null || player.fieldManager == null)
        {
            return AutomationResponse.Fail("player_or_field_missing", "Target player or FieldManager is missing.", new { command = commandName, playerId });
        }

        player.RebindRuntimeReferencesAfterMigration("MPTestAutomationServer.ExecuteMoveUnitCommand", false);
        var field = player.fieldManager;
        if (field == null)
        {
            return AutomationResponse.Fail("field_not_ready", "Target FieldManager is not ready.", new { command = commandName, playerId });
        }

        bool hasExplicitFrom = TryGetVector3Int(body, "from", out Vector3Int from)
            || TryGetVector3Int(body, "source", out from)
            || TryGetVector3IntByPrefix(body, "from", out from);
        bool hasExplicitTo = TryGetVector3Int(body, "to", out Vector3Int to)
            || TryGetVector3Int(body, "target", out to)
            || TryGetVector3IntByPrefix(body, "to", out to);

        string unitName = null;
        if (!hasExplicitFrom || !hasExplicitTo)
        {
            if (!TryFindMoveUnitPositions(field, out from, out to, out unitName, out string findReason))
            {
                return AutomationResponse.Fail("move_unit_target_not_found", "No legal move_unit target was found.", new
                {
                    command = commandName,
                    playerId,
                    reason = findReason
                });
            }
        }

        if (!ValidateMoveUnitTarget(field, from, to, out string validationReason, out Unit unit))
        {
            return AutomationResponse.Fail(validationReason, "move_unit target failed validation.", new
            {
                command = commandName,
                playerId,
                from,
                to
            });
        }

        unitName = unitName ?? (unit != null && unit.Data != null ? unit.Data.name : unit != null ? unit.name : "unknown");
        gameManagers.CommandProcessor.RequestCommandExecution(new MoveUnitCommand(playerId, from, to));
        MPTestLogger.Log("automation_command", "begin", "move_unit", null, new Dictionary<string, object>
        {
            { "playerId", playerId },
            { "from", from.ToString() },
            { "to", to.ToString() },
            { "unit", unitName }
        });

        return AutomationResponse.Ok("command queued", new
        {
            command = "move_unit",
            playerId,
            fromPosition = new { from.x, from.y, from.z },
            toPosition = new { to.x, to.y, to.z },
            unit = unitName
        });
    }

    private AutomationResponse ExecutePlaceWallCommand(JObject body, string commandName)
    {
        int playerId = GetInt(body, "playerId", GetInt(body, "player_id", -1));
        if (!TryGetPrepareCommandContext(commandName, playerId, out var gameManagers, out var player, out var field, out var failure))
        {
            return failure;
        }

        bool hasExplicitPosition = TryGetVector3Int(body, "position", out Vector3Int position)
            || TryGetVector3Int(body, "target", out position)
            || TryGetVector3IntByPrefix(body, "position", out position);
        if (!hasExplicitPosition &&
            !TryFindPlaceWallPosition(player, field, out position, out string findReason))
        {
            return AutomationResponse.Fail("place_wall_target_not_found", "No legal place_wall target was found.", new
            {
                command = commandName,
                playerId,
                reason = findReason
            });
        }

        if (!ValidatePlaceWallTarget(player, field, position, out string validationReason))
        {
            return AutomationResponse.Fail(validationReason, "place_wall target failed validation.", new
            {
                command = commandName,
                playerId,
                position = new { position.x, position.y, position.z }
            });
        }

        int wallCountBefore = player.GetWallCount();
        gameManagers.CommandProcessor.RequestCommandExecution(new PlaceWallCommand(playerId, position));
        MPTestLogger.Log("automation_command", "begin", "place_wall", null, new Dictionary<string, object>
        {
            { "playerId", playerId },
            { "position", position.ToString() },
            { "wallCountBefore", wallCountBefore }
        });

        return AutomationResponse.Ok("command queued", new
        {
            command = "place_wall",
            playerId,
            position = new { position.x, position.y, position.z },
            wallCountBefore
        });
    }

    private AutomationResponse ExecuteRemoveWallCommand(JObject body, string commandName)
    {
        int playerId = GetInt(body, "playerId", GetInt(body, "player_id", -1));
        if (!TryGetPrepareCommandContext(commandName, playerId, out var gameManagers, out var player, out var field, out var failure))
        {
            return failure;
        }

        bool hasExplicitPosition = TryGetVector3Int(body, "position", out Vector3Int position)
            || TryGetVector3Int(body, "target", out position)
            || TryGetVector3IntByPrefix(body, "position", out position);
        if (!hasExplicitPosition &&
            !TryFindRemoveWallPosition(field, out position, out string findReason))
        {
            return AutomationResponse.Fail("remove_wall_target_not_found", "No removable wall target was found.", new
            {
                command = commandName,
                playerId,
                reason = findReason
            });
        }

        if (!ValidateRemoveWallTarget(field, position, out string validationReason))
        {
            return AutomationResponse.Fail(validationReason, "remove_wall target failed validation.", new
            {
                command = commandName,
                playerId,
                position = new { position.x, position.y, position.z }
            });
        }

        int wallCountBefore = player.GetWallCount();
        gameManagers.CommandProcessor.RequestCommandExecution(new RemoveWallCommand(playerId, position));
        MPTestLogger.Log("automation_command", "begin", "remove_wall", null, new Dictionary<string, object>
        {
            { "playerId", playerId },
            { "position", position.ToString() },
            { "wallCountBefore", wallCountBefore }
        });

        return AutomationResponse.Ok("command queued", new
        {
            command = "remove_wall",
            playerId,
            position = new { position.x, position.y, position.z },
            wallCountBefore
        });
    }

    private static bool TryGetPrepareCommandContext(
        string commandName,
        int playerId,
        out GameManagers gameManagers,
        out PlayerManager player,
        out FieldManager field,
        out AutomationResponse failure)
    {
        gameManagers = GameManagers.Instance;
        player = null;
        field = null;
        failure = null;

        if (playerId < 0)
        {
            failure = AutomationResponse.Fail("invalid_player_id", "playerId must be >= 0.", new { command = commandName, playerId });
            return false;
        }

        if (gameManagers == null || gameManagers.Runner == null || !gameManagers.Runner.IsRunning)
        {
            failure = AutomationResponse.Fail("game_managers_unavailable", "GameManagers runner is not available.", new { command = commandName, playerId });
            return false;
        }

        if (!gameManagers.Runner.IsServer || gameManagers.Object == null || !gameManagers.Object.HasStateAuthority)
        {
            failure = AutomationResponse.Fail("command_requires_state_authority", "Command must be issued to the server/State Authority peer.", new { command = commandName, playerId });
            return false;
        }

        if (gameManagers.GetGameState() != GameManagers.GameState.Prepare || gameManagers.IsSequenceTransitioning)
        {
            failure = AutomationResponse.Fail("command_requires_prepare_phase", "Command requires a stable Prepare phase.", new
            {
                command = commandName,
                playerId,
                state = gameManagers.GetGameState().ToString(),
                gameManagers.IsSequenceTransitioning
            });
            return false;
        }

        if (gameManagers.CommandProcessor == null)
        {
            failure = AutomationResponse.Fail("command_processor_missing", "GameManagers.CommandProcessor is not available.", new { command = commandName, playerId });
            return false;
        }

        player = gameManagers.GetPlayer(playerId);
        if (player == null || player.fieldManager == null)
        {
            failure = AutomationResponse.Fail("player_or_field_missing", "Target player or FieldManager is missing.", new { command = commandName, playerId });
            return false;
        }

        player.RebindRuntimeReferencesAfterMigration($"MPTestAutomationServer.{commandName}", false);
        field = player.fieldManager;
        if (field == null)
        {
            failure = AutomationResponse.Fail("field_not_ready", "Target FieldManager is not ready.", new { command = commandName, playerId });
            return false;
        }

        return true;
    }

    private static bool TryFindPlaceWallPosition(PlayerManager player, FieldManager field, out Vector3Int position, out string reason)
    {
        position = default;
        reason = null;
        if (player == null || field == null)
        {
            reason = "player_or_field_missing";
            return false;
        }

        for (int y = 0; y < field.gridSize.y; y++)
        {
            for (int x = 0; x < field.gridSize.x; x++)
            {
                var candidate = new Vector3Int(x, y, 0);
                if (field.HasWallAt(candidate) || field.GetUnitAt(candidate) != null)
                {
                    continue;
                }

                if (ValidatePlaceWallTarget(player, field, candidate, out _))
                {
                    position = candidate;
                    return true;
                }
            }
        }

        reason = "no_empty_wall_cell";
        return false;
    }

    private static bool TryFindRemoveWallPosition(FieldManager field, out Vector3Int position, out string reason)
    {
        position = default;
        reason = null;
        if (field == null)
        {
            reason = "field_not_ready";
            return false;
        }

        for (int y = 0; y < field.gridSize.y; y++)
        {
            for (int x = 0; x < field.gridSize.x; x++)
            {
                var candidate = new Vector3Int(x, y, 0);
                if (field.GetWallAt(candidate) != null)
                {
                    position = candidate;
                    return true;
                }
            }
        }

        reason = "no_destructible_wall";
        return false;
    }

    private static bool ValidatePlaceWallTarget(PlayerManager player, FieldManager field, Vector3Int position, out string reason)
    {
        if (field == null || player == null)
        {
            reason = "field_not_ready";
            return false;
        }

        if (!field.IsValidGridPosition(position))
        {
            reason = "wall_position_out_of_range";
            return false;
        }

        if (field.HasWallAt(position))
        {
            reason = "wall_position_occupied";
            return false;
        }

        if (player.GetWallCount() <= 0)
        {
            reason = "insufficient_wall_stock";
            return false;
        }

        if (player.goalTransform != null && position == field.WorldToGridInt(player.goalTransform.position))
        {
            reason = "wall_goal_cell_blocked";
            return false;
        }

        reason = null;
        return true;
    }

    private static bool ValidateRemoveWallTarget(FieldManager field, Vector3Int position, out string reason)
    {
        if (field == null)
        {
            reason = "field_not_ready";
            return false;
        }

        if (!field.IsValidGridPosition(position))
        {
            reason = "remove_wall_position_out_of_range";
            return false;
        }

        if (field.GetWallAt(position) == null)
        {
            reason = "remove_wall_missing";
            return false;
        }

        reason = null;
        return true;
    }

    private static bool TryFindMoveUnitPositions(
        FieldManager field,
        out Vector3Int from,
        out Vector3Int to,
        out string unitName,
        out string reason)
    {
        from = default;
        to = default;
        unitName = null;
        reason = null;

        var units = field.GetAlliedUnitsOnField();
        if (units == null || units.Count == 0)
        {
            reason = "no_units_on_field";
            return false;
        }

        foreach (var unit in units
                     .Where(candidate => candidate != null)
                     .OrderBy(candidate => candidate.Data != null ? candidate.Data.name : candidate.name)
                     .ThenBy(candidate => candidate.starLevel))
        {
            var source = field.GetUnitPosition(unit);
            if (!source.HasValue || unit.Data == null)
            {
                continue;
            }

            var candidates = field.GetValidPlacementTiles(unit.Data.unitType);
            if (candidates == null)
            {
                continue;
            }

            foreach (var candidate in candidates
                         .Where(position => position != source.Value)
                         .OrderBy(position => position.x)
                         .ThenBy(position => position.y)
                         .ThenBy(position => position.z))
            {
                if (ValidateMoveUnitTarget(field, source.Value, candidate, out _, out _))
                {
                    from = source.Value;
                    to = candidate;
                    unitName = unit.Data != null ? unit.Data.name : unit.name;
                    return true;
                }
            }
        }

        reason = "no_legal_destination";
        return false;
    }

    private static bool ValidateMoveUnitTarget(FieldManager field, Vector3Int from, Vector3Int to, out string reason, out Unit unit)
    {
        unit = null;
        if (field == null)
        {
            reason = "field_not_ready";
            return false;
        }

        if (!field.IsValidGridPosition(from) || !field.IsValidGridPosition(to) || from == to)
        {
            reason = "move_position_invalid";
            return false;
        }

        unit = field.GetUnitAt(from);
        if (unit == null)
        {
            reason = "move_source_empty";
            return false;
        }

        if (field.IsUnitAt(to))
        {
            reason = "move_destination_occupied";
            return false;
        }

        if (unit.Data == null && field.HasWallAt(to))
        {
            reason = "move_pending_unit_type_unknown_for_wall";
            return false;
        }

        if (unit.Data != null && unit.Data.unitType == UnitType.Melee && field.HasWallAt(to))
        {
            reason = "melee_unit_cannot_move_to_wall";
            return false;
        }

        reason = null;
        return true;
    }

    private AutomationResponse CaptureScreenshot(HttpListenerRequest request)
    {
        string path = request.QueryString["path"];
        if (string.IsNullOrWhiteSpace(path))
        {
            string root = string.IsNullOrWhiteSpace(_options.ArtifactDir)
                ? Path.Combine(Application.persistentDataPath, "mp-test")
                : _options.ArtifactDir;
            path = Path.Combine(root, "screenshots", $"{_options.SafeRole}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.png");
        }

        string fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
        ScreenCapture.CaptureScreenshot(fullPath);
        MPTestLogger.Log("automation_screenshot", "begin", null, null, new Dictionary<string, object>
        {
            { "path", fullPath }
        });
        return AutomationResponse.Ok("screenshot requested", new { path = fullPath });
    }

    private AutomationResponse SetFreezeGameFlow(JObject body)
    {
        bool enabled = GetBool(body, "enabled", true);
        string reason = GetString(body, "reason", "automation");
        var options = MPTestCommandLine.SetFreezeGameFlowForRuntime(enabled);
        _options = options;

        MPTestLogger.Log("automation_freeze_game_flow", enabled ? "begin" : "complete", null, reason, new Dictionary<string, object>
        {
            { "enabled", options.FreezeGameFlow },
            { "reason", reason }
        });

        return AutomationResponse.Ok("freeze game flow updated", new
        {
            enabled = options.FreezeGameFlow,
            reason
        });
    }

    private bool IsAuthorized(HttpListenerRequest request)
    {
        string token = request.Headers["X-MPTest-Token"];
        if (string.IsNullOrEmpty(token))
        {
            string authorization = request.Headers["Authorization"];
            const string bearer = "Bearer ";
            if (!string.IsNullOrEmpty(authorization) && authorization.StartsWith(bearer, StringComparison.OrdinalIgnoreCase))
            {
                token = authorization.Substring(bearer.Length).Trim();
            }
        }

        if (string.IsNullOrEmpty(token))
        {
            token = request.QueryString["token"];
        }

        return !string.IsNullOrEmpty(token) && string.Equals(token, _automationToken, StringComparison.Ordinal);
    }

    private static async Task<JObject> ReadBody(HttpListenerRequest request)
    {
        if (!request.HasEntityBody)
        {
            return new JObject();
        }

        using (var reader = new StreamReader(request.InputStream, request.ContentEncoding ?? Encoding.UTF8))
        {
            string text = await reader.ReadToEndAsync();
            if (string.IsNullOrWhiteSpace(text))
            {
                return new JObject();
            }

            return JObject.Parse(text);
        }
    }

    private static async Task WriteJson(HttpListenerResponse httpResponse, AutomationResponse response, int statusCode)
    {
        string json = JsonConvert.SerializeObject(response, Formatting.None);
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        httpResponse.StatusCode = statusCode;
        httpResponse.ContentType = "application/json";
        httpResponse.ContentEncoding = Encoding.UTF8;
        httpResponse.ContentLength64 = bytes.Length;
        await httpResponse.OutputStream.WriteAsync(bytes, 0, bytes.Length);
        httpResponse.Close();
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

    private static bool TryGetVector3Int(JObject body, string key, out Vector3Int value)
    {
        value = default;
        if (body == null || !body.TryGetValue(key, StringComparison.OrdinalIgnoreCase, out JToken token))
        {
            return false;
        }

        if (token is JArray array && array.Count >= 2)
        {
            int x = array[0].Value<int>();
            int y = array[1].Value<int>();
            int z = array.Count >= 3 ? array[2].Value<int>() : 0;
            value = new Vector3Int(x, y, z);
            return true;
        }

        if (token is JObject obj)
        {
            int x = GetInt(obj, "x", int.MinValue);
            int y = GetInt(obj, "y", int.MinValue);
            int z = GetInt(obj, "z", 0);
            if (x != int.MinValue && y != int.MinValue)
            {
                value = new Vector3Int(x, y, z);
                return true;
            }
        }

        return false;
    }

    private static bool TryGetVector3IntByPrefix(JObject body, string prefix, out Vector3Int value)
    {
        value = default;
        int x = GetInt(body, prefix + "X", int.MinValue);
        int y = GetInt(body, prefix + "Y", int.MinValue);
        int z = GetInt(body, prefix + "Z", 0);
        if (x == int.MinValue || y == int.MinValue)
        {
            return false;
        }

        value = new Vector3Int(x, y, z);
        return true;
    }

    private static int GetInt(JObject body, string key, int fallback)
    {
        if (body != null && body.TryGetValue(key, StringComparison.OrdinalIgnoreCase, out JToken token) && int.TryParse(token.ToString(), out int value))
        {
            return value;
        }

        return fallback;
    }

    private static bool GetBool(JObject body, string key, bool fallback)
    {
        if (body == null || !body.TryGetValue(key, StringComparison.OrdinalIgnoreCase, out JToken token))
        {
            return fallback;
        }

        if (token.Type == JTokenType.Boolean)
        {
            return token.Value<bool>();
        }

        string value = token.ToString();
        if (bool.TryParse(value, out bool parsed))
        {
            return parsed;
        }

        if (int.TryParse(value, out int intValue))
        {
            return intValue != 0;
        }

        return fallback;
    }

    private static void SetMaxPlayers(NetworkManager networkManager, int maxPlayers)
    {
        var field = typeof(NetworkManager).GetField("maxSessionPlayers", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        if (field != null)
        {
            field.SetValue(networkManager, Mathf.Clamp(maxPlayers, 2, 4));
        }
    }

    public sealed class AutomationResponse
    {
        [JsonProperty("success")] public bool Success;
        [JsonProperty("message")] public string Message;
        [JsonProperty("timestampUtc")] public string TimestampUtc;
        [JsonProperty("data", NullValueHandling = NullValueHandling.Ignore)] public object Data;
        [JsonProperty("error", NullValueHandling = NullValueHandling.Ignore)] public ErrorBody Error;

        public static AutomationResponse Ok(string message, object data = null)
        {
            return new AutomationResponse
            {
                Success = true,
                Message = message,
                TimestampUtc = DateTime.UtcNow.ToString("o"),
                Data = data
            };
        }

        public static AutomationResponse Fail(string code, string message, object details = null)
        {
            return new AutomationResponse
            {
                Success = false,
                Message = message,
                TimestampUtc = DateTime.UtcNow.ToString("o"),
                Error = new ErrorBody
                {
                    Code = code,
                    Details = details
                }
            };
        }
    }

    public sealed class ErrorBody
    {
        [JsonProperty("code")] public string Code;
        [JsonProperty("details", NullValueHandling = NullValueHandling.Ignore)] public object Details;
    }
}
#endif
