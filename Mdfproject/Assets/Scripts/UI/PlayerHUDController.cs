// Assets/Scripts/UI/PlayerHUDController.cs
using UnityEngine;
using TMPro;

/// <summary>
/// 플레이어의 주요 정보(골드, 라운드 등)를 표시하는 HUD UI를 제어합니다.
/// Update() 대신 GameEvents를 구독하여 정보를 갱신합니다.
/// </summary>
public class PlayerHUDController : MonoBehaviour
{
    [Header("HUD UI 요소")]
    [Tooltip("골드를 표시할 TextMeshPro UI 요소를 연결하세요.")]
    public TextMeshProUGUI goldText;

    [Tooltip("현재 라운드를 표시할 TextMeshPro UI 요소를 연결하세요.")]
    public TextMeshProUGUI roundText;

    private PlayerManager localPlayer;
    private GameManagers gameManager;

    void Start()
    {
        // 게임 시작 시 필요한 참조를 찾고 UI의 초기 값을 설정합니다.
        gameManager = GameManagers.Instance;
        if (gameManager != null)
        {
            localPlayer = gameManager.localPlayer;
        }
        else
        {
            Debug.LogError("GameManagers 인스턴스를 찾을 수 없습니다! HUD가 작동하지 않을 수 있습니다.");
            this.enabled = false;
            return;
        }

        // UI 초기값 설정
        if (localPlayer != null)
        {
            UpdatePlayerStats(localPlayer.playerId, localPlayer.GetHealth(), localPlayer.GetGold());
        }
        if (gameManager != null)
        {
            UpdateRoundText(gameManager.currentRound);
        }
    }

    void OnEnable()
    {
        // 필요한 이벤트들을 구독합니다.
        GameEvents.OnPlayerStatsChanged += UpdatePlayerStats;
        GameEvents.OnRoundStart += UpdateRoundText;
    }

    void OnDisable()
    {
        // 오브젝트가 비활성화될 때 반드시 구독을 해지하여 메모리 누수를 방지합니다.
        GameEvents.OnPlayerStatsChanged -= UpdatePlayerStats;
        GameEvents.OnRoundStart -= UpdateRoundText;
    }

    /// <summary>
    /// OnPlayerStatsChanged 이벤트가 발생할 때 호출되어 골드와 체력 UI를 갱신합니다.
    /// </summary>
    private void UpdatePlayerStats(int playerID, int newHealth, int newGold)
    {
        // 이 이벤트가 로컬 플레이어에게 해당하는 것인지 확인합니다.
        if (localPlayer != null && localPlayer.playerId == playerID)
        {
            if (goldText != null)
            {
                goldText.text = newGold.ToString();
            }
            // 체력 UI가 있다면 여기서 갱신합니다.
            // if (healthText != null) healthText.text = newHealth.ToString();
        }
    }

    /// <summary>
    /// OnRoundStart 이벤트가 발생할 때 호출되어 라운드 UI를 갱신합니다.
    /// </summary>
    private void UpdateRoundText(int roundNumber)
    {
        if (roundText != null)
        {
            roundText.text = $"ROUND\n{roundNumber}";
        }
    }
}