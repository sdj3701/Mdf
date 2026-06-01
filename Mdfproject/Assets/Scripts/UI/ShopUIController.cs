// Assets/Scripts/UI/ShopUIController.cs
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ShopUIController : MonoBehaviour
{
    private enum UiLifecycleState
    {
        Hidden,
        Loading,
        DataBinding,
        Visible,
        Closing
    }

    [Header("Shop UI")]
    public ShopSlot[] shopSlots;
    public Button rerollButton;
    public TextMeshProUGUI rerollCostText;

    [Header("Containers")]
    public GameObject slotsContainer;
    public GameObject rerollButtonObject;

    private ShopManager localPlayerShopManager;
    public event Action<bool> OnContentVisibilityChanged;
    private CanvasGroup _rootCanvasGroup;
    private UiLifecycleState _uiState = UiLifecycleState.Hidden;
    private string _lastDisplaySignature = string.Empty;
    private float _lastDisplayRealtime = -10f;

    private void Awake()
    {
        EnsureRootCanvasGroup();
        SetPanelRootVisibility(false);
    }

    private string SafeOwnerId()
    {
        if (!TryGetLocalShopPlayerId(out var playerId))
        {
            return "?";
        }

        return playerId.ToString();
    }

    private bool TryGetLocalShopPlayerId(out int playerId)
    {
        playerId = -1;

        if (localPlayerShopManager == null || localPlayerShopManager.playerManager == null)
        {
            return false;
        }

        try
        {
            playerId = localPlayerShopManager.playerManager.playerId;
            return playerId >= 0;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (MissingReferenceException)
        {
            return false;
        }
    }

    private void OnEnable()
    {
        _uiState = UiLifecycleState.Loading;
        SetContentVisibility(false);

        if (GameManagers.Instance != null && GameManagers.Instance.localPlayer != null)
        {
            localPlayerShopManager = GameManagers.Instance.localPlayer.shopManager;
            SetupUI();
            BuildDebugGUI.LogClient($"[ShopUI] OnEnable local={SafeOwnerId()}");

            HandleGameStateChange(GameManagers.Instance.GetGameState());
        }
        else
        {
            Debug.LogWarning("[ShopUIController] Local player is not ready on OnEnable.");
            BuildDebugGUI.LogClient("[ShopUI] OnEnable without local player.");
        }

        GameEvents.OnGameStateChanged += HandleGameStateChange;
        GameEvents.OnShopRefreshed += HandleShopRefreshed;
        GameEvents.OnUnitPurchaseSucceeded += HandleUnitPurchaseSucceeded;
    }

    private void OnDisable()
    {
        GameEvents.OnGameStateChanged -= HandleGameStateChange;
        GameEvents.OnShopRefreshed -= HandleShopRefreshed;
        GameEvents.OnUnitPurchaseSucceeded -= HandleUnitPurchaseSucceeded;
        _uiState = UiLifecycleState.Hidden;
        SetLegacyContentVisibilityOnly(false);
    }

    private void HandleGameStateChange(GameManagers.GameState newState)
    {
        if (localPlayerShopManager == null && GameManagers.Instance != null && GameManagers.Instance.localPlayer != null)
        {
            localPlayerShopManager = GameManagers.Instance.localPlayer.shopManager;
            SetupUI();
            Debug.Log("[ShopUIController] Local player resolved and UI initialized.");
        }

        if (localPlayerShopManager == null)
        {
            return;
        }

        bool isPreparePhase = newState == GameManagers.GameState.Prepare;
        if (rerollButton != null)
        {
            rerollButton.interactable = isPreparePhase;
        }

        if (shopSlots != null)
        {
            foreach (var slot in shopSlots)
            {
                if (slot == null)
                {
                    continue;
                }

                if (!slot.IsPurchased() && slot.buyButton != null)
                {
                    slot.buyButton.interactable = isPreparePhase;
                }
            }
        }

        if (!isPreparePhase)
        {
            SetContentVisibility(false);
        }
    }

    private void SetupUI()
    {
        if (localPlayerShopManager == null)
        {
            return;
        }

        if (rerollButton != null)
        {
            rerollButton.onClick.RemoveAllListeners();
            rerollButton.onClick.AddListener(OnRerollButtonClick);
        }

        if (shopSlots != null)
        {
            for (int i = 0; i < shopSlots.Length; i++)
            {
                shopSlots[i]?.Initialize(localPlayerShopManager, i);
            }
        }

        UpdateInfoText();
    }

    private void OnRerollButtonClick()
    {
        if (!TryGetLocalShopPlayerId(out var playerId) || GameManagers.Instance == null || GameManagers.Instance.CommandProcessor == null)
        {
            return;
        }

        var command = new RerollShopCommand(playerId);
        GameManagers.Instance.CommandProcessor.RequestCommandExecution(command);
    }

    private void HandleShopRefreshed(PlayerManager refreshedPlayer)
    {
        if (localPlayerShopManager != null && refreshedPlayer == localPlayerShopManager.playerManager)
        {
            BuildDebugGUI.LogClient($"[ShopUI] OnShopRefreshed local={SafeOwnerId()}");
            UpdateShopSlots();
            UpdateInfoText();
        }
    }

    private void HandleUnitPurchaseSucceeded(int playerID, ShopItem purchasedItem, int slotIndex)
    {
        if (!TryGetLocalShopPlayerId(out var localPlayerId) || localPlayerId != playerID)
        {
            return;
        }

        if (shopSlots == null || slotIndex < 0 || slotIndex >= shopSlots.Length)
        {
            return;
        }

        var slot = shopSlots[slotIndex];
        if (slot == null)
        {
            return;
        }

        slot.SetPurchased();
    }

    public void UpdateShopSlots()
    {
        if (localPlayerShopManager == null)
        {
            return;
        }

        List<ShopItem> currentItems = localPlayerShopManager.GetCurrentShopItems();
        DisplayShopItems(currentItems);
    }

    public void DisplayShopItems(List<ShopItem> items)
    {
        if (items == null)
        {
            Debug.LogError("[ShopUIController] DisplayShopItems: items is null.");
            BuildDebugGUI.LogClient("[ShopUI] DisplayShopItems: items is null");
            _uiState = UiLifecycleState.Hidden;
            SetContentVisibility(false);
            return;
        }

        _uiState = UiLifecycleState.DataBinding;
        BuildDebugGUI.LogClient($"[ShopUI] DisplayShopItems count={items.Count}");

        if (shopSlots != null)
        {
            for (int i = 0; i < shopSlots.Length; i++)
            {
                var slot = shopSlots[i];
                if (slot == null)
                {
                    continue;
                }

                if (i < items.Count)
                {
                    slot.DisplayUnit(items[i]);
                    if (localPlayerShopManager != null && localPlayerShopManager.IsSlotSold(i))
                    {
                        slot.SetPurchased();
                    }
                }
                else
                {
                    slot.DisplayUnit(new ShopItem());
                }
            }
        }

        string signature = BuildShopSignature(items);
        float now = Time.unscaledTime;
        if (signature == _lastDisplaySignature && now - _lastDisplayRealtime < 1f)
        {
            BuildDebugGUI.LogClient($"[ShopUI] Duplicate display signature ignored: {signature}");
        }
        else
        {
            _lastDisplaySignature = signature;
            _lastDisplayRealtime = now;
        }
    }

    public void UpdateInfoText()
    {
        if (localPlayerShopManager != null && rerollCostText != null)
        {
            rerollCostText.text = $"{localPlayerShopManager.GetRerollCost()} G";
        }
    }

    public bool IsContentVisible()
    {
        if (GamePrepareUIToolkitController.TryGetShopVisible(out var toolkitVisible))
        {
            return toolkitVisible;
        }

        return slotsContainer != null && slotsContainer.activeSelf;
    }

    public bool ToggleContent()
    {
        if (GamePrepareUIToolkitController.TryToggleShopFromLegacy(out var toolkitVisible))
        {
            ApplyContentVisibility(false, false);
            OnContentVisibilityChanged?.Invoke(toolkitVisible);
            return toolkitVisible;
        }

        bool newVisibility = !IsContentVisible();
        SetContentVisibility(newVisibility);
        return newVisibility;
    }

    public void SetContentVisibility(bool isVisible)
    {
        if (GamePrepareUIToolkitController.TrySetShopVisibilityFromLegacy(isVisible, out var toolkitVisible))
        {
            ApplyContentVisibility(false, false);
            OnContentVisibilityChanged?.Invoke(toolkitVisible);
            return;
        }

        ApplyContentVisibility(isVisible, true);
    }

    public void SetLegacyContentVisibilityOnly(bool isVisible)
    {
        ApplyContentVisibility(isVisible, false);
    }

    private void ApplyContentVisibility(bool isVisible, bool notify)
    {
        if (slotsContainer != null)
        {
            slotsContainer.SetActive(isVisible);
        }

        if (rerollButtonObject != null)
        {
            rerollButtonObject.SetActive(isVisible);
        }

        SetPanelRootVisibility(isVisible);
        _uiState = isVisible ? UiLifecycleState.Visible : UiLifecycleState.Hidden;
        if (notify)
        {
            OnContentVisibilityChanged?.Invoke(isVisible);
        }
    }

    public void InitializeAndHide()
    {
        _uiState = UiLifecycleState.Hidden;
        SetLegacyContentVisibilityOnly(false);
    }

    public void ShowWithItems(List<ShopItem> items)
    {
        var resolvedItems = items ?? new List<ShopItem>();
        string signature = BuildShopSignature(resolvedItems);
        float now = Time.unscaledTime;
        if (IsContentVisible() && signature == _lastDisplaySignature && now - _lastDisplayRealtime < 1f)
        {
            BuildDebugGUI.LogClient($"[ShopUI] ShowWithItems skipped duplicate signature: {signature}");
            return;
        }

        if (GamePrepareUIToolkitController.TryShowShopFromLegacy(resolvedItems, out var toolkitVisible))
        {
            ApplyContentVisibility(false, false);
            OnContentVisibilityChanged?.Invoke(toolkitVisible);
            return;
        }

        _uiState = UiLifecycleState.DataBinding;
        SetContentVisibility(false);
        DisplayShopItems(resolvedItems);
        SetContentVisibility(true);
    }

    private void EnsureRootCanvasGroup()
    {
        if (_rootCanvasGroup == null)
        {
            _rootCanvasGroup = GetComponent<CanvasGroup>();
            if (_rootCanvasGroup == null)
            {
                _rootCanvasGroup = gameObject.AddComponent<CanvasGroup>();
            }
        }
    }

    private void SetPanelRootVisibility(bool isVisible)
    {
        EnsureRootCanvasGroup();
        _rootCanvasGroup.alpha = isVisible ? 1f : 0f;
        _rootCanvasGroup.interactable = isVisible;
        _rootCanvasGroup.blocksRaycasts = isVisible;
    }

    public bool IsRootRaycastBlocking()
    {
        EnsureRootCanvasGroup();
        return _rootCanvasGroup.blocksRaycasts && _rootCanvasGroup.alpha > 0f;
    }

    private static string BuildShopSignature(List<ShopItem> items)
    {
        int round = -1;
        if (GameManagers.Instance != null)
        {
            try
            {
                round = GameManagers.Instance.currentRound;
            }
            catch (InvalidOperationException)
            {
                round = -1;
            }
        }
        if (items == null || items.Count == 0)
        {
            return $"r={round}|empty";
        }

        var tokens = new List<string>(items.Count);
        foreach (var item in items)
        {
            string unit = item.UnitData != null ? item.UnitData.name : "null";
            int star = item.StarLevel;
            tokens.Add($"{unit}:{star}");
        }

        return $"r={round}|{string.Join(",", tokens)}";
    }
}
