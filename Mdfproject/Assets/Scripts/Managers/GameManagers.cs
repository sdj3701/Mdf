using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using UnityEngine.UI;
using Cysharp.Threading.Tasks;
using Fusion;
using System.Threading;
using System.Threading.Tasks;

// MonoBehaviour 대신 NetworkBehaviour를 상속받아 네트워크 객체로 만듭니다.
public partial class GameManagers : NetworkBehaviour
{
    // 싱글톤 패턴은 유지하되, 초기화는 Spawned()에서 수행합니다.
    // ★ Host Migration 지원을 위해 internal set 사용
    public static GameManagers Instance { get; internal set; }
    public CommandProcessor CommandProcessor { get; private set; }

    // [수정] Fusion 2의 변경 감지를 위한 ChangeDetector 인스턴스
    private ChangeDetector _changeDetector;

    #region 인게임 관련 변수 (네트워크 동기화)
    public enum GameState { Setup, DataLoading, Prepare, Battle1, Battle2, GameOver }

    // [수정] OnChanged 속성을 제거했습니다. Fusion 2에서는 ChangeDetector를 사용합니다.
    [Networked]
    public GameState currentState { get; set; }

    [Networked]
    public int currentRound { get; set; }

    [Networked]
    private TickTimer phaseTimer { get; set; }

    public float currentPhaseTimer => phaseTimer.IsRunning ? phaseTimer.RemainingTime(Runner) ?? 0f : 0f;

    // 세션은 최대 4명까지 지원
    private const int MAX_PLAYERS = 4;

    [Networked, Capacity(4)]
    private NetworkArray<NetworkObject> NetworkPlayers { get; }

    // 싱글플레이어 모드에서 사용할 플레이어 수 (GameSceneInitializer에서 설정)
    [Networked]
    public int singlePlayerModeCount { get; set; }

    [HideInInspector] public PlayerManager localPlayer;

    // 플레이어 데이터가 모두 준비되었을 때 발생시키는 이벤트
    public static System.Action OnPlayersDataReady;
    #endregion

    // 프리팹은 AddressablesManager에서 관리

    // AddressablesManager에서 캐시된 프리팹 접근
    public GameObject defaultMonsterPrefab => AddressablesManager.Instance?.DefaultMonsterPrefab;

    [Header("자동 생성 위치 설정")]
    public Vector3 player1BasePosition = new Vector3(0, 0, 0);
    public Vector3 playerOffset = new Vector3(0, 10, 0);
    private bool _loggedOffsetNormalization;

    public Vector3 GetResolvedPlayerOffset()
    {
        // 기본값 그대로 사용하되, 레거시(Y축만 사용) 설정은 한 번만 경고하고 Z축 기준으로 보정한다.
        bool needsNormalization = Mathf.Abs(playerOffset.z) < 0.001f && Mathf.Abs(playerOffset.y) > 0.001f;
        if (!needsNormalization)
        {
            return playerOffset;
        }

        Vector3 normalized = new Vector3(playerOffset.x, 0f, -Mathf.Abs(playerOffset.y));
        if (!_loggedOffsetNormalization)
        {
            _loggedOffsetNormalization = true;
            // Debug.LogWarning($"[GameManagers] playerOffset is Y-axis based ({playerOffset}). Normalized to {normalized}.");
        }

        return normalized;
    }

    #region 단계별 시간 및 보상
    [Header("단계별 시간 설정 (초)")]
    [Tooltip("게임 시작 후 첫 번째 준비 단계 시간 (초)")]
    public float firstPreparePhaseTime = 60f;
    public float preparePhaseTime = 45f;
    public float combatTime = 60f;

    [Header("폭주 모드 설정")]
    [Tooltip("전투 종료 N초 전에 폭주 모드 발동")]
    public float berserkTriggerTime = 5f;

    [Header("라운드 보상")]
    public int baseGoldPerRound = 5;
    public int maxInterest = 5;
    #endregion

    #region 로비 및 UI 관련 변수
    [Header("로비 캐릭터 선택")]
    public Button[] SelectCharacterButton;
    private Queue<string> characterselectdata = new Queue<string>();
    private List<string> selectCharacterName = new List<string>();
    private int maxqueue = 3;
    #endregion

    private ShopUIController localPlayerShopUI;
    private GameObject localPlayerShopUIGameObject;
    private AugmentUIController augmentSelectionUI;
    private bool _isSettingUpGameUI;
    private bool _hasCompletedGameUISetup;
    private static int _hostMigrationTraceSeq;
    private int _activeMigrationTraceId = -1;
    private enum MigrationRestoreStage
    {
        None,
        Preparing,
        UiRestoreRunning,
        UiRestored,
        WaitingForFlowResume,
        FlowResumed,
        Failed
    }
    private MigrationRestoreStage _migrationRestoreStage = MigrationRestoreStage.None;
    private bool _migrationWarnedPrepareExpiryRace;
    private float _migrationRestoreStartedRealtime;
    private int _migrationRestoreStartFrame = -1;
    private bool _migrationTimerPaused;
    private float _migrationPausedTimerRemainingSeconds;
    private bool _migrationReadyEventPublished;
    private readonly HashSet<int> _migrationPublishedStateEvents = new HashSet<int>();
    private readonly HashSet<string> _migrationPrepareShopRecoveryKeys = new HashSet<string>();
    private readonly HashSet<string> _migrationBattleRebootstrapKeys = new HashSet<string>();
    private float _lastMigrationCommandHoldLogRealtime = -10f;
    private bool _isSpawned;
    private CancellationTokenSource _lifecycleCts;
    private CancellationTokenSource _migrationCts;
    
    /// <summary>
    /// Host Migration 중 또는 Spawned 전에는 Networked 속성에 접근할 수 없습니다.
    /// 이 프로퍼티로 안전하게 체크해서 접근하세요.
    /// </summary>
    public bool IsReadyForNetworkAccess => Object != null && Object.IsValid && _isSpawned;

    private bool IsMigrationRestoreInProgress =>
        _migrationRestoreStage != MigrationRestoreStage.None &&
        _migrationRestoreStage != MigrationRestoreStage.FlowResumed &&
        _migrationRestoreStage != MigrationRestoreStage.Failed;

    private bool IsMigrationUiRestoreCompleted =>
        _migrationRestoreStage == MigrationRestoreStage.UiRestored ||
        _migrationRestoreStage == MigrationRestoreStage.WaitingForFlowResume ||
        _migrationRestoreStage == MigrationRestoreStage.FlowResumed;

    private bool hasCombatBeenShortened = false;
    private bool firstPrepareDurationUsed = false;
    private bool isTransitioningRound = false; // 라운드 전환 중 중복 호출 방지
    private bool _hasBerserkTriggered = false;  // 폭주 모드 트리거 여부
    private bool _hasBerserkTriggeredBattle2 = false;  // Battle2 폭주 모드 트리거 여부
    
    /// <summary>
    /// 현재 버서커 모드가 활성화되어 있는지 반환합니다.
    /// 신규 소환 몬스터에 자동으로 버서커 모드를 적용하기 위해 사용됩니다.
    /// </summary>
    public bool IsBerserkModeActive => 
        (currentState == GameState.Battle1 && _hasBerserkTriggered) || 
        (currentState == GameState.Battle2 && _hasBerserkTriggeredBattle2);
    private TickTimer _battleStartCheckDelay; // 전투 시작 후 상태 체크 딜레이

    #region 전투 시퀀스 관련 필드
    /// <summary>
    /// [더 이상 사용하지 않음 - 호환성을 위해 유지]
    /// 현재 라운드에서 선공(Battle1에서 공격)하는 플레이어 ID
    /// </summary>
    [Networked] public int FirstAttackerPlayerId { get; set; }

    /// <summary>
    /// 라운드별 매칭된 상대 정보. Key: PlayerId, Value: OpponentPlayerId (-1이면 상대 없음)
    /// </summary>
    private Dictionary<int, int> _battleOpponents = new Dictionary<int, int>();
    
    /// <summary>
    /// 각 플레이어별 매칭에서의 선공자 ID. Key: PlayerId, Value: FirstAttackerPlayerId in this match
    /// 각 매칭 쌍마다 독립적으로 선공자를 랜덤으로 결정하여 공정성 보장
    /// </summary>
    private Dictionary<int, int> _matchFirstAttacker = new Dictionary<int, int>();
    #endregion

    /// <summary>
    /// 이 NetworkBehaviour가 네트워크 상에 스폰될 때 Fusion에 의해 호출됩니다.
    /// </summary>
    public override void Spawned()
    {
        EnsureLifecycleCancellationToken();

        bool isHostMigration = HostMigrationHandler.Instance != null && HostMigrationHandler.Instance.IsMigrating;
        Debug.Log($"<color=cyan>[GameManagers.Spawned] ENTER this={BuildDebugSummary(this)} | static={BuildDebugSummary(Instance)} | isHostMigration={isHostMigration}</color>");

        // Host Migration 중에는 old/new Runner의 GameManagers가 잠시 공존할 수 있음.
        // 기존 인스턴스가 다른 Runner 소속이면 새 인스턴스를 유지하고 교체해야 한다.
        if (Instance != null && Instance != this)
        {
            bool existingValid = Instance.Object != null && Instance.Object.IsValid;
            bool sameRunner = existingValid && Instance.Runner == Runner;

            if (sameRunner)
            {
                // Debug.LogWarning($"<color=orange>[GameManagers] 동일 Runner의 중복 인스턴스 감지 - 현재 인스턴스 제거\n  existing={BuildDebugSummary(Instance)}\n  current={BuildDebugSummary(this)}</color>");
                Runner.Despawn(Object);
                return;
            }

            // Debug.LogWarning($"<color=yellow>[GameManagers] 다른 Runner의 기존 Instance 감지 - 새 Runner 인스턴스로 교체\n  existing={BuildDebugSummary(Instance)}\n  current={BuildDebugSummary(this)}</color>");
        }

        Instance = this;
        // Debug.Log($"[GameManagers.Spawned] static Instance 재설정 완료: {BuildDebugSummary(Instance)}");

        if (CommandProcessor == null)
        {
            CommandProcessor = new CommandProcessor();
            // Debug.Log("[GameManagers.Spawned] CommandProcessor 생성");
        }
        
        // Game 씬 진입 시 UIManagers 활성화 (이전 게임 종료 시 비활성화되었을 수 있음)
        if (UIManagers.Instance != null)
        {
            UIManagers.Instance.gameObject.SetActive(true);
        }

        if (LoadManager.Instance == null)
        {
            var go = new GameObject("LoadManager");
            go.AddComponent<LoadManager>();
        }

        // SurvivorBossManager 초기화 (보스 생존 시스템)
        if (SurvivorBossManager.Instance == null)
        {
            var survivorManagerGO = new GameObject("SurvivorBossManager");
            survivorManagerGO.AddComponent<SurvivorBossManager>();
        }

        _changeDetector = GetChangeDetector(ChangeDetector.Source.SimulationState);

        // Host Migration 복원 객체는 GameFlow를 다시 시작하면 중복 스폰/상태 리셋이 발생함.
        // 스냅샷 상태를 유지한 채 로컬 참조만 복원하도록 최소 초기화만 수행한다.
        if (isHostMigration)
        {
            Debug.Log($"<color=cyan>[GameManagers] Host Migration 복원 스폰 감지 - GameFlow 재시작 생략\n  state={currentState}, round={currentRound}, timer={currentPhaseTimer:F1}, hasAuth={Object?.HasStateAuthority}</color>");
            if (LoadManager.Instance != null && !LoadManager.Instance.IsReady)
            {
                Debug.LogWarning("[GameManagers] Host Migration 복원 경로에서 LoadManager가 미준비 상태라 초기화를 재시도합니다.");
                RunLifecycleTask(LoadManager.Instance.InitializeAsync(), "Spawned/LoadManager.InitializeAsync");
            }
            _isSpawned = true;
            GameEvents.TriggerGameManagersReady();
            return;
        }

        // 일반 시작 경로
        RunLifecycleTask(InitializeAndStartGame(), "Spawned/InitializeAndStartGame");
    }

    private void OnDestroy()
    {
        bool wasStaticInstance = Instance == this;
        bool isMigrating = HostMigrationHandler.Instance != null && HostMigrationHandler.Instance.IsMigrating;
        // Debug.LogWarning($"<color=orange>[GameManagers.OnDestroy] 파괴됨: {BuildDebugSummary(this)} | wasStaticInstance={wasStaticInstance} | isMigrating={isMigrating}</color>");

        if (Instance == this)
        {
            Instance = null;
            // Debug.LogWarning("[GameManagers.OnDestroy] static Instance를 null로 정리");
        }

        CancelAndDisposeToken(ref _migrationCts);
        CancelAndDisposeToken(ref _lifecycleCts);
    }

    private void EnsureLifecycleCancellationToken()
    {
        if (_lifecycleCts == null || _lifecycleCts.IsCancellationRequested)
        {
            CancelAndDisposeToken(ref _lifecycleCts);
            _lifecycleCts = new CancellationTokenSource();
        }
    }

    private CancellationToken GetLifecycleCancellationToken()
    {
        EnsureLifecycleCancellationToken();
        return _lifecycleCts.Token;
    }

    private void ResetMigrationCancellationToken()
    {
        CancelAndDisposeToken(ref _migrationCts);
        _migrationCts = new CancellationTokenSource();
    }

    private CancellationToken GetMigrationCancellationToken()
    {
        if (_migrationCts == null || _migrationCts.IsCancellationRequested)
        {
            ResetMigrationCancellationToken();
        }

        return _migrationCts.Token;
    }

    private static void CancelAndDisposeToken(ref CancellationTokenSource cts)
    {
        if (cts == null)
        {
            return;
        }

        try
        {
            cts.Cancel();
        }
        catch
        {
            // 이미 취소/해제된 경우 무시
        }
        finally
        {
            cts.Dispose();
            cts = null;
        }
    }

    private void RunLifecycleTask(UniTask task, string context)
    {
        task.AttachExternalCancellation(GetLifecycleCancellationToken()).Forget(ex => HandleTaskException(ex, context));
    }

    private void RunMigrationTask(UniTask task, string context)
    {
        task.AttachExternalCancellation(GetMigrationCancellationToken()).Forget(ex => HandleTaskException(ex, context));
    }

    private static void HandleTaskException(System.Exception ex, string context)
    {
        if (ex is System.OperationCanceledException)
        {
            return;
        }

        Debug.LogError($"[GameManagers/Async] {context} 실패: {ex.Message}");
    }

    private void SetMigrationRestoreStage(MigrationRestoreStage nextStage, string reason = null)
    {
        if (_migrationRestoreStage == nextStage)
        {
            return;
        }

        var prevStage = _migrationRestoreStage;
        _migrationRestoreStage = nextStage;
        LogMigrationTrace("MigrationStage", $"from={prevStage}, to={nextStage}, reason={reason ?? "n/a"}");

        if (nextStage == MigrationRestoreStage.FlowResumed || nextStage == MigrationRestoreStage.Failed)
        {
            CancelAndDisposeToken(ref _migrationCts);
        }
    }

    private static string BuildDebugSummary(GameManagers gm)
    {
        if (gm == null)
        {
            return "GM=NULL";
        }

        var runner = gm.Runner;
        var obj = gm.Object;
        string runnerName = runner != null ? runner.name : "null";
        bool runnerRunning = runner != null && runner.IsRunning;
        bool objectValid = obj != null && obj.IsValid;
        string stateAuth = obj != null ? obj.HasStateAuthority.ToString() : "null";
        return $"name={gm.name}, instanceId={gm.GetInstanceID()}, hash={gm.GetHashCode()}, runner={runnerName}, runnerRunning={runnerRunning}, objectValid={objectValid}, stateAuth={stateAuth}, ready={gm.IsReadyForNetworkAccess}, active={gm.gameObject.activeInHierarchy}";
    }

    private string BuildMigrationPlayerSnapshot()
    {
        var players = AllPlayers?.Where(p => p != null).ToList();
        if (players == null || players.Count == 0)
        {
            return "players=0";
        }

        return string.Join(" | ", players.Select(player =>
        {
            int shopCount = player.shopManager != null ? player.shopManager.GetCurrentShopItems().Count : -1;
            bool shopDbLoaded = player.shopManager != null && player.shopManager.IsDatabaseLoaded;
            int augmentCount = player.augmentManager?.GetPresentedAugments()?.Count ?? -1;
            bool augmentLoaded = player.augmentManager != null && player.augmentManager.IsDataLoaded;
            return $"P{player.playerId}:shop={shopCount}(db={shopDbLoaded}),aug={augmentCount}(loaded={augmentLoaded}),fight={player.IsActivelyFighting},attacker={player.IsAttackerInCurrentBattle}";
        }));
    }

    private string BuildRoundTransitionSnapshot()
    {
        string localInfo = TryGetPlayerIdSafe(localPlayer, out int localPlayerId) ? localPlayerId.ToString() : "null";
        var players = AllPlayers?.Where(p => p != null).ToList();
        if (players == null || players.Count == 0)
        {
            return $"local={localInfo}, players=0";
        }

        var details = new List<string>(players.Count);
        foreach (var player in players)
        {
            if (!TryGetPlayerIdSafe(player, out int playerId))
            {
                details.Add("P?:unreadable");
                continue;
            }

            string inputAuthority = player.Object != null && player.Object.IsValid
                ? player.Object.InputAuthority.ToString()
                : "invalid";
            bool hasInputAuthority = player.Object != null && player.Object.IsValid && player.Object.HasInputAuthority;
            details.Add($"P{playerId}(ready={player.IsReadyForPlayerActions},hasInput={hasInputAuthority},input={inputAuthority})");
        }

        return $"local={localInfo}, players={string.Join(" | ", details)}";
    }

    private void LogMigrationTrace(string step, string extra = null)
    {
        if (_activeMigrationTraceId < 0)
        {
            return;
        }

        bool timerRunning = phaseTimer.IsRunning;
        bool timerExpired = timerRunning && Runner != null && phaseTimer.Expired(Runner);
        float remaining = timerRunning && Runner != null ? (phaseTimer.RemainingTime(Runner) ?? 0f) : 0f;
        float elapsed = _migrationRestoreStartedRealtime > 0f ? Time.realtimeSinceStartup - _migrationRestoreStartedRealtime : -1f;

        Debug.Log(
            $"[HM-TRACE #{_activeMigrationTraceId}] {step} | frame={Time.frameCount} elapsed={(elapsed >= 0f ? elapsed.ToString("F2") : "N/A")}s " +
            $"| state={currentState} round={currentRound} timerRunning={timerRunning} timerExpired={timerExpired} remain={remaining:F1} " +
            $"| migrationStage={_migrationRestoreStage} restoreInProgress={IsMigrationRestoreInProgress} setupUI={_hasCompletedGameUISetup} uiDone={IsMigrationUiRestoreCompleted} " +
            $"| {BuildMigrationPlayerSnapshot()}" +
            $"{(string.IsNullOrEmpty(extra) ? string.Empty : $" | {extra}")}");
    }

    private void ResetMigrationOneShotGuards()
    {
        _migrationReadyEventPublished = false;
        _migrationPublishedStateEvents.Clear();
        _migrationPrepareShopRecoveryKeys.Clear();
        _migrationBattleRebootstrapKeys.Clear();
    }

    private void TriggerMigrationReadyEventOnce(string context)
    {
        if (_migrationReadyEventPublished)
        {
            LogMigrationTrace("MigrationReadyEvent:SKIP_DUPLICATE", $"context={context}");
            return;
        }

        _migrationReadyEventPublished = true;
        GameEvents.TriggerGameManagersReady();
        LogMigrationTrace("MigrationReadyEvent:FIRED", $"context={context}");
    }

    private void TriggerMigrationStateChangedOnce(GameState state, string context)
    {
        int key = (int)state;
        if (!_migrationPublishedStateEvents.Add(key))
        {
            LogMigrationTrace("MigrationStateChanged:SKIP_DUPLICATE", $"state={state}, context={context}");
            return;
        }

        GameEvents.TriggerGameStateChanged(state);
        LogMigrationTrace("MigrationStateChanged:FIRED", $"state={state}, context={context}");
    }

    private bool TryAcquirePrepareShopRecoveryKey(PlayerManager player, string context, out string key)
    {
        key = string.Empty;
        if (player == null || currentState != GameState.Prepare)
        {
            return false;
        }

        int playerId = TryGetPlayerIdSafe(player, out int safePlayerId) ? safePlayerId : -1;
        key = $"{_activeMigrationTraceId}:{playerId}:{currentRound}:{currentState}";
        bool acquired = _migrationPrepareShopRecoveryKeys.Add(key);
        LogMigrationTrace("PrepareShopRecoveryKey", $"context={context}, key={key}, acquired={acquired}");
        return acquired;
    }

    private bool TryAcquireBattleRebootstrapKey(PlayerManager attacker, PlayerManager defender, string context, out string key)
    {
        key = string.Empty;
        if (attacker == null || defender == null)
        {
            return false;
        }

        if (currentState != GameState.Battle1 && currentState != GameState.Battle2)
        {
            return false;
        }

        int attackerId = TryGetPlayerIdSafe(attacker, out int safeAttackerId) ? safeAttackerId : -1;
        int defenderId = TryGetPlayerIdSafe(defender, out int safeDefenderId) ? safeDefenderId : -1;
        key = $"{_activeMigrationTraceId}:{currentRound}:{currentState}:A{attackerId}:D{defenderId}";

        bool acquired = _migrationBattleRebootstrapKeys.Add(key);
        LogMigrationTrace("BattleRebootstrapKey", $"context={context}, key={key}, acquired={acquired}");
        return acquired;
    }

    public bool IsPrepareInteractionReadyForField(PlayerManager fieldOwner, out string reason)
    {
        reason = string.Empty;

        if (fieldOwner == null)
        {
            reason = "fieldOwner=null";
            return false;
        }

        if (currentState != GameState.Prepare)
        {
            return true;
        }

        if (!IsBoundToActiveRunner())
        {
            reason = "runnerMismatch";
            return false;
        }

        bool fieldOwnerHasInputAuthority =
            fieldOwner.Object != null &&
            fieldOwner.Object.IsValid &&
            fieldOwner.Object.HasInputAuthority;

        if (localPlayer == null ||
            localPlayer.Object == null ||
            !localPlayer.Object.IsValid)
        {
            RelinkLocalPlayer();
        }

        bool localHasInputAuthority =
            localPlayer != null &&
            localPlayer.Object != null &&
            localPlayer.Object.IsValid &&
            localPlayer.Object.HasInputAuthority;

        if (!localHasInputAuthority && !fieldOwnerHasInputAuthority)
        {
            reason = "localInputAuthority=false";
            return false;
        }

        bool ownerMatchesLocal = fieldOwnerHasInputAuthority ||
                                 localPlayer == null ||
                                 fieldOwner == localPlayer;
        if (!ownerMatchesLocal)
        {
            int ownerId = TryGetPlayerIdSafe(fieldOwner, out int safeOwnerId) ? safeOwnerId : -1;
            int localId = TryGetPlayerIdSafe(localPlayer, out int safeLocalId) ? safeLocalId : -1;
            reason = $"fieldOwnerMismatch(owner={ownerId},local={localId})";
            return false;
        }

        if (IsMigrationRestoreInProgress && !IsMigrationUiRestoreCompleted)
        {
            reason = $"migrationStage={_migrationRestoreStage}";
            return false;
        }

        if (localPlayerShopUI != null &&
            !localPlayerShopUI.IsContentVisible() &&
            localPlayerShopUI.IsRootRaycastBlocking())
        {
            localPlayerShopUI.InitializeAndHide();
            LogMigrationTrace("PrepareInteractionGate:FIX_HIDDEN_SHOP_RAYCAST");
        }

        return true;
    }

    /// <summary>
    /// LoadManager 초기화 완료 후 게임 흐름을 시작합니다.
    /// </summary>
    private async UniTask InitializeAndStartGame()
    {
        if (LoadManager.Instance == null)
        {
            Debug.LogError("[GameManagers] LoadManager.Instance is null.");
            return;
        }

        await LoadManager.Instance.InitializeAsync();
        if (Object == null || !Object.IsValid || Instance != this)
        {
            return;
        }

        await GameFlow();
        if (Object == null || !Object.IsValid || Instance != this)
        {
            return;
        }

        _isSpawned = true;
        RelinkLocalPlayer();
        RebuildNetworkPlayersAfterMigration("InitializeAndStartGame");
        // 모든 설정이 끝난 후, 준비 완료 이벤트를 발생시킵니다.
        GameEvents.TriggerGameManagersReady();
    }

    /// <summary>
    /// Fusion의 네트워크/물리 틱마다 호출됩니다. 게임 로직 처리에 적합합니다.
    /// </summary>
    // Host Migration 디버깅용 - StateAuthority 상태 추적
    private async UniTask WaitForPlayerInitializationAsync()
    {
        if (Runner == null || !Runner.IsServer)
        {
            return;
        }

        const float timeoutSeconds = 5f;
        float startTime = Time.realtimeSinceStartup;

        while (Time.realtimeSinceStartup - startTime < timeoutSeconds)
        {
            if (Object == null || !Object.IsValid || !Object.HasStateAuthority)
            {
                return;
            }

            var players = AllPlayers.Where(p => p != null).ToList();
            if (players.Count > 0 && players.All(p => p.IsReadyForPlayerActions))
            {
                return;
            }

            await UniTask.Delay(100);
        }

        var pending = AllPlayers
            .Where(p => p != null && !p.IsReadyForPlayerActions)
            .Select(p => $"P{p.playerId}")
            .ToArray();

        if (pending.Length > 0)
        {
            Debug.LogWarning($"[StartNextRound] Player initialization timeout: {string.Join(", ", pending)}");
        }
    }

    private float _lastStateAuthorityLogTime = 0f;
    private bool _wasStateAuthorityLastFrame = false;
    
    public override void FixedUpdateNetwork()
    {
        bool hasAuth = Object.HasStateAuthority;
        
        // StateAuthority 상태 변경 감지
        if (hasAuth != _wasStateAuthorityLastFrame)
        {
            // Debug.Log($"<color=magenta>[GameManagers.FixedUpdateNetwork] StateAuthority 변경: {_wasStateAuthorityLastFrame} → {hasAuth}</color>");
            _wasStateAuthorityLastFrame = hasAuth;
        }
        
        // 5초마다 상태 로깅 (Host Migration 디버깅용)
        if (Time.time - _lastStateAuthorityLogTime > 5f)
        {
            _lastStateAuthorityLogTime = Time.time;
            // Debug.Log($"[GameManagers.FixedUpdateNetwork] 주기적 상태 - StateAuth: {hasAuth}, State: {currentState}, Round: {currentRound}, Timer: {currentPhaseTimer:F1}s");
        }
        
        if (!hasAuth) return;

        if (IsMigrationRestoreInProgress && currentState == GameState.Prepare && phaseTimer.IsRunning && !IsMigrationUiRestoreCompleted)
        {
            float remain = phaseTimer.RemainingTime(Runner) ?? 0f;
            if (remain <= 2f && !_migrationWarnedPrepareExpiryRace)
            {
                _migrationWarnedPrepareExpiryRace = true;
                Debug.LogWarning($"[HM-TRACE #{_activeMigrationTraceId}] Prepare 타이머({remain:F1}s)가 UI 복원 완료 전 만료될 위험이 있습니다.");
                LogMigrationTrace("FixedUpdateNetwork:PrepareRaceWarning");
            }
        }

        if (phaseTimer.Expired(Runner))
        {
            if (IsMigrationRestoreInProgress && !IsMigrationUiRestoreCompleted && currentState == GameState.Prepare)
            {
                Debug.LogError($"[HM-TRACE #{_activeMigrationTraceId}] Prepare 타이머 만료 시점에도 UI 복원이 완료되지 않았습니다.");
                LogMigrationTrace("FixedUpdateNetwork:PrepareExpiredBeforeUI");
            }

            phaseTimer = TickTimer.None;
            // Debug.Log($"<color=yellow>[GameManagers] 타이머 만료! 상태: {currentState}</color>");
            switch (currentState)
            {
                case GameState.Prepare:
                    StartBattle1Phase();
                    break;
                case GameState.Battle1:
                    StartBattle2Phase();
                    break;
                case GameState.Battle2:
                    if (!isTransitioningRound)
                    {
                        isTransitioningRound = true;
                        RunLifecycleTask(StartNextRound(), "FixedUpdateNetwork/StartNextRound");
                    }
                    break;
            }
        }
        // 전투 단축: 모든 플레이어의 전투가 끝났을 때 남은 시간을 3초로
        // 전투 시작 후 1초 딜레이가 끝난 후부터 체크
        else if ((currentState == GameState.Battle1 || currentState == GameState.Battle2) && 
                 !hasCombatBeenShortened && 
                 _battleStartCheckDelay.Expired(Runner))
        {
            bool allFinished = true;
            foreach (var player in AllPlayers)
            {
                if (player == null) continue;
                
                // 개별 플레이어의 전투 상태 체크 및 업데이트
                bool playerFinished = IsPlayerBattleFinished(player);
                
                // 개별 플레이어 상태 업데이트 (전투 중 → 전투 종료)
                if (playerFinished && player.IsActivelyFighting)
                {
                    player.SetFightingState(false);
                    // Debug.Log($"[빠른진행 체크] Player {player.playerId}: 전투 종료 → 방패");
                    
                    // 상대 플레이어도 함께 전투 종료 처리 (공격자-수비자 페어 동기화)
                    int opponentId = GetBattleOpponent(player.playerId);
                    if (opponentId != -1)
                    {
                        var opponent = GetPlayer(opponentId);
                        if (opponent != null && opponent.IsActivelyFighting)
                        {
                            // 상대의 전투도 종료됐는지 확인
                            bool opponentFinished = IsPlayerBattleFinished(opponent);
                            if (opponentFinished)
                            {
                                opponent.SetFightingState(false);
                                // Debug.Log($"[빠른진행 체크] Player {opponent.playerId}: 상대 전투 종료로 함께 방패");
                            }
                        }
                    }
                }
                
                if (!playerFinished)
                {
                    allFinished = false;
                }
            }
            
            if (allFinished && phaseTimer.RemainingTime(Runner) > 3f)
            {
                phaseTimer = TickTimer.CreateFromSeconds(Runner, 3f);
                hasCombatBeenShortened = true;
                // Debug.Log("<color=cyan>[GameManagers] 모든 플레이어 전투 종료 - 빠른 진행 (3초)</color>");
            }
        }
        
        // 폭주 모드: Battle1 또는 Battle2에서 5초 남았을 때 트리거
        if (currentState == GameState.Battle1 && !_hasBerserkTriggered)
        {
            float remaining = phaseTimer.RemainingTime(Runner) ?? 0f;
            if (remaining <= berserkTriggerTime && remaining > 0f)
            {
                _hasBerserkTriggered = true;
                TriggerBerserkMode();
            }
        }
        else if (currentState == GameState.Battle2 && !_hasBerserkTriggeredBattle2)
        {
            float remaining = phaseTimer.RemainingTime(Runner) ?? 0f;
            if (remaining <= berserkTriggerTime && remaining > 0f)
            {
                _hasBerserkTriggeredBattle2 = true;
                TriggerBerserkMode();
            }
        }
        
        // 각 플레이어의 전투 상태(IsActivelyFighting) 업데이트는 빠른 진행 체크에서 수행
        // (매 틱 호출하면 상태가 불안정해짐)
    }

    /// <summary>
    /// 해당 플레이어의 전투가 끝났는지 확인합니다.
    /// - 공격자: 풀이 비어있고 모든 수비자 필드에 몬스터가 없으면 종료
    /// - 수비자: 자기 필드에 몬스터가 없고 공격자의 풀도 비었으면 종료
    /// </summary>
    private bool IsPlayerBattleFinished(PlayerManager player)
    {
        if (player == null) return true;
        
        if (player.IsAttackerInCurrentBattle)
        {
            // 공격자: 풀이 비어있고 모든 수비자 필드에 몬스터가 없으면 종료
            bool hasPool = player.AttackMonsterPool != null && 
                           player.AttackMonsterPool.Exists(p => !p.IsEmpty);
            bool anyDefenderHasMonsters = AllPlayers.Any(p => 
                p != null && 
                !p.IsAttackerInCurrentBattle && 
                p.monsterSpawner != null && 
                p.monsterSpawner.HasLivingMonsters());
            
            return !hasPool && !anyDefenderHasMonsters;
        }
        else
        {
            // 수비자: 자기 필드에 몬스터가 없고, 상대 공격자의 풀도 비었으면 종료
            bool hasMonsters = player.monsterSpawner != null && 
                               player.monsterSpawner.HasLivingMonsters();
            
            // 공격자의 풀에 몬스터가 남아있으면 아직 전투 중
            bool anyAttackerHasPool = AllPlayers.Any(p => 
                p != null && 
                p.IsAttackerInCurrentBattle && 
                p.AttackMonsterPool != null && 
                p.AttackMonsterPool.Exists(e => !e.IsEmpty));
            
            return !hasMonsters && !anyAttackerHasPool;
        }
    }
    
    /// <summary>
    /// 현재 전투에서 해당 공격자의 상대 수비자를 찾습니다.
    /// (수비자 필드에 이 공격자가 소환한 몬스터가 있는 플레이어를 찾음)
    /// </summary>
    private PlayerManager GetOpponentForPlayer(PlayerManager attacker)
    {
        if (!attacker.IsAttackerInCurrentBattle) return null;
        
        // 수비자 역할인 플레이어 중 자기 필드에 몬스터가 있는 플레이어 반환
        // (자기 자신이 아닌 다른 플레이어)
        foreach (var player in AllPlayers)
        {
            if (player == null || player == attacker) continue;
            
            // 수비자이고 (공격자가 아님) 필드에 몬스터가 있으면 상대
            if (!player.IsAttackerInCurrentBattle && 
                player.monsterSpawner != null && 
                player.monsterSpawner.HasLivingMonsters())
            {
                return player;
            }
        }
        return null;
    }

    /// <summary>
    /// 매 프레임 호출됩니다. 시각적 요소나 입력 처리, 그리고 변경 감지에 사용됩니다.
    /// </summary>
    public override void Render()
    {
        foreach (var propertyName in _changeDetector.DetectChanges(this))
        {
            if (propertyName == nameof(currentState))
            {
                HandleNetworkStateChange(currentState);
            }
        }

        if (CommandProcessor != null)
        {
            if (IsMigrationRestoreInProgress)
            {
                if (Time.realtimeSinceStartup - _lastMigrationCommandHoldLogRealtime > 1f)
                {
                    _lastMigrationCommandHoldLogRealtime = Time.realtimeSinceStartup;
                    Debug.Log($"[HM-TRACE #{_activeMigrationTraceId}] CommandProcessor 보류: stage={_migrationRestoreStage}");
                }
            }
            else
            {
                CommandProcessor.ProcessCommands();
            }
        }
    }

    /// <summary>
    /// 게임 시작 및 설정 플로우입니다. Host와 Client 모두 실행되며, 내부에서 역할을 분기합니다.
    /// </summary>
    private async UniTask GameFlow()
    {
        // Networked 속성은 StateAuthority(서버)만 설정 가능
        if (Object == null || !Object.IsValid || Instance != this)
        {
            return;
        }

        if (Object != null && Object.IsValid && Object.HasStateAuthority)
        {
            TransitionToSetupState("GameFlow.Initialize");
        }
        
        // 프리팹 로드
        if (AddressablesManager.Instance == null)
        {
            Debug.LogError("[GameManagers] AddressablesManager.Instance is null.");
            return;
        }
        await AddressablesManager.Instance.LoadGamePrefabsAsync();
        if (Object == null || !Object.IsValid || Instance != this)
        {
            return;
        }
        
        // 플레이어/그리드 생성 (서버만 실행, 내부에서 Rpc_LinkSpawnedObjects 호출)
        await SetupPlayersAndGrids();
        if (Object == null || !Object.IsValid || Instance != this)
        {
            return;
        }

        // 첫 라운드 UI(증강/상점) 전에 로컬 플레이어 참조를 선반영한다.
        RelinkLocalPlayer();
        
        // UI 설정 및 데이터 로딩 (SetupGameUI에서 데이터 로딩까지 처리)
        await SetupGameUI();
        if (Object == null || !Object.IsValid || Instance != this)
        {
            return;
        }

        // 서버: 첫 라운드 시작 (Reroll은 StartNextRound에서 처리)
        if (Runner != null && Runner.IsServer)
        {
            await StartNextRound();
        }
    }

    private async UniTask SetupPlayersAndGrids()
    {
        if (!Runner.IsServer)
        {
            // Debug.LogWarning("[SetupPlayersAndGrids] 서버가 아니므로 플레이어 생성을 건너뜁니다.");
            return;
        }

        // 프리팹 유효성 검사 (루프 밖에서 1번만)
        var gridPrefab = AddressablesManager.Instance?.GridPrefab;
        var playerManagerPrefab = AddressablesManager.Instance?.PlayerManagerPrefab;
        
        if (gridPrefab == null || playerManagerPrefab == null)
        {
            // Debug.LogError("❌ 프리팹이 로드되지 않았습니다! AddressablesManager를 확인하세요.");
            return;
        }

        if (BuildDebugGUI.Instance != null) 
            BuildDebugGUI.Instance.Log("호스트가 플레이어와 그리드 생성을 시작합니다.");

        var playerRefs = Runner.ActivePlayers.ToList();
        int playersToCreate = DeterminePlayerCount();
        bool isSinglePlayer = Runner.GameMode == GameMode.Single;

        for (int i = 0; i < playersToCreate; i++)
        {
            Vector3 playerPosition = player1BasePosition + playerOffset * i;
            bool isAI = isSinglePlayer ? (i > 0) : (i >= playerRefs.Count);
            PlayerRef inputAuthority = (!isAI && i < playerRefs.Count) ? playerRefs[i] : PlayerRef.None;

            // Grid 스폰
            NetworkObject gridNO = await Runner.SpawnAsync(gridPrefab, playerPosition, Quaternion.identity);
            if (gridNO == null)
            {
                // Debug.LogError($"❌ Player {i}의 Grid 생성 실패!");
                continue;
            }

            // PlayerManager 스폰
            NetworkObject playerNO = await Runner.SpawnAsync(playerManagerPrefab, playerPosition, Quaternion.identity, inputAuthority);
            if (playerNO == null)
            {
                // Debug.LogError($"❌ Player {i}의 PlayerManager 생성 실패!");
                continue;
            }

            NetworkPlayers.Set(i, playerNO);
            playerNO.name = isAI ? $"Player {i + 1} (AI)" : $"Player {i + 1}";

            PlayerManager newPlayer = playerNO.GetComponent<PlayerManager>();
            if (newPlayer != null)
            {
                newPlayer.Rpc_InitializePlayer(i, gridNO);
            }

            if (isAI)
            {
                var aiController = playerNO.gameObject.AddComponent<AIPlayerController>();
                aiController.Initialize(newPlayer, this.CommandProcessor);
            }
        }

        // TODO : 추후 방향성에 따라서 수정(매칭관련)
        Rpc_LinkSpawnedObjects();
    }

    /// <summary>
    /// 플레이어 생성 수를 결정합니다.
    /// </summary>
    private int DeterminePlayerCount()
    {
        if (Runner.GameMode == GameMode.Single)
        {
            var initializer = FindObjectOfType<GameSceneInitializer>();
            if (initializer != null)
            {
                int count = Mathf.Min(initializer.singlePlayerCount, MAX_PLAYERS);
                singlePlayerModeCount = count;
                return count;
            }
            return singlePlayerModeCount > 0 ? Mathf.Min(singlePlayerModeCount, MAX_PLAYERS) : 2;
        }
        
        int sessionMaxPlayers = Runner.SessionInfo?.MaxPlayers ?? 2;
        return Mathf.Min(sessionMaxPlayers, MAX_PLAYERS);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void Rpc_LinkSpawnedObjects()
    {
        
        if (BuildDebugGUI.Instance != null) BuildDebugGUI.Instance.Log("생성된 네트워크 객체들을 연결하는 중...");

        // InputAuthority를 가진 플레이어를 찾아 로컬 플레이어로 설정
        localPlayer = AllPlayers.FirstOrDefault(p => p != null && p.Object != null && p.Object.HasInputAuthority);

        // 멀티플레이에서는 첫 번째 플레이어 폴백이 원격 플레이어 오인을 만들 수 있으므로 금지.
        // 싱글플레이에서만 마지막 폴백으로 허용한다.
        if (localPlayer == null && Runner != null && Runner.GameMode == GameMode.Single && AllPlayers.Any())
        {
            localPlayer = AllPlayers.First(p => p != null);
        }

        var allPlayersList = AllPlayers.ToList();
        if (allPlayersList.Count == 2)
        {
            allPlayersList[0].opponentManager = allPlayersList[1];
            allPlayersList[1].opponentManager = allPlayersList[0];
        }
        
        if (BuildDebugGUI.Instance != null) BuildDebugGUI.Instance.Log("객체 연결 완료");

        // 싱글플레이 모드에서 singlePlayerModeCount가 설정되지 않았다면 기본값으로 설정
        if (Runner.GameMode == GameMode.Single && singlePlayerModeCount <= 0)
        {
            // 싱글플레이 모드에서는 실제 플레이어 수를 기반으로 singlePlayerModeCount 설정
            singlePlayerModeCount = allPlayersList.Count;
        }

        // 플레이어 데이터가 모두 준비되었을 때 발생시키는 이벤트 호출
        OnPlayersDataReady?.Invoke();
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_BroadcastCommandToClients(CommandType type, int[] intParams, string[] stringParams, Vector3[] vectorParams)
    {
        string who = Object.HasStateAuthority ? "Server" : "Client";
        if (CommandProcessor != null)
        {
            CommandProcessor.ReceiveAndEnqueueCommand(type, intParams, stringParams, vectorParams);
        }
    }

    #region Notification Helper Methods (Command Pattern 기반)
    /// <summary>
    /// 구매 성공을 모든 클라이언트에 알립니다.
    /// </summary>
    public void NotifyPurchaseSucceeded(int playerID, int slotIndex)
    {
        var cmd = new NotifyPurchaseSucceededCommand(playerID, slotIndex);
        CommandProcessor.RequestCommandExecution(cmd);
    }

    /// <summary>
    /// 증강 선택을 모든 클라이언트에 알립니다.
    /// </summary>
    public void NotifyAugmentSelected(int playerID, string augmentName)
    {
        var cmd = new NotifyAugmentSelectedCommand(playerID, augmentName);
        CommandProcessor.RequestCommandExecution(cmd);
    }

    /// <summary>
    /// 벽 배치 성공을 모든 클라이언트에 알립니다.
    /// </summary>
    public void NotifyWallPlacementSucceeded(int playerID, int x, int y)
    {
        var cmd = new NotifyWallPlacementCommand(playerID, x, y);
        CommandProcessor.RequestCommandExecution(cmd);
    }

    /// <summary>
    /// 벽 제거 성공을 모든 클라이언트에 알립니다.
    /// </summary>
    public void NotifyWallRemovalSucceeded(int playerID, int x, int y)
    {
        var cmd = new NotifyWallRemovalCommand(playerID, x, y);
        CommandProcessor.RequestCommandExecution(cmd);
    }
    #endregion

    #region Legacy RPC Methods (Deprecated - Command Pattern으로 마이그레이션 권장)
    [System.Obsolete("Use NotifyPurchaseSucceeded() instead. This RPC will be removed in future versions.")]
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_NotifyPurchaseSucceeded(int playerID, int slotIndex)
    {
        GameEvents.TriggerUnitPurchaseSucceeded(playerID, default(ShopItem), slotIndex);
    }

    [System.Obsolete("Use NotifyAugmentSelected() instead. This RPC will be removed in future versions.")]
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_NotifyAugmentSelected(int playerID, string augmentName)
    {
        var player = GetPlayer(playerID);
        if (player != null)
        {
            var augments = player.augmentManager?.GetPresentedAugments();
            AugmentData chosenAugment = augments?.FirstOrDefault(a => a?.augmentName == augmentName);
            
            if (chosenAugment != null)
            {
                GameEvents.TriggerAugmentApplied(player, chosenAugment);
                // Debug.Log($"<color=green>[RPC_NotifyAugmentSelected] Player {playerID}: '{augmentName}' 선택 알림</color>");
            }
        }
    }

    [System.Obsolete("Use NotifyWallPlacementSucceeded() instead. This RPC will be removed in future versions.")]
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_NotifyWallPlacementSucceeded(int playerID, int x, int y)
    {
        var pos = new Vector3Int(x, y, 0);
        GameEvents.TriggerWallPlacementSucceeded(playerID, pos);
    }

    [System.Obsolete("Use NotifyWallRemovalSucceeded() instead. This RPC will be removed in future versions.")]
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_NotifyWallRemovalSucceeded(int playerID, int x, int y)
    {
        var pos = new Vector3Int(x, y, 0);
        GameEvents.TriggerWallRemovalSucceeded(playerID, pos);
    }
    
    /// <summary>
    /// 전투 시작을 모든 클라이언트에 알립니다.
    /// 각 클라이언트는 자신이 해당 플레이어인 경우 카메라/UI 처리를 수행합니다.
    /// </summary>
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_NotifyBattleStart(int playerId, bool isAttacker, int opponentId)
    {
        // 로컬 플레이어가 아니면 무시
        if (!TryGetPlayerIdSafe(localPlayer, out int localPlayerId) || localPlayerId != playerId) return;

        localPlayer.monsterSpawner?.EnsureRuntimeReferencesForMigration("RPC_NotifyBattleStart(local)");
        
        // 공격자인 경우
        if (isAttacker && opponentId != -1)
        {
            var opponent = GetPlayer(opponentId);
            if (opponent != null)
            {
                // opponentManager 설정 (클라이언트에서도 보스 풀 추가를 위해 필요)
                localPlayer.opponentManager = opponent;
                
                // 클라이언트에서도 AttackMonsterPool 갱신 (UI 표시를 위해)
                localPlayer.RefreshAttackMonsterPool(currentRound, opponentId);
                
                // AttackSequenceManager 시작
                var attackSeqMgr = localPlayer.GetComponent<AttackSequenceManager>();
                if (attackSeqMgr == null)
                {
                    attackSeqMgr = localPlayer.gameObject.AddComponent<AttackSequenceManager>();
                    // Debug.LogWarning($"[RPC_NotifyBattleStart] AttackSequenceManager 누락으로 동적 생성: Player {playerId}");
                }

                if (attackSeqMgr.Owner != localPlayer)
                {
                    attackSeqMgr.Initialize(localPlayer);
                    // Debug.Log($"[RPC_NotifyBattleStart] AttackSequenceManager 재초기화: Player {playerId}");
                }

                if (attackSeqMgr != null)
                {
                    attackSeqMgr.StartAttackSequence(opponent);
                }
                
                // 카메라를 상대 필드로 이동 (공격 모드)
                if (CameraManager.Instance != null)
                {
                    RunLifecycleTask(
                        CameraManager.Instance.MoveToPlayerField(opponent, isAttackMode: true),
                        "RPC_NotifyBattleStart/MoveToPlayerField");
                }
                
                // 공격 시퀀스 UI 표시 (재초기화 후 표시)
                RunLifecycleTask(ShowAttackSequenceUIAsync(attackSeqMgr), "RPC_NotifyBattleStart/ShowAttackSequenceUI");
                
                // Debug.Log($"<color=green>[RPC_NotifyBattleStart] 로컬 Player {playerId}: 공격자 (상대: Player {opponentId}, 라운드: {currentRound})</color>");
            }
        }
        else
        {
            // 수비자인 경우
            // AttackSequenceManager 종료
            var attackSeqMgr = localPlayer.GetComponent<AttackSequenceManager>();
            if (attackSeqMgr != null)
            {
                attackSeqMgr.EndAttackSequence();
            }
            
            // 공격 시퀀스 UI 숨김
            AttackSequenceUIController.Instance?.Hide();
            
            // 카메라 본인 필드 복귀
            if (CameraManager.Instance != null)
            {
                CameraManager.Instance.ReturnToOwnField();
            }
            
            // Debug.Log($"<color=blue>[RPC_NotifyBattleStart] 로컬 Player {playerId}: 수비자</color>");
        }
        
        // 전투 시작 이벤트 발생
        GameEvents.TriggerBattleSequenceStarted(isAttacker);
    }
    
    /// <summary>
    /// 클라이언트가 서버에 몬스터 소환을 요청합니다.
    /// </summary>
    /// <param name="attackerPlayerId">공격자 플레이어 ID</param>
    /// <param name="defenderPlayerId">수비자 플레이어 ID</param>
    /// <param name="monsterDataName">소환할 몬스터 데이터 이름</param>
    /// <param name="spawnPosition">소환 위치</param>
    /// <param name="isBoss">보스 여부</param>
    /// <param name="bossUniqueId">보스 고유 ID</param>
    /// <param name="originPlayerId">보스 소환자 ID</param>
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_RequestSpawnMonster(int attackerPlayerId, int defenderPlayerId, string monsterDataName, Vector3 spawnPosition, bool isBoss, int bossUniqueId, int originPlayerId)
    {
        // 서버만 처리
        if (Object == null || !Object.HasStateAuthority) return;
        
        var attacker = GetPlayer(attackerPlayerId);
        var defender = GetPlayer(defenderPlayerId);
        
        if (attacker == null || defender == null)
        {
            // Debug.LogWarning($"[RPC_RequestSpawnMonster] 플레이어를 찾을 수 없음: attacker={attackerPlayerId}, defender={defenderPlayerId}");
            return;
        }
        
        // 몬스터 데이터 찾기
        var pool = attacker.AttackMonsterPool;
        MonsterPoolEntry targetEntry = null;
        foreach (var entry in pool)
        {
            if (entry.MonsterData != null && entry.MonsterData.name == monsterDataName && !entry.IsEmpty)
            {
                targetEntry = entry;
                break;
            }
        }
        
        if (targetEntry == null)
        {
            // Debug.LogWarning($"[RPC_RequestSpawnMonster] 몬스터 풀에서 '{monsterDataName}'을 찾을 수 없음");
            return;
        }
        
        // 서버에서 몬스터 소환
        RunLifecycleTask(
            SpawnMonsterOnServerAsync(attacker, defender, targetEntry, spawnPosition),
            "RPC_RequestSpawnMonster/SpawnMonsterOnServerAsync");
    }
    
    private async UniTask SpawnMonsterOnServerAsync(PlayerManager attacker, PlayerManager defender, MonsterPoolEntry entry, Vector3 spawnPosition)
    {
        if (attacker?.monsterSpawner == null || defender?.fieldManager == null) return;
        
        var monster = await attacker.monsterSpawner.SpawnMonsterAtPositionAsync(
            entry.MonsterData,
            spawnPosition,
            defender.fieldManager,
            entry.IsBoss,
            entry.BossUniqueId,
            entry.OriginPlayerId
        );
        
        if (monster != null)
        {
            if (!attacker.TryConsumeMonsterFromPool(entry.MonsterData))
            {
                // Debug.LogWarning($"[RPC_RequestSpawnMonster] 소환 성공 후 풀 소비 실패: '{entry.MonsterData.monsterName}'");
            }
            // Debug.Log($"<color=green>[RPC_RequestSpawnMonster] 몬스터 '{entry.MonsterData.monsterName}' 소환 성공</color>");
        }
        else
        {
            // Debug.LogWarning($"[RPC_RequestSpawnMonster] 몬스터 '{entry.MonsterData.monsterName}' 소환 실패");
        }
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_RequestUseMagicScroll(int attackerPlayerId, string scrollDataName, Vector3 position, RpcInfo info = default)
    {
        if (Object == null || !Object.HasStateAuthority) return;
        if (currentState != GameState.Battle1 && currentState != GameState.Battle2) return;
        if (string.IsNullOrWhiteSpace(scrollDataName)) return;

        var attacker = GetPlayer(attackerPlayerId);
        if (attacker == null) 
        {
            return;
        }

        if (!IsRpcSourceAuthorizedForPlayer(attacker, info.Source)) return;
        if (!attacker.IsAttackerInCurrentBattle) return;
        if (!TryGetBattleDefenderField(attacker, out FieldManager defenderField)) return;
        if (!IsWithinFieldOuterBounds(defenderField, position)) return;

        MagicScrollData targetScroll = null;
        foreach (var scroll in attacker.OwnedScrolls)
        {
            if (scroll != null && scroll.name == scrollDataName)
            {
                targetScroll = scroll;
                break;
            }
        }

        if (targetScroll == null)
        {
            return;
        }

        if (!attacker.TryConsumeMagicScroll(targetScroll))
        {
            return;
        }

        RPC_BroadcastMagicScrollUsed(attackerPlayerId, scrollDataName, position);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_BroadcastMagicScrollUsed(int attackerPlayerId, string scrollDataName, Vector3 position)
    {
        RunLifecycleTask(
            CreateScrollCasterLocal(attackerPlayerId, scrollDataName, position),
            "RPC_BroadcastMagicScrollUsed/CreateScrollCasterLocal");

        GameEvents.TriggerMagicScrollUsed(attackerPlayerId, scrollDataName, position);
    }

    private async UniTask CreateScrollCasterLocal(int attackerPlayerId, string scrollDataName, Vector3 position)
    {
        var scrollData = await AssetLoader.LoadAssetAsync<MagicScrollData>(scrollDataName);
        if (scrollData == null || scrollData.skillData == null)
        {
            return;
        }

        var casterGO = new GameObject($"ScrollCaster_{attackerPlayerId}_{scrollDataName}");
        casterGO.transform.position = position;

        var caster = casterGO.AddComponent<ScrollCaster>();
        caster.Initialize();
        caster.CastSkill(scrollData.skillData);
    }
    #endregion

    private async UniTask StartNextRound()
    {
        if (Object == null || !Object.IsValid || !Object.HasStateAuthority) return;
        if (currentState == GameState.GameOver) return;

        // 첫 Prepare 진입 시점(local UI 표시 이전)에 로컬 플레이어 참조를 보강한다.
        await WaitForPlayerInitializationAsync();
        if (Object == null || !Object.IsValid || !Object.HasStateAuthority) return;

        RelinkLocalPlayer();
        Debug.Log($"[StartNextRound] Begin round transition. state={currentState}, round={currentRound}, {BuildRoundTransitionSnapshot()}");
        if (localPlayer == null)
        {
            Debug.LogWarning("[StartNextRound] localPlayer could not be resolved before Prepare sync.");
        }
        
        // 턴 시작 시 보스 침공 상태 리셋 (턴당 1회 침공 제한용)
        if (CommandProcessor == null)
        {
            Debug.LogWarning("[StartNextRound] CommandProcessor is null.");
            return;
        }

        if (SurvivorBossManager.Instance != null)
        {
            SurvivorBossManager.Instance.ResetTurnInvasionState();
        }

        // 전투 종료 시 필드에 남은 몬스터 정리 (라운드 증가 전에 처리)
        foreach (var player in AllPlayers)
        {
            if (player?.monsterSpawner != null)
            {
                player.monsterSpawner.OnCombatPhaseEnded();
            }
            
            // AttackSequenceManager 종료
            var attackSeqMgr = player?.GetComponent<AttackSequenceManager>();
            if (attackSeqMgr != null)
            {
                attackSeqMgr.EndAttackSequence();
            }
        }

        // 로컬 플레이어 카메라 본인 필드로 복귀
        if (CameraManager.Instance != null)
        {
            CameraManager.Instance.ReturnToOwnField();
        }

        // --- 라운드 종료 시 탈락 판정 ---
        var eliminatedPlayers = CheckEliminatedPlayers();
        if (eliminatedPlayers.Count > 0)
        {
            foreach (var eliminated in eliminatedPlayers)
            {
                // Debug.Log($"<color=red>[GameManagers] Player {eliminated.playerId} 탈락! (체력: {eliminated.GetHealth()})</color>");
                GameOver(eliminated);
            }
            
            // 생존자가 1명 이하면 게임 종료
            var survivors = AllPlayers.Where(p => p != null && !eliminatedPlayers.Contains(p) && p.GetHealth() > 0).ToList();
            if (survivors.Count <= 1)
            {
                return; // GameOver에서 처리됨
            }
        }

        // 라운드 증가 (첫 라운드는 1로 설정)
        if (currentState != GameState.Setup)
        {
            currentRound++;
        }
        else
        {
            currentRound = 1;
        }

        TryPushMigrationSnapshotForCriticalTransition($"StartNextRound:BeforePrepareTransition:R{currentRound}");
        TransitionToPrepareState("StartNextRound");

        foreach (var player in AllPlayers)
        {
            if (player == null) continue;
            if (!player.IsReadyForPlayerActions)
            {
                Debug.LogWarning($"[StartNextRound] Skip sync for uninitialized player. playerId={player.playerId}");
                continue;
            }

            if (player.shopManager == null)
            {
                player.shopManager = player.GetComponentInChildren<ShopManager>(true);
                if (player.shopManager != null)
                {
                    player.shopManager.playerManager = player;
                }
            }

            if (player.augmentManager == null)
            {
                player.augmentManager = player.GetComponentInChildren<AugmentManager>(true);
                if (player.augmentManager != null)
                {
                    player.augmentManager.playerManager = player;
                }
            }
            
            // 골드 지급 및 상점 리롤
            player.AddGold(baseGoldPerRound + GetInterest(player.GetGold()));
            if (player.shopManager != null)
            {
                player.shopManager.Reroll(true);
            }
            else
            {
                // Debug.LogWarning($"[StartNextRound] Player {player.playerId}: shopManager가 null이라 리롤을 건너뜁니다.");
            }

            // 상점 아이템 동기화 (Command Pattern 사용)
            var shopItems = player.shopManager != null ? player.shopManager.GetCurrentShopItems() : new List<ShopItem>();
            string[] shopNames = shopItems.Select(i => i.UnitData?.name ?? "").ToArray();
            int[] shopStars = shopItems.Select(i => i.StarLevel).ToArray();
            var syncShopCmd = new SyncShopItemsCommand(player.playerId, shopNames, shopStars);
            CommandProcessor.RequestCommandExecution(syncShopCmd);

            // AI 준비 단계 플래그 리셋
            player.mazeConstructionComplete = false;
            player.unitPurchaseComplete = false;

            // 스폰/도착 지점은 이제 PlayerManager.SetupSpawnAndGoalPositions에서 초기화 시 고정 설정됨
            // (도착: 필드 정 가운데, 스폰: 동서남북 테두리 구멍 중 랜덤)
            
            // 증강 생성 및 동기화 (한 루프에서 처리)
            List<AugmentData> presentedAugments = new List<AugmentData>();
            if (player.augmentManager != null)
            {
                if (!player.augmentManager.IsDataLoaded)
                {
                    await player.augmentManager.LoadAllAugmentsAsync();
                }

                if (!player.augmentManager.IsDataLoaded)
                {
                    // Debug.LogWarning($"[StartNextRound] Player {player.playerId}: 증강 데이터 로딩 실패/미완료");
                    continue;
                }

                player.augmentManager.PresentAugments();
                presentedAugments = player.augmentManager.GetPresentedAugments() ?? new List<AugmentData>();
            }
            else
            {
                // Debug.LogWarning($"[StartNextRound] Player {player.playerId}: augmentManager가 null입니다. 빈 증강 목록으로 동기화합니다.");
            }
            
            var augmentNames = presentedAugments
                .Select(a => a != null ? a.augmentName : string.Empty)
                .ToArray();
            
            var syncAugmentCmd = new SyncAugmentsCommand(player.playerId, augmentNames);
            CommandProcessor.RequestCommandExecution(syncAugmentCmd);
        }

        // UI 로직이 완료될 때까지 대기
        await HandleUIForNewState(currentState);

        float prepDuration = (!firstPrepareDurationUsed && currentRound == 1) ? firstPreparePhaseTime : preparePhaseTime;
        firstPrepareDurationUsed = true;
        phaseTimer = TickTimer.CreateFromSeconds(Runner, prepDuration);
        Debug.Log($"[StartNextRound] Prepare phase armed. round={currentRound}, prepDuration={prepDuration:F1}, {BuildRoundTransitionSnapshot()}");
        isTransitioningRound = false; // 라운드 전환 완료
    }

    private bool TryRunBattleStartPrecheck(string context, out string reason)
    {
        bool hasAuthority = Object != null && Object.HasStateAuthority;
        bool runnerMatched = IsBoundToActiveRunner();
        bool uiReady = !IsMigrationRestoreInProgress || IsMigrationUiRestoreCompleted;
        bool playersReady = AreAllPlayersRuntimeReadyForMigration(out string playersReason);
        bool mappingReady = IsMigrationBattleMappingReady(out string mappingReason);
        bool wallMapReady = AreWallMapsReadyForMigration(out string wallReason);
        bool aiTakeoverReady = IsMigrationAiTakeoverReady(out string aiReason);
        bool spawnerReady = AreBattleSpawnerTargetsReady(out string spawnerReason);

        if (hasAuthority && runnerMatched && uiReady && playersReady && mappingReady && wallMapReady && aiTakeoverReady && spawnerReady)
        {
            reason = string.Empty;
            return true;
        }

        reason =
            $"authority={hasAuthority},runnerMatched={runnerMatched},uiReady={uiReady},playersReady={playersReady},mappingReady={mappingReady},wallMapReady={wallMapReady},aiTakeoverReady={aiTakeoverReady},spawnerReady={spawnerReady}" +
            $" | playersReason={playersReason},mappingReason={mappingReason},wallReason={wallReason},aiReason={aiReason},spawnerReason={spawnerReason}";
        return false;
    }

    private bool AreBattleSpawnerTargetsReady(out string reason)
    {
        reason = string.Empty;
        var players = AllPlayers
            .Where(player => player != null && player.Object != null && player.Object.IsValid)
            .ToList();
        if (players.Count == 0)
        {
            reason = "players=0";
            return false;
        }

        foreach (var player in players)
        {
            if (!TryGetPlayerIdSafe(player, out int playerId) || playerId < 0)
            {
                continue;
            }

            if (player.monsterSpawner == null)
            {
                reason = $"P{playerId}:monsterSpawner=null";
                return false;
            }

            if (!player.monsterSpawner.IsRuntimeReady(out string spawnerReason))
            {
                reason = $"P{playerId}:spawnerNotReady({spawnerReason})";
                return false;
            }

            int opponentId = GetBattleOpponent(playerId);
            if (opponentId < 0)
            {
                continue;
            }

            var opponent = GetPlayer(opponentId);
            if (opponent == null)
            {
                reason = $"P{playerId}:opponentNull({opponentId})";
                return false;
            }

            if (opponent.fieldManager == null || opponent.astarGrid == null || opponent.goalTransform == null)
            {
                reason = $"P{playerId}:opponentRuntimeNotReady({opponentId})";
                return false;
            }
        }

        return true;
    }

    private void RearmBattleTransitionRetryTimer(string context, string reason)
    {
        if (Runner == null)
        {
            return;
        }

        const float retrySeconds = 0.75f;
        phaseTimer = TickTimer.CreateFromSeconds(Runner, retrySeconds);
        Debug.LogWarning($"[{context}] BattleStartPrecheck failed. retryIn={retrySeconds:F2}s, detail={reason}");
        LogMigrationTrace($"{context}:BattleStartPrecheckRetry", reason);
    }

    #region 전투 시퀀스 메서드

    /// <summary>
    /// Battle1 시퀀스를 시작합니다. 선공 플레이어가 공격, 상대가 수비.
    /// </summary>
    private void StartBattle1Phase()
    {
        if (!Object.HasStateAuthority) return;
        if (currentState == GameState.GameOver) return;

        LogMigrationTrace("StartBattle1Phase:ENTER");

        // 증강을 선택하지 않은 플레이어에게 첫 번째 증강 자동 선택
        foreach (var player in AllPlayers)
        {
            if (player == null) continue;
            
            var presentedAugments = player.augmentManager?.GetPresentedAugments();
            if (presentedAugments != null && presentedAugments.Count > 0)
            {
                var firstAugment = presentedAugments[0];
                // Debug.Log($"<color=orange>[StartBattle1Phase] Player {player.playerId}: 시간 초과로 인해 '{firstAugment.augmentName}' 증강 자동 선택</color>");
                
                player.augmentManager.SelectAndApplyAugment(firstAugment);
                NotifyAugmentSelected(player.playerId, firstAugment.augmentName);
            }
        }

        // 상대 매칭 및 선공 플레이어 결정
        AssignBattleOpponents();

        if (!TryRunBattleStartPrecheck("StartBattle1Phase", out string precheckReason))
        {
            RearmBattleTransitionRetryTimer("StartBattle1Phase", precheckReason);
            return;
        }

        TryPushMigrationSnapshotForCriticalTransition($"StartBattle1Phase:BeforeBattle1Transition:R{currentRound}");
        TransitionToBattle1State("StartBattle1Phase");
        hasCombatBeenShortened = false;
        _hasBerserkTriggered = false;
        _battleStartCheckDelay = TickTimer.CreateFromSeconds(Runner, 1f); // 1초 딜레이


        // 생존 보스 타겟 할당
        if (SurvivorBossManager.Instance != null)
        {
            SurvivorBossManager.Instance.AssignTargetsToSurvivors();
        }

        // Battle1: 선공자가 공격, 후공자가 수비 (수비자 필드에 몬스터 스폰)
        StartBattleForPlayers(isFirstBattle: true);

        phaseTimer = TickTimer.CreateFromSeconds(Runner, combatTime);
        // Debug.Log($"<color=cyan>[GameManagers] Battle1 시작! 각 매칭마다 선공자 랜덤 결정됨</color>");
    }

    /// <summary>
    /// Battle2 시퀀스를 시작합니다. 공수 역할 교체.
    /// </summary>
    private void StartBattle2Phase()
    {
        if (!Object.HasStateAuthority) return;
        if (currentState == GameState.GameOver) return;

        LogMigrationTrace("StartBattle2Phase:ENTER");
        EnsureBattleMappingAfterMigration();

        if (!TryRunBattleStartPrecheck("StartBattle2Phase", out string precheckReason))
        {
            RearmBattleTransitionRetryTimer("StartBattle2Phase", precheckReason);
            return;
        }

        // Battle1에서 남은 몬스터 정리
        foreach (var player in AllPlayers)
        {
            if (player?.monsterSpawner != null)
            {
                player.monsterSpawner.OnCombatPhaseEnded();
            }
        }

        TryPushMigrationSnapshotForCriticalTransition($"StartBattle2Phase:BeforeBattle2Transition:R{currentRound}");
        TransitionToBattle2State("StartBattle2Phase");
        hasCombatBeenShortened = false;
        _hasBerserkTriggeredBattle2 = false;
        _battleStartCheckDelay = TickTimer.CreateFromSeconds(Runner, 1f); // 1초 딜레이


        // Battle2: 공수 역할 교체 (후공자가 공격, 선공자가 수비)
        StartBattleForPlayers(isFirstBattle: false);

        phaseTimer = TickTimer.CreateFromSeconds(Runner, combatTime);
        // Debug.Log("<color=cyan>[GameManagers] Battle2 시작! 공수 역할 교체</color>");
    }

    /// <summary>
    /// 라운드별 상대 매칭을 수행합니다.
    /// 2명 플레이어: 서로 상대
    /// 3명+ 플레이어: 2명씩 페어링, 홀수인 경우 1명은 상대 없음
    /// 각 매칭마다 독립적으로 선공자를 랜덤 결정하여 공정성 보장
    /// </summary>
    private void AssignBattleOpponents()
    {
        _battleOpponents.Clear();
        _matchFirstAttacker.Clear();
        
        var alivePlayers = AllPlayers.Where(p => p != null && p.GetHealth() > 0).ToList();
        
        if (alivePlayers.Count == 0) return;

        // 랜덤 셔플 (Fisher-Yates)
        for (int i = alivePlayers.Count - 1; i > 0; i--)
        {
            int j = UnityEngine.Random.Range(0, i + 1);
            var temp = alivePlayers[i];
            alivePlayers[i] = alivePlayers[j];
            alivePlayers[j] = temp;
        }

        // 2명씩 매칭하고, 각 매칭마다 선공자 랜덤 결정
        for (int i = 0; i < alivePlayers.Count; i += 2)
        {
            if (i + 1 < alivePlayers.Count)
            {
                var player1 = alivePlayers[i];
                var player2 = alivePlayers[i + 1];
                
                // 양쪽 서로 상대로 지정
                _battleOpponents[player1.playerId] = player2.playerId;
                _battleOpponents[player2.playerId] = player1.playerId;
                
                // 이 매칭의 선공자를 50% 확률로 랜덤 결정
                bool player1IsFirstAttacker = UnityEngine.Random.value > 0.5f;
                int matchFirstAttackerId = player1IsFirstAttacker ? player1.playerId : player2.playerId;
                
                // 양쪽 플레이어에게 이 매칭의 선공자 ID 저장
                _matchFirstAttacker[player1.playerId] = matchFirstAttackerId;
                _matchFirstAttacker[player2.playerId] = matchFirstAttackerId;
                
                // [호환성] 첫 번째 매칭의 선공자를 FirstAttackerPlayerId에 저장 (디버그 로그용)
                if (i == 0)
                {
                    FirstAttackerPlayerId = matchFirstAttackerId;
                }
                
                // Debug.Log($"<color=yellow>[AssignBattleOpponents] 매칭: P{player1.playerId} vs P{player2.playerId}, 선공자: P{matchFirstAttackerId}</color>");
            }
            else
            {
                // 홀수: 마지막 사람은 상대 없음
                _battleOpponents[alivePlayers[i].playerId] = -1;
                _matchFirstAttacker[alivePlayers[i].playerId] = -1; // 상대 없으면 선공자도 없음
                
                // Debug.Log($"<color=gray>[AssignBattleOpponents] P{alivePlayers[i].playerId}: 상대 없음 (혼자)</color>");
            }
        }

        // Debug.Log($"<color=yellow>[AssignBattleOpponents] 매칭 완료: {string.Join(", ", _battleOpponents.Select(kv => $"P{kv.Key}↔P{kv.Value}"))}</color>");
    }

    /// <summary>
    /// 전투를 시작합니다. 공격자는 수동 소환 대기, 수비자는 웨이브 자동 스폰.
    /// </summary>
    /// <param name="isFirstBattle">true면 Battle1 (매칭별 선공자 공격), false면 Battle2 (매칭별 후공자 공격)</param>
    private void StartBattleForPlayers(bool isFirstBattle)
    {
        LogMigrationTrace("StartBattleForPlayers:ENTER", $"isFirstBattle={isFirstBattle}");
        EnsureBattleMappingAfterMigration();

        var battleReadyMap = new Dictionary<int, bool>();
        foreach (var player in AllPlayers)
        {
            if (player == null || player.Object == null || !player.Object.IsValid) continue;
            if (!TryGetPlayerIdSafe(player, out int playerId) || playerId < 0) continue;

            player.RebindRuntimeReferencesAfterMigration($"StartBattleForPlayers(Player {playerId})", false);
            bool ready = player.IsRuntimeReady(out string readyReason);
            battleReadyMap[playerId] = ready;
            if (!ready)
            {
                // Debug.LogError($"[StartBattleForPlayers] Player {playerId} 런타임 준비 미완료 - 전투 시작 스킵 (reason={readyReason})");
                player.SetFightingState(false);
            }
        }

        foreach (var player in AllPlayers)
        {
            if (player == null || player.Object == null || !player.Object.IsValid) continue;
            if (!TryGetPlayerIdSafe(player, out int playerId) || playerId < 0) continue;
            if (!battleReadyMap.TryGetValue(playerId, out bool playerReady) || !playerReady)
            {
                continue;
            }

            var battleAttackSeqMgr = player.GetComponent<AttackSequenceManager>();
            if (battleAttackSeqMgr == null)
            {
                battleAttackSeqMgr = player.gameObject.AddComponent<AttackSequenceManager>();
                battleAttackSeqMgr.Initialize(player);
                // Debug.LogWarning($"[StartBattleForPlayers] AttackSequenceManager 동적 생성: Player {playerId}");
            }
            else if (battleAttackSeqMgr.Owner != player)
            {
                battleAttackSeqMgr.Initialize(player);
                // Debug.Log($"[StartBattleForPlayers] AttackSequenceManager 재초기화: Player {playerId}");
            }

            int opponentId = _battleOpponents.TryGetValue(playerId, out int oppId) ? oppId : -1;
            bool hasOpponent = opponentId != -1;

            if (hasOpponent)
            {
                bool opponentReady = battleReadyMap.TryGetValue(opponentId, out bool value) && value;
                if (!opponentReady)
                {
                    // Debug.LogError($"[StartBattleForPlayers] 상대 Player {opponentId} 런타임 준비 미완료 - Player {playerId} 전투 시작 스킵");
                    player.SetFightingState(false);
                    continue;
                }
            }

            // 이 플레이어의 매칭에서 선공자가 누구인지 확인
            int matchFirstAttackerId = _matchFirstAttacker.TryGetValue(playerId, out int firstId) ? firstId : -1;
            
            // Battle1: 매칭별 선공자가 공격자
            // Battle2: 매칭별 선공자가 수비자 (역할 교체)
            bool isAttackerFirstBattle = playerId == matchFirstAttackerId;
            bool isAttackerInThisBattle = isFirstBattle ? isAttackerFirstBattle : !isAttackerFirstBattle;

            player.IsAttackerInCurrentBattle = isAttackerInThisBattle;

            if (hasOpponent)
            {
                if (isAttackerInThisBattle)
                {
                    // 공격자 역할: 기본 웨이브 + AttackMonsterPool 소환
                    try
                    {
                        player.RefreshAttackMonsterPool(currentRound, opponentId);
                    }
                    catch (System.Exception e)
                    {
                        // Debug.LogError($"[StartBattleForPlayers] Player {playerId} AttackMonsterPool 갱신 중 예외: {e.Message}");
                    }
                    player.SetFightingState(true);

                    var opponent = GetPlayer(opponentId);
                    bool isAI = ComponentRegistry.Has<AIPlayerController>(playerId.ToString());
                    
                    if (player.monsterSpawner != null && opponent?.fieldManager != null)
                    {
                        // [공격자가 모든 몬스터 소환] 기본 웨이브 + 증강체 몬스터
                        RunLifecycleTask(
                            player.monsterSpawner.SpawnAllMonstersToTargetField(currentRound, opponent.fieldManager, isAI),
                            "StartBattleForPlayers/SpawnAllMonstersToTargetField");
                        // Debug.Log($"<color=orange>[StartBattle] Player {playerId}: 공격자 - 수비자 {opponentId} 필드에 전체 웨이브 소환 (AI={isAI})</color>");
                    }
                }
                else
                {
                    // 수비자: 공격자가 소환할 때까지 대기
                    player.SetFightingState(true);
                    
                    // 생존 보스 소환 (이전 라운드에서 살아남은 보스가 이 플레이어에게 침공)
                    if (player.monsterSpawner != null)
                    {
                        RunLifecycleTask(
                            player.monsterSpawner.SpawnSurvivorBossesAsync(),
                            "StartBattleForPlayers/SpawnSurvivorBossesAsync");
                    }
                    
                    // 카메라/UI 처리는 RPC_NotifyBattleStart에서 각 클라이언트가 처리
                    // Debug.Log($"<color=blue>[StartBattle] Player {playerId}: 수비자 (상대: Player {opponentId})</color>");
                }
            }
            else
            {
                // 상대 없음: 수비 모드에서 기본 웨이브만 AI 자동 소환
                if (!isAttackerInThisBattle)
                {
                    // 수비 시퀀스: 기본 웨이브를 AI가 자동 소환 (증강 공격유닛 제외)
                    player.monsterSpawner.SpawnWaveWithoutAugments(currentRound);
                    // Debug.Log($"<color=gray>[StartBattle] Player {playerId}: 상대 없음, 수비 (기본 웨이브만)</color>");
                }
                else
                {
                    // 공격 시퀀스: 관전 모드 (전투 참여 안 함)
                    player.SetFightingState(false);
                    // Debug.Log($"<color=gray>[StartBattle] Player {playerId}: 상대 없음, 공격 (관전 모드)</color>");
                }
            }
        }

        // 모든 클라이언트에 전투 시작 알림 (RPC)
        foreach (var player in AllPlayers)
        {
            if (player == null || player.Object == null || !player.Object.IsValid) continue;
            if (!TryGetPlayerIdSafe(player, out int playerId) || playerId < 0) continue;
            if (!battleReadyMap.TryGetValue(playerId, out bool playerReady) || !playerReady)
            {
                continue;
            }

            int opponentId = _battleOpponents.TryGetValue(playerId, out int oppId) ? oppId : -1;
            if (opponentId != -1)
            {
                bool opponentReady = battleReadyMap.TryGetValue(opponentId, out bool value) && value;
                if (!opponentReady)
                {
                    continue;
                }
            }

            bool isAttackerFlag = false;
            try
            {
                isAttackerFlag = player.IsAttackerInCurrentBattle;
            }
            catch (System.InvalidOperationException)
            {
                // Spawned 이전 객체는 기본값(false)로 처리
            }

            RPC_NotifyBattleStart(playerId, isAttackerFlag, opponentId);
        }
    }

    #endregion

    /// <summary>
    /// 폭주 모드를 트리거합니다. (전투 종료 5초 전)
    /// 공격팀의 몬스터 + 수비팀의 유닛에 공격속도/공격력 1.5배, 이동속도 2배 적용
    /// [중요] 공격팀이 소환한 몬스터는 수비팀의 필드(monsterParent)에 존재하므로,
    ///        수비팀의 monsterSpawner에서 버프를 적용해야 합니다.
    /// </summary>
    private void TriggerBerserkMode()
    {
        // Debug.Log("<color=red>[GameManagers] ⚡ 폭주 모드 발동! (남은 시간: 5초)</color>");
        
        foreach (var player in AllPlayers)
        {
            if (player == null) continue;
            if (!player.IsActivelyFighting) continue;  // 전투 중인 플레이어만
            
            bool isDefender = !player.IsAttackerInCurrentBattle;
            
            if (isDefender)
            {
                // 수비팀: 자신 필드의 몬스터(공격팀이 소환) + 유닛 모두에 버프 적용
                player.monsterSpawner?.ApplyBerserkModeToAllMonsters();
                player.fieldManager?.ApplyBerserkModeToAllUnits();
                // Debug.Log($"<color=red>[TriggerBerserkMode] Player {player.playerId}: 수비팀 - 몬스터+유닛 버서커 버프</color>");
            }
            // 공격팀은 자신의 필드에 전투가 없으므로 버프 적용 불필요
        }
    }

    private List<PlayerManager> CheckEliminatedPlayers()
    {
        var eliminated = new List<PlayerManager>();
        var allAlivePlayers = AllPlayers.Where(p => p != null).ToList();
        var playersAtOrBelowZero = allAlivePlayers
            .Where(p => p.GetHealth() <= 0)
            .ToList();

        if (playersAtOrBelowZero.Count == 0)
        {
            return eliminated;
        }

        var survivors = allAlivePlayers.Where(p => p.GetHealth() > 0).ToList();
        
        if (survivors.Count > 0)
        {
            eliminated.AddRange(playersAtOrBelowZero);
        }
        else
        {
            // 전멸 상황: 체력 최고인 플레이어만 생존, 나머지 탈락
            int maxHealth = playersAtOrBelowZero.Max(p => p.GetHealth());
            var winner = playersAtOrBelowZero.First(p => p.GetHealth() == maxHealth);
            
            foreach (var player in playersAtOrBelowZero)
            {
                if (player != winner)
                {
                    eliminated.Add(player);
                }
            }
        }
        
        return eliminated;
    }

    /// <summary>
    /// 현재 게임 상태를 반환합니다. Spawned 상태가 아니면 Setup을 반환합니다.
    /// </summary>
    public GameState GetGameState()
    {
        // Host Migration 중이거나 Spawned 되지 않은 경우 안전하게 기본값 반환
        if (!IsReadyForNetworkAccess)
        {
            return GameState.Setup;
        }
        return currentState;
    }
    public void OnMonsterReachedGoal(PlayerManager failedPlayer)
    {
        if (Runner.IsServer)
        {
             if (currentState == GameState.GameOver) return;
             failedPlayer.TakeDamage(1);
        }
    }

    public void GameOver(PlayerManager loser)
    {
        if (!Object.HasStateAuthority) return;
        if (currentState == GameState.GameOver) return;

        var alivePlayers = AllPlayers.Where(p => p != null && p.GetHealth() > 0).ToList();
        if (alivePlayers.Count <= 1)
        {
            TransitionToGameOverState("GameOver");
            phaseTimer = TickTimer.None;

            PlayerManager winner = alivePlayers.FirstOrDefault();
            
            // 안전한 씬 전환을 위해 비동기로 처리 (UI 표시 후 딜레이)
            RunLifecycleTask(SafeSceneTransitionAsync(), "GameOver/SafeSceneTransitionAsync");
        }
    }
    
    /// <summary>
    /// 게임 종료 후 안전하게 씬을 전환합니다.
    /// 승리/패배 UI를 표시하고 일정 시간 후 MatchingLobby로 이동합니다.
    /// </summary>
    private async UniTask SafeSceneTransitionAsync()
    {
        // 승리/패배 UI가 표시될 시간을 줌 (3초 대기)
        await UniTask.Delay(3000);
        
        // 씬 전환 전 UIManagers 비활성화 (네트워크 프로퍼티 접근 에러 방지)
        if (UIManagers.Instance != null)
        {
            UIManagers.Instance.gameObject.SetActive(false);
        }
        
        // 게임 종료 후 MatchingLobby 씬으로 전환
        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.LeaveAndLoad("MatchingLobby");
        }
        else
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene("MatchingLobby");
        }
    }

    private int GetInterest(int gold) => Mathf.Min(gold / 10, maxInterest);
    public List<PlayerManager> GetRankedPlayers() => AllPlayers.Where(p => p != null).OrderByDescending(p => p.GetHealth()).ThenBy(p => p.gameObject.name).ToList();

    #region 로비 관련 함수
    public void SetMaxQueueSize(int count) => maxqueue = count;
    public int GetMaxSize() => maxqueue;
    public int CurrentQueueSize() => characterselectdata.Count;
    public string GetCharacterName(int count) => characterselectdata.ElementAtOrDefault(count);
    public string GetSelectCharacterName(int i) => selectCharacterName.ElementAtOrDefault(i);
    public void Pushqueue(string name)
    {
        if (characterselectdata.Count >= maxqueue)
        {
            characterselectdata.Dequeue();
            selectCharacterName.RemoveAt(0);
        }
        characterselectdata.Enqueue(name);
        selectCharacterName.Add(name);
    }
    #endregion
}
