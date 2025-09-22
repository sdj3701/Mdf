// Assets/Scripts/Managers/GameManagers.cs
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Cysharp.Threading.Tasks;

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

    [Header("생성할 프리팹")]
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
    
    private bool hasCombatBeenShortened = false;

    private void Update()
    {
        // 서버로부터 수신하여 큐에 쌓인 커맨드들을 실행합니다.
        if (CommandProcessor != null)
        {
            CommandProcessor.ProcessCommands();
        }
    }

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
            StartCoroutine(GameFlow());
        }
    }

    public GameState GetGameState()
    {
        return currentState;
    }
    
    public PlayerManager GetPlayer(int id)
    {
        var player = players.FirstOrDefault(p => p.playerId == id);
        if (player == null)
        {
            Debug.LogWarning($"GameManagers: ID '{id}'에 해당하는 플레이어를 찾을 수 없습니다.");
        }
        return player;
    }


    private IEnumerator GameFlow()
    {
        ChangeState(GameState.Setup);
        SetupPlayersAndGrids();
        yield return null;

        yield return SetupGameUI().ToCoroutine();
        
        ChangeState(GameState.DataLoading);
        yield return WaitForDataLoading();

        StartCoroutine(GameLoop());
    }
    
    private void SetupPlayersAndGrids()
    {
        players.Clear();
        localPlayer = null;

        for (int i = 0; i < playerCount; i++)
        {
            Vector3 playerPosition = player1BasePosition + playerOffset * i;
            GameObject playerGO = Instantiate(playerManagerPrefab, playerPosition, Quaternion.identity);
            bool isAI = i < isAIPlayer.Length && isAIPlayer[i];
            playerGO.name = $"Player {i + 1}" + (isAI ? " (AI)" : "");
            
            PlayerManager newPlayer = playerGO.GetComponent<PlayerManager>();
            GameObject gridGO = Instantiate(gridPrefab, playerPosition, Quaternion.identity);
            gridGO.name = $"Grid {i + 1}";
            newPlayer.InitializePlayer(i, gridGO, defaultMonsterPrefab);

            if (isAI)
            {
                var aiController = playerGO.AddComponent<AIPlayerController>();
                aiController.Initialize(newPlayer, this.CommandProcessor);
            }

            if (localPlayer == null && !isAI)
            {
                localPlayer = newPlayer;
            }
            
            players.Add(newPlayer);
        }

        if (localPlayer == null && players.Count > 0)
        {
            localPlayer = players[0]; // Fallback: if all players are AI, treat P1 as the local player for observation.
        }
        
        // Setup opponent logic for 2 players. For more players, opponent-targeting effects may not work as intended
        // without changes to PlayerManager to support multiple opponents.
        if (playerCount == 2)
        {
            players[0].opponentManager = players[1];
            players[1].opponentManager = players[0];
        }

        if (localPlayer != null)
        {
            Debug.Log($"플레이어와 그리드 자동 생성 및 설정 완료. 총 {playerCount}명. 로컬 플레이어는 Player {localPlayer.playerId} 입니다.");
        }
        else
        {
            Debug.LogWarning("플레이어 생성에 실패했거나 로컬 플레이어를 찾을 수 없습니다.");
        }
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
                foreach (var player in players)
                {
                    player.augmentManager.PresentAugments();
                }

                if(augmentSelectionUI != null)
                {
                    if (localPlayerShopUIGameObject != null)
                    {
                        localPlayerShopUIGameObject.SetActive(false);
                    }
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
            if (localPlayerShopUIGameObject != null)
            {
                localPlayerShopUI.SetContentVisibility(false);
            }
            
            if (currentState == GameState.GameOver) break;

            ChangeState(GameState.Combat);
            foreach (var player in players)
            {
                player.monsterSpawner.SpawnWave(currentRound);
            }

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

            if (currentState == GameState.Combat && !hasCombatBeenShortened &&
                players.Count > 0 && players.All(p => p != null && !p.IsActivelyFighting))
            {
                if (_currentPhaseTimer > 3f)
                {
                    _currentPhaseTimer = 3f;
                    hasCombatBeenShortened = true;
                    Debug.Log("<color=cyan>모든 전투 종료! 남은 시간을 3초로 단축합니다.</color>");
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

        if (newState == GameState.Combat)
        {
            hasCombatBeenShortened = false;
        }
  
        GameEvents.TriggerGameStateChanged(newState);
    }

    public void OnMonsterReachedGoal(PlayerManager failedPlayer)
    {
        if (currentState == GameState.GameOver) return;
        Debug.Log($"Player {failedPlayer.playerId}가 몬스터를 놓쳤습니다!");
        int damageOnLeak = 1;
        failedPlayer.TakeDamage(damageOnLeak);
    }

    public async void GameOver(PlayerManager loser)
    {
        if (currentState == GameState.GameOver) return;

        var alivePlayers = players.Where(p => p != null && p.GetHealth() > 0).ToList();

        if (alivePlayers.Count > 1)
        {
            Debug.Log($"Player {loser.playerId}가 패배했습니다! 남은 플레이어: {alivePlayers.Count}명");
            return;
        }

        ChangeState(GameState.GameOver);
        PlayerManager winner = alivePlayers.FirstOrDefault();

        if (winner != null)
        {
            Debug.Log($"<color=red>게임 종료!</color> 승자: Player {winner.playerId}");
        }
        else
        {
            Debug.Log("<color=red>게임 종료! 무승부입니다.</color>");
        }
        
        StopAllCoroutines();

        if (localPlayer != null && localPlayer.GetHealth() <= 0)
        {
            await UIManagers.Instance.GetUIElement("UI_Pnl_Defeat");
        }
        else if (localPlayer == winner)
        {
            await UIManagers.Instance.GetUIElement("UI_Pnl_Victory");
        }
    }

    private int GetInterest(int gold) => Mathf.Min(gold / 10, maxInterest);

    public List<PlayerManager> GetRankedPlayers()
    {
        return players.Where(p => p != null)
                      .OrderByDescending(p => p.GetHealth())
                      .ThenBy(p => p.gameObject.name)
                      .ToList();
    }

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
