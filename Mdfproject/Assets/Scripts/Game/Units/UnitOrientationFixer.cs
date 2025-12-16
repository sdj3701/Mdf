// Assets/Scripts/Game/Units/UnitOrientationFixer.cs
using UnityEngine;

/// <summary>
/// Ensures the character's rig root maintains a specific local rotation after Animator applies its pose,
/// and (optionally) rotates the unit root to face the main camera on spawn.
///
/// Use this when the imported model requires a rig base rotation like X=-90, Y=180 to look correct.
/// Attach to the unit root (same object that has Animator).
/// </summary>
public class UnitOrientationFixer : MonoBehaviour
{
    [Header("Rig Root Detection")]
    [Tooltip("Explicit rig root Transform (e.g., 'Armature'). If null, will try to find by name.")]
    public Transform rigRoot;

    [Tooltip("If rigRoot is null, try this child name first (common: 'Armature').")]
    public string rigRootName = "Armature";

    [Header("Rig Local Rotation Target (degrees)")]
    [Tooltip("Local Euler angles to enforce on the rig root (X, Y, Z). Example: (-90, 180, 0)")]
    public Vector3 rigLocalEulerTarget = new Vector3(-90f, 180f, 0f);

    [Header("Behavior")]
    [Tooltip("Rotate the unit root to face the camera once on spawn (yaw only).")]
    public bool faceCameraOnSpawn = true;

    [Tooltip("Continuously enforce rig local rotation in LateUpdate (after Animator).")]
    public bool enforceEveryLateUpdate = true;

    [Tooltip("Explicit camera to face. If null, falls back to Camera.main.")]
    public Camera targetCamera;

    [Tooltip("Additional yaw degrees applied after facing the camera. Use 180 if your visual forward is flipped.")]
    public float yawOffsetDeg = 0f;

    private Animator _animator;
    private bool _didInitialFaceCamera;

    private void Awake()
    {
        _animator = GetComponent<Animator>();
    }

    private System.Collections.IEnumerator Start()
    {
        // Wait at least one frame so Animator applies default pose/retargeting
        yield return null;
        // Some controllers apply orientation on the second update; wait one more frame for safety
        yield return null;

        // One-time face camera (yaw only)
        if (faceCameraOnSpawn && !_didInitialFaceCamera)
        {
            FaceCameraYaw();
            _didInitialFaceCamera = true;
        }

        // Also enforce once immediately
        EnforceRigLocalRotation();
    }

    private void LateUpdate()
    {
        if (enforceEveryLateUpdate)
        {
            EnforceRigLocalRotation();
        }
    }

    private void EnforceRigLocalRotation()
    {
        var root = EnsureRigRoot();
        if (root == null) return;
        root.localRotation = Quaternion.Euler(rigLocalEulerTarget);
    }

    private Transform EnsureRigRoot()
    {
        if (rigRoot != null) return rigRoot;

        // Try configured name first
        if (!string.IsNullOrEmpty(rigRootName))
        {
            var t = transform.Find(rigRootName);
            if (t != null)
            {
                rigRoot = t;
                return rigRoot;
            }
        }

        // Common fallback names
        string[] candidates = { "Armature", "armature", "Rig", "rig", "Skeleton", "skeleton", "Root", "root" };
        foreach (var name in candidates)
        {
            var t = transform.Find(name);
            if (t != null)
            {
                rigRoot = t;
                return rigRoot;
            }
        }

        // Last resort: use SkinnedMeshRenderer rootBone parent as approximate rig root
        var smr = GetComponentInChildren<SkinnedMeshRenderer>();
        if (smr != null)
        {
            if (smr.rootBone != null)
            {
                rigRoot = smr.rootBone.parent != null ? smr.rootBone.parent : smr.rootBone;
                return rigRoot;
            }
        }

        return null;
    }

    private void FaceCameraYaw()
    {
        Camera cam = targetCamera != null ? targetCamera : Camera.main;
        if (cam == null) return;

        Vector3 toCam = cam.transform.position - transform.position;
        toCam.y = 0f; // yaw-only
        if (toCam.sqrMagnitude < 0.0001f) return;
        var baseRot = Quaternion.LookRotation(toCam.normalized, Vector3.up);
        var yawOffset = Quaternion.AngleAxis(yawOffsetDeg, Vector3.up);
        transform.rotation = yawOffset * baseRot;
    }
}
