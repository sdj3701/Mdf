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
    public GameObject purchasedOverlay; // 구매 후 표시될 오버레이

    private UnitData currentUnitData;
    private ShopManager shopManager;

    public void Initialize(ShopManager manager)
    {
        this.shopManager = manager;
        buyButton.onClick.AddListener(OnBuyButtonClick);
    }

    public void DisplayUnit(UnitData unitData)
    {
        this.currentUnitData = unitData;
        
        if (unitData != null)
        {
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
        buyButton.interactable = false;
        if(purchasedOverlay) purchasedOverlay.SetActive(true);
    }
}