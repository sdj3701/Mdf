// Assets/Scripts/Managers/ShopManager.cs
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks; // UniTask 사용을 위해 추가

public class ShopManager : MonoBehaviour
{
    public PlayerManager playerManager;
    private List<UnitData> allUnitDatabase = new List<UnitData>();
    [SerializeField] private int rerollCost = 2;
    private List<UnitData> currentShopItems = new List<UnitData>();
    
    // ✅ 데이터 로딩 완료 여부를 외부에서 확인할 수 있도록 public으로 변경
    public bool IsDatabaseLoaded { get; private set; } = false;
    private UniTaskCompletionSource<bool> databaseLoadTask = new UniTaskCompletionSource<bool>();

    void Awake()
    {
        LoadAllUnitsFromAddressables();
    }

    private async void LoadAllUnitsFromAddressables()
    {
        var handle = UnityEngine.AddressableAssets.Addressables.LoadAssetsAsync<UnitData>("Unit", null);
        await handle.Task;

        if (handle.Status == UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus.Succeeded)
        {
            allUnitDatabase = handle.Result.ToList();
            IsDatabaseLoaded = true; // ✅ isDatabaseLoaded -> IsDatabaseLoaded
            databaseLoadTask.TrySetResult(true); // ✅ 로딩이 완료되었음을 알림
            Debug.Log($"Player {playerManager.playerId}: {allUnitDatabase.Count}개의 유닛 데이터를 성공적으로 로드했습니다.");
        }
        else
        {
            databaseLoadTask.TrySetException(handle.OperationException); // ✅ 실패 시 에러 전파
            Debug.LogError($"어드레서블에서 유닛 데이터 로딩 실패: {handle.OperationException}");
        }
    }

    // ✅ 데이터 로딩이 끝날 때까지 기다리는 함수 추가
    public UniTask WaitUntilDatabaseLoaded()
    {
        return databaseLoadTask.Task.AsUniTask();
    }

    public List<UnitData> GetCurrentShopItems() => currentShopItems;
    public int GetRerollCost() => rerollCost;

    public void Reroll(bool isFree = false)
    {
        // ✅ isDatabaseLoaded -> IsDatabaseLoaded
        if (!IsDatabaseLoaded)
        {
            Debug.LogWarning("유닛 데이터베이스가 아직 로드되지 않아 리롤할 수 없습니다.");
            return;
        }

        if (!isFree)
        {
            if (!playerManager.SpendGold(rerollCost))
            {
                Debug.Log($"Player {playerManager.playerId}: 골드가 부족하여 리롤할 수 없습니다.");
                return;
            }
        }

        currentShopItems.Clear();
        for (int i = 0; i < 5; i++)
        {
            if (allUnitDatabase.Count > 0)
            {
                UnitData randomUnit = allUnitDatabase[Random.Range(0, allUnitDatabase.Count)];
                currentShopItems.Add(randomUnit);
            }
        }
        Debug.Log($"Player {playerManager.playerId}의 상점이 리롤되었습니다. (무료: {isFree})");
    }

    public void TryBuyUnit(UnitData unitToBuy, ShopSlot slot)
    {
        if (GameManagers.Instance != null && GameManagers.Instance.GetGameState() != GameManagers.GameState.Prepare)
        {
            Debug.LogWarning("준비 단계에서만 유닛을 구매할 수 있습니다.");
            return;
        }

        if (playerManager.SpendGold(unitToBuy.cost))
        {
            Debug.Log($"{unitToBuy.unitName} 구매 성공!");
            playerManager.AddUnit(unitToBuy);
            slot.SetPurchased();
        }
        else
        {
            Debug.Log("골드가 부족하여 구매에 실패했습니다.");
        }
    }
}