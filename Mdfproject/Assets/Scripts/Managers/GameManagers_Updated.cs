/*// Assets/Scripts/Managers/GameManagers.cs - 수정된 버전
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using UnityEngine.UI;

public class GameManagers : MonoBehaviour
{
    public static GameManagers Instance { get; private set; }

    #region 인게임 관련 변수
    public enum GameState { Prepare, Combat, Augment, GameOver }
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
    public float prepareTime = 20f;
    public float combatTime = 60f;
    public float augmentTime = 15f;
    [Header("라운드 보상")]
    public int baseGoldPerRound = 5;
    #endregion

    #region 로비 및 UI 관련 변수
    [Header("로비 캐릭터 선택")]
    public Button[] SelectCharacterButton;
    private Queue<string> characterselectdata = new Queue<string>();
    private List<string> selectCharacterName = new List<string>();
    private int maxqueue = 3;
    #endregion

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

    #region 개선된 게임 로직
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
            StartCoroutine(SetupGame());
        }
    }

    public GameState GetGameState()
    {
        return currentState;
    }

    private IEnumerator SetupGame()
    {
        yield return null;
        FindAndSetupPlayers();
        if (player1 != null && player2 != null)
        {
            StopAllCoroutines();
            StartCoroutine(GameLoop());
        }
        else
        {
            Debug.LogError("플레이어 설정 실패로 게임 루프를 시작할 수 없습니다.");
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

    private IEnumerator GameLoop()
    {
        while (currentState != GameState.GameOver)
        {
            // --- 준비 단계 ---
            ChangeState(GameState.Prepare);
            player1.AddGold(baseGoldPerRound + GetInterest(player1.GetGold()));
            player2.AddGold(baseGoldPerRound + GetInterest(player2.GetGold()));
            player1.shopManager.Reroll(true);
            player2.shopManager.Reroll(true);

            // ✅ [개선] UI 시스템을 안전하게 사용
            yield return StartCoroutine(SafelyShowUI("UI_Pnl_Shop"));
            yield return StartCoroutine(PhaseTimerCoroutine(prepareTime));
            yield return StartCoroutine(SafelyHideUI("UI_Pnl_Shop"));
            
            // --- 전투 단계 ---
            ChangeState(GameState.Combat);
            player1.monsterSpawner.SpawnWave(currentRound);
            player2.monsterSpawner.SpawnWave(currentRound);
            yield return StartCoroutine(PhaseTimerCoroutine(combatTime));

            if (currentState == GameState.GameOver) break;

            // --- 증강 단계 ---
            ChangeState(GameState.Augment);
            player1.augmentManager.PresentAugments();
            player2.augmentManager.PresentAugments();
            yield return StartCoroutine(SafelyShowUI("AugmentPanel"));
            yield return StartCoroutine(PhaseTimerCoroutine(augmentTime));
            yield return StartCoroutine(SafelyHideUI("AugmentPanel"));
            
            currentRound++;
        }
    }

    // ✅ [새로운 함수] UI를 안전하게 표시하는 함수
    private IEnumerator SafelyShowUI(string uiName)
    {
        if (UIManagers.Instance != null)
        {
            var uiTask = UIManagers.Instance.GetUIElement(uiName);
            yield return new WaitUntil(() => uiTask.Status != Cysharp.Threading.Tasks.UniTaskStatus.Pending);
            
            if (uiTask.Status == Cysharp.Threading.Tasks.UniTaskStatus.Succeeded)
            {
                Debug.Log($"✅ UI 표시 성공: {uiName}");
            }
            else
            {
                Debug.LogWarning($"⚠️ UI 표시 실패: {uiName}");
            }
        }
    }

    // ✅ [새로운 함수] UI를 안전하게 숨기는 함수  
    private IEnumerator SafelyHideUI(string uiName)
    {
        if (UIManagers.Instance != null)
        {
            UIManagers.Instance.ReturnUIElement(uiName);
            Debug.Log($"✅ UI 숨김 완료: {uiName}");
        }
        yield return null;
    }

    private IEnumerator PhaseTimerCoroutine(float duration)
    {
        _currentPhaseTimer = duration;
        while (_currentPhaseTimer > 0)
        {
            _currentPhaseTimer -= Time.deltaTime;
            yield return null;
        }
        _currentPhaseTimer = 0;
    }

    public void ChangeState(GameState newState)
    {
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
        
        // ✅ [개선] 게임 종료 시 리소스 정리
        CleanupGameResources();
    }

    // ✅ [새로운 함수] 게임 종료 시 리소스 정리
    private void CleanupGameResources()
    {
        // Addressable 리소스 정리
        if (AddressablesManager.Instance != null)
        {
            AddressablesManager.Instance.ReleaseAllAssets();
        }
        
        // UI 정리는 씬 전환 시 자동으로 처리됨
        Debug.Log("✅ 게임 리소스 정리 완료");
    }

    private int GetInterest(int gold) => Mathf.Min(gold / 10, 5);

    #endregion

    #region 로비 관련 함수 (변경 없음)
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
*/