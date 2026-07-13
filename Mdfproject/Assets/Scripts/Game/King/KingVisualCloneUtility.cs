using System;
using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>
/// Creates a presentation-only copy of a unit prefab without ever activating its gameplay code.
/// The complete serialized prefab is cloned under an inactive staging parent first, then every
/// non-presentation component is removed before the result can enter the active hierarchy. This
/// preserves native Animator/Humanoid bindings and renderer state exactly as authored on the base
/// unit instead of reconstructing those components at runtime.
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

        GameObject stagingRoot = new GameObject("KingVisualCloneStaging");
        stagingRoot.SetActive(false);
        if (parent != null)
        {
            stagingRoot.transform.SetParent(parent, false);
        }

        GameObject clone = null;
        try
        {
            // activeInHierarchy remains false while Unity copies the serialized prefab, so Unit,
            // NetworkBehaviour, and other gameplay lifecycle methods cannot run before stripping.
            clone = UnityEngine.Object.Instantiate(source, stagingRoot.transform, false);
            clone.SetActive(false);
            clone.name = source.name;

            MapTransformHierarchy(source.transform, clone.transform, transformMap);
            StripNonPresentationComponents(clone);

            clone.transform.SetParent(parent, false);
            primaryAnimator = clone.GetComponentInChildren<Animator>(true);
            if (!IsPresentationOnly(clone))
            {
                Debug.LogError($"[King] Unsafe gameplay component remained in visual-only clone '{source.name}'.");
                DestroyImmediately(clone);
                clone = null;
                primaryAnimator = null;
                transformMap.Clear();
            }

            return clone;
        }
        catch (Exception exception)
        {
            Debug.LogError($"[King] Failed to create visual-only clone '{source.name}': {exception.Message}");
            if (clone != null)
            {
                DestroyImmediately(clone);
            }

            primaryAnimator = null;
            transformMap.Clear();
            return null;
        }
        finally
        {
            DestroyImmediately(stagingRoot);
        }
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

        Component[] components = root.GetComponentsInChildren<Component>(true);
        for (int i = 0; i < components.Length; i++)
        {
            if (!IsAllowedPresentationComponent(components[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static void StripNonPresentationComponents(GameObject root)
    {
        Component[] components = root.GetComponentsInChildren<Component>(true);

        // Remove gameplay behaviours first so native dependencies such as NetworkObject and
        // Rigidbody can then be removed without leaving a live behaviour attached to them.
        for (int i = 0; i < components.Length; i++)
        {
            Component component = components[i];
            if (component != null
                && component is MonoBehaviour
                && !(component is NetworkObject)
                && !IsAllowedPresentationComponent(component))
            {
                DestroyImmediately(component);
            }
        }

        components = root.GetComponentsInChildren<Component>(true);
        for (int i = 0; i < components.Length; i++)
        {
            Component component = components[i];
            if (component != null && !IsAllowedPresentationComponent(component))
            {
                DestroyImmediately(component);
            }
        }
    }

    private static bool IsAllowedPresentationComponent(Component component)
    {
        return component is Transform
               || component is Animator
               || component is Renderer
               || component is MeshFilter
               || component is HeadLookController
               || component is UnitOrientationFixer
               || component is BodyScaler;
    }

    private static void MapTransformHierarchy(
        Transform source,
        Transform clone,
        Dictionary<Transform, Transform> transformMap)
    {
        if (source == null || clone == null)
        {
            return;
        }

        transformMap[source] = clone;
        int childCount = Mathf.Min(source.childCount, clone.childCount);
        for (int i = 0; i < childCount; i++)
        {
            MapTransformHierarchy(source.GetChild(i), clone.GetChild(i), transformMap);
        }
    }

    private static void DestroyImmediately(UnityEngine.Object target)
    {
        if (target != null)
        {
            // The clone must be stripped synchronously while its staging hierarchy is inactive.
            UnityEngine.Object.DestroyImmediate(target);
        }
    }
}
