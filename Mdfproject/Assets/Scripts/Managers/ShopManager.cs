// Assets/Scripts/Managers/ShopManager.cs
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class ShopManager : MonoBehaviour
{
    public PlayerManager playerManager;
    private List<UnitData> allUnitDatabase = new List<UnitData>();
    [SerializeField] private int rerollCost = 2;
    private List<UnitData> currentShopItems = new List<UnitData>();
    private bool isDatabaseLoaded = false;

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
            isDatabaseLoaded = true;
            Debug.Log($"Player {playerManager.playerId}: {allUnitDatabase.Count}개의 유닛 데이터를 성공적으로 로드했습니다.");
        }
        else
        {
            Debug.LogError($"어드레서블에서 유닛 데이터 로딩 실패: {handle.OperationException}");
        }
    }

    public List<UnitData> GetCurrentShopItems() => currentShopItems;
    public int GetRerollCost() => rerollCost;

    public void Reroll(bool isFree = false)
    {
        if (!isDatabaseLoaded)
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
        // ✅ [핵심 추가] 현재 게임 상태가 '준비' 단계일 때만 구매가 가능하도록 제한합니다.
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