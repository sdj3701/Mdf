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
    private bool _quitStarted;

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

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);
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

        var runner = FindActiveRunner();
        if (runner != null && runner.IsRunning)
        {
            yield return ShutdownRunner(runner, reason);
        }
        else
        {
            MPTestLogger.Log("runner_shutdown_complete", "info", "no_active_runner", reason);
        }

        if (stopAutomationServer != null)
        {
            MPTestLogger.Log("automation_server_stop", "begin", reason);
            stopAutomationServer();
            MPTestLogger.Log("automation_server_stop", "complete", reason);
        }
        else
        {
            MPTestLogger.Log("automation_server_stop", "info", "not_present", reason);
        }

        MPTestLogger.Log("application_quit_called", "begin", reason, null, new Dictionary<string, object>
        {
            { "exitCode", exitCode }
        });
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

    private static IEnumerator ShutdownRunner(NetworkRunner runner, string reason)
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

        float deadline = Time.realtimeSinceStartup + RunnerShutdownTimeoutSeconds;
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

        MPTestLogger.Log("runner_shutdown_complete", "pass", reason, null, RunnerFields(runner));
    }

    private static NetworkRunner FindActiveRunner()
    {
        var networkManager = NetworkManager.Instance;
        if (networkManager != null && networkManager._runner != null && networkManager._runner.IsRunning)
        {
            return networkManager._runner;
        }

        var runners = FindObjectsOfType<NetworkRunner>(true);
        foreach (var runner in runners)
        {
            if (runner != null && runner.IsRunning)
            {
                return runner;
            }
        }

        return null;
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
