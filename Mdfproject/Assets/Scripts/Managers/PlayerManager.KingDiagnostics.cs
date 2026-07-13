using System;
using UnityEngine;

internal struct KingBaseHeadPoseDiagnostics
{
    public string BaseUnitKey;
    public string BaseUnitName;
    public bool KingHeadFound;
    public bool BaseUnitFound;
    public bool CameraFound;
    public bool KingHeadLookRecent;
    public bool BaseHeadLookRecent;
    public int KingHeadLookFrameAge;
    public int BaseHeadLookFrameAge;
    public string KingAnimatorCullingMode;
    public string BaseAnimatorCullingMode;
    public float RootRelativeRotationDeltaDeg;
    public float KingForwardElevationDeg;
    public float BaseForwardElevationDeg;
    public float KingToCameraAngleDeg;
    public float BaseToCameraAngleDeg;
    public float KingToConfiguredLookAngleDeg;
    public float BaseToConfiguredLookAngleDeg;
}

public partial class PlayerManager
{
    /// <summary>
    /// Captures the final Humanoid head-bone pose of the King and a live field unit
    /// using the same base UnitData. This is read-only MP harness telemetry: it lets a
    /// visual regression distinguish an Animator/IK pose mismatch from camera, scale,
    /// hat, or renderer differences without adding a second King presentation path.
    /// </summary>
    internal bool TryCaptureKingBaseHeadPoseDiagnostics(
        out KingBaseHeadPoseDiagnostics diagnostics)
    {
        diagnostics = new KingBaseHeadPoseDiagnostics
        {
            BaseUnitKey = _selectedKingData != null && _selectedKingData.baseUnitData != null
                ? _selectedKingData.baseUnitData.name
                : string.Empty,
            BaseUnitName = string.Empty,
            KingHeadLookFrameAge = -1,
            BaseHeadLookFrameAge = -1,
            KingAnimatorCullingMode = string.Empty,
            BaseAnimatorCullingMode = string.Empty,
            RootRelativeRotationDeltaDeg = -1f,
            KingForwardElevationDeg = -91f,
            BaseForwardElevationDeg = -91f,
            KingToCameraAngleDeg = -1f,
            BaseToCameraAngleDeg = -1f,
            KingToConfiguredLookAngleDeg = -1f,
            BaseToConfiguredLookAngleDeg = -1f
        };

        UnitData baseData = _selectedKingData != null ? _selectedKingData.baseUnitData : null;
        if (baseData == null
            || !TryResolveHeadPoseSource(
                _kingAnimator,
                _kingHeadLookController,
                out Transform kingHead))
        {
            return false;
        }

        diagnostics.KingAnimatorCullingMode = _kingAnimator.cullingMode.ToString();
        diagnostics.KingHeadFound = true;
        diagnostics.KingHeadLookFrameAge = GetHeadLookFrameAge(_kingHeadLookController);
        diagnostics.KingHeadLookRecent = diagnostics.KingHeadLookFrameAge >= 0
                                         && diagnostics.KingHeadLookFrameAge <= 3;

        Unit baseUnit = FindLiveBaseUnitForHeadPose(baseData, out Animator baseAnimator,
            out HeadLookController baseHeadLook, out Transform baseHead);
        diagnostics.BaseUnitFound = baseUnit != null;
        if (baseUnit == null || baseAnimator == null || baseHead == null)
        {
            return false;
        }

        diagnostics.BaseUnitName = baseUnit.name;
        diagnostics.BaseAnimatorCullingMode = baseAnimator.cullingMode.ToString();
        diagnostics.BaseHeadLookFrameAge = GetHeadLookFrameAge(baseHeadLook);
        diagnostics.BaseHeadLookRecent = diagnostics.BaseHeadLookFrameAge >= 0
                                         && diagnostics.BaseHeadLookFrameAge <= 3;

        Quaternion kingRootRelativeRotation =
            Quaternion.Inverse(_kingAnimator.transform.rotation) * kingHead.rotation;
        Quaternion baseRootRelativeRotation =
            Quaternion.Inverse(baseAnimator.transform.rotation) * baseHead.rotation;
        diagnostics.RootRelativeRotationDeltaDeg = Quaternion.Angle(
            kingRootRelativeRotation,
            baseRootRelativeRotation);

        diagnostics.KingForwardElevationDeg = GetForwardElevationDeg(kingHead);
        diagnostics.BaseForwardElevationDeg = GetForwardElevationDeg(baseHead);
        diagnostics.KingToConfiguredLookAngleDeg = GetConfiguredLookAngleDeg(
            kingHead,
            _kingAnimator,
            _kingHeadLookController);
        diagnostics.BaseToConfiguredLookAngleDeg = GetConfiguredLookAngleDeg(
            baseHead,
            baseAnimator,
            baseHeadLook);

        Camera camera = _kingOrientationFixer != null
            ? _kingOrientationFixer.ResolveTargetCamera()
            : Camera.main;
        if (camera != null)
        {
            diagnostics.CameraFound = true;
            diagnostics.KingToCameraAngleDeg = GetHeadToCameraAngleDeg(kingHead, camera);
            diagnostics.BaseToCameraAngleDeg = GetHeadToCameraAngleDeg(baseHead, camera);
        }

        return true;
    }

    private Unit FindLiveBaseUnitForHeadPose(
        UnitData baseData,
        out Animator animator,
        out HeadLookController headLook,
        out Transform head)
    {
        animator = null;
        headLook = null;
        head = null;

        Transform unitParent = fieldManager != null ? fieldManager.unitParent : null;
        if (unitParent == null)
        {
            return null;
        }

        Unit[] units = unitParent.GetComponentsInChildren<Unit>(true);
        for (int i = 0; i < units.Length; i++)
        {
            Unit unit = units[i];
            if (unit == null || !IsSameBaseUnit(unit, baseData))
            {
                continue;
            }

            HeadLookController candidateHeadLook =
                unit.GetComponentInChildren<HeadLookController>(true);
            Animator candidateAnimator = candidateHeadLook != null
                ? candidateHeadLook.GetComponent<Animator>()
                : unit.GetComponentInChildren<Animator>(true);
            if (!TryResolveHeadPoseSource(
                    candidateAnimator,
                    candidateHeadLook,
                    out Transform candidateHead))
            {
                continue;
            }

            if (unit.gameObject.activeInHierarchy
                && candidateAnimator.isActiveAndEnabled
                && candidateHeadLook != null
                && candidateHeadLook.isActiveAndEnabled
                && candidateHeadLook.WasLookAtAppliedRecently(3))
            {
                animator = candidateAnimator;
                headLook = candidateHeadLook;
                head = candidateHead;
                return unit;
            }
        }

        return null;
    }

    private static bool IsSameBaseUnit(Unit unit, UnitData baseData)
    {
        if (unit == null || baseData == null)
        {
            return false;
        }

        if (ReferenceEquals(unit.Data, baseData))
        {
            return true;
        }

        string candidateKey = unit.Data != null ? unit.Data.name : unit.UnitDataKeyForRoster;
        return string.Equals(candidateKey, baseData.name, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryResolveHeadPoseSource(
        Animator animator,
        HeadLookController headLook,
        out Transform head)
    {
        head = null;
        if (animator == null || animator.avatar == null || !animator.isHuman)
        {
            return false;
        }

        head = animator.GetBoneTransform(HumanBodyBones.Head);
        return head != null && (headLook == null || headLook.GetComponent<Animator>() == animator);
    }

    private static int GetHeadLookFrameAge(HeadLookController headLook)
    {
        return headLook == null || headLook.LastLookAtAppliedFrame < 0
            ? -1
            : Mathf.Max(0, Time.frameCount - headLook.LastLookAtAppliedFrame);
    }

    private static float GetForwardElevationDeg(Transform head)
    {
        float upDot = Mathf.Clamp(Vector3.Dot(head.forward.normalized, Vector3.up), -1f, 1f);
        return Mathf.Asin(upDot) * Mathf.Rad2Deg;
    }

    private static float GetHeadToCameraAngleDeg(Transform head, Camera camera)
    {
        Vector3 toCamera = camera.transform.position - head.position;
        return toCamera.sqrMagnitude > 0.000001f
            ? Vector3.Angle(head.forward, toCamera)
            : 0f;
    }

    private static float GetConfiguredLookAngleDeg(
        Transform head,
        Animator animator,
        HeadLookController headLook)
    {
        if (headLook == null)
        {
            return -1f;
        }

        Vector3 configuredDirection =
            (animator.transform.forward + Vector3.up * headLook.tiltAngle).normalized;
        return Vector3.Angle(head.forward, configuredDirection);
    }
}
