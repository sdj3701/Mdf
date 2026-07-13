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
    public void EveryKingPresentationClonePreservesBasePoseComponentsAndMappedReferences()
    {
        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        NUnitAssert.That(settings, Is.Not.Null);

        foreach (KingSelectionCatalog.Entry entry in KingSelectionCatalog.Entries)
        {
            string key = entry.KingUnitKey;
            KingUnitData king = AssetDatabase.LoadAssetAtPath<KingUnitData>(
                $"{UnitsRoot}/{key}.asset");
            NUnitAssert.That(king, Is.Not.Null, key);
            NUnitAssert.That(king.baseUnitData, Is.Not.Null, key);
            NUnitAssert.That(king.baseUnitData.prefabsByStarLevel, Is.Not.Null.And.Not.Empty, key);

            AddressableAssetEntry prefabEntry = FindAddressableEntryByAddress(
                settings,
                king.baseUnitData.prefabsByStarLevel[0]);
            NUnitAssert.That(prefabEntry, Is.Not.Null, key);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                AssetDatabase.GUIDToAssetPath(prefabEntry.guid));
            NUnitAssert.That(prefab, Is.Not.Null, key);

            HeadLookController sourceHeadLook = prefab.GetComponentInChildren<HeadLookController>(true);
            UnitOrientationFixer sourceOrientation = prefab.GetComponentInChildren<UnitOrientationFixer>(true);
            BodyScaler sourceBodyScaler = prefab.GetComponentInChildren<BodyScaler>(true);
            NUnitAssert.That(sourceHeadLook, Is.Not.Null, key);
            NUnitAssert.That(sourceOrientation, Is.Not.Null, key);
            NUnitAssert.That(sourceBodyScaler, Is.Not.Null, key);
            Animator sourceAnimator = sourceHeadLook.GetComponent<Animator>();
            NUnitAssert.That(sourceAnimator, Is.Not.Null, key);

            GameObject clone = KingVisualCloneUtility.CreateVisualOnly(
                prefab,
                null,
                out Animator primaryCloneAnimator,
                out Dictionary<Transform, Transform> transformMap);
            try
            {
                NUnitAssert.That(clone, Is.Not.Null, key);
                NUnitAssert.That(KingVisualCloneUtility.IsPresentationOnly(clone), Is.True, key);

                Animator cloneAnimator = GetMappedComponent<Animator>(
                    transformMap,
                    sourceAnimator.transform,
                    key);
                HeadLookController cloneHeadLook = GetMappedComponent<HeadLookController>(
                    transformMap,
                    sourceHeadLook.transform,
                    key);
                UnitOrientationFixer cloneOrientation = GetMappedComponent<UnitOrientationFixer>(
                    transformMap,
                    sourceOrientation.transform,
                    key);
                BodyScaler cloneBodyScaler = GetMappedComponent<BodyScaler>(
                    transformMap,
                    sourceBodyScaler.transform,
                    key);

                NUnitAssert.That(cloneHeadLook, Is.Not.Null, key);
                NUnitAssert.That(cloneAnimator, Is.Not.Null, key);
                NUnitAssert.That(cloneOrientation, Is.Not.Null, key);
                NUnitAssert.That(cloneBodyScaler, Is.Not.Null, key);
                NUnitAssert.That(primaryCloneAnimator, Is.SameAs(cloneAnimator), key);

                NUnitAssert.That(cloneAnimator.avatar, Is.SameAs(sourceAnimator.avatar), key);
                NUnitAssert.That(
                    cloneAnimator.runtimeAnimatorController,
                    Is.SameAs(sourceAnimator.runtimeAnimatorController),
                    key);
                NUnitAssert.That(cloneAnimator.applyRootMotion, Is.EqualTo(sourceAnimator.applyRootMotion), key);
                NUnitAssert.That(cloneAnimator.updateMode, Is.EqualTo(sourceAnimator.updateMode), key);
                NUnitAssert.That(cloneAnimator.cullingMode, Is.EqualTo(sourceAnimator.cullingMode), key);
                NUnitAssert.That(cloneAnimator.speed, Is.EqualTo(sourceAnimator.speed), key);
                NUnitAssert.That(cloneAnimator.fireEvents, Is.EqualTo(sourceAnimator.fireEvents), key);
                NUnitAssert.That(cloneAnimator.enabled, Is.EqualTo(sourceAnimator.enabled), key);

                NUnitAssert.That(cloneHeadLook.enabled, Is.EqualTo(sourceHeadLook.enabled), key);
                NUnitAssert.That(cloneHeadLook.lookAtWeight,
                    Is.EqualTo(sourceHeadLook.lookAtWeight).Within(0.0001f), key);
                NUnitAssert.That(cloneHeadLook.tiltAngle,
                    Is.EqualTo(sourceHeadLook.tiltAngle).Within(0.0001f), key);
                NUnitAssert.That(cloneHeadLook.lookAtBodyWeight,
                    Is.EqualTo(sourceHeadLook.lookAtBodyWeight).Within(0.0001f), key);
                NUnitAssert.That(cloneHeadLook.lookAtHeadWeight,
                    Is.EqualTo(sourceHeadLook.lookAtHeadWeight).Within(0.0001f), key);
                NUnitAssert.That(cloneHeadLook.lookAtClampWeight,
                    Is.EqualTo(sourceHeadLook.lookAtClampWeight).Within(0.0001f), key);

                NUnitAssert.That(
                    cloneOrientation.rigRoot,
                    Is.SameAs(GetMappedTransform(transformMap, sourceOrientation.rigRoot, key)),
                    $"{key}: UnitOrientationFixer.rigRoot must reference the cloned hierarchy.");
                NUnitAssert.That(cloneOrientation.rigRootName, Is.EqualTo(sourceOrientation.rigRootName), key);
                NUnitAssert.That(
                    cloneOrientation.rigLocalEulerTarget,
                    Is.EqualTo(sourceOrientation.rigLocalEulerTarget),
                    key);
                NUnitAssert.That(
                    cloneOrientation.faceCameraOnSpawn,
                    Is.EqualTo(sourceOrientation.faceCameraOnSpawn),
                    key);
                NUnitAssert.That(
                    cloneOrientation.faceCameraEveryFrame,
                    Is.EqualTo(sourceOrientation.faceCameraEveryFrame),
                    key);
                NUnitAssert.That(
                    cloneOrientation.enforceEveryLateUpdate,
                    Is.EqualTo(sourceOrientation.enforceEveryLateUpdate),
                    key);
                NUnitAssert.That(
                    cloneOrientation.targetCamera == sourceOrientation.targetCamera,
                    Is.True,
                    $"{key}: targetCamera must preserve Unity object/null reference semantics.");
                NUnitAssert.That(cloneOrientation.yawOffsetDeg,
                    Is.EqualTo(sourceOrientation.yawOffsetDeg).Within(0.0001f), key);
                NUnitAssert.That(cloneOrientation.enabled, Is.EqualTo(sourceOrientation.enabled), key);

                AssertMappedBodyScaler(sourceBodyScaler, cloneBodyScaler, transformMap, key);
            }
            finally
            {
                if (clone != null)
                {
                    UnityEngine.Object.DestroyImmediate(clone);
                }
            }
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

    private static T GetMappedComponent<T>(
        IReadOnlyDictionary<Transform, Transform> transformMap,
        Transform sourceTransform,
        string context)
        where T : Component
    {
        Transform mappedTransform = GetMappedTransform(transformMap, sourceTransform, context);
        return mappedTransform != null ? mappedTransform.GetComponent<T>() : null;
    }

    private static Transform GetMappedTransform(
        IReadOnlyDictionary<Transform, Transform> transformMap,
        Transform sourceTransform,
        string context)
    {
        if (sourceTransform == null)
        {
            return null;
        }

        NUnitAssert.That(
            transformMap.TryGetValue(sourceTransform, out Transform mappedTransform),
            Is.True,
            $"{context}: source transform '{sourceTransform.name}' is absent from the clone map.");
        NUnitAssert.That(mappedTransform, Is.Not.Null, context);
        NUnitAssert.That(mappedTransform, Is.Not.SameAs(sourceTransform), context);
        return mappedTransform;
    }

    private static void AssertMappedBodyScaler(
        BodyScaler source,
        BodyScaler clone,
        IReadOnlyDictionary<Transform, Transform> transformMap,
        string context)
    {
        NUnitAssert.That(clone.enabled, Is.EqualTo(source.enabled), context);
        NUnitAssert.That(clone.bodyWidth, Is.EqualTo(source.bodyWidth).Within(0.0001f), context);
        NUnitAssert.That(clone.bodyHeight, Is.EqualTo(source.bodyHeight).Within(0.0001f), context);
        NUnitAssert.That(clone.bodyDepth, Is.EqualTo(source.bodyDepth).Within(0.0001f), context);
        NUnitAssert.That(clone.headWidth, Is.EqualTo(source.headWidth).Within(0.0001f), context);
        NUnitAssert.That(clone.headHeight, Is.EqualTo(source.headHeight).Within(0.0001f), context);
        NUnitAssert.That(clone.headDepth, Is.EqualTo(source.headDepth).Within(0.0001f), context);
        NUnitAssert.That(
            clone.headBone,
            Is.SameAs(GetMappedTransform(transformMap, source.headBone, context)),
            $"{context}: BodyScaler.headBone must reference the cloned hierarchy.");

        if (source.bodyBones == null)
        {
            NUnitAssert.That(clone.bodyBones, Is.Null, context);
            return;
        }

        NUnitAssert.That(clone.bodyBones, Is.Not.Null, context);
        NUnitAssert.That(clone.bodyBones, Has.Length.EqualTo(source.bodyBones.Length), context);
        for (int i = 0; i < source.bodyBones.Length; i++)
        {
            NUnitAssert.That(
                clone.bodyBones[i],
                Is.SameAs(GetMappedTransform(transformMap, source.bodyBones[i], context)),
                $"{context}: BodyScaler.bodyBones[{i}] must reference the cloned hierarchy.");
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
