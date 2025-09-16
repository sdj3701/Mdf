using UnityEngine;
using System.Linq;
using AI.UtilitySystem.Considerations.Placement;

namespace AI.UtilitySystem.Considerations.Placement
{
    /// <summary>
    /// 원거리 유닛이 근접 유닛 근처에 배치될 때 높은 점수를 부여합니다.
    /// </summary>
    public class RangedUnitSynergyConsideration : Consideration
    {
        private const float EFFECTIVE_DISTANCE = 5f;

        public override float Score(AIContext context)
        {
            if (context.UnitToPlace.unitType != UnitType.Ranged)
            {
                return 0.5f; // 원거리 유닛이 아니면 이 평가는 의미 없음 (중립 점수)
            }

            var meleeAllies = context.AlliedUnitsOnField
                .Where(u => u.Data != null && u.Data.unitType == UnitType.Melee)
                .ToList();

            if (meleeAllies.Count == 0)
            {
                return 0.2f; // 보호해줄 근접 유닛이 없으면 낮은 점수
            }

            float minDistanceToMelee = float.MaxValue;
            foreach (var meleeAlly in meleeAllies)
            {
                float dist = Vector3Int.Distance(context.PlacementPosition, meleeAlly.transform.position.ToVector3Int());
                if (dist < minDistanceToMelee)
                {
                    minDistanceToMelee = dist;
                }
            }

            // 가장 가까운 근접 유닛과의 거리가 멀수록 점수가 낮아짐
            return 1.0f - Mathf.Clamp01(minDistanceToMelee / EFFECTIVE_DISTANCE);
        }
    }
}
