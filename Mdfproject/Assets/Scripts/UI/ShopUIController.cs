// Assets/Scripts/UI/ShopUIController.cs
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ShopUIController : MonoBehaviour
{
    [Header("Shop UI")]
    public ShopSlot[] shopSlots;
    public Button rerollButton;
    public TextMeshProUGUI rerollCostText;

    [Header("Containers")]
    public GameObject slotsContainer;
    public GameObject rerollButtonObject;

    private ShopManager localPlayerShopManager;
    public event Action<bool> OnContentVisibilityChanged;

    private string SafeOwnerId()
    {
        if (localPlayerShopManager == null || localPlayerShopManager.playerManager == null)
        {
            return "null";
        }

        try
        {
            return localPlayerShopManager.playerManager.playerId.ToString();
        }
        catch (InvalidOperationException)
        {
            return "?";
        }
    }

    private void OnEnable()
    {
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
        rerollButton.interactable = isPreparePhase;

        foreach (var slot in shopSlots)
        {
            if (!slot.IsPurchased())
            {
                slot.buyButton.interactable = isPreparePhase;
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

        rerollButton.onClick.RemoveAllListeners();
        rerollButton.onClick.AddListener(OnRerollButtonClick);

        for (int i = 0; i < shopSlots.Length; i++)
        {
            shopSlots[i].Initialize(localPlayerShopManager, i);
        }

        UpdateInfoText();
    }

    private void OnRerollButtonClick()
    {
        if (localPlayerShopManager == null)
        {
            return;
        }

        var command = new RerollShopCommand(localPlayerShopManager.playerManager.playerId);
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
        if (localPlayerShopManager == null || localPlayerShopManager.playerManager.playerId != playerID)
        {
            return;
        }

        if (slotIndex >= 0 && slotIndex < shopSlots.Length)
        {
            shopSlots[slotIndex].SetPurchased();
        }
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
            return;
        }

        BuildDebugGUI.LogClient($"[ShopUI] DisplayShopItems count={items.Count}");

        for (int i = 0; i < shopSlots.Length; i++)
        {
            if (i < items.Count)
            {
                shopSlots[i].DisplayUnit(items[i]);
            }
            else
            {
                shopSlots[i].DisplayUnit(new ShopItem());
            }
        }
    }

    public void UpdateInfoText()
    {
        if (localPlayerShopManager != null)
        {
            rerollCostText.text = $"{localPlayerShopManager.GetRerollCost()} G";
        }
    }

    public bool IsContentVisible()
    {
        return slotsContainer != null && slotsContainer.activeSelf;
    }

    public bool ToggleContent()
    {
        bool newVisibility = !IsContentVisible();
        SetContentVisibility(newVisibility);
        return newVisibility;
    }

    public void SetContentVisibility(bool isVisible)
    {
        if (slotsContainer != null)
        {
            slotsContainer.SetActive(isVisible);
        }

        if (rerollButtonObject != null)
        {
            rerollButtonObject.SetActive(isVisible);
        }

        OnContentVisibilityChanged?.Invoke(isVisible);
    }

    public void InitializeAndHide()
    {
        SetContentVisibility(false);
    }
}
