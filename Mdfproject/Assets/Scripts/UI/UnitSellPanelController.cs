using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class UnitSellPanelController : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Button sellButton;
    [SerializeField] private TextMeshProUGUI sellPriceText;
    [SerializeField] private Vector3 worldOffset = new Vector3(0f, -0.3f, 0f);
    [SerializeField] private Vector2 screenOffset = Vector2.zero;
    [SerializeField] private bool overrideSorting = true;
    [SerializeField] private int sortingOrder = 200;

    private Unit currentUnit;
    private FieldManager fieldManager;
    private Canvas targetCanvas;
    private Camera targetCamera;
    private RectTransform rectTransform;
    private Canvas selfCanvas;

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        selfCanvas = GetComponent<Canvas>();
    }

    private void OnEnable()
    {
        GameEvents.OnGameStateChanged += HandleGameStateChanged;
    }

    private void OnDisable()
    {
        if (sellButton != null)
        {
            sellButton.onClick.RemoveListener(OnSellButtonClicked);
        }
        GameEvents.OnGameStateChanged -= HandleGameStateChanged;
        currentUnit = null;
        fieldManager = null;
        targetCanvas = null;
        targetCamera = null;
    }

    private void LateUpdate()
    {
        if (currentUnit == null || fieldManager == null) return;
        UpdatePosition();
    }

    public void Bind(Unit unit, FieldManager manager)
    {
        currentUnit = unit;
        fieldManager = manager;
        targetCanvas = UIManagers.Instance != null ? UIManagers.Instance.mainCanvas : null;
        targetCamera = manager != null ? manager.PlayerCamera : Camera.main;
        if (selfCanvas != null && selfCanvas.renderMode == RenderMode.WorldSpace)
        {
            selfCanvas.worldCamera = targetCamera;
            if (overrideSorting)
            {
                selfCanvas.overrideSorting = true;
                selfCanvas.sortingOrder = sortingOrder;
            }
        }
        HookSellButton();
        UpdateSellSection();
        UpdatePosition();
    }

    private void HandleGameStateChanged(GameManagers.GameState newState)
    {
        UpdateSellSection();
    }

    private void HookSellButton()
    {
        if (sellButton == null) return;
        sellButton.onClick.RemoveListener(OnSellButtonClicked);
        sellButton.onClick.AddListener(OnSellButtonClicked);
    }

    private void UpdateSellSection()
    {
        if (sellPriceText != null)
        {
            sellPriceText.text = (currentUnit != null && fieldManager != null)
                ? fieldManager.GetSellPrice(currentUnit).ToString()
                : string.Empty;
        }

        if (sellButton != null)
        {
            sellButton.interactable = CanSellCurrentUnit();
        }
    }

    private bool CanSellCurrentUnit()
    {
        if (currentUnit == null || fieldManager == null || fieldManager.playerManager == null) return false;
        var gm = GameManagers.Instance;
        if (gm != null && gm.GetGameState() != GameManagers.GameState.Prepare)
        {
            return false;
        }
        if (!fieldManager.IsLocalControlledField())
        {
            return false;
        }
        return fieldManager.GetUnitPosition(currentUnit).HasValue;
    }

    private void OnSellButtonClicked()
    {
        if (!CanSellCurrentUnit()) return;
        var pos = fieldManager.GetUnitPosition(currentUnit);
        if (!pos.HasValue) return;

        var command = new SellUnitCommand(fieldManager.playerManager.playerId, pos.Value);
        if (GameManagers.Instance != null && GameManagers.Instance.CommandProcessor != null)
        {
            GameManagers.Instance.CommandProcessor.RequestCommandExecution(command);
        }
        else
        {
            fieldManager.TrySellUnitAt(pos.Value);
        }

        // 유닛 판매 후 모든 선택 UI 패널 숨기기 (유닛 디테일, 유닛 판매, 벽 제거)
        fieldManager.HideAllSelectionPanels();
    }

    private void UpdatePosition()
    {
        if (rectTransform == null || currentUnit == null) return;
        var cam = targetCamera != null ? targetCamera : Camera.main;
        if (cam == null) return;

        if (selfCanvas != null && selfCanvas.renderMode == RenderMode.WorldSpace)
        {
            rectTransform.position = GetAnchorWorldPosition();
            return;
        }

        if (targetCanvas == null) return;
        Vector3 screenPos = GetAnchorScreenPosition(cam);
        if (screenPos.z < 0f) return;

        RectTransform parentRect = rectTransform.parent as RectTransform;
        if (parentRect == null) return;

        Camera eventCamera = targetCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : targetCanvas.worldCamera;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRect, screenPos, eventCamera, out Vector2 localPoint))
        {
            rectTransform.localPosition = new Vector3(
                localPoint.x + screenOffset.x,
                localPoint.y + screenOffset.y,
                rectTransform.localPosition.z);
        }
    }

    private Vector3 GetAnchorScreenPosition(Camera cam)
    {
        if (currentUnit == null || cam == null) return Vector3.zero;

        Vector3 worldPos = GetAnchorWorldPosition();
        Vector3 viewport = cam.WorldToViewportPoint(worldPos);
        return new Vector3(
            viewport.x * Screen.width,
            viewport.y * Screen.height,
            viewport.z);
    }

    private Vector3 GetAnchorWorldPosition()
    {
        if (currentUnit == null) return Vector3.zero;
        return currentUnit.transform.position + worldOffset;
    }

}
