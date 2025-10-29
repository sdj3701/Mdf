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
        
        GameFlow().Forget();
        

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
                    // 일단 수정
                    StartNextRound().Forget();
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
        // 로컬 플레이어의 UI만 업데이트해야 하므로, 로컬 플레이어 확인 후 비동기 UI 로직 호출
        if (localPlayer == null) return;
        
        // UI 업데이트 및 이벤트 발송은 UniTask의 'Fire-and-Forget' 패턴으로 처리
        // Render()는 async/await을 할 수 없습니다.
        Debug.Log($"--- 라운드 {currentRound}: <color=yellow>{newState}</color> 단계 시작 (네트워크 반응) ---");
        GameEvents.TriggerGameStateChanged(newState);
        HandleUIForNewState(newState).Forget();
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

        await SetupGameUI();

        // UI 설정이 완료될 때까지 잠시 대기
        await UniTask.Delay(100);

        currentState = GameState.DataLoading;

        // 데이터 로딩 실패를 감지하기 위한 타임아웃 로직 (15초)
        var playersList = AllPlayers.ToList();
        var shopLoadingTasks = playersList
            .Select(p => p.shopManager.WaitUntilDatabaseLoaded().AsTask())
            .ToList();
        var augmentLoadingTasks = playersList
            .Select(p => (p.augmentManager != null ? p.augmentManager.WaitUntilAugmentDataLoaded().AsTask() : Task.CompletedTask))
            .ToList();
        var allLoadingTasks = shopLoadingTasks.Concat(augmentLoadingTasks).ToList();

        if (BuildDebugGUI.Instance != null) BuildDebugGUI.Instance.Log($"[GameFlow] {playersList.Count}명의 플레이어 데이터 및 증강 데이터 로딩 시작. (15초 후 타임아웃)");

        var timeoutTask = Task.Delay(15000); // 15초 (15000ms)
        var completedTask = await Task.WhenAny(Task.WhenAll(allLoadingTasks), timeoutTask);

        if (completedTask == timeoutTask)
        {
            if (BuildDebugGUI.Instance != null) BuildDebugGUI.Instance.Log("<color=red>[GameFlow] 데이터 로딩 시간 초과! 게임을 시작할 수 없습니다.</color>");

            for (int i = 0; i < playersList.Count; i++)
            {
                bool shopDone = shopLoadingTasks[i].IsCompleted;
                bool augmentDone = augmentLoadingTasks[i].IsCompleted;
                if (!shopDone || !augmentDone)
                {
                    string detail = (!shopDone && !augmentDone) ? "상점+증강" : (!shopDone ? "상점" : "증강");
                    if (BuildDebugGUI.Instance != null) BuildDebugGUI.Instance.Log($"<color=red>[GameFlow] 로딩 실패 플레이어: Player {playersList[i].playerId} ({detail})</color>");
                }
            }
            // 데이터 로딩 실패 시, 게임 흐름을 중단합니다.
            return;
        }
        else
        {
            Debug.Log("<color=green>모든 데이터 로딩 완료. 첫 라운드를 시작합니다.</color>");
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
            await StartNextRound();
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
            //BuildDebugGUI.Instance.Log(i.ToString());
            Vector3 playerPosition = player1BasePosition + playerOffset * i;
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

    private async UniTask SetupGameUI()
    {
        try
        {
            Debug.Log("<color=red>UI 생성</color>");
            var shopPanelTask = UIManagers.Instance.GetUIElement("UI_Pnl_Shop");
            var augmentPanelTask = UIManagers.Instance.GetUIElement("UI_Pnl_Augment");
            var (shopPanelInstance, augmentPanelInstance) = await UniTask.WhenAll(shopPanelTask, augmentPanelTask);

            if (shopPanelInstance != null)
            {
                localPlayerShopUI = shopPanelInstance.GetComponent<ShopUIController>();
                localPlayerShopUIGameObject = shopPanelInstance;
                localPlayerShopUI.SetContentVisibility(false);
                Debug.Log("<color=red> not shopPanelInstance </color>");
            }
            if (augmentPanelInstance != null)
            {
                augmentSelectionUI = augmentPanelInstance.GetComponent<AugmentUIController>();
                UIManagers.Instance.ReturnUIElement("UI_Pnl_Augment");
                Debug.Log("<color=red> Not augmentPanelInstance</color>");
            }
            Debug.Log("<color=red>UI 완성</color>");
            //LogGameMode();
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

    private async UniTask StartNextRound()
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
        
        // [수정] OnGameStateChanged를 제거하고 상태 변경 이벤트 및 UI 로직을 여기서 명시적으로 await 합니다.
        Debug.Log($"--- 라운드 {currentRound}: <color=yellow>{currentState}</color> 단계 시작 (명시적 흐름) ---");
        GameEvents.TriggerGameStateChanged(currentState); // 상태 변경 이벤트는 여기서 한번 트리거
        
        // UI 로직이 완료될 때까지 명시적으로 기다립니다.
        await HandleUIForNewState(currentState); 
        
        Debug.Log(currentRound); // << 이 코드는 이제 HandleUIForNewState가 완료되면 실행됩니다!

        foreach (var player in AllPlayers)
        {
            if (player == null) continue;
            player.AddGold(baseGoldPerRound + GetInterest(player.GetGold()));
            player.shopManager.Reroll(true);
        }
        Debug.Log(currentRound);
        if (currentRound >= 1)
        {
            Debug.Log("PresentedAugments Check");
            foreach (var player in AllPlayers)
            {
                if (player == null) continue;
                // 호스트에서만 증강을 굴리고, 결과를 모든 클라이언트와 동기화합니다.
                player.augmentManager.PresentAugments();
            }

            // 각 플레이어의 제시 증강 이름을 모든 클라이언트에 동기화
            foreach (var player in AllPlayers)
            {
                if (player == null) continue;
                var names = player.augmentManager.GetPresentedAugments()
                    .Select(a => a != null ? a.augmentName : string.Empty)
                    .ToArray();
                PresentedAugments(player.playerId, names);
            }
        }

        phaseTimer = TickTimer.CreateFromSeconds(Runner, preparePhaseTime);
    }

    private void PresentedAugments(int targetPlayerId, string[] augmentNames)
    {
        var target = GetPlayer(targetPlayerId);
        if (target == null || target.augmentManager == null) return;
        target.augmentManager.SetPresentedAugmentsByNames(augmentNames);
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

    // private async UniTask OnGameStateChanged(GameState newState)
    // {
    //     Debug.Log($"--- 라운드 {currentRound}: <color=yellow>{newState}</color> 단계 시작 --- (호출된 상태: {currentState})");
    //     Debug.Log($"[OnGameStateChanged] HandleUIForNewState 호출 시작");

    //     GameEvents.TriggerGameStateChanged(newState);

    //     // await을 사용하여 HandleUIForNewState가 완료될 때까지 기다립니다.
    //     await HandleUIForNewState(newState); 
    //     Debug.Log($"[OnGameStateChanged] HandleUIForNewState 호출 완료");
    // }

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
                        await localPlayer.augmentManager.EnsureAugmentsPresentedAsync(); // 예시: 싱글 플레이처럼 동작하도록 호출

                        // 2. [수정된 핵심 로직] 증강 데이터가 준비될 때까지 최대 5초간 대기합니다.
                        Debug.Log($"[HandleUIForNewState] 증강 데이터 준비를 위해 최대 5초간 대기합니다.");
                        try
                        {
                            // presentedAugments 리스트에 1개 이상의 데이터가 들어올 때까지 대기
                            await UniTask.WaitUntil(() =>
                                localPlayer != null && localPlayer.augmentManager.GetPresentedAugments().Count > 0
                            ).Timeout(System.TimeSpan.FromSeconds(5));
                            Debug.Log($"<color=green>{localPlayer.augmentManager.GetPresentedAugments().Count}.</color>");
                            
                            Debug.Log("<color=green>[HandleUIForNewState] 증강 데이터 준비 완료.</color>");
                        }
                        catch (System.TimeoutException)
                        {
                            Debug.Log($"<color=green>{localPlayer.augmentManager.GetPresentedAugments().Count}.</color>");
                            // 5초 안에 데이터가 들어오지 않았을 경우 (에러는 발생시키되 게임은 멈추지 않음)
                            Debug.LogError("<color=red>[HandleUIForNewState] 5초 안에 증강 데이터 로드에 실패했습니다. (Timeout). 빈 목록으로 UI 표시를 시도합니다.</color>");
                        }

                        Debug.Log("<color=blue> Augment UI Open </color>");
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
        Debug.Log("<color=green> check </color>");
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

      private void LogGameMode()
    {
        if (Runner == null)
        {
            Debug.LogError("[GameModeCheck] Runner가 초기화되지 않았습니다.");
            return;
        }

        string modeString = "알 수 없는 모드";
        string colorTag = "#FFFFFF"; // 기본 흰색

        switch (Runner.GameMode)
        {
            case GameMode.Shared:
                modeString = "Shared Mode Client";
                colorTag = "#FFC0CB"; // 분홍색
                break;
            case GameMode.AutoHostOrClient:
            case GameMode.Client:
                modeString = "Client Mode";
                colorTag = "#ADD8E6"; // 연한 파란색 (클라이언트)
                break;
            case GameMode.Host:
                modeString = "Host Mode (StateAuthority)";
                colorTag = "#FFA500"; // 주황색 (호스트/권한)
                break;
            case GameMode.Server:
                modeString = "Server Mode (StateAuthority)";
                colorTag = "#FF4500"; // 주황색 (서버/권한)
                break;
            case GameMode.Single:
                modeString = "Single Player Mode (Host와 유사)";
                colorTag = "#90EE90"; // 연한 녹색
                break;
        }

        // Object.HasStateAuthority 확인
        string authorityStatus = "";
        if (Object.HasStateAuthority)
        {
            authorityStatus = " (이 객체의 상태 권한 보유)";
        }
        else if (Object.HasInputAuthority)
        {
            authorityStatus = " (이 객체의 입력 권한 보유)";
        }

        Debug.Log($"<color={colorTag}>[GameModeCheck] 현재 모드: {modeString}{authorityStatus}</color>");

        // Host와 Client를 간단히 구분하는 로직
        if (Object.HasStateAuthority)
        {
            Debug.Log("<color=red>★★★ 현재 이 컴퓨터가 'Host' 또는 'Server'입니다. 게임 흐름을 제어합니다. ★★★</color>");
        }
        else
        {
            Debug.Log("<color=blue>--- 현재 이 컴퓨터는 'Client'입니다. 호스트를 따릅니다. ---</color>");
        }
    }
}
