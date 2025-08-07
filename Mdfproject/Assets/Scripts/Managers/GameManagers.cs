// Assets/Scripts/Managers/GameManagers.cs
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class GameManagers : MonoBehaviour
{
    public static GameManagers Instance { get; private set; }

    #region 인게임 관련 변수
    public enum GameState { Prepare, Combat, Augment, GameOver }

    [Header("게임 상태")]
    [SerializeField] private GameState currentState;
    public int currentRound = 1;

    // --- 여기를 수정했습니다 ---
    // 1. 실제 데이터 저장을 위한 private 변수를 만듭니다.
    // 2. [SerializeField]를 사용해 private 변수를 인스펙터에서 볼 수 있게 합니다.
    [Header("현재 페이즈 타이머 (읽기 전용)")]
    [SerializeField] private float _currentPhaseTimer;
    
    // 3. 외부 스크립트가 안전하게 값을 읽어갈 수 있도록 public 프로퍼티를 유지합니다.
    //    이 프로퍼티는 private 변수의 값을 반환합니다.
    public float currentPhaseTimer => _currentPhaseTimer;
    // --- 수정 끝 ---


    [Header("플레이어 관리 (자동 할당)")]
    public PlayerManager player1;
    public PlayerManager player2;

    [Header("단계별 시간 설정 (초)")]
    public float prepareTime = 20f;
    public float combatTime = 60f;
    public float augmentTime = 15f;

    [Header("라운드 보상")]
    public int baseGoldPerRound = 5;
    #endregion

    #region 로비 관련 변수
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
            Debug.Log("Game 씬 로드 감지. 플레이어 설정 및 게임 루프를 시작합니다.");
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
        if (players.Length < 2)
        {
            Debug.LogError("오류: 게임 씬에서 2명의 플레이어를 찾을 수 없습니다!");
            return;
        }
        player1 = players.FirstOrDefault(p => p.playerId == 0);
        player2 = players.FirstOrDefault(p => p.playerId == 1);
        if (player1 == null || player2 == null)
        {
            Debug.LogError("오류: 플레이어 ID(0, 1) 할당에 실패했습니다.");
            return;
        }
        player1.opponentManager = player2;
        player2.opponentManager = player1;
        Debug.Log("플레이어 설정 완료: Player1, Player2 참조가 자동으로 연결되었습니다.");
    }

    private IEnumerator GameLoop()
    {
        while (currentState != GameState.GameOver)
        {
            ChangeState(GameState.Prepare);
            player1.AddGold(baseGoldPerRound + GetInterest(player1.GetGold()));
            player2.AddGold(baseGoldPerRound + GetInterest(player2.GetGold()));
            player1.shopManager.Reroll();
            player2.shopManager.Reroll();
            yield return StartCoroutine(PhaseTimerCoroutine(prepareTime)); 

            ChangeState(GameState.Combat);
            player1.monsterSpawner.SpawnWave(currentRound);
            player2.monsterSpawner.SpawnWave(currentRound);
            yield return StartCoroutine(PhaseTimerCoroutine(combatTime));

            if (currentState == GameState.GameOver) break;

            ChangeState(GameState.Augment);
            player1.augmentManager.PresentAugments();
            player2.augmentManager.PresentAugments();
            yield return StartCoroutine(PhaseTimerCoroutine(augmentTime)); 
            
            currentRound++;
        }
    }

    private IEnumerator PhaseTimerCoroutine(float duration)
    {
        _currentPhaseTimer = duration; // 값을 변경할 때는 private 변수에 직접 접근
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
    }

    private int GetInterest(int gold) => Mathf.Min(gold / 10, 5);

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