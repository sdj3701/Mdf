using System;
using System.Collections.Generic;
using UnityEngine;

public static class MPTestCommandLine
{
    public const string EnableFlag = "--mpTest";

    private static Options _cachedOptions;
    private static bool _parsed;

    public static bool IsEnabled
    {
        get
        {
            var options = GetOptions();
            return options.Enabled;
        }
    }

    public static bool IsGameFlowFrozen
    {
        get
        {
            var options = GetOptions();
            return options.Enabled && options.FreezeGameFlow;
        }
    }

    public static Options GetOptions()
    {
        if (_parsed)
        {
            return _cachedOptions;
        }

        _cachedOptions = Parse(Environment.GetCommandLineArgs());
        _parsed = true;
        return _cachedOptions;
    }

    public static Options SetFreezeGameFlowForRuntime(bool freeze)
    {
        var options = GetOptions();
        if (!options.Enabled)
        {
            return options;
        }

        options.FreezeGameFlow = freeze;
        _cachedOptions = options;
        _parsed = true;
        return _cachedOptions;
    }

    public static Options Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (string.IsNullOrEmpty(arg) || !arg.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                values[arg] = args[i + 1];
                i++;
            }
            else
            {
                flags.Add(arg);
            }
        }

        bool enabled = flags.Contains(EnableFlag) || values.ContainsKey(EnableFlag);
        return new Options
        {
            Enabled = enabled,
            Role = Get(values, "--mpRole", "host"),
            Session = Get(values, "--mpSession", "mp-local"),
            MaxPlayers = Mathf.Clamp(GetInt(values, "--mpMaxPlayers", 2), 2, 4),
            Scene = Get(values, "--mpScene", "Game"),
            AutoStart = enabled && (flags.Contains("--mpAutoStart") || values.ContainsKey("--mpAutoStart")),
            LoadGame = enabled && (flags.Contains("--mpLoadGame") || values.ContainsKey("--mpLoadGame")),
            ExitAfterSeconds = Mathf.Max(0, GetInt(values, "--mpExitAfterSeconds", 0)),
            AutomationPort = GetInt(values, "--mpAutomationPort", 0),
            AutomationToken = Get(values, "--mpAutomationToken", string.Empty),
            ConnectionToken = Get(values, "--mpConnectionToken", string.Empty),
            CaseName = Get(values, "--mpCase", "manual"),
            ArtifactDir = Get(values, "--mpArtifactDir", string.Empty),
            Seed = GetInt(values, "--mpSeed", 0),
            Scenario = Get(values, "--mpScenario", "game_smoke"),
            DisableAiFill = enabled && (flags.Contains("--mpDisableAiFill") || values.ContainsKey("--mpDisableAiFill")),
            FreezeGameFlow = enabled && (flags.Contains("--mpFreezeGameFlow") || values.ContainsKey("--mpFreezeGameFlow")),
            HumanBot = enabled && (flags.Contains("--mpHumanBot") || values.ContainsKey("--mpHumanBot")),
            BotPersona = Get(values, "--mpBotPersona", "balanced"),
            BotSeed = GetInt(values, "--mpBotSeed", GetInt(values, "--mpSeed", 0)),
            BotDurationSeconds = Mathf.Max(0, GetInt(values, "--mpBotDurationSeconds", 0)),
            BotStopAtRound = Mathf.Max(0, GetInt(values, "--mpBotStopAtRound", 0)),
            BotMaxCommands = Mathf.Max(0, GetInt(values, "--mpBotMaxCommands", 0)),
            BotSkipPrepare = enabled && (flags.Contains("--mpBotSkipPrepare") || values.ContainsKey("--mpBotSkipPrepare")),
            BotPrepareAugmentOnly = enabled && (flags.Contains("--mpBotPrepareAugmentOnly") || values.ContainsKey("--mpBotPrepareAugmentOnly")),
            BotPreferScrollAugment = enabled && (flags.Contains("--mpBotPreferScrollAugment") || values.ContainsKey("--mpBotPreferScrollAugment")),
            BotRecordJournal = Get(values, "--mpBotRecordJournal", string.Empty)
        };
    }

    private static string Get(Dictionary<string, string> values, string key, string fallback)
    {
        return values.TryGetValue(key, out string value) && !string.IsNullOrEmpty(value) ? value : fallback;
    }

    private static int GetInt(Dictionary<string, string> values, string key, int fallback)
    {
        if (values.TryGetValue(key, out string value) && int.TryParse(value, out int parsed))
        {
            return parsed;
        }

        return fallback;
    }

    public struct Options
    {
        public bool Enabled;
        public string Role;
        public string Session;
        public int MaxPlayers;
        public string Scene;
        public bool AutoStart;
        public bool LoadGame;
        public int ExitAfterSeconds;
        public int AutomationPort;
        public string AutomationToken;
        public string ConnectionToken;
        public string CaseName;
        public string ArtifactDir;
        public int Seed;
        public string Scenario;
        public bool DisableAiFill;
        public bool FreezeGameFlow;
        public bool HumanBot;
        public string BotPersona;
        public int BotSeed;
        public int BotDurationSeconds;
        public int BotStopAtRound;
        public int BotMaxCommands;
        public bool BotSkipPrepare;
        public bool BotPrepareAugmentOnly;
        public bool BotPreferScrollAugment;
        public string BotRecordJournal;

        public string SafeRole => string.IsNullOrEmpty(Role) ? "unknown" : Role.ToLowerInvariant();
        public string AutomationTokenHash => MPTestLogger.HashForLog(AutomationToken);
        public string ConnectionTokenHash => DurableConnectionTokenIdentity.BuildHash(ConnectionToken);
    }
}
