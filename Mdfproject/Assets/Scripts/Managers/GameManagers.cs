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

    #region 인게임 관련 변수
    public enum GameState { Setup, DataLoading, Prepare, Combat, GameOver }
    [Header("게임 상태")]
    [SerializeField] private GameState currentState;
    public int currentRound = 1;
    [Header("현재 페이즈 타이머 (읽기 전용)")]
    [SerializeField] private float _currentPhaseTimer;
    public float currentPhaseTimer => _currentPhaseTimer;
    
    [HideInInspector] public PlayerManager player1;
    [HideInInspector] public PlayerManager player2;
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

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
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
        GameEvents.OnAugmentSelected += HandleAugmentChosen;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        GameEvents.OnAugmentSelected -= HandleAugmentChosen;
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
        GameObject player1GO = Instantiate(playerManagerPrefab, player1BasePosition, Quaternion.identity);
        player1GO.name = "Player 1";
        player1 = player1GO.GetComponent<PlayerManager>();
        GameObject grid1GO = Instantiate(gridPrefab, player1BasePosition, Quaternion.identity);
        grid1GO.name = "Grid 1";
        player1.InitializePlayer(0, grid1GO, defaultMonsterPrefab);

        Vector3 player2Position = player1BasePosition + playerOffset;
        GameObject player2GO = Instantiate(playerManagerPrefab, player2Position, Quaternion.identity);
        player2GO.name = "Player 2";
        player2 = player2GO.GetComponent<PlayerManager>();
        GameObject grid2GO = Instantiate(gridPrefab, player2Position, Quaternion.identity);
        grid2GO.name = "Grid 2";
        player2.InitializePlayer(1, grid2GO, defaultMonsterPrefab);

        player1.opponentManager = player2;
        player2.opponentManager = player1;
        
        localPlayer = player1;
        Debug.Log("플레이어와 그리드 자동 생성 및 설정 완료. 로컬 플레이어는 Player " + localPlayer.playerId + " 입니다.");
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
        if(player1 == null || player2 == null)
        {
            Debug.LogError("플레이어가 설정되지 않아 데이터 로딩을 시작할 수 없습니다.");
            yield break;
        }

        Debug.Log("모든 플레이어의 데이터 로딩을 기다립니다...");
        yield return UniTask.WhenAll(
            player1.shopManager.WaitUntilDatabaseLoaded(), 
            player2.shopManager.WaitUntilDatabaseLoaded()
        ).ToCoroutine();
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
            
            player1.AddGold(baseGoldPerRound + GetInterest(player1.GetGold()));
            player2.AddGold(baseGoldPerRound + GetInterest(player2.GetGold()));
            player1.shopManager.Reroll(true);
            player2.shopManager.Reroll(true);

            if (currentRound >= 1)
            {
                player1.augmentManager.PresentAugments();
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
            player1.monsterSpawner.SpawnWave(currentRound);
            player2.monsterSpawner.SpawnWave(currentRound);

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
                player1 != null && !player1.IsActivelyFighting &&
                player2 != null && !player2.IsActivelyFighting)
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
        ChangeState(GameState.GameOver);
        PlayerManager winner = (loser == player1) ? player2 : player1;
        Debug.Log($"<color=red>게임 종료!</color> 승자: Player {winner.playerId}");
        StopAllCoroutines();

        if (localPlayer == loser)
        {
            // 패배 UI 표시
            await UIManagers.Instance.GetUIElement("UI_Pnl_Defeat");
        }
        else if (localPlayer == winner)
        {
            // 승리 UI 표시
            await UIManagers.Instance.GetUIElement("UI_Pnl_Victory");
        }
    }

    private int GetInterest(int gold) => Mathf.Min(gold / 10, maxInterest);

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
