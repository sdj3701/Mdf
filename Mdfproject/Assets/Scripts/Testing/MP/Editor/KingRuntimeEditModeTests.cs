using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public class KingRuntimeEditModeTests
{
    [Test]
    public void KingBuffDataSumsMultipleModifiersByStatAndMode()
    {
        KingBuffData data = ScriptableObject.CreateInstance<KingBuffData>();
        try
        {
            data.modifiers = new[]
            {
                new KingBuffModifier
                {
                    stat = KingBuffStat.AttackDamage,
                    mode = KingBuffModifierMode.Percent,
                    value = 0.1f
                },
                new KingBuffModifier
                {
                    stat = KingBuffStat.AttackDamage,
                    mode = KingBuffModifierMode.Percent,
                    value = 0.05f
                },
                new KingBuffModifier
                {
                    stat = KingBuffStat.Defense,
                    mode = KingBuffModifierMode.Flat,
                    value = 3f
                }
            };

            Assert.That(
                data.GetModifier(KingBuffStat.AttackDamage, KingBuffModifierMode.Percent),
                Is.EqualTo(0.15f).Within(0.0001f));
            Assert.That(
                data.GetModifier(KingBuffStat.Defense, KingBuffModifierMode.Flat),
                Is.EqualTo(3f));
            Assert.That(
                data.GetModifier(KingBuffStat.MaxHealth, KingBuffModifierMode.Percent),
                Is.Zero);
        }
        finally
        {
            Object.DestroyImmediate(data);
        }
    }

    [Test]
    public void KingUnitDataAppliesRoundGrowthAndStackingAugmentBonus()
    {
        UnitData unit = ScriptableObject.CreateInstance<UnitData>();
        KingUnitData king = ScriptableObject.CreateInstance<KingUnitData>();
        try
        {
            unit.baseAttackDamage = 100f;
            unit.attackSpeed = 2f;
            king.baseUnitData = unit;
            king.baseAttackDamageMultiplier = 1.5f;
            king.baseAttackSpeedMultiplier = 1.25f;
            king.attackDamageGrowthPerRound = 0.1f;
            king.attackSpeedGrowthPerRound = 0.05f;

            Assert.That(king.ResolveAttackDamage(3, 0.2f), Is.EqualTo(210f).Within(0.001f));
            Assert.That(king.ResolveAttackSpeed(3, 0.15f), Is.EqualTo(3.125f).Within(0.001f));
        }
        finally
        {
            Object.DestroyImmediate(king);
            Object.DestroyImmediate(unit);
        }
    }

    [Test]
    public void KingHeadLookTuningChangesOnlyOptedInPresentationClone()
    {
        var gameObject = new GameObject("king-head-look-tuning-test");
        KingUnitData king = ScriptableObject.CreateInstance<KingUnitData>();
        try
        {
            MethodInfo applyTuning = typeof(PlayerManager).GetMethod(
                "ApplyKingHeadLookTuning",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(applyTuning, Is.Not.Null);
            gameObject.AddComponent<Animator>();
            HeadLookController headLook = gameObject.AddComponent<HeadLookController>();
            headLook.lookAtWeight = 0.9f;
            headLook.tiltAngle = 4f;
            headLook.lookAtBodyWeight = 0.1f;
            headLook.lookAtHeadWeight = 0.8f;
            headLook.lookAtClampWeight = 0.5f;

            applyTuning.Invoke(null, new object[] { headLook, king });
            Assert.That(headLook.lookAtWeight, Is.EqualTo(0.9f).Within(0.0001f));
            Assert.That(headLook.tiltAngle, Is.EqualTo(4f).Within(0.0001f));
            Assert.That(headLook.lookAtBodyWeight, Is.EqualTo(0.1f).Within(0.0001f));
            Assert.That(headLook.lookAtHeadWeight, Is.EqualTo(0.8f).Within(0.0001f));
            Assert.That(headLook.lookAtClampWeight, Is.EqualTo(0.5f).Within(0.0001f));

            king.overridePresentationHeadLook = true;
            king.presentationHeadLookWeight = 1f;
            king.presentationHeadLookTiltAngle = 5f;
            king.presentationHeadLookBodyWeight = 0.2f;
            king.presentationHeadLookHeadWeight = 1f;
            king.presentationHeadLookClampWeight = 0.2f;
            applyTuning.Invoke(null, new object[] { headLook, king });

            Assert.That(headLook.lookAtWeight, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(headLook.tiltAngle, Is.EqualTo(5f).Within(0.0001f));
            Assert.That(headLook.lookAtBodyWeight, Is.EqualTo(0.2f).Within(0.0001f));
            Assert.That(headLook.lookAtHeadWeight, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(headLook.lookAtClampWeight, Is.EqualTo(0.2f).Within(0.0001f));
        }
        finally
        {
            Object.DestroyImmediate(king);
            Object.DestroyImmediate(gameObject);
        }
    }

    [Test]
    public void KingSkillDamageIsDataDrivenAndPowerScaled()
    {
        KingSkillData skill = ScriptableObject.CreateInstance<KingSkillData>();
        try
        {
            skill.damageMultiplier = 2f;
            skill.flatDamage = 10f;
            Assert.That(skill.ResolveDamage(50f, 1.25f), Is.EqualTo(137.5f).Within(0.001f));
        }
        finally
        {
            Object.DestroyImmediate(skill);
        }
    }

    [Test]
    public void PersistentKingStatusRequiresAnExplicitFiniteTargetLimit()
    {
        KingSkillData skill = ScriptableObject.CreateInstance<KingSkillData>();
        try
        {
            skill.statusEffect = StatusEffectType.Stunned;
            skill.statusDuration = 2f;
            skill.maxTargets = 0;
            Assert.That(skill.HasBoundedStatusEffect, Is.False);

            skill.maxTargets = 8;
            Assert.That(skill.HasBoundedStatusEffect, Is.True);
        }
        finally
        {
            Object.DestroyImmediate(skill);
        }
    }

    [Test]
    public void KingAreaSkillSnapshotsTargetsBeforeApplyingDamage()
    {
        const BindingFlags instanceMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        MethodInfo apply = typeof(PlayerManager).GetMethod("ApplyKingSkillAuthoritative", instanceMembers);
        Assert.That(apply, Is.Not.Null);
        Assert.That(
            MdfCompiledCodePolicy.ReferencesField(
                apply,
                typeof(PlayerManager),
                "_kingSkillTargetSnapshot"),
            Is.True,
            "Damage may despawn and reparent a monster, so the transform children cannot be mutated while being enumerated.");
        Assert.That(
            MdfCompiledCodePolicy.ReferencesMethod(
                apply,
                typeof(System.Collections.Generic.List<Monster>),
                nameof(System.Collections.Generic.List<Monster>.Add)),
            Is.True);
    }

    [Test]
    public void DefenseSequenceIdChangesForEveryRoundAndBattleHalf()
    {
        int roundOneBattleOne = PlayerManager.BuildKingDefenseSequenceId(1, GameManagers.GameState.Battle1);
        int roundOneBattleTwo = PlayerManager.BuildKingDefenseSequenceId(1, GameManagers.GameState.Battle2);
        int roundTwoBattleOne = PlayerManager.BuildKingDefenseSequenceId(2, GameManagers.GameState.Battle1);

        Assert.That(roundOneBattleOne, Is.Not.EqualTo(roundOneBattleTwo));
        Assert.That(roundOneBattleOne, Is.Not.EqualTo(roundTwoBattleOne));
        Assert.That(roundOneBattleTwo, Is.Not.EqualTo(roundTwoBattleOne));
    }

    [Test]
    public void PlacedUnitCameraYawIgnoresHeightAndPreservesYawOffsetSemantics()
    {
        const BindingFlags staticMembers = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        MethodInfo resolveYaw = typeof(UnitOrientationFixer).GetMethod(
            "TryResolveCameraFacingYaw",
            staticMembers);
        Assert.That(resolveYaw, Is.Not.Null);

        var cameraObject = new GameObject("camera-yaw-test");
        try
        {
            Camera camera = cameraObject.AddComponent<Camera>();
            Vector3 actorPosition = new Vector3(2f, 3f, -4f);
            cameraObject.transform.position = new Vector3(12f, 80f, -4f);

            object[] zeroOffsetArgs = { actorPosition, camera, 0f, Quaternion.identity };
            Assert.That((bool)resolveYaw.Invoke(null, zeroOffsetArgs), Is.True);
            Quaternion zeroOffset = (Quaternion)zeroOffsetArgs[3];
            Assert.That(Vector3.Dot(zeroOffset * Vector3.forward, Vector3.right), Is.GreaterThan(0.9999f));

            cameraObject.transform.position = new Vector3(12f, -80f, -4f);
            object[] differentHeightArgs = { actorPosition, camera, 0f, Quaternion.identity };
            Assert.That((bool)resolveYaw.Invoke(null, differentHeightArgs), Is.True);
            Assert.That(
                Quaternion.Angle(zeroOffset, (Quaternion)differentHeightArgs[3]),
                Is.LessThan(0.001f));

            object[] reversedArgs = { actorPosition, camera, 180f, Quaternion.identity };
            Assert.That((bool)resolveYaw.Invoke(null, reversedArgs), Is.True);
            Quaternion reversed = (Quaternion)reversedArgs[3];
            Assert.That(Vector3.Dot(reversed * Vector3.forward, Vector3.left), Is.GreaterThan(0.9999f));

            cameraObject.transform.position = new Vector3(actorPosition.x, 100f, actorPosition.z);
            object[] zeroPlanarDistanceArgs = { actorPosition, camera, 0f, Quaternion.identity };
            Assert.That((bool)resolveYaw.Invoke(null, zeroPlanarDistanceArgs), Is.False);
            Assert.That((Quaternion)zeroPlanarDistanceArgs[3], Is.EqualTo(Quaternion.identity));
        }
        finally
        {
            Object.DestroyImmediate(cameraObject);
        }
    }

    [Test]
    public void VisualOnlyKingCloneDoesNotCopyGameplayOrPhysicsComponents()
    {
        var source = new GameObject("KingVisualSource");
        GameObject clone = null;
        Mesh mesh = null;
        try
        {
            source.SetActive(false);
            source.AddComponent<Fusion.NetworkObject>();
            source.AddComponent<Unit>();
            source.AddComponent<BoxCollider>();
            source.AddComponent<Rigidbody>();
            source.AddComponent<Animator>();
            HeadLookController sourceHeadLook = source.AddComponent<HeadLookController>();
            sourceHeadLook.lookAtWeight = 0.407f;
            sourceHeadLook.tiltAngle = 1.75f;
            sourceHeadLook.lookAtBodyWeight = 0.2f;
            sourceHeadLook.lookAtHeadWeight = 0.9f;
            sourceHeadLook.lookAtClampWeight = 0.15f;

            var visualChild = new GameObject("VisualMesh");
            visualChild.transform.SetParent(source.transform, false);
            mesh = new Mesh { name = "KingVisualTestMesh" };
            visualChild.AddComponent<MeshFilter>().sharedMesh = mesh;
            visualChild.AddComponent<MeshRenderer>();

            clone = KingVisualCloneUtility.CreateVisualOnly(
                source,
                null,
                out Animator animator,
                out var transformMap);

            Assert.That(clone, Is.Not.Null);
            Assert.That(KingVisualCloneUtility.IsPresentationOnly(clone), Is.True);
            Assert.That(clone.GetComponentInChildren<Unit>(true), Is.Null);
            Assert.That(clone.GetComponentInChildren<Fusion.NetworkObject>(true), Is.Null);
            Assert.That(clone.GetComponentInChildren<Collider>(true), Is.Null);
            Assert.That(clone.GetComponentInChildren<Rigidbody>(true), Is.Null);
            Assert.That(clone.GetComponentInChildren<MeshFilter>(true)?.sharedMesh, Is.SameAs(mesh));
            Assert.That(animator, Is.Not.Null);
            Assert.That(transformMap.Count, Is.EqualTo(2));

            HeadLookController clonedHeadLook = clone.GetComponentInChildren<HeadLookController>(true);
            Assert.That(clonedHeadLook, Is.Not.Null);
            Assert.That(clonedHeadLook.lookAtWeight, Is.EqualTo(0.407f).Within(0.0001f));
            Assert.That(clonedHeadLook.tiltAngle, Is.EqualTo(1.75f).Within(0.0001f));
            Assert.That(clonedHeadLook.lookAtBodyWeight, Is.EqualTo(0.2f).Within(0.0001f));
            Assert.That(clonedHeadLook.lookAtHeadWeight, Is.EqualTo(0.9f).Within(0.0001f));
            Assert.That(clonedHeadLook.lookAtClampWeight, Is.EqualTo(0.15f).Within(0.0001f));
            MonoBehaviour[] clonedBehaviours = clone.GetComponentsInChildren<MonoBehaviour>(true);
            Assert.That(clonedBehaviours, Has.Length.EqualTo(1));
            Assert.That(clonedBehaviours[0], Is.TypeOf<HeadLookController>());

            clone.AddComponent<UnitOrientationFixer>();
            Assert.That(
                KingVisualCloneUtility.IsPresentationOnly(clone),
                Is.False,
                "Only HeadLookController is allow-listed on the presentation clone.");
        }
        finally
        {
            if (clone != null)
            {
                Object.DestroyImmediate(clone);
            }
            Object.DestroyImmediate(source);
            if (mesh != null)
            {
                Object.DestroyImmediate(mesh);
            }
        }
    }

    [Test]
    public void KingLateUpdateFacesCameraAndAttackDoesNotPersistTargetYaw()
    {
        const BindingFlags instanceMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var playerObject = new GameObject("king-facing-player");
        var presentationObject = new GameObject("king-facing-presentation");
        var cameraObject = new GameObject("king-facing-camera");
        var fieldObject = new GameObject("king-facing-field");

        try
        {
            PlayerManager player = playerObject.AddComponent<PlayerManager>();
            FieldManager field = fieldObject.AddComponent<FieldManager>();
            Camera camera = cameraObject.AddComponent<Camera>();
            cameraObject.transform.position = new Vector3(9f, 20f, -3f);
            presentationObject.transform.position = new Vector3(-1f, 0f, -3f);
            presentationObject.transform.SetParent(playerObject.transform, true);

            FieldInfo cachedCamera = typeof(FieldManager).GetField(
                "_cachedPlayerCamera",
                instanceMembers);
            Assert.That(cachedCamera, Is.Not.Null);
            cachedCamera.SetValue(field, camera);
            player.fieldManager = field;

            SetPlayerField(player, "_kingPresentation", presentationObject);
            SetPlayerField(player, "_kingHasCanonicalTransform", true);
            SetPlayerField(player, "_kingCanonicalLocalPosition", presentationObject.transform.localPosition);
            SetPlayerField(player, "_kingCanonicalLocalRotation", Quaternion.Euler(0f, 35f, 0f));
            SetPlayerField(player, "_kingCanonicalLocalScale", Vector3.one);
            SetPlayerField(player, "_kingFacesCamera", true);
            SetPlayerField(player, "_kingCameraYawOffsetDegrees", 0f);

            MethodInfo lateUpdate = typeof(PlayerManager).GetMethod("LateUpdate", instanceMembers);
            Assert.That(lateUpdate, Is.Not.Null);
            lateUpdate.Invoke(player, null);

            Vector3 planarToCamera = camera.transform.position - presentationObject.transform.position;
            planarToCamera.y = 0f;
            Assert.That(
                Vector3.Dot(presentationObject.transform.forward, planarToCamera.normalized),
                Is.GreaterThan(0.9999f));

            Quaternion cameraFacingRotation = presentationObject.transform.rotation;
            MethodInfo playAttack = typeof(PlayerManager).GetMethod(
                "PlayKingAttackPresentation",
                instanceMembers);
            Assert.That(playAttack, Is.Not.Null);
            Assert.That(
                (bool)playAttack.Invoke(player, new object[] { presentationObject.transform.position + Vector3.back * 20f }),
                Is.True);
            Assert.That(
                Quaternion.Angle(presentationObject.transform.rotation, cameraFacingRotation),
                Is.LessThan(0.001f));

            FieldInfo canonicalRotation = typeof(PlayerManager).GetField(
                "_kingCanonicalLocalRotation",
                instanceMembers);
            Assert.That(canonicalRotation, Is.Not.Null);
            Assert.That(
                Quaternion.Angle((Quaternion)canonicalRotation.GetValue(player), presentationObject.transform.localRotation),
                Is.LessThan(0.001f));
        }
        finally
        {
            Object.DestroyImmediate(playerObject);
            Object.DestroyImmediate(presentationObject);
            Object.DestroyImmediate(cameraObject);
            Object.DestroyImmediate(fieldObject);
        }
    }

    [Test]
    public void KingGoalAnchorIgnoresFlattenedGoalScaleAndPinsPresentationTransforms()
    {
        const BindingFlags instanceMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        const BindingFlags staticMembers = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        var playerObject = new GameObject("king-anchor-player");
        var fieldRoot = new GameObject("king-anchor-field");
        var goalObject = new GameObject("Goal");
        var anchorObject = new GameObject("KingGoalAnchor");
        var presentationObject = new GameObject("KingPresentation");
        var rigObject = new GameObject("Armature");

        try
        {
            fieldRoot.transform.position = new Vector3(7f, 2f, -4f);
            fieldRoot.transform.localScale = new Vector3(2f, 3f, 4f);
            goalObject.transform.SetParent(fieldRoot.transform, false);
            goalObject.transform.localPosition = new Vector3(1.5f, 0.2f, 2.5f);
            goalObject.transform.localScale = new Vector3(1f, 0.02f, 1f);
            anchorObject.transform.SetParent(goalObject.transform, false);

            MethodInfo alignAnchor = typeof(PlayerManager).GetMethod(
                "AlignKingPresentationAnchor",
                staticMembers);
            Assert.That(alignAnchor, Is.Not.Null);
            alignAnchor.Invoke(null, new object[] { anchorObject.transform, goalObject.transform });

            Assert.That(anchorObject.transform.parent, Is.SameAs(fieldRoot.transform));
            Assert.That(Vector3.Distance(anchorObject.transform.position, goalObject.transform.position), Is.LessThan(0.0001f));
            Assert.That(anchorObject.transform.lossyScale.x, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(anchorObject.transform.lossyScale.y, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(anchorObject.transform.lossyScale.z, Is.EqualTo(1f).Within(0.0001f));

            presentationObject.transform.SetParent(anchorObject.transform, false);
            rigObject.transform.SetParent(presentationObject.transform, false);
            var canonicalPosition = new Vector3(0f, 0.15f, 0f);
            var canonicalRotation = Quaternion.Euler(0f, 35f, 0f);
            var canonicalScale = Vector3.one * 1.3f;
            var canonicalRigPosition = new Vector3(0f, 0.25f, 0f);
            var canonicalRigRotation = Quaternion.Euler(-90f, 180f, 0f);
            var canonicalRigScale = new Vector3(1f, 1.1f, 0.9f);

            var player = playerObject.AddComponent<PlayerManager>();
            SetPlayerField(player, "<goalTransform>k__BackingField", goalObject.transform);
            SetPlayerField(player, "_kingPresentationAnchor", anchorObject.transform);
            SetPlayerField(player, "_kingPresentation", presentationObject);
            SetPlayerField(player, "_kingHasCanonicalTransform", true);
            SetPlayerField(player, "_kingCanonicalLocalPosition", canonicalPosition);
            SetPlayerField(player, "_kingCanonicalLocalRotation", canonicalRotation);
            SetPlayerField(player, "_kingCanonicalLocalScale", canonicalScale);
            SetPlayerField(player, "_kingExpectedWorldScale", canonicalScale);
            SetPlayerField(player, "_kingDamageReactionPlaying", false);
            SetPlayerField(player, "_kingRigRoot", rigObject.transform);
            SetPlayerField(player, "_kingRigLocalPosition", canonicalRigPosition);
            SetPlayerField(player, "_kingRigLocalRotation", canonicalRigRotation);
            SetPlayerField(player, "_kingRigLocalScale", canonicalRigScale);
            SetPlayerField(player, "_kingRigPinRequired", true);
            SetPlayerField(player, "_kingHasRigLocalRotation", true);

            presentationObject.transform.localPosition = new Vector3(8f, 9f, 10f);
            presentationObject.transform.localRotation = Quaternion.Euler(20f, 70f, 15f);
            presentationObject.transform.localScale = Vector3.one * 4f;
            rigObject.transform.localPosition = new Vector3(4f, 5f, 6f);
            rigObject.transform.localRotation = Quaternion.identity;
            rigObject.transform.localScale = Vector3.one * 3f;

            MethodInfo lateUpdate = typeof(PlayerManager).GetMethod("LateUpdate", instanceMembers);
            Assert.That(lateUpdate, Is.Not.Null);
            lateUpdate.Invoke(player, null);

            Assert.That(presentationObject.transform.localPosition, Is.EqualTo(canonicalPosition));
            Assert.That(Quaternion.Angle(presentationObject.transform.localRotation, canonicalRotation), Is.LessThan(0.001f));
            Assert.That(presentationObject.transform.localScale, Is.EqualTo(canonicalScale));
            Assert.That(rigObject.transform.localPosition, Is.EqualTo(canonicalRigPosition));
            Assert.That(Quaternion.Angle(rigObject.transform.localRotation, canonicalRigRotation), Is.LessThan(0.001f));
            Assert.That(rigObject.transform.localScale, Is.EqualTo(canonicalRigScale));
            Assert.That(Vector3.Distance(anchorObject.transform.position, goalObject.transform.position), Is.LessThan(0.0001f));

            MethodInfo diagnostics = typeof(PlayerManager).GetMethod(
                "TryCaptureKingPresentationDiagnostics",
                instanceMembers);
            Assert.That(diagnostics, Is.Not.Null);
            object[] diagnosticArgs = { 0f, 0f, 0f, 0f, 0f, false, false, false };
            Assert.That((bool)diagnostics.Invoke(player, diagnosticArgs), Is.True);
            Assert.That((float)diagnosticArgs[0], Is.LessThan(0.0001f), "goal distance");
            Assert.That((float)diagnosticArgs[2], Is.LessThan(0.0001f), "world scale drift");
            Assert.That((float)diagnosticArgs[3], Is.LessThan(0.0001f), "presentation transform drift");
            Assert.That((float)diagnosticArgs[4], Is.LessThan(0.0001f), "rig transform drift");
            Assert.That((bool)diagnosticArgs[5], Is.True, "neutral goal anchor");
            Assert.That((bool)diagnosticArgs[6], Is.True, "rig pin required");
            Assert.That((bool)diagnosticArgs[7], Is.True, "rig pin active");
        }
        finally
        {
            Object.DestroyImmediate(playerObject);
            Object.DestroyImmediate(fieldRoot);
        }
    }

    [Test]
    public void KingLoadRetryDelayBacksOffAndIsBounded()
    {
        float first = PlayerManager.ComputeKingLoadRetryDelay(1);
        float second = PlayerManager.ComputeKingLoadRetryDelay(2);
        float late = PlayerManager.ComputeKingLoadRetryDelay(100);

        Assert.That(first, Is.GreaterThan(0f));
        Assert.That(second, Is.GreaterThan(first));
        Assert.That(late, Is.GreaterThanOrEqualTo(second));
        Assert.That(late, Is.LessThanOrEqualTo(5f));
    }

    [Test]
    public void RuntimeAndHostMigrationReadinessUseStrictKingDataGate()
    {
        const BindingFlags instanceMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        MethodInfo runtimeReady = typeof(PlayerManager).GetMethod(
            nameof(PlayerManager.IsRuntimeReady),
            instanceMembers);
        MethodInfo migrationPlayerGate = typeof(GameManagers).GetMethod(
            "AreAllPlayersRuntimeReadyForMigration",
            instanceMembers);
        MethodInfo handlerPlayerGate = typeof(HostMigrationHandler).GetMethod(
            "EnsurePlayersRuntimeReady",
            instanceMembers);

        Assert.That(runtimeReady, Is.Not.Null);
        Assert.That(migrationPlayerGate, Is.Not.Null);
        Assert.That(handlerPlayerGate, Is.Not.Null);
        Assert.That(
            MdfCompiledCodePolicy.ReferencesMethod(
                runtimeReady,
                typeof(PlayerManager),
                nameof(PlayerManager.IsKingRuntimeDataReady)),
            Is.True,
            "Player runtime readiness must include loaded King data and required references.");
        Assert.That(
            MdfCompiledCodePolicy.ReferencesMethod(
                migrationPlayerGate,
                typeof(PlayerManager),
                nameof(PlayerManager.IsRuntimeReady)),
            Is.True,
            "GameManagers migration recovery must wait for strict player runtime readiness.");
        Assert.That(
            MdfCompiledCodePolicy.ReferencesMethod(
                handlerPlayerGate,
                typeof(PlayerManager),
                nameof(PlayerManager.IsRuntimeReady)),
            Is.True,
            "HostMigrationHandler terminal recovery gate must reject an unready King runtime.");
    }

    private static void SetPlayerField<T>(PlayerManager player, string fieldName, T value)
    {
        FieldInfo field = typeof(PlayerManager).GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, fieldName);
        field.SetValue(player, value);
    }
}
