// Assets/Scripts/Managers/CameraManager.cs
using UnityEngine;
using Cysharp.Threading.Tasks;

/// <summary>
/// 카메라 전환을 관리하는 매니저.
/// 플레이어 필드 간 이동, 관전 모드 등을 처리합니다.
/// </summary>
public class CameraManager : MonoBehaviour
{
    #region 싱글톤
    private static CameraManager _instance;
    public static CameraManager Instance => _instance;

    private void Awake()
    {
        if (_instance == null)
        {
            _instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }
    #endregion

    #region 필드
    [Header("카메라 참조")]
    [SerializeField] private Camera mainCamera;
    
    [Header("전환 설정")]
    [Tooltip("카메라 이동 시간 (초)")]
    [SerializeField] private float transitionDuration = 0.5f;

    [Header("필드 오프셋")]
    [Tooltip("필드 중심에서 카메라까지의 오프셋")]
    [SerializeField] private Vector3 cameraOffset = new Vector3(0f, 15f, -10f);
    
    [Tooltip("카메라 회전")]
    [SerializeField] private Vector3 cameraRotation = new Vector3(60f, 0f, 0f);

    private PlayerManager _ownField;
    private PlayerManager _currentViewingField;
    private bool _isTransitioning;
    private Vector3 _originalPosition;
    private Quaternion _originalRotation;
    #endregion

    #region 초기화
    private void Start()
    {
        if (mainCamera == null)
        {
            mainCamera = Camera.main;
        }
        
        // 원래 위치 저장
        if (mainCamera != null)
        {
            _originalPosition = mainCamera.transform.position;
            _originalRotation = mainCamera.transform.rotation;
        }
    }

    private void OnEnable()
    {
        GameEvents.OnGameStateChanged += HandleGameStateChanged;
    }

    private void OnDisable()
    {
        GameEvents.OnGameStateChanged -= HandleGameStateChanged;
    }

    /// <summary>
    /// 카메라 매니저 초기화. 본인 필드 설정.
    /// </summary>
    public void Initialize(PlayerManager ownField)
    {
        _ownField = ownField;
        _currentViewingField = ownField;
        
        // 원래 위치를 본인 필드 기준으로 설정
        SetCameraToFieldImmediate(ownField);
        _originalPosition = mainCamera.transform.position;
        _originalRotation = mainCamera.transform.rotation;
        
        Debug.Log($"<color=cyan>[CameraManager] 초기화 완료. 본인 필드: Player {ownField.playerId}</color>");
    }
    #endregion

    #region Update (입력 처리)
    private void Update()
    {
        // 스페이스바로 자기 필드 복귀
        if (Input.GetKeyDown(KeyCode.Space))
        {
            ReturnToOwnField();
        }
    }
    #endregion

    #region 카메라 이동
    /// <summary>
    /// 특정 플레이어의 필드로 카메라를 이동합니다.
    /// </summary>
    public async UniTask MoveToPlayerField(PlayerManager targetPlayer)
    {
        if (targetPlayer == null || _isTransitioning) return;
        if (_currentViewingField == targetPlayer) return;
        if (mainCamera == null) return;

        _isTransitioning = true;
        _currentViewingField = targetPlayer;

        Vector3 startPosition = mainCamera.transform.position;
        Vector3 targetPosition = GetCameraPositionForField(targetPlayer);
        
        // Lerp를 사용한 부드러운 이동
        float elapsed = 0f;
        while (elapsed < transitionDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, elapsed / transitionDuration);
            mainCamera.transform.position = Vector3.Lerp(startPosition, targetPosition, t);
            await UniTask.Yield();
        }
        
        mainCamera.transform.position = targetPosition;
        _isTransitioning = false;
        
        Debug.Log($"<color=yellow>[CameraManager] Player {targetPlayer.playerId} 필드로 이동 완료</color>");
    }

    /// <summary>
    /// 본인 필드로 카메라를 복귀합니다.
    /// </summary>
    public void ReturnToOwnField()
    {
        if (_ownField == null) return;
        if (_currentViewingField == _ownField) return;

        MoveToPlayerField(_ownField).Forget();
        Debug.Log("<color=green>[CameraManager] 본인 필드로 복귀</color>");
    }

    /// <summary>
    /// 즉시 특정 필드로 카메라를 이동합니다. (애니메이션 없음)
    /// </summary>
    public void SetCameraToFieldImmediate(PlayerManager targetPlayer)
    {
        if (targetPlayer == null || mainCamera == null) return;

        Vector3 targetPosition = GetCameraPositionForField(targetPlayer);
        mainCamera.transform.position = targetPosition;
        mainCamera.transform.rotation = Quaternion.Euler(cameraRotation);
        _currentViewingField = targetPlayer;
    }

    /// <summary>
    /// 필드 중심 기준 카메라 위치 계산
    /// </summary>
    private Vector3 GetCameraPositionForField(PlayerManager player)
    {
        if (player?.fieldManager == null) return _originalPosition;

        // 필드의 중심 계산
        Vector3 fieldCenter = player.fieldManager.gridOrigin + 
            new Vector3(
                player.fieldManager.gridSize.x * player.fieldManager.cellSize * 0.5f,
                0f,
                player.fieldManager.gridSize.y * player.fieldManager.cellSize * 0.5f
            );

        return fieldCenter + cameraOffset;
    }
    #endregion

    #region 이벤트 핸들러
    private void HandleGameStateChanged(GameManagers.GameState newState)
    {
        // 전투 종료 시 자동으로 본인 필드로 복귀
        if (newState == GameManagers.GameState.Prepare)
        {
            ReturnToOwnField();
        }
    }
    #endregion

    #region 공개 프로퍼티
    public bool IsViewingOwnField => _currentViewingField == _ownField;
    public PlayerManager CurrentViewingField => _currentViewingField;
    public PlayerManager OwnField => _ownField;
    #endregion
}
