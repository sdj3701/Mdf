// Assets/Scripts/Managers/ShopItem.cs (새 파일)
using UnityEngine;
/// <summary>
/// 상점에서 판매되는 개별 상품의 정보를 담는 구조체입니다.
/// </summary>
public struct ShopItem
{
    public UnitData UnitData { get; private set; }
    public int StarLevel { get; private set; }
    public int CalculatedCost { get; private set; }

    public ShopItem(UnitData unitData, int starLevel)
    {
        this.UnitData = unitData;
        this.StarLevel = starLevel;

        // 2성 유닛의 가격은 1성 유닛(UnitData.cost)의 4배로 계산합니다.
        if (starLevel == 2)
        {
            // Mathf.Pow(2, 2) == 4
            this.CalculatedCost = unitData.cost * (int)Mathf.Pow(2, starLevel);
        }
        else // 1성 또는 기타
        {
            this.CalculatedCost = unitData.cost;
        }
    }
}