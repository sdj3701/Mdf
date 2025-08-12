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

    // ✅ [추가] 슬롯의 구매 상태를 기억하는 변수
    private bool isPurchased = false;

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
            // ✅ [추가] 새로운 유닛이 표시되면 구매 상태를 초기화합니다.
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
        // ✅ [추가] 구매되었음을 변수에 기록합니다.
        isPurchased = true;
        buyButton.interactable = false;
        if(purchasedOverlay) purchasedOverlay.SetActive(true);
    }

    /// <summary>
    /// ✅ [추가] 이 슬롯이 현재 구매된 상태인지 여부를 반환하는 함수입니다.
    /// </summary>
    public bool IsPurchased()
    {
        return isPurchased;
    }
}