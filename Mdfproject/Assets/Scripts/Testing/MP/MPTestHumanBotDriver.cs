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
    private bool _hasSubmittedAugmentSelection;
    private int _uiPresentationCount;
    private int _shopPurchasePresentationCount;
    private int _augmentSelectionPresentationCount;
    private int _panelDismissPresentationCount;
    private int _uiPresentationFailureCount;
    private string _lastUiAction;
    private int _lastUiShopSlotIndex = -1;
    private bool _lastUiShopVisible;
    private bool _lastUiAugmentVisible;
    private bool _lastUiShopSlotPending;
    private bool _lastUiShopSlotSold;
    private bool _lastUiShopSlotEnabled;

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
        _hasSubmittedAugmentSelection = false;
        _uiPresentationCount = 0;
        _shopPurchasePresentationCount = 0;
        _augmentSelectionPresentationCount = 0;
        _panelDismissPresentationCount = 0;
        _uiPresentationFailureCount = 0;
        _lastUiAction = null;
        _lastUiShopSlotIndex = -1;
        _lastUiShopVisible = false;
        _lastUiAugmentVisible = false;
        _lastUiShopSlotPending = false;
        _lastUiShopSlotSold = false;
        _lastUiShopSlotEnabled = false;
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

        PlayerManager localPlayer = GameManagers.Instance != null
            ? ResolveLocalInputPlayer(GameManagers.Instance)
            : null;
        bool hideAugment = _hasSubmittedAugmentSelection
                           || (localPlayer != null && !HasPresentedAugment(localPlayer));
        DismissPreparePanels("prepare_panels_closed_on_stop", hideAugment);

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

        PresentAcceptedCommand(decision);
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

    private void CloseShopUiAfterPrepareIdle(MdfDecisionContext context, MdfDecision decision)
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

        DismissPreparePanels("prepare_panels_closed_after_shopping_complete", hideAugment: true);
    }

    private void PresentAcceptedCommand(MdfDecision decision)
    {
        if (decision == null)
        {
            return;
        }

        int shopSlotIndex = decision.Command is BuyUnitCommand buyCommand
            ? buyCommand.ShopSlotIndex
            : -1;
        if (decision.CommandType == CommandType.SelectAugment)
        {
            _hasSubmittedAugmentSelection = true;
        }

        try
        {
            if (GamePrepareUIToolkitController.TryPresentHumanBotCommand(
                    decision.CommandType,
                    shopSlotIndex,
                    out GamePrepareUIToolkitController.MpTestPrepareUiPresentationState toolkitState))
            {
                RecordUiPresentation(
                    decision.CommandTypeName,
                    decision.PlayerId,
                    toolkitState,
                    "toolkit");
                return;
            }

            GamePrepareUIToolkitController.MpTestPrepareUiPresentationState legacyState =
                PresentLegacyCommand(decision.CommandType, shopSlotIndex);
            RecordUiPresentation(
                decision.CommandTypeName,
                decision.PlayerId,
                legacyState,
                "legacy");
        }
        catch (Exception exception)
        {
            _uiPresentationFailureCount++;
            MPTestLogger.Log("human_bot_ui", "fail", "command_presentation_exception", exception.Message, new Dictionary<string, object>
            {
                { "commandType", decision.CommandTypeName },
                { "playerId", decision.PlayerId },
                { "shopSlotIndex", shopSlotIndex },
                { "exceptionType", exception.GetType().Name }
            });
        }
    }

    private GamePrepareUIToolkitController.MpTestPrepareUiPresentationState PresentLegacyCommand(
        CommandType commandType,
        int shopSlotIndex)
    {
        ShopUIController shopUi = UnityEngine.Object.FindObjectOfType<ShopUIController>(true);
        AugmentUIController augmentUi = UnityEngine.Object.FindObjectOfType<AugmentUIController>(true);
        string action = "no_prepare_ui_change";
        bool pending = false;
        bool sold = false;
        bool enabled = false;

        switch (commandType)
        {
            case CommandType.BuyUnit:
                ShopSlot slot = shopUi != null
                                && shopUi.shopSlots != null
                                && shopSlotIndex >= 0
                                && shopSlotIndex < shopUi.shopSlots.Length
                    ? shopUi.shopSlots[shopSlotIndex]
                    : null;
                pending = slot != null && slot.TryBeginPurchasePresentation();
                sold = slot != null && slot.IsPurchased();
                enabled = slot != null && slot.buyButton != null && slot.buyButton.interactable;
                action = pending ? "shop_purchase_pending" : "shop_purchase_pending_rejected";
                break;
            case CommandType.SelectAugment:
                augmentUi?.CloseAfterLocalSubmission();
                action = "augment_selection_closed";
                break;
            case CommandType.PlaceWall:
            case CommandType.MoveUnit:
                shopUi?.SetContentVisibility(false);
                augmentUi?.InitializeAndHide();
                action = "prepare_panels_closed_for_board_action";
                break;
        }

        return new GamePrepareUIToolkitController.MpTestPrepareUiPresentationState(
            action,
            shopUi != null && shopUi.IsContentVisible(),
            augmentUi != null && augmentUi.IsContentVisible(),
            shopSlotIndex,
            pending,
            sold,
            enabled);
    }

    private void DismissPreparePanels(string code, bool hideAugment)
    {
        try
        {
            GamePrepareUIToolkitController.MpTestPrepareUiPresentationState state;
            string surface;
            if (GamePrepareUIToolkitController.TryDismissHumanBotPreparePanels(hideAugment, out state))
            {
                surface = "toolkit";
            }
            else
            {
                ShopUIController shopUi = UnityEngine.Object.FindObjectOfType<ShopUIController>(true);
                AugmentUIController augmentUi = UnityEngine.Object.FindObjectOfType<AugmentUIController>(true);
                shopUi?.SetContentVisibility(false);
                if (hideAugment)
                {
                    augmentUi?.InitializeAndHide();
                }

                state = new GamePrepareUIToolkitController.MpTestPrepareUiPresentationState(
                    hideAugment ? "prepare_panels_dismissed" : "shop_panel_dismissed",
                    shopUi != null && shopUi.IsContentVisible(),
                    augmentUi != null && augmentUi.IsContentVisible(),
                    -1,
                    false,
                    false,
                    false);
                surface = "legacy";
            }

            RecordUiPresentation("Observe", _lastPlayerId, state, surface, code);
        }
        catch (Exception exception)
        {
            _uiPresentationFailureCount++;
            MPTestLogger.Log("human_bot_ui", "fail", code, exception.Message, new Dictionary<string, object>
            {
                { "playerId", _lastPlayerId },
                { "exceptionType", exception.GetType().Name }
            });
        }
    }

    private void RecordUiPresentation(
        string commandTypeName,
        int playerId,
        GamePrepareUIToolkitController.MpTestPrepareUiPresentationState state,
        string surface,
        string code = null)
    {
        bool meaningful = !string.Equals(state.Action, "no_prepare_ui_change", StringComparison.Ordinal);
        if (meaningful)
        {
            _uiPresentationCount++;
        }
        if (string.Equals(state.Action, "shop_purchase_pending", StringComparison.Ordinal))
        {
            _shopPurchasePresentationCount++;
        }
        else if (string.Equals(state.Action, "augment_selection_closed", StringComparison.Ordinal))
        {
            _augmentSelectionPresentationCount++;
        }
        else if (state.Action.Contains("dismissed") || state.Action.Contains("closed_for_board_action"))
        {
            _panelDismissPresentationCount++;
        }

        _lastUiAction = state.Action;
        _lastUiShopVisible = state.ShopIsVisible;
        _lastUiAugmentVisible = state.AugmentIsVisible;
        if (state.ShopSlotIndex >= 0)
        {
            _lastUiShopSlotIndex = state.ShopSlotIndex;
            _lastUiShopSlotPending = state.ShopSlotPending;
            _lastUiShopSlotSold = state.ShopSlotSold;
            _lastUiShopSlotEnabled = state.ShopSlotEnabled;
        }

        MPTestLogger.Log("human_bot_ui", meaningful ? "pass" : "info", code ?? state.Action, null, new Dictionary<string, object>
        {
            { "commandType", commandTypeName },
            { "playerId", playerId },
            { "surface", surface },
            { "action", state.Action },
            { "shopVisible", state.ShopIsVisible },
            { "augmentVisible", state.AugmentIsVisible },
            { "shopSlotIndex", state.ShopSlotIndex },
            { "shopSlotPending", state.ShopSlotPending },
            { "shopSlotSold", state.ShopSlotSold },
            { "shopSlotEnabled", state.ShopSlotEnabled }
        });
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
            PreferScrollAugment = _preferScrollAugment,
            UiPresentationCount = _uiPresentationCount,
            ShopPurchasePresentationCount = _shopPurchasePresentationCount,
            AugmentSelectionPresentationCount = _augmentSelectionPresentationCount,
            PanelDismissPresentationCount = _panelDismissPresentationCount,
            UiPresentationFailureCount = _uiPresentationFailureCount,
            LastUiAction = _lastUiAction,
            LastUiShopSlotIndex = _lastUiShopSlotIndex,
            LastUiShopVisible = _lastUiShopVisible,
            LastUiAugmentVisible = _lastUiAugmentVisible,
            LastUiShopSlotPending = _lastUiShopSlotPending,
            LastUiShopSlotSold = _lastUiShopSlotSold,
            LastUiShopSlotEnabled = _lastUiShopSlotEnabled
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
        [JsonProperty("uiPresentationCount")] public int UiPresentationCount;
        [JsonProperty("shopPurchasePresentationCount")] public int ShopPurchasePresentationCount;
        [JsonProperty("augmentSelectionPresentationCount")] public int AugmentSelectionPresentationCount;
        [JsonProperty("panelDismissPresentationCount")] public int PanelDismissPresentationCount;
        [JsonProperty("uiPresentationFailureCount")] public int UiPresentationFailureCount;
        [JsonProperty("lastUiAction")] public string LastUiAction;
        [JsonProperty("lastUiShopSlotIndex")] public int LastUiShopSlotIndex;
        [JsonProperty("lastUiShopVisible")] public bool LastUiShopVisible;
        [JsonProperty("lastUiAugmentVisible")] public bool LastUiAugmentVisible;
        [JsonProperty("lastUiShopSlotPending")] public bool LastUiShopSlotPending;
        [JsonProperty("lastUiShopSlotSold")] public bool LastUiShopSlotSold;
        [JsonProperty("lastUiShopSlotEnabled")] public bool LastUiShopSlotEnabled;
    }
}
#endif
