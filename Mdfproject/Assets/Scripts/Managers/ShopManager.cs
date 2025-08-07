// Assets/Scripts/Managers/ShopManager.cs
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class ShopManager : MonoBehaviour
{
    public PlayerManager playerManager;
    public List<UnitData> allUnitDatabase; // 구매 가능한 모든 유닛 데이터 목록
    public ShopSlot[] shopSlots; // 씬에 있는 5개의 ShopSlot UI
    
    private int rerollCost = 2;

    void Start()
    {
        foreach (var slot in shopSlots)
        {
            slot.Initialize(this);
        }
        // 첫 라운드 리롤은 GameManager가 호출
    }

    public void Reroll()
    {
        // TODO: 리롤 비용 차감 로직 추가
        // if (!playerManager.SpendGold(rerollCost)) return;

        for (int i = 0; i < shopSlots.Length; i++)
        {
            UnitData randomUnit = allUnitDatabase[Random.Range(0, allUnitDatabase.Count)];
            shopSlots[i].DisplayUnit(randomUnit);
        }
        Debug.Log($"Player {playerManager.playerId}의 상점이 리롤되었습니다.");
    }

    public void TryBuyUnit(UnitData unitToBuy, ShopSlot slot)
    {
        if (playerManager.SpendGold(unitToBuy.cost))
        {
            Debug.Log($"{unitToBuy.unitName} 구매 성공!");
            playerManager.AddUnit(unitToBuy); // 플레이어에게 유닛 추가
            slot.SetPurchased(); // 슬롯을 구매 완료 상태로 변경
        }
        else
        {
            Debug.Log("골드가 부족하여 구매에 실패했습니다.");
        }
    }
}