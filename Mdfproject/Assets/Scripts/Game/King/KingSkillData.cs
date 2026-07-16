using UnityEngine;

public enum KingSkillEffectKind
{
    AllFieldMonsters,
    GoalRadius,
    PlayerHealAndAllFieldMonsters
}

[CreateAssetMenu(fileName = "KingSkill_New", menuName = "Game/King/Skill Data")]
public class KingSkillData : ScriptableObject, IStableContentIdentity
{
    [SerializeField, Tooltip("Immutable gameplay identity. Do not change after release.")]
    private string contentId;

    public string ContentId => StableDataKeyUtility.NormalizeContentId(contentId);
    public int ContentIdHash => StableDataKeyUtility.StableContentIdHash(contentId);

    [Header("Presentation")]
    public string skillName;
    [TextArea(2, 6)] public string description;
    [AddressableKey(typeof(Sprite))] public string iconKey;

    [Header("Targets")]
    public KingSkillEffectKind effectKind = KingSkillEffectKind.AllFieldMonsters;
    [Min(0f)] public float radius;
    [Tooltip("0 means every valid target.")]
    [Min(0)] public int maxTargets;

    [Header("Damage and healing")]
    [Min(0f)] public float damageMultiplier = 1f;
    [Min(0f)] public float flatDamage;
    public DamageType damageType = DamageType.Physical;
    [Min(0)] public int healPlayerAmount;

    [Header("Optional status effect")]
    public StatusEffectType statusEffect = StatusEffectType.None;
    [Min(0f)] public float statusDuration;
    [Range(0.05f, 1f)] public float slowMultiplier = 1f;
    [Min(0f)] public float dotTickInterval;
    [Min(0f)] public float dotDamagePerTick;

    public float ResolveDamage(float kingAttackDamage, float skillPowerMultiplier)
    {
        float rawDamage = Mathf.Max(0f, kingAttackDamage) * Mathf.Max(0f, damageMultiplier)
                          + Mathf.Max(0f, flatDamage);
        return rawDamage * Mathf.Max(0f, skillPowerMultiplier);
    }

    public bool TargetsWholeField => effectKind != KingSkillEffectKind.GoalRadius;
    public bool HealsOwner => effectKind == KingSkillEffectKind.PlayerHealAndAllFieldMonsters
                              && healPlayerAmount > 0;
    public bool HasBoundedStatusEffect => statusEffect != StatusEffectType.None
                                          && statusDuration > 0f
                                          && maxTargets > 0;
}
