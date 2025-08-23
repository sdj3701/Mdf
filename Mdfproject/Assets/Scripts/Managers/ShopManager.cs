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
    
    public bool IsDatabaseLoaded { get; private set; } = false;
    private UniTaskCompletionSource<bool> databaseLoadTask = new UniTaskCompletionSource<bool>();

    void Awake()
    {
        LoadAllUnitsFromAddressables();
    }

    // (Addressables 로딩 코드는 기존과 동일)
    private async void LoadAllUnitsFromAddressables()
    {
        var handle = UnityEngine.AddressableAssets.Addressables.LoadAssetsAsync<UnitData>("Unit", null);
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
    }

    // [변경됨] 매개변수가 UnitData에서 ShopItem으로 변경되었습니다.
    public void TryBuyUnit(ShopItem itemToBuy, ShopSlot slot)
    {
        if (GameManagers.Instance != null && GameManagers.Instance.GetGameState() != GameManagers.GameState.Prepare)
        {
            Debug.LogWarning("준비 단계에서만 유닛을 구매할 수 있습니다.");
            return;
        }

        // [변경됨] 가격을 itemToBuy.CalculatedCost에서 가져옵니다.
        if (playerManager.SpendGold(itemToBuy.CalculatedCost))
        {
            // [변경됨] PlayerManager에게 UnitData와 StarLevel을 모두 전달합니다.
            playerManager.AddUnit(itemToBuy.UnitData, itemToBuy.StarLevel);
            slot.SetPurchased();
        }
        else
        {
            Debug.Log("골드가 부족하여 구매에 실패했습니다.");
        }
    }
}