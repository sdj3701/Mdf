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

    private void RefreshRuntimeReferences(bool verboseLog = false)
    {
        var latestGameManager = GameManagers.Instance;
        if (latestGameManager != gameManager)
        {
            gameManager = latestGameManager;
            localPlayer = null;

            if (verboseLog && gameManager != null)
            {
                Debug.Log("[PlayerHUDController] GameManagers 참조 재바인딩 완료");
            }
        }

        if (gameManager != null && gameManager.localPlayer != localPlayer)
        {
            localPlayer = gameManager.localPlayer;
            if (verboseLog && localPlayer != null)
            {
                Debug.Log($"[PlayerHUDController] localPlayer 재바인딩 완료: Player {localPlayer.playerId}");
            }
        }
    }

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
        RefreshRuntimeReferences();

        SubscribeToShopVisibility();
        RefreshShopToggleText();

        if (gameManager != null)
        {
            HandleGameStateChange(gameManager.GetGameState());
        }
    }

    private void OnGameManagersReady()
    {
        RefreshRuntimeReferences(true);

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
        // Host Migration 후 Instance 교체를 반영하기 위해 매 프레임 최신 참조를 확인합니다.
        RefreshRuntimeReferences();
        if (gameManager == null)
        {
            return;
        }
        
        // Host Migration 중이거나 Spawned 되지 않은 경우 네트워크 프로퍼티 접근 안함
        if (!gameManager.IsReadyForNetworkAccess)
        {
            return;
        }
        
        // GameOver 상태이면 네트워크 프로퍼티 접근 안함 (씬 전환 대기 중)
        if (gameManager.GetGameState() == GameManagers.GameState.GameOver)
        {
            return;
        }

        if (localPlayer == null)
        {
            return;
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
