using UnityEngine;

namespace AI.UtilitySystem.Considerations.Placement
{
    /// <summary>
    /// 근접 유닛이 Ground 타일이 아닌 곳(breakWall 등)에 배치될 때 매우 낮은 점수를 부여합니다.
    /// </summary>
    public class MeleePlacementConsideration : Consideration
    {
        public override float Score(AIContext context)
        {
            // 이 평가는 근접 유닛에게만 의미가 있습니다.
            if (context.UnitToPlace.unitType != UnitType.Melee)
            {
                return 0.5f; // 원거리 유닛의 경우 중립 점수를 반환합니다.
            }

            var fieldManager = context.Player.fieldManager;
            if (fieldManager == null)
            {
                return 0.5f; // 필요한 정보가 없으면 중립 점수를 반환합니다.
            }

            // 배치하려는 위치에 벽(브레이크월 포함)이 있으면 매우 낮은 점수
            if (fieldManager.HasWallAt(context.PlacementPosition))
            {
                // 근접 유닛을 벽 위에 놓으려고 하면 매우 낮은 점수를 부여합니다.
                return 0.05f; 
            }
            
            // 그 외의 경우 (Ground 타일 등) 최고점을 부여하여 배치를 장려합니다.
            return 1.0f;
        }
    }
}
