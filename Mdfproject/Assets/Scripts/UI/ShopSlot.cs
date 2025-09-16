using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.AddressableAssets; // 어드레서블 시스템 사용을 위해 추가
using UnityEngine.ResourceManagement.AsyncOperations; // 비동기 작업 핸들 관리를 위해 추가

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

    // 아이콘을 비동기 로드할 때 메모리 관리를 위해 로딩 핸들을 저장합니다.
    private AsyncOperationHandle<Sprite> iconLoadHandle;

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
    /// 아이콘은 어드레서블 주소를 이용해 비동기적으로 로드합니다.
    /// </summary>
    public async void DisplayUnit(ShopItem shopItem)
    {
        this.currentShopItem = shopItem;
        
        // --- 1. 이전 아이콘 로딩 작업이 있었다면 해제 (메모리 누수 방지) ---
        if (iconLoadHandle.IsValid())
        {
            Addressables.Release(iconLoadHandle);
        }
        // 아이콘을 잠시 기본 이미지나 null로 초기화합니다.
        unitIcon.sprite = null;

        // --- 2. 아이템 데이터 유효성 검사 ---
        if (shopItem.UnitData == null)
        {
            // 유효하지 않은 아이템이면 슬롯을 비활성화하고 종료합니다.
            gameObject.SetActive(false);
            return;
        }

        // --- 3. 슬롯 상태 초기화 및 기본 정보 표시 ---
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

        // --- 4. 어드레서블을 통해 아이콘 비동기 로드 ---
        if (!string.IsNullOrEmpty(shopItem.UnitData.unitIcon))
        {
            // 주소를 사용해 스프라이트 로딩 작업을 시작하고 핸들을 저장합니다.
            iconLoadHandle = Addressables.LoadAssetAsync<Sprite>(shopItem.UnitData.unitIcon);
            
            // 로딩이 완료될 때까지 기다립니다.
            await iconLoadHandle.Task;

            // 로딩이 완료된 후, 이 오브젝트가 여전히 유효하고 로딩에 성공했다면 스프라이트를 적용합니다.
            if (this != null && iconLoadHandle.Status == AsyncOperationStatus.Succeeded)
            {
                unitIcon.sprite = iconLoadHandle.Result;
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
            GameEvents.TriggerUnitPurchased(shopManager.playerManager, currentShopItem.UnitData, currentShopItem.StarLevel);
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

    // 오브젝트가 파괴될 때 로드된 어드레서블 에셋을 확실히 해제하여 메모리 누수를 방지합니다.
    private void OnDestroy()
    {
        if (iconLoadHandle.IsValid())
        {
            Addressables.Release(iconLoadHandle);
        }
    }
}