// Assets/Scripts/UI/ShopUIController.cs
using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public class ShopUIController : MonoBehaviour
{
    [Header("UI 요소 연결")]
    public ShopSlot[] shopSlots;
    public Button rerollButton;
    public TextMeshProUGUI rerollCostText;

    [Header("슬롯 컨테이너 관련 설정")]
    public GameObject slotsContainer;
    public GameObject rerollButtonObject;

    private ShopManager localPlayerShopManager;
    public event Action<bool> OnContentVisibilityChanged;

    void OnEnable()
    {
        // 패널이 활성화될 때 로컬 플레이어 정보를 찾아 UI를 설정합니다.
        if (GameManagers.Instance != null && GameManagers.Instance.localPlayer != null)
        {
            localPlayerShopManager = GameManagers.Instance.localPlayer.shopManager;
            SetupUI();

            // 현재 게임 상태에 맞춰 UI를 즉시 갱신합니다.
            HandleGameStateChange(GameManagers.Instance.GetGameState());
        }
        else
        {
            Debug.LogWarning("[ShopUIController] 로컬 플레이어가 아직 지정되지 않았습니다. 이벤트 구독 후 재시도합니다.");
        }

        // 게임 상태 변경, 상점 갱신, 구매 성공 이벤트를 구독합니다.
        GameEvents.OnGameStateChanged += HandleGameStateChange;
        GameEvents.OnShopRefreshed += HandleShopRefreshed;
        GameEvents.OnUnitPurchaseSucceeded += HandleUnitPurchaseSucceeded;
    }

    void OnDisable()
    {
        // 패널 비활성화 시 이벤트 구독을 해제하여 메모리를 보호합니다.
        GameEvents.OnGameStateChanged -= HandleGameStateChange;
        GameEvents.OnShopRefreshed -= HandleShopRefreshed;
        GameEvents.OnUnitPurchaseSucceeded -= HandleUnitPurchaseSucceeded;
    }

    /// <summary>
    /// 게임 상태 변경 이벤트가 발생했을 때 호출되는 핸들러입니다.
    /// </summary>
    private void HandleGameStateChange(GameManagers.GameState newState)
    {
        // [예외처리] 로컬 플레이어가 아직 지정되지 않았다면 초기화를 시도
        if (localPlayerShopManager == null && GameManagers.Instance != null && GameManagers.Instance.localPlayer != null)
        {
            localPlayerShopManager = GameManagers.Instance.localPlayer.shopManager;
            SetupUI();
            Debug.Log("[ShopUIController] 로컬 플레이어가 설정되어 UI를 초기화했습니다.");
        }

        // 여전히 로컬 플레이어가 없으면 처리하지 않음
        if (localPlayerShopManager == null) return;

        bool isPreparePhase = (newState == GameManagers.GameState.Prepare);

        // 버튼들의 활성/비활성 여부를 게임 상태에 따라 결정합니다.
        rerollButton.interactable = isPreparePhase;

        foreach (var slot in shopSlots)
        {
            if (!slot.IsPurchased())
            {
                slot.buyButton.interactable = isPreparePhase;
            }
        }

        // 전투 페이즈로 넘어가면 상점 UI를 자동으로 닫습니다.
        if (!isPreparePhase)
        {
            SetContentVisibility(false);
        }
    }

    private void SetupUI()
    {
        if (localPlayerShopManager == null) return;

        rerollButton.onClick.RemoveAllListeners();
        rerollButton.onClick.AddListener(OnRerollButtonClick);

        for (int i = 0; i < shopSlots.Length; i++)
        {
            shopSlots[i].Initialize(localPlayerShopManager, i); // 슬롯별 인덱스도 전달
        }

        UpdateInfoText();
    }

    private void OnRerollButtonClick()
    {
        if (localPlayerShopManager != null)
        {
            var command = new RerollShopCommand(localPlayerShopManager.playerManager.playerId);
            GameManagers.Instance.CommandProcessor.RequestCommandExecution(command);
        }
    }

    private void HandleShopRefreshed(PlayerManager refreshedPlayer)
    {
        if (localPlayerShopManager != null && refreshedPlayer == localPlayerShopManager.playerManager)
        {
            UpdateShopSlots();
            UpdateInfoText();
        }
    }

    private void HandleUnitPurchaseSucceeded(int playerID, ShopItem purchasedItem, int slotIndex)
    {
        // 이벤트가 로컬 플레이어에 해당하는지 확인
        if (localPlayerShopManager != null && localPlayerShopManager.playerManager.playerId == playerID)
        {
            // 해당 슬롯의 '구매 완료' 상태로 변경
            if (slotIndex >= 0 && slotIndex < shopSlots.Length)
            {
                shopSlots[slotIndex].SetPurchased();
            }
        }
    }

    public void UpdateShopSlots()
    {
        if (localPlayerShopManager == null) return;
        List<ShopItem> currentItems = localPlayerShopManager.GetCurrentShopItems();
        DisplayShopItems(currentItems);
    }

    public void DisplayShopItems(List<ShopItem> items)
    {
        if (items == null)
        {
            Debug.LogError("표시할 리스트가 null입니다.");
            return;
        }

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
        if (slotsContainer != null) slotsContainer.SetActive(isVisible);
        if (rerollButtonObject != null) rerollButtonObject.SetActive(isVisible);
        OnContentVisibilityChanged?.Invoke(isVisible);
    }

    /// <summary>
    /// GameManagers에서 호출. 초기화 후 UI를 숨깁니다.
    /// </summary>
    public void InitializeAndHide()
    {
        SetContentVisibility(false);
    }
}
