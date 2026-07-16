using Fusion;
using MDF.Runtime.Grid;
using UnityEngine;

public partial class PlayerManager
{
    [Networked] public int SelectedMapThemeId { get; private set; }

    private FieldMapThemePresenter _mapThemePresenter;
    private int _lastAppliedMapThemeId = -1;

    private void InitializeMapThemeOnSpawn()
    {
        if (Object != null
            && Object.IsValid
            && Object.HasStateAuthority
            && !MapThemeCatalog.IsAllowed(SelectedMapThemeId))
        {
            SelectedMapThemeId = MapThemeCatalog.DefaultId;
        }

        _lastAppliedMapThemeId = -1;
        RenderMapTheme();
    }

    private void BindMapThemePresenter(GameObject gridRoot)
    {
        FieldMapThemePresenter presenter = gridRoot != null
            ? gridRoot.GetComponentInChildren<FieldMapThemePresenter>(true)
            : null;
        if (_mapThemePresenter == presenter)
        {
            RenderMapTheme();
            return;
        }

        _mapThemePresenter = presenter;
        _lastAppliedMapThemeId = -1;
        RenderMapTheme();
    }

    private void TryBindMapThemePresenterFromField()
    {
        if (_mapThemePresenter != null || fieldManager == null || fieldManager.ground3D == null)
        {
            return;
        }

        _mapThemePresenter = fieldManager.ground3D.GetComponentInParent<FieldMapThemePresenter>(true);
        _lastAppliedMapThemeId = -1;
    }

    private void RenderMapTheme()
    {
        TryBindMapThemePresenterFromField();
        if (_mapThemePresenter == null)
        {
            return;
        }

        int themeId = MapThemeCatalog.NormalizeOrDefault(SelectedMapThemeId);
        if (_lastAppliedMapThemeId == themeId
            && _mapThemePresenter.AppliedThemeId == themeId)
        {
            return;
        }

        _mapThemePresenter.ApplyTheme(themeId);
        _lastAppliedMapThemeId = themeId;
    }

    public void SetSelectedMapThemeIdAuthoritative(int requestedThemeId)
    {
        if (Object != null && Object.IsValid && !Object.HasStateAuthority)
        {
            return;
        }

        int themeId = MapThemeCatalog.NormalizeOrDefault(requestedThemeId);
        if (Object != null && Object.IsValid)
        {
            SelectedMapThemeId = themeId;
        }

        _lastAppliedMapThemeId = -1;
        RenderMapTheme();
    }

    public bool TryCaptureMapThemePresentation(out int appliedThemeId)
    {
        RenderMapTheme();
        appliedThemeId = _mapThemePresenter != null
            ? _mapThemePresenter.AppliedThemeId
            : 0;
        return _mapThemePresenter != null;
    }

    public bool RestoreMapThemeAfterHostMigration(int requestedThemeId, string context)
    {
        if (Object == null || !Object.IsValid || !Object.HasStateAuthority)
        {
            Debug.LogError($"[MapTheme] Host migration restore requires State Authority ({context})", this);
            return false;
        }

        int restoredThemeId = MapThemeCatalog.NormalizeOrDefault(requestedThemeId);
        if (restoredThemeId != requestedThemeId)
        {
            Debug.LogWarning(
                $"[MapTheme] Invalid durable map theme id {requestedThemeId}; " +
                $"using cosmetic default {restoredThemeId} ({context})",
                this);
        }

        SelectedMapThemeId = restoredThemeId;
        _lastAppliedMapThemeId = -1;
        RenderMapTheme();
        return true;
    }
}
