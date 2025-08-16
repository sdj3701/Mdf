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
        // 초기 설정(리스너 추가 등)은 여전히 한 번만 실행합니다.
        if (!isContentLoaded)
        {
            if (GameManagers.Instance != null && GameManagers.Instance.localPlayer != null)
            {
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
        
        UpdateInfoText();
    }
    
    void Update()
    {
        if (!isContentLoaded) return;
        if (GameManagers.Instance == null) return; // GameManager가 없을 때 에러 방지

        bool isPreparePhase = (GameManagers.Instance.GetGameState() == GameManagers.GameState.Prepare);
        rerollButton.interactable = isPreparePhase;
        
        if(openToggleButton != null) openToggleButton.interactable = true;

        foreach (var slot in shopSlots)
        {
            // 슬롯이 구매되지 않았고, 준비 단계일 때만 구매 버튼 활성화
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
    }

    // ✅ [수정된 부분 1] 기존 함수는 내부에서만 사용하도록 변경
    public void UpdateShopSlots()
    {
        if (localPlayerShopManager == null) return;
        List<UnitData> currentItems = localPlayerShopManager.GetCurrentShopItems();
        DisplayShopItems(currentItems); // 아래의 새 함수를 호출
    }

    // ✅ [수정된 부분 2] 데이터를 직접 받아서 화면을 그리는, 더 안정적인 public 함수 추가
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