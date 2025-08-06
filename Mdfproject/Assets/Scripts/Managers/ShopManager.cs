using UnityEngine;
using System.Collections.Generic;

public class ShopManager : MonoBehaviour
{
    public PlayerManager playerManager; // 플레이어 정보 (골드 등)
    public List<UnitData> allUnitDatabase; // 구매 가능한 모든 유닛 데이터 목록
    public ShopSlot[] shopSlots; // 씬에 있는 5개의 ShopSlot UI

    void Start()
    {
        // 각 슬롯 초기화
        foreach (var slot in shopSlots)
        {
            slot.Initialize(this);
        }
        Reroll(); // 게임 시작 시 한번 리롤
    }

    /// <summary>
    /// 상점을 새로고침(리롤)하는 함수
    /// </summary>
    [ContextMenu("Test Reroll")] // 테스트를 위해 인스펙터에서 바로 실행 가능하게 함
    public void Reroll()
    {
        // TODO: 리롤 비용 차감 로직
        // playerManager.SpendGold(2);

        // 5개의 슬롯에 랜덤 유닛 표시
        for (int i = 0; i < shopSlots.Length; i++)
        {
            // 데이터베이스에서 랜덤 유닛 선택 (실제로는 레벨별 확률 등 복잡한 로직 필요)
            UnitData randomUnit = allUnitDatabase[Random.Range(0, allUnitDatabase.Count)];
            shopSlots[i].DisplayUnit(randomUnit);
        }
    }

    /// <summary>
    /// 유닛 구매를 시도하는 함수 (ShopSlot이 호출)
    /// </summary>
    public void TryBuyUnit(UnitData unitToBuy)
    {
        // 플레이어가 돈이 충분한지 확인
        if (playerManager.SpendGold(unitToBuy.cost))
        {
            // 구매 성공!
            Debug.Log(unitToBuy.unitName + " 구매 성공!");
            // TODO: 구매한 유닛을 플레이어의 벤치(대기석)에 추가하는 로직
        }
        else
        {
            // 구매 실패
            Debug.Log("골드가 부족합니다!");
        }
    }
}