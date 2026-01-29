// Assets/Scripts/Network/HostMigrationHandler.cs
// Host Migration 처리를 담당하는 핸들러 클래스
// 오브젝트 유지 방식: Runner를 종료하지 않고 Fusion이 자동으로 State Authority를 이전

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Fusion;

/// <summary>
/// Host Migration 중 저장되는 게임 상태 데이터
/// </summary>
[Serializable]
public struct GameMigrationData
{
    public int CurrentRound;
    public int GameStateValue;
    public float RemainingPhaseTime;
    public string CurrentSceneName;
}

/// <summary>
/// Host Migration 중 저장되는 플레이어 데이터
/// </summary>
[Serializable]
public struct PlayerMigrationData
{
    public int PlayerId;
    public int Gold;
    public int Health;
    public string ConnectionToken;
    public bool IsAI;
}

/// <summary>
/// Photon Fusion 2 Host Migration을 처리하는 핸들러 클래스
/// 
/// [오브젝트 유지 방식]
/// - NetworkObject 프리팹에서 "Destroy When State Authority Leaves" = FALSE 설정
/// - NetworkObject 프리팹에서 "Allow State Authority Override" = TRUE 설정
/// - Host가 나가면 오브젝트가 유지되고, 새 Host가 자동으로 State Authority를 획득
/// - Runner를 종료하지 않고 Fusion이 자동으로 처리하도록 함
/// </summary>
public class HostMigrationHandler : MonoBehaviour
{
    public static HostMigrationHandler Instance { get; private set; }

    [Header("Migration Settings")]
    [SerializeField] private GameObject _migrationUIPanel; // 선택적: "호스트 변경 중..." UI

    // 마이그레이션 중 캐싱되는 데이터
    private GameMigrationData _cachedGameData;
    private Dictionary<string, PlayerMigrationData> _cachedPlayerData = new Dictionary<string, PlayerMigrationData>();
    
    // 마이그레이션 상태
    private bool _isMigrating = false;
    public bool IsMigrating => _isMigrating;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else if (Instance != this)
        {
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// Host Migration을 시작합니다. NetworkManager의 OnHostMigration에서 호출됩니다.
    /// 
    /// [중요] 오브젝트 유지 방식에서는:
    /// - Runner를 종료하지 않음!
    /// - Fusion이 자동으로 새 Host를 선출하고 State Authority를 이전
    /// - 로컬 상태(ChangeDetector, 싱글톤 참조 등)만 수동 복원
    /// </summary>
    public void StartMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken)
    {
        if (_isMigrating)
        {
            Debug.LogWarning("[HostMigrationHandler] 이미 마이그레이션 진행 중입니다.");
            return;
        }

        Debug.Log("<color=yellow>[HostMigrationHandler] Host Migration 시작! (오브젝트 유지 방식)</color>");
        _isMigrating = true;

        // UI 표시 (있는 경우)
        ShowMigrationUI(true);

        // 현재 게임 상태 캐싱 (백업용)
        CacheCurrentGameState(runner);

        // [오브젝트 유지 방식]
        // Runner를 종료하지 않음! Fusion이 자동으로 처리함
        // 새 Host가 선출되면 State Authority가 자동으로 이전됨
        
        // 로컬 상태 복원을 위한 코루틴 시작
        StartCoroutine(HandleMigrationCoroutine(runner));
    }

    /// <summary>
    /// 현재 게임 상태를 캐싱합니다.
    /// </summary>
    private void CacheCurrentGameState(NetworkRunner runner)
    {
        _cachedGameData = new GameMigrationData
        {
            CurrentSceneName = SceneManager.GetActiveScene().name
        };

        // GameManagers에서 추가 상태 가져오기
        if (GameManagers.Instance != null && GameManagers.Instance.IsReadyForNetworkAccess)
        {
            _cachedGameData.CurrentRound = GameManagers.Instance.currentRound;
            _cachedGameData.GameStateValue = (int)GameManagers.Instance.currentState;
            _cachedGameData.RemainingPhaseTime = GameManagers.Instance.currentPhaseTimer;
        }

        Debug.Log($"[HostMigrationHandler] 게임 상태 캐싱 완료 - Round: {_cachedGameData.CurrentRound}, Scene: {_cachedGameData.CurrentSceneName}");
    }

    /// <summary>
    /// Host Migration 메인 코루틴
    /// [오브젝트 유지 방식] Runner를 종료하지 않고 로컬 상태만 복원
    /// </summary>
    private IEnumerator HandleMigrationCoroutine(NetworkRunner runner)
    {
        Debug.Log("[HostMigrationHandler] 새 Host 선출 대기 중...");
        
        // 잠시 대기하여 Fusion이 새 Host를 선출할 시간을 줌
        yield return new WaitForSeconds(1f);
        
        // 새 Host가 되었는지 확인
        if (runner != null && runner.IsRunning)
        {
            bool isNewHost = runner.IsServer || runner.IsSharedModeMasterClient;
            Debug.Log($"[HostMigrationHandler] 현재 클라이언트가 새 Host인가? {isNewHost}");
            
            if (isNewHost)
            {
                Debug.Log("<color=green>[HostMigrationHandler] 이 클라이언트가 새 Host가 되었습니다!</color>");
                
                // 새 Host로서 State Authority 요청 (필요한 경우)
                yield return RequestStateAuthorityForOrphanedObjects(runner);
            }
        }
        
        // 로컬 GameManagers 상태 복원
        yield return WaitAndRestoreGameManagers();
        
        // 완료!
        OnMigrationComplete();
    }

    /// <summary>
    /// State Authority가 없는 오브젝트들에 대해 새 Host가 State Authority를 요청
    /// </summary>
    private IEnumerator RequestStateAuthorityForOrphanedObjects(NetworkRunner runner)
    {
        Debug.Log("[HostMigrationHandler] 고아 오브젝트에 대한 State Authority 요청 중...");
        
        // 모든 NetworkObject를 순회하며 State Authority가 없는 것들 찾기
        var allNetworkObjects = FindObjectsOfType<NetworkObject>();
        
        foreach (var no in allNetworkObjects)
        {
            if (no == null || !no.IsValid) continue;
            
            // State Authority가 없거나 유효하지 않은 경우
            if (!no.HasStateAuthority)
            {
                try
                {
                    // 새 Host가 State Authority 요청
                    no.RequestStateAuthority();
                    Debug.Log($"[HostMigrationHandler] State Authority 요청: {no.name}");
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[HostMigrationHandler] State Authority 요청 실패: {no.name} - {e.Message}");
                }
            }
        }
        
        // State Authority 이전 완료 대기
        yield return new WaitForSeconds(0.5f);
        
        Debug.Log("[HostMigrationHandler] State Authority 요청 완료");
    }
    
    /// <summary>
    /// GameManagers가 준비될 때까지 대기 후 로컬 상태를 복원합니다.
    /// </summary>
    private IEnumerator WaitAndRestoreGameManagers()
    {
        float waitTime = 0f;
        const float maxWaitTime = 5f;
        
        // GameManagers가 준비될 때까지 대기
        while (waitTime < maxWaitTime)
        {
            if (GameManagers.Instance != null && GameManagers.Instance.IsReadyForNetworkAccess)
            {
                Debug.Log("[HostMigrationHandler] GameManagers 로컬 상태 복원 중...");
                GameManagers.Instance.RestoreAfterHostMigration();
                Debug.Log("<color=green>[HostMigrationHandler] GameManagers 로컬 상태 복원 완료!</color>");
                yield break;
            }
            
            yield return new WaitForSeconds(0.1f);
            waitTime += 0.1f;
        }
        
        Debug.LogWarning("[HostMigrationHandler] GameManagers 복원 대기 시간 초과.");
    }

    /// <summary>
    /// Host Migration 완료 처리
    /// </summary>
    private void OnMigrationComplete()
    {
        _isMigrating = false;
        ShowMigrationUI(false);

        // 캐시 클리어
        _cachedPlayerData.Clear();

        Debug.Log("<color=green>[HostMigrationHandler] Host Migration 성공! 게임이 계속됩니다!</color>");
    }

    /// <summary>
    /// Migration UI 표시/숨김
    /// </summary>
    private void ShowMigrationUI(bool show)
    {
        if (_migrationUIPanel != null)
        {
            _migrationUIPanel.SetActive(show);
        }
    }

    /// <summary>
    /// 이탈한 플레이어 데이터를 캐싱합니다. (재참여용)
    /// </summary>
    public void CacheDisconnectedPlayer(string connectionToken, PlayerMigrationData data)
    {
        _cachedPlayerData[connectionToken] = data;
        Debug.Log($"[HostMigrationHandler] 플레이어 데이터 캐싱: {connectionToken}");
    }

    /// <summary>
    /// 재참여 플레이어 데이터를 가져옵니다.
    /// </summary>
    public bool TryGetCachedPlayerData(string connectionToken, out PlayerMigrationData data)
    {
        return _cachedPlayerData.TryGetValue(connectionToken, out data);
    }
    
    /// <summary>
    /// 캐싱된 게임 데이터를 가져옵니다.
    /// </summary>
    public GameMigrationData GetCachedGameData()
    {
        return _cachedGameData;
    }
}
