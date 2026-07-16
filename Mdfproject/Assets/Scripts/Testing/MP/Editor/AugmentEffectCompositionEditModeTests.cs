#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public sealed class AugmentEffectCompositionEditModeTests
{
    private static readonly FieldInfo EffectsField = typeof(AugmentData).GetField(
        "effects",
        BindingFlags.Instance | BindingFlags.NonPublic);

    [Test]
    public void LegacyAsset_ExposesOneVirtualEffectWithoutChangingAuthoredData()
    {
        AugmentData augment = ScriptableObject.CreateInstance<AugmentData>();
        MonsterData monster = ScriptableObject.CreateInstance<MonsterData>();
        try
        {
            augment.targetType = TargetType.Player;
            augment.effectType = EffectType.StrengthenMonsterType;
            augment.value = 17f;
            augment.strengthenedMonsterData = monster;
            augment.monsterHealthBonusPercent = 0.25f;

            Assert.That(augment.HasAuthoredEffects, Is.False);
            Assert.That(augment.EffectCount, Is.EqualTo(1));

            AugmentEffectData effect = augment.GetEffect(0);
            Assert.That(effect.targetType, Is.EqualTo(TargetType.Player));
            Assert.That(effect.effectType, Is.EqualTo(EffectType.StrengthenMonsterType));
            Assert.That(effect.value, Is.EqualTo(17f));
            Assert.That(effect.strengthenedMonsterData, Is.SameAs(monster));
            Assert.That(effect.monsterHealthBonusPercent, Is.EqualTo(0.25f));
            Assert.That(augment.AuthoredEffects, Is.Empty);

            // The fallback is refreshed, so old runtime-created assets remain correct after mutation.
            augment.monsterHealthBonusPercent = 0.5f;
            Assert.That(augment.GetEffect(0).monsterHealthBonusPercent, Is.EqualTo(0.5f));
        }
        finally
        {
            Object.DestroyImmediate(augment);
            Object.DestroyImmediate(monster);
        }
    }

    [Test]
    public void AuthoredEffects_AreCanonicalOrderedAndCanComposeDifferentTypes()
    {
        AugmentData augment = ScriptableObject.CreateInstance<AugmentData>();
        MagicScrollData scroll = ScriptableObject.CreateInstance<MagicScrollData>();
        try
        {
            augment.effectType = EffectType.AddGold;
            augment.value = 999f;
            SetEffects(
                augment,
                new AugmentEffectData
                {
                    targetType = TargetType.Player,
                    effectType = EffectType.AddGold,
                    value = 5f
                },
                new AugmentEffectData
                {
                    targetType = TargetType.Player,
                    effectType = EffectType.GrantMagicScroll,
                    magicScrollData = scroll
                });

            Assert.That(augment.HasAuthoredEffects, Is.True);
            Assert.That(augment.EffectCount, Is.EqualTo(2));
            Assert.That(augment.GetEffect(0).value, Is.EqualTo(5f), "canonical data must override legacy fields");
            Assert.That(augment.GetEffect(1).targetType, Is.EqualTo(TargetType.Player));
            Assert.That(augment.ContainsEffect(EffectType.AddGold), Is.True);
            Assert.That(augment.TryGetMagicScroll(out MagicScrollData resolved), Is.True);
            Assert.That(resolved, Is.SameAs(scroll));
        }
        finally
        {
            Object.DestroyImmediate(augment);
            Object.DestroyImmediate(scroll);
        }
    }

    [Test]
    public void Validator_RejectsTargetsThatDoNotMatchEffectSemantics()
    {
        AugmentData augment = ScriptableObject.CreateInstance<AugmentData>();
        try
        {
            SetEffects(
                augment,
                new AugmentEffectData
                {
                    effectType = EffectType.AddGold,
                    targetType = TargetType.Opponent,
                    value = 5f
                },
                new AugmentEffectData
                {
                    effectType = EffectType.IncreaseEnemyHealth,
                    targetType = TargetType.Player,
                    value = 10f
                });

            var issues = new List<ContentValidationIssue>();
            AugmentEffectCompositionValidator.Validate(augment, issues);

            Assert.That(issues.Count(issue => issue.Code == "augment.effect_target_invalid"), Is.EqualTo(2));
            Assert.That(AugmentEffectCompositionValidator.IsTargetAllowed(
                EffectType.StrengthenKing,
                TargetType.Player), Is.True);
            Assert.That(AugmentEffectCompositionValidator.IsTargetAllowed(
                EffectType.SpawnMonsterOnEnemyField,
                TargetType.Opponent), Is.True);
        }
        finally
        {
            Object.DestroyImmediate(augment);
        }
    }

    [Test]
    public void Validator_RejectsMissingTypedPayloadAndUnsupportedMultipleBosses()
    {
        AugmentData augment = ScriptableObject.CreateInstance<AugmentData>();
        try
        {
            SetEffects(
                augment,
                new AugmentEffectData
                {
                    effectType = EffectType.GrantMagicScroll,
                    targetType = TargetType.Player
                },
                new AugmentEffectData
                {
                    effectType = EffectType.SpawnMonsterOnEnemyField,
                    targetType = TargetType.Opponent,
                    isBossSummon = true
                },
                new AugmentEffectData
                {
                    effectType = EffectType.SpawnMonsterOnEnemyField,
                    targetType = TargetType.Opponent,
                    isBossSummon = true
                });

            var issues = new List<ContentValidationIssue>();
            AugmentEffectCompositionValidator.Validate(augment, issues);
            string[] codes = issues.Select(issue => issue.Code).ToArray();

            Assert.That(codes, Does.Contain("augment.scroll_missing"));
            Assert.That(codes.Count(code => code == "augment.boss_missing"), Is.EqualTo(2));
            Assert.That(codes, Does.Contain("augment.multiple_boss_effects_unsupported"));
        }
        finally
        {
            Object.DestroyImmediate(augment);
        }
    }

    private static void SetEffects(AugmentData augment, params AugmentEffectData[] effects)
    {
        Assert.That(EffectsField, Is.Not.Null);
        EffectsField.SetValue(augment, new List<AugmentEffectData>(effects));
    }
}
#endif
