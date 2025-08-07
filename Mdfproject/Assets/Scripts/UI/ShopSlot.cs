using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ShopSlot : MonoBehaviour
{
    // --- UI 요소 연결 ---
    public Image unitIcon;
    public TextMeshProUGUI unitNameText;
    public TextMeshProUGUI unitCostText;
    public Button buyButton;

    // --- 데이터 저장 ---
    private UnitData currentUnitData;
    private ShopManager shopManager; // 상점 매니저 참조

    /// <summary>
    /// 슬롯 초기화 및 버튼 클릭 이벤트 연결
    /// </summary>
    public void Initialize(ShopManager manager)
    {
        this.shopManager = manager;
        buyButton.onClick.AddListener(OnBuyButtonClick);
    }

    /// <summary>
    /// 유닛 데이터를 받아와서 UI에 표시
    /// </summary>
    public void DisplayUnit(UnitData unitData)
    {
        this.currentUnitData = unitData;

        // 데이터가 null이 아니면 (판매할 유닛이 있으면)
        if (unitData != null)
        {
            unitIcon.sprite = unitData.unitIcon;
            unitNameText.text = unitData.unitName;
            unitCostText.text = unitData.cost.ToString();
            gameObject.SetActive(true); // 슬롯 활성화
        }
        // 데이터가 null이면 (판매할 유닛이 없으면)
        else
        {
            gameObject.SetActive(false); // 슬롯 비활성화
        }
    }

    /// <summary>
    /// 구매 버튼 클릭 시 호출될 함수
    /// </summary>
    private void OnBuyButtonClick()
    {
        // 실제 구매 로직은 ShopManager에게 위임
        if (currentUnitData != null)
        {
            shopManager.TryBuyUnit(currentUnitData);
        }
    }
}