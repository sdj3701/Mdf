#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

public sealed class MPTestBotJournal
{
    private const int RecentLimit = 100;
    private readonly Queue<object> _recent = new Queue<object>();

    public string Path { get; private set; }

    public MPTestBotJournal(string path)
    {
        Path = string.IsNullOrWhiteSpace(path) ? null : System.IO.Path.GetFullPath(path);
        if (!string.IsNullOrEmpty(Path))
        {
            string directory = System.IO.Path.GetDirectoryName(Path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }
    }

    public object[] Recent => _recent.ToArray();

    public void Record(object entry)
    {
        if (entry == null)
        {
            return;
        }

        _recent.Enqueue(entry);
        while (_recent.Count > RecentLimit)
        {
            _recent.Dequeue();
        }

        if (string.IsNullOrEmpty(Path))
        {
            return;
        }

        string json = JsonConvert.SerializeObject(entry, Formatting.None);
        File.AppendAllText(Path, json + Environment.NewLine);
    }

#pragma warning disable CS0618
    public static object BuildDecisionEntry(MPTestHumanBotDriver.BotStatus status, MPTestHumanBotPolicy.Decision decision)
    {
        return new
        {
            kind = "bot_decision",
            ts = DateTime.UtcNow.ToString("o"),
            seq = status != null ? status.CommandsIssued + 1 : 0,
            playerId = decision.PlayerId,
            persona = status != null ? status.Persona : "unknown",
            gameState = decision.GameState,
            round = decision.Round,
            observed = decision.Observed,
            decision = new
            {
                commandType = decision.CommandType,
                reason = decision.Reason,
                target = decision.Target
            }
        };
    }
#pragma warning restore CS0618

    public static object BuildDecisionEntry(MPTestHumanBotDriver.BotStatus status, MdfDecision decision)
    {
        return new
        {
            kind = "bot_decision",
            ts = DateTime.UtcNow.ToString("o"),
            seq = status != null ? status.CommandsIssued + 1 : 0,
            playerId = decision != null ? decision.PlayerId : -1,
            persona = status != null ? status.Persona : "unknown",
            gameState = decision != null ? decision.GameState : "unknown",
            round = decision != null ? decision.Round : 0,
            observed = decision != null ? decision.Observed : null,
            decision = new
            {
                commandType = decision != null ? decision.CommandTypeName : "Observe",
                reason = decision != null ? decision.Reason : "no_decision",
                target = decision != null ? decision.Target : null,
                score = decision != null ? decision.Score : 0f,
                kind = decision != null ? decision.Kind.ToString() : "Observe",
                fields = decision != null ? decision.JournalFields : null
            }
        };
    }

    public static object BuildStatusEntry(string kind, MPTestHumanBotDriver.BotStatus status, string reason = null)
    {
        return new
        {
            kind,
            ts = DateTime.UtcNow.ToString("o"),
            status,
            reason
        };
    }
}
#endif
