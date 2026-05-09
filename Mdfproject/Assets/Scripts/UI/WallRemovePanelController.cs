using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 벽 제거 패널 컨트롤러. 벽을 선택했을 때 제거 버튼을 표시합니다.
/// UnitSellPanelController와 유사한 구조로 월드 스페이스 캔버스를 사용합니다.
/// </summary>
public class WallRemovePanelController : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Button removeButton;
    [SerializeField] private Vector3 worldOffset = new Vector3(0.5f, 2f, 0f);
    [SerializeField] private Vector2 screenOffset = Vector2.zero;
    [SerializeField] private bool overrideSorting = true;
    [SerializeField] private int sortingOrder = 300;

    private DestructibleWall _currentWall;
    private Vector3Int _wallGridPosition;
    private FieldManager _fieldManager;
    private Canvas _targetCanvas;
    private Camera _targetCamera;
    private RectTransform _rectTransform;
    private Canvas _selfCanvas;

    private void Awake()
    {
        _rectTransform = GetComponent<RectTransform>();
        _selfCanvas = GetComponent<Canvas>();
    }

    private void OnEnable()
    {
        GameEvents.OnGameStateChanged += HandleGameStateChanged;
    }

    private void OnDisable()
    {
        if (removeButton != null)
        {
            removeButton.onClick.RemoveListener(OnRemoveButtonClicked);
        }
        GameEvents.OnGameStateChanged -= HandleGameStateChanged;
        _currentWall = null;
        _fieldManager = null;
        _targetCanvas = null;
        _targetCamera = null;
    }

    private void LateUpdate()
    {
        if (_currentWall == null || _fieldManager == null) return;
        UpdatePosition();
    }

    /// <summary>
    /// 벽과 필드 매니저를 바인딩하고 UI를 초기화합니다.
    /// </summary>
    public void Bind(DestructibleWall wall, Vector3Int gridPosition, FieldManager manager)
    {
        _currentWall = wall;
        _wallGridPosition = gridPosition;
        _fieldManager = manager;
        _targetCanvas = UIManagers.Instance != null ? UIManagers.Instance.mainCanvas : null;
        _targetCamera = manager != null ? manager.PlayerCamera : Camera.main;

        if (_selfCanvas != null && _selfCanvas.renderMode == RenderMode.WorldSpace)
        {
            _selfCanvas.worldCamera = _targetCamera;
            if (overrideSorting)
            {
                _selfCanvas.overrideSorting = true;
                _selfCanvas.sortingOrder = sortingOrder;
            }
        }

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
        return _fieldManager.GetWallAt(_wallGridPosition) != null;
    }

    private void OnRemoveButtonClicked()
    {
        if (!CanRemoveCurrentWall()) return;

        var command = new RemoveWallCommand(_fieldManager.playerManager.playerId, _wallGridPosition);
        if (GameManagers.Instance != null && GameManagers.Instance.CommandProcessor != null)
        {
            GameManagers.Instance.CommandProcessor.RequestCommandExecution(command);
        }
        else
        {
            // CommandProcessor가 없는 경우 직접 실행 (싱글플레이어 폴백)
            _fieldManager.RemoveWallAt(_wallGridPosition);
            _fieldManager.playerManager.ReturnWall();
        }

        // 벽 제거 후 모든 선택 UI 패널 숨기기 (유닛 디테일, 유닛 판매, 벽 제거)
        _fieldManager.HideAllSelectionPanels();
    }

    private void UpdatePosition()
    {
        if (_rectTransform == null || _currentWall == null) return;
        var cam = _targetCamera != null ? _targetCamera : Camera.main;
        if (cam == null) return;

        if (_selfCanvas != null && _selfCanvas.renderMode == RenderMode.WorldSpace)
        {
            _rectTransform.position = GetAnchorWorldPosition();
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
        return _currentWall.transform.position + worldOffset;
    }
}
