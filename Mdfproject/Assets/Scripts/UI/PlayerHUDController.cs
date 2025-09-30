// Assets/Scripts/UI/PlayerHUDController.cs
using UnityEngine;
using TMPro;

public class PlayerHUDController : MonoBehaviour
{
    [Header("HUD UI 요소")]
    public TextMeshProUGUI goldText;
    public TextMeshProUGUI roundText;

    private PlayerManager localPlayer;
    private GameManagers gameManager;

    // [추가] 초기화가 완료되었는지 확인하기 위한 플래그
    private bool isInitialized = false;

    // [수정] Start() 메서드를 비워두거나 삭제합니다.
    // void Start() { }

    // [추가] Update() 메서드에서 초기화를 시도합니다.
    void Update()
    {
        // 아직 초기화되지 않았다면 매 프레임 초기화를 시도합니다.
        if (!isInitialized)
        {
            Initialize();
        }
    }

    // [추가] 초기화 로직을 별도의 메서드로 분리합니다.
    private void Initialize()
    {
        gameManager = GameManagers.Instance;
        if (gameManager != null)
        {
            localPlayer = gameManager.localPlayer;
            
            // localPlayer도 GameManagers가 초기화된 후에 할당되므로, 여기서 한 번 더 확인합니다.
            if (localPlayer != null)
            {
                // UI 초기값 설정
                UpdatePlayerStats(localPlayer.playerId, localPlayer.GetHealth(), localPlayer.GetGold());
                UpdateRoundText(gameManager.currentRound);

                // 이벤트 구독
                GameEvents.OnPlayerStatsChanged += UpdatePlayerStats;
                GameEvents.OnRoundStart += UpdateRoundText;

                // 초기화가 성공적으로 완료되었으므로 플래그를 true로 설정합니다.
                isInitialized = true;
                Debug.Log("PlayerHUDController 초기화 및 이벤트 구독 완료.");
            }
        }
    }

    // [수정] OnEnable/OnDisable을 Initialize와 분리하여 관리합니다.
    void OnDisable()
    {
        // isInitialized 여부와 관계없이 항상 구독 해제를 시도하여 안전성을 높입니다.
        GameEvents.OnPlayerStatsChanged -= UpdatePlayerStats;
        GameEvents.OnRoundStart -= UpdateRoundText;
    }

    private void UpdatePlayerStats(int playerID, int newHealth, int newGold)
    {
        // localPlayer가 아직 설정되지 않았을 수 있으므로 null 체크를 추가합니다.
        if (localPlayer != null && localPlayer.playerId == playerID)
        {
            if (goldText != null)
            {
                goldText.text = newGold.ToString();
            }
        }
    }
    
    private void UpdateRoundText(int roundNumber)
    {
        if (roundText != null)
        {
            // 라운드가 0일 경우 "ROUND 1"로 표시되도록 보정할 수 있습니다.
            roundText.text = $"ROUND\n{Mathf.Max(1, roundNumber)}";
        }
    }
}