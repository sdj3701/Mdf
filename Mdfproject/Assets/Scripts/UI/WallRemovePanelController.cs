using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using TMPro;

/// <summary>
/// 벽 제거 패널 컨트롤러. 벽을 선택했을 때 제거 버튼을 표시합니다.
/// UnitSellPanelController와 유사한 구조로 월드 스페이스 캔버스를 사용합니다.
/// </summary>
public class WallRemovePanelController : MonoBehaviour
{
    private static readonly List<WallRemovePanelController> ActiveControllers = new List<WallRemovePanelController>(4);
    private static readonly Vector3[] ButtonWorldCorners = new Vector3[4];

    [Header("UI")]
    [SerializeField] private Button removeButton;
    [SerializeField] private Button upgradeButton;
    [SerializeField] private TMP_Text upgradeLabel;
    [SerializeField] private Vector3 worldOffset = new Vector3(0.5f, 2f, 0f);
    [SerializeField] private Vector2 screenOffset = Vector2.zero;
    [SerializeField] private bool overrideSorting = true;
    [SerializeField] private int sortingOrder = 300;

    private GameObject _currentWall;
    private Vector3Int _wallGridPosition;
    private FieldManager _fieldManager;
    private Canvas _targetCanvas;
    private Camera _targetCamera;
    private RectTransform _rectTransform;
    private Canvas _selfCanvas;
    private CanvasGroup _actionInputCanvasGroup;
    private Canvas[] _presentationCanvases;
    private bool _removeRequested;
    private bool _upgradeRequested;
    private int _lastRemoveDispatchFrame = -1;
    private int _lastUpgradeDispatchFrame = -1;
    private int _requestedUpgradeLevel;
    private float _upgradeRequestDeadline;
    private DestructibleWall _currentDestructibleWall;
    private RectTransform _removeButtonRect;
    private Vector2 _removeWithUpgradePosition;
    private Button _capturedFallbackButton;
    private int _capturedPointerId = -1;
    private int _fallbackCaptureBlockedThroughFrame = -1;
    private bool _actionsAwaitingNextFrame;
    private bool _actionsAwaitingPointerRelease;
    private int _actionRaycastEnableAfterFrame = -1;

    private void Awake()
    {
        _rectTransform = GetComponent<RectTransform>();
        _selfCanvas = GetComponent<Canvas>();
        _actionInputCanvasGroup = GetComponent<CanvasGroup>();
        if (_actionInputCanvasGroup == null)
        {
            _actionInputCanvasGroup = gameObject.AddComponent<CanvasGroup>();
        }
        _presentationCanvases = GetComponentsInChildren<Canvas>(true);
        _removeButtonRect = removeButton != null ? removeButton.transform as RectTransform : null;
        if (_removeButtonRect != null)
        {
            _removeWithUpgradePosition = _removeButtonRect.anchoredPosition;
        }
    }

    private void OnEnable()
    {
        BeginActionInputGate();
        if (!ActiveControllers.Contains(this))
        {
            ActiveControllers.Add(this);
        }

        GameEvents.OnGameStateChanged += HandleGameStateChanged;
        GameEvents.OnPlayerStatsChanged += HandlePlayerStatsChanged;
    }

    private void OnDisable()
    {
        if (removeButton != null)
        {
            removeButton.onClick.RemoveListener(OnRemoveButtonClicked);
        }
        if (upgradeButton != null)
        {
            upgradeButton.onClick.RemoveListener(OnUpgradeButtonClicked);
        }
        UnsubscribeFromCurrentWall();
        GameEvents.OnGameStateChanged -= HandleGameStateChanged;
        GameEvents.OnPlayerStatsChanged -= HandlePlayerStatsChanged;
        ActiveControllers.Remove(this);
        _currentWall = null;
        _fieldManager = null;
        _targetCanvas = null;
        _targetCamera = null;
        _removeRequested = false;
        _upgradeRequested = false;
        _lastRemoveDispatchFrame = -1;
        _lastUpgradeDispatchFrame = -1;
        _capturedFallbackButton = null;
        _capturedPointerId = -1;
        _fallbackCaptureBlockedThroughFrame = -1;
        _actionsAwaitingNextFrame = false;
        _actionsAwaitingPointerRelease = false;
        _actionRaycastEnableAfterFrame = -1;
        SetActionRaycastBlocking(true);
    }

    private void Update()
    {
        if (_actionsAwaitingPointerRelease && !MdfInput.PrimaryPointerIsPressed())
        {
            // Keep raycasts blocked for the entire release frame. Otherwise EventSystem can
            // reuse the press that selected the wall as a click on this newly opened panel.
            _actionsAwaitingPointerRelease = false;
            _actionRaycastEnableAfterFrame = Time.frameCount;
        }

        int actionGateFrame = Mathf.Max(
            _fallbackCaptureBlockedThroughFrame,
            _actionRaycastEnableAfterFrame);
        if (_actionsAwaitingNextFrame &&
            !_actionsAwaitingPointerRelease &&
            Time.frameCount > actionGateFrame)
        {
            _actionsAwaitingNextFrame = false;
            SetActionRaycastBlocking(true);
            UpdateActionSection();
        }

        if (_upgradeRequested && Time.unscaledTime >= _upgradeRequestDeadline)
        {
            _upgradeRequested = false;
            UpdateActionSection();
        }

        if (MdfInput.TryGetPrimaryPointerPressThisFrame(out int pressedPointerId, out Vector2 pressedPosition))
        {
            _capturedFallbackButton = Time.frameCount > _fallbackCaptureBlockedThroughFrame
                ? ResolveFallbackButton(pressedPosition)
                : null;
            _capturedPointerId = _capturedFallbackButton != null
                ? pressedPointerId
                : -1;
        }

        if (!MdfInput.TryGetPrimaryPointerReleaseThisFrame(
                out int releasedPointerId,
                out Vector2 releasedPosition))
        {
            return;
        }

        Button capturedButton = _capturedFallbackButton;
        bool shouldDispatch = capturedButton != null &&
                              _capturedPointerId == releasedPointerId &&
                              IsFallbackButtonAvailable(capturedButton, releasedPosition);
        _capturedFallbackButton = null;
        _capturedPointerId = -1;
        if (!shouldDispatch)
        {
            return;
        }

        if (capturedButton == upgradeButton)
        {
            OnUpgradeButtonClicked();
        }
        else if (capturedButton == removeButton)
        {
            OnRemoveButtonClicked();
        }
    }

    private void LateUpdate()
    {
        if (_currentWall == null || _fieldManager == null) return;
        UpdatePosition();
    }

    /// <summary>
    /// 벽과 필드 매니저를 바인딩하고 UI를 초기화합니다.
    /// </summary>
    public void Bind(GameObject wall, Vector3Int gridPosition, FieldManager manager)
    {
        UnsubscribeFromCurrentWall();
        _currentWall = wall;
        _currentDestructibleWall = wall != null ? wall.GetComponent<DestructibleWall>() : null;
        if (_currentDestructibleWall != null)
        {
            _currentDestructibleWall.OnUpgradeChanged += HandleWallUpgradeChanged;
        }
        _wallGridPosition = gridPosition;
        _fieldManager = manager;
        _targetCanvas = UIManagers.Instance != null ? UIManagers.Instance.mainCanvas : null;
        _targetCamera = manager != null ? manager.PlayerCamera : Camera.main;
        _removeRequested = false;
        _upgradeRequested = false;
        _lastRemoveDispatchFrame = -1;
        _lastUpgradeDispatchFrame = -1;
        _capturedFallbackButton = null;
        _capturedPointerId = -1;
        BeginActionInputGate();

        ApplyCanvasCamera(_targetCamera);

        ApplyBillboardCamera(_targetCamera);
        HookButtons();
        UpdateActionSection();
        UpdatePosition();
    }

    private void HandleGameStateChanged(GameManagers.GameState newState)
    {
        UpdateActionSection();
    }

    private void HandlePlayerStatsChanged(int playerId, int health, int gold)
    {
        if (_fieldManager != null && _fieldManager.playerManager != null &&
            _fieldManager.playerManager.playerId == playerId)
        {
            UpdateActionSection();
        }
    }

    private void HandleWallUpgradeChanged(int level, int investedGold)
    {
        if (_upgradeRequested && level != _requestedUpgradeLevel)
        {
            _upgradeRequested = false;
        }
        UpdateActionSection();
    }

    private void HookButtons()
    {
        if (removeButton != null)
        {
            removeButton.onClick.RemoveListener(OnRemoveButtonClicked);
            removeButton.onClick.AddListener(OnRemoveButtonClicked);
        }
        if (upgradeButton != null)
        {
            upgradeButton.onClick.RemoveListener(OnUpgradeButtonClicked);
            upgradeButton.onClick.AddListener(OnUpgradeButtonClicked);
        }
    }

    private void UpdateActionSection()
    {
        bool interactionArmed = !_actionsAwaitingNextFrame &&
                                Time.frameCount > _fallbackCaptureBlockedThroughFrame;
        bool canRemove = interactionArmed && CanRemoveCurrentWall();

        bool isUpgradeableWall = _currentDestructibleWall != null;
        if (upgradeButton != null)
        {
            upgradeButton.gameObject.SetActive(isUpgradeableWall);
        }
        if (_removeButtonRect != null)
        {
            _removeButtonRect.anchoredPosition = isUpgradeableWall
                ? _removeWithUpgradePosition
                : new Vector2(0f, _removeWithUpgradePosition.y);
        }
        if (!isUpgradeableWall)
        {
            SetActionButtonsInteractable(canRemove, false);
            return;
        }

        int currentLevel = _currentDestructibleWall.CurrentLevel;
        bool hasQuote = _currentDestructibleWall.TryGetUpgradeQuote(
            currentLevel,
            out int nextLevel,
            out int cost,
            out _);
        PlayerManager owner = _fieldManager != null ? _fieldManager.playerManager : null;
        bool canUpgrade = interactionArmed && hasQuote && !_upgradeRequested && canRemove &&
                          owner != null && owner.GetGold() >= cost;
        SetActionButtonsInteractable(canRemove, canUpgrade);
        if (upgradeLabel != null)
        {
            upgradeLabel.text = hasQuote
                ? $"UP {currentLevel}>{nextLevel}\n{cost}"
                : $"Lv.{currentLevel} MAX";
        }
    }

    private bool CanRemoveCurrentWall()
    {
        if (_currentWall == null || _fieldManager == null || _fieldManager.playerManager == null) return false;

        var gm = GameManagers.Instance;
        if (gm != null && (gm.GetGameState() != GameManagers.GameState.Prepare || gm.IsSequenceTransitioning))
        {
            return false;
        }

        if (!_fieldManager.IsLocalControlledField())
        {
            return false;
        }

        // 벽이 아직 존재하는지 확인
        return _fieldManager.GetRemovableWallObjectAt(_wallGridPosition) == _currentWall;
    }

    private void OnRemoveButtonClicked()
    {
        int dispatchFrame = Time.frameCount;
        if (_removeRequested || _lastRemoveDispatchFrame == dispatchFrame)
        {
            return;
        }

        if (!CanRemoveCurrentWall()) return;
        var commandProcessor = GameManagers.Instance != null
            ? GameManagers.Instance.CommandProcessor
            : null;
        if (commandProcessor == null)
        {
            // Durable wall state is authority-owned; never mutate or refund directly from UI.
            return;
        }

        // 벽 제거 후 모든 선택 UI 패널 숨기기 (유닛 디테일, 유닛 판매, 벽 제거)
        // World-space fallback input and Unity's Button.onClick can both fire for one release.
        // Keep this guard even when the authority completes the command within the same frame.
        _lastRemoveDispatchFrame = dispatchFrame;
        _removeRequested = true;
        var command = new RemoveWallCommand(_fieldManager.playerManager.playerId, _wallGridPosition);
        commandProcessor.RequestCommandExecution(command);
        _fieldManager.HideAllSelectionPanels();
    }

    private void OnUpgradeButtonClicked()
    {
        int dispatchFrame = Time.frameCount;
        if (_upgradeRequested || _lastUpgradeDispatchFrame == dispatchFrame ||
            _currentDestructibleWall == null)
        {
            return;
        }

        int expectedLevel = _currentDestructibleWall.CurrentLevel;
        if (!_currentDestructibleWall.TryGetUpgradeQuote(expectedLevel, out _, out int cost, out _))
        {
            return;
        }
        PlayerManager owner = _fieldManager != null ? _fieldManager.playerManager : null;
        if (!CanRemoveCurrentWall() || owner == null || owner.GetGold() < cost)
        {
            return;
        }

        CommandProcessor commandProcessor = GameManagers.Instance != null
            ? GameManagers.Instance.CommandProcessor
            : null;
        if (commandProcessor == null)
        {
            return;
        }

        // The command may finish synchronously on the host and clear _upgradeRequested before
        // Button.onClick runs. The frame gate still prevents one release from buying two levels.
        _lastUpgradeDispatchFrame = dispatchFrame;
        _upgradeRequested = true;
        _requestedUpgradeLevel = expectedLevel;
        _upgradeRequestDeadline = Time.unscaledTime + 2f;
        UpdateActionSection();
        commandProcessor.RequestCommandExecution(
            new UpgradeWallCommand(owner.playerId, _wallGridPosition, expectedLevel));
    }

    public static bool IsPointerOverActiveRemoveButton(Vector2 screenPosition)
    {
        return IsPointerOverActiveActionButton(screenPosition);
    }

    public static bool IsPointerOverActiveActionButton(Vector2 screenPosition)
    {
        for (int i = ActiveControllers.Count - 1; i >= 0; i--)
        {
            var controller = ActiveControllers[i];
            if (controller == null)
            {
                ActiveControllers.RemoveAt(i);
                continue;
            }

            if (controller.isActiveAndEnabled &&
                (controller.IsPointerOverButton(controller.removeButton, screenPosition) ||
                 controller.IsPointerOverButton(controller.upgradeButton, screenPosition)))
            {
                return true;
            }
        }

        return false;
    }

    private Button ResolveFallbackButton(Vector2 screenPosition)
    {
        if (IsFallbackButtonAvailable(upgradeButton, screenPosition))
        {
            return upgradeButton;
        }

        return IsFallbackButtonAvailable(removeButton, screenPosition)
            ? removeButton
            : null;
    }

    private bool IsFallbackButtonAvailable(Button button, Vector2 screenPosition)
    {
        return button != null &&
               button.interactable &&
               IsPointerOverButton(button, screenPosition) &&
               MdfInput.IsTopmostVisibleUiTarget(button.gameObject, screenPosition);
    }

    private void ApplyCanvasCamera(Camera camera)
    {
        if (_presentationCanvases == null || _presentationCanvases.Length == 0)
        {
            _presentationCanvases = GetComponentsInChildren<Canvas>(true);
        }

        for (int i = 0; i < _presentationCanvases.Length; i++)
        {
            Canvas canvas = _presentationCanvases[i];
            if (canvas != null && canvas.renderMode == RenderMode.WorldSpace)
            {
                canvas.worldCamera = camera;
            }
        }

        if (_selfCanvas != null && _selfCanvas.renderMode == RenderMode.WorldSpace && overrideSorting)
        {
            _selfCanvas.overrideSorting = true;
            _selfCanvas.sortingOrder = sortingOrder;
        }
    }

    private void SetActionButtonsInteractable(bool canRemove, bool canUpgrade)
    {
        if (removeButton != null)
        {
            removeButton.interactable = canRemove;
        }
        if (upgradeButton != null)
        {
            upgradeButton.interactable = canUpgrade;
        }
    }

    private void BeginActionInputGate()
    {
        _fallbackCaptureBlockedThroughFrame = Time.frameCount;
        _actionsAwaitingNextFrame = true;
        _actionsAwaitingPointerRelease = MdfInput.PrimaryPointerIsPressed();
        _actionRaycastEnableAfterFrame = Time.frameCount;
        SetActionRaycastBlocking(false);
        SetActionButtonsInteractable(false, false);
    }

    private void SetActionRaycastBlocking(bool blocksRaycasts)
    {
        if (_actionInputCanvasGroup != null)
        {
            _actionInputCanvasGroup.blocksRaycasts = blocksRaycasts;
        }
    }

    private bool IsPointerOverButton(Button button, Vector2 screenPosition)
    {
        if (button == null || !button.gameObject.activeInHierarchy)
        {
            return false;
        }

        var buttonRect = button.transform as RectTransform;
        if (buttonRect == null)
        {
            return false;
        }

        Camera eventCamera = null;
        Canvas buttonCanvas = button.GetComponentInParent<Canvas>();
        if (buttonCanvas != null && buttonCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
        {
            eventCamera = buttonCanvas.worldCamera != null ? buttonCanvas.worldCamera : _targetCamera;
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

    private void UnsubscribeFromCurrentWall()
    {
        if (_currentDestructibleWall != null)
        {
            _currentDestructibleWall.OnUpgradeChanged -= HandleWallUpgradeChanged;
        }
        _currentDestructibleWall = null;
    }

    private void UpdatePosition()
    {
        if (_rectTransform == null || _currentWall == null) return;
        var cam = _targetCamera != null ? _targetCamera : Camera.main;
        if (cam == null) return;

        if (_selfCanvas != null && _selfCanvas.renderMode == RenderMode.WorldSpace)
        {
            _rectTransform.SetPositionAndRotation(GetAnchorWorldPosition(), cam.transform.rotation);
            return;
        }

        if (_targetCanvas == null) return;
        Vector3 screenPos = GetAnchorScreenPosition(cam);
        if (screenPos.z < 0f) return;

        RectTransform parentRect = _rectTransform.parent as RectTransform;
        if (parentRect == null) return;

        Camera eventCamera = _targetCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _targetCanvas.worldCamera;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRect, screenPos, eventCamera, out Vector2 localPoint))
        {
            _rectTransform.localPosition = new Vector3(
                localPoint.x + screenOffset.x,
                localPoint.y + screenOffset.y,
                _rectTransform.localPosition.z);
        }
    }

    private Vector3 GetAnchorScreenPosition(Camera cam)
    {
        if (_currentWall == null || cam == null) return Vector3.zero;

        Vector3 worldPos = GetAnchorWorldPosition();
        Vector3 viewport = cam.WorldToViewportPoint(worldPos);
        return new Vector3(
            viewport.x * Screen.width,
            viewport.y * Screen.height,
            viewport.z);
    }

    private Vector3 GetAnchorWorldPosition()
    {
        if (_currentWall == null) return Vector3.zero;
        if (_fieldManager != null && _fieldManager.IsValidGridPosition(_wallGridPosition))
        {
            return _fieldManager.GetWallRootWorldPosition(_wallGridPosition) + worldOffset;
        }

        return _currentWall.transform.position + worldOffset;
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
