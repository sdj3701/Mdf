using System;
using UnityEngine;

[CreateAssetMenu(fileName = "WallProgression_Default", menuName = "Game/Walls/Wall Progression")]
public sealed class WallProgressionData : ScriptableObject
{
    [SerializeField] private WallLevelData[] levels = Array.Empty<WallLevelData>();

    public int LevelCount => levels?.Length ?? 0;
    public int MaxLevel => LevelCount;

    public bool TryGetLevel(int level, out WallLevelData data)
    {
        data = null;
        if (levels == null || level < 1 || level > levels.Length)
        {
            return false;
        }

        data = levels[level - 1];
        return data != null && data.Level == level;
    }

    public bool TryGetNextLevel(int currentLevel, out WallLevelData nextLevel)
    {
        return TryGetLevel(currentLevel + 1, out nextLevel);
    }

    public bool IsValid(out string reason)
    {
        if (levels == null || levels.Length == 0)
        {
            reason = "wall_progression_empty";
            return false;
        }

        float previousHealth = 0f;
        for (int i = 0; i < levels.Length; i++)
        {
            WallLevelData entry = levels[i];
            int expectedLevel = i + 1;
            if (entry == null)
            {
                reason = $"wall_level_missing:{expectedLevel}";
                return false;
            }
            if (entry.Level != expectedLevel)
            {
                reason = $"wall_level_not_contiguous:{entry.Level}:{expectedLevel}";
                return false;
            }
            if (entry.MaxHealth <= previousHealth)
            {
                reason = $"wall_health_not_increasing:{entry.Level}";
                return false;
            }
            if (entry.Level == 1 && entry.UpgradeCostFromPreviousLevel != 0)
            {
                reason = "wall_level_one_upgrade_cost_must_be_zero";
                return false;
            }

            previousHealth = entry.MaxHealth;
        }

        reason = string.Empty;
        return true;
    }

#if UNITY_EDITOR
    public void ConfigureForEditor(WallLevelData[] configuredLevels)
    {
        levels = configuredLevels ?? Array.Empty<WallLevelData>();
    }
#endif
}
