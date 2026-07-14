using System;
using System.Collections.Generic;
using System.Text;
using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class MPTestLogger
{
    private const int RecentLimit = 300;
    private static readonly Queue<string> RecentLines = new Queue<string>();

    public static IReadOnlyCollection<string> Recent => RecentLines.ToArray();
#if UNITY_EDITOR
    public static bool EditorTestLoggingEnabled { get; set; }
#endif

    public static bool IsEnabled
    {
        get
        {
#if !(UNITY_EDITOR || DEVELOPMENT_BUILD)
            return false;
#else
            bool commandLineEnabled = MPTestCommandLine.GetOptions().Enabled;
#if UNITY_EDITOR
            return commandLineEnabled || EditorTestLoggingEnabled;
#else
            return commandLineEnabled;
#endif
#endif
        }
    }

    public static void Log(string phase, string result = "info", string code = null, string message = null, IDictionary<string, object> fields = null)
    {
#if !(UNITY_EDITOR || DEVELOPMENT_BUILD)
        return;
#else
        var options = MPTestCommandLine.GetOptions();
#if UNITY_EDITOR
        if (!options.Enabled && !EditorTestLoggingEnabled)
        {
            return;
        }
#else
        if (!options.Enabled)
        {
            return;
        }
#endif

        var builder = new StringBuilder();
        builder.Append("[MPTEST]");
        Append(builder, "ts", DateTime.UtcNow.ToString("o"));
        Append(builder, "case", options.CaseName);
        Append(builder, "role", options.SafeRole);
        Append(builder, "session", options.Session);
        Append(builder, "scenario", options.Scenario);
        Append(builder, "phase", phase);
        Append(builder, "scene", SceneManager.GetActiveScene().name);

        var runner = NetworkManager.Instance != null ? NetworkManager.Instance._runner : null;
        if (runner != null)
        {
            Append(builder, "mode", runner.GameMode.ToString());
            Append(builder, "tick", runner.Tick.ToString());
            Append(builder, "players", CountPlayers(runner).ToString());
        }

        if (!string.IsNullOrEmpty(code))
        {
            Append(builder, "code", code);
        }

        Append(builder, "result", result);

        if (fields != null)
        {
            foreach (var pair in fields)
            {
                Append(builder, pair.Key, pair.Value != null ? pair.Value.ToString() : "null");
            }
        }

        if (!string.IsNullOrEmpty(message))
        {
            Append(builder, "msg", message);
        }

        string line = builder.ToString();
        RecentLines.Enqueue(line);
        while (RecentLines.Count > RecentLimit)
        {
            RecentLines.Dequeue();
        }

        Debug.Log(line);
#endif
    }

    public static void Pass(string phase, string message = null, IDictionary<string, object> fields = null)
    {
        Log(phase, "pass", null, message, fields);
    }

    public static void Fail(string phase, string code, string message = null, IDictionary<string, object> fields = null)
    {
        Log(phase, "fail", code, message, fields);
    }

    public static string HashForLog(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "empty";
        }

        unchecked
        {
            uint hash = 2166136261;
            for (int i = 0; i < value.Length; i++)
            {
                hash ^= value[i];
                hash *= 16777619;
            }

            return hash.ToString("X8");
        }
    }

    private static void Append(StringBuilder builder, string key, string value)
    {
        builder.Append(' ');
        builder.Append(SanitizeKey(key));
        builder.Append('=');
        builder.Append(QuoteIfNeeded(SanitizeValue(value)));
    }

    private static string SanitizeKey(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return "unknown";
        }

        return key.Replace(" ", "_");
    }

    private static string SanitizeValue(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "empty";
        }

        return value.Replace("\r", " ").Replace("\n", " ").Replace("\"", "'");
    }

    private static string QuoteIfNeeded(string value)
    {
        return value.IndexOf(' ') >= 0 ? "\"" + value + "\"" : value;
    }

    private static int CountPlayers(NetworkRunner runner)
    {
        int count = 0;
        foreach (var ignored in runner.ActivePlayers)
        {
            count++;
        }

        return count;
    }
}
