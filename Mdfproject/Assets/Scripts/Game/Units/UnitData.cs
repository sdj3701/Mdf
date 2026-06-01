using UnityEngine;

public enum UnitType
{
    Melee,
    Ranged
}

public enum ManaRegenType
{
    OnAttack,
    Passive
}

public enum AttackTargetType
{
    Single,
    Splash
}

[CreateAssetMenu(fileName = "New UnitData", menuName = "Game/Unit Data")]
public class UnitData : ScriptableObject
{
    [Header("Common")]
    public string unitName;

    [AddressableKey(typeof(Sprite))]
    public string unitIcon;

    public int cost;
    public UnitType unitType;

    [Header("Stats")]
    public float baseHealth;
    public float baseAttackDamage;
    public float attackSpeed;
    public float attackRange;
    public DamageType damageType;
    public float defense;
    public float magicResistance;

    [Header("Special")]
    public int blockCount;
    public AttackTargetType attackTargetType = AttackTargetType.Single;
    public float splashRadius = 0f;

    [Header("Mana")]
    public ManaRegenType manaRegenType = ManaRegenType.OnAttack;
    public float manaOnAttack = 15f;
    public float manaPerSecond = 5f;

    [Header("Star Level Assets")]
    [AddressableKey(typeof(GameObject))]
    public string[] prefabsByStarLevel = new string[3];

    [AddressableKey(typeof(SkillData))]
    public string[] skillsByStarLevel = new string[3];

    [Header("Basic Attack VFX")]
    [Tooltip("Shared profile for slash and projectile basic attack VFX. The tuning scenes save into this asset.")]
    public BasicAttackVfxProfile basicAttackVfxProfile;

    public ProjectileVfxConfig GetProjectileVfxConfig()
    {
        return basicAttackVfxProfile != null ? basicAttackVfxProfile.GetProjectileConfig() : null;
    }

    public float ResolveProjectileSpeed(float fallbackSpeed = ProjectileVfxConfig.DefaultProjectileSpeed)
    {
        ProjectileVfxConfig config = GetProjectileVfxConfig();
        return config != null ? config.ResolveProjectileSpeed(fallbackSpeed) : fallbackSpeed;
    }

    public string GetProjectilePrefabKey()
    {
        ProjectileVfxConfig config = GetProjectileVfxConfig();
        return config != null && config.HasProjectileKey ? config.projectileKey : string.Empty;
    }

    public BasicAttackVfxConfig GetBasicAttackVfxConfig(int starLevel)
    {
        BasicAttackVfxConfig config = basicAttackVfxProfile != null ? basicAttackVfxProfile.GetSlashConfig(starLevel) : null;
        return config != null && config.HasPrefabKey ? config : null;
    }

    private void OnValidate()
    {
        if (basicAttackVfxProfile != null)
        {
            basicAttackVfxProfile.EnsureConfigs();
        }
    }
}
