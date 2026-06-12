using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Cysharp.Threading.Tasks;

public class ShopSlot : MonoBehaviour
{
    [Header("UI 요소")]
    public Image unitIcon;
    public TextMeshProUGUI unitNameText;
    public TextMeshProUGUI unitCostText;
    public Button buyButton;
    public GameObject purchasedOverlay;
    public TextMeshProUGUI starLevelText;

    [Header("코스트별 테두리 이미지")]
    public Sprite[] costBorders = new Sprite[5]; // 1~5 코스트

    private ShopItem currentShopItem;
    private ShopManager shopManager;
    private bool isPurchased = false;
    private int slotIndex;

    private int iconBindVersion;

    /// <summary>
    /// ShopUIController에 의해 호출되어 슬롯을 초기화합니다.
    /// </summary>
    public void Initialize(ShopManager manager, int index)
    {
        this.shopManager = manager;
        this.slotIndex = index; // 인덱스 저장
        buyButton.onClick.RemoveAllListeners(); 
        buyButton.onClick.AddListener(OnBuyButtonClick);
    }

    /// <summary>
    /// ShopItem 데이터를 받아 UI에 표시합니다.
    /// 아이콘은 어드레서블 주소를 이용해 비동기적으로 로드합니다.
    /// </summary>
    public async void DisplayUnit(ShopItem shopItem)
    {
        this.currentShopItem = shopItem;
        int bindVersion = ++iconBindVersion;
        unitIcon.sprite = null;

        if (shopItem.UnitData == null)
        {
            gameObject.SetActive(false);
            return;
        }

        gameObject.SetActive(true);
        isPurchased = false;
        
        unitNameText.text = shopItem.UnitData.unitName;
        unitCostText.text = $"{shopItem.CalculatedCost}";
        starLevelText.text = $"{shopItem.StarLevel}성";

        // 코스트에 맞는 테두리 이미지 설정
        Image borderImage = buyButton.GetComponent<Image>();
        if (borderImage != null && costBorders != null)
        {
            // 테두리는 유닛의 기본 코스트를 기준으로 합니다.
            int baseCost = shopItem.UnitData.cost;
            if (baseCost >= 1 && baseCost <= costBorders.Length)
            {
                borderImage.sprite = costBorders[baseCost - 1];
            }
        }

        buyButton.interactable = true;
        if(purchasedOverlay) purchasedOverlay.SetActive(false);

        if (!string.IsNullOrEmpty(shopItem.UnitData.unitIcon))
        {
            Sprite loadedIcon = await UISpriteCache.LoadAsync(shopItem.UnitData.unitIcon);
            if (this != null &&
                bindVersion == iconBindVersion &&
                loadedIcon != null)
            {
                unitIcon.sprite = loadedIcon;
            }
        }
    }

    /// <summary>
    /// 구매 버튼을 클릭했을 때 호출됩니다.
    /// </summary>
    private void OnBuyButtonClick()
    {
        if (currentShopItem.UnitData != null && shopManager != null && !isPurchased)
        {
            // 기존: GameEvents.TriggerUnitPurchased(...)
            // 변경: BuyUnitCommand 생성 및 실행
            var command = new BuyUnitCommand(shopManager.playerManager.playerId, this.slotIndex);
            GameManagers.Instance.CommandProcessor.RequestCommandExecution(command);
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

    private void OnDestroy()
    {
        iconBindVersion++;
    }
}
