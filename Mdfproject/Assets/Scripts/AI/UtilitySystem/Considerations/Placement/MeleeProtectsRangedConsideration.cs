using UnityEngine;
using System.Linq;

namespace AI.UtilitySystem.Considerations.Placement
{
    /// <summary>
    /// 근접 유닛을 배치할 때, 주변의 아군 원거리 유닛을 보호하는 위치에 높은 점수를 부여합니다.
    /// </summary>
    public class MeleeProtectsRangedConsideration : Consideration
    {
        private const float PROTECT_RADIUS = 1.5f; // 약 1칸 거리 내
        private const float NORMALIZATION_FACTOR = 3.0f; // 주변 원거리 유닛이 3명일 때 만점으로 가정

        public override float Score(AIContext context)
        {
            // 이 평가는 근접 유닛을 배치할 때만 의미가 있습니다.
            if (context.UnitToPlace.unitType != UnitType.Melee)
            {
                return 0.5f; // 근접 유닛이 아니면 중립 점수 반환
            }

            if (context.AlliedUnitsOnField == null || context.AlliedUnitsOnField.Count == 0)
            {
                return 0f; // 보호할 아군이 없으면 점수 없음
            }

            int rangedAlliesNearby = 0;
            foreach (var ally in context.AlliedUnitsOnField)
            {
                if (ally.Data != null && ally.Data.unitType == UnitType.Ranged)
                {
                    float distance = Vector3Int.Distance(context.PlacementPosition, ally.transform.position.ToVector3Int());
                    if (distance <= PROTECT_RADIUS)
                    {
                        rangedAlliesNearby++;
                    }
                }
            }
            
            // 주변 원거리 유닛 수에 따라 점수를 계산하고 정규화합니다.
            return Mathf.Clamp01(rangedAlliesNearby / NORMALIZATION_FACTOR);
        }
    }
}
