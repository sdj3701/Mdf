using UnityEngine;
using System.Linq;

namespace AI.UtilitySystem.Considerations.Placement
{
    /// <summary>
    /// 아군 유닛과의 평균 거리를 기반으로 점수를 계산하는 고려사항.
    /// 가까이 뭉쳐있을수록 높은 점수를 받습니다 (최대 거리 10칸 기준).
    /// </summary>
    public class ProximityToAlliesConsideration : Consideration
    {
        private const float MAX_DISTANCE = 10f;
        
        public override float Score(AIContext context)
        {
            if (context.AlliedUnitsOnField == null || context.AlliedUnitsOnField.Count == 0)
            {
                // 필드에 다른 아군이 없으면, 이 점수는 0이 되어 공격 범위 효율이 최우선 순위가 됩니다.
                return 0f;
            }

            float totalProximityScore = 0f;
            foreach (var ally in context.AlliedUnitsOnField)
            {
                float distance = Vector3Int.Distance(context.PlacementPosition, ally.transform.position.ToVector3Int());
                
                // 거리가 가까울수록 1에 가까운 점수를 계산합니다.
                float scorePerAlly = 1.0f - Mathf.Clamp01(distance / MAX_DISTANCE);
                
                // [핵심 변경] 점수를 제곱하여 가까운 거리의 가중치를 훨씬 높게 부여합니다.
                // 예: 0.9 -> 0.81, 0.5 -> 0.25. 멀어질수록 점수가 급격히 낮아집니다.
                totalProximityScore += scorePerAlly * scorePerAlly;
            }

            // 점수를 정규화합니다. 아군이 5명일 때 만점이라고 가정하고, 그 이상은 모두 만점으로 처리합니다.
            // 이렇게 하면 아군 유닛의 '수'와 '근접도'를 모두 점수에 반영할 수 있습니다.
            const float normalizationFactor = 5.0f; 
            float score = Mathf.Clamp01(totalProximityScore / normalizationFactor);
            
            // TODO: 유닛 데이터에 '힐러' 역할 태그가 있다면 뭉치는 것의 중요도를 높일 수 있습니다.
            // if (context.UnitToPlace.HasTag("Healer"))
            // {
            //     score *= 1.5f;
            // }

            return score;
        }
    }
    
    // Vector3를 Vector3Int로 변환하는 확장 메서드 (임시)
    public static class Vector3Extensions
    {
        public static Vector3Int ToVector3Int(this Vector3 vec)
        {
            return new Vector3Int(Mathf.RoundToInt(vec.x), Mathf.RoundToInt(vec.y), Mathf.RoundToInt(vec.z));
        }
    }
}
