// Assets/Scripts/UI/ShopSlot.cs
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ShopSlot : MonoBehaviour
{
    [Header("UI 요소")]
    public Image unitIcon;
    public TextMeshProUGUI unitNameText;
    public TextMeshProUGUI unitCostText;
    public Button buyButton;
    public GameObject purchasedOverlay;
    public TextMeshProUGUI starLevelText;

    private ShopItem currentShopItem;
    private ShopManager shopManager; // PlayerManager 참조를 얻기 위해 유지합니다.
    
    private bool isPurchased = false;

    /// <summary>
    /// ShopUIController에 의해 호출되어 슬롯을 초기화합니다.
    /// </summary>
    public void Initialize(ShopManager manager)
    {
        this.shopManager = manager;
        buyButton.onClick.RemoveAllListeners(); 
        buyButton.onClick.AddListener(OnBuyButtonClick);
    }

    /// <summary>
    /// ShopItem 데이터를 받아 UI에 표시합니다.
    /// </summary>
    public void DisplayUnit(ShopItem shopItem)
    {
        this.currentShopItem = shopItem;
        
        // UnitData가 유효한지 확인하여 슬롯 활성화 여부를 결정합니다.
        if (shopItem.UnitData != null)
        {
            isPurchased = false;

            unitIcon.sprite = shopItem.UnitData.unitIcon;
            unitNameText.text = shopItem.UnitData.unitName;
            unitCostText.text = $"{shopItem.CalculatedCost}";
            starLevelText.text = $"{shopItem.StarLevel}성";
            
            buyButton.interactable = true;
            if(purchasedOverlay) purchasedOverlay.SetActive(false);
            gameObject.SetActive(true);
        }
        else
        {
            // 유효하지 않은 아이템이면 슬롯을 비활성화합니다.
            gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// 구매 버튼을 클릭했을 때 호출됩니다.
    /// </summary>
    private void OnBuyButtonClick()
    {
        if (currentShopItem.UnitData != null && shopManager != null && !isPurchased)
        {
            // [핵심 변경점]
            // 이제 직접 구매를 처리하지 않고, "유닛 구매가 요청되었다"는 이벤트를 시스템 전체에 알립니다.
            // PlayerManager가 이 이벤트를 듣고 실제 구매 로직을 처리하게 됩니다.
            GameEvents.TriggerUnitPurchased(shopManager.playerManager, currentShopItem.UnitData, currentShopItem.StarLevel);
            
            // UI는 즉시 '구매됨' 상태로 변경하여 사용자에게 빠른 피드백을 줍니다.
            SetPurchased();
        }
    }

    /// <summary>
    /// 슬롯을 '구매 완료' 상태로 변경합니다.
    /// </summary>
    public void SetPurchased()
    {
        isPurchased = true;
        buyButton.interactable = false;
        if(purchasedOverlay) purchasedOverlay.SetActive(true);
    }
    
    /// <summary>
    /// 이 슬롯이 이미 구매되었는지 여부를 반환합니다.
    /// </summary>
    public bool IsPurchased() => isPurchased;
}