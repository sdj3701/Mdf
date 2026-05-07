using System.Collections.Generic;
using UnityEngine;

public sealed class PrepareShopDecisionScore
{
    public string UnitKey { get; set; }
    public PrepareUnitRole UnitRole { get; set; }
    public int ShopSlot { get; set; }
    public int StarLevel { get; set; }
    public int Cost { get; set; }
    public int MatchingSameUnitSameStarCount { get; set; }
    public float BaseQualityScore { get; set; }
    public float RoleDeficitBonus { get; set; }
    public float RoleOverTargetPenalty { get; set; }
    public float MergeBonus { get; set; }
    public float PersonaBonus { get; set; }
    public float GoldReservePenalty { get; set; }
    public float FinalScore { get; set; }

    public float CompositionScore => RoleDeficitBonus + RoleOverTargetPenalty;

    public Dictionary<string, object> ToJournalFields()
    {
        return new Dictionary<string, object>
        {
            { "selectedShopSlot", ShopSlot },
            { "selectedUnitKey", UnitKey ?? "unknown-unit" },
            { "selectedUnitRole", UnitCompositionAnalyzer.RoleName(UnitRole) },
            { "selectedUnitStarLevel", StarLevel },
            { "selectedUnitCost", Cost },
            { "matchingSameUnitSameStarCount", MatchingSameUnitSameStarCount },
            { "compositionScore", Format(CompositionScore) },
            { "mergeBonus", Format(MergeBonus) },
            { "rolePenalty", Format(RoleOverTargetPenalty) },
            { "goldReservePenalty", Format(GoldReservePenalty) },
            { "buyScoreBreakdown", new Dictionary<string, object>
                {
                    { "baseQualityScore", Format(BaseQualityScore) },
                    { "roleDeficitBonus", Format(RoleDeficitBonus) },
                    { "roleOverTargetPenalty", Format(RoleOverTargetPenalty) },
                    { "mergeBonus", Format(MergeBonus) },
                    { "personaBonus", Format(PersonaBonus) },
                    { "goldReservePenalty", Format(GoldReservePenalty) },
                    { "finalScore", Format(FinalScore) }
                }
            }
        };
    }

    private static string Format(float value)
    {
        return value.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
    }
}

public sealed class PrepareRerollGateResult
{
    public bool CanReroll { get; set; }
    public string Reason { get; set; }
    public int SoldSlotCount { get; set; }
    public int ShopSlotCount { get; set; }
    public int UnsoldSlotCount { get; set; }
    public int Gold { get; set; }
    public int RerollCost { get; set; }
    public float BestAffordablePurchaseScore { get; set; }
    public string BestAffordableUnitKey { get; set; }

    public Dictionary<string, object> ToJournalFields()
    {
        return new Dictionary<string, object>
        {
            { "rerollGateReason", string.IsNullOrWhiteSpace(Reason) ? "unknown" : Reason },
            { "soldSlotCount", SoldSlotCount },
            { "shopSlotCount", ShopSlotCount },
            { "unsoldSlotCount", UnsoldSlotCount },
            { "gold", Gold },
            { "rerollCost", RerollCost },
            { "bestAffordablePurchaseScore", BestAffordablePurchaseScore.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) },
            { "bestAffordableUnitKey", string.IsNullOrWhiteSpace(BestAffordableUnitKey) ? "none" : BestAffordableUnitKey }
        };
    }
}
