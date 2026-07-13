#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Fusion;
using UnityEngine;

public sealed class MPTestGracefulQuit : MonoBehaviour
{
    private const float RunnerShutdownTimeoutSeconds = 8f;

    private static MPTestGracefulQuit _instance;
    private MPTestCommandLine.Options _options;
    private Action _beginAutomationShutdown;
    private Action _stopAutomationServer;
    private bool _quitStarted;
    private bool _allowImmediateQuit;
    private bool _quitHookRegistered;

    public static MPTestGracefulQuit Ensure()
    {
        if (_instance != null)
        {
            return _instance;
        }

        var go = new GameObject("MPTestGracefulQuit");
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<MPTestGracefulQuit>();
        return _instance;
    }

    public static bool RequestQuit(
        MPTestCommandLine.Options options,
        string reason,
        int exitCode = 0,
        Action stopAutomationServer = null,
        float delaySeconds = 0f)
    {
        if (!options.Enabled)
        {
            return false;
        }

        return Ensure().BeginQuit(options, reason, exitCode, stopAutomationServer, delaySeconds);
    }

    public static void Configure(
        MPTestCommandLine.Options options,
        Action beginAutomationShutdown = null,
        Action stopAutomationServer = null)
    {
        if (!options.Enabled)
        {
            return;
        }

        MPTestGracefulQuit instance = Ensure();
        instance._options = options;
        if (beginAutomationShutdown != null)
        {
            instance._beginAutomationShutdown = beginAutomationShutdown;
        }

        if (stopAutomationServer != null)
        {
            instance._stopAutomationServer = stopAutomationServer;
        }
    }

    public static bool ShouldInterceptWindowClose(
        bool mpTestEnabled,
        bool isEditor,
        bool allowImmediateQuit)
    {
        return mpTestEnabled && !isEditor && !allowImmediateQuit;
    }

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);
        RegisterQuitHook();
    }

    private void OnDestroy()
    {
        if (_instance != this)
        {
            return;
        }

        UnregisterQuitHook();
        _instance = null;
    }

    private void RegisterQuitHook()
    {
        if (_quitHookRegistered)
        {
            return;
        }

        Application.wantsToQuit += HandleWantsToQuit;
        _quitHookRegistered = true;
    }

    private void UnregisterQuitHook()
    {
        if (!_quitHookRegistered)
        {
            return;
        }

        Application.wantsToQuit -= HandleWantsToQuit;
        _quitHookRegistered = false;
    }

    private bool HandleWantsToQuit()
    {
        MPTestCommandLine.Options options = _options.Enabled
            ? _options
            : MPTestCommandLine.GetOptions();
        if (!ShouldInterceptWindowClose(options.Enabled, Application.isEditor, _allowImmediateQuit))
        {
            return true;
        }

        if (!_quitStarted)
        {
            MPTestLogger.Log("window_close_intercepted", "begin", "os_window_close");
            BeginQuit(options, "os_window_close", 0, _stopAutomationServer, 0f);
        }

        return false;
    }

    private bool BeginQuit(
        MPTestCommandLine.Options options,
        string reason,
        int exitCode,
        Action stopAutomationServer,
        float delaySeconds)
    {
        if (_quitStarted)
        {
            MPTestLogger.Log("quit_requested", "info", "already_running", reason, new Dictionary<string, object>
            {
                { "exitCode", exitCode }
            });
            return false;
        }

        _quitStarted = true;
        _options = options;
        if (stopAutomationServer != null)
        {
            _stopAutomationServer = stopAutomationServer;
        }

        try
        {
            _beginAutomationShutdown?.Invoke();
        }
        catch (Exception ex)
        {
            MPTestLogger.Log("automation_server_stop_accepting", "fail", ex.GetType().Name, ex.Message);
        }

        MPTestLogger.Log("quit_requested", "begin", reason, null, new Dictionary<string, object>
        {
            { "exitCode", exitCode },
            { "delaySeconds", delaySeconds }
        });
        StartCoroutine(QuitCoroutine(options, reason, exitCode, stopAutomationServer, delaySeconds));
        return true;
    }

    private IEnumerator QuitCoroutine(
        MPTestCommandLine.Options options,
        string reason,
        int exitCode,
        Action stopAutomationServer,
        float delaySeconds)
    {
        if (delaySeconds > 0f)
        {
            yield return new WaitForSecondsRealtime(delaySeconds);
        }

        StopHumanBot(reason);
        FreezeGameFlow(reason);

        List<NetworkRunner> runners = FindActiveRunners();
        if (runners.Count > 0)
        {
            float deadline = Time.realtimeSinceStartup + RunnerShutdownTimeoutSeconds;
            for (int i = 0; i < runners.Count; i++)
            {
                NetworkRunner runner = runners[i];
                if (runner == null || !runner.IsRunning)
                {
                    continue;
                }

                if (Time.realtimeSinceStartup >= deadline)
                {
                    MPTestLogger.Log("runner_shutdown_timeout", "fail", "total_timeout", reason, RunnerFields(runner));
                    break;
                }

                yield return ShutdownRunner(runner, reason, deadline);
            }
        }
        else
        {
            MPTestLogger.Log("runner_shutdown_complete", "info", "no_active_runner", reason);
        }

        Action stopServer = stopAutomationServer ?? _stopAutomationServer;
        if (stopServer != null)
        {
            MPTestLogger.Log("automation_server_stop", "begin", reason);
            try
            {
                stopServer();
                MPTestLogger.Log("automation_server_stop", "complete", reason);
            }
            catch (Exception ex)
            {
                MPTestLogger.Log("automation_server_stop", "fail", ex.GetType().Name, ex.Message);
            }
        }
        else
        {
            MPTestLogger.Log("automation_server_stop", "info", "not_present", reason);
        }

        MPTestLogger.Log("application_quit_called", "begin", reason, null, new Dictionary<string, object>
        {
            { "exitCode", exitCode }
        });
        _allowImmediateQuit = true;
        Application.Quit(exitCode);
    }

    private static void StopHumanBot(string reason)
    {
        MPTestLogger.Log("human_bot_stop_requested", "begin", reason);
        var driver = MPTestHumanBotDriver.Instance;
        if (driver != null)
        {
            driver.StopDriver($"graceful_quit:{reason}");
        }
    }

    private static void FreezeGameFlow(string reason)
    {
        var options = MPTestCommandLine.SetFreezeGameFlowForRuntime(true);
        MPTestLogger.Log("automation_freeze_game_flow", "begin", "graceful_quit", reason, new Dictionary<string, object>
        {
            { "enabled", options.FreezeGameFlow }
        });
    }

    private static IEnumerator ShutdownRunner(NetworkRunner runner, string reason, float deadline)
    {
        MPTestLogger.Log("runner_shutdown_begin", "begin", reason, null, RunnerFields(runner));
        Task shutdownTask = null;
        try
        {
            shutdownTask = runner.Shutdown();
        }
        catch (Exception ex)
        {
            MPTestLogger.Log("runner_shutdown_complete", "fail", ex.GetType().Name, ex.Message, RunnerFields(runner));
            yield break;
        }

        while (shutdownTask != null && !shutdownTask.IsCompleted && Time.realtimeSinceStartup < deadline)
        {
            yield return null;
        }

        if (shutdownTask != null && !shutdownTask.IsCompleted)
        {
            MPTestLogger.Log("runner_shutdown_timeout", "fail", "timeout", reason, RunnerFields(runner));
            yield break;
        }

        if (shutdownTask != null && shutdownTask.IsFaulted)
        {
            string message = shutdownTask.Exception != null
                ? shutdownTask.Exception.GetBaseException().Message
                : "unknown";
            MPTestLogger.Log("runner_shutdown_complete", "fail", "task_faulted", message, RunnerFields(runner));
            yield break;
        }

        if (shutdownTask != null && shutdownTask.IsCanceled)
        {
            MPTestLogger.Log("runner_shutdown_complete", "fail", "task_canceled", reason, RunnerFields(runner));
            yield break;
        }

        MPTestLogger.Log("runner_shutdown_complete", "pass", reason, null, RunnerFields(runner));
    }

    private static List<NetworkRunner> FindActiveRunners()
    {
        var active = new List<NetworkRunner>();
        var networkManager = NetworkManager.Instance;
        if (networkManager != null && networkManager._runner != null && networkManager._runner.IsRunning)
        {
            active.Add(networkManager._runner);
        }

        var runners = FindObjectsOfType<NetworkRunner>(true);
        foreach (var runner in runners)
        {
            if (runner != null && runner.IsRunning && !active.Contains(runner))
            {
                active.Add(runner);
            }
        }

        return active;
    }

    private static Dictionary<string, object> RunnerFields(NetworkRunner runner)
    {
        var fields = new Dictionary<string, object>();
        if (runner == null)
        {
            fields["runner"] = "null";
            return fields;
        }

        fields["runner"] = runner.name;
        fields["isRunning"] = runner.IsRunning;
        fields["mode"] = runner.GameMode.ToString();
        fields["isServer"] = runner.IsServer;
        return fields;
    }
}
#endif
