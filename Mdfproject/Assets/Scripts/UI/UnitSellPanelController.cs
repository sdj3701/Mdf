using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public class UnitSellPanelController : MonoBehaviour
{
    private static readonly List<UnitSellPanelController> ActiveControllers = new List<UnitSellPanelController>(4);
    private static readonly Vector3[] ButtonWorldCorners = new Vector3[4];

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
    private Canvas[] presentationCanvases;
    private bool sellRequested;
    private int lastSellDispatchFrame = -1;
    private int capturedPointerId = -1;
    private int fallbackCaptureBlockedThroughFrame = -1;

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        selfCanvas = GetComponent<Canvas>();
        presentationCanvases = GetComponentsInChildren<Canvas>(true);
    }

    private void OnEnable()
    {
        fallbackCaptureBlockedThroughFrame = Time.frameCount;
        if (!ActiveControllers.Contains(this))
        {
            ActiveControllers.Add(this);
        }

        GameEvents.OnGameStateChanged += HandleGameStateChanged;
    }

    private void OnDisable()
    {
        if (sellButton != null)
        {
            sellButton.onClick.RemoveListener(OnSellButtonClicked);
        }
        GameEvents.OnGameStateChanged -= HandleGameStateChanged;
        ActiveControllers.Remove(this);
        currentUnit = null;
        fieldManager = null;
        targetCanvas = null;
        targetCamera = null;
        sellRequested = false;
        lastSellDispatchFrame = -1;
        capturedPointerId = -1;
        fallbackCaptureBlockedThroughFrame = -1;
    }

    private void Update()
    {
        if (MdfInput.TryGetPrimaryPointerPressThisFrame(out int pressedPointerId, out Vector2 pressedPosition))
        {
            capturedPointerId = Time.frameCount > fallbackCaptureBlockedThroughFrame &&
                                CanCaptureFallback(pressedPosition)
                ? pressedPointerId
                : -1;
        }

        if (!MdfInput.TryGetPrimaryPointerReleaseThisFrame(
                out int releasedPointerId,
                out Vector2 releasedPosition))
        {
            return;
        }

        bool shouldDispatch = capturedPointerId >= 0 &&
                              capturedPointerId == releasedPointerId &&
                              CanCaptureFallback(releasedPosition);
        capturedPointerId = -1;
        if (shouldDispatch)
        {
            OnSellButtonClicked();
        }
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
        sellRequested = false;
        lastSellDispatchFrame = -1;
        capturedPointerId = -1;
        fallbackCaptureBlockedThroughFrame = Time.frameCount;
        targetCanvas = UIManagers.Instance != null ? UIManagers.Instance.mainCanvas : null;
        targetCamera = manager != null ? manager.PlayerCamera : Camera.main;
        ApplyCanvasCamera(targetCamera);
        ApplyBillboardCamera(targetCamera);
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
            sellButton.interactable = !sellRequested && CanSellCurrentUnit();
        }
    }

    private bool CanSellCurrentUnit()
    {
        if (currentUnit == null || fieldManager == null || fieldManager.playerManager == null) return false;
        var gm = GameManagers.Instance;
        if (gm != null && (gm.GetGameState() != GameManagers.GameState.Prepare || gm.IsSequenceTransitioning))
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
        int dispatchFrame = Time.frameCount;
        if (sellRequested || lastSellDispatchFrame == dispatchFrame || !CanSellCurrentUnit()) return;
        var pos = fieldManager.GetUnitPosition(currentUnit);
        if (!pos.HasValue) return;

        // A world-space GraphicRaycaster can miss this panel while UI Toolkit is active. The
        // release fallback below and Button.onClick may both arrive in one frame, so reserve the
        // action before dispatching the authority-owned command.
        lastSellDispatchFrame = dispatchFrame;
        sellRequested = true;
        UpdateSellSection();
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

    public static bool IsPointerOverActiveSellButton(Vector2 screenPosition)
    {
        for (int i = ActiveControllers.Count - 1; i >= 0; i--)
        {
            UnitSellPanelController controller = ActiveControllers[i];
            if (controller == null)
            {
                ActiveControllers.RemoveAt(i);
                continue;
            }

            if (controller.isActiveAndEnabled &&
                controller.IsPointerOverButton(controller.sellButton, screenPosition))
            {
                return true;
            }
        }

        return false;
    }

    private bool CanCaptureFallback(Vector2 screenPosition)
    {
        return sellButton != null &&
               sellButton.interactable &&
               IsPointerOverButton(sellButton, screenPosition) &&
               !WallRemovePanelController.IsPointerOverActiveActionButton(screenPosition) &&
               MdfInput.IsTopmostVisibleUiTarget(sellButton.gameObject, screenPosition);
    }

    private void ApplyCanvasCamera(Camera camera)
    {
        if (presentationCanvases == null || presentationCanvases.Length == 0)
        {
            presentationCanvases = GetComponentsInChildren<Canvas>(true);
        }

        for (int i = 0; i < presentationCanvases.Length; i++)
        {
            Canvas canvas = presentationCanvases[i];
            if (canvas != null && canvas.renderMode == RenderMode.WorldSpace)
            {
                canvas.worldCamera = camera;
            }
        }

        if (selfCanvas != null && selfCanvas.renderMode == RenderMode.WorldSpace && overrideSorting)
        {
            selfCanvas.overrideSorting = true;
            selfCanvas.sortingOrder = sortingOrder;
        }
    }

    private bool IsPointerOverButton(Button button, Vector2 screenPosition)
    {
        if (button == null || !button.gameObject.activeInHierarchy)
        {
            return false;
        }

        RectTransform buttonRect = button.transform as RectTransform;
        if (buttonRect == null)
        {
            return false;
        }

        Camera eventCamera = null;
        Canvas buttonCanvas = button.GetComponentInParent<Canvas>();
        if (buttonCanvas != null && buttonCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
        {
            eventCamera = buttonCanvas.worldCamera != null ? buttonCanvas.worldCamera : targetCamera;
        }

        if (RectTransformUtility.RectangleContainsScreenPoint(buttonRect, screenPosition, eventCamera))
        {
            return true;
        }

        if (eventCamera != null)
        {
            return false;
        }

        buttonRect.GetWorldCorners(ButtonWorldCorners);
        float minX = Mathf.Min(ButtonWorldCorners[0].x, ButtonWorldCorners[2].x);
        float maxX = Mathf.Max(ButtonWorldCorners[0].x, ButtonWorldCorners[2].x);
        float minY = Mathf.Min(ButtonWorldCorners[0].y, ButtonWorldCorners[2].y);
        float maxY = Mathf.Max(ButtonWorldCorners[0].y, ButtonWorldCorners[2].y);
        return screenPosition.x >= minX && screenPosition.x <= maxX &&
               screenPosition.y >= minY && screenPosition.y <= maxY;
    }

    private void UpdatePosition()
    {
        if (rectTransform == null || currentUnit == null) return;
        var cam = targetCamera != null ? targetCamera : Camera.main;
        if (cam == null) return;

        if (selfCanvas != null && selfCanvas.renderMode == RenderMode.WorldSpace)
        {
            rectTransform.SetPositionAndRotation(GetAnchorWorldPosition(), cam.transform.rotation);
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

    private void ApplyBillboardCamera(Camera camera)
    {
        var billboards = GetComponentsInChildren<UIBillboard>(true);
        foreach (var billboard in billboards)
        {
            if (billboard != null)
            {
                billboard.SetCamera(camera);
            }
        }
    }

}
