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
    public enum GameState { Setup, Augment, Prepare, Combat, GameOver }
    [Header("게임 상태")]
    [SerializeField] private GameState currentState;
    public int currentRound = 1;
    [Header("현재 페이즈 타이머 (읽기 전용)")]
    [SerializeField] private float _currentPhaseTimer;
    public float currentPhaseTimer => _currentPhaseTimer;
    [Header("플레이어 관리 (자동 할당)")]
    public PlayerManager player1;
    public PlayerManager player2;
    public PlayerManager localPlayer;
    #endregion

    #region 단계별 시간 및 보상
    [Header("단계별 시간 설정 (초)")]
    public float augmentTime = 15f;
    public float prepareTime = 30f;
    public float combatTime = 60f;

    [Header("라운드 보상")]
    public int baseGoldPerRound = 5;
    public int maxInterest = 5;
    #endregion

    #region 로비 및 UI 관련 변수 (기존 코드 유지)
    [Header("로비 캐릭터 선택")]
    public Button[] SelectCharacterButton;
    private Queue<string> characterselectdata = new Queue<string>();
    private List<string> selectCharacterName = new List<string>();
    private int maxqueue = 3;
    #endregion

    private ShopUIController localPlayerShopUI;
    private AugmentUIController augmentSelectionUI;


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
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == "Game" || scene.name == "TestScene_GameManager")
        {
            StopAllCoroutines();
            StartCoroutine(SetupGame());
        }
    }

    public GameState GetGameState()
    {
        return currentState;
    }

    private IEnumerator SetupGame()
    {
        ChangeState(GameState.Setup);
        yield return null;

        FindAndSetupPlayers();

        if (player1 != null && player2 != null)
        {
            SetupGameUI().Forget();
        }
        else
        {
            Debug.LogError("플레이어 설정 실패! 게임 루프를 시작할 수 없습니다.");
        }
    }

    private void FindAndSetupPlayers()
    {
        PlayerManager[] players = FindObjectsOfType<PlayerManager>();
        player1 = players.FirstOrDefault(p => p.playerId == 0);
        player2 = players.FirstOrDefault(p => p.playerId == 1);

        if (player1 != null && player2 != null)
        {
            player1.opponentManager = player2;
            player2.opponentManager = player1;
            localPlayer = player1;
            Debug.Log("플레이어 설정 완료. 로컬 플레이어는 Player " + localPlayer.playerId + " 입니다.");
        }
    }

    private async UniTaskVoid SetupGameUI()
    {
        try
        {
            var shopPanelTask = UIManagers.Instance.GetUIElement("UI_Pnl_Shop");
            var augmentPanelTask = UIManagers.Instance.GetUIElement("UI_Pnl_Augment");

            // ✅ [수정] 튜플 분해(Deconstruction) 문법으로 결과를 각각의 변수에 바로 할당합니다.
            // 이렇게 하면 코드가 더 깔끔하고 직관적입니다.
            var (shopPanelInstance, augmentPanelInstance) = await UniTask.WhenAll(shopPanelTask, augmentPanelTask);

            if (shopPanelInstance != null)
            {
                localPlayerShopUI = shopPanelInstance.GetComponent<ShopUIController>();
                localPlayerShopUI.SetContentVisibility(false);
            }

            if (augmentPanelInstance != null)
            {
                augmentSelectionUI = augmentPanelInstance.GetComponent<AugmentUIController>();
                UIManagers.Instance.ReturnUIElement("UI_Pnl_Augment");
            }

            StartCoroutine(GameLoop());
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"UI 설정 중 심각한 에러 발생: {ex.Message}");
        }
    }

    private IEnumerator GameLoop()
    {
        while (currentState != GameState.GameOver)
        {
            if (currentRound > 1)
            {
                ChangeState(GameState.Augment);
                player1.augmentManager.PresentAugments();
                // player2.augmentManager.PresentAugments();

                if(augmentSelectionUI != null)
                {
                    // TODO: AugmentManager에서 제시된 증강 목록을 가져와 UI에 설정하는 로직 필요
                    // 예시: augmentSelectionUI.SetAugmentChoices(localPlayer.augmentManager.GetPresentedAugments());
                    UIManagers.Instance.GetUIElement("UI_Pnl_Augment");
                }

                yield return StartCoroutine(PhaseTimerCoroutine(augmentTime));
                UIManagers.Instance.ReturnUIElement("UI_Pnl_Augment");
            }

            if (currentState == GameState.GameOver) break;

            ChangeState(GameState.Prepare);
            player1.AddGold(baseGoldPerRound + GetInterest(player1.GetGold()));
            player2.AddGold(baseGoldPerRound + GetInterest(player2.GetGold()));
            player1.shopManager.Reroll(true);
            player2.shopManager.Reroll(true);

            if (localPlayerShopUI != null)
            {
                localPlayerShopUI.UpdateShopSlots();
                localPlayerShopUI.SetContentVisibility(true);
            }

            yield return StartCoroutine(PhaseTimerCoroutine(prepareTime));

            if (localPlayerShopUI != null)
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
    }

    public void GameOver(PlayerManager loser)
    {
        if (currentState == GameState.GameOver) return;
        ChangeState(GameState.GameOver);
        PlayerManager winner = (loser == player1) ? player2 : player1;
        Debug.Log($"<color=red>게임 종료!</color> 승자: Player {winner.playerId}");
        StopAllCoroutines();
    }

    private int GetInterest(int gold) => Mathf.Min(gold / 10, maxInterest);

    #region 로비 관련 함수 (기존 코드 유지)
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