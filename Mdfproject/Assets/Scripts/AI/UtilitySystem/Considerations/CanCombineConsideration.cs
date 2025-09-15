namespace AI.UtilitySystem.Considerations
{
    // 유닛 조합 가능성을 평가하는 고려사항
    public class CanCombineConsideration : Consideration
    {
        public override float Score(AIContext context)
        {
            var item = context.CurrentShopItem;
            if (item.UnitData == null) return 0f;

            int unitsOnField = 0; // TODO: FieldManager에 동일 유닛 개수 세는 기능 필요
            
            // 임시 로직: PlayerManager의 ownedUnits 리스트를 순회하여 개수를 셉니다.
            foreach (var unit in context.Player.ownedUnits)
            {
                if (unit.Data == item.UnitData && unit.starLevel == item.StarLevel)
                {
                    unitsOnField++;
                }
            }

            if (unitsOnField >= 2) return 1.0f; // 2개 있으면 조합 가능성이 매우 높음 (최고 점수)
            if (unitsOnField == 1) return 0.5f; // 1개 있으면 페어를 만들 수 있음 (중간 점수)
            return 0.25f; // 필드에 없는 새로운 유닛이라도 기본적인 가치를 부여 (낮은 점수)
        }
    }
}
