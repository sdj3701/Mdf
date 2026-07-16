using System;
using UnityEngine;

namespace MDF.Runtime.Grid
{
    /// <summary>
    /// Cosmetic-only field theme switch. It changes renderer visibility but never touches the
    /// field collider, GameObject active state, layers, transforms, or navigation data.
    /// </summary>
    public sealed class FieldMapThemePresenter : MonoBehaviour
    {
        [SerializeField] private Renderer[] classicRenderers = Array.Empty<Renderer>();
        [SerializeField] private Renderer[] arenaRenderers = Array.Empty<Renderer>();

        public int AppliedThemeId { get; private set; } = MapThemeCatalog.DefaultId;
        public Renderer[] ClassicRenderers => classicRenderers;
        public Renderer[] ArenaRenderers => arenaRenderers;

        private void Awake()
        {
            ApplyTheme(MapThemeCatalog.DefaultId);
        }

        public void Configure(Renderer[] classic, Renderer[] arena)
        {
            classicRenderers = classic ?? Array.Empty<Renderer>();
            arenaRenderers = arena ?? Array.Empty<Renderer>();
        }

        public void ApplyTheme(int requestedThemeId)
        {
            int themeId = MapThemeCatalog.NormalizeOrDefault(requestedThemeId);
            bool useClassic = themeId == (int)MapThemeId.Classic;
            SetRenderersEnabled(classicRenderers, useClassic);
            SetRenderersEnabled(arenaRenderers, !useClassic);
            AppliedThemeId = themeId;
        }

        private static void SetRenderersEnabled(Renderer[] renderers, bool enabled)
        {
            if (renderers == null)
            {
                return;
            }

            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                {
                    renderers[i].enabled = enabled;
                }
            }
        }
    }
}
