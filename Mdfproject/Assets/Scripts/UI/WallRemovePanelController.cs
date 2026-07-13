using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

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
    private bool _removeRequested;

    private void Awake()
    {
        _rectTransform = GetComponent<RectTransform>();
        _selfCanvas = GetComponent<Canvas>();
    }

    private void OnEnable()
    {
        if (!ActiveControllers.Contains(this))
        {
            ActiveControllers.Add(this);
        }

        GameEvents.OnGameStateChanged += HandleGameStateChanged;
    }

    private void OnDisable()
    {
        if (removeButton != null)
        {
            removeButton.onClick.RemoveListener(OnRemoveButtonClicked);
        }
        GameEvents.OnGameStateChanged -= HandleGameStateChanged;
        ActiveControllers.Remove(this);
        _currentWall = null;
        _fieldManager = null;
        _targetCanvas = null;
        _targetCamera = null;
        _removeRequested = false;
    }

    private void Update()
    {
        if (MdfInput.PrimaryPointerWasReleasedThisFrame() && IsPointerOverRemoveButton(MdfInput.PointerPosition))
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
        _currentWall = wall;
        _wallGridPosition = gridPosition;
        _fieldManager = manager;
        _targetCanvas = UIManagers.Instance != null ? UIManagers.Instance.mainCanvas : null;
        _targetCamera = manager != null ? manager.PlayerCamera : Camera.main;
        _removeRequested = false;

        if (_selfCanvas != null && _selfCanvas.renderMode == RenderMode.WorldSpace)
        {
            _selfCanvas.worldCamera = _targetCamera;
            if (overrideSorting)
            {
                _selfCanvas.overrideSorting = true;
                _selfCanvas.sortingOrder = sortingOrder;
            }
        }

        ApplyBillboardCamera(_targetCamera);
        HookRemoveButton();
        UpdateRemoveSection();
        UpdatePosition();
    }

    private void HandleGameStateChanged(GameManagers.GameState newState)
    {
        UpdateRemoveSection();
    }

    private void HookRemoveButton()
    {
        if (removeButton == null) return;
        removeButton.onClick.RemoveListener(OnRemoveButtonClicked);
        removeButton.onClick.AddListener(OnRemoveButtonClicked);
    }

    private void UpdateRemoveSection()
    {
        if (removeButton != null)
        {
            removeButton.interactable = CanRemoveCurrentWall();
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
        if (_removeRequested)
        {
            return;
        }

        if (!CanRemoveCurrentWall()) return;
        _removeRequested = true;

        var commandProcessor = GameManagers.Instance != null
            ? GameManagers.Instance.CommandProcessor
            : null;
        if (commandProcessor != null)
        {
            var command = new RemoveWallCommand(_fieldManager.playerManager.playerId, _wallGridPosition);
            commandProcessor.RequestCommandExecution(command);
        }
        else
        {
            // Durable wall state is authority-owned; never mutate or refund directly from UI.
            _removeRequested = false;
            return;
        }

        // 벽 제거 후 모든 선택 UI 패널 숨기기 (유닛 디테일, 유닛 판매, 벽 제거)
        _fieldManager.HideAllSelectionPanels();
    }

    public static bool IsPointerOverActiveRemoveButton(Vector2 screenPosition)
    {
        for (int i = ActiveControllers.Count - 1; i >= 0; i--)
        {
            var controller = ActiveControllers[i];
            if (controller == null)
            {
                ActiveControllers.RemoveAt(i);
                continue;
            }

            if (controller.isActiveAndEnabled && controller.IsPointerOverRemoveButton(screenPosition))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsPointerOverRemoveButton(Vector2 screenPosition)
    {
        if (removeButton == null || !removeButton.gameObject.activeInHierarchy)
        {
            return false;
        }

        var buttonRect = removeButton.transform as RectTransform;
        if (buttonRect == null)
        {
            return false;
        }

        Camera eventCamera = null;
        Canvas buttonCanvas = removeButton.GetComponentInParent<Canvas>();
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
