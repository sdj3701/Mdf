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
    public Button closeButton;
    public TextMeshProUGUI rerollCostText;
    // ✅ [제거] public TextMeshProUGUI goldText;

    [Header("내부 콘텐츠 토글 설정")]
    public GameObject slotsContainer;
    public GameObject rerollButtonObject; 
    public Button openToggleButton;

    private ShopManager localPlayerShopManager;
    private bool isContentLoaded = false; 

    void Awake()
    {
        if (openToggleButton != null)
        {
            openToggleButton.onClick.AddListener(ToggleContent);
        }
        if (closeButton != null)
        {
            closeButton.onClick.AddListener(() => SetContentVisibility(false));
        }
    }

    void OnEnable()
    {
        if (!isContentLoaded)
        {
            if (GameManagers.Instance != null && GameManagers.Instance.localPlayer != null)
            {
                // ✅ [수정] PlayerManager 참조를 더 이상 저장하지 않습니다.
                localPlayerShopManager = GameManagers.Instance.localPlayer.shopManager;
                SetupUI();
                isContentLoaded = true;
            }
            else
            {
                Debug.LogError("로컬 플레이어를 찾을 수 없어 상점 UI를 초기화할 수 없습니다!");
                gameObject.SetActive(false);
            }
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

        UpdateShopSlots();
        UpdateInfoText();
    }
    
    void Update()
    {
        if (!isContentLoaded) return;

        bool isPreparePhase = (GameManagers.Instance.GetGameState() == GameManagers.GameState.Prepare);
        rerollButton.interactable = isPreparePhase;
        
        if(openToggleButton != null) openToggleButton.interactable = true;

        foreach (var slot in shopSlots)
        {
            if (!slot.IsPurchased())
            {
                slot.buyButton.interactable = isPreparePhase;
            }
        }

        // ✅ [제거] 골드 텍스트를 업데이트하는 로직을 완전히 제거합니다.
        /*
        if (localPlayerManager != null)
        {
            goldText.text = $"<color=yellow>{localPlayerManager.GetGold()}</color> G";
        }
        */
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
    }

    public void UpdateShopSlots()
    {
        if (localPlayerShopManager == null) return;
        List<UnitData> currentItems = localPlayerShopManager.GetCurrentShopItems();
        for (int i = 0; i < shopSlots.Length; i++)
        {
            if (i < currentItems.Count) shopSlots[i].DisplayUnit(currentItems[i]);
            else shopSlots[i].DisplayUnit(null);
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