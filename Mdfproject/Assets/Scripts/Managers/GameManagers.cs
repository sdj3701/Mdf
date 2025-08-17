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
    public enum GameState { Setup, DataLoading, Prepare, Combat, GameOver } // ✅ DataLoading 상태 추가
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
        if (player1 != null && player1.augmentManager != null)
        {
            player1.augmentManager.OnAugmentChosen -= HandleAugmentChosen;
        }
        if (player2 != null && player2.augmentManager != null)
        {
            player2.augmentManager.OnAugmentChosen -= HandleAugmentChosen;
        }
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == "Game" || scene.name == "TestScene_GameManager")
        {
            StopAllCoroutines();
            StartCoroutine(GameFlow()); // ✅ 게임 전체 흐름을 관리하는 새 코루틴 호출
        }
    }

    public GameState GetGameState()
    {
        return currentState;
    }

    // ✅ 게임 전체 흐름을 관리하는 최상위 코루틴
    private IEnumerator GameFlow()
    {
        // 1. 셋업
        ChangeState(GameState.Setup);
        FindAndSetupPlayers();
        yield return null; // 한 프레임 대기

        // 2. UI 초기화
        yield return SetupGameUI().ToCoroutine();
        
        // 3. 비동기 데이터 로딩 대기
        ChangeState(GameState.DataLoading);
        yield return WaitForDataLoading();

        // 4. 메인 게임 루프 시작
        StartCoroutine(GameLoop());
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

            if (player1.augmentManager != null) player1.augmentManager.OnAugmentChosen -= HandleAugmentChosen;
            if (player2.augmentManager != null) player2.augmentManager.OnAugmentChosen -= HandleAugmentChosen;
            
            if (player1.augmentManager != null) player1.augmentManager.OnAugmentChosen += HandleAugmentChosen;
            if (player2.augmentManager != null) player2.augmentManager.OnAugmentChosen += HandleAugmentChosen;

            Debug.Log("플레이어 설정 완료. 로컬 플레이어는 Player " + localPlayer.playerId + " 입니다.");
        }
        else
        {
            Debug.LogError("플레이어 설정 실패! 게임을 시작할 수 없습니다.");
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

    // ✅ 데이터 로딩을 기다리는 코루틴 헬퍼 함수
    private IEnumerator WaitForDataLoading()
    {
        if(player1 == null || player2 == null)
        {
            Debug.LogError("플레이어가 설정되지 않아 데이터 로딩을 시작할 수 없습니다.");
            yield break;
        }

        Debug.Log("모든 플레이어의 데이터 로딩을 기다립니다...");
        // UniTask를 코루틴에서 기다리기 위해 ToCoroutine() 사용
        yield return UniTask.WhenAll(
            player1.shopManager.WaitUntilDatabaseLoaded(), 
            player2.shopManager.WaitUntilDatabaseLoaded()
            // 필요하다면 AugmentManager의 로딩도 여기에 추가할 수 있습니다.
        ).ToCoroutine();
        Debug.Log("모든 데이터 로딩 완료. 게임 루프를 시작합니다.");
    }

    private void HandleAugmentChosen()
    {
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
            ChangeState(GameState.Prepare);
            
            // 골드 지급 및 무료 리롤 (데이터 로딩이 보장된 상태)
            player1.AddGold(baseGoldPerRound + GetInterest(player1.GetGold()));
            player2.AddGold(baseGoldPerRound + GetInterest(player2.GetGold()));
            player1.shopManager.Reroll(true);
            player2.shopManager.Reroll(true);

            if (currentRound >= 1)
            {
                player1.augmentManager.PresentAugments();
                if(augmentSelectionUI != null)
                {
                    augmentSelectionUI.SetAugmentChoices(localPlayer.augmentManager.GetPresentedAugments());
                    if (localPlayerShopUIGameObject != null)
                    {
                        localPlayerShopUIGameObject.SetActive(false);
                    }
                    UIManagers.Instance.GetUIElement("UI_Pnl_Augment");
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

    /// <summary>
    /// [추가] 몬스터가 목표 지점에 도달했을 때 Monster.cs에 의해 호출되는 중앙 처리 함수입니다.
    /// </summary>
    /// <param name="failedPlayer">수비에 실패한 플레이어</param>
    public void OnMonsterReachedGoal(PlayerManager failedPlayer)
    {
        // 게임이 이미 끝난 상태라면 아무것도 처리하지 않습니다.
        if (currentState == GameState.GameOver) return;

        Debug.Log($"Player {failedPlayer.playerId}가 몬스터를 놓쳤습니다!");

        // 몬스터를 놓쳤을 때의 피해량을 여기에 정의합니다. (예: 1의 데미지)
        int damageOnLeak = 1;
        
        // 해당 플레이어에게 데미지를 적용합니다.
        // PlayerManager.TakeDamage() 내부에서 체력이 0이 되면 GameOver()를 호출할 것입니다.
        failedPlayer.TakeDamage(damageOnLeak);
    }

    /// <summary>
    /// [수정] 이제 이 함수는 PlayerManager에 의해서만 호출되며, 게임 오버 UI를 띄우는 최종 책임을 가집니다.
    /// </summary>
    public void GameOver(PlayerManager loser)
    {
        if (currentState == GameState.GameOver) return;
        ChangeState(GameState.GameOver);
        PlayerManager winner = (loser == player1) ? player2 : player1;
        Debug.Log($"<color=red>게임 종료!</color> 승자: Player {winner.playerId}");

        // --- [역할 이전] 게임 오버 UI를 띄우는 로직을 이곳에서 중앙 관리합니다. ---
        // 예시: UIManagers.Instance.GetUIElement("UI_Pnl_GameOver");
        // 이 UI에는 승자와 패자 정보를 표시하는 텍스트가 포함될 수 있습니다.

        StopAllCoroutines();
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