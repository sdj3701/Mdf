using UnityEngine;

[CreateAssetMenu(fileName = "UnitData_King_New", menuName = "Game/King/Unit Data")]
public class KingUnitData : ScriptableObject, IStableContentIdentity
{
    [SerializeField, Tooltip("Immutable gameplay identity. Do not change after release.")]
    private string contentId;

    public string ContentId => StableDataKeyUtility.NormalizeContentId(contentId);
    public int ContentIdHash => StableDataKeyUtility.StableContentIdHash(contentId);

    [Header("Definition")]
    public UnitData baseUnitData;
    public KingBuffData kingBuff;
    public KingSkillData kingSkill;

    [Header("Goal presentation")]
    [Min(0.1f)] public float presentationScale = 1.3f;
    public Vector3 presentationOffset = Vector3.zero;
    public Vector3 presentationEulerAngles = Vector3.zero;

    [Header("King combat")]
    [Min(0f)] public float baseAttackDamageMultiplier = 1f;
    [Min(0f)] public float baseAttackSpeedMultiplier = 1f;
    [Tooltip("Final responsive floor for King attacks. Faster base units keep their authored attack speed.")]
    [Min(0f)] public float minimumAttackSpeed = 1f;
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
        float resolvedAttackSpeed = Mathf.Max(0f, baseUnitData.attackSpeed)
                                    * Mathf.Max(0f, baseAttackSpeedMultiplier)
                                    * Mathf.Max(0f, 1f + growth + augmentPercentBonus);
        return Mathf.Max(Mathf.Max(0f, minimumAttackSpeed), resolvedAttackSpeed);
    }
}
