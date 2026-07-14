// Assets/Scripts/UI/PlayerHUDController.cs
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class PlayerHUDController : MonoBehaviour
{
    private const float RuntimeReferenceFallbackInterval = 0.5f;
    private const float HudFallbackRefreshInterval = 0.25f;

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
    private int _lastGold = int.MinValue;
    private int _lastRound = int.MinValue;
    private int _lastWallCount = int.MinValue;
    private bool hudDirty = true;
    private float nextRuntimeReferenceFallbackTime;
    private float nextHudFallbackRefreshTime;

    private bool RefreshRuntimeReferences(bool verboseLog = false)
    {
        bool changed = false;
        var latestGameManager = GameManagers.Instance;
        if (latestGameManager != gameManager)
        {
            gameManager = latestGameManager;
            localPlayer = null;
            InvalidateCachedHudValues();
            changed = true;

            if (verboseLog && gameManager != null)
            {
                Debug.Log("[PlayerHUDController] GameManagers 참조 재바인딩 완료");
            }
        }

        if (gameManager != null && gameManager.localPlayer != localPlayer)
        {
            localPlayer = gameManager.localPlayer;
            InvalidateCachedHudValues();
            changed = true;
            if (verboseLog && localPlayer != null)
            {
                Debug.Log($"[PlayerHUDController] localPlayer 재바인딩 완료: Player {localPlayer.playerId}");
            }
        }

        if (changed)
        {
            MarkHudDirty();
        }

        return changed;
    }

    void OnEnable()
    {
        GameEvents.OnGameManagersReady += OnGameManagersReady;
        GameEvents.OnGameStateChanged += HandleGameStateChange;
        GameEvents.OnBattleSequenceStarted += HandleBattleSequenceStarted;
        GameEvents.OnGameStateRestored += HandleGameStateChange;
        GameEvents.OnHostMigrationCompleted += HandleHostMigrationCompleted;
        GameEvents.OnPlayerStatsChanged += HandlePlayerStatsChanged;
        GameEvents.OnPlayerWallCountChanged += HandlePlayerWallCountChanged;

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
        GameEvents.OnGameStateRestored -= HandleGameStateChange;
        GameEvents.OnHostMigrationCompleted -= HandleHostMigrationCompleted;
        GameEvents.OnPlayerStatsChanged -= HandlePlayerStatsChanged;
        GameEvents.OnPlayerWallCountChanged -= HandlePlayerWallCountChanged;

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
        if (!HasVisibleLegacyValueTarget())
        {
            return;
        }

        // Host Migration 후 Instance 교체를 반영하기 위해 매 프레임 최신 참조를 확인합니다.
        if (ShouldRefreshRuntimeReferences())
        {
            RefreshRuntimeReferences();
        }

        RefreshHud(false);
    }

    private bool ShouldRefreshRuntimeReferences()
    {
        if (Time.unscaledTime < nextRuntimeReferenceFallbackTime)
        {
            return false;
        }

        nextRuntimeReferenceFallbackTime = Time.unscaledTime + RuntimeReferenceFallbackInterval;
        if (gameManager != GameManagers.Instance)
        {
            return true;
        }

        if (gameManager != null && gameManager.localPlayer != localPlayer)
        {
            return true;
        }

        return gameManager == null || localPlayer == null;
    }

    private void RefreshHud(bool force)
    {
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

        if (!force && !hudDirty && Time.unscaledTime < nextHudFallbackRefreshTime)
        {
            return;
        }

        nextHudFallbackRefreshTime = Time.unscaledTime + HudFallbackRefreshInterval;
        hudDirty = false;

        // HUD UI 업데이트
        if (goldText != null && goldText.gameObject.activeInHierarchy)
        {
            int gold = localPlayer.GetGold();
            if (gold != _lastGold)
            {
                _lastGold = gold;
                goldText.text = gold.ToString();
            }
        }

        if (roundText != null && roundText.gameObject.activeInHierarchy)
        {
            int round = Mathf.Max(1, gameManager.currentRound);
            if (round != _lastRound)
            {
                _lastRound = round;
                roundText.text = $"ROUND\n{round}";
            }
        }

        if (wallCountText != null && wallCountText.gameObject.activeInHierarchy)
        {
            int wallCount = localPlayer.GetWallCount();
            if (wallCount != _lastWallCount)
            {
                _lastWallCount = wallCount;
                wallCountText.text = wallCount.ToString();
            }
        }
    }

    private void HandleGameStateChange(GameManagers.GameState newState)
    {
        MarkHudDirty();
        bool isPreparePhase = (newState == GameManagers.GameState.Prepare);
        bool useToolkitHud = GamePrepareUIToolkitController.IsToolkitActive;

        if (wallPlacementButton != null)
        {
            wallPlacementButton.SetActive(isPreparePhase && !useToolkitHud);
        }

        if (shopToggleButton != null)
        {
            shopToggleButton.gameObject.SetActive(isPreparePhase && !useToolkitHud);
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
        RefreshHud(true);
    }

    private void HandleHostMigrationCompleted(bool isNewHost)
    {
        RefreshRuntimeReferences(true);
        MarkHudDirty();
        RefreshHud(true);
    }

    private void HandlePlayerStatsChanged(int playerId, int newHealth, int newGold)
    {
        if (!IsLocalPlayerId(playerId))
        {
            return;
        }

        MarkHudDirty();
        RefreshHud(true);
    }

    private void HandlePlayerWallCountChanged(int playerId, int newWallCount)
    {
        if (!IsLocalPlayerId(playerId))
        {
            return;
        }

        MarkHudDirty();
        RefreshHud(true);
    }

    public void SetLegacyHudButtonsVisible(bool visible)
    {
        if (wallPlacementButton != null)
        {
            wallPlacementButton.SetActive(visible);
        }

        if (shopToggleButton != null)
        {
            shopToggleButton.gameObject.SetActive(visible);
        }
    }

    public void SetLegacyResourceHudVisible(bool visible)
    {
        if (goldText != null)
        {
            goldText.gameObject.SetActive(visible);
        }

        if (wallCountText != null)
        {
            wallCountText.gameObject.SetActive(visible);
        }

        if (visible)
        {
            InvalidateCachedHudValues();
            MarkHudDirty();
        }
    }

    private bool HasVisibleLegacyValueTarget()
    {
        return (goldText != null && goldText.gameObject.activeInHierarchy) ||
               (roundText != null && roundText.gameObject.activeInHierarchy) ||
               (wallCountText != null && wallCountText.gameObject.activeInHierarchy);
    }

    private void InvalidateCachedHudValues()
    {
        _lastGold = int.MinValue;
        _lastRound = int.MinValue;
        _lastWallCount = int.MinValue;
    }

    private void HandleBattleSequenceStarted(bool isAttacking)
    {
        RefreshRuntimeReferences();
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

    private bool IsLocalPlayerId(int playerId)
    {
        RefreshRuntimeReferences();
        if (localPlayer == null)
        {
            return false;
        }

        try
        {
            return localPlayer.playerId == playerId;
        }
        catch (System.InvalidOperationException)
        {
            return false;
        }
    }

    private void MarkHudDirty()
    {
        hudDirty = true;
        nextHudFallbackRefreshTime = 0f;
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
