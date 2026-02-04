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
    /// [세션 재시작 방식]
    /// - HostMigrationToken을 사용하여 새 Host로 세션을 재시작
    /// - 남은 클라이언트가 새 Host가 되어 StateAuthority를 획득
    /// - Fusion이 Token에 저장된 게임 상태를 자동 복원
    /// </summary>
    public void StartMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken)
    {
        if (_isMigrating)
        {
            Debug.LogWarning("[HostMigrationHandler] 이미 마이그레이션 진행 중입니다.");
            return;
        }

        Debug.Log("<color=yellow>═══════════════════════════════════════════</color>");
        Debug.Log("<color=yellow>[HostMigrationHandler] Host Migration 시작! (세션 재시작 방식)</color>");
        Debug.Log("<color=yellow>═══════════════════════════════════════════</color>");
        _isMigrating = true;

        // UI 표시 (있는 경우)
        ShowMigrationUI(true);

        // 현재 게임 상태 캐싱 (백업용)
        CacheCurrentGameState(runner);

        // [세션 재시작 방식]
        // HostMigrationToken을 사용하여 새 Host로 세션을 재시작
        // 이 방식으로 남은 클라이언트가 새 Host가 됨!
        StartCoroutine(RestartAsNewHostCoroutine(runner, hostMigrationToken));
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
    /// [새 방식] HostMigrationToken을 사용하여 새 Host로 세션을 재시작합니다.
    /// 이 방식으로 남은 클라이언트가 새 Host가 되어 StateAuthority를 획득합니다.
    /// </summary>
    private IEnumerator RestartAsNewHostCoroutine(NetworkRunner oldRunner, HostMigrationToken hostMigrationToken)
    {
        Debug.Log("<color=yellow>[HostMigrationHandler] 세션 재시작 준비 중...</color>");
        
        // 기존 Runner 정리를 위해 잠시 대기
        yield return new WaitForSeconds(0.5f);
        
        Debug.Log("[HostMigrationHandler] 대기 완료, Runner 상태 확인...");
        Debug.Log($"  - oldRunner null? {oldRunner == null}");
        Debug.Log($"  - oldRunner.IsRunning? {oldRunner?.IsRunning}");
        
        // ★ 중요: Shutdown을 호출하지 않음!
        // Shutdown을 호출하면 코루틴이 중단될 수 있음
        // 대신 oldRunner 참조만 저장하고, 새 Runner 시작 후에 정리
        NetworkRunner runnerToCleanup = oldRunner;
        
        Debug.Log("<color=cyan>[HostMigrationHandler] 새 세션 시작 준비 (기존 Runner는 나중에 정리)...</color>");
        
        // 새 Runner 생성 및 새 Host로 시작
        Debug.Log("<color=cyan>[HostMigrationHandler] 새 Host로 세션 재시작 중...</color>");
        
        // async 메서드를 별도로 실행하고 완료를 기다림
        NetworkRunner newRunner = null;
        bool taskCompleted = false;
        bool taskFailed = false;
        string taskError = "";
        
        // async 작업을 시작하고 콜백으로 결과를 받음
        StartGameWithMigrationTokenAsync(hostMigrationToken, 
            (runner) => 
            {
                newRunner = runner;
                taskCompleted = true;
            },
            (error) =>
            {
                taskError = error;
                taskFailed = true;
                taskCompleted = true;
            });
        
        // 완료 대기 (최대 30초)
        float timeout = 30f;
        float elapsed = 0f;
        while (!taskCompleted && elapsed < timeout)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }
        
        if (!taskCompleted)
        {
            Debug.LogError("<color=red>[HostMigrationHandler] 세션 재시작 타임아웃!</color>");
            OnMigrationComplete();
            yield break;
        }
        
        if (taskFailed)
        {
            Debug.LogError($"<color=red>[HostMigrationHandler] 세션 재시작 실패: {taskError}</color>");
            OnMigrationComplete();
            yield break;
        }
        
        if (newRunner == null || !newRunner.IsRunning)
        {
            Debug.LogError("<color=red>[HostMigrationHandler] 새 Runner 시작 실패!</color>");
            OnMigrationComplete();
            yield break;
        }
        
        Debug.Log("<color=green>[HostMigrationHandler] 새 Host로 세션 재시작 성공!</color>");
        Debug.Log($"  - Runner.GameMode: {newRunner.GameMode}");
        Debug.Log($"  - Runner.IsServer: {newRunner.IsServer}");
        Debug.Log($"  - Runner.IsRunning: {newRunner.IsRunning}");
        
        // NetworkManager에 새 Runner 설정
        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.SetRunnerAfterMigration(newRunner);
        }
        
        // 기존 Runner 정리 - 절대 파괴하지 않음!
        // ★ 중요: NetworkRunner.OnDestroy()가 내부적으로 Shutdown()을 호출함
        // Shutdown이 호출되면 Photon Cloud 연결이 끊어지므로 파괴하면 안 됨
        if (runnerToCleanup != null && runnerToCleanup != newRunner)
        {
            Debug.Log("[HostMigrationHandler] 기존 Runner 비활성화 (파괴 안 함!)...");
            try
            {
                // 콜백 제거
                runnerToCleanup.RemoveCallbacks(NetworkManager.Instance);
                
                // ★ GameObject를 비활성화만! 절대 파괴하지 않음!
                // 게임이 종료될 때 자연스럽게 정리됨
                if (runnerToCleanup.gameObject != null)
                {
                    runnerToCleanup.gameObject.SetActive(false);
                    runnerToCleanup.gameObject.name = "NetworkManager_OLD_DISABLED";
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[HostMigrationHandler] 기존 Runner 정리 중 예외 (무시됨): {e.Message}");
            }
        }
        
        // GameManagers Spawned 대기 및 복원
        yield return WaitAndRestoreGameManagers();
        
        // 완료!
        OnMigrationComplete();
    }
    
    /// <summary>
    /// 콜백 패턴으로 async 작업 실행 (코루틴 호환)
    /// </summary>
    private async void StartGameWithMigrationTokenAsync(
        HostMigrationToken hostMigrationToken, 
        System.Action<NetworkRunner> onSuccess,
        System.Action<string> onError)
    {
        try
        {
            Debug.Log("[HostMigrationHandler] StartGameWithMigrationTokenAsync 시작...");
            var runner = await StartGameWithMigrationToken(hostMigrationToken);
            Debug.Log($"[HostMigrationHandler] StartGameWithMigrationToken 완료, runner: {runner?.name}");
            onSuccess?.Invoke(runner);
        }
        catch (Exception e)
        {
            Debug.LogError($"[HostMigrationHandler] StartGameWithMigrationTokenAsync 예외: {e.Message}");
            Debug.LogException(e);
            onError?.Invoke(e.Message);
        }
    }
    
    /// <summary>
    /// HostMigrationToken을 사용하여 새 Host로 게임을 시작합니다.
    /// </summary>
    private async System.Threading.Tasks.Task<NetworkRunner> StartGameWithMigrationToken(HostMigrationToken hostMigrationToken)
    {
        try
        {
            Debug.Log("[HostMigrationHandler] StartGame with HostMigrationToken...");
            
            // ★ 새로운 방식: 별도의 GameObject에 새 Runner 생성
            // 기존 Runner가 있는 GameObject를 건드리지 않음 (파괴 문제 방지)
            
            // 새 Runner용 GameObject 생성
            var newRunnerGO = new GameObject("NetworkRunner_Migrated");
            UnityEngine.Object.DontDestroyOnLoad(newRunnerGO);
            
            Debug.Log("[HostMigrationHandler] 새 Runner GameObject 생성 완료");
            
            // 새 Runner 생성
            var newRunner = newRunnerGO.AddComponent<NetworkRunner>();
            
            if (newRunner == null)
            {
                Debug.LogError("[HostMigrationHandler] 새 Runner 생성 실패!");
                UnityEngine.Object.Destroy(newRunnerGO);
                return null;
            }
            
            // NetworkManager를 콜백으로 등록
            newRunner.AddCallbacks(NetworkManager.Instance);
            newRunner.ProvideInput = true;
            
            Debug.Log("[HostMigrationHandler] 새 Runner 컴포넌트 추가 완료");
            
            // ObjectProvider 설정
            var objectProvider = newRunnerGO.AddComponent<PooledNetworkObjectProvider>();
            
            // SceneManager 설정
            var sceneManager = newRunnerGO.AddComponent<NetworkSceneManagerDefault>();
            
            // 새 Host로 게임 시작 (HostMigrationToken 사용)
            var result = await newRunner.StartGame(new StartGameArgs()
            {
                GameMode = GameMode.Host,  // ★ 새 Host가 됨!
                HostMigrationToken = hostMigrationToken,  // ★ 기존 상태 복원
                SceneManager = sceneManager,
                ObjectProvider = objectProvider,
                HostMigrationResume = HostMigrationResume,  // 오브젝트 복원 콜백
                ConnectionToken = System.Text.Encoding.UTF8.GetBytes(
                    PlayerPrefs.GetString("PlayerUUID", System.Guid.NewGuid().ToString())),
            });
            
            if (result.Ok)
            {
                Debug.Log("<color=green>[HostMigrationHandler] StartGame 성공!</color>");
                return newRunner;
            }
            else
            {
                Debug.LogError($"<color=red>[HostMigrationHandler] StartGame 실패: {result.ShutdownReason}</color>");
                return null;
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"<color=red>[HostMigrationHandler] StartGame 예외: {e.Message}</color>");
            Debug.LogException(e);
            return null;
        }
    }
    
    /// <summary>
    /// Host Migration 시 네트워크 오브젝트 복원 콜백
    /// Fusion이 이 콜백을 호출하여 오브젝트 복원이 완료되었음을 알립니다.
    /// </summary>
    private void HostMigrationResume(NetworkRunner runner)
    {
        Debug.Log($"<color=cyan>[HostMigrationHandler] Host Migration 오브젝트 복원 완료!</color>");
        Debug.Log($"  - Runner: {runner?.name}");
        Debug.Log($"  - IsServer: {runner?.IsServer}");
        Debug.Log($"  - 총 오브젝트 수: {runner?.GetAllNetworkObjects()?.Count ?? 0}");
        
        // Fusion이 자동으로 모든 오브젝트를 복원합니다.
        // 이 콜백은 복원이 완료된 후 호출됩니다.
    }

    /// <summary>
    /// State Authority가 없는 오브젝트들에 대해 새 Host가 State Authority를 요청
    /// </summary>
    private IEnumerator RequestStateAuthorityForOrphanedObjects(NetworkRunner runner)
    {
        Debug.Log("<color=yellow>[HostMigrationHandler] State Authority 요청 시작...</color>");
        
        // 모든 NetworkObject를 순회하며 State Authority가 없는 것들 찾기
        var allNetworkObjects = FindObjectsOfType<NetworkObject>();
        int requestCount = 0;
        int alreadyHasCount = 0;
        
        Debug.Log($"<color=cyan>[HostMigrationHandler] 총 NetworkObject 수: {allNetworkObjects.Length}</color>");
        
        foreach (var no in allNetworkObjects)
        {
            if (no == null || !no.IsValid) 
            {
                Debug.Log($"[HostMigrationHandler] 스킵 (null 또는 invalid): {no?.name}");
                continue;
            }
            
            // State Authority가 없거나 유효하지 않은 경우
            if (!no.HasStateAuthority)
            {
                try
                {
                    // 새 Host가 State Authority 요청
                    no.RequestStateAuthority();
                    requestCount++;
                    Debug.Log($"<color=orange>[HostMigrationHandler] State Authority 요청: {no.name}</color>");
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[HostMigrationHandler] State Authority 요청 실패: {no.name} - {e.Message}");
                }
            }
            else
            {
                alreadyHasCount++;
                Debug.Log($"<color=green>[HostMigrationHandler] 이미 State Authority 보유: {no.name}</color>");
            }
        }
        
        Debug.Log($"<color=cyan>[HostMigrationHandler] 요청 완료 - 요청: {requestCount}개, 이미 보유: {alreadyHasCount}개</color>");
        
        // State Authority 이전 완료 대기 (더 긴 시간)
        yield return new WaitForSeconds(1f);
        
        // State Authority 획득 확인
        int acquiredCount = 0;
        int failedCount = 0;
        foreach (var no in allNetworkObjects)
        {
            if (no != null && no.IsValid)
            {
                if (no.HasStateAuthority)
                {
                    acquiredCount++;
                }
                else
                {
                    failedCount++;
                    Debug.LogWarning($"<color=red>[HostMigrationHandler] State Authority 획득 실패: {no.name}</color>");
                }
            }
        }
        Debug.Log($"<color=green>[HostMigrationHandler] State Authority 획득 결과: 성공 {acquiredCount}개, 실패 {failedCount}개</color>");
    }
    
    /// <summary>
    /// GameManagers가 준비될 때까지 대기 후 로컬 상태를 복원합니다.
    /// </summary>
    private IEnumerator WaitAndRestoreGameManagers()
    {
        float waitTime = 0f;
        const float maxWaitTime = 5f;
        
        Debug.Log("<color=yellow>[HostMigrationHandler] GameManagers 복원 대기 시작...</color>");
        
        // GameManagers가 준비될 때까지 대기
        while (waitTime < maxWaitTime)
        {
            bool instanceExists = GameManagers.Instance != null;
            bool isReady = instanceExists && GameManagers.Instance.IsReadyForNetworkAccess;
            
            Debug.Log($"[HostMigrationHandler] 대기 중... ({waitTime:F1}s) - Instance: {instanceExists}, Ready: {isReady}");
            
            if (isReady)
            {
                Debug.Log("<color=cyan>[HostMigrationHandler] GameManagers 준비 완료! 복원 시작...</color>");
                
                // 추가 상태 정보 로깅
                var gm = GameManagers.Instance;
                Debug.Log($"[HostMigrationHandler] 복원 전 상태 - Round: {gm.currentRound}, State: {gm.currentState}, Timer: {gm.currentPhaseTimer:F1}s");
                
                GameManagers.Instance.RestoreAfterHostMigration();
                
                Debug.Log($"<color=green>[HostMigrationHandler] GameManagers 로컬 상태 복원 완료!</color>");
                yield break;
            }
            
            yield return new WaitForSeconds(0.1f);
            waitTime += 0.1f;
        }
        
        // 시간 초과 - 수동 복원 시도
        Debug.LogWarning($"<color=orange>[HostMigrationHandler] GameManagers 대기 시간 초과! 수동 복원 시도...</color>");
        
        if (GameManagers.Instance != null)
        {
            Debug.Log("[HostMigrationHandler] GameManagers.Instance 존재 - 강제 복원 시도");
            
            // 캐싱된 게임 데이터로 UI 및 상태 복원
            if (_cachedGameData.CurrentRound > 0)
            {
                Debug.Log($"[HostMigrationHandler] 캐싱된 데이터로 복원 - Round: {_cachedGameData.CurrentRound}, State: {_cachedGameData.GameStateValue}");
            }
            
            // localPlayer 찾기 시도
            var allPlayerManagers = UnityEngine.Object.FindObjectsOfType<PlayerManager>();
            Debug.Log($"[HostMigrationHandler] 발견된 PlayerManager 수: {allPlayerManagers.Length}");
            
            foreach (var pm in allPlayerManagers)
            {
                if (pm != null && pm.Object != null && pm.Object.HasInputAuthority)
                {
                    GameManagers.Instance.localPlayer = pm;
                    Debug.Log($"[HostMigrationHandler] localPlayer 수동 설정: Player {pm.playerId}");
                    break;
                }
            }
            
            // CommandProcessor 확인
            if (GameManagers.Instance.CommandProcessor == null)
            {
                // 리플렉션이나 public 메서드로 설정해야 하는데, 여기서는 로그만
                Debug.LogWarning("[HostMigrationHandler] CommandProcessor가 null입니다!");
            }
        }
        else
        {
            Debug.LogError("<color=red>[HostMigrationHandler] GameManagers.Instance가 null! 복원 불가!</color>");
            
            // Scene에서 GameManagers 찾기 시도
            var foundGM = UnityEngine.Object.FindObjectOfType<GameManagers>();
            if (foundGM != null)
            {
                Debug.Log($"[HostMigrationHandler] Scene에서 GameManagers 발견: {foundGM.name}");
            }
        }
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
