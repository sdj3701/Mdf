// Assets/Scripts/Managers/GameManagers.cs

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Cysharp.Threading.Tasks;
using Fusion; // Fusion 네임스페이스 추가
using System.Threading.Tasks; // Task 네임스페이스 추가

public class GameManagers : MonoBehaviour
{
    public static GameManagers Instance { get; private set; }
    public CommandProcessor CommandProcessor { get; private set; }

    #region 인게임 관련 변수
    public enum GameState { Setup, DataLoading, Prepare, Combat, GameOver }
    [Header("게임 상태")]
    [SerializeField] private GameState currentState;
    public int currentRound = 1;
    [Header("현재 페이즈 타이머 (읽기 전용)")]
    [SerializeField] private float _currentPhaseTimer;
    public float currentPhaseTimer => _currentPhaseTimer;

    [Header("플레이어 설정")]
    [Range(1, 4)]
    public int playerCount = 2;
    public bool[] isAIPlayer = new bool[4] { false, true, false, false };

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

    private bool buildTestCheck;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            CommandProcessor = new CommandProcessor();
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    // [수정] Start 메서드는 더 이상 필요하지 않거나 다른 초기화 로직을 담을 수 있습니다.
    private void Start()
    {
        networkManager = NetworkManager.Instance;
    }

    private void Update()
    {
        if (CommandProcessor != null)
        {
            CommandProcessor.ProcessCommands();
        }
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        GameEvents.OnAugmentApplied += HandleAugmentChosen;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        GameEvents.OnAugmentApplied -= HandleAugmentChosen;
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == "Game" || FindObjectOfType<GameSceneInitializer>() != null)
        {
            StopAllCoroutines();
            // .Forget()은 UniTask의 확장 기능으로, 비동기 작업의 완료를 기다리지 않고 "일단 실행만 시키고 잊어버리는" 역할을 합니다. 오류가 발생해도 무시하므로, 반환값이 중요하지 않은 경우에 사용됩니다.
            GameFlow().Forget();
        }
    }

    // [수정] GameFlow 코루틴을 UniTask를 사용하는 비동기 메서드로 변경합니다.
    private async UniTask GameFlow()
    {
        ChangeState(GameState.Setup);
        // [수정] 이제 SetupPlayersAndGrids는 비동기 작업이므로 완료를 기다립니다.
        await SetupPlayersAndGrids();

        await SetupGameUI();

        ChangeState(GameState.DataLoading);
        await WaitForDataLoading();

        StartCoroutine(GameLoop());
    }

    // [수정] 로컬 생성에서 네트워크 스폰 방식으로 메서드 전체를 변경합니다.
    private async UniTask SetupPlayersAndGrids()
    {
        var runner = NetworkManager.Instance._runner;
        if (runner == null)
        {
            Debug.LogError("NetworkRunner가 없습니다! 셋업을 진행할 수 없습니다.");
            return;
        }

        // 이 로직은 호스트/서버만 실행하여 모든 객체를 생성합니다.
        if (runner.IsServer)
        {
            Debug.Log("호스트가 플레이어와 그리드 생성을 시작합니다.");
            players.Clear();
            var playerRefs = runner.ActivePlayers.ToList();
            playerCount = runner.SessionInfo.MaxPlayers; // 세션의 최대 플레이어 수로 설정

            for (int i = 0; i < playerCount; i++)
            {
                Vector3 playerPosition = player1BasePosition + playerOffset * i;

                // 이 슬롯이 AI용인지, 실제 플레이어용인지 결정합니다.
                bool isAI = i >= playerRefs.Count || (i < isAIPlayer.Length && isAIPlayer[i]);
                PlayerRef inputAuthority = isAI ? PlayerRef.None : playerRefs[i];

                // 그리드를 네트워크 객체로 스폰합니다.
                NetworkObject gridNO = await runner.SpawnAsync(gridPrefab, playerPosition, Quaternion.identity);

                // PlayerManager를 네트워크 객체로 스폰하고, 실제 플레이어에게 입력 권한을 부여합니다.
                NetworkObject playerNO = await runner.SpawnAsync(playerManagerPrefab, playerPosition, Quaternion.identity, inputAuthority);

                PlayerManager newPlayer = playerNO.GetComponent<PlayerManager>();
                if (newPlayer != null)
                {
                    // 모든 클라이언트에서 초기화 로직이 실행되도록 RPC를 호출합니다.
                    newPlayer.Rpc_InitializePlayer(i, gridNO);
                }

                // AI 관련 로직은 서버에만 존재합니다.
                if (isAI)
                {
                    playerNO.name = $"Player {i + 1} (AI)";
                    var aiController = playerNO.gameObject.AddComponent<AIPlayerController>();
                    aiController.Initialize(newPlayer, this.CommandProcessor);
                }
                else
                {
                    playerNO.name = $"Player {i + 1}";
                }
            }
        }

        // 모든 클라이언트(호스트 포함)는 생성된 객체들을 찾아서 로컬 리스트에 연결해야 합니다.
        await LinkSpawnedObjects();
    }

    /// <summary>
    /// 모든 클라이언트에서 실행되어, 네트워크를 통해 생성된 PlayerManager 객체들을
    /// 로컬 players 리스트에 연결하고 localPlayer를 설정합니다.
    /// </summary>
    private async UniTask LinkSpawnedObjects()
    {
        Debug.Log("생성된 네트워크 객체들을 연결하는 중...");
        // 씬에 필요한 모든 PlayerManager가 스폰될 때까지 기다립니다.
        while (FindObjectsOfType<PlayerManager>().Length < playerCount)
        {
            await UniTask.Yield();
        }

        // 모든 PlayerManager를 찾은 후, playerId를 기준으로 정렬하여 모든 클라이언트에서 동일한 순서를 보장합니다.
        players = FindObjectsOfType<PlayerManager>().OrderBy(p => p.playerId).ToList();

        // 각 클라이언트는 자신이 입력 권한을 가진 PlayerManager를 찾아 로컬 플레이어로 설정합니다.
        localPlayer = players.FirstOrDefault(p => p.Object.HasInputAuthority);

        // 상대방 참조를 설정합니다 (2인용 게임 기준).
        if (playerCount == 2 && players.Count == 2)
        {
            players[0].opponentManager = players[1];
            players[1].opponentManager = players[0];
        }

        Debug.Log($"객체 연결 완료. 총 {players.Count}명의 플레이어 발견. 로컬 플레이어: Player {localPlayer?.playerId}");
    }

    // ... (이하 나머지 코드는 기존과 동일)

    public GameState GetGameState() => currentState;
    public PlayerManager GetPlayer(int id) => players.FirstOrDefault(p => p.playerId == id);

    private async UniTask SetupGameUI()
    {
        try
        {
            Debug.Log($"<color=red> Create UI </color>");
            buildTestCheck = true;
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

    private IEnumerator WaitForDataLoading()
    {
        if (players.Count == 0)
        {
            Debug.LogError("플레이어가 설정되지 않아 데이터 로딩을 시작할 수 없습니다.");
            yield break;
        }

        Debug.Log("모든 플레이어의 데이터 로딩을 기다립니다...");
        var loadingTasks = players.Select(p => p.shopManager.WaitUntilDatabaseLoaded());
        yield return UniTask.WhenAll(loadingTasks).ToCoroutine();
        Debug.Log("모든 데이터 로딩 완료. 게임 루프를 시작합니다.");
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

    private IEnumerator GameLoop()
    {
        while (currentState != GameState.GameOver)
        {
            GameEvents.TriggerRoundStart(currentRound);
            ChangeState(GameState.Prepare);

            foreach (var player in players)
            {
                player.AddGold(baseGoldPerRound + GetInterest(player.GetGold()));
                player.shopManager.Reroll(true);
            }

            if (currentRound >= 1)
            {
                foreach (var player in players) player.augmentManager.PresentAugments();
                if (augmentSelectionUI != null)
                {
                    if (localPlayerShopUIGameObject != null) localPlayerShopUIGameObject.SetActive(false);
                    yield return UIManagers.Instance.GetUIElement("UI_Pnl_Augment").ToCoroutine();
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

            yield return StartCoroutine(PhaseTimerCoroutine(preparePhaseTime));

            UIManagers.Instance.ReturnUIElement("UI_Pnl_Augment");
            if (localPlayerShopUIGameObject != null) localPlayerShopUI.SetContentVisibility(false);

            if (currentState == GameState.GameOver) break;

            ChangeState(GameState.Combat);
            foreach (var player in players) player.monsterSpawner.SpawnWave(currentRound);

            yield return StartCoroutine(PhaseTimerCoroutine(combatTime));
            if (currentState == GameState.GameOver) break;
            currentRound++;
        }
        Debug.Log("게임 루프가 종료되었습니다.");
    }

    private IEnumerator PhaseTimerCoroutine(float duration)
    {
        _currentPhaseTimer = duration;
        while (_currentPhaseTimer > 0)
        {
            _currentPhaseTimer -= Time.deltaTime;
            if (currentState == GameState.Combat && !hasCombatBeenShortened && players.All(p => p != null && !p.IsActivelyFighting))
            {
                if (_currentPhaseTimer > 3f)
                {
                    _currentPhaseTimer = 3f;
                    hasCombatBeenShortened = true;
                }
            }
            if (currentState == GameState.GameOver) yield break;
            yield return null;
        }
        _currentPhaseTimer = 0;
    }

    public void ChangeState(GameState newState)
    {
        if (currentState == newState) return;
        currentState = newState;
        Debug.Log($"--- 라운드 {currentRound}: <color=yellow>{newState}</color> 단계 시작 ---");
        if (newState == GameState.Combat) hasCombatBeenShortened = false;
        GameEvents.TriggerGameStateChanged(newState);
    }

    public void OnMonsterReachedGoal(PlayerManager failedPlayer)
    {
        if (currentState == GameState.GameOver) return;
        failedPlayer.TakeDamage(1);
    }

    public async void GameOver(PlayerManager loser)
    {
        if (currentState == GameState.GameOver) return;
        var alivePlayers = players.Where(p => p != null && p.GetHealth() > 0).ToList();
        if (alivePlayers.Count > 1) return;

        ChangeState(GameState.GameOver);
        PlayerManager winner = alivePlayers.FirstOrDefault();
        Debug.Log(winner != null ? $"게임 종료! 승자: Player {winner.playerId}" : "게임 종료! 무승부입니다.");
        StopAllCoroutines();

        if (localPlayer != null && localPlayer.GetHealth() <= 0) await UIManagers.Instance.GetUIElement("UI_Pnl_Defeat");
        else if (localPlayer == winner) await UIManagers.Instance.GetUIElement("UI_Pnl_Victory");
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
        // 현재 상태를 화면에 텍스트로 표시합니다.
        GUI.Label(new Rect(20, 270, 180, 40), $"현재 상태: {buildTestCheck}");

    }
}