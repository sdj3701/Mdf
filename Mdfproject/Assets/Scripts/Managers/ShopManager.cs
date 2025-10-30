// Assets/Scripts/Managers/ShopManager.cs
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;

public class ShopManager : MonoBehaviour
{
    public PlayerManager playerManager;
    private List<UnitData> allUnitDatabase = new List<UnitData>();
    [SerializeField] private int rerollCost = 2;

    // [변경됨] 이제 UnitData가 아닌 ShopItem 리스트를 관리합니다.
    private List<ShopItem> currentShopItems = new List<ShopItem>();
    private bool[] _isSlotSold = new bool[5];
    
    public bool IsDatabaseLoaded { get; private set; } = false;
    private UniTaskCompletionSource<bool> databaseLoadTask = new UniTaskCompletionSource<bool>();

    void Awake()
    {
        LoadAllUnitsFromAddressables();
    }

    // (Addressables 로딩 코드는 기존과 동일)
    private async void LoadAllUnitsFromAddressables()
    {
        var handle = UnityEngine.AddressableAssets.Addressables.LoadAssetsAsync<UnitData>("UnitData", null);
        await handle.Task;

        if (handle.Status == UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus.Succeeded)
        {
            allUnitDatabase = handle.Result.ToList();
            IsDatabaseLoaded = true;
            databaseLoadTask.TrySetResult(true);
        }
        else
        {
            databaseLoadTask.TrySetException(handle.OperationException);
        }
    }

    public UniTask WaitUntilDatabaseLoaded() => databaseLoadTask.Task.AsUniTask();

    // [변경됨] 반환 타입이 List<ShopItem>으로 변경되었습니다.
    public List<ShopItem> GetCurrentShopItems() => currentShopItems;
    public int GetRerollCost() => rerollCost;

    // [핵심 로직] Reroll 메서드가 성급 확률을 계산하도록 완전히 변경됩니다.
    public void Reroll(bool isFree = false)
    {
        if (!IsDatabaseLoaded)
        {
            Debug.LogWarning("유닛 데이터베이스가 아직 로드되지 않아 리롤할 수 없습니다.");
            return;
        }

        if (!isFree && !playerManager.SpendGold(rerollCost))
        {
            Debug.Log($"Player {playerManager.playerId}: 골드가 부족하여 리롤할 수 없습니다.");
            return;
        }

        currentShopItems.Clear();
        for (int i = 0; i < _isSlotSold.Length; i++)
        {
            _isSlotSold[i] = false;
        }

        for (int i = 0; i < 5; i++) // 5개의 슬롯을 채웁니다.
        {
            if (allUnitDatabase.Count > 0)
            {
                // 1. 먼저 어떤 유닛이 나올지 랜덤으로 선택합니다.
                UnitData randomUnitData = allUnitDatabase[Random.Range(0, allUnitDatabase.Count)];

                // 2. 해당 유닛의 성급을 확률에 따라 결정합니다.
                int starLevel;
                float roll = Random.value; // 0.0 ~ 1.0 사이의 랜덤 값

                if (roll < 0.7f) // 70% 확률
                {
                    starLevel = 1;
                }
                else // 30% 확률
                {
                    starLevel = 2;
                }

                // 3. 결정된 유닛과 성급으로 ShopItem을 만들어 리스트에 추가합니다.
                currentShopItems.Add(new ShopItem(randomUnitData, starLevel));
            }
        }
        Debug.Log($"Player {playerManager.playerId}의 상점이 리롤되었습니다. (무료: {isFree})");
        GameEvents.TriggerShopRefreshed(playerManager);
    }

    /// <summary>
    /// 유닛 데이터 로딩을 보장하고, 상점 아이템 리롤을 실행합니다.
    /// 이 함수를 호출하면 currentShopItems가 채워집니다 (Count > 0).
    /// </summary>
    public async UniTask EnsureShopRerolledAsync()
    {
        // 1. 유닛 데이터(Addressables) 로드가 완료될 때까지 기다림
        // Reroll 함수 내부에서 IsDatabaseLoaded를 체크하지만, 비동기로 외부에서 기다려주어 확실하게 보장합니다.
        await WaitUntilDatabaseLoaded(); 
        
        // 2. 데이터 로딩이 완료되면 Reroll을 호출하여 currentShopItems를 채움
        //    * Reroll() 함수 내부에 currentShopItems.Add(...) 로직이 이미 구현되어 있습니다.
        
        // 상점 아이템이 0개일 때만 리롤을 수행하여 채웁니다. (새 라운드 시작 등)
        if (currentShopItems.Count == 0)
        {
            Debug.Log($"[ShopManager] 상점 데이터가 비어있어 Reroll을 강제 실행하여 currentShopItems를 채웁니다.");
            Reroll(isFree: true); // 처음 상점을 채우는 것이므로 무료 리롤로 처리합니다.
        }
    }

    public void MarkSlotAsPurchased(int slotIndex)
    {
        if (slotIndex >= 0 && slotIndex < _isSlotSold.Length)
        {
            _isSlotSold[slotIndex] = true;
        }
    }

    public Dictionary<int, ShopItem> GetAvailableShopItems()
    {
        var availableItems = new Dictionary<int, ShopItem>();
        for (int i = 0; i < currentShopItems.Count; i++)
        {
            if (!_isSlotSold[i])
            {
                availableItems.Add(i, currentShopItems[i]);
            }
        }
        return availableItems;
    }
}
