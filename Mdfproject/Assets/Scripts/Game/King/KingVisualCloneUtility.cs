using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>
/// Creates a presentation-only copy of a unit prefab without instantiating any gameplay component.
/// The source prefab is treated as a read-only visual template: only transforms, renderers, meshes,
/// native Animator settings, and the presentation-only head-look controller are copied into
/// newly-created objects.
/// </summary>
public static class KingVisualCloneUtility
{
    public static GameObject CreateVisualOnly(
        GameObject source,
        Transform parent,
        out Animator primaryAnimator,
        out Dictionary<Transform, Transform> transformMap)
    {
        primaryAnimator = null;
        transformMap = new Dictionary<Transform, Transform>();
        if (source == null)
        {
            return null;
        }

        GameObject root = CreateTransformHierarchy(source.transform, parent, transformMap, true);
        if (root == null)
        {
            return null;
        }

        CopyVisualComponents(source.transform, transformMap, ref primaryAnimator);
        if (!IsPresentationOnly(root))
        {
            Debug.LogError($"[King] Unsafe gameplay component was found in visual-only clone '{source.name}'.");
            DestroySafely(root);
            primaryAnimator = null;
            transformMap.Clear();
            return null;
        }

        return root;
    }

    public static bool IsPresentationOnly(GameObject root)
    {
        if (root == null
            || root.GetComponentInChildren<Unit>(true) != null
            || root.GetComponentInChildren<NetworkObject>(true) != null
            || root.GetComponentInChildren<Collider>(true) != null
            || root.GetComponentInChildren<Rigidbody>(true) != null
            || root.GetComponentInChildren<Canvas>(true) != null)
        {
            return false;
        }

        MonoBehaviour[] behaviours = root.GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
        {
            if (!(behaviours[i] is HeadLookController))
            {
                return false;
            }
        }

        return true;
    }

    private static GameObject CreateTransformHierarchy(
        Transform source,
        Transform parent,
        Dictionary<Transform, Transform> transformMap,
        bool isRoot)
    {
        if (source == null)
        {
            return null;
        }

        var clone = new GameObject(source.name);
        if (isRoot)
        {
            // Keep the complete hierarchy inactive until every visual component is configured.
            clone.SetActive(false);
        }

        clone.layer = source.gameObject.layer;
        Transform cloneTransform = clone.transform;
        cloneTransform.SetParent(parent, false);
        cloneTransform.localPosition = source.localPosition;
        cloneTransform.localRotation = source.localRotation;
        cloneTransform.localScale = source.localScale;
        transformMap[source] = cloneTransform;

        int childCount = source.childCount;
        for (int i = 0; i < childCount; i++)
        {
            Transform sourceChild = source.GetChild(i);
            GameObject clonedChild = CreateTransformHierarchy(
                sourceChild,
                cloneTransform,
                transformMap,
                false);
            if (clonedChild != null)
            {
                clonedChild.SetActive(sourceChild.gameObject.activeSelf);
            }
        }

        return clone;
    }

    private static void CopyVisualComponents(
        Transform sourceRoot,
        Dictionary<Transform, Transform> transformMap,
        ref Animator primaryAnimator)
    {
        Transform[] sourceTransforms = sourceRoot.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < sourceTransforms.Length; i++)
        {
            Transform sourceTransform = sourceTransforms[i];
            if (sourceTransform == null || !transformMap.TryGetValue(sourceTransform, out Transform cloneTransform))
            {
                continue;
            }

            CopyMeshFilter(sourceTransform, cloneTransform);
            CopyMeshRenderer(sourceTransform, cloneTransform);
            CopySkinnedMeshRenderer(sourceTransform, cloneTransform, transformMap);
            CopySpriteRenderer(sourceTransform, cloneTransform);
            CopyAnimator(sourceTransform, cloneTransform, ref primaryAnimator);
            CopyHeadLookController(sourceTransform, cloneTransform);
        }
    }

    private static void CopyMeshFilter(Transform source, Transform clone)
    {
        MeshFilter sourceFilter = source.GetComponent<MeshFilter>();
        if (sourceFilter == null)
        {
            return;
        }

        MeshFilter cloneFilter = clone.gameObject.AddComponent<MeshFilter>();
        cloneFilter.sharedMesh = sourceFilter.sharedMesh;
    }

    private static void CopyMeshRenderer(Transform source, Transform clone)
    {
        MeshRenderer sourceRenderer = source.GetComponent<MeshRenderer>();
        if (sourceRenderer == null)
        {
            return;
        }

        MeshRenderer cloneRenderer = clone.gameObject.AddComponent<MeshRenderer>();
        CopyRendererSettings(sourceRenderer, cloneRenderer);
    }

    private static void CopySkinnedMeshRenderer(
        Transform source,
        Transform clone,
        Dictionary<Transform, Transform> transformMap)
    {
        SkinnedMeshRenderer sourceRenderer = source.GetComponent<SkinnedMeshRenderer>();
        if (sourceRenderer == null)
        {
            return;
        }

        SkinnedMeshRenderer cloneRenderer = clone.gameObject.AddComponent<SkinnedMeshRenderer>();
        CopyRendererSettings(sourceRenderer, cloneRenderer);
        cloneRenderer.sharedMesh = sourceRenderer.sharedMesh;
        cloneRenderer.quality = sourceRenderer.quality;
        cloneRenderer.updateWhenOffscreen = sourceRenderer.updateWhenOffscreen;
        cloneRenderer.skinnedMotionVectors = sourceRenderer.skinnedMotionVectors;
        cloneRenderer.localBounds = sourceRenderer.localBounds;

        Transform[] sourceBones = sourceRenderer.bones;
        var cloneBones = new Transform[sourceBones.Length];
        for (int i = 0; i < sourceBones.Length; i++)
        {
            Transform sourceBone = sourceBones[i];
            if (sourceBone != null)
            {
                transformMap.TryGetValue(sourceBone, out cloneBones[i]);
            }
        }
        cloneRenderer.bones = cloneBones;

        if (sourceRenderer.rootBone != null
            && transformMap.TryGetValue(sourceRenderer.rootBone, out Transform cloneRootBone))
        {
            cloneRenderer.rootBone = cloneRootBone;
        }

        Mesh mesh = sourceRenderer.sharedMesh;
        if (mesh == null)
        {
            return;
        }

        int blendShapeCount = mesh.blendShapeCount;
        for (int i = 0; i < blendShapeCount; i++)
        {
            cloneRenderer.SetBlendShapeWeight(i, sourceRenderer.GetBlendShapeWeight(i));
        }
    }

    private static void CopySpriteRenderer(Transform source, Transform clone)
    {
        SpriteRenderer sourceRenderer = source.GetComponent<SpriteRenderer>();
        if (sourceRenderer == null)
        {
            return;
        }

        SpriteRenderer cloneRenderer = clone.gameObject.AddComponent<SpriteRenderer>();
        CopyRendererSettings(sourceRenderer, cloneRenderer);
        cloneRenderer.sprite = sourceRenderer.sprite;
        cloneRenderer.color = sourceRenderer.color;
        cloneRenderer.flipX = sourceRenderer.flipX;
        cloneRenderer.flipY = sourceRenderer.flipY;
        cloneRenderer.drawMode = sourceRenderer.drawMode;
        cloneRenderer.size = sourceRenderer.size;
        cloneRenderer.tileMode = sourceRenderer.tileMode;
        cloneRenderer.maskInteraction = sourceRenderer.maskInteraction;
        cloneRenderer.spriteSortPoint = sourceRenderer.spriteSortPoint;
    }

    private static void CopyAnimator(Transform source, Transform clone, ref Animator primaryAnimator)
    {
        Animator sourceAnimator = source.GetComponent<Animator>();
        if (sourceAnimator == null)
        {
            return;
        }

        Animator cloneAnimator = clone.gameObject.AddComponent<Animator>();
        cloneAnimator.runtimeAnimatorController = sourceAnimator.runtimeAnimatorController;
        cloneAnimator.avatar = sourceAnimator.avatar;
        cloneAnimator.applyRootMotion = false;
        cloneAnimator.updateMode = sourceAnimator.updateMode;
        cloneAnimator.cullingMode = sourceAnimator.cullingMode;
        cloneAnimator.speed = sourceAnimator.speed;
        cloneAnimator.fireEvents = sourceAnimator.fireEvents;
        cloneAnimator.enabled = sourceAnimator.enabled;
        if (primaryAnimator == null)
        {
            primaryAnimator = cloneAnimator;
        }
    }

    private static void CopyHeadLookController(Transform source, Transform clone)
    {
        HeadLookController sourceHeadLook = source.GetComponent<HeadLookController>();
        if (sourceHeadLook == null || clone.GetComponent<Animator>() == null)
        {
            return;
        }

        HeadLookController cloneHeadLook = clone.gameObject.AddComponent<HeadLookController>();
        cloneHeadLook.enabled = sourceHeadLook.enabled;
        cloneHeadLook.lookAtWeight = sourceHeadLook.lookAtWeight;
        cloneHeadLook.tiltAngle = sourceHeadLook.tiltAngle;
        cloneHeadLook.lookAtBodyWeight = sourceHeadLook.lookAtBodyWeight;
        cloneHeadLook.lookAtHeadWeight = sourceHeadLook.lookAtHeadWeight;
        cloneHeadLook.lookAtClampWeight = sourceHeadLook.lookAtClampWeight;
    }

    private static void CopyRendererSettings(Renderer source, Renderer clone)
    {
        clone.enabled = source.enabled;
        clone.sharedMaterials = source.sharedMaterials;
        clone.shadowCastingMode = source.shadowCastingMode;
        clone.receiveShadows = source.receiveShadows;
        clone.lightProbeUsage = source.lightProbeUsage;
        clone.reflectionProbeUsage = source.reflectionProbeUsage;
        clone.motionVectorGenerationMode = source.motionVectorGenerationMode;
        clone.allowOcclusionWhenDynamic = source.allowOcclusionWhenDynamic;
        clone.sortingLayerID = source.sortingLayerID;
        clone.sortingOrder = source.sortingOrder;
    }

    private static void DestroySafely(GameObject target)
    {
        if (target == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Object.Destroy(target);
        }
        else
        {
            Object.DestroyImmediate(target);
        }
    }
}
