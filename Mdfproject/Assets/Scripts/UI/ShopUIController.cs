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
    public Button toggleButton; // 'Close'와 'Open' 역할을 할 단일 버튼
    public TextMeshProUGUI toggleButtonText; // 토글 버튼의 텍스트

    [Header("내부 콘텐츠 토글 설정")]
    public GameObject slotsContainer;
    public GameObject rerollButtonObject;

    private ShopManager localPlayerShopManager;

    void Awake()
    {
        // 토글 버튼에 리스너를 연결합니다.
        if (toggleButton != null)
        {
            toggleButton.onClick.AddListener(ToggleContent);
        }
    }

    void OnEnable()
    {
        // UI가 활성화될 때마다 항상 로컬 플레이어 매니저를 찾고 UI를 설정합니다.
        if (GameManagers.Instance != null && GameManagers.Instance.localPlayer != null)
        {
            localPlayerShopManager = GameManagers.Instance.localPlayer.shopManager;
            SetupUI();
            
            // 상점이 켜질 때 기본적으로 내용을 표시하도록 설정합니다.
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
        
        // 준비 단계일 때만 리롤 버튼과 토글 버튼이 활성화됩니다.
        rerollButton.interactable = isPreparePhase;
        if (toggleButton != null) toggleButton.interactable = isPreparePhase;

        // 구매 버튼 활성화 로직은 그대로 유지
        foreach (var slot in shopSlots)
        {
            if (!slot.IsPurchased())
            {
                slot.buyButton.interactable = isPreparePhase;
            }
        }
    }

    /// <summary>
    /// 토글 버튼을 누를 때 호출되어 상점 내용의 표시 여부를 전환합니다.
    /// </summary>
    public void ToggleContent()
    {
        // 현재 슬롯 컨테이너의 활성화 상태를 뒤집습니다.
        bool newVisibility = !slotsContainer.activeSelf;
        SetContentVisibility(newVisibility);
    }

    /// <summary>
    /// 상점 내용(슬롯, 리롤 버튼)의 표시 여부를 설정하고 버튼 텍스트를 업데이트합니다.
    /// </summary>
    public void SetContentVisibility(bool isVisible)
    {
        if (slotsContainer != null) slotsContainer.SetActive(isVisible);
        if (rerollButtonObject != null) rerollButtonObject.SetActive(isVisible);

        // 내용의 표시 여부에 따라 버튼 텍스트를 업데이트합니다.
        UpdateButtonText();
    }
    
    /// <summary>
    /// 현재 상점 상태에 맞춰 토글 버튼의 텍스트를 "Open" 또는 "Close"로 변경합니다.
    /// </summary>
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

    public void UpdateShopSlots()
    {
        if (localPlayerShopManager == null) return;
        List<UnitData> currentItems = localPlayerShopManager.GetCurrentShopItems();
        DisplayShopItems(currentItems);
    }

    public void DisplayShopItems(List<UnitData> items)
    {
        if (items == null)
        {
            Debug.LogError("표시할 아이템 리스트가 null입니다!");
            return;
        }

        Debug.Log($"[ShopUIController] DisplayShopItems 호출됨. 아이템 {items.Count}개로 화면 갱신 시도.");
        for (int i = 0; i < shopSlots.Length; i++)
        {
            if (i < items.Count)
            {
                shopSlots[i].DisplayUnit(items[i]);
            }
            else
            {
                shopSlots[i].DisplayUnit(null);
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