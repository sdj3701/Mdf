// Assets/Scripts/UI/ShopSlot.cs
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ShopSlot : MonoBehaviour
{
    public Image unitIcon;
    public TextMeshProUGUI unitNameText;
    public TextMeshProUGUI unitCostText;
    public Button buyButton;
    public GameObject purchasedOverlay;
    public TextMeshProUGUI starLevelText; // [추가됨] 성급을 표시할 텍스트

    private ShopItem currentShopItem; // [변경됨] UnitData -> ShopItem
    private ShopManager shopManager;
    
    private bool isPurchased = false;

    public void Initialize(ShopManager manager)
    {
        this.shopManager = manager;
        buyButton.onClick.RemoveAllListeners(); 
        buyButton.onClick.AddListener(OnBuyButtonClick);
    }

    // [변경됨] 매개변수가 ShopItem으로 변경되고, UI 표시 로직이 수정됩니다.
    public void DisplayUnit(ShopItem shopItem)
    {
        this.currentShopItem = shopItem;
        
        // UnitData가 null이 아닌 유효한 ShopItem인지 확인
        if (shopItem.UnitData != null)
        {
            isPurchased = false;

            unitIcon.sprite = shopItem.UnitData.unitIcon;
            unitNameText.text = shopItem.UnitData.unitName;
            unitCostText.text = $"{shopItem.CalculatedCost}"; // 계산된 가격 표시
            starLevelText.text = $"{shopItem.StarLevel}성"; // 성급 표시
            
            buyButton.interactable = true;
            if(purchasedOverlay) purchasedOverlay.SetActive(false);
            gameObject.SetActive(true);
        }
        else
        {
            gameObject.SetActive(false);
        }
    }

    private void OnBuyButtonClick()
    {
        if (currentShopItem.UnitData != null && shopManager != null)
        {
            // [변경됨] currentShopItem 전체를 전달합니다.
            shopManager.TryBuyUnit(currentShopItem, this);
        }
    }

    public void SetPurchased()
    {
        isPurchased = true;
        buyButton.interactable = false;
        if(purchasedOverlay) purchasedOverlay.SetActive(true);
    }
    
    public bool IsPurchased() => isPurchased;
}