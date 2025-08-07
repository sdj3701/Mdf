// Assets/Scripts/Managers/FieldManager.cs
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class FieldManager : MonoBehaviour
{
    public PlayerManager playerManager;
    private List<Unit> placedUnits = new List<Unit>();

    // TODO: 타일 데이터 및 그리드 시스템 구현 필요

    public bool TryPlaceUnit(Unit unit, Vector3Int gridPosition)
    {
        // TODO: 배치 유효성 검사 (타일 속성, 다른 유닛 유무 등)
        // if (!IsValidPlacement(gridPosition)) return false;

        unit.transform.position = gridPosition; // 예시 위치
        placedUnits.Add(unit);
        playerManager.ownedUnits.Remove(unit); // 벤치 -> 필드로 이동 개념

        // 배치 후 즉시 조합 검사
        CheckForCombination();
        return true;
    }

    public void CheckForCombination()
    {
        var combinableGroup = placedUnits
            .Where(u => u.starLevel < 3) // 3성 미만 유닛만
            .GroupBy(u => u.unitData.unitName + "_" + u.starLevel) // "유닛이름_성급" 으로 그룹화
            .Where(g => g.Count() >= 3)
            .FirstOrDefault();

        if (combinableGroup != null)
        {
            List<Unit> unitsToCombine = combinableGroup.Take(3).ToList();
            
            UnitData unitData = unitsToCombine[0].unitData;
            int currentStarLevel = unitsToCombine[0].starLevel;

            Debug.Log($"<color=cyan>조합 발생!</color> {unitData.unitName} {currentStarLevel}성 3개 -> {currentStarLevel + 1}성 1개");

            // 2개는 파괴, 1개는 업그레이드
            for (int i = 0; i < 2; i++)
            {
                placedUnits.Remove(unitsToCombine[i]);
                Destroy(unitsToCombine[i].gameObject);
            }
            
            Unit upgradedUnit = unitsToCombine[2];
            upgradedUnit.Upgrade();
            
            // TODO: 업그레이드된 유닛 재배치 또는 이펙트 처리
            
            // 연쇄 조합을 위해 다시 검사
            CheckForCombination();
        }
    }
}