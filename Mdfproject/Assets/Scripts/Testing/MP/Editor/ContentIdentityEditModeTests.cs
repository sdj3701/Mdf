#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class ContentIdentityEditModeTests
{
    [Test]
    public void ProjectContentHasValidUniqueIdsNamesReferencesAndHashes()
    {
        ContentValidationIssue[] errors = ContentIdentityValidator.ValidateProjectContent()
            .Where(issue => issue.Severity == ContentValidationSeverity.Error)
            .ToArray();

        Assert.That(errors, Is.Empty, string.Join("\n", errors.Select(error => error.ToString())));
    }

    [Test]
    public void ScrollAugmentsHaveDistinctContentIdsDisplayNamesAndPayloads()
    {
        string[] paths =
        {
            "Assets/GameData/Augments/Aug_Scroll_Berserk.asset",
            "Assets/GameData/Augments/Aug_Scroll_BloodCurse.asset",
            "Assets/GameData/Augments/Aug_Scroll_Heal.asset",
            "Assets/GameData/Augments/Aug_Scroll_Stun.asset"
        };
        AugmentData[] augments = paths.Select(AssetDatabase.LoadAssetAtPath<AugmentData>).ToArray();

        Assert.That(augments, Has.All.Not.Null);
        Assert.That(augments.Select(augment => augment.ContentId).Distinct(StringComparer.Ordinal).Count(), Is.EqualTo(paths.Length));
        Assert.That(augments.Select(augment => augment.augmentName).Distinct(StringComparer.Ordinal).Count(), Is.EqualTo(paths.Length));
        Assert.That(augments.Select(augment => augment.ContentIdHash).Distinct().Count(), Is.EqualTo(paths.Length));
        Assert.That(augments.Select(augment => augment.magicScrollData).Distinct().Count(), Is.EqualTo(paths.Length));
        Assert.That(augments.All(augment =>
            augment.effectType == EffectType.GrantMagicScroll &&
            augment.magicScrollData != null &&
            augment.magicScrollData.skillData != null), Is.True);
    }

    [Test]
    public void ResolverUsesContentIdEvenWhenDisplayNamesMatch()
    {
        var first = ScriptableObject.CreateInstance<AugmentData>();
        var second = ScriptableObject.CreateInstance<AugmentData>();
        var go = new GameObject("ContentIdentityResolverTest");
        try
        {
            first.augmentName = "same display name";
            second.augmentName = "same display name";
            SetContentId(first, "augment.test.first");
            SetContentId(second, "augment.test.second");

            AugmentManager manager = go.AddComponent<AugmentManager>();
            FieldInfo silverField = typeof(AugmentManager).GetField(
                "silverAugments",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(silverField, Is.Not.Null);
            silverField.SetValue(manager, new List<AugmentData> { first, second });

            Assert.That(manager.FindAugmentByContentId(first.ContentId), Is.SameAs(first));
            Assert.That(manager.FindAugmentByContentId(second.ContentId), Is.SameAs(second));
            Assert.That(manager.FindAugmentByName("same display name"), Is.Null,
                "legacy display names must fail closed when they map to multiple ContentIds");

            silverField.SetValue(manager, new List<AugmentData> { first });
            Assert.That(manager.FindAugmentByName("same display name"), Is.SameAs(first),
                "a unique legacy display name remains compatible");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(go);
            UnityEngine.Object.DestroyImmediate(first);
            UnityEngine.Object.DestroyImmediate(second);
        }
    }

    [Test]
    public void ContentIdNormalizationIsCaseAndWhitespaceStable()
    {
        Assert.That(StableDataKeyUtility.NormalizeContentId("  Augment.Scroll.Heal  "), Is.EqualTo("augment.scroll.heal"));
        Assert.That(
            StableDataKeyUtility.StableContentIdHash(" AUGMENT.SCROLL.HEAL "),
            Is.EqualTo(StableDataKeyUtility.StableContentIdHash("augment.scroll.heal")));
    }

    [Test]
    public void ValidatorRejectsExactContentIdReuseAcrossDataTypes()
    {
        var augment = ScriptableObject.CreateInstance<AugmentData>();
        var skill = ScriptableObject.CreateInstance<SkillData>();
        try
        {
            SetContentId(augment, "content.shared.identity");
            SetContentId(skill, "content.shared.identity");
            var issues = new List<ContentValidationIssue>();
            MethodInfo validate = typeof(ContentIdentityValidator).GetMethod(
                "ValidateCrossTypeIdentities",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(validate, Is.Not.Null);

            validate.Invoke(null, new object[]
            {
                new[] { augment },
                new[] { skill },
                issues
            });

            Assert.That(issues.Any(issue => issue.Code == "content_id.cross_type_duplicate"), Is.True);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(augment);
            UnityEngine.Object.DestroyImmediate(skill);
        }
    }

    [Test]
    public void PlayerSnapshotUsesContentIdRejectsKnownAmbiguousLegacyNameAndResolvesUniqueAssetName()
    {
        AugmentData heal = AssetDatabase.LoadAssetAtPath<AugmentData>(
            "Assets/GameData/Augments/Aug_Scroll_Heal.asset");
        Assert.That(heal, Is.Not.Null);

        const BindingFlags StaticPrivate = BindingFlags.Static | BindingFlags.NonPublic;
        MethodInfo snapshotId = typeof(PlayerManager).GetMethod("StableAugmentSnapshotId", StaticPrivate);
        MethodInfo resolveId = typeof(PlayerManager).GetMethod("ResolveLoadedAugmentContentIdByStableId", StaticPrivate);
        Assert.That(snapshotId, Is.Not.Null);
        Assert.That(resolveId, Is.Not.Null);

        int contentHash = (int)snapshotId.Invoke(null, new object[] { heal.ContentId });
        Assert.That(contentHash, Is.EqualTo(heal.ContentIdHash));
        Assert.That(resolveId.Invoke(null, new object[] { contentHash }), Is.EqualTo(heal.ContentId));

        int ambiguousLegacyNameHash = StableDataKeyUtility.StableHash(heal.augmentName.Trim());
        Assert.That(resolveId.Invoke(null, new object[] { ambiguousLegacyNameHash }), Is.EqualTo(string.Empty));

        int uniqueLegacyAssetNameHash = StableDataKeyUtility.StableHash(heal.name.Trim());
        Assert.That(resolveId.Invoke(null, new object[] { uniqueLegacyAssetNameHash }), Is.EqualTo(heal.ContentId));
    }

    [Test]
    public void LegacyAugmentNotificationRpc_IsContentIdOnly()
    {
        MethodInfo rpc = typeof(GameManagers).GetMethod(
            "RPC_NotifyAugmentSelected",
            BindingFlags.Instance | BindingFlags.Public);

        Assert.That(rpc, Is.Not.Null);
        Assert.That(MdfCompiledCodePolicy.ReferencesMethod(
            rpc,
            typeof(StableDataKeyUtility),
            "NormalizeContentId"), Is.True);
        Assert.That(MdfCompiledCodePolicy.ReferencesField(
            rpc,
            typeof(AugmentData),
            "augmentName"), Is.False,
            "network entry points must not resolve ambiguous display names");
    }

    private static void SetContentId(UnityEngine.Object asset, string contentId)
    {
        var serialized = new SerializedObject(asset);
        SerializedProperty property = serialized.FindProperty("contentId");
        Assert.That(property, Is.Not.Null);
        property.stringValue = contentId;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }
}
#endif
