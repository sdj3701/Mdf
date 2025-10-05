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

    void OnEnable()
    {
        // GameManagers가 준비될 때 이벤트를 구독합니다.
        GameEvents.OnGameManagersReady += OnGameManagersReady;
    }

    void OnDisable()
    {
        // 이벤트 구독 해제
        GameEvents.OnGameManagersReady -= OnGameManagersReady;
    }

    void Start()
    {
        // Start()에서도 시도해봅니다 (이미 준비되어 있을 수 있음)
        if (gameManager == null)
        {
            gameManager = GameManagers.Instance;
        }
        if (gameManager != null && localPlayer == null)
        {
            localPlayer = gameManager.localPlayer;
        }
    }

    private void OnGameManagersReady()
    {
        // GameManagers가 준비되면 참조를 저장합니다.
        if (gameManager == null)
        {
            gameManager = GameManagers.Instance;
        }
        if (gameManager != null && localPlayer == null)
        {
            localPlayer = gameManager.localPlayer;
        }
        Debug.Log("[PlayerHUDController] GameManagers 참조 획득 완료");
    }

    void Update()
    {
        // GameManager가 없으면 다시 시도합니다.
        if (gameManager == null)
        {
            gameManager = GameManagers.Instance;
            if (gameManager == null)
            {
                return;
            }
        }

        // localPlayer가 없으면 다시 시도합니다.
        if (localPlayer == null)
        {
            localPlayer = gameManager.localPlayer;
            if (localPlayer == null)
            {
                return;
            }
        }

        // 매 프레임 UI를 직접 업데이트합니다.
        if (goldText != null)
        {
            goldText.text = localPlayer.GetGold().ToString();
        }

        if (roundText != null)
        {
            roundText.text = $"ROUND\n{Mathf.Max(1, gameManager.currentRound)}";
        }
    }
}
