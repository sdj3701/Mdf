using UnityEngine;

public enum BasicAttackVfxProfileKind
{
    None = 0,
    Slash = 1,
    Projectile = 2,
    Hybrid = 3
}

[CreateAssetMenu(fileName = "BasicAttackVfxProfile", menuName = "Game/VFX/Basic Attack VFX Profile")]
public sealed class BasicAttackVfxProfile : ScriptableObject
{
    [Header("Profile")]
    public BasicAttackVfxProfileKind profileKind = BasicAttackVfxProfileKind.None;

    [Header("Slash")]
    [Tooltip("Slash VFX prefab and placement settings. Element 0 is 1-star, 1 is 2-star, and 2 is 3-star.")]
    public BasicAttackVfxConfig[] slashConfigsByStarLevel = new BasicAttackVfxConfig[3];

    [Header("Projectile")]
    [Tooltip("Ranged basic attack muzzle flash, projectile, and impact flash settings.")]
    public ProjectileVfxConfig projectileVfxConfig = ProjectileVfxConfig.CreateDefault();

    private void OnValidate()
    {
        EnsureConfigs();
    }

    public void EnsureConfigs()
    {
        if (slashConfigsByStarLevel == null || slashConfigsByStarLevel.Length != 3)
        {
            var resized = new BasicAttackVfxConfig[3];
            if (slashConfigsByStarLevel != null)
            {
                int count = Mathf.Min(3, slashConfigsByStarLevel.Length);
                for (int i = 0; i < count; i++)
                {
                    resized[i] = slashConfigsByStarLevel[i];
                }
            }

            slashConfigsByStarLevel = resized;
        }

        for (int i = 0; i < slashConfigsByStarLevel.Length; i++)
        {
            if (slashConfigsByStarLevel[i] == null)
            {
                slashConfigsByStarLevel[i] = BasicAttackVfxConfig.CreateDefault();
            }
        }

        if (projectileVfxConfig == null)
        {
            projectileVfxConfig = ProjectileVfxConfig.CreateDefault();
        }
    }

    public BasicAttackVfxConfig GetSlashConfig(int starLevel)
    {
        int index = Mathf.Clamp(starLevel - 1, 0, 2);
        EnsureConfigs();
        return slashConfigsByStarLevel[index];
    }

    public ProjectileVfxConfig GetProjectileConfig()
    {
        EnsureConfigs();
        return projectileVfxConfig;
    }
}
