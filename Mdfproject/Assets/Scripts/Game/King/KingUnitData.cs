using UnityEngine;

[CreateAssetMenu(fileName = "UnitData_King_New", menuName = "Game/King/Unit Data")]
public class KingUnitData : ScriptableObject
{
    [Header("Definition")]
    public UnitData baseUnitData;
    public KingBuffData kingBuff;
    public KingSkillData kingSkill;

    [Header("Goal presentation")]
    [Min(0.1f)] public float presentationScale = 1.3f;
    public Vector3 presentationOffset = Vector3.zero;
    public Vector3 presentationEulerAngles = Vector3.zero;

    [Header("King head presentation")]
    [Tooltip("Use King-only HeadLook values instead of changing the shared base-unit prefab.")]
    public bool overridePresentationHeadLook;
    [Tooltip("Aim this King presentation at the active local camera instead of using the base unit's fixed look direction.")]
    public bool presentationHeadLookAtCamera;
    [Range(0f, 1f)] public float presentationHeadLookWeight = 0.407f;
    [Range(0f, 5f)] public float presentationHeadLookTiltAngle = 1.75f;
    [Range(0f, 1f)] public float presentationHeadLookBodyWeight;
    [Range(0f, 1f)] public float presentationHeadLookHeadWeight = 1f;
    [Range(0f, 1f)] public float presentationHeadLookClampWeight = 0.5f;

    [Header("King combat")]
    [Min(0f)] public float baseAttackDamageMultiplier = 1f;
    [Min(0f)] public float baseAttackSpeedMultiplier = 1f;
    [Tooltip("Additive growth applied for every round after round 1. 0.1 = +10% per round.")]
    [Min(0f)] public float attackDamageGrowthPerRound = 0.1f;
    [Tooltip("Additive growth applied for every round after round 1. 0.05 = +5% per round.")]
    [Min(0f)] public float attackSpeedGrowthPerRound = 0.05f;

    public float ResolveAttackDamage(int round, float augmentPercentBonus = 0f)
    {
        if (baseUnitData == null)
        {
            return 0f;
        }

        int completedGrowthSteps = Mathf.Max(0, round - 1);
        float growth = completedGrowthSteps * Mathf.Max(0f, attackDamageGrowthPerRound);
        return Mathf.Max(0f, baseUnitData.baseAttackDamage)
               * Mathf.Max(0f, baseAttackDamageMultiplier)
               * Mathf.Max(0f, 1f + growth + augmentPercentBonus);
    }

    public float ResolveAttackSpeed(int round, float augmentPercentBonus = 0f)
    {
        if (baseUnitData == null)
        {
            return 0f;
        }

        int completedGrowthSteps = Mathf.Max(0, round - 1);
        float growth = completedGrowthSteps * Mathf.Max(0f, attackSpeedGrowthPerRound);
        return Mathf.Max(0f, baseUnitData.attackSpeed)
               * Mathf.Max(0f, baseAttackSpeedMultiplier)
               * Mathf.Max(0f, 1f + growth + augmentPercentBonus);
    }
}
