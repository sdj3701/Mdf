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
                if (playerNO != null && playerNO.TryGetComponent<PlayerManager>(out var playerManager))
                {
                    yield return playerManager;
                }
            }
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
    private bool _isSpawned;
    private readonly HashSet<int> _spawnGoalRandomized = new HashSet<int>();

    private bool hasCombatBeenShortened = false;
    private bool firstPrepareDurationUsed = false;

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

        if (LoadManager.Instance == null)
        {
            var go = new GameObject("LoadManager");
            go.AddComponent<LoadManager>();
        }

        _changeDetector = GetChangeDetector(ChangeDetector.Source.SimulationState);

        // 초기화 완료 후 GameFlow 시작
        InitializeAndStartGame().Forget();
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
        GameEvents.TriggerGameStateChanged(newState);
        HandleUIForNewState(newState).Forget();
    }

    /// <summary>
    /// 게임 시작 및 설정 플로우입니다. Host와 Client 모두 실행되며, 내부에서 역할을 분기합니다.
    /// </summary>
    private async UniTask GameFlow()
    {
        currentState = GameState.Setup;
        
        // AddressablesManager에서 게임 프리팹 로드
        await AddressablesManager.Instance.LoadGamePrefabsAsync();
        
        await SetupPlayersAndGrids();

        // 플레이어가 완전히 연결될 때까지 대기
        await UniTask.WaitUntil(() => localPlayer != null && AllPlayers.Any());
        
        await SetupGameUI();

        currentState = GameState.DataLoading;

        // 모든 플레이어의 데이터 로딩 대기 (15초 타임아웃)
        var playersList = AllPlayers.ToList();
        var allLoadingTasks = playersList
            .SelectMany(p => new[] {
                p.shopManager.WaitUntilDatabaseLoaded().AsTask(),
                p.augmentManager != null ? p.augmentManager.WaitUntilAugmentDataLoaded().AsTask() : Task.CompletedTask
            }).ToList();

        if (BuildDebugGUI.Instance != null) 
            BuildDebugGUI.Instance.Log($"[GameFlow] {playersList.Count}명의 플레이어 데이터 로딩 시작. (15초 후 타임아웃)");

        var timeoutTask = Task.Delay(15000);
        var completedTask = await Task.WhenAny(Task.WhenAll(allLoadingTasks), timeoutTask);

        if (completedTask == timeoutTask)
        {
            if (BuildDebugGUI.Instance != null) 
                BuildDebugGUI.Instance.Log("<color=red>[GameFlow] 데이터 로딩 시간 초과! 게임을 시작할 수 없습니다.</color>");
            return;
        }

        if (BuildDebugGUI.Instance != null) 
            BuildDebugGUI.Instance.Log("<color=green>[GameFlow] 모든 데이터 로딩 완료.</color>");

        // 클라이언트: 서버에 데이터 동기화 요청 (UI 설정 후)
        if (!Runner.IsServer && localPlayer != null)
        {
            Debug.Log($"[GameFlow] 클라이언트가 서버에 데이터 동기화 요청");
            localPlayer.RPC_RequestSyncData();
        }

        // 서버: 초기 상점 리롤 및 첫 라운드 시작
        if (Runner.IsServer)
        {
            foreach (var player in playersList)
            {
                if (player.shopManager != null && player.shopManager.GetCurrentShopItems().Count == 0)
                {
                    player.shopManager.Reroll(isFree: true);
                    Debug.Log($"[GameFlow] Player {player.playerId} 초기 상점 리롤 완료");
                }
            }
            
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
    #endregion

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
            
            //LogGameMode();
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"UI 설정 중 심각한 에러 발생: {ex.Message}");
            if (BuildDebugGUI.Instance != null) BuildDebugGUI.Instance.Log("UI 설정 중 심각한 에러 발생");
        }
    }

    private async void HandleAugmentChosen(PlayerManager selectingPlayer, AugmentData chosenAugment)
    {
        if (selectingPlayer != localPlayer) return;

        UIManagers.Instance.ReturnUIElement("UI_Pnl_Augment");
        if (localPlayerShopUIGameObject != null && localPlayerShopUI != null)
        {
            localPlayerShopUIGameObject.SetActive(true);
            localPlayerShopUI.SetContentVisibility(true);
            // 상점 UI를 표시하기 전에, 데이터베이스 로드를 기다리고 상점을 채우는 것을 보장합니다.
            await localPlayer.shopManager.EnsureShopRerolledAsync();
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
        GameEvents.TriggerGameStateChanged(currentState); // 상태 변경 이벤트는 여기서 한번 트리거

        // UI 로직이 완료될 때까지 명시적으로 기다립니다.
        //await HandleUIForNewState(currentState);

        

        foreach (var player in AllPlayers)
        {
            if (player == null) continue;
            player.AddGold(baseGoldPerRound + GetInterest(player.GetGold()));
            player.shopManager.Reroll(true);

            // 상점 아이템 동기화 (Command Pattern 사용)
            var shopItems = player.shopManager.GetCurrentShopItems();
            string[] shopNames = shopItems.Select(i => i.UnitData?.name ?? "").ToArray();
            int[] shopStars = shopItems.Select(i => i.StarLevel).ToArray();
            var syncShopCmd = new SyncShopItemsCommand(player.playerId, shopNames, shopStars);
            CommandProcessor.RequestCommandExecution(syncShopCmd);

            // AI 준비 단계 플래그 리셋
            player.mazeConstructionComplete = false;
            player.unitPurchaseComplete = false;

            // 스폰/도착 지점은 게임 시작 시 1회만 랜덤 지정
            if (!_spawnGoalRandomized.Contains(player.playerId) && player.fieldManager != null)
            {
                MazePlanner.RandomizeSpawnAndGoal(player.fieldManager, player);
                _spawnGoalRandomized.Add(player.playerId);
            }
        }
        
        if (currentRound >= 1)
        {
            
            foreach (var player in AllPlayers)
            {
                if (player == null) continue;
                // 호스트에서만 증강을 굴리고, 결과를 모든 클라이언트와 동기화합니다.
                player.augmentManager.PresentAugments();
            }

            // 각 플레이어의 제시 증강 이름을 모든 클라이언트에 동기화 (Command Pattern 사용)
            foreach (var player in AllPlayers)
            {
                if (player == null) continue;
                var names = player.augmentManager.GetPresentedAugments()
                    .Select(a => a != null ? a.augmentName : string.Empty)
                    .ToArray();
                var syncAugmentCmd = new SyncAugmentsCommand(player.playerId, names);
                CommandProcessor.RequestCommandExecution(syncAugmentCmd);
                Debug.Log($"[StartNextRound] Player {player.playerId} 증강체 동기화: {string.Join(", ", names)}");
            }
        }
        // UI 로직이 완료될 때까지 명시적으로 기다립니다.
        await HandleUIForNewState(currentState);

        float prepDuration = (!firstPrepareDurationUsed && currentRound == 1) ? firstPreparePhaseTime : preparePhaseTime;
        firstPrepareDurationUsed = true;
        phaseTimer = TickTimer.CreateFromSeconds(Runner, prepDuration);
    }

    // private void PresentedAugments(int targetPlayerId, string[] augmentNames)
    // {
    //     var target = GetPlayer(targetPlayerId);
    //     if (target == null || target.augmentManager == null) return;
    //     target.augmentManager.SetPresentedAugmentsByNames(augmentNames);
    // }

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

        switch (newState)
        {
            case GameState.Prepare:
                if (currentRound >= 1)
                {
                    if (augmentSelectionUI != null)
                    {
                        if (localPlayerShopUIGameObject != null) localPlayerShopUIGameObject.SetActive(false);
                        await UIManagers.Instance.GetUIElement("UI_Pnl_Augment");
                        await localPlayer.augmentManager.EnsureAugmentsPresentedAsync(); // 예시: 싱글 플레이처럼 동작하도록 호출

                        // 2. [수정된 핵심 로직] 증강 데이터가 준비될 때까지 최대 5초간 대기합니다.
                        try
                        {
                            // presentedAugments 리스트에 1개 이상의 데이터가 들어올 때까지 대기
                            await UniTask.WaitUntil(() =>
                                localPlayer != null && localPlayer.augmentManager.GetPresentedAugments().Count > 0
                            ).Timeout(System.TimeSpan.FromSeconds(5));
                        }
                        catch (System.TimeoutException)
                        {
                            // 5초 안에 데이터가 들어오지 않았을 경우 (에러는 발생시키되 게임은 멈추지 않음)
                            Debug.LogError("<color=red>[HandleUIForNewState] 5초 안에 증강 데이터 로드에 실패했습니다. (Timeout). 빈 목록으로 UI 표시를 시도합니다.</color>");
                        }
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
