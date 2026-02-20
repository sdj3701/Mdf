using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using UnityEngine.UI;
using Cysharp.Threading.Tasks;
using Fusion;
using System.Threading.Tasks;

// MonoBehaviour 대신 NetworkBehaviour를 상속받아 네트워크 객체로 만듭니다.
public class GameManagers : NetworkBehaviour
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

    public IEnumerable<PlayerManager> AllPlayers
    {
        get
        {
            if (NetworkPlayers.Length == 0) yield break;
            foreach (var playerNO in NetworkPlayers)
            {
                if (playerNO == null || !playerNO.IsValid)
                {
                    continue;
                }

                if (playerNO.TryGetComponent<PlayerManager>(out var playerManager)
                    && playerManager != null
                    && playerManager.Object != null
                    && playerManager.Object.IsValid)
                {
                    yield return playerManager;
                }
            }
        }
    }

    private static bool IsPlayerReadable(PlayerManager player)
    {
        return player != null
            && player.Object != null
            && player.Object.IsValid;
    }

    private static bool TryGetPlayerIdSafe(PlayerManager player, out int playerId)
    {
        playerId = -1;
        if (!IsPlayerReadable(player))
        {
            return false;
        }

        try
        {
            playerId = player.playerId;
            return true;
        }
        catch (System.InvalidOperationException)
        {
            return false;
        }
    }
    #endregion

    // 프리팹은 AddressablesManager에서 관리

    // AddressablesManager에서 캐시된 프리팹 접근
    public GameObject defaultMonsterPrefab => AddressablesManager.Instance?.DefaultMonsterPrefab;

    [Header("자동 생성 위치 설정")]
    public Vector3 player1BasePosition = new Vector3(0, 0, 0);
    public Vector3 playerOffset = new Vector3(0, 10, 0);

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
    private bool _migrationRestoreInProgress;
    private bool _migrationUiRestoreCompleted;
    private bool _migrationSetupUiCompleted;
    private bool _migrationWarnedPrepareExpiryRace;
    private float _migrationRestoreStartedRealtime;
    private int _migrationRestoreStartFrame = -1;
    private bool _migrationTimerPaused;
    private float _migrationPausedTimerRemainingSeconds;
    private bool _isSpawned;
    
    /// <summary>
    /// Host Migration 중 또는 Spawned 전에는 Networked 속성에 접근할 수 없습니다.
    /// 이 프로퍼티로 안전하게 체크해서 접근하세요.
    /// </summary>
    public bool IsReadyForNetworkAccess => Object != null && Object.IsValid && _isSpawned;

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
                Debug.LogWarning($"<color=orange>[GameManagers] 동일 Runner의 중복 인스턴스 감지 - 현재 인스턴스 제거\n  existing={BuildDebugSummary(Instance)}\n  current={BuildDebugSummary(this)}</color>");
                Runner.Despawn(Object);
                return;
            }

            Debug.LogWarning($"<color=yellow>[GameManagers] 다른 Runner의 기존 Instance 감지 - 새 Runner 인스턴스로 교체\n  existing={BuildDebugSummary(Instance)}\n  current={BuildDebugSummary(this)}</color>");
        }

        Instance = this;
        Debug.Log($"[GameManagers.Spawned] static Instance 재설정 완료: {BuildDebugSummary(Instance)}");

        if (CommandProcessor == null)
        {
            CommandProcessor = new CommandProcessor();
            Debug.Log("[GameManagers.Spawned] CommandProcessor 생성");
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
                LoadManager.Instance.InitializeAsync().Forget();
            }
            _isSpawned = true;
            GameEvents.TriggerGameManagersReady();
            return;
        }

        // 일반 시작 경로
        InitializeAndStartGame().Forget();
    }

    private void OnDestroy()
    {
        bool wasStaticInstance = Instance == this;
        bool isMigrating = HostMigrationHandler.Instance != null && HostMigrationHandler.Instance.IsMigrating;
        Debug.LogWarning($"<color=orange>[GameManagers.OnDestroy] 파괴됨: {BuildDebugSummary(this)} | wasStaticInstance={wasStaticInstance} | isMigrating={isMigrating}</color>");

        if (Instance == this)
        {
            Instance = null;
            Debug.LogWarning("[GameManagers.OnDestroy] static Instance를 null로 정리");
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
            $"| restoreInProgress={_migrationRestoreInProgress} setupUI={_migrationSetupUiCompleted} uiDone={_migrationUiRestoreCompleted} " +
            $"| {BuildMigrationPlayerSnapshot()}" +
            $"{(string.IsNullOrEmpty(extra) ? string.Empty : $" | {extra}")}");
    }

    /// <summary>
    /// LoadManager 초기화 완료 후 게임 흐름을 시작합니다.
    /// </summary>
    private async UniTask InitializeAndStartGame()
    {
        await LoadManager.Instance.InitializeAsync();
        await GameFlow();

        _isSpawned = true;
        // 모든 설정이 끝난 후, 준비 완료 이벤트를 발생시킵니다.
        GameEvents.TriggerGameManagersReady();
    }

    /// <summary>
    /// Fusion의 네트워크/물리 틱마다 호출됩니다. 게임 로직 처리에 적합합니다.
    /// </summary>
    // Host Migration 디버깅용 - StateAuthority 상태 추적
    private float _lastStateAuthorityLogTime = 0f;
    private bool _wasStateAuthorityLastFrame = false;
    
    public override void FixedUpdateNetwork()
    {
        bool hasAuth = Object.HasStateAuthority;
        
        // StateAuthority 상태 변경 감지
        if (hasAuth != _wasStateAuthorityLastFrame)
        {
            Debug.Log($"<color=magenta>[GameManagers.FixedUpdateNetwork] StateAuthority 변경: {_wasStateAuthorityLastFrame} → {hasAuth}</color>");
            _wasStateAuthorityLastFrame = hasAuth;
        }
        
        // 5초마다 상태 로깅 (Host Migration 디버깅용)
        if (Time.time - _lastStateAuthorityLogTime > 5f)
        {
            _lastStateAuthorityLogTime = Time.time;
            Debug.Log($"[GameManagers.FixedUpdateNetwork] 주기적 상태 - StateAuth: {hasAuth}, State: {currentState}, Round: {currentRound}, Timer: {currentPhaseTimer:F1}s");
        }
        
        if (!hasAuth) return;

        if (_migrationRestoreInProgress && currentState == GameState.Prepare && phaseTimer.IsRunning && !_migrationUiRestoreCompleted)
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
            if (_migrationRestoreInProgress && !_migrationUiRestoreCompleted && currentState == GameState.Prepare)
            {
                Debug.LogError($"[HM-TRACE #{_activeMigrationTraceId}] Prepare 타이머 만료 시점에도 UI 복원이 완료되지 않았습니다.");
                LogMigrationTrace("FixedUpdateNetwork:PrepareExpiredBeforeUI");
            }

            phaseTimer = TickTimer.None;
            Debug.Log($"<color=yellow>[GameManagers] 타이머 만료! 상태: {currentState}</color>");
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
                        StartNextRound().Forget();
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
                    Debug.Log($"[빠른진행 체크] Player {player.playerId}: 전투 종료 → 방패");
                    
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
                                Debug.Log($"[빠른진행 체크] Player {opponent.playerId}: 상대 전투 종료로 함께 방패");
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
                Debug.Log("<color=cyan>[GameManagers] 모든 플레이어 전투 종료 - 빠른 진행 (3초)</color>");
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
            CommandProcessor.ProcessCommands();
        }
    }

    private void OnEnable()
    {
        GameEvents.OnAugmentApplied += HandleAugmentChosen;
    }

    private void OnDisable()
    {
        GameEvents.OnAugmentApplied -= HandleAugmentChosen;
    }

    // [새로 추가] 네트워크 상태가 변경될 때 모든 클라이언트에서 반응하는 함수 (Render에서 호출됨)
    private void HandleNetworkStateChange(GameState newState)
    {
        // UI 초기화가 완료되기 전에는 처리하지 않음
        if (!_isSpawned) return;
        
        // 로컬 플레이어의 UI만 업데이트해야 하므로, 로컬 플레이어 확인 후 비동기 UI 로직 호출
        if (localPlayer == null)
        {
            RelinkLocalPlayer();
            if (localPlayer == null) return;
        }

        // UI 업데이트 및 이벤트 발송은 UniTask의 'Fire-and-Forget' 패턴으로 처리
        // Render()는 async/await을 할 수 없습니다.
        GameEvents.TriggerGameStateChanged(newState);
        HandleUIForNewState(newState).Forget();
    }

    /// <summary>
    /// 게임 시작 및 설정 플로우입니다. Host와 Client 모두 실행되며, 내부에서 역할을 분기합니다.
    /// </summary>
    private async UniTask GameFlow()
    {
        // Networked 속성은 StateAuthority(서버)만 설정 가능
        if (Object.HasStateAuthority)
        {
            currentState = GameState.Setup;
        }
        
        // 프리팹 로드
        await AddressablesManager.Instance.LoadGamePrefabsAsync();
        
        // 플레이어/그리드 생성 (서버만 실행, 내부에서 Rpc_LinkSpawnedObjects 호출)
        await SetupPlayersAndGrids();
        
        // UI 설정 및 데이터 로딩 (SetupGameUI에서 데이터 로딩까지 처리)
        await SetupGameUI();

        // 서버: 첫 라운드 시작 (Reroll은 StartNextRound에서 처리)
        if (Runner.IsServer)
        {
            await StartNextRound();
        }
    }

    private async UniTask SetupPlayersAndGrids()
    {
        if (!Runner.IsServer)
        {
            Debug.LogWarning("[SetupPlayersAndGrids] 서버가 아니므로 플레이어 생성을 건너뜁니다.");
            return;
        }

        // 프리팹 유효성 검사 (루프 밖에서 1번만)
        var gridPrefab = AddressablesManager.Instance?.GridPrefab;
        var playerManagerPrefab = AddressablesManager.Instance?.PlayerManagerPrefab;
        
        if (gridPrefab == null || playerManagerPrefab == null)
        {
            Debug.LogError("❌ 프리팹이 로드되지 않았습니다! AddressablesManager를 확인하세요.");
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
                Debug.LogError($"❌ Player {i}의 Grid 생성 실패!");
                continue;
            }

            // PlayerManager 스폰
            NetworkObject playerNO = await Runner.SpawnAsync(playerManagerPrefab, playerPosition, Quaternion.identity, inputAuthority);
            if (playerNO == null)
            {
                Debug.LogError($"❌ Player {i}의 PlayerManager 생성 실패!");
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
        localPlayer = AllPlayers.FirstOrDefault(p => p != null && p.Object.HasInputAuthority);

        // 싱글플레이 모드에서는 InputAuthority가 없을 수 있으므로, 첫 번째 플레이어를 로컬 플레이어로 설정
        if (localPlayer == null && AllPlayers.Any())
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
                Debug.Log($"<color=green>[RPC_NotifyAugmentSelected] Player {playerID}: '{augmentName}' 선택 알림</color>");
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
                    Debug.LogWarning($"[RPC_NotifyBattleStart] AttackSequenceManager 누락으로 동적 생성: Player {playerId}");
                }

                if (attackSeqMgr.Owner != localPlayer)
                {
                    attackSeqMgr.Initialize(localPlayer);
                    Debug.Log($"[RPC_NotifyBattleStart] AttackSequenceManager 재초기화: Player {playerId}");
                }

                if (attackSeqMgr != null)
                {
                    attackSeqMgr.StartAttackSequence(opponent);
                }
                
                // 카메라를 상대 필드로 이동 (공격 모드)
                if (CameraManager.Instance != null)
                {
                    CameraManager.Instance.MoveToPlayerField(opponent, isAttackMode: true).Forget();
                }
                
                // 공격 시퀀스 UI 표시 (재초기화 후 표시)
                ShowAttackSequenceUIAsync(attackSeqMgr).Forget();
                
                Debug.Log($"<color=green>[RPC_NotifyBattleStart] 로컬 Player {playerId}: 공격자 (상대: Player {opponentId}, 라운드: {currentRound})</color>");
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
            
            Debug.Log($"<color=blue>[RPC_NotifyBattleStart] 로컬 Player {playerId}: 수비자</color>");
        }
        
        // 전투 시작 이벤트 발생
        GameEvents.TriggerBattleSequenceStarted(isAttacker);
    }
    
    /// <summary>
    /// 공격 시퀀스 UI를 비동기로 초기화하고 표시합니다.
    /// </summary>
    private async UniTask ShowAttackSequenceUIAsync(AttackSequenceManager attackSeqMgr)
    {
        if (localPlayer == null || attackSeqMgr == null) return;
        
        // UI 로드/초기화
        var ui = await AttackSequenceUIController.GetOrCreateAsync(localPlayer, attackSeqMgr);
        if (ui != null)
        {
            ui.Show(true);
            Debug.Log($"<color=cyan>[ShowAttackSequenceUIAsync] 공격 시퀀스 UI 표시 완료</color>");
        }
        else
        {
            Debug.LogWarning("[ShowAttackSequenceUIAsync] UI 로드 실패");
        }
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
            Debug.LogWarning($"[RPC_RequestSpawnMonster] 플레이어를 찾을 수 없음: attacker={attackerPlayerId}, defender={defenderPlayerId}");
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
            Debug.LogWarning($"[RPC_RequestSpawnMonster] 몬스터 풀에서 '{monsterDataName}'을 찾을 수 없음");
            return;
        }
        
        // 서버에서 몬스터 소환
        SpawnMonsterOnServerAsync(attacker, defender, targetEntry, spawnPosition).Forget();
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
                Debug.LogWarning($"[RPC_RequestSpawnMonster] 소환 성공 후 풀 소비 실패: '{entry.MonsterData.monsterName}'");
            }
            Debug.Log($"<color=green>[RPC_RequestSpawnMonster] 몬스터 '{entry.MonsterData.monsterName}' 소환 성공</color>");
        }
        else
        {
            Debug.LogWarning($"[RPC_RequestSpawnMonster] 몬스터 '{entry.MonsterData.monsterName}' 소환 실패");
        }
    }
    #endregion

    /// <summary>
    /// UI 요소를 로드하고 참조를 저장합니다. 상태 관리는 각 UIController가 담당합니다.
    /// </summary>
    private async UniTask SetupGameUI(bool forceRefresh = false)
    {
        LogMigrationTrace("SetupGameUI:ENTER", $"forceRefresh={forceRefresh}");

        if (UIManagers.Instance == null)
        {
            var resolvedUIManager = FindObjectOfType<UIManagers>(true);
            if (resolvedUIManager != null)
            {
                UIManagers.Instance = resolvedUIManager;
                if (!resolvedUIManager.gameObject.activeSelf)
                {
                    resolvedUIManager.gameObject.SetActive(true);
                }
                Debug.LogWarning("[SetupGameUI] UIManagers.Instance가 null이어서 FindObjectOfType로 복구했습니다.");
            }
        }

        if (UIManagers.Instance == null)
        {
            Debug.LogWarning("[SetupGameUI] UIManagers.Instance가 null입니다.");
            return;
        }

        if (_isSettingUpGameUI)
        {
            LogMigrationTrace("SetupGameUI:WAIT_OTHER_TASK");
            await UniTask.WaitUntil(() => !_isSettingUpGameUI);
            return;
        }

        if (_hasCompletedGameUISetup && !forceRefresh && localPlayerShopUI != null && augmentSelectionUI != null)
        {
            LogMigrationTrace("SetupGameUI:SKIP_ALREADY_DONE");
            return;
        }

        _isSettingUpGameUI = true;

        try
        {
            var shopPanelTask = UIManagers.Instance.GetUIElement("UI_Pnl_Shop");
            var augmentPanelTask = UIManagers.Instance.GetUIElement("UI_Pnl_Augment");
            var (shopPanelInstance, augmentPanelInstance) = await UniTask.WhenAll(shopPanelTask, augmentPanelTask);

            // 참조 저장 후 Controller의 초기화 메서드 호출
            if (shopPanelInstance != null)
            {
                localPlayerShopUI = shopPanelInstance.GetComponent<ShopUIController>();
                localPlayerShopUIGameObject = shopPanelInstance;
                if (localPlayerShopUI != null)
                {
                    localPlayerShopUI.InitializeAndHide();
                }
                else
                {
                    Debug.LogError("[SetupGameUI] UI_Pnl_Shop에 ShopUIController가 없습니다.");
                }
            }
            else
            {
                Debug.LogError("[SetupGameUI] UI_Pnl_Shop 로드 실패 (null)");
            }
            
            if (augmentPanelInstance != null)
            {
                augmentSelectionUI = augmentPanelInstance.GetComponent<AugmentUIController>();
                if (augmentSelectionUI != null)
                {
                    augmentSelectionUI.InitializeAndHide();
                }
                else
                {
                    Debug.LogError("[SetupGameUI] UI_Pnl_Augment에 AugmentUIController가 없습니다.");
                }
            }
            else
            {
                Debug.LogError("[SetupGameUI] UI_Pnl_Augment 로드 실패 (null)");
            }
            
            // 모든 플레이어의 상점/증강 데이터 로딩
            var playersSnapshot = AllPlayers?.Where(p => p != null).ToList() ?? new List<PlayerManager>();
            if (playersSnapshot.Count == 0)
            {
                playersSnapshot = FindObjectsOfType<PlayerManager>(true)
                    .Where(p => p != null && p.Object != null && p.Runner == Runner)
                    .ToList();
                Debug.LogWarning($"[SetupGameUI] AllPlayers 스냅샷이 비어 FindObjectsOfType 폴백 사용: count={playersSnapshot.Count}");
            }

            foreach (var player in playersSnapshot)
            {
                try
                {
                    if (player.shopManager == null)
                    {
                        player.shopManager = player.GetComponentInChildren<ShopManager>(true);
                    }

                    if (player.augmentManager == null)
                    {
                        player.augmentManager = player.GetComponentInChildren<AugmentManager>(true);
                    }

                    if (player.shopManager != null && player.shopManager.playerManager == null)
                    {
                        player.shopManager.playerManager = player;
                    }

                    if (player.augmentManager != null && player.augmentManager.playerManager == null)
                    {
                        player.augmentManager.playerManager = player;
                    }

                    // 증강 데이터 로딩 (명시적 호출 + 대기)
                    if (player.augmentManager != null && !player.augmentManager.IsDataLoaded)
                    {
                        await player.augmentManager.LoadAllAugmentsAsync();
                    }
                    
                    // 상점 데이터 로딩 (ShopManager.Start에서 이미 시작됨, 대기만)
                    if (player.shopManager != null)
                    {
                        await player.shopManager.WaitUntilDatabaseLoaded();
                    }

                    // Host Migration 복원 중 Prepare 단계에서 상점이 비어 있으면
                    // 새 Host가 즉시 무료 리롤 + 동기화하여 클라이언트 대기 타임아웃을 방지한다.
                    if (_migrationRestoreInProgress &&
                        currentState == GameState.Prepare &&
                        Object != null &&
                        Object.HasStateAuthority &&
                        player.shopManager != null)
                    {
                        var migratedShopItems = player.shopManager.GetCurrentShopItems();
                        if (migratedShopItems == null || migratedShopItems.Count == 0)
                        {
                            player.shopManager.Reroll(true);
                            migratedShopItems = player.shopManager.GetCurrentShopItems();

                            Debug.Log($"[복원/UI] Host 보정 리롤 실행: Player {player.playerId}, itemCount={migratedShopItems?.Count ?? 0}");

                            if (CommandProcessor != null && migratedShopItems != null && migratedShopItems.Count > 0)
                            {
                                string[] shopNames = migratedShopItems.Select(i => i.UnitData?.name ?? string.Empty).ToArray();
                                int[] shopStars = migratedShopItems.Select(i => i.StarLevel).ToArray();
                                var syncShopCmd = new SyncShopItemsCommand(player.playerId, shopNames, shopStars);
                                CommandProcessor.RequestCommandExecution(syncShopCmd);
                                Debug.Log($"[복원/UI] Host 상점 동기화 커맨드 전송: Player {player.playerId}, itemCount={migratedShopItems.Count}");
                            }
                        }
                    }

                    if (_activeMigrationTraceId >= 0)
                    {
                        int shopCount = player.shopManager != null ? player.shopManager.GetCurrentShopItems().Count : -1;
                        int augmentCount = player.augmentManager?.GetPresentedAugments()?.Count ?? -1;
                        Debug.Log($"[HM-TRACE #{_activeMigrationTraceId}] SetupGameUI:PLAYER_READY P{player.playerId} shopCount={shopCount} shopDbLoaded={(player.shopManager != null && player.shopManager.IsDatabaseLoaded)} augmentChoices={augmentCount} augmentLoaded={(player.augmentManager != null && player.augmentManager.IsDataLoaded)}");
                    }
                }
                catch (System.Exception playerEx)
                {
                    Debug.LogError($"[SetupGameUI] Player {(player != null ? player.playerId.ToString() : "null")} 처리 중 예외: {playerEx.Message}");
                    Debug.LogException(playerEx);
                }
            }
            
            _hasCompletedGameUISetup = localPlayerShopUI != null && augmentSelectionUI != null;
            _migrationSetupUiCompleted = _hasCompletedGameUISetup;
            Debug.Log("<color=green>[SetupGameUI] 모든 플레이어의 상점/증강 데이터 로딩 완료</color>");
            LogMigrationTrace("SetupGameUI:SUCCESS", $"hasCompleted={_hasCompletedGameUISetup}");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"UI 설정 중 심각한 에러 발생: {ex.Message}");
            Debug.LogException(ex);
            Debug.LogError($"[SetupGameUI] 상태 덤프: state={currentState}, round={currentRound}, localPlayer={(localPlayer != null ? localPlayer.playerId.ToString() : "null")}, runner={(Runner != null ? Runner.name : "null")}");
            if (BuildDebugGUI.Instance != null) BuildDebugGUI.Instance.Log("UI 설정 중 심각한 에러 발생");
            LogMigrationTrace("SetupGameUI:EXCEPTION", $"error={ex.Message}");
        }
        finally
        {
            _isSettingUpGameUI = false;
            LogMigrationTrace("SetupGameUI:EXIT");
        }
    }

    private async void HandleAugmentChosen(PlayerManager selectingPlayer, AugmentData chosenAugment)
    {
        if (selectingPlayer != localPlayer) return;

        Debug.Log($"<color=cyan>[HandleAugmentChosen] 증강 '{chosenAugment?.augmentName}' 선택됨 → 증강 UI 비활성화</color>");
        
        // 1. 증강 UI 비활성화 (부모 GameObject 비활성화) - 활성 상태일 때만
        if (UIManagers.Instance != null && UIManagers.Instance.IsUIElementActive("UI_Pnl_Augment"))
        {
            UIManagers.Instance.ReturnUIElement("UI_Pnl_Augment");
        }
        
        // 2. 상점 UI 활성화 - 준비 단계에서만 열도록 체크
        // [버그 수정] 플레이어가 잠수해서 증강이 자동 선택된 경우, 이미 전투 상태일 수 있음
        // 전투 중에는 상점 UI를 열지 않음
        if (currentState != GameState.Prepare)
        {
            Debug.Log($"<color=yellow>[HandleAugmentChosen] 현재 {currentState} 상태이므로 상점 UI를 열지 않음 (잠수 플레이어 자동 선택)</color>");
            return;
        }
        
        if (localPlayerShopUIGameObject != null && localPlayerShopUI != null)
        {
            Debug.Log($"<color=cyan>[HandleAugmentChosen] 상점 UI 활성화</color>");
            localPlayerShopUIGameObject.SetActive(true);  // 부모 GameObject 활성화
            localPlayerShopUI.SetContentVisibility(true);  // 콘텐츠 표시
            
            // 상점 UI를 표시하기 전에, 데이터베이스 로드를 기다리고 상점을 채우는 것을 보장합니다.
            await localPlayer.shopManager.EnsureShopRerolledAsync();
            var shopItems = localPlayer.shopManager.GetCurrentShopItems();
            localPlayerShopUI.DisplayShopItems(shopItems);
            
            Debug.Log($"<color=cyan>[HandleAugmentChosen] 상점 UI 표시 완료 (아이템 수: {shopItems?.Count ?? 0})</color>");
        }
        else
        {
            Debug.LogWarning("[HandleAugmentChosen] 상점 UI 참조가 null입니다!");
        }
    }

    private async UniTask StartNextRound()
    {
        if (!Object.HasStateAuthority) return;
        if (currentState == GameState.GameOver) return;
        
        // 턴 시작 시 보스 침공 상태 리셋 (턴당 1회 침공 제한용)
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
                Debug.Log($"<color=red>[GameManagers] Player {eliminated.playerId} 탈락! (체력: {eliminated.GetHealth()})</color>");
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

        currentState = GameState.Prepare;
        GameEvents.TriggerGameStateChanged(currentState);

        foreach (var player in AllPlayers)
        {
            if (player == null) continue;

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
                Debug.LogWarning($"[StartNextRound] Player {player.playerId}: shopManager가 null이라 리롤을 건너뜁니다.");
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
                    Debug.LogWarning($"[StartNextRound] Player {player.playerId}: 증강 데이터 로딩 실패/미완료");
                    continue;
                }

                player.augmentManager.PresentAugments();
                presentedAugments = player.augmentManager.GetPresentedAugments() ?? new List<AugmentData>();
            }
            else
            {
                Debug.LogWarning($"[StartNextRound] Player {player.playerId}: augmentManager가 null입니다. 빈 증강 목록으로 동기화합니다.");
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
        isTransitioningRound = false; // 라운드 전환 완료
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
                Debug.Log($"<color=orange>[StartBattle1Phase] Player {player.playerId}: 시간 초과로 인해 '{firstAugment.augmentName}' 증강 자동 선택</color>");
                
                player.augmentManager.SelectAndApplyAugment(firstAugment);
                NotifyAugmentSelected(player.playerId, firstAugment.augmentName);
            }
        }

        // 상대 매칭 및 선공 플레이어 결정
        AssignBattleOpponents();

        currentState = GameState.Battle1;
        hasCombatBeenShortened = false;
        _hasBerserkTriggered = false;
        _battleStartCheckDelay = TickTimer.CreateFromSeconds(Runner, 1f); // 1초 딜레이

        HandleUIForNewState(currentState).Forget();

        // 생존 보스 타겟 할당
        if (SurvivorBossManager.Instance != null)
        {
            SurvivorBossManager.Instance.AssignTargetsToSurvivors();
        }

        // Battle1: 선공자가 공격, 후공자가 수비 (수비자 필드에 몬스터 스폰)
        StartBattleForPlayers(isFirstBattle: true);

        phaseTimer = TickTimer.CreateFromSeconds(Runner, combatTime);
        Debug.Log($"<color=cyan>[GameManagers] Battle1 시작! 각 매칭마다 선공자 랜덤 결정됨</color>");
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

        // Battle1에서 남은 몬스터 정리
        foreach (var player in AllPlayers)
        {
            if (player?.monsterSpawner != null)
            {
                player.monsterSpawner.OnCombatPhaseEnded();
            }
        }

        currentState = GameState.Battle2;
        hasCombatBeenShortened = false;
        _hasBerserkTriggeredBattle2 = false;
        _battleStartCheckDelay = TickTimer.CreateFromSeconds(Runner, 1f); // 1초 딜레이

        HandleUIForNewState(currentState).Forget();

        // Battle2: 공수 역할 교체 (후공자가 공격, 선공자가 수비)
        StartBattleForPlayers(isFirstBattle: false);

        phaseTimer = TickTimer.CreateFromSeconds(Runner, combatTime);
        Debug.Log("<color=cyan>[GameManagers] Battle2 시작! 공수 역할 교체</color>");
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
                
                Debug.Log($"<color=yellow>[AssignBattleOpponents] 매칭: P{player1.playerId} vs P{player2.playerId}, 선공자: P{matchFirstAttackerId}</color>");
            }
            else
            {
                // 홀수: 마지막 사람은 상대 없음
                _battleOpponents[alivePlayers[i].playerId] = -1;
                _matchFirstAttacker[alivePlayers[i].playerId] = -1; // 상대 없으면 선공자도 없음
                
                Debug.Log($"<color=gray>[AssignBattleOpponents] P{alivePlayers[i].playerId}: 상대 없음 (혼자)</color>");
            }
        }

        Debug.Log($"<color=yellow>[AssignBattleOpponents] 매칭 완료: {string.Join(", ", _battleOpponents.Select(kv => $"P{kv.Key}↔P{kv.Value}"))}</color>");
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
            if (player == null || player.playerId < 0 || player.Object == null || !player.Object.IsValid) continue;

            player.RebindRuntimeReferencesAfterMigration($"StartBattleForPlayers(Player {player.playerId})", false);
            bool ready = player.IsRuntimeReady(out string readyReason);
            battleReadyMap[player.playerId] = ready;
            if (!ready)
            {
                Debug.LogError($"[StartBattleForPlayers] Player {player.playerId} 런타임 준비 미완료 - 전투 시작 스킵 (reason={readyReason})");
                player.SetFightingState(false);
            }
        }

        foreach (var player in AllPlayers)
        {
            if (player == null || player.playerId < 0 || player.Object == null || !player.Object.IsValid) continue;
            if (!battleReadyMap.TryGetValue(player.playerId, out bool playerReady) || !playerReady)
            {
                continue;
            }

            var battleAttackSeqMgr = player.GetComponent<AttackSequenceManager>();
            if (battleAttackSeqMgr == null)
            {
                battleAttackSeqMgr = player.gameObject.AddComponent<AttackSequenceManager>();
                battleAttackSeqMgr.Initialize(player);
                Debug.LogWarning($"[StartBattleForPlayers] AttackSequenceManager 동적 생성: Player {player.playerId}");
            }
            else if (battleAttackSeqMgr.Owner != player)
            {
                battleAttackSeqMgr.Initialize(player);
                Debug.Log($"[StartBattleForPlayers] AttackSequenceManager 재초기화: Player {player.playerId}");
            }

            int opponentId = _battleOpponents.TryGetValue(player.playerId, out int oppId) ? oppId : -1;
            bool hasOpponent = opponentId != -1;

            if (hasOpponent)
            {
                bool opponentReady = battleReadyMap.TryGetValue(opponentId, out bool value) && value;
                if (!opponentReady)
                {
                    Debug.LogError($"[StartBattleForPlayers] 상대 Player {opponentId} 런타임 준비 미완료 - Player {player.playerId} 전투 시작 스킵");
                    player.SetFightingState(false);
                    continue;
                }
            }

            // 이 플레이어의 매칭에서 선공자가 누구인지 확인
            int matchFirstAttackerId = _matchFirstAttacker.TryGetValue(player.playerId, out int firstId) ? firstId : -1;
            
            // Battle1: 매칭별 선공자가 공격자
            // Battle2: 매칭별 선공자가 수비자 (역할 교체)
            bool isAttackerFirstBattle = player.playerId == matchFirstAttackerId;
            bool isAttackerInThisBattle = isFirstBattle ? isAttackerFirstBattle : !isAttackerFirstBattle;

            player.IsAttackerInCurrentBattle = isAttackerInThisBattle;

            if (hasOpponent)
            {
                if (isAttackerInThisBattle)
                {
                    // 공격자 역할: 기본 웨이브 + AttackMonsterPool 소환
                    player.RefreshAttackMonsterPool(currentRound, opponentId);
                    player.SetFightingState(true);

                    var opponent = AllPlayers.FirstOrDefault(p => p != null && p.playerId == opponentId);
                    bool isAI = ComponentRegistry.Has<AIPlayerController>(player.playerId.ToString());
                    
                    if (player.monsterSpawner != null && opponent?.fieldManager != null)
                    {
                        // [공격자가 모든 몬스터 소환] 기본 웨이브 + 증강체 몬스터
                        player.monsterSpawner.SpawnAllMonstersToTargetField(currentRound, opponent.fieldManager, isAI).Forget();
                        Debug.Log($"<color=orange>[StartBattle] Player {player.playerId}: 공격자 - 수비자 {opponentId} 필드에 전체 웨이브 소환 (AI={isAI})</color>");
                    }
                }
                else
                {
                    // 수비자: 공격자가 소환할 때까지 대기
                    player.SetFightingState(true);
                    
                    // 생존 보스 소환 (이전 라운드에서 살아남은 보스가 이 플레이어에게 침공)
                    if (player.monsterSpawner != null)
                    {
                        player.monsterSpawner.SpawnSurvivorBossesAsync().Forget();
                    }
                    
                    // 카메라/UI 처리는 RPC_NotifyBattleStart에서 각 클라이언트가 처리
                    Debug.Log($"<color=blue>[StartBattle] Player {player.playerId}: 수비자 (상대: Player {opponentId})</color>");
                }
            }
            else
            {
                // 상대 없음: 수비 모드에서 기본 웨이브만 AI 자동 소환
                if (!isAttackerInThisBattle)
                {
                    // 수비 시퀀스: 기본 웨이브를 AI가 자동 소환 (증강 공격유닛 제외)
                    player.monsterSpawner.SpawnWaveWithoutAugments(currentRound);
                    Debug.Log($"<color=gray>[StartBattle] Player {player.playerId}: 상대 없음, 수비 (기본 웨이브만)</color>");
                }
                else
                {
                    // 공격 시퀀스: 관전 모드 (전투 참여 안 함)
                    player.SetFightingState(false);
                    Debug.Log($"<color=gray>[StartBattle] Player {player.playerId}: 상대 없음, 공격 (관전 모드)</color>");
                }
            }
        }

        // 모든 클라이언트에 전투 시작 알림 (RPC)
        foreach (var player in AllPlayers)
        {
            if (player == null || player.playerId < 0 || player.Object == null || !player.Object.IsValid) continue;
            if (!battleReadyMap.TryGetValue(player.playerId, out bool playerReady) || !playerReady)
            {
                continue;
            }

            int opponentId = _battleOpponents.TryGetValue(player.playerId, out int oppId) ? oppId : -1;
            if (opponentId != -1)
            {
                bool opponentReady = battleReadyMap.TryGetValue(opponentId, out bool value) && value;
                if (!opponentReady)
                {
                    continue;
                }
            }

            RPC_NotifyBattleStart(player.playerId, player.IsAttackerInCurrentBattle, opponentId);
        }
    }

    /// <summary>
    /// 특정 플레이어의 현재 전투 상대 ID를 반환합니다. (-1이면 상대 없음)
    /// </summary>
    public int GetBattleOpponent(int playerId)
    {
        if (_battleOpponents.TryGetValue(playerId, out int oppId))
        {
            return oppId;
        }

        var player = GetPlayer(playerId);
        if (player?.opponentManager != null)
        {
            int fallbackOpp = player.opponentManager.playerId;
            _battleOpponents[playerId] = fallbackOpp;
            if (!_battleOpponents.ContainsKey(fallbackOpp))
            {
                _battleOpponents[fallbackOpp] = playerId;
            }
            Debug.LogWarning($"[GetBattleOpponent] 딕셔너리 누락으로 opponentManager 폴백 사용: P{playerId} -> P{fallbackOpp}");
            return fallbackOpp;
        }

        return -1;
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
        Debug.Log("<color=red>[GameManagers] ⚡ 폭주 모드 발동! (남은 시간: 5초)</color>");
        
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
                Debug.Log($"<color=red>[TriggerBerserkMode] Player {player.playerId}: 수비팀 - 몬스터+유닛 버서커 버프</color>");
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

    private async UniTask HandleUIForNewState(GameState newState)
    {
        // [수정] 싱글플레이 모드 지원: 서버(호스트)이거나 로컬 플레이어가 있을 때만 UI 처리
        if (localPlayer == null)
        {
            if (!Runner.IsServer)
            {
                return; // 클라이언트인데 로컬 플레이어가 없으면 UI 처리 안함
            }
            else
            {
                Debug.LogWarning("[HandleUIForNewState] 로컬 플레이어가 아직 설정되지 않았습니다. UI 처리를 건너뜁니다.");
                return;
            }
        }

        switch (newState)
        {
            case GameState.Prepare:
                // 상점 UI 숨김 (증강 UI는 SyncAugmentsCommand에서 활성화)
                if (localPlayerShopUIGameObject != null)
                {
                    localPlayerShopUIGameObject.SetActive(false);
                }
                break;
            case GameState.Battle1:
            case GameState.Battle2:
                // 전투 단계 진입 시 모든 UI 비활성화
                Debug.Log($"<color=yellow>[HandleUIForNewState] {newState} 단계 - UI 비활성화</color>");
                if (UIManagers.Instance != null && UIManagers.Instance.IsUIElementActive("UI_Pnl_Augment"))
                {
                    UIManagers.Instance.ReturnUIElement("UI_Pnl_Augment");
                }
                if (localPlayerShopUIGameObject != null)
                {
                    localPlayerShopUIGameObject.SetActive(false);
                }
                // TODO: 공격 시퀀스 UI 활성화 (공격자인 경우)
                break;
            case GameState.GameOver:
                if (UIManagers.Instance == null)
                {
                    Debug.LogWarning("[HandleUIForNewState] GameOver UI를 표시할 UIManagers.Instance가 없습니다.");
                    break;
                }

                // 승자 판정: 체력이 가장 높은 플레이어 (0 이하여도 덜 마이너스인 쪽이 승리)
                PlayerManager winner = AllPlayers
                    .Where(p => p != null)
                    .OrderByDescending(p => p.GetHealth())
                    .FirstOrDefault();
                
                // 로컬 플레이어의 승패 UI 표시
                if (localPlayer != null)
                {
                    if (localPlayer == winner)
                    {
                        await UIManagers.Instance.GetUIElement("UI_Pnl_Victory");
                    }
                    else
                    {
                        await UIManagers.Instance.GetUIElement("UI_Pnl_Defeat");
                    }
                }
                break;
        }
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
    public PlayerManager GetPlayer(int id)
    {
        foreach (var player in AllPlayers)
        {
            if (player == null || player.Object == null || !player.Object.IsValid)
            {
                continue;
            }

            try
            {
                if (player.playerId == id)
                {
                    return player;
                }
            }
            catch (System.InvalidOperationException)
            {
                // Host Migration 중 Spawned 전 객체는 건너뛴다.
            }
        }

        return null;
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
            currentState = GameState.GameOver;
            phaseTimer = TickTimer.None;

            PlayerManager winner = alivePlayers.FirstOrDefault();
            
            // 안전한 씬 전환을 위해 비동기로 처리 (UI 표시 후 딜레이)
            SafeSceneTransitionAsync().Forget();
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

    #region Host Migration 지원
    /// <summary>
    /// Host Migration 복원 전에 캐시된 상태가 더 앞선 상태라면 역행을 방지하기 위해 적용합니다.
    /// </summary>
    public bool TryApplyCachedStateForMigration(GameMigrationData cachedData, float elapsedSinceCacheSeconds, string context)
    {
        if (!IsReadyForNetworkAccess || Runner == null || Object == null || !Object.HasStateAuthority)
        {
            return false;
        }

        if (cachedData.CurrentRound <= 0)
        {
            return false;
        }

        if (cachedData.GameStateValue < (int)GameState.Setup || cachedData.GameStateValue > (int)GameState.GameOver)
        {
            Debug.LogWarning($"[GameManagers] 캐시 상태 값이 유효하지 않아 적용하지 않습니다. stateValue={cachedData.GameStateValue}, context={context}");
            return false;
        }

        GameState cachedState = (GameState)cachedData.GameStateValue;
        int cachedRank = GetMigrationStateRank(cachedState);
        int currentRank = GetMigrationStateRank(currentState);

        bool roundBehind = currentRound < cachedData.CurrentRound;
        bool stateBehind = currentRound == cachedData.CurrentRound && currentRank < cachedRank;
        bool shouldPromoteState = roundBehind || stateBehind;

        float elapsed = Mathf.Max(0f, elapsedSinceCacheSeconds);
        float adjustedCachedRemaining = Mathf.Max(0f, cachedData.RemainingPhaseTime - elapsed);
        float currentRemaining = phaseTimer.IsRunning ? (phaseTimer.RemainingTime(Runner) ?? 0f) : 0f;
        bool sameRoundSameState = currentRound == cachedData.CurrentRound && currentRank == cachedRank;
        bool shouldPromoteTimerOnly = !shouldPromoteState && sameRoundSameState && adjustedCachedRemaining > currentRemaining + 1f;

        if (!shouldPromoteState && !shouldPromoteTimerOnly)
        {
            return false;
        }

        int beforeRound = currentRound;
        GameState beforeState = currentState;
        float beforeRemaining = currentRemaining;

        if (shouldPromoteState)
        {
            currentRound = cachedData.CurrentRound;
            currentState = cachedState;
        }

        bool timerRequired =
            cachedState == GameState.Prepare ||
            cachedState == GameState.Battle1 ||
            cachedState == GameState.Battle2;

        if (timerRequired && adjustedCachedRemaining > 0.25f)
        {
            phaseTimer = TickTimer.CreateFromSeconds(Runner, adjustedCachedRemaining);
        }
        else if (timerRequired && shouldPromoteState)
        {
            phaseTimer = TickTimer.None;
        }

        Debug.Log($"<color=magenta>[GameManagers] HostMigration 캐시 상태 적용 ({context})\n  before=R{beforeRound}/{beforeState} {beforeRemaining:F1}s\n  cached=R{cachedData.CurrentRound}/{cachedState} {cachedData.RemainingPhaseTime:F1}s (elapsed={elapsed:F1})\n  after=R{currentRound}/{currentState} {currentPhaseTimer:F1}s</color>");
        return true;
    }

    private static int GetMigrationStateRank(GameState state)
    {
        return state switch
        {
            GameState.Setup => 0,
            GameState.DataLoading => 1,
            GameState.Prepare => 2,
            GameState.Battle1 => 3,
            GameState.Battle2 => 4,
            GameState.GameOver => 5,
            _ => -1
        };
    }

    /// <summary>
    /// Host Migration 후 게임 상태를 복원합니다.
    /// Networked 속성들 (currentState, currentRound, phaseTimer)은 Fusion이 자동 복원합니다.
    /// 이 메서드는 로컬 상태만 복원합니다.
    /// </summary>
    public void RestoreAfterHostMigration()
    {
        _activeMigrationTraceId = ++_hostMigrationTraceSeq;
        _migrationRestoreInProgress = true;
        _migrationUiRestoreCompleted = false;
        _migrationSetupUiCompleted = false;
        _migrationWarnedPrepareExpiryRace = false;
        _migrationRestoreStartedRealtime = Time.realtimeSinceStartup;
        _migrationRestoreStartFrame = Time.frameCount;
        _migrationTimerPaused = false;
        _migrationPausedTimerRemainingSeconds = 0f;

        Debug.Log("<color=yellow>═══════════════════════════════════════════</color>");
        Debug.Log("<color=yellow>[GameManagers] RestoreAfterHostMigration 시작!</color>");
        Debug.Log("<color=yellow>═══════════════════════════════════════════</color>");
        LogMigrationTrace("RestoreAfterHostMigration:BEGIN", $"startFrame={_migrationRestoreStartFrame}");
        
        // 상태 체크 로깅
        Debug.Log($"[복원] Object 유효: {Object != null}");
        Debug.Log($"[복원] Object.IsValid: {Object?.IsValid}");
        Debug.Log($"[복원] _isSpawned: {_isSpawned}");
        Debug.Log($"[복원] IsReadyForNetworkAccess: {IsReadyForNetworkAccess}");
        Debug.Log($"[복원] HasStateAuthority: {Object?.HasStateAuthority}");
        bool runnerMatched = IsBoundToActiveRunner();
        Debug.Log($"[복원] RunnerMatched: {runnerMatched}");
        
        // Spawned 상태가 아니면 대기 후 재시도
        if (!IsReadyForNetworkAccess)
        {
            Debug.LogWarning("<color=red>[GameManagers] 아직 Spawned 상태가 아닙니다. 복원을 건너뜁니다.</color>");
            LogMigrationTrace("RestoreAfterHostMigration:ABORT_NOT_READY");
            _migrationRestoreInProgress = false;
            return;
        }

        if (!runnerMatched)
        {
            Debug.LogError("<color=red>[GameManagers] 활성 Runner와 불일치하여 복원을 중단합니다.</color>");
            LogMigrationTrace("RestoreAfterHostMigration:ABORT_RUNNER_MISMATCH");
            _migrationRestoreInProgress = false;
            return;
        }

        if (Runner != null && Runner.IsServer && (Object == null || !Object.HasStateAuthority))
        {
            Debug.LogError("<color=red>[GameManagers] 새 Host인데 StateAuthority가 없어 복원을 중단합니다.</color>");
            LogMigrationTrace("RestoreAfterHostMigration:ABORT_NO_STATE_AUTH");
            _migrationRestoreInProgress = false;
            return;
        }
        
        Debug.Log($"<color=cyan>[복원] 현재 게임 상태 - Round: {currentRound}, State: {currentState}, Timer: {currentPhaseTimer:F1}s</color>");
        
        // 1. ChangeDetector 재초기화
        Debug.Log("[복원] 1. ChangeDetector 재초기화...");
        if (Object != null)
        {
            _changeDetector = GetChangeDetector(ChangeDetector.Source.SimulationState);
            Debug.Log("[복원] ChangeDetector 재초기화 완료");
        }
        
        // 2. 로컬 플레이어 참조 재연결
        Debug.Log("[복원] 2. 로컬 플레이어 재연결...");
        RelinkLocalPlayer();
        Debug.Log($"[복원] localPlayer: {(localPlayer != null ? $"Player {localPlayer.playerId}" : "null")}");
        
        // 3. CommandProcessor 재초기화 (필요한 경우)
        Debug.Log("[복원] 3. CommandProcessor 체크...");
        if (CommandProcessor == null)
        {
            CommandProcessor = new CommandProcessor();
            Debug.Log("[복원] CommandProcessor 새로 생성");
        }
        else
        {
            Debug.Log("[복원] CommandProcessor 이미 존재");
        }
        
        // 4. UI 상태 복원
        Debug.Log("[복원] 4. UI 상태 복원...");
        LogMigrationTrace("RestoreAfterHostMigration:KickRestoreLocalUI");
        RestoreLocalUIAfterMigrationAsync().Forget();
        
        // 5. 싱글톤 인스턴스 재설정
        Debug.Log("[복원] 5. 싱글톤 인스턴스 체크...");
        if (Instance == null || Instance != this)
        {
            Instance = this;
            Debug.Log("[복원] 싱글톤 인스턴스 재설정 완료");
        }
        
        // 6. 상점/증강 데이터 재동기화 (새 Host인 경우)
        Debug.Log("[복원] 6. 상점/증강 데이터 동기화 체크...");
        if (Object != null && Object.HasStateAuthority)
        {
            Debug.Log("<color=green>[복원] 새 Host - 상점 데이터 재동기화 시작</color>");
            foreach (var player in AllPlayers)
            {
                if (player?.shopManager != null)
                {
                    var items = player.shopManager.GetCurrentShopItems();
                    Debug.Log($"[복원] Player {player.playerId} 상점 아이템: {items?.Count ?? 0}개");
                    
                    if (items != null && items.Count > 0)
                    {
                        string[] names = items.Select(i => i.UnitData?.name ?? "").ToArray();
                        int[] stars = items.Select(i => i.StarLevel).ToArray();
                        var cmd = new SyncShopItemsCommand(player.playerId, names, stars);
                        CommandProcessor.RequestCommandExecution(cmd);
                    }
                }
            }
        }
        else if (Object != null && !Object.HasStateAuthority && localPlayer != null)
        {
            Debug.Log("<color=cyan>[복원] 클라이언트 - 서버에 데이터 동기화 요청</color>");
            localPlayer.RPC_RequestSyncData();
        }
        
        // 7. 게임 흐름 재개는 "권한 + 플레이어 런타임 준비 + UI 복원 완료" 이후에 수행
        PausePhaseTimerForMigrationIfNeeded();
        Debug.Log("<color=magenta>═══ [STEP 6] 게임 흐름 재개 조건 대기 시작 ═══</color>");
        StartCoroutine(WaitForRestoreDependenciesAndResumeFlow());
        
        // ★ 8. [Observer Pattern] 상태 복원 완료 이벤트 발행
        GameEvents.TriggerGameStateRestored(currentState);
        
        Debug.Log("<color=magenta>═══ [STEP 7] Host Migration 완료 ═══</color>");
        Debug.Log($"<color=green>[STEP 7] 최종 상태 확인:</color>");
        Debug.Log($"  GameState: {currentState}");
        Debug.Log($"  라운드: {currentRound}");
        Debug.Log($"  타이머 실행 중: {phaseTimer.IsRunning}");
        Debug.Log($"  남은 시간: {currentPhaseTimer:F1}초");
        Debug.Log($"  StateAuthority: {Object?.HasStateAuthority}");
        
        // Battle 상태 확인
        bool isInBattle = currentState == GameState.Battle1 || currentState == GameState.Battle2;
        if (isInBattle)
        {
            Debug.Log($"<color=cyan>[STEP 7] ✓ Battle 상태 복원 성공! ({currentState})</color>");
        }

        LogMigrationTrace("RestoreAfterHostMigration:END");
    }

    private void PausePhaseTimerForMigrationIfNeeded()
    {
        if (Runner == null || Object == null || !Object.HasStateAuthority)
        {
            return;
        }

        if (!phaseTimer.IsRunning)
        {
            return;
        }

        float remaining = phaseTimer.RemainingTime(Runner) ?? 0f;
        if (remaining <= 0f)
        {
            return;
        }

        _migrationTimerPaused = true;
        _migrationPausedTimerRemainingSeconds = remaining;
        phaseTimer = TickTimer.None;

        Debug.Log($"[STEP 6] Host Migration 복원 중 타이머 일시 정지: {remaining:F1}초");
        LogMigrationTrace("PausePhaseTimerForMigrationIfNeeded", $"remaining={remaining:F1}");
    }

    private IEnumerator WaitForRestoreDependenciesAndResumeFlow()
    {
        float waitTime = 0f;
        const float maxWaitTime = 8f;

        LogMigrationTrace("WaitForRestoreDependenciesAndResumeFlow:BEGIN");

        while (waitTime < maxWaitTime)
        {
            bool hasAuthority = Object != null && Object.HasStateAuthority;
            bool runnerMatched = IsBoundToActiveRunner();
            bool uiReady = _migrationUiRestoreCompleted;
            bool playersReady = AreAllPlayersRuntimeReadyForMigration(out string notReadyReason);

            if (hasAuthority && runnerMatched && uiReady && playersReady)
            {
                Debug.Log($"<color=green>[STEP 6] 재개 조건 충족 ({waitTime:F1}s): authority={hasAuthority}, runnerMatched={runnerMatched}, uiReady={uiReady}, playersReady={playersReady}</color>");
                LogMigrationTrace("WaitForRestoreDependenciesAndResumeFlow:READY", $"waited={waitTime:F1}s");
                ResumeGameFlowFromCurrentState();
                yield break;
            }

            if (waitTime == 0f || Mathf.Abs((waitTime * 10f) % 10f) < 0.001f)
            {
                Debug.Log($"[STEP 6] 조건 대기 중... ({waitTime:F1}s) authority={hasAuthority}, runnerMatched={runnerMatched}, uiReady={uiReady}, playersReady={playersReady}");
                if (!playersReady && !string.IsNullOrEmpty(notReadyReason))
                {
                    Debug.LogWarning($"[STEP 6] 플레이어 런타임 준비 미완료: {notReadyReason}");
                }
                if (!runnerMatched)
                {
                    Debug.LogWarning("[STEP 6] GameManagers.Runner가 현재 활성 Runner와 다릅니다.");
                }
            }

            yield return new WaitForSeconds(0.1f);
            waitTime += 0.1f;
        }

        Debug.LogWarning($"<color=orange>[STEP 6] 재개 조건 대기 시간 초과 ({maxWaitTime:F1}s)</color>");
        LogMigrationTrace("WaitForRestoreDependenciesAndResumeFlow:TIMEOUT", $"waited={maxWaitTime:F1}s");

        if (Object != null && Object.HasStateAuthority && IsBoundToActiveRunner())
        {
            ResumeGameFlowFromCurrentState();
        }
    }

    private bool IsBoundToActiveRunner()
    {
        if (Runner == null)
        {
            return false;
        }

        var activeRunner = NetworkManager.Instance?._runner;
        return activeRunner != null && activeRunner == Runner;
    }

    private bool AreAllPlayersRuntimeReadyForMigration(out string reason)
    {
        reason = string.Empty;

        var players = AllPlayers
            .Where(p => p != null && p.playerId >= 0 && p.Object != null && p.Object.IsValid)
            .ToList();
        if (players.Count == 0)
        {
            reason = "players=0(valid)";
            return false;
        }

        var notReady = new List<string>();
        foreach (var player in players)
        {
            player.RebindRuntimeReferencesAfterMigration("GameManagers.WaitForRestoreDependencies", false);
            if (!player.IsRuntimeReady(out string playerReason))
            {
                notReady.Add($"P{player.playerId}:{playerReason}");
            }
        }

        if (notReady.Count > 0)
        {
            reason = string.Join(", ", notReady);
            return false;
        }

        return true;
    }
    
    /// <summary>
    /// [State Machine Pattern]
    /// 현재 상태에서 게임 흐름을 재개합니다 (새 Host 전용).
    /// 타이머가 없거나 만료되었으면 현재 상태에 맞게 재설정합니다.
    /// </summary>
    private void ResumeGameFlowFromCurrentState()
    {
        LogMigrationTrace("ResumeGameFlowFromCurrentState:ENTER");

        if (_migrationTimerPaused && Runner != null && Object != null && Object.HasStateAuthority)
        {
            float restoreSeconds = Mathf.Max(0.25f, _migrationPausedTimerRemainingSeconds);
            phaseTimer = TickTimer.CreateFromSeconds(Runner, restoreSeconds);
            Debug.Log($"[STEP 6] 일시 정지된 타이머 복원: {restoreSeconds:F1}초");
            LogMigrationTrace("ResumeGameFlowFromCurrentState:RESTORE_PAUSED_TIMER", $"restoreSeconds={restoreSeconds:F1}");
            _migrationTimerPaused = false;
            _migrationPausedTimerRemainingSeconds = 0f;
        }

        bool timerRunning = phaseTimer.IsRunning;
        bool timerExpired = timerRunning && phaseTimer.Expired(Runner);
        float remainingTime = timerRunning ? (phaseTimer.RemainingTime(Runner) ?? 0f) : 0f;
        
        Debug.Log($"<color=yellow>[STEP 6] 현재 상태: {currentState}</color>");
        Debug.Log($"[STEP 6] 타이머 상태:");
        Debug.Log($"  - Running: {timerRunning}");
        Debug.Log($"  - Expired: {timerExpired}");
        Debug.Log($"  - Remaining: {remainingTime:F1}초");
        
        // 타이머가 정상 동작 중이면 유지
        if (timerRunning && !timerExpired && remainingTime > 0.5f)
        {
            Debug.Log($"<color=green>[STEP 6] ✓ 기존 타이머 유지 ({remainingTime:F1}초 남음) - 게임 재개!</color>");
            LogMigrationTrace("ResumeGameFlowFromCurrentState:KEEP_TIMER");
            return;
        }
        
        // 타이머가 없거나 만료되었으면 현재 상태에 맞게 재설정
        float newDuration = currentState switch
        {
            GameState.Prepare => firstPrepareDurationUsed ? preparePhaseTime : firstPreparePhaseTime,
            GameState.Battle1 => combatTime,
            GameState.Battle2 => combatTime,
            _ => 0f
        };
        
        if (newDuration > 0f)
        {
            phaseTimer = TickTimer.CreateFromSeconds(Runner, newDuration);
            Debug.Log($"<color=cyan>[STEP 6] ✓ 타이머 재설정: {newDuration}초 - 게임 재개!</color>");
            Debug.Log($"<color=cyan>[STEP 6] 현재 상태 ({currentState})에서 계속 진행됩니다.</color>");
            LogMigrationTrace("ResumeGameFlowFromCurrentState:RESET_TIMER", $"newDuration={newDuration:F1}");
        }
        else
        {
            Debug.Log($"[STEP 6] {currentState} 상태는 타이머가 필요 없음");
            LogMigrationTrace("ResumeGameFlowFromCurrentState:NO_TIMER");
        }
    }
    
    /// <summary>
    /// Host Migration 후 로컬 플레이어 참조를 다시 연결합니다.
    /// </summary>
    private void RelinkLocalPlayer()
    {
        Debug.Log($"[GameManagers] RelinkLocalPlayer 시작 - AllPlayers 수: {AllPlayers.Count()}");
        
        // 방법 1: AllPlayers에서 InputAuthority 가진 플레이어 찾기
        localPlayer = AllPlayers.FirstOrDefault(p => 
            p != null && p.Object != null && p.Object.HasInputAuthority);
        
        // 방법 2: AllPlayers에 없으면 FindObjectsOfType으로 폴백
        if (localPlayer == null)
        {
            Debug.Log("[GameManagers] AllPlayers에서 못 찾음, FindObjectsOfType 시도...");
            var allPlayerManagers = FindObjectsOfType<PlayerManager>();
            Debug.Log($"[GameManagers] 발견된 PlayerManager 수: {allPlayerManagers.Length}");
            
            foreach (var pm in allPlayerManagers)
            {
                Debug.Log($"  - {pm.name}: Object={pm.Object != null}, HasInputAuthority={pm.Object?.HasInputAuthority}");
                if (pm != null && pm.Object != null && pm.Object.HasInputAuthority)
                {
                    localPlayer = pm;
                    break;
                }
            }
        }
        
        if (localPlayer != null)
        {
            Debug.Log($"[GameManagers] 로컬 플레이어 재연결 성공: Player {localPlayer.playerId}");
            
            // opponentManager 재연결 (2인 게임의 경우)
            var allPlayersList = AllPlayers.ToList();
            
            // AllPlayers가 비어있으면 FindObjectsOfType 사용
            if (allPlayersList.Count == 0)
            {
                allPlayersList = FindObjectsOfType<PlayerManager>().ToList();
            }
            
            if (allPlayersList.Count == 2)
            {
                var opponent = allPlayersList.FirstOrDefault(p => p != localPlayer);
                if (opponent != null)
                {
                    localPlayer.opponentManager = opponent;
                    opponent.opponentManager = localPlayer;
                    Debug.Log($"[GameManagers] opponentManager 재연결: Player {opponent.playerId}");
                }
            }
        }
        else
        {
            Debug.LogWarning("[GameManagers] 로컬 플레이어를 찾을 수 없습니다!");
        }
    }

    private void EnsureBattleMappingAfterMigration()
    {
        if (_battleOpponents.Count > 0 && _matchFirstAttacker.Count > 0)
        {
            return;
        }

        var alivePlayers = AllPlayers.Where(p => p != null && p.GetHealth() > 0).ToList();
        if (alivePlayers.Count == 0)
        {
            return;
        }

        _battleOpponents.Clear();
        _matchFirstAttacker.Clear();

        if (alivePlayers.Count == 2)
        {
            var a = alivePlayers[0];
            var b = alivePlayers[1];
            _battleOpponents[a.playerId] = b.playerId;
            _battleOpponents[b.playerId] = a.playerId;

            int firstAttacker = ResolveFirstAttackerForResumePair(a, b);
            _matchFirstAttacker[a.playerId] = firstAttacker;
            _matchFirstAttacker[b.playerId] = firstAttacker;
            FirstAttackerPlayerId = firstAttacker;

            Debug.LogWarning($"[복원/매칭] 2인 폴백 재구성 완료: P{a.playerId}↔P{b.playerId}, 선공자=P{firstAttacker}, state={currentState}");
            return;
        }

        var processed = new HashSet<int>();
        foreach (var player in alivePlayers)
        {
            if (processed.Contains(player.playerId))
            {
                continue;
            }

            var opponent = player.opponentManager;
            if (opponent != null && alivePlayers.Contains(opponent))
            {
                _battleOpponents[player.playerId] = opponent.playerId;
                _battleOpponents[opponent.playerId] = player.playerId;

                int firstAttacker = ResolveFirstAttackerForResumePair(player, opponent);
                _matchFirstAttacker[player.playerId] = firstAttacker;
                _matchFirstAttacker[opponent.playerId] = firstAttacker;

                processed.Add(player.playerId);
                processed.Add(opponent.playerId);
            }
            else
            {
                _battleOpponents[player.playerId] = -1;
                _matchFirstAttacker[player.playerId] = -1;
                processed.Add(player.playerId);
            }
        }

        Debug.LogWarning($"[복원/매칭] opponentManager 기반 재구성 완료: {string.Join(", ", _battleOpponents.Select(kv => $"P{kv.Key}↔P{kv.Value}"))}");
    }

    private int ResolveFirstAttackerForResumePair(PlayerManager a, PlayerManager b)
    {
        if (currentState == GameState.Battle1)
        {
            if (a.IsAttackerInCurrentBattle && !b.IsAttackerInCurrentBattle) return a.playerId;
            if (b.IsAttackerInCurrentBattle && !a.IsAttackerInCurrentBattle) return b.playerId;
        }
        else if (currentState == GameState.Battle2)
        {
            // Battle2는 Battle1의 공수 반대이므로, 현재 수비자가 Battle1 선공자
            if (!a.IsAttackerInCurrentBattle && b.IsAttackerInCurrentBattle) return a.playerId;
            if (!b.IsAttackerInCurrentBattle && a.IsAttackerInCurrentBattle) return b.playerId;
        }

        if (FirstAttackerPlayerId == a.playerId || FirstAttackerPlayerId == b.playerId)
        {
            return FirstAttackerPlayerId;
        }

        return a.playerId;
    }

    private async UniTask RestoreLocalUIAfterMigrationAsync()
    {
        bool completed = false;
        LogMigrationTrace("RestoreLocalUI:ENTER");

        try
        {
            await SetupGameUI(forceRefresh: true);
            LogMigrationTrace("RestoreLocalUI:AFTER_SETUP_UI");

            if (localPlayer == null)
            {
                RelinkLocalPlayer();
            }

            if (localPlayer == null)
            {
                Debug.LogWarning("[복원/UI] localPlayer를 찾지 못해 UI 이벤트를 재발행할 수 없습니다.");
                LogMigrationTrace("RestoreLocalUI:ABORT_NO_LOCAL_PLAYER");
                return;
            }

            Debug.Log($"[복원/UI] UI 이벤트 재발행 시작 - State: {currentState}, LocalPlayer: {localPlayer.playerId}");

            GameEvents.TriggerGameManagersReady();
            GameEvents.TriggerGameStateChanged(currentState);
            await HandleUIForNewState(currentState);
            LogMigrationTrace("RestoreLocalUI:AFTER_STATE_EVENTS");

            if (currentState != GameState.Prepare)
            {
                completed = true;
                LogMigrationTrace("RestoreLocalUI:END_NON_PREPARE");
                return;
            }

            var augmentChoices = localPlayer.augmentManager?.GetPresentedAugments();
            if (augmentChoices != null && augmentChoices.Count > 0)
            {
                GameEvents.TriggerAugmentPhaseStart(localPlayer, augmentChoices);
                Debug.Log($"[복원/UI] 증강 UI 이벤트 재발행 완료 (선택지: {augmentChoices.Count})");
                completed = true;
                LogMigrationTrace("RestoreLocalUI:END_AUGMENT_EVENT", $"choiceCount={augmentChoices.Count}");
                return;
            }

            Debug.LogWarning("[복원/UI] 증강 선택지가 없어 상점 UI 폴백을 시도합니다.");
            await ShowLocalShopFallbackAsync();
            completed = true;
            LogMigrationTrace("RestoreLocalUI:END_SHOP_FALLBACK");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[복원/UI] RestoreLocalUIAfterMigrationAsync 예외: {ex.Message}");
            Debug.LogException(ex);
            LogMigrationTrace("RestoreLocalUI:EXCEPTION", $"error={ex.Message}");
        }
        finally
        {
            _migrationUiRestoreCompleted = completed;
            _migrationRestoreInProgress = false;
            LogMigrationTrace("RestoreLocalUI:FINALLY", $"completed={completed}");
        }
    }

    private async UniTask ShowLocalShopFallbackAsync()
    {
        LogMigrationTrace("ShowLocalShopFallback:ENTER");

        if (localPlayer == null || localPlayer.shopManager == null)
        {
            LogMigrationTrace("ShowLocalShopFallback:ABORT_NO_PLAYER_OR_SHOP");
            return;
        }

        if (localPlayerShopUIGameObject == null || localPlayerShopUI == null)
        {
            LogMigrationTrace("ShowLocalShopFallback:ABORT_UI_REF_NULL");
            return;
        }

        if (Object != null && Object.HasStateAuthority && currentState == GameState.Prepare)
        {
            var hostShopItems = localPlayer.shopManager.GetCurrentShopItems();
            if (hostShopItems == null || hostShopItems.Count == 0)
            {
                localPlayer.shopManager.Reroll(true);
                hostShopItems = localPlayer.shopManager.GetCurrentShopItems();
                Debug.Log($"[복원/UI] 로컬 Host 상점 긴급 리롤: Player {localPlayer.playerId}, itemCount={hostShopItems?.Count ?? 0}");

                if (CommandProcessor != null && hostShopItems != null && hostShopItems.Count > 0)
                {
                    string[] shopNames = hostShopItems.Select(i => i.UnitData?.name ?? string.Empty).ToArray();
                    int[] shopStars = hostShopItems.Select(i => i.StarLevel).ToArray();
                    var syncShopCmd = new SyncShopItemsCommand(localPlayer.playerId, shopNames, shopStars);
                    CommandProcessor.RequestCommandExecution(syncShopCmd);
                    Debug.Log($"[복원/UI] 로컬 Host 상점 동기화 전송: Player {localPlayer.playerId}, itemCount={hostShopItems.Count}");
                }
            }
        }

        await localPlayer.shopManager.EnsureShopRerolledAsync();
        localPlayerShopUIGameObject.SetActive(true);
        localPlayerShopUI.SetContentVisibility(true);
        localPlayerShopUI.DisplayShopItems(localPlayer.shopManager.GetCurrentShopItems());
        Debug.Log("[복원/UI] 상점 UI 폴백 표시 완료");
        LogMigrationTrace("ShowLocalShopFallback:SUCCESS", $"shopCount={localPlayer.shopManager.GetCurrentShopItems().Count}");
    }
    #endregion


}
