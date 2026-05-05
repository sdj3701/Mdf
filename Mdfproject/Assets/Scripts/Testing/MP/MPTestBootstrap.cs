using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Fusion;
using GameCore.Enums;
using UnityEngine;

public sealed class MPTestBootstrap : MonoBehaviour
{
    private MPTestCommandLine.Options _options;
    private bool _started;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        var options = MPTestCommandLine.GetOptions();
        if (!options.Enabled)
        {
            return;
        }

        var existing = FindObjectOfType<MPTestBootstrap>();
        if (existing != null)
        {
            return;
        }

        var go = new GameObject("MPTestBootstrap");
        DontDestroyOnLoad(go);
        go.AddComponent<MPTestBootstrap>();
    }

    private void Awake()
    {
        _options = MPTestCommandLine.GetOptions();
        Application.runInBackground = true;
        UnityEngine.Random.InitState(_options.Seed);

        if (!string.IsNullOrEmpty(_options.ConnectionToken))
        {
            PlayerPrefs.SetString("PlayerUUID", _options.ConnectionToken);
            PlayerPrefs.Save();
        }

        MPTestLogger.Log("bootstrap", "begin", null, null, new Dictionary<string, object>
        {
            { "autoStart", _options.AutoStart },
            { "loadGame", _options.LoadGame },
            { "maxPlayers", _options.MaxPlayers },
            { "seed", _options.Seed },
            { "connectionTokenHash", _options.ConnectionTokenHash },
            { "automationTokenHash", _options.AutomationTokenHash }
        });

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        MPTestMainThreadDispatcher.Ensure();
        if (MPTestAutomationServer.CanStart(_options, out _))
        {
            gameObject.AddComponent<MPTestAutomationServer>().StartServer(_options);
        }

        if (_options.HumanBot)
        {
            var driver = gameObject.GetComponent<MPTestHumanBotDriver>() ?? gameObject.AddComponent<MPTestHumanBotDriver>();
            if (!driver.StartDriver(_options, out string reason))
            {
                MPTestLogger.Fail("human_bot", "start_failed", reason);
            }
        }
#endif
    }

    private void Start()
    {
        if (_options.ExitAfterSeconds > 0)
        {
            StartCoroutine(ExitAfterSeconds(_options.ExitAfterSeconds));
        }

        if (_options.AutoStart && !_started)
        {
            _started = true;
            StartCoroutine(AutoStartCoroutine());
        }
    }

    private IEnumerator AutoStartCoroutine()
    {
        MPTestLogger.Log("autostart", "begin");

        yield return WaitForNetworkManager(20f);
        var networkManager = NetworkManager.Instance;
        if (networkManager == null)
        {
            MPTestLogger.Fail("autostart", "network_manager_missing", "NetworkManager.Instance was not found.");
            yield break;
        }

        SetMaxPlayers(networkManager, _options.MaxPlayers);
        networkManager.SetRoomNameInput(_options.Session);

        if (networkManager._runner == null)
        {
            networkManager.JoinLobby();
            MPTestLogger.Log("join_lobby", "begin");
            yield return WaitUntil(() => NetworkManager.Instance != null && NetworkManager.Instance.State == ConnectionState.InLobby, 20f);
        }

        if (networkManager.State != ConnectionState.InLobby)
        {
            MPTestLogger.Fail("join_lobby", "lobby_timeout", $"state={networkManager.State}");
            yield break;
        }

        var role = ParseRole(_options.Role);
        string scene = _options.LoadGame ? _options.Scene : string.IsNullOrEmpty(_options.Scene) ? "Game" : _options.Scene;
        MPTestLogger.Log("start_game", "begin", null, null, new Dictionary<string, object>
        {
            { "mode", role },
            { "sceneTarget", scene }
        });

        networkManager.StartGame(role, _options.Session, scene);
        yield return WaitUntil(() =>
        {
            var current = NetworkManager.Instance;
            return current != null && current._runner != null && current._runner.IsRunning;
        }, 30f);

        var runner = NetworkManager.Instance != null ? NetworkManager.Instance._runner : null;
        if (runner == null || !runner.IsRunning)
        {
            MPTestLogger.Fail("start_game", "runner_timeout", "NetworkRunner did not start.");
            yield break;
        }

        MPTestLogger.Pass("start_game", "NetworkRunner started.");
    }

    private IEnumerator WaitForNetworkManager(float timeoutSeconds)
    {
        yield return WaitUntil(() => NetworkManager.Instance != null, timeoutSeconds);
    }

    private IEnumerator WaitUntil(Func<bool> predicate, float timeoutSeconds)
    {
        float deadline = Time.realtimeSinceStartup + timeoutSeconds;
        while (Time.realtimeSinceStartup < deadline)
        {
            if (predicate())
            {
                yield break;
            }

            yield return null;
        }
    }

    private IEnumerator ExitAfterSeconds(int seconds)
    {
        yield return new WaitForSecondsRealtime(seconds);
        MPTestLogger.Log("exit_after_seconds", "begin", null, null, new Dictionary<string, object>
        {
            { "seconds", seconds }
        });

        Application.Quit(0);
    }

    private static GameMode ParseRole(string role)
    {
        if (string.Equals(role, "client", StringComparison.OrdinalIgnoreCase))
        {
            return GameMode.Client;
        }

        return GameMode.Host;
    }

    private static void SetMaxPlayers(NetworkManager networkManager, int maxPlayers)
    {
        var field = typeof(NetworkManager).GetField("maxSessionPlayers", BindingFlags.Instance | BindingFlags.NonPublic);
        if (field != null)
        {
            field.SetValue(networkManager, Mathf.Clamp(maxPlayers, 2, 4));
        }
    }
}
