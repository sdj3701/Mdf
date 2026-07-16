using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class KingAttackPresentationEditModeTests
{
    private const string UnitsRoot = "Assets/GameData/Units";

    [Test]
    public void KingUnitDataKeepsFastBaseSpeedAndRaisesOnlySluggishBases()
    {
        UnitData unit = ScriptableObject.CreateInstance<UnitData>();
        KingUnitData king = ScriptableObject.CreateInstance<KingUnitData>();
        try
        {
            king.baseUnitData = unit;
            king.minimumAttackSpeed = 1f;
            king.baseAttackSpeedMultiplier = 1f;
            king.attackSpeedGrowthPerRound = 0f;

            unit.attackSpeed = 0.7f;
            Assert.That(king.ResolveAttackSpeed(1), Is.EqualTo(1f).Within(0.001f));

            unit.attackSpeed = 1.8f;
            Assert.That(king.ResolveAttackSpeed(1), Is.EqualTo(1.8f).Within(0.001f));

            king.baseAttackSpeedMultiplier = 0.25f;
            Assert.That(king.ResolveAttackSpeed(1), Is.EqualTo(1f).Within(0.001f),
                "authored multipliers must not push the final King cadence below its responsive floor");
        }
        finally
        {
            Object.DestroyImmediate(king);
            Object.DestroyImmediate(unit);
        }
    }

    [Test]
    public void KingAttackPresentationReusesBaseVfxWithoutAddingASecondDamagePath()
    {
        const BindingFlags instanceMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        MethodInfo playKingVfx = typeof(PlayerManager).GetMethod("PlayKingBaseAttackVfx", instanceMembers);
        Assert.That(playKingVfx, Is.Not.Null);
        Assert.That(
            MdfCompiledCodePolicy.ReferencesMethod(
                playKingVfx,
                typeof(ProjectileVfxManager),
                nameof(ProjectileVfxManager.PlayFromKingAttack)),
            Is.True,
            "Ranged Kings must resolve the base UnitData projectile profile through the shared presenter.");
        Assert.That(
            MdfCompiledCodePolicy.ReferencesMethod(
                playKingVfx,
                typeof(UnitAttackVfxPresenter),
                nameof(UnitAttackVfxPresenter.PlayBasicAttack)),
            Is.True,
            "Melee Kings must resolve the base UnitData slash profile through the shared presenter.");
        Assert.That(
            MdfCompiledCodePolicy.ReferencesMethod(
                playKingVfx,
                typeof(LocalVfxVisibility),
                nameof(LocalVfxVisibility.ShouldPlay)),
            Is.True,
            "Off-field King effects must obey the same local visibility policy as ordinary attacks.");

        MethodInfo genericMeleePresentation = typeof(UnitAttackVfxPresenter).GetMethod(
            nameof(UnitAttackVfxPresenter.PlayBasicAttack),
            instanceMembers,
            null,
            new[] { typeof(UnitData), typeof(int), typeof(Transform), typeof(Vector3), typeof(float) },
            null);
        Assert.That(genericMeleePresentation, Is.Not.Null);
        Assert.That(
            typeof(CombatScheduler.ProjectileEventData).GetField(
                nameof(CombatScheduler.ProjectileEventData.VfxConfigOverride)),
            Is.Not.Null,
            "A King projectile must carry the already-resolved base profile only inside the local presentation event.");

        string kingSource = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/Managers/PlayerManager.King.cs");
        Assert.That(kingSource, Does.Contain("TryScheduleDirectHitAtTick("),
            "A ranged King must enqueue exactly one authority-owned hit at the presentation impact tick.");
        Assert.That(kingSource, Does.Not.Contain("scheduler.ScheduleHit("),
            "King presentation must not ask the generic scheduler to publish a duplicate projectile event.");
    }

    [Test]
    public void KingCapturesTargetBeforeDamageAndCarriesIdentityToTheProjectilePresenter()
    {
        string kingSource = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/Managers/PlayerManager.King.cs");
        int capturePosition = kingSource.IndexOf(
            "Vector3 targetPosition = target.transform.position;",
            System.StringComparison.Ordinal);
        int directSchedule = kingSource.IndexOf(
            "damageScheduled = scheduler.TryScheduleDirectHitAtTick(",
            System.StringComparison.Ordinal);
        int immediateFallback = kingSource.IndexOf(
            "target.TakeDamage(damage, baseUnitData.damageType);",
            System.StringComparison.Ordinal);

        Assert.That(capturePosition, Is.GreaterThanOrEqualTo(0));
        Assert.That(directSchedule, Is.GreaterThan(capturePosition));
        Assert.That(immediateFallback, Is.GreaterThan(capturePosition));
        Assert.That(kingSource, Does.Contain("KingAttackPresentationTargetId = targetId;"));

        string projectileSource = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/VFX/ProjectileVfxManager.cs");
        Assert.That(projectileSource, Does.Contain("Target = target,"));
        Assert.That(projectileSource, Does.Not.Contain("Target = null,"),
            "The King projectile must retain a live target when the captured NetworkId still resolves.");
    }

    [Test]
    public void KingProjectileUsesAuthoredFireTimingAndAuthorityImpactTick()
    {
        string kingSource = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/Managers/PlayerManager.King.cs");
        Assert.That(kingSource, Does.Contain("ResolveProjectileSpawnNormalizedTime()"));
        Assert.That(kingSource, Does.Contain("presentationFireTick = Runner.Tick + SecondsToKingTicksCeil(fireDelaySeconds);"));
        Assert.That(kingSource, Does.Contain("presentationHitTick = presentationFireTick + Mathf.Max("));
        Assert.That(kingSource, Does.Contain("KingAttackPresentationFireTick = presentationFireTick;"));
        Assert.That(kingSource, Does.Contain("KingAttackPresentationHitTick = presentationHitTick;"));

        string schedulerSource = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/Managers/CombatScheduler.cs");
        Assert.That(schedulerSource, Does.Contain("public bool TryScheduleDirectHitAtTick("));
        Assert.That(schedulerSource, Does.Contain("|| !Object.HasStateAuthority"));
        Assert.That(schedulerSource, Does.Contain("return EnqueuePendingHit(pendingHit);"));

        string projectileSource = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/VFX/ProjectileVfxManager.cs");
        Assert.That(projectileSource, Does.Contain("WaitForFireTickAsync(runner, evt.FireTick, lifecycleGeneration)"));
    }

    [Test]
    public void KingAnimatorAndProjectileLoadsAreLifecycleBound()
    {
        string kingSource = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/Managers/PlayerManager.King.cs");
        Assert.That(kingSource, Does.Contain("_kingAnimator.speed = ResolveKingAttackAnimationPlaybackSpeed();"));
        Assert.That(kingSource, Does.Contain("return Mathf.Max(0.01f, CurrentKingAttackSpeed);"),
            "The presentation animation rate must use the same attacks-per-second value as the authority timer.");
        Assert.That(kingSource, Does.Contain("ResetKingAnimatorSpeedWhenAttackAnimationFinishes"));
        Assert.That(kingSource, Does.Contain(
            "_lastObservedKingAttackPresentationSequence = KingAttackPresentationSequence;"));

        string projectileSource = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/VFX/ProjectileVfxManager.cs");
        Assert.That(projectileSource, Does.Contain("private int _lifecycleGeneration;"));
        Assert.That(projectileSource, Does.Contain("AdvanceLifecycleGeneration();"));
        Assert.That(projectileSource, Does.Contain("IsLifecycleCurrent(lifecycleGeneration)"));
    }

    [Test]
    public void ProjectilePresenterAcceptsTheKingBaseProfileOverride()
    {
        var managerObject = new GameObject("king-projectile-vfx-manager");
        var profile = ScriptableObject.CreateInstance<BasicAttackVfxProfile>();
        var unitData = ScriptableObject.CreateInstance<UnitData>();
        try
        {
            profile.projectileVfxConfig = ProjectileVfxConfig.CreateDefault("KingBaseProjectile");
            unitData.basicAttackVfxProfile = profile;
            var manager = managerObject.AddComponent<ProjectileVfxManager>();
            MethodInfo resolve = typeof(ProjectileVfxManager).GetMethod(
                "TryResolveProjectileVfx",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(resolve, Is.Not.Null);

            var evt = new CombatScheduler.ProjectileEventData
            {
                VfxConfigOverride = unitData.GetProjectileVfxConfig()
            };
            object[] args = { evt, null };
            Assert.That((bool)resolve.Invoke(manager, args), Is.True);
            Assert.That(args[1], Is.SameAs(profile.projectileVfxConfig));
        }
        finally
        {
            Object.DestroyImmediate(unitData);
            Object.DestroyImmediate(profile);
            Object.DestroyImmediate(managerObject);
        }
    }

    [Test]
    public void KingFirePointUsesTheMappedBasePrefabTransform()
    {
        const BindingFlags instanceMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var playerObject = new GameObject("king-fire-point-player");
        var firePointObject = new GameObject("BaseFirePointClone");
        try
        {
            PlayerManager player = playerObject.AddComponent<PlayerManager>();
            firePointObject.transform.position = new Vector3(3f, 4f, 5f);
            typeof(PlayerManager).GetField("_kingAttackFirePoint", instanceMembers)
                ?.SetValue(player, firePointObject.transform);
            MethodInfo resolve = typeof(PlayerManager).GetMethod("ResolveKingAttackFirePosition", instanceMembers);
            Assert.That(resolve, Is.Not.Null);
            Assert.That((Vector3)resolve.Invoke(player, null), Is.EqualTo(firePointObject.transform.position));
        }
        finally
        {
            Object.DestroyImmediate(firePointObject);
            Object.DestroyImmediate(playerObject);
        }
    }

    [Test]
    public void EveryKingInheritsBaseAttackVfxAndUsesResponsiveAttackSpeed()
    {
        foreach (KingSelectionCatalog.Entry entry in KingSelectionCatalog.Entries)
        {
            KingUnitData king = AssetDatabase.LoadAssetAtPath<KingUnitData>(
                $"{UnitsRoot}/{entry.KingUnitKey}.asset");
            Assert.That(king, Is.Not.Null, entry.KingUnitKey);
            Assert.That(king.baseUnitData, Is.Not.Null, entry.KingUnitKey);
            Assert.That(king.baseUnitData.basicAttackVfxProfile, Is.Not.Null,
                $"{entry.KingUnitKey} must inherit the base UnitData attack profile.");
            Assert.That(king.minimumAttackSpeed, Is.EqualTo(1f).Within(0.001f), entry.KingUnitKey);
            Assert.That(king.ResolveAttackSpeed(1), Is.GreaterThanOrEqualTo(1f), entry.KingUnitKey);

            if (king.baseUnitData.unitType == UnitType.Ranged)
            {
                ProjectileVfxConfig projectile = king.baseUnitData.GetProjectileVfxConfig();
                Assert.That(projectile, Is.Not.Null, $"{entry.KingUnitKey} projectile config");
                Assert.That(projectile.HasProjectileKey, Is.True, $"{entry.KingUnitKey} projectile key");
            }
            else
            {
                BasicAttackVfxConfig slash = king.baseUnitData.GetBasicAttackVfxConfig(1);
                Assert.That(slash, Is.Not.Null, $"{entry.KingUnitKey} slash config");
                Assert.That(slash.HasPrefabKey, Is.True, $"{entry.KingUnitKey} slash key");
            }
        }
    }

    [Test]
    public void KingUserFacingNamesOmitPossessiveKingPrefixes()
    {
        foreach (KingSelectionCatalog.Entry entry in KingSelectionCatalog.Entries)
        {
            KingUnitData king = AssetDatabase.LoadAssetAtPath<KingUnitData>(
                $"{UnitsRoot}/{entry.KingUnitKey}.asset");
            Assert.That(king, Is.Not.Null, entry.KingUnitKey);
            AssertUserFacingKingText(king.kingSkill.skillName, $"{entry.KingUnitKey}.skillName");
            AssertUserFacingKingText(king.kingSkill.description, $"{entry.KingUnitKey}.skillDescription");
            AssertUserFacingKingText(king.kingBuff.buffName, $"{entry.KingUnitKey}.buffName");
            AssertUserFacingKingText(king.kingBuff.description, $"{entry.KingUnitKey}.buffDescription");
        }

        string[] augmentKeys =
        {
            "Aug_King_Training_Silver",
            "Aug_King_Armory_Gold",
            "Aug_King_DivineCrown_Prismatic"
        };
        foreach (string augmentKey in augmentKeys)
        {
            AugmentData augment = AssetDatabase.LoadAssetAtPath<AugmentData>(
                $"Assets/GameData/Augments/{augmentKey}.asset");
            Assert.That(augment, Is.Not.Null, augmentKey);
            AssertUserFacingKingText(augment.augmentName, $"{augmentKey}.augmentName");
            AssertUserFacingKingText(augment.description, $"{augmentKey}.description");
        }
    }

    private static void AssertUserFacingKingText(string value, string context)
    {
        Assert.That(value, Is.Not.Null.And.Not.Empty, context);
        Assert.That(value, Does.Not.Contain("국왕의 "), context);
        Assert.That(value, Does.Not.Contain("왕의 "), context);
    }
}
