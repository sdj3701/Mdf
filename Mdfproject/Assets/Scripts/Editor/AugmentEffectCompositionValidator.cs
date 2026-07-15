#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

/// <summary>
/// Effect-specific validation kept separate from identity validation so the same rules can be
/// exercised by focused tests and reused by the build validator.
/// </summary>
public static class AugmentEffectCompositionValidator
{
    public static void Validate(
        AugmentData augment,
        ICollection<ContentValidationIssue> issues)
    {
        if (augment == null || issues == null)
        {
            return;
        }

        string path = AssetDatabase.GetAssetPath(augment);
        if (!augment.HasAuthoredEffects)
        {
            AddError(
                issues,
                "augment.effects_not_migrated",
                path,
                "Canonical effects are empty. Run the augment effect migration before building.");
            return;
        }

        int bossEffectCount = 0;
        for (int index = 0; index < augment.AuthoredEffects.Count; index++)
        {
            AugmentEffectData effect = augment.AuthoredEffects[index];
            string effectPath = $"{path} effects[{index}]";
            if (effect == null)
            {
                AddError(issues, "augment.effect_null", effectPath, "Effect entries cannot be null.");
                continue;
            }

            if (!Enum.IsDefined(typeof(EffectType), effect.effectType))
            {
                AddError(issues, "augment.effect_type_invalid", effectPath, $"Unknown effect type {(int)effect.effectType}.");
                continue;
            }

            if (!Enum.IsDefined(typeof(TargetType), effect.targetType))
            {
                AddError(issues, "augment.target_type_invalid", effectPath, $"Unknown target type {(int)effect.targetType}.");
                continue;
            }

            if (!IsTargetAllowed(effect.effectType, effect.targetType))
            {
                AddError(
                    issues,
                    "augment.effect_target_invalid",
                    effectPath,
                    $"{effect.effectType} cannot target {effect.targetType}.");
            }

            if (effect.monsterHealthBonusPercent < 0f ||
                effect.monsterDamageBonusPercent < 0f ||
                effect.monsterMoveSpeedBonusPercent < 0f ||
                effect.kingDamageBonusPercent < 0f ||
                effect.kingAttackSpeedBonusPercent < 0f ||
                effect.kingSkillPowerBonusPercent < 0f)
            {
                AddError(issues, "augment.effect_negative_bonus", effectPath, "Percentage bonuses cannot be negative.");
            }

            switch (effect.effectType)
            {
                case EffectType.GrantMagicScroll:
                    if (effect.magicScrollData == null)
                    {
                        AddError(issues, "augment.scroll_missing", effectPath, "GrantMagicScroll requires magicScrollData.");
                    }
                    else if (effect.magicScrollData.skillData == null)
                    {
                        AddError(issues, "augment.scroll_skill_missing", effectPath, "Granted scroll requires skillData.");
                    }
                    break;

                case EffectType.SpawnMonsterOnEnemyField:
                    if (effect.isBossSummon)
                    {
                        bossEffectCount++;
                        if (effect.bossMonsterData == null)
                        {
                            AddError(issues, "augment.boss_missing", effectPath, "Boss summon requires bossMonsterData.");
                        }
                    }
                    else if (effect.monsterSpawnEntries == null ||
                             !effect.monsterSpawnEntries.Any(entry =>
                                 entry != null && entry.monsterData != null && entry.count > 0))
                    {
                        AddError(issues, "augment.spawn_entries_missing", effectPath, "Monster summon requires at least one valid spawn entry.");
                    }

                    if (effect.monsterSpawnEntries != null &&
                        effect.monsterSpawnEntries.Any(entry => entry == null || entry.monsterData == null || entry.count <= 0))
                    {
                        AddError(issues, "augment.spawn_entry_invalid", effectPath, "Every summon entry requires monsterData and a positive count.");
                    }
                    break;

                case EffectType.StrengthenMonsterType:
                    if (effect.strengthenedMonsterData == null)
                    {
                        AddError(issues, "augment.strengthened_monster_missing", effectPath, "Monster strengthening requires strengthenedMonsterData.");
                    }

                    if (effect.monsterHealthBonusPercent <= 0f &&
                        effect.monsterDamageBonusPercent <= 0f &&
                        effect.monsterMoveSpeedBonusPercent <= 0f)
                    {
                        AddError(issues, "augment.strengthened_monster_empty", effectPath, "Monster strengthening requires at least one positive bonus.");
                    }
                    break;

                case EffectType.StrengthenKing:
                    if (effect.kingDamageBonusPercent <= 0f &&
                        effect.kingAttackSpeedBonusPercent <= 0f &&
                        effect.kingSkillPowerBonusPercent <= 0f)
                    {
                        AddError(issues, "augment.strengthened_king_empty", effectPath, "King strengthening requires at least one positive bonus.");
                    }
                    break;

                case EffectType.IncreaseMyUnitAttack:
                case EffectType.IncreaseMyUnitAttackSpeed:
                case EffectType.AddGold:
                case EffectType.AddWallPlacementCount:
                case EffectType.IncreaseEnemyHealth:
                case EffectType.IncreaseEnemyMoveSpeed:
                case EffectType.IncreaseBlackMagicMaximum:
                case EffectType.GrantPermanentWallPlacementCount:
                    if (effect.value <= 0f)
                    {
                        AddError(issues, "augment.effect_value_invalid", effectPath, $"{effect.effectType} requires a positive value.");
                    }
                    break;
            }
        }

        // Owned bosses are represented as AugmentData entries in the current snapshot schema.
        // Multiple boss payloads in one augment would be ambiguous when one copy is consumed.
        if (bossEffectCount > 1)
        {
            AddError(
                issues,
                "augment.multiple_boss_effects_unsupported",
                path,
                "An augment may contain at most one boss summon effect until owned-boss snapshots identify effects separately.");
        }
    }

    public static bool IsTargetAllowed(EffectType effectType, TargetType targetType)
    {
        switch (effectType)
        {
            case EffectType.IncreaseEnemyHealth:
            case EffectType.IncreaseEnemyMoveSpeed:
            case EffectType.SpawnMonsterOnEnemyField:
                return targetType == TargetType.Opponent;

            case EffectType.IncreaseMyUnitAttack:
            case EffectType.IncreaseMyUnitAttackSpeed:
            case EffectType.AddGold:
            case EffectType.AddWallPlacementCount:
            case EffectType.GrantMagicScroll:
            case EffectType.StrengthenMonsterType:
            case EffectType.IncreaseBlackMagicMaximum:
            case EffectType.GrantPermanentWallPlacementCount:
            case EffectType.StrengthenKing:
                return targetType == TargetType.Player;

            default:
                return false;
        }
    }

    private static void AddError(
        ICollection<ContentValidationIssue> issues,
        string code,
        string path,
        string message)
    {
        issues.Add(new ContentValidationIssue(ContentValidationSeverity.Error, code, path, message));
    }
}
#endif
