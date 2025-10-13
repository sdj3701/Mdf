using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using UnityEngine.UI;
using Cysharp.Threading.Tasks;
using Fusion;
using System.Threading.Tasks; // [추가됨] Task.Delay와 Task.WhenAny를 사용하기 위해 필요합니다.

// MonoBehaviour 대신 NetworkBehaviour를 상속받아 네트워크 객체로 만듭니다.
public class GameManagers : NetworkBehaviour
{
    // 싱글톤 패턴은 유지하되, 초기화는 Spawned()에서 수행합니다.
    public static GameManagers Instance { get; private set; }
    public CommandProcessor CommandProcessor { get; private set; }

    // [수정] Fusion 2의 변경 감지를 위한 ChangeDetector 인스턴스
    private ChangeDetector _changeDetector;

    #region 인게임 관련 변수 (네트워크 동기화)
    public enum GameState { Setup, DataLoading, Prepare, Combat, GameOver }

    // [수정] OnChanged 속성을 제거했습니다. Fusion 2에서는 ChangeDetector를 사용합니다.
    [Networked]
    public GameState currentState { get; set; }

    [Networked]
    public int currentRound { get; set; }

    [Networked]
    private TickTimer phaseTimer { get; set; }

    public float currentPhaseTimer => phaseTimer.IsRunning ? phaseTimer.RemainingTime(Runner) ?? 0f : 0f;

    // 세션은 항상 4명 (Inspector 설정 제거)
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
                if (playerNO != null && playerNO.TryGetComponent<PlayerManager>(out var playerManager))
                {
                    yield return playerManager;
                }
            }
        }
    }
    #endregion

    [Header("생성할 프리팹 (NetworkObject 필수)")]
    public GameObject playerManagerPrefab;
    public GameObject gridPrefab;
    public GameObject defaultMonsterPrefab;

    [Header("자동 생성 위치 설정")]
    public Vector3 player1BasePosition = new Vector3(0, 0, 0);
    public Vector3 playerOffset = new Vector3(0, 10, 0);

    #region 단계별 시간 및 보상
    [Header("단계별 시간 설정 (초)")]
    public float preparePhaseTime = 45f;
    public float combatTime = 60f;

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
    private NetworkManager networkManager;
    private bool _isSpawned;

    private bool hasCombatBeenShortened = false;

    /// <summary>
    /// 이 NetworkBehaviour가 네트워크 상에 스폰될 때 Fusion에 의해 호출됩니다.
    /// </summary>
    public override void Spawned()
    {
        if (Instance == null)
        {
            Instance = this;
            CommandProcessor = new CommandProcessor();
        }
        else
        {
            Runner.Despawn(Object);
            return;
        }

        _changeDetector = GetChangeDetector(ChangeDetector.Source.SimulationState);

        networkManager = NetworkManager.Instance;
        

        if (Object.HasStateAuthority)
        {
            Debug.Log("호스트의 GameManagers 스폰 완료. 게임 흐름을 시작합니다.");
            GameFlow().Forget();
        }

        _isSpawned = true;
        // 모든 설정이 끝난 후, 준비 완료 이벤트를 발생시킵니다.
        GameEvents.TriggerGameManagersReady();
    }

    /// <summary>
    /// Fusion의 네트워크/물리 틱마다 호출됩니다. 게임 로직 처리에 적합합니다.
    /// </summary>
    public override void FixedUpdateNetwork()
    {
        if (!Object.HasStateAuthority) return;

        if (phaseTimer.Expired(Runner))
        {
            phaseTimer = TickTimer.None;
            switch (currentState)
            {
                case GameState.Prepare:
                    StartCombatPhase();
                    break;
                case GameState.Combat:
                    StartNextRound();
                    break;
            }
        }
        else if (currentState == GameState.Combat && !hasCombatBeenShortened && AllPlayers.All(p => p != null && !p.IsActivelyFighting))
        {
            if (phaseTimer.RemainingTime(Runner) > 3f)
            {
                phaseTimer = TickTimer.CreateFromSeconds(Runner, 3f);
                hasCombatBeenShortened = true;
            }
        }
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
                OnGameStateChanged(currentState);
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

    /// <summary>
    /// 호스트에서만 호출되는 게임 시작 및 설정 플로우입니다.
    /// </summary>
    private async UniTask GameFlow()
    {
        currentState = GameState.Setup;
        
        await SetupPlayersAndGrids();

        // [수정] 플레이어가 완전히 연결될 때까지 대기
        await UniTask.WaitUntil(() => localPlayer != null && AllPlayers.Any());
        Debug.Log($"플레이어 연결 완료. 로컬 플레이어: Player {localPlayer.playerId}, 총 플레이어 수: {AllPlayers.Count()}");

        Rpc_SetupGameUI();

        // UI 설정이 완료될 때까지 잠시 대기
        await UniTask.Delay(100);

        currentState = GameState.DataLoading;

        // 데이터 로딩 실패를 감지하기 위한 타임아웃 로직 (15초)
        var playersList = AllPlayers.ToList();
        var loadingTasks = playersList
            .Select(p => p.shopManager.WaitUntilDatabaseLoaded().AsTask())
            .ToList();

        if (BuildDebugGUI.Instance != null) BuildDebugGUI.Instance.Log($"[GameFlow] {playersList.Count}명의 플레이어 데이터 로딩 시작. (15초 후 타임아웃)");

        var timeoutTask = Task.Delay(15000); // 15초 (15000ms)
        var completedTask = await Task.WhenAny(Task.WhenAll(loadingTasks), timeoutTask);

        if (completedTask == timeoutTask)
        {
            if (BuildDebugGUI.Instance != null) BuildDebugGUI.Instance.Log("<color=red>[GameFlow] 데이터 로딩 시간 초과! 게임을 시작할 수 없습니다.</color>");

            for (int i = 0; i < playersList.Count; i++)
            {
                if (!loadingTasks[i].IsCompleted)
                {
                    if (BuildDebugGUI.Instance != null) BuildDebugGUI.Instance.Log($"<color=red>[GameFlow] 로딩 실패 플레이어: Player {playersList[i].playerId}</color>");
                }
            }
            // 데이터 로딩 실패 시, 게임 흐름을 중단합니다.
            return;
        }
        else
        {
            Debug.Log("모든 데이터 로딩 완료. 첫 라운드를 시작합니다.");
            if (BuildDebugGUI.Instance != null) BuildDebugGUI.Instance.Log("<color=green>[GameFlow] 모든 데이터 로딩 완료. 첫 라운드를 시작합니다.</color>");

            // 싱글플레이어 모드에서는 singlePlayerModeCount를 기반으로 실제 게임 로직에 반영
            if (Runner.GameMode == GameMode.Single)
            {
                var allPlayersList = playersList;
                if (singlePlayerModeCount >= 2)
                {
                    for (int i = 0; i < singlePlayerModeCount; i++)
                    {
                        if (i < allPlayersList.Count && (i + 1) < allPlayersList.Count)
                        {
                            allPlayersList[i].opponentManager = allPlayersList[i + 1];
                            allPlayersList[i + 1].opponentManager = allPlayersList[i];
                        }
                    }
                }
            }

            StartNextRound();
        }
    }

    private async UniTask SetupPlayersAndGrids()
    {
        Debug.Log($"[SetupPlayersAndGrids] Runner.IsServer: {Runner.IsServer}, Runner.GameMode: {Runner.GameMode}");

        if (!Runner.IsServer)
        {
            Debug.LogWarning("[SetupPlayersAndGrids] 서버가 아니므로 플레이어 생성을 건너뜁니다.");
            return;
        }

        Debug.Log("호스트가 플레이어와 그리드 생성을 시작합니다.");
        if (BuildDebugGUI.Instance != null) BuildDebugGUI.Instance.Log("호스트가 플레이어와 그리드 생성을 시작합니다.");
        var playerRefs = Runner.ActivePlayers.ToList();

        // 플레이어 생성 수 및 AI 설정 결정
        int playersToCreate;
        bool[] isAIPlayer = new bool[MAX_PLAYERS];
        
        if (Runner.GameMode == GameMode.Single)
        {
            // 싱글플레이 모드: GameSceneInitializer에서 직접 가져오기
            var initializer = FindObjectOfType<GameSceneInitializer>();
            if (initializer != null)
            {
                playersToCreate = initializer.singlePlayerCount;
                singlePlayerModeCount = playersToCreate; // 네트워크 동기화
                Debug.Log($"[싱글플레이 모드] GameSceneInitializer에서 플레이어 수 가져옴: {playersToCreate}명");
            }
            else
            {
                // GameSceneInitializer가 없으면 singlePlayerModeCount 사용 (멀티플레이에서 Single 모드로 전환 시)
                playersToCreate = singlePlayerModeCount > 0 ? singlePlayerModeCount : 2;
                Debug.Log($"[싱글플레이 모드] singlePlayerModeCount 사용: {playersToCreate}명");
            }
            
            Debug.Log($"[싱글플레이 모드] {playersToCreate}명 생성 (0번=로컬, 나머지=AI)");
            
            // 0번은 로컬 플레이어, 나머지는 AI
            for (int i = 0; i < MAX_PLAYERS; i++)
            {
                isAIPlayer[i] = (i >= playersToCreate) ? false : (i > 0); // i=0은 로컬, i>0은 AI
            }
        }
        else
        {
            // 멀티플레이 모드: 항상 4명, 접속 안한 슬롯은 AI
            playersToCreate = MAX_PLAYERS;
            
            Debug.Log($"[멀티플레이 모드] 4명 생성 (접속: {playerRefs.Count}명, AI: {MAX_PLAYERS - playerRefs.Count}명)");
            
            // 실제 접속한 플레이어 수만큼은 실제 플레이어, 나머지는 AI
            for (int i = 0; i < MAX_PLAYERS; i++)
            {
                isAIPlayer[i] = (i >= playerRefs.Count);
            }
        }
        
        Debug.Log($"총 {playersToCreate}명의 플레이어 생성 예정 (현재 접속: {playerRefs.Count}명)");

        for (int i = 0; i < playersToCreate; i++)
        {
            // [3D Migration] 각 플레이어의 그리드를 XZ 평면에서 분리합니다.
            // 과거 2D 프로젝트에서는 Y(상하)로 띄웠지만, 3D(XZ)로 전환 후에는 겹치게 됩니다.
            // 해결: inspector에서 설정된 playerOffset을 XZ로 투영하고, 
            //       만약 Z가 0이고 Y만 설정돼 있다면(레거시 설정) Y를 Z로 매핑합니다.
            Vector3 effectiveOffset = playerOffset;
            if (Mathf.Approximately(effectiveOffset.z, 0f) && !Mathf.Approximately(effectiveOffset.y, 0f))
            {
                // 레거시(2D) 설정 대응: Y 오프셋을 Z 오프셋으로 사용
                effectiveOffset = new Vector3(effectiveOffset.x, 0f, effectiveOffset.y);
            }
            // XZ만 사용하고 Y는 무시
            effectiveOffset.y = 0f;
            Vector3 playerPosition = new Vector3(
                player1BasePosition.x + effectiveOffset.x * i,
                player1BasePosition.y, // 동일한 바닥 높이 유지
                player1BasePosition.z + effectiveOffset.z * i
            );
            bool isAI = isAIPlayer[i];
            PlayerRef inputAuthority = PlayerRef.None;

            if (!isAI && i < playerRefs.Count)
            {
                // 실제 접속한 플레이어에게 InputAuthority 부여
                inputAuthority = playerRefs[i];
            }

            Debug.Log($"🎮 Player {i} 생성 시작 - AI: {isAI}, Position: {playerPosition}, InputAuthority: {inputAuthority}");
            
            // Prefab 유효성 검사
            if (gridPrefab == null)
            {
                Debug.LogError($"❌ gridPrefab이 null입니다! Inspector에서 할당되었는지 확인하세요.");
                continue;
            }
            if (playerManagerPrefab == null)
            {
                Debug.LogError($"❌ playerManagerPrefab이 null입니다! Inspector에서 할당되었는지 확인하세요.");
                continue;
            }

            Debug.Log($"🌍 Player {i}의 Grid 생성 중...");
            NetworkObject gridNO = await Runner.SpawnAsync(gridPrefab, playerPosition, Quaternion.identity);
            if (gridNO == null)
            {
                Debug.LogError($"❌ Player {i}의 Grid 생성 실패!");
                continue;
            }
            Debug.Log($"✅ Player {i}의 Grid 생성 완료: {gridNO.name}");

            Debug.Log($"👤 Player {i}의 PlayerManager 생성 중...");
            NetworkObject playerNO = await Runner.SpawnAsync(playerManagerPrefab, playerPosition, Quaternion.identity, inputAuthority);
            if (playerNO == null)
            {
                Debug.LogError($"❌ Player {i}의 PlayerManager 생성 실패!");
                continue;
            }
            Debug.Log($"✅ Player {i}의 PlayerManager 생성 완료: {playerNO.name}");

            NetworkPlayers.Set(i, playerNO);

            PlayerManager newPlayer = playerNO.GetComponent<PlayerManager>();
            if (newPlayer != null)
            {
                newPlayer.Rpc_InitializePlayer(i, gridNO);
            }

            if (isAI)
            {
                playerNO.name = $"Player {i + 1} (AI)";
                var aiController = playerNO.gameObject.AddComponent<AIPlayerController>();
                aiController.Initialize(newPlayer, this.CommandProcessor);
                Debug.Log($"✅ AI 플레이어 {i + 1} 생성 완료");
            }
            else
            {
                playerNO.name = $"Player {i + 1}";
                Debug.Log($"✅ 실제 플레이어 {i + 1} 생성 완료");
            }
        }

        Rpc_LinkSpawnedObjects();
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void Rpc_LinkSpawnedObjects()
    {
        Debug.Log("생성된 네트워크 객체들을 연결하는 중...");
        if (BuildDebugGUI.Instance != null) BuildDebugGUI.Instance.Log("생성된 네트워크 객체들을 연결하는 중...");

        // InputAuthority를 가진 플레이어를 찾아 로컬 플레이어로 설정
        localPlayer = AllPlayers.FirstOrDefault(p => p != null && p.Object.HasInputAuthority);

        // 싱글플레이 모드에서는 InputAuthority가 없을 수 있으므로, 첫 번째 플레이어를 로컬 플레이어로 설정
        if (localPlayer == null && AllPlayers.Any())
        {
            localPlayer = AllPlayers.First(p => p != null);
            Debug.Log($"[싱글플레이 모드] 첫 번째 플레이어를 로컬 플레이어로 설정: Player {localPlayer.playerId}");
        }

        var allPlayersList = AllPlayers.ToList();
        if (allPlayersList.Count == 2)
        {
            allPlayersList[0].opponentManager = allPlayersList[1];
            allPlayersList[1].opponentManager = allPlayersList[0];
        }
        Debug.Log($"객체 연결 완료. 총 {allPlayersList.Count}명의 플레이어 발견. 로컬 플레이어: Player {localPlayer?.playerId}");
        if (BuildDebugGUI.Instance != null) BuildDebugGUI.Instance.Log("객체 연결 완료");
        
        // 싱글플레이 모드에서 singlePlayerModeCount가 설정되지 않았다면 기본값으로 설정
        if (Runner.GameMode == GameMode.Single && singlePlayerModeCount <= 0)
        {
            // 싱글플레이 모드에서는 실제 플레이어 수를 기반으로 singlePlayerModeCount 설정
            singlePlayerModeCount = allPlayersList.Count;
            Debug.Log($"[Rpc_LinkSpawnedObjects] 싱글플레이 모드에서 singlePlayerModeCount를 {singlePlayerModeCount}로 설정");
        }
        
        // 플레이어 데이터가 모두 준비되었을 때 발생시키는 이벤트 호출
        OnPlayersDataReady?.Invoke();
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void Rpc_SetSinglePlayerModeCount(int count)
    {
        singlePlayerModeCount = count;
        Debug.Log($"[Rpc_SetSinglePlayerModeCount] 싱글플레이어 모드 플레이어 수 설정: {count}");
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void Rpc_SetupGameUI()
    {
        SetupGameUI().Forget();
    }

    private async UniTask SetupGameUI()
    {
        try
        {
            var shopPanelTask = UIManagers.Instance.GetUIElement("UI_Pnl_Shop");
            var augmentPanelTask = UIManagers.Instance.GetUIElement("UI_Pnl_Augment");
            var (shopPanelInstance, augmentPanelInstance) = await UniTask.WhenAll(shopPanelTask, augmentPanelTask);

            if (shopPanelInstance != null)
            {
                localPlayerShopUI = shopPanelInstance.GetComponent<ShopUIController>();
                localPlayerShopUIGameObject = shopPanelInstance;
                localPlayerShopUI.SetContentVisibility(false);
            }
            if (augmentPanelInstance != null)
            {
                augmentSelectionUI = augmentPanelInstance.GetComponent<AugmentUIController>();
                UIManagers.Instance.ReturnUIElement("UI_Pnl_Augment");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"UI 설정 중 심각한 에러 발생: {ex.Message}");
            if (BuildDebugGUI.Instance != null) BuildDebugGUI.Instance.Log("UI 설정 중 심각한 에러 발생");
        }
    }

    private void HandleAugmentChosen(PlayerManager selectingPlayer, AugmentData chosenAugment)
    {
        if (selectingPlayer != localPlayer) return;

        UIManagers.Instance.ReturnUIElement("UI_Pnl_Augment");
        if (localPlayerShopUIGameObject != null && localPlayerShopUI != null)
        {
            localPlayerShopUIGameObject.SetActive(true);
            localPlayerShopUI.SetContentVisibility(true);
            var shopItems = localPlayer.shopManager.GetCurrentShopItems();
            localPlayerShopUI.DisplayShopItems(shopItems);
        }
    }

    private void StartNextRound()
    {
        if (!Object.HasStateAuthority) return;
        if (currentState == GameState.GameOver) return;

        if (currentState != GameState.DataLoading)
        {
            currentRound++;
        }
        else
        {
            currentRound = 1;
        }

        currentState = GameState.Prepare;
        OnGameStateChanged(currentState); // 상태 변경 후 직접 이벤트 호출

        foreach (var player in AllPlayers)
        {
            if (player == null) continue;
            player.AddGold(baseGoldPerRound + GetInterest(player.GetGold()));
            player.shopManager.Reroll(true);
        }

        if (currentRound >= 1)
        {
            foreach (var player in AllPlayers)
            {
                if (player == null) continue;
                player.augmentManager.PresentAugments();
            }
        }

        phaseTimer = TickTimer.CreateFromSeconds(Runner, preparePhaseTime);
    }

    private void StartCombatPhase()
    {
        if (!Object.HasStateAuthority) return;
        if (currentState == GameState.GameOver) return;

        currentState = GameState.Combat;
        hasCombatBeenShortened = false;

        foreach (var player in AllPlayers)
        {
            if (player == null) continue;
            player.monsterSpawner.SpawnWave(currentRound);
        }

        phaseTimer = TickTimer.CreateFromSeconds(Runner, combatTime);
    }

    private void OnGameStateChanged(GameState newState)
    {
        Debug.Log($"--- 라운드 {currentRound}: <color=yellow>{newState}</color> 단계 시작 --- (호출된 상태: {currentState})");
        Debug.Log($"[OnGameStateChanged] HandleUIForNewState 호출 시작");

        GameEvents.TriggerGameStateChanged(newState);

        HandleUIForNewState(newState).Forget();
        Debug.Log($"[OnGameStateChanged] HandleUIForNewState 호출 완료");
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

        Debug.Log($"[HandleUIForNewState] newState: {newState}, currentRound: {currentRound}");

        switch (newState)
        {
            case GameState.Prepare:
                Debug.Log($"[HandleUIForNewState] Prepare 상태 처리 시작. currentRound: {currentRound}, augmentSelectionUI: {augmentSelectionUI != null}");
                if (currentRound >= 1)
                {
                    Debug.Log($"[HandleUIForNewState] currentRound >= 1 조건 만족");
                    if (augmentSelectionUI != null)
                    {
                        Debug.Log($"[HandleUIForNewState] augmentSelectionUI != null 조건 만족");
                        if (localPlayerShopUIGameObject != null) localPlayerShopUIGameObject.SetActive(false);
                        await UIManagers.Instance.GetUIElement("UI_Pnl_Augment");
                        GameEvents.TriggerAugmentPhaseStart(localPlayer, localPlayer.augmentManager.GetPresentedAugments());
                    }
                    else
                    {
                        Debug.LogWarning("[HandleUIForNewState] augmentSelectionUI가 null입니다.");
                    }
                }
                else
                {
                    if (localPlayerShopUI != null)
                    {
                        localPlayerShopUIGameObject.SetActive(true);
                        localPlayerShopUI.SetContentVisibility(true);
                        localPlayerShopUI.UpdateShopSlots();
                    }
                }
                break;
            case GameState.Combat:
                UIManagers.Instance.ReturnUIElement("UI_Pnl_Augment");
                if (localPlayerShopUIGameObject != null) localPlayerShopUI.SetContentVisibility(false);
                break;
            case GameState.GameOver:
                PlayerManager winner = AllPlayers.FirstOrDefault(p => p != null && p.GetHealth() > 0);
                if (localPlayer != null && localPlayer.GetHealth() <= 0) await UIManagers.Instance.GetUIElement("UI_Pnl_Defeat");
                else if (localPlayer == winner) await UIManagers.Instance.GetUIElement("UI_Pnl_Victory");
                break;
        }
    }

    public GameState GetGameState() => currentState;
    public PlayerManager GetPlayer(int id) => AllPlayers.FirstOrDefault(p => p.playerId == id);

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
            Debug.Log(winner != null ? $"게임 종료! 승자: Player {winner.playerId}" : "게임 종료! 무승부입니다.");
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

    void OnGUI()
    {
        if (!_isSpawned)
        {
            return;
        }
        GUI.Label(new Rect(20, 270, 180, 40), $"현재 상태: {currentState}");
        GUI.Label(new Rect(20, 290, 180, 40), $"남은 시간: {currentPhaseTimer:F1}");
    }
}
