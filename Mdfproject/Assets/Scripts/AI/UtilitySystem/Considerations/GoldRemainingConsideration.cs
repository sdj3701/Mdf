using UnityEngine;

namespace AI.UtilitySystem.Considerations
{
    // 유닛 구매 후 남는 골드를 평가하는 고려사항
    public class GoldRemainingConsideration : Consideration
    {
        public override float Score(AIContext context)
        {
            var item = context.CurrentShopItem;
            int goldAfterBuy = context.Player.GetGold() - item.CalculatedCost;

            if (goldAfterBuy < context.Player.shopManager.GetRerollCost())
            {
                return 0.0f; // 구매 후 리롤할 돈도 없으면 0점
            }
            // 남는 돈을 0~1 사이로 정규화 (최대 20골드 기준)
            return Mathf.Clamp01((float)goldAfterBuy / 20f);
        }
    }
}
