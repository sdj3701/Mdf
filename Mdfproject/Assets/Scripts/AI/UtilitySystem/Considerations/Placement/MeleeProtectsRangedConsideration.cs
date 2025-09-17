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
            int totalRangedUnits = 0;
            foreach (var ally in context.AlliedUnitsOnField)
            {
                if (ally.Data != null && ally.Data.unitType == UnitType.Ranged)
                {
                    totalRangedUnits++;
                    Vector3Int originalAllyPos = ally.transform.position.ToVector3Int();

                    // 원거리 유닛의 좌표를 AI 필드 좌표계로 변환
                    // 원거리 유닛이 y=-10 영역에 있다면 +10 오프셋 적용
                    Vector3Int allyPos = originalAllyPos;
                    if (originalAllyPos.y <= -5) // y=-10 영역에 있는 경우
                    {
                        allyPos = new Vector3Int(originalAllyPos.x, originalAllyPos.y + 10, originalAllyPos.z);
                    }

                    float distance = Vector3Int.Distance(context.PlacementPosition, allyPos);

                    if (distance <= PROTECT_RADIUS)
                    {
                        rangedAlliesNearby++;
                    }
                }
            }

            float score = Mathf.Clamp01(rangedAlliesNearby / NORMALIZATION_FACTOR);

            // 주변 원거리 유닛 수에 따라 점수를 계산하고 정규화합니다.
            return score;
        }
    }
}
