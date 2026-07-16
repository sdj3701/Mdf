using UnityEngine;

/// <summary>
/// Read-only selection surface for the presentation-only King clone. The clone intentionally has
/// no Collider or gameplay component, so field input selects it from its rendered world bounds.
/// </summary>
public partial class PlayerManager
{
    public bool TryGetKingPresentationBounds(out Bounds bounds)
    {
        bounds = default;
        if (_kingPresentation == null || !_kingPresentation.activeInHierarchy)
        {
            return false;
        }

        bool found = false;
        Renderer[] renderers = _kingRenderers;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
            {
                continue;
            }

            if (!found)
            {
                bounds = renderer.bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        return found;
    }
}
