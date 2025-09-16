using UnityEngine;
using System.Linq;

namespace AI.UtilitySystem.Considerations.Placement
{
    /// <summary>
    /// 근접 유닛이 몬스터의 예상 이동 경로 위에 배치될 때 높은 점수를 부여합니다.
    /// </summary>
    public class OnMonsterPathConsideration : Consideration
    {
        public override float Score(AIContext context)
        {
            // 이 평가는 근접 유닛에게만 의미가 있습니다.
            if (context.UnitToPlace.unitType != UnitType.Melee)
            {
                return 0.5f; // 근접 유닛이 아니면 중립 점수 반환
            }
            
            var monsterPath = context.MonsterPath;

            // 경로가 없거나 계산되지 않은 경우 (예: 유닛에 의해 길이 막힘)
            if (monsterPath == null || monsterPath.Count == 0)
            {
                // 경로를 찾지 못했다면, 이 고려사항은 최저 점수를 반환하여 다른 고려사항으로 결정되도록 합니다.
                return 0.0f;
            }

            Vector3Int placementPos = context.PlacementPosition;

            // 배치 위치가 몬스터 경로에 포함되는지 확인합니다.
            bool isOnPath = monsterPath.Any(node => node.x == placementPos.x && node.y == placementPos.y);

            // 경로 위에 있다면 최고점, 아니면 최저점을 부여하여 경로 위 배치를 강력하게 유도합니다.
            return isOnPath ? 1.0f : 0.0f;
        }
    }
}
