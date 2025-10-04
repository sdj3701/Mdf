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

    // OnEnable에서 이벤트를 구독합니다.
    void OnEnable()
    {
        GameEvents.OnGameManagersReady += Initialize;
    }

    // OnDisable에서 이벤트를 구독 해제합니다.
    void OnDisable()
    {
        GameEvents.OnGameManagersReady -= Initialize;
        if (isInitialized) // 초기화가 된 경우에만 다른 이벤트 구독 해제
        {
            GameEvents.OnPlayerStatsChanged -= UpdatePlayerStats;
            GameEvents.OnRoundStart -= UpdateRoundText;
        }
    }

    // GameManagers가 준비되면 이 메서드가 호출됩니다.
    private void Initialize()
    {
        if (isInitialized) return; // 중복 초기화 방지

        gameManager = GameManagers.Instance;
        if (gameManager != null)
        {
            localPlayer = gameManager.localPlayer;
            
            if (localPlayer != null)
            {
                UpdatePlayerStats(localPlayer.playerId, localPlayer.GetHealth(), localPlayer.GetGold());
                UpdateRoundText(gameManager.currentRound);

                GameEvents.OnPlayerStatsChanged += UpdatePlayerStats;
                GameEvents.OnRoundStart += UpdateRoundText;

                isInitialized = true;
                Debug.Log("PlayerHUDController 초기화 및 이벤트 구독 완료.");
            }
        }
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
