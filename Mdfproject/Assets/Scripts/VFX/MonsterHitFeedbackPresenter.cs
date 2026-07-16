using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Plays short, local-only monster hit feedback. This component never changes
/// gameplay state, the network root, colliders, or Animator playback speed.
/// </summary>
[DisallowMultipleComponent]
public sealed class MonsterHitFeedbackPresenter : MonoBehaviour
{
    public const float ReactionDurationSeconds = 0.08f;

    private const float MinimumDamageDelta = 0.001f;
    private const float FlashBlend = 0.85f;
    private const float HorizontalPunch = 0.045f;
    private const float VerticalSquash = 0.08f;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly Color HitFlashColor = new Color(1f, 0.28f, 0.16f, 1f);

    private Renderer[] _renderers = System.Array.Empty<Renderer>();
    private MaterialPropertyBlock[] _workingBlocks = System.Array.Empty<MaterialPropertyBlock>();
    private MaterialPropertyBlock[] _originalBlocks = System.Array.Empty<MaterialPropertyBlock>();
    private Color[] _baseColors = System.Array.Empty<Color>();
    private bool[] _supportsColor = System.Array.Empty<bool>();
    private Transform _visualPunchRoot;
    private Vector3 _baseVisualScale;
    private bool _isConfigured;
    private bool _isPlaying;
    private float _reactionStartedAt;

    public static bool ShouldPlayForHealthChange(
        float previousHealth,
        float previousMaxHealth,
        float currentHealth,
        float currentMaxHealth)
    {
        float maxHealthTolerance = Mathf.Max(0.01f, Mathf.Abs(previousMaxHealth) * 0.0001f);
        return previousHealth > 0f
            && currentHealth < previousHealth - MinimumDamageDelta
            && Mathf.Abs(currentMaxHealth - previousMaxHealth) <= maxHealthTolerance;
    }

    public void Configure(Transform visualRoot)
    {
        Transform safeVisualRoot = visualRoot != null
            && visualRoot != transform
            && visualRoot.IsChildOf(transform)
                ? visualRoot
                : null;

        if (_isConfigured && _visualPunchRoot == safeVisualRoot && _renderers.Length > 0)
        {
            return;
        }

        RestorePresentation();
        _visualPunchRoot = safeVisualRoot;
        if (_visualPunchRoot != null)
        {
            _baseVisualScale = _visualPunchRoot.localScale;
        }

        CaptureRendererState();
        _isConfigured = true;
    }

    public bool Play()
    {
        if (!isActiveAndEnabled)
        {
            return false;
        }

        if (!_isConfigured || _renderers.Length == 0)
        {
            Configure(_visualPunchRoot);
        }

        if (_renderers.Length == 0 && _visualPunchRoot == null)
        {
            return false;
        }

        RestorePresentation();
        _reactionStartedAt = Time.unscaledTime;
        _isPlaying = true;
        ApplyPresentation(0f);
        return true;
    }

    public void RestorePresentation()
    {
        int rendererCount = Mathf.Min(_renderers.Length, _originalBlocks.Length);
        for (int i = 0; i < rendererCount; i++)
        {
            Renderer targetRenderer = _renderers[i];
            if (targetRenderer != null && _originalBlocks[i] != null)
            {
                targetRenderer.SetPropertyBlock(_originalBlocks[i]);
            }
        }

        if (_visualPunchRoot != null)
        {
            _visualPunchRoot.localScale = _baseVisualScale;
        }

        _isPlaying = false;
    }

    private void LateUpdate()
    {
        if (!_isPlaying)
        {
            return;
        }

        float normalizedTime = Mathf.Clamp01(
            (Time.unscaledTime - _reactionStartedAt) / ReactionDurationSeconds);
        if (normalizedTime >= 1f)
        {
            RestorePresentation();
            return;
        }

        ApplyPresentation(normalizedTime);
    }

    private void CaptureRendererState()
    {
        Renderer[] candidates = GetComponentsInChildren<Renderer>(true);
        var visibleRenderers = new List<Renderer>(candidates.Length);
        for (int i = 0; i < candidates.Length; i++)
        {
            Renderer candidate = candidates[i];
            if (candidate is MeshRenderer || candidate is SkinnedMeshRenderer)
            {
                visibleRenderers.Add(candidate);
            }
        }

        _renderers = visibleRenderers.ToArray();
        _workingBlocks = new MaterialPropertyBlock[_renderers.Length];
        _originalBlocks = new MaterialPropertyBlock[_renderers.Length];
        _baseColors = new Color[_renderers.Length];
        _supportsColor = new bool[_renderers.Length];

        for (int i = 0; i < _renderers.Length; i++)
        {
            Renderer targetRenderer = _renderers[i];
            _workingBlocks[i] = new MaterialPropertyBlock();
            _originalBlocks[i] = new MaterialPropertyBlock();
            if (targetRenderer == null)
            {
                continue;
            }

            targetRenderer.GetPropertyBlock(_originalBlocks[i]);
            Material material = targetRenderer.sharedMaterial;
            if (material == null)
            {
                _baseColors[i] = Color.white;
                continue;
            }

            if (material.HasProperty(BaseColorId))
            {
                _baseColors[i] = material.GetColor(BaseColorId);
                _supportsColor[i] = true;
            }
            else if (material.HasProperty(ColorId))
            {
                _baseColors[i] = material.GetColor(ColorId);
                _supportsColor[i] = true;
            }
            else
            {
                _baseColors[i] = Color.white;
            }
        }
    }

    private void ApplyPresentation(float normalizedTime)
    {
        float flashStrength = 1f - Mathf.SmoothStep(0f, 1f, normalizedTime);
        float punchStrength = Mathf.Sin(normalizedTime * Mathf.PI);

        int rendererCount = Mathf.Min(_renderers.Length, _workingBlocks.Length);
        for (int i = 0; i < rendererCount; i++)
        {
            Renderer targetRenderer = _renderers[i];
            MaterialPropertyBlock block = _workingBlocks[i];
            if (targetRenderer == null || block == null || !_supportsColor[i])
            {
                continue;
            }

            targetRenderer.SetPropertyBlock(_originalBlocks[i]);
            block.Clear();
            targetRenderer.GetPropertyBlock(block);
            Color baseColor = _baseColors[i];
            Color flashColor = Color.Lerp(baseColor, HitFlashColor, flashStrength * FlashBlend);
            flashColor.a = baseColor.a;
            block.SetColor(BaseColorId, flashColor);
            block.SetColor(ColorId, flashColor);
            targetRenderer.SetPropertyBlock(block);
        }

        if (_visualPunchRoot != null)
        {
            _visualPunchRoot.localScale = Vector3.Scale(
                _baseVisualScale,
                new Vector3(
                    1f + punchStrength * HorizontalPunch,
                    1f - punchStrength * VerticalSquash,
                    1f + punchStrength * HorizontalPunch));
        }
    }

    private void OnDisable()
    {
        RestorePresentation();
    }
}
