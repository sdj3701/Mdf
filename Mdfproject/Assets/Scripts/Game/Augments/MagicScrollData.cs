using UnityEngine;

public enum MagicScrollTacticalRole
{
    Damage,
    Debuff,
    Buff,
    Heal,
    Utility
}

public enum MagicScrollTargetDomain
{
    EnemyUnits,
    AlliedMonsters,
    EnemyUnitsNearAlliedMonsters,
    GroundPoint
}

[CreateAssetMenu(fileName = "New MagicScrollData", menuName = "Game/Magic Scroll Data")]
public class MagicScrollData : ScriptableObject
{
    [Header("Base Info")]
    [Tooltip("Scroll display name.")]
    public string scrollName;

    [TextArea(2, 4)]
    [Tooltip("Scroll effect description.")]
    public string description;

    [Tooltip("Icon shown in UI.")]
    public Sprite icon;

    [Tooltip("Augment tier for this scroll.")]
    public AugmentTier tier;

    [Header("AI Tactical Metadata")]
    [Tooltip("How AI should classify this scroll's strategic purpose.")]
    public MagicScrollTacticalRole tacticalRole = MagicScrollTacticalRole.Utility;

    [Tooltip("Authority-owned target domain used by AI scoring and server validation.")]
    public MagicScrollTargetDomain targetDomain = MagicScrollTargetDomain.GroundPoint;

    [Tooltip("Whether shared AI/HumanBot policies may choose this scroll.")]
    public bool canAiUse = false;

    [Tooltip("Minimum evaluator score required before AI may use this scroll.")]
    public float aiMinValue = 0f;

    [Header("Skill Reference")]
    [Tooltip("Skill data executed by this scroll on State Authority.")]
    public SkillData skillData;
}
