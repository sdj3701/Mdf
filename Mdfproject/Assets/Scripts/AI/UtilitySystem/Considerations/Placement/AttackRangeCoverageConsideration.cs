using System.Collections.Generic;
using UnityEngine;

namespace AI.UtilitySystem.Considerations.Placement
{
    /// <summary>
    /// 유닛의 공격 범위가 맵 안쪽을 얼마나 많이 포함하는지 평가합니다.
    /// 맵 중앙에 가까울수록 높은 점수를 받습니다.
    /// </summary>
    public class AttackRangeCoverageConsideration : Consideration
    {
        public override float Score(AIContext context)
        {
            var fieldManager = context.Player.fieldManager;
            if (fieldManager == null) return 0.5f;

            float range = context.UnitToPlace.attackRange;
            if (range <= 0) return 1.0f; // 공격 범위가 없으면 맵 커버리지에 대한 손실이 없으므로 최고점

            Vector3Int position = context.PlacementPosition;

            // Ground 타일 목록을 해시셋으로 만들어 빠른 조회를 가능하게 합니다.
            var groundTiles = new HashSet<Vector3Int>(fieldManager.GetValidPlacementTiles(UnitType.Melee));
            if (groundTiles.Count == 0) return 0f; // Ground 타일이 없으면 점수를 0으로 처리

            int totalTilesInCircle = 0;
            int coveredGroundTiles = 0;
            int intRange = Mathf.CeilToInt(range);

            // 공격 범위에 포함되는 사각형 영역을 순회
            for (int x = -intRange; x <= intRange; x++)
            {
                for (int y = -intRange; y <= intRange; y++)
                {
                    // 원 안에 있는지 확인
                    if (x * x + y * y <= range * range)
                    {
                        totalTilesInCircle++;
                        Vector3Int currentTilePos = position + new Vector3Int(x, y, 0);
                        
                        // 해당 타일이 Ground 타일인지 확인
                        if (groundTiles.Contains(currentTilePos))
                        {
                            coveredGroundTiles++;
                        }
                    }
                }
            }

            if (totalTilesInCircle == 0) return 0f;

            // 공격 범위 내 타일 중 Ground 타일이 차지하는 비율을 점수로 반환
            return (float)coveredGroundTiles / totalTilesInCircle;
        }
    }
}
