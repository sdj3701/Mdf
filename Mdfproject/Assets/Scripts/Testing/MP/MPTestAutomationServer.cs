#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.IO;
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
                    tokenHash = _options.AutomationTokenHash
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

        if (path == "/quit")
        {
            return await RequireMethod(request, "POST", () => MainThread(() =>
            {
                MPTestLogger.Log("automation_quit", "begin");
                Invoke(nameof(QuitAfterResponse), 0.25f);
                return AutomationResponse.Ok("quit requested", new { scene = SceneManager.GetActiveScene().name });
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

        return AutomationResponse.Fail("unsupported_command", "Only reroll_shop is currently supported by the runtime command harness.", new
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
        string journalPath = GetString(body, "journalPath", GetString(body, "journal_path", _options.BotRecordJournal));

        if (!driver.StartDriver(
            _options,
            out string reason,
            personaOverride: persona,
            seedOverride: seed,
            durationSecondsOverride: durationSeconds,
            stopAtRoundOverride: stopAtRound,
            maxCommandsOverride: maxCommands,
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

    private void QuitAfterResponse()
    {
        StopServer();
        Application.Quit(0);
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

    private static int GetInt(JObject body, string key, int fallback)
    {
        if (body != null && body.TryGetValue(key, StringComparison.OrdinalIgnoreCase, out JToken token) && int.TryParse(token.ToString(), out int value))
        {
            return value;
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
