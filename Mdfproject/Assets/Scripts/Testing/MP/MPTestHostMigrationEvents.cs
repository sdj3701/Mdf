using System;
using System.Collections.Generic;
using Fusion;

public static class MPTestHostMigrationEvents
{
    public static string LastEvent { get; private set; } = "none";
    public static int EventCount { get; private set; }
    public static int OnHostMigrationCount { get; private set; }
    public static int NonNullTokenCount { get; private set; }
    public static int ResumeCount { get; private set; }
    public static int StartGameSuccessCount { get; private set; }
    public static int CompleteCount { get; private set; }
    public static int FailureCount { get; private set; }

    public static void Record(string eventName, NetworkRunner runner = null, HostMigrationToken token = null, IDictionary<string, object> fields = null)
    {
        string safeEvent = string.IsNullOrWhiteSpace(eventName) ? "unknown" : eventName;
        LastEvent = safeEvent;
        EventCount++;

        if (safeEvent.IndexOf("on_host_migration", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            OnHostMigrationCount++;
        }

        if (token != null)
        {
            NonNullTokenCount++;
        }

        if (safeEvent.IndexOf("resume", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            ResumeCount++;
        }

        if (safeEvent.IndexOf("start_game_success", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            StartGameSuccessCount++;
        }

        if (safeEvent.IndexOf("complete", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            CompleteCount++;
        }

        bool failed = safeEvent.IndexOf("fail", StringComparison.OrdinalIgnoreCase) >= 0 ||
                      safeEvent.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0 ||
                      safeEvent.IndexOf("abort", StringComparison.OrdinalIgnoreCase) >= 0;
        if (failed)
        {
            FailureCount++;
        }

        var payload = fields != null
            ? new Dictionary<string, object>(fields)
            : new Dictionary<string, object>();
        payload["event"] = safeEvent;
        payload["eventCount"] = EventCount;
        payload["tokenPresent"] = token != null;
        payload["runnerName"] = runner != null ? runner.name : "null";
        payload["runnerRunning"] = runner != null && runner.IsRunning;
        payload["runnerMode"] = runner != null ? runner.GameMode.ToString() : "null";
        payload["runnerIsServer"] = runner != null && runner.IsServer;
        payload["tokenMode"] = token != null ? token.GameMode.ToString() : "null";

        MPTestLogger.Log("host_migration", failed ? "fail" : "info", safeEvent, null, payload);
    }
}
