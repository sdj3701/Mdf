// Assets/Scripts/UI/ShopUIController.cs
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
    [Tooltip("준비 단계에서만 활성화되는 벽 생성 버튼입니다.")]
    public GameObject wallPlacementButton;

    [Header("상점 토글 버튼 설정")]
    public Button toggleButton;
    public TextMeshProUGUI toggleButtonText;

    [Header("내부 콘텐츠 토글 설정")]
    public GameObject slotsContainer;
    public GameObject rerollButtonObject;

    private ShopManager localPlayerShopManager;

    void Awake()
    {
        if (toggleButton != null)
        {
            toggleButton.onClick.AddListener(ToggleContent);
        }
    }

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
            Debug.LogError("로컬 플레이어를 찾을 수 없어 상점 UI를 초기화할 수 없습니다!");
            gameObject.SetActive(false);
        }

        // 게임 상태 변경 이벤트를 구독합니다.
        GameEvents.OnGameStateChanged += HandleGameStateChange;
        GameEvents.OnShopRefreshed += HandleShopRefreshed;
    }

    void OnDisable()
    {
        // 패널이 비활성화될 때 이벤트 구독을 해지하여 메모리 누수를 방지합니다.
        GameEvents.OnGameStateChanged -= HandleGameStateChange;
        GameEvents.OnShopRefreshed -= HandleShopRefreshed;
    }

    /// <summary>
    /// 게임 상태 변경 이벤트가 발생했을 때 호출되는 핸들러입니다.
    /// </summary>
    private void HandleGameStateChange(GameManagers.GameState newState)
    {
        bool isPreparePhase = (newState == GameManagers.GameState.Prepare);
        
        // [수정] 게임 상태에 따라 벽 생성 버튼과 상점 토글 버튼의 가시성을 제어합니다.
        if (wallPlacementButton != null)
        {
            wallPlacementButton.SetActive(isPreparePhase);
        }
        if (toggleButton != null)
        {
            toggleButton.gameObject.SetActive(isPreparePhase);
        }
        
        // 버튼들의 상호작용 여부를 게임 상태에 따라 결정합니다.
        rerollButton.interactable = isPreparePhase;

        foreach (var slot in shopSlots)
        {
            if (!slot.IsPurchased())
            {
                slot.buyButton.interactable = isPreparePhase;
            }
        }

        // 전투 페이즈가 되면 상점 내용을 자동으로 숨깁니다.
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

        foreach (var slot in shopSlots)
        {
            slot.Initialize(localPlayerShopManager);
        }

        UpdateInfoText();
    }

    private void OnRerollButtonClick()
    {
        if (localPlayerShopManager != null)
        {
            GameEvents.TriggerShopRerollRequested(localPlayerShopManager.playerManager);
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
            Debug.LogError("표시할 아이템 리스트가 null입니다!");
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

    public void ToggleContent()
    {
        bool newVisibility = !slotsContainer.activeSelf;
        SetContentVisibility(newVisibility);
    }

    public void SetContentVisibility(bool isVisible)
    {
        if (slotsContainer != null) slotsContainer.SetActive(isVisible);
        if (rerollButtonObject != null) rerollButtonObject.SetActive(isVisible);
        UpdateButtonText();
    }
    
    private void UpdateButtonText()
    {
        if (toggleButtonText == null) return;

        if (slotsContainer != null && slotsContainer.activeSelf)
        {
            toggleButtonText.text = "Close";
        }
        else
        {
            toggleButtonText.text = "Open";
        }
    }
}
