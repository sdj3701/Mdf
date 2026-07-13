#if UNITY_EDITOR
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;

public sealed class KingSkillCommandEditModeTests
{
    [Test]
    public void CommandTypeAppendsKingSkillWithoutRenumberingExistingCommands()
    {
        Assert.That((int)CommandType.SetSkillActivationMode, Is.EqualTo(14));
        Assert.That((int)CommandType.ActivateKingSkill, Is.EqualTo(15));
    }

    [Test]
    public void CommandProcessorSerializesAndDeserializesKingSkillCommand()
    {
        Assert.That(
            MdfCompiledCodePolicy.ContainsIsInstanceOf(typeof(CommandProcessor), typeof(ActivateKingSkillCommand)),
            Is.True,
            "CommandProcessor must serialize ActivateKingSkillCommand through the shared command envelope.");
        Assert.That(
            MdfCompiledCodePolicy.ReferencesMethod(typeof(CommandProcessor), typeof(ActivateKingSkillCommand), ".ctor"),
            Is.True,
            "CommandProcessor must deserialize the envelope back into ActivateKingSkillCommand.");
    }

    [Test]
    public void KingSkillCommandDelegatesGameplayMutationToPlayerManager()
    {
        Assert.That(
            MdfCompiledCodePolicy.ReferencesMethod(
                typeof(ActivateKingSkillCommand),
                typeof(PlayerManager),
                nameof(PlayerManager.TryActivateKingSkillAuthoritative)),
            Is.True);
    }

    [Test]
    public void ClientRequestValidatorChecksAuthoritativeKingSkillState()
    {
        MethodInfo validator = typeof(PlayerCommandRequestValidator).GetMethod(
            "ValidateActivateKingSkillRequest",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.That(validator, Is.Not.Null);
        Assert.That(
            MdfCompiledCodePolicy.ReferencesMethod(validator, typeof(PlayerManager), "get_CanUseKingSkill"),
            Is.True);
        Assert.That(
            MdfCompiledCodePolicy.ReferencesMethod(validator, typeof(PlayerManager), "get_KingSkillUsedThisDefense"),
            Is.True);
        Assert.That(
            MdfCompiledCodePolicy.ReferencesMethod(validator, typeof(PlayerManager), "get_IsAttackerInCurrentBattle"),
            Is.True);
    }

    [Test]
    public void DefenderHudContainsDedicatedKingSkillControlsAndUsesCommandPath()
    {
        var layout = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
            "Assets/Resources/UI/GamePrepare/GamePreparePanels.uxml");
        Assert.That(layout, Is.Not.Null);

        var root = new VisualElement();
        layout.CloneTree(root);
        Assert.That(root.Q<VisualElement>("game-king-skill-button"), Is.Not.Null);
        Assert.That(root.Q<Image>("game-king-skill-icon"), Is.Not.Null);
        Assert.That(root.Q<Label>("game-king-skill-name"), Is.Not.Null);
        Assert.That(root.Q<Label>("game-king-skill-description"), Is.Not.Null);
        Assert.That(root.Q<Label>("game-king-skill-state"), Is.Not.Null);

        Assert.That(
            MdfCompiledCodePolicy.ReferencesMethod(
                typeof(GamePrepareUIToolkitController),
                typeof(ActivateKingSkillCommand),
                ".ctor"),
            Is.True);
        Assert.That(
            MdfCompiledCodePolicy.ReferencesMethod(
                typeof(GamePrepareUIToolkitController),
                typeof(PlayerManager),
                "get_CanUseKingSkill"),
            Is.True);
        Assert.That(
            MdfCompiledCodePolicy.ReferencesMethod(
                typeof(GamePrepareUIToolkitController),
                typeof(PlayerManager),
                nameof(PlayerManager.TryActivateKingSkillAuthoritative)),
            Is.False,
            "The defender HUD may request the command, but must not mutate king gameplay directly.");
    }

    [Test]
    public void StrengthenKingAugmentUsesCumulativeAuthorityApi()
    {
        Assert.That(
            (int)EffectType.StrengthenKing,
            Is.GreaterThan((int)EffectType.GrantPermanentWallPlacementCount));
        Assert.That(
            MdfCompiledCodePolicy.ReferencesMethod(
                typeof(AugmentManager),
                typeof(PlayerManager),
                nameof(PlayerManager.ApplyKingAugment)),
            Is.True);
        Assert.That(typeof(AugmentData).GetField(nameof(AugmentData.kingDamageBonusPercent)), Is.Not.Null);
        Assert.That(typeof(AugmentData).GetField(nameof(AugmentData.kingAttackSpeedBonusPercent)), Is.Not.Null);
        Assert.That(typeof(AugmentData).GetField(nameof(AugmentData.kingSkillPowerBonusPercent)), Is.Not.Null);
    }
}
#endif
