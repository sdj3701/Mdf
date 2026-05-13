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

    [Header("수비 모드 카메라 (기본)")]
    [Tooltip("필드 중심에서 카메라까지의 오프셋 (GetPlayerCamera와 동일)")]
    [SerializeField] private Vector3 defenseOffset = new Vector3(4f, 15f, -12f);
    
    [Tooltip("카메라 회전 (수비)")]
    [SerializeField] private Vector3 defenseRotation = new Vector3(45f, 0f, 0f);

    [Header("공격 모드 카메라 (상대 필드 조망)")]
    [Tooltip("공격 시 카메라 오프셋 (더 멀리)")]
    [SerializeField] private Vector3 attackOffset = new Vector3(0f, 25f, -18f);
    
    [Tooltip("카메라 회전 (공격 - 더 위에서 내려다봄)")]
    [SerializeField] private Vector3 attackRotation = new Vector3(55f, 0f, 0f);

    private PlayerManager _ownField;
    private PlayerManager _currentViewingField;
    private bool _isTransitioning;
    private bool _isAttackMode;
    private Vector3 _originalPosition;  // 자신의 필드를 보는 수비 모드 카메라 위치
    private Quaternion _originalRotation;  // 자신의 필드를 보는 수비 모드 카메라 회전
    private Vector3 _ownFieldCenter;  // 본인 필드 중심 위치
    
    [Header("필드 간격 설정")]
    [Tooltip("플레이어 간 필드 Z 오프셋 (GameManagers의 Player Offset.z와 동일해야 함)")]
    [SerializeField] private float fieldZOffset = -14f;
    #endregion

    #region 안전 유틸
    private static bool IsPlayerReadable(PlayerManager player)
    {
        return player != null
            && player.Object != null
            && player.Object.IsValid;
    }

    private static bool TryGetPlayerId(PlayerManager player, out int playerId)
    {
        playerId = -1;
        if (!IsPlayerReadable(player))
        {
            return false;
        }

        try
        {
            playerId = player.playerId;
            return true;
        }
        catch (System.InvalidOperationException)
        {
            return false;
        }
    }

    private bool TryRebindOwnField(string context)
    {
        if (IsPlayerReadable(_ownField))
        {
            return true;
        }

        var candidate = GameManagers.Instance?.localPlayer;
        if (!IsPlayerReadable(candidate))
        {
            return false;
        }

        _ownField = candidate;
        if (!IsPlayerReadable(_currentViewingField))
        {
            _currentViewingField = candidate;
        }

        // Debug.Log($"[CameraManager] ownField 재바인딩 완료 ({context})");
        return true;
    }
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
    /// 씬 카메라 위치(호스트 필드 기준)에서 playerId * fieldZOffset만큼 z값을 이동합니다.
    /// </summary>
    public void Initialize(PlayerManager ownField)
    {
        _ownField = ownField;
        _currentViewingField = ownField;
        
        // 본인 필드 중심 위치 계산
        _ownFieldCenter = GetFieldCenter(ownField);
        
        if (mainCamera != null)
        {
            // 씬 카메라 위치 (호스트 필드 기준)
            Vector3 sceneCameraPos = mainCamera.transform.position;
            Quaternion sceneCameraRot = mainCamera.transform.rotation;
            
            // playerId에 따라 z 오프셋 계산
            // Player 0: z값 변화 없음
            // Player 1: z값 -14
            // Player 2: z값 -28
            int ownId = 0;
            if (!TryGetPlayerId(ownField, out ownId))
            {
                ownId = 0;
            }
            float zOffset = ownId * fieldZOffset;
            
            // 자신의 필드로 카메라 이동
            _originalPosition = sceneCameraPos + new Vector3(0f, 0f, zOffset);
            _originalRotation = sceneCameraRot;
            
            mainCamera.transform.position = _originalPosition;
            mainCamera.transform.rotation = _originalRotation;
            
            // Debug.Log($"<color=cyan>[CameraManager] 초기화 완료. Player {ownId}, " +
                // $"z오프셋: {zOffset}, 카메라 위치: {_originalPosition}</color>");
        }
    }
    #endregion

    #region Update (입력 처리)
    private void Update()
    {
        // 스페이스바로 자기 필드 복귀
        if (MdfInput.GetKeyDown(KeyCode.Space))
        {
            ReturnToOwnField();
        }
    }
    #endregion

    #region 카메라 이동
    /// <summary>
    /// 특정 플레이어의 필드로 카메라를 이동합니다.
    /// 인스펙터에서 설정한 원래 위치를 기준으로, 필드 간 위치 차이만 오프셋으로 적용합니다.
    /// </summary>
    /// <param name="targetPlayer">대상 플레이어</param>
    /// <param name="isAttackMode">공격 모드 여부 (인스펙터 설정값 사용). 기본값: false (관전/수비 모드)</param>
    public async UniTask MoveToPlayerField(PlayerManager targetPlayer, bool isAttackMode = false)
    {
        if (targetPlayer == null || _isTransitioning) return;
        if (mainCamera == null) return;
        if (!TryRebindOwnField("MoveToPlayerField")) return;

        _isTransitioning = true;
        _currentViewingField = targetPlayer;
        _isAttackMode = isAttackMode;

        Vector3 startPosition = mainCamera.transform.position;
        Quaternion startRotation = mainCamera.transform.rotation;
        
        // playerId 차이로 z 오프셋 계산
        // Player 1 → Player 0: (0 - 1) * -14 = +14 (위로)
        // Player 3 → Player 1: (1 - 3) * -14 = +28 (위로)
        float zOffset;
        if (TryGetPlayerId(targetPlayer, out int targetPlayerId) && TryGetPlayerId(_ownField, out int ownPlayerId))
        {
            int playerIdDiff = targetPlayerId - ownPlayerId;
            zOffset = playerIdDiff * fieldZOffset;
        }
        else
        {
            // Host Migration 직후 Spawned 전 객체가 섞이는 구간에서는 월드 좌표 차이로 폴백한다.
            zOffset = GetFieldCenter(targetPlayer).z - GetFieldCenter(_ownField).z;
            // Debug.LogWarning($"[CameraManager] playerId 접근 불가로 월드 좌표 폴백 사용. zOffset={zOffset:F2}");
        }
        
        Vector3 targetPosition;
        Quaternion targetRotation;
        
        // 수비 모드 위치 계산 (기본)
        Vector3 defensePosition = _originalPosition + new Vector3(0f, 0f, zOffset);
        
        if (isAttackMode)
        {
            // 공격 모드: 수비 모드 위치 + (attackOffset - defenseOffset)
            Vector3 attackDiff = attackOffset - defenseOffset;
            Vector3 rotationDiff = attackRotation - defenseRotation;
            
            targetPosition = defensePosition + attackDiff;
            targetRotation = Quaternion.Euler(_originalRotation.eulerAngles + rotationDiff);
        }
        else
        {
            // 수비/관전 모드
            targetPosition = defensePosition;
            targetRotation = _originalRotation;
        }
        
        // Lerp를 사용한 부드러운 이동 + 회전
        float elapsed = 0f;
        while (elapsed < transitionDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, elapsed / transitionDuration);
            mainCamera.transform.position = Vector3.Lerp(startPosition, targetPosition, t);
            mainCamera.transform.rotation = Quaternion.Slerp(startRotation, targetRotation, t);
            await UniTask.Yield();
        }
        
        mainCamera.transform.position = targetPosition;
        mainCamera.transform.rotation = targetRotation;
        _isTransitioning = false;

        string targetIdText = TryGetPlayerId(targetPlayer, out int targetId) ? targetId.ToString() : "unknown";
        // Debug.Log($"<color=yellow>[CameraManager] Player {targetIdText} 필드로 이동 완료 (공격모드: {isAttackMode}, 위치: {targetPosition})</color>");
    }

    /// <summary>
    /// 필드 중심 좌표를 계산합니다.
    /// </summary>
    private Vector3 GetFieldCenter(PlayerManager player)
    {
        if (player?.fieldManager == null)
        {
            return player != null ? player.transform.position : Vector3.zero;
        }

        return player.fieldManager.gridOrigin + 
            new Vector3(
                player.fieldManager.gridSize.x * player.fieldManager.cellSize * 0.5f,
                0f,
                player.fieldManager.gridSize.y * player.fieldManager.cellSize * 0.5f
            );
    }

    /// <summary>
    /// 본인 필드로 카메라를 복귀합니다. (수비 모드)
    /// </summary>
    public void ReturnToOwnField()
    {
        if (!TryRebindOwnField("ReturnToOwnField")) return;
        
        _isAttackMode = false;
        MoveToPlayerField(_ownField, isAttackMode: false).Forget();
        // Debug.Log("<color=green>[CameraManager] 본인 필드로 복귀 (수비 모드)</color>");
    }

    /// <summary>
    /// 즉시 특정 필드로 카메라를 이동합니다. (애니메이션 없음)
    /// </summary>
    public void SetCameraToFieldImmediate(PlayerManager targetPlayer, bool isAttackMode = false)
    {
        if (targetPlayer == null || mainCamera == null) return;

        _isAttackMode = isAttackMode;
        Vector3 currentOffset = isAttackMode ? attackOffset : defenseOffset;
        Vector3 currentRotation = isAttackMode ? attackRotation : defenseRotation;

        Vector3 fieldCenter = GetFieldCenter(targetPlayer);
        mainCamera.transform.position = fieldCenter + currentOffset;
        mainCamera.transform.rotation = Quaternion.Euler(currentRotation);
        _currentViewingField = targetPlayer;
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
