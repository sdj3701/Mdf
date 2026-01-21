// Assets/Scripts/UI/PlayerHUDController.cs
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class PlayerHUDController : MonoBehaviour
{
    [Header("HUD UI 요소")]
    public TextMeshProUGUI goldText;
    public TextMeshProUGUI roundText;
    public TextMeshProUGUI wallCountText;
    public TextMeshProUGUI opponentNameText;

    [Header("Shop Controls")]
    public ShopUIController shopUIController;
    public Button shopToggleButton;
    public TextMeshProUGUI shopToggleButtonText;
    public GameObject wallPlacementButton;

    private PlayerManager localPlayer;
    private GameManagers gameManager;

    void OnEnable()
    {
        GameEvents.OnGameManagersReady += OnGameManagersReady;
        GameEvents.OnGameStateChanged += HandleGameStateChange;
        GameEvents.OnBattleSequenceStarted += HandleBattleSequenceStarted;

        if (shopToggleButton != null)
        {
            shopToggleButton.onClick.AddListener(OnShopToggleButtonClicked);
        }

        SubscribeToShopVisibility();
    }

    void OnDisable()
    {
        GameEvents.OnGameManagersReady -= OnGameManagersReady;
        GameEvents.OnGameStateChanged -= HandleGameStateChange;
        GameEvents.OnBattleSequenceStarted -= HandleBattleSequenceStarted;

        if (shopToggleButton != null)
        {
            shopToggleButton.onClick.RemoveListener(OnShopToggleButtonClicked);
        }

        if (shopUIController != null)
        {
            shopUIController.OnContentVisibilityChanged -= HandleShopVisibilityChanged;
        }
    }

    void Start()
    {
        if (gameManager == null)
        {
            gameManager = GameManagers.Instance;
        }
        if (gameManager != null && localPlayer == null)
        {
            localPlayer = gameManager.localPlayer;
        }

        SubscribeToShopVisibility();
        RefreshShopToggleText();

        if (gameManager != null)
        {
            HandleGameStateChange(gameManager.GetGameState());
        }
    }

    private void OnGameManagersReady()
    {
        if (gameManager == null)
        {
            gameManager = GameManagers.Instance;
        }
        if (gameManager != null && localPlayer == null)
        {
            localPlayer = gameManager.localPlayer;
        }

        SubscribeToShopVisibility();
        RefreshShopToggleText();

        if (gameManager != null)
        {
            HandleGameStateChange(gameManager.GetGameState());
        }

        Debug.Log("[PlayerHUDController] GameManagers 참조 획득 완료");
    }

    void Update()
    {
        // GameManager가 없으면 매 프레임 시도
        if (gameManager == null)
        {
            gameManager = GameManagers.Instance;
            if (gameManager == null)
            {
                return;
            }
        }
        
        // GameOver 상태이면 네트워크 프로퍼티 접근 안함 (씬 전환 대기 중)
        if (gameManager.GetGameState() == GameManagers.GameState.GameOver)
        {
            return;
        }

        // localPlayer가 없으면 매 프레임 시도
        if (localPlayer == null)
        {
            localPlayer = gameManager.localPlayer;
            if (localPlayer == null)
            {
                return;
            }
        }

        // HUD UI 업데이트
        if (goldText != null)
        {
            goldText.text = localPlayer.GetGold().ToString();
        }

        if (roundText != null)
        {
            roundText.text = $"ROUND\n{Mathf.Max(1, gameManager.currentRound)}";
        }

        if (wallCountText != null)
        {
            wallCountText.text = localPlayer.GetWallCount().ToString();
        }
    }

    private void HandleGameStateChange(GameManagers.GameState newState)
    {
        bool isPreparePhase = (newState == GameManagers.GameState.Prepare);

        if (wallPlacementButton != null)
        {
            wallPlacementButton.SetActive(isPreparePhase);
        }

        if (shopToggleButton != null)
        {
            shopToggleButton.gameObject.SetActive(isPreparePhase);
        }

        if (!isPreparePhase && shopUIController != null)
        {
            shopUIController.SetContentVisibility(false);
        }

        // 전투 중이 아니면 상대 이름 숨김
        if (isPreparePhase && opponentNameText != null)
        {
            opponentNameText.gameObject.SetActive(false);
        }

        RefreshShopToggleText();
    }

    private void HandleBattleSequenceStarted(bool isAttacking)
    {
        if (opponentNameText == null) return;
        if (localPlayer == null) return;

        var opponent = localPlayer.opponentManager;
        if (opponent != null)
        {
            string roleText = isAttacking ? "공격" : "수비";
            opponentNameText.text = $"{roleText} VS Player {opponent.playerId}";
            opponentNameText.gameObject.SetActive(true);
        }
        else
        {
            opponentNameText.text = isAttacking ? "관전 모드" : "AI 웨이브";
            opponentNameText.gameObject.SetActive(true);
        }
    }

    private void OnShopToggleButtonClicked()
    {
        if (shopUIController == null)
        {
            SubscribeToShopVisibility();
            if (shopUIController == null) return;
        }

        shopUIController.ToggleContent();
        RefreshShopToggleText();
    }

    private void HandleShopVisibilityChanged(bool isVisible)
    {
        RefreshShopToggleText();
    }

    private void RefreshShopToggleText()
    {
        if (shopToggleButtonText == null) return;

        bool isVisible = shopUIController != null && shopUIController.IsContentVisible();
        shopToggleButtonText.text = isVisible ? "Close" : "Open";
    }

    private void SubscribeToShopVisibility()
    {
        if (shopUIController == null)
        {
            shopUIController = FindObjectOfType<ShopUIController>(true);
        }

        if (shopUIController != null)
        {
            shopUIController.OnContentVisibilityChanged -= HandleShopVisibilityChanged;
            shopUIController.OnContentVisibilityChanged += HandleShopVisibilityChanged;
        }
    }
}
