using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using UnityEngine.UI;
using Cysharp.Threading.Tasks;
using Fusion;

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

    [Range(1, 4)]
    public int playerCount = 2;
    public bool[] isAIPlayer = new bool[4] { false, true, false, false };

    [Networked, Capacity(4)]
    private NetworkArray<NetworkObject> NetworkPlayers { get; }

    [HideInInspector] public List<PlayerManager> players = new List<PlayerManager>();
    [HideInInspector] public PlayerManager localPlayer;
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

    private bool hasCombatBeenShortened = false;
    private bool _isSpawned = false;

    /// <summary>
    /// 이 NetworkBehaviour가 네트워크 상에 스폰될 때 Fusion에 의해 호출됩니다.
    /// </summary>
    public override void Spawned()
    {
        Debug.Log("11111111111111111111111111111111111111");
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

        // ▼▼▼ 2. Spawned가 성공적으로 호출되었으므로 플래그를 true로 설정합니다. ▼▼▼
        _isSpawned = true;
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
        else if (currentState == GameState.Combat && !hasCombatBeenShortened && players.All(p => p != null && !p.IsActivelyFighting))
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
        // [수정] ChangeDetector를 사용하여 네트워크 변수의 변경을 감지합니다.
        foreach (var propertyName in _changeDetector.DetectChanges(this))
        {
            // currentState 프로퍼티가 변경되었을 때
            if (propertyName == nameof(currentState))
            {
                // 변경에 따른 로직을 처리하는 함수를 호출합니다.
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
        await UniTask.WaitUntil(() => localPlayer != null && players.Count > 0);
        Debug.Log($"플레이어 연결 완료. 로컬 플레이어: Player {localPlayer.playerId}, 총 플레이어 수: {players.Count}");

        Rpc_SetupGameUI();

        // UI 설정이 완료될 때까지 잠시 대기
        await UniTask.Delay(100);

        currentState = GameState.DataLoading;
        var loadingTasks = players.Select(p => p.shopManager.WaitUntilDatabaseLoaded());
        await UniTask.WhenAll(loadingTasks);
        Debug.Log("모든 데이터 로딩 완료. 첫 라운드를 시작합니다.");

        StartNextRound();
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
        var playerRefs = Runner.ActivePlayers.ToList();

        // [수정] 싱글플레이 모드 지원: SessionInfo가 null일 수 있으므로 Inspector 값을 우선 사용
        if (Runner.SessionInfo != null && Runner.SessionInfo.MaxPlayers > 0)
        {
            playerCount = Runner.SessionInfo.MaxPlayers;
        }
        // playerCount는 Inspector에서 설정된 값을 유지 (싱글플레이 모드에서 사용)

        Debug.Log($"총 {playerCount}명의 플레이어 생성 예정 (현재 접속: {playerRefs.Count}명, 나머지는 AI)");

        for (int i = 0; i < playerCount; i++)
        {
            Vector3 playerPosition = player1BasePosition + playerOffset * i;
            bool isAI = i >= playerRefs.Count || (i < isAIPlayer.Length && isAIPlayer[i]);
            PlayerRef inputAuthority = isAI ? PlayerRef.None : playerRefs[i];

            NetworkObject gridNO = await Runner.SpawnAsync(gridPrefab, playerPosition, Quaternion.identity);
            NetworkObject playerNO = await Runner.SpawnAsync(playerManagerPrefab, playerPosition, Quaternion.identity, inputAuthority);

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
        players.Clear();
        foreach (var playerNO in NetworkPlayers)
        {
            if(playerNO != null)
                players.Add(playerNO.GetComponent<PlayerManager>());
        }

        // [수정] 싱글플레이 모드 지원: InputAuthority가 있는 플레이어를 찾고, 없으면 첫 번째 플레이어를 로컬 플레이어로 설정
        localPlayer = players.FirstOrDefault(p => p != null && p.Object.HasInputAuthority);

        // 싱글플레이 모드(Shared 모드)에서는 모든 플레이어가 InputAuthority를 가지지 않을 수 있음
        if (localPlayer == null && players.Count > 0)
        {
            localPlayer = players[0];
            Debug.Log($"[싱글플레이 모드] 첫 번째 플레이어를 로컬 플레이어로 설정: Player {localPlayer.playerId}");
        }

        if (playerCount == 2 && players.Count == 2)
        {
            players[0].opponentManager = players[1];
            players[1].opponentManager = players[0];
        }
        Debug.Log($"객체 연결 완료. 총 {players.Count}명의 플레이어 발견. 로컬 플레이어: Player {localPlayer?.playerId}");
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

        foreach (var player in players)
        {
            player.AddGold(baseGoldPerRound + GetInterest(player.GetGold()));
            player.shopManager.Reroll(true);
        }

        if (currentRound >= 1)
        {
            foreach (var player in players) player.augmentManager.PresentAugments();
        }

        phaseTimer = TickTimer.CreateFromSeconds(Runner, preparePhaseTime);
    }

    private void StartCombatPhase()
    {
        if (!Object.HasStateAuthority) return;
        if (currentState == GameState.GameOver) return;

        currentState = GameState.Combat;
        hasCombatBeenShortened = false;

        foreach (var player in players)
        {
            player.monsterSpawner.SpawnWave(currentRound);
        }

        phaseTimer = TickTimer.CreateFromSeconds(Runner, combatTime);
    }

    /// <summary>
    /// [수정] ChangeDetector에 의해 호출되는 새로운 게임 상태 처리 함수입니다.
    /// </summary>
    private void OnGameStateChanged(GameState newState)
    {
        Debug.Log($"--- 라운드 {currentRound}: <color=yellow>{newState}</color> 단계 시작 ---");

        GameEvents.TriggerGameStateChanged(newState);

        HandleUIForNewState(newState).Forget();
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
                if (currentRound >= 1)
                {
                    if (augmentSelectionUI != null)
                    {
                        if (localPlayerShopUIGameObject != null) localPlayerShopUIGameObject.SetActive(false);
                        await UIManagers.Instance.GetUIElement("UI_Pnl_Augment");
                        GameEvents.TriggerAugmentPhaseStart(localPlayer, localPlayer.augmentManager.GetPresentedAugments());
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
                PlayerManager winner = players.FirstOrDefault(p => p != null && p.GetHealth() > 0);
                if (localPlayer != null && localPlayer.GetHealth() <= 0) await UIManagers.Instance.GetUIElement("UI_Pnl_Defeat");
                else if (localPlayer == winner) await UIManagers.Instance.GetUIElement("UI_Pnl_Victory");
                break;
        }
    }

    public GameState GetGameState() => currentState;
    public PlayerManager GetPlayer(int id) => players.FirstOrDefault(p => p.playerId == id);

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

        var alivePlayers = players.Where(p => p != null && p.GetHealth() > 0).ToList();
        if (alivePlayers.Count <= 1)
        {
            currentState = GameState.GameOver;
            phaseTimer = TickTimer.None;

            PlayerManager winner = alivePlayers.FirstOrDefault();
            Debug.Log(winner != null ? $"게임 종료! 승자: Player {winner.playerId}" : "게임 종료! 무승부입니다.");
        }
    }

    private int GetInterest(int gold) => Mathf.Min(gold / 10, maxInterest);
    public List<PlayerManager> GetRankedPlayers() => players.Where(p => p != null).OrderByDescending(p => p.GetHealth()).ThenBy(p => p.gameObject.name).ToList();

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
            return; // 아직 스폰되지 않았으면 아무것도 그리지 않고 함수를 종료합니다.
        }

        GUI.Label(new Rect(20, 270, 180, 40), $"현재 상태: {currentState}");
        GUI.Label(new Rect(20, 290, 180, 40), $"남은 시간: {currentPhaseTimer:F1}");
    }
}
