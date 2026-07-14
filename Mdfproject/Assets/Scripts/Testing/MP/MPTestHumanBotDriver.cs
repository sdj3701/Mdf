#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Fusion;
using Newtonsoft.Json;
using UnityEngine;

public sealed class MPTestHumanBotDriver : MonoBehaviour
{
    private const float MinimumCommandIntervalSeconds = 0.7f;

    public static MPTestHumanBotDriver Instance { get; private set; }

    private MPTestCommandLine.Options _options;
    private MdfBotProfile _profile;
    private PrepareDecisionPolicy _preparePolicy;
    private BattleDecisionPolicy _battlePolicy;
    private HumanClientCommandEmitter _commandEmitter;
    private MPTestBotJournal _journal;
    private MPTestBotPersona _persona;
    private bool _configured;
    private bool _running;
    private float _startedAt;
    private float _nextDecisionAt;
    private float _lastNoopLogAt;
    private int _commandsIssued;
    private string _lastDecision;
    private string _lastCommandType;
    private string _lastError;
    private string _stopReason;
    private int _lastPlayerId = -1;
    private bool _lastHadInputAuthority;
    private int _maxCommands;
    private int _durationSeconds;
    private int _stopAtRound;
    private bool _skipPrepare;
    private bool _prepareAugmentOnly;
    private bool _preferScrollAugment;

    public BotStatus Status => BuildStatus();
    public object[] RecentJournal => _journal != null ? _journal.Recent : Array.Empty<object>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public void Configure(MPTestCommandLine.Options options)
    {
        _options = options;
        _configured = true;
    }

    public bool StartDriver(
        MPTestCommandLine.Options options,
        out string reason,
        string personaOverride = null,
        int? seedOverride = null,
        int? durationSecondsOverride = null,
        int? stopAtRoundOverride = null,
        int? maxCommandsOverride = null,
        bool? skipPrepareOverride = null,
        bool? prepareAugmentOnlyOverride = null,
        bool? preferScrollAugmentOverride = null,
        string journalPathOverride = null)
    {
        Configure(options);
        if (!CanRun(options, out reason))
        {
            _lastError = reason;
            return false;
        }

        _persona = MPTestBotPersonaParser.Parse(string.IsNullOrWhiteSpace(personaOverride) ? options.BotPersona : personaOverride);
        int seed = seedOverride ?? options.BotSeed;
        _durationSeconds = Mathf.Max(0, durationSecondsOverride ?? options.BotDurationSeconds);
        _stopAtRound = Mathf.Max(0, stopAtRoundOverride ?? options.BotStopAtRound);
        _maxCommands = Mathf.Max(0, maxCommandsOverride ?? options.BotMaxCommands);
        _skipPrepare = skipPrepareOverride ?? options.BotSkipPrepare;
        _prepareAugmentOnly = prepareAugmentOnlyOverride ?? options.BotPrepareAugmentOnly;
        _preferScrollAugment = preferScrollAugmentOverride ?? options.BotPreferScrollAugment;
        string journalPath = ResolveJournalPath(options, journalPathOverride);

        _profile = MdfBotProfile.Create(
            _persona.ToCliValue(),
            seed,
            _preferScrollAugment,
            MdfBotProfile.DefaultDecisionIntervalSeconds);
        _preparePolicy = new PrepareDecisionPolicy(_profile);
        _battlePolicy = new BattleDecisionPolicy();
        _commandEmitter = null;
        _journal = new MPTestBotJournal(journalPath);
        _commandsIssued = 0;
        _lastDecision = null;
        _lastCommandType = null;
        _lastError = null;
        _stopReason = null;
        _running = true;
        _startedAt = Time.realtimeSinceStartup;
        _nextDecisionAt = 0f;

        MPTestLogger.Log("human_bot", "begin", "start", null, new Dictionary<string, object>
        {
            { "persona", _persona.ToCliValue() },
            { "seed", seed },
            { "maxCommands", _maxCommands },
            { "durationSeconds", _durationSeconds },
            { "stopAtRound", _stopAtRound },
            { "skipPrepare", _skipPrepare },
            { "prepareAugmentOnly", _prepareAugmentOnly },
            { "preferScrollAugment", _preferScrollAugment },
            { "journal", string.IsNullOrEmpty(journalPath) ? "none" : journalPath }
        });
        _journal.Record(MPTestBotJournal.BuildStatusEntry("bot_start", BuildStatus()));
        reason = null;
        return true;
    }

    public void StopDriver(string reason = "stopped")
    {
        if (!_running)
        {
            _stopReason = reason;
            return;
        }

        _running = false;
        _stopReason = reason;
        MPTestLogger.Log("human_bot", "pass", "stop", reason, new Dictionary<string, object>
        {
            { "commandsIssued", _commandsIssued },
            { "persona", _persona.ToCliValue() }
        });
        _journal?.Record(MPTestBotJournal.BuildStatusEntry("bot_stop", BuildStatus(), reason));
    }

    private void Update()
    {
        if (!_running)
        {
            return;
        }

        if (ShouldStop())
        {
            return;
        }

        if (Time.realtimeSinceStartup < _nextDecisionAt)
        {
            return;
        }

        float decisionInterval = _profile != null
            ? _profile.DecisionIntervalSeconds
            : MdfBotProfile.DefaultDecisionIntervalSeconds;
        decisionInterval = Mathf.Max(decisionInterval, MinimumCommandIntervalSeconds);
        GameManagers gameManagers = GameManagers.Instance;
        if (gameManagers != null)
        {
            decisionInterval = BattleSpawnCadence.ResolveDecisionInterval(
                gameManagers.GetGameState(),
                decisionInterval);
        }
        _nextDecisionAt = Time.realtimeSinceStartup + decisionInterval;
        TickDecision();
    }

    private void TickDecision()
    {
        var gm = GameManagers.Instance;
        if (gm == null || gm.CommandProcessor == null)
        {
            SetTransientError("game_managers_or_command_processor_missing");
            return;
        }

        PlayerManager player = ResolveLocalInputPlayer(gm);
        if (player == null)
        {
            SetTransientError("local_input_player_missing");
            return;
        }

        _lastPlayerId = player.playerId;
        _lastHadInputAuthority = player.Object != null && player.Object.IsValid && player.Object.HasInputAuthority;
        if (!_lastHadInputAuthority)
        {
            SetTransientError("local_player_missing_input_authority");
            return;
        }

        _commandEmitter = new HumanClientCommandEmitter(gm, player, "human_bot_emitter");
        var context = MdfDecisionContext.Create(
            gm,
            player,
            CommandExecutionScope.ClientRequest,
            _profile != null ? _profile.Persona : _persona.ToCliValue(),
            isHumanBot: true,
            isServerAi: false,
            isTestAutomation: true);

        MdfDecision decision = null;
        bool hasDecision;
        switch (gm.GetGameState())
        {
            case GameManagers.GameState.Prepare:
                if (_skipPrepare)
                {
                    decision = MdfDecision.Observe(context, "bot_skip_prepare");
                    hasDecision = false;
                }
                else if (_prepareAugmentOnly && !HasPresentedAugment(player))
                {
                    decision = MdfDecision.Observe(context, "bot_prepare_augment_only_done");
                    hasDecision = false;
                }
                else
                {
                    hasDecision = _preparePolicy != null && _preparePolicy.TryChoose(context, out decision);
                    if (_prepareAugmentOnly && decision != null && decision.CommandType != CommandType.SelectAugment)
                    {
                        decision = MdfDecision.Observe(context, "bot_prepare_augment_only_blocked");
                        hasDecision = false;
                    }
                }
                break;
            case GameManagers.GameState.Battle1:
            case GameManagers.GameState.Battle2:
                hasDecision = _battlePolicy != null && _battlePolicy.TryChoose(context, out decision);
                break;
            default:
                decision = MdfDecision.Observe(context, "human_bot_observe_non_action_phase");
                hasDecision = false;
                break;
        }

        if (!hasDecision || decision == null || !decision.HasCommandPayload)
        {
            _lastDecision = decision != null ? decision.Reason : "no_decision";
            _lastCommandType = decision != null ? decision.CommandTypeName : "Observe";
            CloseShopUiAfterPrepareIdle(context, decision);
            if (Time.realtimeSinceStartup - _lastNoopLogAt > 2f)
            {
                _lastNoopLogAt = Time.realtimeSinceStartup;
                MPTestLogger.Log("human_bot_decision", "info", "observe", _lastDecision, new Dictionary<string, object>
                {
                    { "playerId", _lastPlayerId },
                    { "persona", _persona.ToCliValue() }
                });
            }
            return;
        }

        CloseShopUiBeforeBoardAction(decision);

        if (!_commandEmitter.TryEmit(decision, out BattleCommandResult emitResult))
        {
            _lastDecision = decision.Reason;
            _lastCommandType = decision.CommandTypeName;
            _lastError = emitResult.ErrorCode;
            MPTestLogger.Log("human_bot_decision", "fail", decision.CommandTypeName, emitResult.Message, new Dictionary<string, object>
            {
                { "playerId", decision.PlayerId },
                { "persona", _persona.ToCliValue() },
                { "errorCode", emitResult.ErrorCode },
                { "target", decision.Target ?? "none" }
            });
            _journal?.Record(MPTestBotJournal.BuildDecisionEntry(BuildStatus(), decision));
            return;
        }

        _commandsIssued++;
        _lastDecision = decision.Reason;
        _lastCommandType = decision.CommandTypeName;
        _lastError = null;

        MPTestLogger.Log("human_bot_decision", "begin", decision.CommandTypeName, decision.Reason, new Dictionary<string, object>
        {
            { "playerId", decision.PlayerId },
            { "persona", _persona.ToCliValue() },
            { "commandsIssued", _commandsIssued },
            { "target", decision.Target ?? "none" }
        });
        _journal?.Record(MPTestBotJournal.BuildDecisionEntry(BuildStatus(), decision));
    }

    private static void CloseShopUiAfterPrepareIdle(MdfDecisionContext context, MdfDecision decision)
    {
        if (context == null || context.GameState != GameManagers.GameState.Prepare)
        {
            return;
        }

        string reason = decision != null ? decision.Reason : null;
        if (!string.Equals(reason, "no_legal_prepare_command", StringComparison.Ordinal))
        {
            return;
        }

        CloseShopUi("shop_close_after_shopping_complete", "Observe", context.Actor != null ? context.Actor.playerId : -1);
    }

    private static void CloseShopUiBeforeBoardAction(MdfDecision decision)
    {
        if (decision == null ||
            (decision.CommandType != CommandType.PlaceWall && decision.CommandType != CommandType.MoveUnit))
        {
            return;
        }

        CloseShopUi("shop_close_before_board_action", decision.CommandTypeName, decision.PlayerId);
    }

    private static void CloseShopUi(string code, string commandTypeName, int playerId)
    {
        try
        {
            ShopUIController shopUi = UnityEngine.Object.FindObjectOfType<ShopUIController>(true);
            bool shopUiPresent = shopUi != null;
            bool wasVisible = shopUiPresent && shopUi.IsContentVisible();
            if (wasVisible)
            {
                shopUi.SetContentVisibility(false);
            }

            MPTestLogger.Log("human_bot_ui", wasVisible ? "pass" : "info", code, null, new Dictionary<string, object>
            {
                { "commandType", commandTypeName },
                { "playerId", playerId },
                { "shopUiPresent", shopUiPresent },
                { "wasVisible", wasVisible },
                { "closed", wasVisible }
            });
        }
        catch (Exception exception)
        {
            MPTestLogger.Log("human_bot_ui", "fail", code, exception.Message, new Dictionary<string, object>
            {
                { "commandType", commandTypeName },
                { "playerId", playerId },
                { "exceptionType", exception.GetType().Name }
            });
        }
    }

    private bool ShouldStop()
    {
        if (_durationSeconds > 0 && Time.realtimeSinceStartup - _startedAt >= _durationSeconds)
        {
            StopDriver("duration_reached");
            return true;
        }

        if (_maxCommands > 0 && _commandsIssued >= _maxCommands)
        {
            StopDriver("max_commands_reached");
            return true;
        }

        var gm = GameManagers.Instance;
        if (_stopAtRound > 0 && gm != null && gm.currentRound >= _stopAtRound && _commandsIssued > 0)
        {
            StopDriver("stop_round_reached");
            return true;
        }

        return false;
    }

    private void SetTransientError(string error)
    {
        _lastError = error;
        if (Time.realtimeSinceStartup - _lastNoopLogAt > 2f)
        {
            _lastNoopLogAt = Time.realtimeSinceStartup;
            MPTestLogger.Log("human_bot", "info", error, null, new Dictionary<string, object>
            {
                { "persona", _persona.ToCliValue() },
                { "commandsIssued", _commandsIssued }
            });
        }
    }

    private PlayerManager ResolveLocalInputPlayer(GameManagers gm)
    {
        if (gm.localPlayer != null &&
            gm.localPlayer.Object != null &&
            gm.localPlayer.Object.IsValid &&
            gm.localPlayer.Object.HasInputAuthority)
        {
            return gm.localPlayer;
        }

        return gm.AllPlayers.FirstOrDefault(player =>
            player != null &&
            player.Object != null &&
            player.Object.IsValid &&
            player.Object.HasInputAuthority);
    }

    private BotStatus BuildStatus()
    {
        return new BotStatus
        {
            Enabled = _configured && _options.Enabled && _options.HumanBot,
            Running = _running,
            Persona = _persona.ToCliValue(),
            CommandsIssued = _commandsIssued,
            LastDecision = _lastDecision,
            LastCommandType = _lastCommandType,
            LastError = _lastError,
            JournalPath = _journal != null ? _journal.Path : null,
            PlayerId = _lastPlayerId,
            HasLocalInputAuthority = _lastHadInputAuthority,
            StopReason = _stopReason,
            SkipPrepare = _skipPrepare,
            PrepareAugmentOnly = _prepareAugmentOnly,
            PreferScrollAugment = _preferScrollAugment
        };
    }

    private static bool HasPresentedAugment(PlayerManager player)
    {
        var snapshotNames = player != null ? player.GetPresentedAugmentSnapshotNames() : null;
        return snapshotNames != null && snapshotNames.Length > 0;
    }

    public static bool CanRun(MPTestCommandLine.Options options, out string reason)
    {
        if (!options.Enabled)
        {
            reason = "missing --mpTest";
            return false;
        }

        if (!options.HumanBot)
        {
            reason = "missing --mpHumanBot";
            return false;
        }

        reason = null;
        return true;
    }

    private static string ResolveJournalPath(MPTestCommandLine.Options options, string overridePath)
    {
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return overridePath;
        }

        if (!string.IsNullOrWhiteSpace(options.BotRecordJournal))
        {
            return options.BotRecordJournal;
        }

        if (string.IsNullOrWhiteSpace(options.ArtifactDir))
        {
            return null;
        }

        string fileName = $"bot-{options.SafeRole}.jsonl";
        return Path.Combine(options.ArtifactDir, fileName);
    }

    [Serializable]
    public sealed class BotStatus
    {
        [JsonProperty("enabled")] public bool Enabled;
        [JsonProperty("running")] public bool Running;
        [JsonProperty("persona")] public string Persona;
        [JsonProperty("commandsIssued")] public int CommandsIssued;
        [JsonProperty("lastDecision")] public string LastDecision;
        [JsonProperty("lastCommandType")] public string LastCommandType;
        [JsonProperty("lastError")] public string LastError;
        [JsonProperty("journalPath")] public string JournalPath;
        [JsonProperty("playerId")] public int PlayerId;
        [JsonProperty("hasLocalInputAuthority")] public bool HasLocalInputAuthority;
        [JsonProperty("stopReason")] public string StopReason;
        [JsonProperty("skipPrepare")] public bool SkipPrepare;
        [JsonProperty("prepareAugmentOnly")] public bool PrepareAugmentOnly;
        [JsonProperty("preferScrollAugment")] public bool PreferScrollAugment;
    }
}
#endif
