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
        if (GameManagers.Instance != null && GameManagers.Instance.localPlayer != null)
        {
            localPlayerShopManager = GameManagers.Instance.localPlayer.shopManager;
            SetupUI();
            SetContentVisibility(true);
        }
        else
        {
            Debug.LogError("로컬 플레이어를 찾을 수 없어 상점 UI를 초기화할 수 없습니다!");
            gameObject.SetActive(false);
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

    void Update()
    {
        if (GameManagers.Instance == null || localPlayerShopManager == null) return;

        bool isPreparePhase = (GameManagers.Instance.GetGameState() == GameManagers.GameState.Prepare);
        
        rerollButton.interactable = isPreparePhase;
        if (toggleButton != null) toggleButton.interactable = isPreparePhase;

        foreach (var slot in shopSlots)
        {
            if (!slot.IsPurchased())
            {
                slot.buyButton.interactable = isPreparePhase;
            }
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

    /// <summary>
    /// 상점 슬롯의 내용을 최신 정보로 업데이트합니다.
    /// </summary>
    public void UpdateShopSlots()
    {
        if (localPlayerShopManager == null) return;

        // --- [수정된 부분] ---
        // ShopManager로부터 List<ShopItem>을 받도록 변수 타입을 수정합니다.
        List<ShopItem> currentItems = localPlayerShopManager.GetCurrentShopItems();
        DisplayShopItems(currentItems);
    }

    /// <summary>
    /// ShopItem 리스트를 받아 각 슬롯에 표시합니다.
    /// </summary>
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
                // 빈 슬롯은 비어있는 ShopItem으로 초기화하여 비활성화합니다.
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
    
    private void OnRerollButtonClick()
    {
        if (localPlayerShopManager != null)
        {
            localPlayerShopManager.Reroll();
            UpdateShopSlots();
            UpdateInfoText();
        }
    }
}