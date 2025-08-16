// Assets/Scripts/UI/ShopSlot.cs
using UnityEngine;
using UnityEngine.UI;
using TMPro;
// Assets/Scripts/UI/ShopSlot.cs

public class ShopSlot : MonoBehaviour
{
    public Image unitIcon;
    public TextMeshProUGUI unitNameText;
    public TextMeshProUGUI unitCostText;
    public Button buyButton;
    public GameObject purchasedOverlay;

    private UnitData currentUnitData;
    private ShopManager shopManager;
    
    private bool isPurchased = false;

    public void Initialize(ShopManager manager)
    {
        this.shopManager = manager;
        // --- 수정된 부분 ---
        // 리스너를 추가하기 전에 항상 기존의 모든 리스너를 제거합니다.
        // 이렇게 하면 이 함수가 여러 번 호출되어도 안전합니다.
        buyButton.onClick.RemoveAllListeners(); 
        buyButton.onClick.AddListener(OnBuyButtonClick);
        // --- 수정 끝 ---
    }

    // ... (이하 코드는 그대로)
    public void DisplayUnit(UnitData unitData)
    {
        this.currentUnitData = unitData;
        
        if (unitData != null)
        {
            isPurchased = false;

            unitIcon.sprite = unitData.unitIcon;
            unitNameText.text = unitData.unitName;
            unitCostText.text = $"G {unitData.cost}";
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
        if (currentUnitData != null && shopManager != null)
        {
            shopManager.TryBuyUnit(currentUnitData, this);
        }
    }

    public void SetPurchased()
    {
        isPurchased = true;
        buyButton.interactable = false;
        if(purchasedOverlay) purchasedOverlay.SetActive(true);
    }
    
    public bool IsPurchased()
    {
        return isPurchased;
    }
}