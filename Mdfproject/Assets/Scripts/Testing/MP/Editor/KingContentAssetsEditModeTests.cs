#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.Animations;
using UnityEngine;
using NUnitAssert = NUnit.Framework.Assert;

public sealed class KingContentAssetsEditModeTests
{
    private const string UnitsRoot = "Assets/GameData/Units";

    [Test]
    public void EveryCatalogEntryHasCompleteAddressableKingDefinition()
    {
        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        NUnitAssert.That(settings, Is.Not.Null);

        foreach (KingSelectionCatalog.Entry entry in KingSelectionCatalog.Entries)
        {
            string path = $"{UnitsRoot}/{entry.KingUnitKey}.asset";
            KingUnitData king = AssetDatabase.LoadAssetAtPath<KingUnitData>(path);
            NUnitAssert.That(king, Is.Not.Null, path);
            NUnitAssert.That(king.baseUnitData, Is.Not.Null, $"{entry.KingUnitKey}.baseUnitData");
            NUnitAssert.That(king.baseUnitData.name, Is.EqualTo(entry.BaseUnitKey));
            NUnitAssert.That(king.kingBuff, Is.Not.Null, $"{entry.KingUnitKey}.kingBuff");
            NUnitAssert.That(king.kingSkill, Is.Not.Null, $"{entry.KingUnitKey}.kingSkill");
            NUnitAssert.That(king.presentationScale, Is.EqualTo(1.3f).Within(0.001f));
            NUnitAssert.That(king.attackDamageGrowthPerRound, Is.EqualTo(0.12f).Within(0.001f));
            NUnitAssert.That(king.attackSpeedGrowthPerRound, Is.EqualTo(0.04f).Within(0.001f));

            string guid = AssetDatabase.AssetPathToGUID(path);
            AddressableAssetEntry addressable = settings.FindAssetEntry(guid);
            NUnitAssert.That(addressable, Is.Not.Null, $"Addressables entry: {entry.KingUnitKey}");
            NUnitAssert.That(addressable.address, Is.EqualTo(entry.KingUnitKey));
            NUnitAssert.That(addressable.labels, Does.Contain("KingUnitData"));
            NUnitAssert.That(addressable.labels, Does.Contain("mdf-gameplay-data"));
        }
    }

    [Test]
    public void EveryKingBasePrefabSupportsCameraFacingAndHeadLookPresentation()
    {
        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        NUnitAssert.That(settings, Is.Not.Null);
        NUnitAssert.That(KingSelectionCatalog.Entries.Count, Is.EqualTo(7));

        var uniquePrefabPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (KingSelectionCatalog.Entry entry in KingSelectionCatalog.Entries)
        {
            KingUnitData king = AssetDatabase.LoadAssetAtPath<KingUnitData>(
                $"{UnitsRoot}/{entry.KingUnitKey}.asset");
            NUnitAssert.That(king, Is.Not.Null, entry.KingUnitKey);
            NUnitAssert.That(king.baseUnitData, Is.Not.Null, $"{entry.KingUnitKey}.baseUnitData");
            NUnitAssert.That(king.baseUnitData.prefabsByStarLevel, Is.Not.Null.And.Not.Empty,
                $"{entry.KingUnitKey}.baseUnitData.prefabsByStarLevel");

            string prefabAddress = king.baseUnitData.prefabsByStarLevel[0];
            AddressableAssetEntry prefabEntry = FindAddressableEntryByAddress(settings, prefabAddress);
            NUnitAssert.That(prefabEntry, Is.Not.Null,
                $"{entry.KingUnitKey} base prefab Addressable: {prefabAddress}");

            string prefabPath = AssetDatabase.GUIDToAssetPath(prefabEntry.guid);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            NUnitAssert.That(prefab, Is.Not.Null, $"{prefabAddress} -> {prefabPath}");
            uniquePrefabPaths.Add(prefabPath);

            UnitOrientationFixer orientation = prefab.GetComponentInChildren<UnitOrientationFixer>(true);
            NUnitAssert.That(orientation, Is.Not.Null,
                $"{entry.KingUnitKey}: {prefabAddress} has no UnitOrientationFixer");
            NUnitAssert.That(orientation.enabled, Is.True,
                $"{entry.KingUnitKey}: UnitOrientationFixer is disabled");
            NUnitAssert.That(IsActiveInPrefabHierarchy(orientation.transform, prefab.transform), Is.True,
                $"{entry.KingUnitKey}: UnitOrientationFixer is under an inactive object");
            NUnitAssert.That(orientation.faceCameraOnSpawn, Is.True,
                $"{entry.KingUnitKey}: initial camera-facing is disabled");
            NUnitAssert.That(orientation.faceCameraEveryFrame, Is.True,
                $"{entry.KingUnitKey}: continuous camera-facing is disabled");

            HeadLookController headLook = prefab.GetComponentInChildren<HeadLookController>(true);
            NUnitAssert.That(headLook, Is.Not.Null,
                $"{entry.KingUnitKey}: {prefabAddress} has no HeadLookController");
            NUnitAssert.That(headLook.enabled, Is.True,
                $"{entry.KingUnitKey}: HeadLookController is disabled");
            NUnitAssert.That(IsActiveInPrefabHierarchy(headLook.transform, prefab.transform), Is.True,
                $"{entry.KingUnitKey}: HeadLookController is under an inactive object");

            Animator animator = headLook.GetComponent<Animator>();
            NUnitAssert.That(animator, Is.Not.Null,
                $"{entry.KingUnitKey}: HeadLookController must share its object with Animator");
            NUnitAssert.That(animator.enabled, Is.True,
                $"{entry.KingUnitKey}: Animator is disabled");
            NUnitAssert.That(animator.avatar, Is.Not.Null,
                $"{entry.KingUnitKey}: Animator has no Avatar");
            NUnitAssert.That(animator.avatar.isValid, Is.True,
                $"{entry.KingUnitKey}: Avatar is invalid");
            NUnitAssert.That(animator.avatar.isHuman, Is.True,
                $"{entry.KingUnitKey}: Avatar is not Humanoid");

            HumanBone headBone = animator.avatar.humanDescription.human
                .FirstOrDefault(bone => bone.humanName == "Head");
            NUnitAssert.That(headBone.boneName, Is.Not.Null.And.Not.Empty,
                $"{entry.KingUnitKey}: Humanoid Avatar has no Head mapping");
            NUnitAssert.That(
                prefab.GetComponentsInChildren<Transform>(true)
                    .Any(candidate => candidate.name == headBone.boneName),
                Is.True,
                $"{entry.KingUnitKey}: mapped Head transform '{headBone.boneName}' is absent from the prefab");

            AnimatorController controller = ResolveAnimatorController(animator.runtimeAnimatorController);
            NUnitAssert.That(controller, Is.Not.Null,
                $"{entry.KingUnitKey}: Animator Controller is missing or unsupported");
            NUnitAssert.That(controller.layers.Any(layer => layer.iKPass), Is.True,
                $"{entry.KingUnitKey}: Animator Controller must enable IK Pass for HeadLookController");
        }

        NUnitAssert.That(uniquePrefabPaths, Has.Count.EqualTo(6),
            "Seven King choices currently resolve to six unique base prefabs (Mage and Pyromancer share one).");
    }

    [Test]
    public void MageFamilyKingsRelaxOnlyTheirKingPresentationHeadLookClamp()
    {
        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        NUnitAssert.That(settings, Is.Not.Null);
        string[] correctedKingKeys =
        {
            "UnitData_King_Mage",
            "UnitData_King_Pyromancer"
        };
        foreach (string key in correctedKingKeys)
        {
            KingUnitData corrected = AssetDatabase.LoadAssetAtPath<KingUnitData>(
                $"{UnitsRoot}/{key}.asset");
            NUnitAssert.That(corrected, Is.Not.Null, key);
            NUnitAssert.That(corrected.overridePresentationHeadLook, Is.True, key);
            NUnitAssert.That(corrected.baseUnitData, Is.Not.Null, key);
            NUnitAssert.That(corrected.baseUnitData.prefabsByStarLevel, Is.Not.Null.And.Not.Empty, key);

            AddressableAssetEntry prefabEntry = FindAddressableEntryByAddress(
                settings,
                corrected.baseUnitData.prefabsByStarLevel[0]);
            NUnitAssert.That(prefabEntry, Is.Not.Null, key);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                AssetDatabase.GUIDToAssetPath(prefabEntry.guid));
            NUnitAssert.That(prefab, Is.Not.Null, key);
            HeadLookController sourceHeadLook = prefab.GetComponentInChildren<HeadLookController>(true);
            NUnitAssert.That(sourceHeadLook, Is.Not.Null, key);

            NUnitAssert.That(
                corrected.presentationHeadLookWeight,
                Is.EqualTo(sourceHeadLook.lookAtWeight).Within(0.0001f),
                key);
            NUnitAssert.That(
                corrected.presentationHeadLookTiltAngle,
                Is.EqualTo(sourceHeadLook.tiltAngle).Within(0.0001f),
                key);
            NUnitAssert.That(corrected.presentationHeadLookBodyWeight,
                Is.EqualTo(0.2f).Within(0.0001f), key);
            NUnitAssert.That(corrected.presentationHeadLookHeadWeight,
                Is.EqualTo(1f).Within(0.0001f), key);
            NUnitAssert.That(corrected.presentationHeadLookClampWeight,
                Is.LessThan(sourceHeadLook.lookAtClampWeight), key);
        }

        foreach (KingSelectionCatalog.Entry entry in KingSelectionCatalog.Entries)
        {
            if (correctedKingKeys.Contains(entry.KingUnitKey))
            {
                continue;
            }

            KingUnitData unaffected = AssetDatabase.LoadAssetAtPath<KingUnitData>(
                $"{UnitsRoot}/{entry.KingUnitKey}.asset");
            NUnitAssert.That(unaffected.overridePresentationHeadLook, Is.False,
                $"{entry.KingUnitKey} should retain its working base prefab pose.");
        }
    }

    [Test]
    public void KingBuffsAndSkillsAreDistinctAndDataDriven()
    {
        var buffSignatures = new HashSet<string>(StringComparer.Ordinal);
        var skillNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (KingSelectionCatalog.Entry entry in KingSelectionCatalog.Entries)
        {
            KingUnitData king = AssetDatabase.LoadAssetAtPath<KingUnitData>(
                $"{UnitsRoot}/{entry.KingUnitKey}.asset");
            NUnitAssert.That(king, Is.Not.Null);
            NUnitAssert.That(king.kingBuff.modifiers, Is.Not.Null.And.Not.Empty);
            NUnitAssert.That(king.kingSkill.skillName, Is.Not.Null.And.Not.Empty);
            NUnitAssert.That(king.kingSkill.description, Is.Not.Null.And.Not.Empty);
            NUnitAssert.That(king.kingSkill.iconKey, Is.EqualTo(entry.IconKey));

            string signature = string.Join(
                ";",
                king.kingBuff.modifiers
                    .OrderBy(modifier => modifier.stat)
                    .ThenBy(modifier => modifier.mode)
                    .Select(modifier => $"{modifier.stat}:{modifier.mode}:{modifier.value:F3}"));
            NUnitAssert.That(buffSignatures.Add(signature), Is.True,
                $"Each base type must have its own buff recipe: {entry.KingUnitKey}");
            NUnitAssert.That(skillNames.Add(king.kingSkill.skillName), Is.True,
                $"Each king must have a distinct skill name: {entry.KingUnitKey}");
        }
    }

    [Test]
    public void InitialKingSkillsDoNotConsumePerMonsterPersistentStatusSlots()
    {
        foreach (KingSelectionCatalog.Entry entry in KingSelectionCatalog.Entries)
        {
            KingUnitData king = AssetDatabase.LoadAssetAtPath<KingUnitData>(
                $"{UnitsRoot}/{entry.KingUnitKey}.asset");
            NUnitAssert.That(king, Is.Not.Null);
            NUnitAssert.That(king.kingSkill, Is.Not.Null);
            NUnitAssert.That(king.kingSkill.statusEffect, Is.EqualTo(StatusEffectType.None),
                $"Initial whole-field King skills must stay instant instead of allocating one global scheduler slot per monster: {entry.KingUnitKey}");
            NUnitAssert.That(king.kingSkill.statusDuration, Is.Zero,
                $"Instant King skill has stale status duration: {entry.KingUnitKey}");
            NUnitAssert.That(king.kingSkill.HasBoundedStatusEffect, Is.False);
        }
    }

    [Test]
    public void RepeatableKingStrengtheningAugmentsAreCompleteAndAddressable()
    {
        string[] names =
        {
            "Aug_King_Training_Silver",
            "Aug_King_Armory_Gold",
            "Aug_King_DivineCrown_Prismatic"
        };
        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;

        foreach (string name in names)
        {
            string path = $"Assets/GameData/Augments/{name}.asset";
            AugmentData augment = AssetDatabase.LoadAssetAtPath<AugmentData>(path);
            NUnitAssert.That(augment, Is.Not.Null, path);
            NUnitAssert.That(augment.effectType, Is.EqualTo(EffectType.StrengthenKing));
            NUnitAssert.That(augment.targetType, Is.EqualTo(TargetType.Player));
            NUnitAssert.That(augment.kingDamageBonusPercent, Is.GreaterThan(0f));
            NUnitAssert.That(augment.kingAttackSpeedBonusPercent, Is.GreaterThan(0f));
            NUnitAssert.That(augment.kingSkillPowerBonusPercent, Is.GreaterThan(0f));
            NUnitAssert.That(augment.icon, Is.Not.Null);

            AddressableAssetEntry addressable = settings.FindAssetEntry(AssetDatabase.AssetPathToGUID(path));
            NUnitAssert.That(addressable, Is.Not.Null);
            NUnitAssert.That(addressable.address, Is.EqualTo(name));
            NUnitAssert.That(addressable.labels, Does.Contain("Augment"));
        }
    }

    private static AddressableAssetEntry FindAddressableEntryByAddress(
        AddressableAssetSettings settings,
        string address)
    {
        return settings.groups
            .Where(group => group != null)
            .SelectMany(group => group.entries)
            .SingleOrDefault(entry => entry.address == address);
    }

    private static bool IsActiveInPrefabHierarchy(Transform candidate, Transform prefabRoot)
    {
        for (Transform current = candidate; current != null; current = current.parent)
        {
            if (!current.gameObject.activeSelf)
            {
                return false;
            }

            if (current == prefabRoot)
            {
                return true;
            }
        }

        return false;
    }

    private static AnimatorController ResolveAnimatorController(RuntimeAnimatorController runtimeController)
    {
        while (runtimeController is AnimatorOverrideController overrideController)
        {
            runtimeController = overrideController.runtimeAnimatorController;
        }

        return runtimeController as AnimatorController;
    }
}
#endif
