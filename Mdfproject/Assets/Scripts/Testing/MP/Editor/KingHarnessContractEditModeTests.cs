#if UNITY_EDITOR
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using NUnitAssert = NUnit.Framework.Assert;

public sealed class KingHarnessContractEditModeTests
{
    private const BindingFlags InstanceMembers =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    [Test]
    public void VisualCapturePreparationHidesShopAugmentAndDevelopmentLogOverlay()
    {
        MethodInfo handler = typeof(MPTestAutomationServer).GetMethod(
            "ExecutePrepareVisualCaptureCommand",
            InstanceMembers);
        NUnitAssert.That(handler, Is.Not.Null);
        NUnitAssert.That(
            MdfCompiledCodePolicy.ReferencesMethod(
                handler,
                typeof(GamePrepareUIToolkitController),
                nameof(GamePrepareUIToolkitController.TrySetShopVisibilityFromLegacy)),
            Is.True);
        NUnitAssert.That(
            MdfCompiledCodePolicy.ReferencesMethod(
                handler,
                typeof(GamePrepareUIToolkitController),
                nameof(GamePrepareUIToolkitController.TryHideAugmentForVisualCapture)),
            Is.True);
        NUnitAssert.That(
            MdfCompiledCodePolicy.ReferencesMethod(
                handler,
                typeof(BuildDebugGUI),
                nameof(BuildDebugGUI.SetVisible)),
            Is.True);
    }

    [Test]
    public void LiveSnapshotUsesSchemaV3AndSerializesEveryKingContractField()
    {
        MPTestStateSnapshot.Snapshot live = MPTestStateSnapshot.Capture(
            "editor-test",
            "king-schema-contract",
            "king-schema-session");
        NUnitAssert.That(live.Version, Is.EqualTo(3));

        var expectedPlayer = new MPTestStateSnapshot.PlayerSnapshot
        {
            PlayerId = 7,
            SelectedMapThemeId = (int)MapThemeId.Classic,
            AppliedMapThemeId = (int)MapThemeId.Classic,
            MapThemePresentationReady = true,
            SelectedKingUnitKeyHash = 101,
            KingDataReady = true,
            KingSkillUsedThisDefense = true,
            KingCanUseSkill = false,
            KingDefenseSequenceId = 102,
            KingDamageReactionSequence = 103,
            KingAttackPresentationSequence = 104,
            KingSkillPresentationSequence = 105,
            KingAttackDamageBonusPermille = 106,
            KingAttackSpeedBonusPermille = 107,
            KingSkillPowerBonusPermille = 108,
            KingPresentationReady = true,
            KingPresentationGoalDistance = 0.001f,
            KingPresentationScaleMultiplier = 1.3f,
            KingPresentationWorldScaleDrift = 0.0002f,
            KingPresentationTransformDrift = 0.0003f,
            KingRigTransformDrift = 0.0004f,
            KingUsesNeutralGoalAnchor = true,
            KingRigPinRequired = true,
            KingRigPinActive = true,
            KingCameraFacingAngle = 0.25f,
            KingHeadLookActive = false,
            KingHeadLookApplied = false,
            KingHeadPresentationMode = "base_idle",
            KingHeadPose = new MPTestStateSnapshot.KingHeadPoseSnapshot
            {
                Comparable = true,
                KingHeadFound = true,
                BaseUnitFound = true,
                CameraFound = true,
                BaseUnitKey = "UnitData_Mage",
                BaseUnitName = "Mage(Clone)",
                KingHeadLookRecent = false,
                BaseHeadLookRecent = true,
                KingHeadLookFrameAge = -1,
                BaseHeadLookFrameAge = 2,
                KingAnimatorCullingMode = "AlwaysAnimate",
                BaseAnimatorCullingMode = "CullUpdateTransforms",
                RootRelativeRotationDeltaDeg = 3.5f,
                KingForwardElevationDeg = 42f,
                BaseForwardElevationDeg = 39f,
                KingToCameraAngleDeg = 8f,
                BaseToCameraAngleDeg = 9f,
                KingToConfiguredLookAngleDeg = 4f,
                BaseToConfiguredLookAngleDeg = 5f
            },
            Field = new MPTestStateSnapshot.FieldSnapshot
            {
                GoalReady = true,
                GoalCell = "5,4,0",
                RegularUnitGoalViolationCount = 0
            }
        };
        var snapshot = new MPTestStateSnapshot.Snapshot
        {
            Version = live.Version,
            Role = "editor-test",
            CaseName = "king-schema-contract",
            Session = "king-schema-session",
            Players = new[] { expectedPlayer }
        };

        string json = MPTestStateSnapshot.ToJson(snapshot);
        JObject document = JObject.Parse(json);
        JToken player = document["players"]?[0];

        NUnitAssert.That((int)document["version"], Is.EqualTo(3));
        NUnitAssert.That((int)player?["selectedMapThemeId"], Is.EqualTo((int)MapThemeId.Classic));
        NUnitAssert.That((int)player?["appliedMapThemeId"], Is.EqualTo((int)MapThemeId.Classic));
        NUnitAssert.That((bool)player?["mapThemePresentationReady"], Is.True);
        NUnitAssert.That((int)player?["selectedKingUnitKeyHash"], Is.EqualTo(101));
        NUnitAssert.That((bool)player?["kingDataReady"], Is.True);
        NUnitAssert.That((bool)player?["kingSkillUsedThisDefense"], Is.True);
        NUnitAssert.That((bool)player?["kingCanUseSkill"], Is.False);
        NUnitAssert.That((int)player?["kingDefenseSequenceId"], Is.EqualTo(102));
        NUnitAssert.That((int)player?["kingDamageReactionSequence"], Is.EqualTo(103));
        NUnitAssert.That((int)player?["kingAttackPresentationSequence"], Is.EqualTo(104));
        NUnitAssert.That((int)player?["kingSkillPresentationSequence"], Is.EqualTo(105));
        NUnitAssert.That((int)player?["kingAttackDamageBonusPermille"], Is.EqualTo(106));
        NUnitAssert.That((int)player?["kingAttackSpeedBonusPermille"], Is.EqualTo(107));
        NUnitAssert.That((int)player?["kingSkillPowerBonusPermille"], Is.EqualTo(108));
        NUnitAssert.That((bool)player?["kingPresentationReady"], Is.True);
        NUnitAssert.That((float)player?["kingPresentationGoalDistance"], Is.EqualTo(0.001f).Within(0.00001f));
        NUnitAssert.That((float)player?["kingPresentationScaleMultiplier"], Is.EqualTo(1.3f).Within(0.00001f));
        NUnitAssert.That((float)player?["kingPresentationWorldScaleDrift"], Is.EqualTo(0.0002f).Within(0.00001f));
        NUnitAssert.That((float)player?["kingPresentationTransformDrift"], Is.EqualTo(0.0003f).Within(0.00001f));
        NUnitAssert.That((float)player?["kingRigTransformDrift"], Is.EqualTo(0.0004f).Within(0.00001f));
        NUnitAssert.That((bool)player?["kingUsesNeutralGoalAnchor"], Is.True);
        NUnitAssert.That((bool)player?["kingRigPinRequired"], Is.True);
        NUnitAssert.That((bool)player?["kingRigPinActive"], Is.True);
        NUnitAssert.That((float)player?["kingCameraFacingAngle"], Is.EqualTo(0.25f).Within(0.00001f));
        NUnitAssert.That((bool)player?["kingHeadLookActive"], Is.False);
        NUnitAssert.That((bool)player?["kingHeadLookApplied"], Is.False);
        NUnitAssert.That((string)player?["kingHeadPresentationMode"], Is.EqualTo("base_idle"));
        NUnitAssert.That((bool)player?["kingHeadPose"]?["comparable"], Is.True);
        NUnitAssert.That((string)player?["kingHeadPose"]?["baseUnitKey"], Is.EqualTo("UnitData_Mage"));
        NUnitAssert.That((float)player?["kingHeadPose"]?["rootRelativeRotationDeltaDeg"],
            Is.EqualTo(3.5f).Within(0.00001f));
        NUnitAssert.That((float)player?["kingHeadPose"]?["kingForwardElevationDeg"],
            Is.EqualTo(42f).Within(0.00001f));
        NUnitAssert.That((float)player?["kingHeadPose"]?["baseForwardElevationDeg"],
            Is.EqualTo(39f).Within(0.00001f));
        NUnitAssert.That((string)player?["kingHeadPose"]?["kingAnimatorCullingMode"],
            Is.EqualTo("AlwaysAnimate"));
        NUnitAssert.That((string)player?["field"]?["goalCell"], Is.EqualTo("5,4,0"));
        NUnitAssert.That((int)player?["field"]?["regularUnitGoalViolationCount"], Is.Zero);

        MPTestStateSnapshot.Snapshot restored =
            JsonConvert.DeserializeObject<MPTestStateSnapshot.Snapshot>(json);
        NUnitAssert.That(restored, Is.Not.Null);
        NUnitAssert.That(restored.Players, Has.Length.EqualTo(1));
        NUnitAssert.That(restored.Players[0].SelectedMapThemeId, Is.EqualTo((int)MapThemeId.Classic));
        NUnitAssert.That(restored.Players[0].AppliedMapThemeId, Is.EqualTo((int)MapThemeId.Classic));
        NUnitAssert.That(restored.Players[0].MapThemePresentationReady, Is.True);
        NUnitAssert.That(restored.Players[0].SelectedKingUnitKeyHash, Is.EqualTo(101));
        NUnitAssert.That(restored.Players[0].KingSkillPresentationSequence, Is.EqualTo(105));
        NUnitAssert.That(restored.Players[0].KingSkillPowerBonusPermille, Is.EqualTo(108));
        NUnitAssert.That(restored.Players[0].KingPresentationReady, Is.True);
        NUnitAssert.That(restored.Players[0].KingPresentationScaleMultiplier, Is.EqualTo(1.3f).Within(0.00001f));
        NUnitAssert.That(restored.Players[0].KingUsesNeutralGoalAnchor, Is.True);
        NUnitAssert.That(restored.Players[0].KingRigPinRequired, Is.True);
        NUnitAssert.That(restored.Players[0].KingRigPinActive, Is.True);
        NUnitAssert.That(restored.Players[0].KingCameraFacingAngle, Is.EqualTo(0.25f).Within(0.00001f));
        NUnitAssert.That(restored.Players[0].KingHeadLookActive, Is.False);
        NUnitAssert.That(restored.Players[0].KingHeadLookApplied, Is.False);
        NUnitAssert.That(restored.Players[0].KingHeadPresentationMode, Is.EqualTo("base_idle"));
        NUnitAssert.That(restored.Players[0].KingHeadPose, Is.Not.Null);
        NUnitAssert.That(restored.Players[0].KingHeadPose.Comparable, Is.True);
        NUnitAssert.That(restored.Players[0].KingHeadPose.BaseUnitKey, Is.EqualTo("UnitData_Mage"));
        NUnitAssert.That(restored.Players[0].KingHeadPose.RootRelativeRotationDeltaDeg,
            Is.EqualTo(3.5f).Within(0.00001f));
        NUnitAssert.That(restored.Players[0].Field.GoalCell, Is.EqualTo("5,4,0"));
        NUnitAssert.That(restored.Players[0].Field.RegularUnitGoalViolationCount, Is.Zero);
    }

    [Test]
    public void LegacySchemaV1WithoutKingFieldsStillDeserializesWithSafeDefaults()
    {
        const string legacyJson =
            "{\"version\":1,\"role\":\"host\",\"caseName\":\"legacy\",\"session\":\"legacy-session\","
            + "\"players\":[{\"playerId\":3,\"health\":100,\"gold\":10,\"wallCount\":5}]}";

        MPTestStateSnapshot.Snapshot restored =
            JsonConvert.DeserializeObject<MPTestStateSnapshot.Snapshot>(legacyJson);

        NUnitAssert.That(restored, Is.Not.Null);
        NUnitAssert.That(restored.Version, Is.EqualTo(1));
        NUnitAssert.That(restored.Players, Has.Length.EqualTo(1));
        MPTestStateSnapshot.PlayerSnapshot player = restored.Players[0];
        NUnitAssert.That(player.PlayerId, Is.EqualTo(3));
        NUnitAssert.That(player.SelectedKingUnitKeyHash, Is.Zero);
        NUnitAssert.That(player.KingDataReady, Is.False);
        NUnitAssert.That(player.KingSkillUsedThisDefense, Is.False);
        NUnitAssert.That(player.KingCanUseSkill, Is.False);
        NUnitAssert.That(player.KingDefenseSequenceId, Is.Zero);
        NUnitAssert.That(player.KingAttackDamageBonusPermille, Is.Zero);

        JObject roundTrip = JObject.Parse(MPTestStateSnapshot.ToJson(restored));
        NUnitAssert.That((int)roundTrip["version"], Is.EqualTo(1),
            "Reading an old artifact must not silently upgrade its declared schema version.");
    }

    [Test]
    public void ActivateKingSkillAutomationRouteKeepsAuthorityReadinessAndSharedCommandContracts()
    {
        MethodInfo route = typeof(MPTestAutomationServer).GetMethod("ExecuteCommand", InstanceMembers);
        MethodInfo handler = typeof(MPTestAutomationServer).GetMethod(
            "ExecuteActivateKingSkillCommand",
            InstanceMembers);

        NUnitAssert.That(route, Is.Not.Null);
        NUnitAssert.That(handler, Is.Not.Null);
        NUnitAssert.That(MdfCompiledCodePolicy.ContainsStringLiteral(route, "activate_king_skill"), Is.True);
        NUnitAssert.That(
            MdfCompiledCodePolicy.ReferencesMethod(
                route,
                typeof(MPTestAutomationServer),
                "ExecuteActivateKingSkillCommand"),
            Is.True);

        NUnitAssert.That(
            MdfCompiledCodePolicy.ReferencesMethod(handler, typeof(Fusion.NetworkObject), "get_HasInputAuthority"),
            Is.True);
        NUnitAssert.That(
            MdfCompiledCodePolicy.ReferencesMethod(handler, typeof(Fusion.NetworkObject), "get_HasStateAuthority"),
            Is.True);
        NUnitAssert.That(
            MdfCompiledCodePolicy.ReferencesMethod(handler, typeof(PlayerManager), "get_CanUseKingSkill"),
            Is.True);
        NUnitAssert.That(
            MdfCompiledCodePolicy.ContainsStringLiteral(handler, "king_command_authority_missing"),
            Is.True);
        NUnitAssert.That(
            MdfCompiledCodePolicy.ContainsStringLiteral(handler, "king_skill_not_ready"),
            Is.True);
        NUnitAssert.That(
            MdfCompiledCodePolicy.ReferencesMethod(handler, typeof(ActivateKingSkillCommand), ".ctor"),
            Is.True);
        NUnitAssert.That(
            MdfCompiledCodePolicy.ReferencesMethod(
                handler,
                typeof(CommandProcessor),
                nameof(CommandProcessor.RequestCommandExecution)),
            Is.True);
    }

    [Test]
    public void KingAvailabilityAndClientValidatorRejectTransitionOrUnreadyState()
    {
        MethodInfo availability = typeof(PlayerManager).GetMethod("IsKingDefensePhase", InstanceMembers);
        MethodInfo clientValidator = typeof(PlayerCommandRequestValidator).GetMethod(
            "ValidateActivateKingSkillRequest",
            InstanceMembers);

        NUnitAssert.That(availability, Is.Not.Null);
        NUnitAssert.That(clientValidator, Is.Not.Null);
        bool usesTransitionFlag = MdfCompiledCodePolicy.ReferencesMethod(
            availability,
            typeof(GameManagers),
            "get_IsSequenceTransitioning");
        bool usesSharedBattleBoundary = MdfCompiledCodePolicy.ReferencesMethod(
            availability,
            typeof(BattleCommandValidator),
            nameof(BattleCommandValidator.IsBattlePhase));
        NUnitAssert.That(
            usesTransitionFlag || usesSharedBattleBoundary,
            Is.True,
            "Host/runtime availability must close at the same transition boundary as client validation.");
        NUnitAssert.That(
            MdfCompiledCodePolicy.ReferencesMethod(
                availability,
                typeof(PlayerManager),
                "get_IsReadyForPlayerActions"),
            Is.True,
            "The host command path must not bypass player readiness enforced by the client envelope.");
        NUnitAssert.That(
            MdfCompiledCodePolicy.ReferencesMethod(
                clientValidator,
                typeof(BattleCommandValidator),
                nameof(BattleCommandValidator.IsBattlePhase)),
            Is.True);
        NUnitAssert.That(
            MdfCompiledCodePolicy.ReferencesMethod(
                clientValidator,
                typeof(PlayerManager),
                "get_CanUseKingSkill"),
            Is.True);
    }

    [Test]
    public void KingSelectionHarnessStartsBothPeersDirectlyInJoinLobby()
    {
        string source = MdfSourcePolicy.ReadStaticContract(
            "../tools/harness/mp/run_editor_host_build_client.py");

        NUnitAssert.That(
            source,
            Does.Contain(
                "effective_lobby_scene = KING_SELECTION_SCENE if verify_king_selection else args.lobby_scene"),
            "Switching a running MatchingLobby session to JoinLobby races NetworkPlayer creation.");
        NUnitAssert.That(source, Does.Contain("scene=effective_lobby_scene"));
        NUnitAssert.That(
            source,
            Does.Contain(
                "wait_build_peer_started(client.join, artifact_dir, \"build-client\", session, effective_lobby_scene"));
    }
}
#endif
