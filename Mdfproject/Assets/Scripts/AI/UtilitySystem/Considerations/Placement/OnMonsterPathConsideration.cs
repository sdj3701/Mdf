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
            
            var astarGrid = context.Player.astarGrid;
            if (astarGrid == null)
            {
                // 경로 정보를 알 수 없으면 이 평가를 무시합니다.
                return 0.5f;
            }
            
            var monsterPath = astarGrid.FinalPath;

            // 경로가 없거나 계산되지 않은 경우 (예: 준비 단계 시작 직후)
            if (monsterPath == null || monsterPath.Count == 0)
            {
                // 경로가 없으면 다른 요소(뭉치기, 범위 등)로 위치를 결정하도록 중립 점수를 줍니다.
                return 0.5f;
            }

            Vector3Int placementPos = context.PlacementPosition;

            // 배치 위치가 몬스터 경로에 포함되는지 확인합니다.
            bool isOnPath = monsterPath.Any(node => node.x == placementPos.x && node.y == placementPos.y);

            // 경로 위에 있다면 최고점, 아니면 최저점을 부여하여 경로 위 배치를 강력하게 유도합니다.
            return isOnPath ? 1.0f : 0.0f;
        }
    }
}
