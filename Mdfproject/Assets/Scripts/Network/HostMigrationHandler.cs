// Assets/Scripts/Network/HostMigrationHandler.cs
// Host Migration 처리를 담당하는 핸들러 클래스
// 오브젝트 유지 방식: Runner를 종료하지 않고 Fusion이 자동으로 State Authority를 이전

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Fusion;
using System.Linq;

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
            
            // ★★★ 중요: HostMigrationHandler를 독립적인 GameObject로 분리 ★★★
            // NetworkRunner의 자식으로 있으면 Runner가 비활성화될 때 함께 비활성화됨
            // 따라서 부모가 있으면 분리하여 독립적으로 만듬
            if (transform.parent != null)
            {
                Debug.Log($"<color=yellow>[HostMigrationHandler] 부모({transform.parent.name})에서 분리하여 독립 객체로 전환</color>");
                
                // 새 독립 GameObject 생성
                GameObject independentObj = new GameObject("HostMigrationHandler_Independent");
                
                // 이 컴포넌트를 새 객체로 이동 (Unity에서는 컴포넌트 이동이 안 되므로 새로 생성)
                HostMigrationHandler newHandler = independentObj.AddComponent<HostMigrationHandler>();
                
                // 설정 복사
                newHandler._migrationUIPanel = this._migrationUIPanel;
                
                // Instance를 새 핸들러로 교체
                Instance = newHandler;
                DontDestroyOnLoad(independentObj);
                
                // 기존 객체 제거
                Destroy(this);
                return;
            }
            
            DontDestroyOnLoad(gameObject);
            Debug.Log("<color=green>[HostMigrationHandler] 초기화 완료 (독립 객체)</color>");
        }
        else if (Instance != this)
        {
            Debug.Log($"[HostMigrationHandler] 중복 인스턴스 제거: {gameObject.name}");
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

        // [Observer Pattern] Migration 시작 이벤트 발행
        GameEvents.TriggerHostMigrationStarted();

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
        Debug.Log("<color=magenta>═══ [STEP 1] 상태 캐싱 시작 ═══</color>");
        
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
            
            Debug.Log($"<color=cyan>[STEP 1] 캐싱된 상태:</color>");
            Debug.Log($"  상태: {(GameManagers.GameState)_cachedGameData.GameStateValue}");
            Debug.Log($"  라운드: {_cachedGameData.CurrentRound}");
            Debug.Log($"  남은 시간: {_cachedGameData.RemainingPhaseTime:F1}초");
            Debug.Log($"  씨: {_cachedGameData.CurrentSceneName}");
        }
        else
        {
            Debug.LogWarning("[STEP 1] GameManagers 접근 불가 - 기본값으로 캐싱");
        }
    }

    /// <summary>
    /// [새 방식] HostMigrationToken을 사용하여 새 Host로 세션을 재시작합니다.
    /// 이 방식으로 남은 클라이언트가 새 Host가 되어 StateAuthority를 획득합니다.
    /// </summary>
    private IEnumerator RestartAsNewHostCoroutine(NetworkRunner oldRunner, HostMigrationToken hostMigrationToken)
    {
        Debug.Log("<color=magenta>═══ [STEP 2] 세션 재시작 준비 ═══</color>");
        
        // 기존 Runner 정리를 위해 잠시 대기
        yield return new WaitForSeconds(0.5f);
        
        Debug.Log("[STEP 2] 대기 완료, 기존 Runner 상태:");
        Debug.Log($"  - oldRunner null? {oldRunner == null}");
        Debug.Log($"  - oldRunner.IsRunning? {oldRunner?.IsRunning}");
        
        // ★ 중요: Shutdown을 호출하지 않음!
        // Shutdown을 호출하면 코루틴이 중단될 수 있음
        // 대신 oldRunner 참조만 저장하고, 새 Runner 시작 후에 정리
        NetworkRunner runnerToCleanup = oldRunner;
        
        Debug.Log("<color=magenta>═══ [STEP 3] 새 Host로 세션 재시작 ═══</color>");
        
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
        
        Debug.Log("<color=magenta>═══ [STEP 4] 새 Host 등록 완료 ═══</color>");
        Debug.Log($"<color=green>[STEP 4] 새 Host로 세션 재시작 성공!</color>");
        Debug.Log($"  - Runner.GameMode: {newRunner.GameMode}");
        Debug.Log($"  - Runner.IsServer: {newRunner.IsServer}");
        Debug.Log($"  - Runner.IsRunning: {newRunner.IsRunning}");
        
        // NetworkManager에 새 Runner 설정
        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.SetRunnerAfterMigration(newRunner);
            Debug.Log("[STEP 4] NetworkManager에 새 Runner 설정 완료");
        }
        
        // ★★★ 중요: GameManagers 복원을 기존 Runner 비활성화 전에 먼저 실행! ★★★
        // 이유: HostMigrationHandler가 기존 Runner의 GameObject에 있으므로,
        // 비활성화하면 코루틴이 중단됨
        Debug.Log("<color=magenta>═══ [STEP 5] GameManagers 복원 시작 ═══</color>");
        Debug.Log("[STEP 5] 기존 Runner 비활성화 전에 복원 먼저 실행!");
        
        // GameManagers Spawned 대기 및 복원
        yield return WaitAndRestoreGameManagers();
        
        Debug.Log("[STEP 5] 복원 완료 - 이제 기존 Runner 비활성화");
        
        // 기존 Runner 정리 - 절대 파괴하지 않음!
        // ★ 중요: NetworkRunner.OnDestroy()가 내부적으로 Shutdown()을 호출함
        // Shutdown이 호출되면 Photon Cloud 연결이 끊어지므로 파괴하면 안 됨
        if (runnerToCleanup != null && runnerToCleanup != newRunner)
        {
            Debug.Log("[STEP 5] 기존 Runner 비활성화 (파괴 안 함!)...");
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
                Debug.Log("[STEP 5] 기존 Runner 비활성화 완료");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[HostMigrationHandler] 기존 Runner 정리 중 예외 (무시됨): {e.Message}");
            }
        }
        
        // 완료!
        Debug.Log("[STEP 6] OnMigrationComplete 호출...");
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
    /// Fusion이 GetResumeSnapshotNetworkObjects 호출 시 자동으로 인스턴스를 생성합니다.
    /// 여기서는 추가 Spawn 없이 복원된 오브젝트를 확인하고 필요한 초기화만 수행합니다.
    /// </summary>
    private void HostMigrationResume(NetworkRunner runner)
    {
        Debug.Log("<color=cyan>═══════════════════════════════════════════</color>");
        Debug.Log("<color=cyan>[HostMigrationHandler] HostMigrationResume 시작!</color>");
        Debug.Log("<color=cyan>═══════════════════════════════════════════</color>");
        Debug.Log($"  - Runner: {runner?.name}");
        Debug.Log($"  - IsServer: {runner?.IsServer}");
        
        // Resume Snapshot 오브젝트 가져오기
        // ★ 중요: 이 호출만으로 Fusion이 자동으로 인스턴스를 생성합니다!
        // 추가로 runner.Spawn을 호출하면 중복 생성됩니다.
        var resumeObjects = runner.GetResumeSnapshotNetworkObjects().ToList();
        Debug.Log($"<color=yellow>[HostMigrationHandler] Resume Snapshot 오브젝트 수: {resumeObjects.Count}</color>");
        
        int gameManagerCount = 0;
        int playerCount = 0;
        int unitCount = 0;
        int otherCount = 0;
        
        GameManagers restoredGM = null;  // ★ 복원된 GameManagers 저장
        
        // 복원된 오브젝트 카테고리별 카운트 및 확인
        foreach (var resumeNO in resumeObjects)
        {
            if (resumeNO == null) continue;
            
            Debug.Log($"<color=orange>[HostMigrationHandler] 복원됨: {resumeNO.name} (Id: {resumeNO.Id})</color>");
            
            // GameManagers 확인
            if (resumeNO.TryGetComponent<GameManagers>(out var gm))
            {
                gameManagerCount++;
                restoredGM = gm;  // ★ 복원된 GameManagers 저장
                Debug.Log($"<color=green>[HostMigrationHandler] GameManagers 복원됨: {gm.currentRound} 라운드, 상태: {gm.currentState}</color>");
                Debug.Log($"<color=green>  - HasStateAuthority: {resumeNO.HasStateAuthority}</color>");
            }
            // PlayerManager 확인
            else if (resumeNO.TryGetComponent<PlayerManager>(out var pm))
            {
                playerCount++;
                Debug.Log($"  - Player {pm.playerId}, InputAuthority: {resumeNO.InputAuthority}");
            }
            // Unit 확인
            else if (resumeNO.TryGetComponent<Unit>(out var unit))
            {
                unitCount++;
            }
            else
            {
                otherCount++;
            }
        }
        
        Debug.Log($"<color=cyan>[HostMigrationHandler] 복원 요약:</color>");
        Debug.Log($"  - GameManagers: {gameManagerCount}");
        Debug.Log($"  - Players: {playerCount}");
        Debug.Log($"  - Units: {unitCount}");
        Debug.Log($"  - Others: {otherCount}");
        
        // ★★★ 핵심: GameManagers.Instance를 새 Runner에서 복원된 객체로 교체 ★★★
        if (restoredGM != null)
        {
            Debug.Log("<color=magenta>[HostMigrationHandler] GameManagers.Instance를 새 Runner의 객체로 교체!</color>");
            Debug.Log($"  - 기존 Instance: {(GameManagers.Instance != null ? GameManagers.Instance.GetHashCode().ToString() : "null")}");
            Debug.Log($"  - 새 Instance: {restoredGM.GetHashCode()}");
            
            GameManagers.Instance = restoredGM;
            
            // 새 Host라면 StateAuthority 요청
            if (runner.IsServer && restoredGM.Object != null && !restoredGM.Object.HasStateAuthority)
            {
                Debug.Log("<color=yellow>[HostMigrationHandler] 새 Host - GameManagers StateAuthority 요청</color>");
                restoredGM.Object.RequestStateAuthority();
            }
        }
        
        // Scene 오브젝트도 확인 (추가 처리 필요시)
        var sceneObjects = runner.GetResumeSnapshotNetworkSceneObjects();
        int sceneCount = 0;
        foreach (var tuple in sceneObjects)
        {
            NetworkObject sceneNO = tuple.Item1;
            if (sceneNO != null)
            {
                sceneCount++;
                Debug.Log($"[HostMigrationHandler] Scene 오브젝트: {sceneNO.name}");
            }
        }
        Debug.Log($"  - Scene Objects: {sceneCount}");
        
        // 현재 존재하는 오브젝트 수 확인
        var allObjects = runner.GetAllNetworkObjects();
        Debug.Log($"<color=green>[HostMigrationHandler] 총 NetworkObject 수: {allObjects?.Count ?? 0}</color>");
        Debug.Log("<color=cyan>═══════════════════════════════════════════</color>");
    }
    
    /// <summary>
    /// GameManagers를 스냅샷에서 복원합니다.
    /// </summary>
    private void RestoreGameManagersFromSnapshot(NetworkRunner runner, NetworkObject resumeNO, GameManagers sourceGM)
    {
        Debug.Log("<color=magenta>[HostMigrationHandler] GameManagers 복원 중...</color>");
        
        // 기존 GameManagers.Instance가 있으면 상태 복원
        if (GameManagers.Instance != null)
        {
            Debug.Log("[HostMigrationHandler] 기존 GameManagers에 상태 복원");
            
            // 캐싱된 데이터로 복원
            if (_cachedGameData.CurrentRound > 0)
            {
                Debug.Log($"[HostMigrationHandler] 캐싱된 데이터 사용 - Round: {_cachedGameData.CurrentRound}");
            }
            
            return;
        }
        
        // 새로 Spawn
        runner.Spawn(resumeNO,
            position: resumeNO.transform.position,
            rotation: resumeNO.transform.rotation,
            onBeforeSpawned: (runner, spawnedNO) =>
            {
                spawnedNO.CopyStateFrom(resumeNO);
                Debug.Log("<color=magenta>[HostMigrationHandler] GameManagers Spawn 완료</color>");
            });
    }
    
    /// <summary>
    /// PlayerManager를 스냅샷에서 복원합니다.
    /// </summary>
    private void RestorePlayerManagerFromSnapshot(NetworkRunner runner, NetworkObject resumeNO, PlayerManager sourcePM)
    {
        Debug.Log($"<color=blue>[HostMigrationHandler] PlayerManager 복원 중: Player {sourcePM.playerId}</color>");
        
        // 이미 존재하는 PlayerManager 찾기
        var existingPMs = UnityEngine.Object.FindObjectsOfType<PlayerManager>();
        var existingPM = existingPMs.FirstOrDefault(pm => pm.playerId == sourcePM.playerId);
        
        if (existingPM != null && existingPM.Object != null && existingPM.Object.IsValid)
        {
            Debug.Log($"[HostMigrationHandler] 기존 PlayerManager 사용: Player {sourcePM.playerId}");
            // 기존 오브젝트에서 StateAuthority 요청
            if (!existingPM.Object.HasStateAuthority)
            {
                existingPM.Object.RequestStateAuthority();
            }
            return;
        }
        
        // 새로 Spawn
        runner.Spawn(resumeNO,
            position: resumeNO.transform.position,
            rotation: resumeNO.transform.rotation,
            inputAuthority: resumeNO.InputAuthority,
            onBeforeSpawned: (runner, spawnedNO) =>
            {
                spawnedNO.CopyStateFrom(resumeNO);
                Debug.Log($"<color=blue>[HostMigrationHandler] PlayerManager Spawn 완료: Player {sourcePM.playerId}</color>");
            });
    }
    
    /// <summary>
    /// Scene 오브젝트(Grid 등)를 스냅샷에서 복원합니다.
    /// </summary>
    private void RestoreSceneObjectsFromSnapshot(NetworkRunner runner)
    {
        Debug.Log("<color=yellow>[HostMigrationHandler] Scene 오브젝트 복원 중...</color>");
        
        var sceneObjects = runner.GetResumeSnapshotNetworkSceneObjects();
        int count = 0;
        
        // Scene 오브젝트 순회 - 튜플에서 Item1이 NetworkObject
        foreach (var tuple in sceneObjects)
        {
            try
            {
                NetworkObject sceneNO = tuple.Item1;
                if (sceneNO == null) continue;
                
                Debug.Log($"[HostMigrationHandler] Scene 오브젝트 복원: {sceneNO.name}");
                
                // Scene 오브젝트는 이미 존재하므로 상태만 복원
                var existingNO = runner.FindObject(sceneNO.Id);
                if (existingNO != null)
                {
                    existingNO.CopyStateFrom(sceneNO);
                    count++;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[HostMigrationHandler] Scene 오브젝트 복원 실패 - {e.Message}");
            }
        }
        
        Debug.Log($"<color=yellow>[HostMigrationHandler] Scene 오브젝트 복원 완료: {count}개</color>");
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
        Debug.Log("<color=yellow>[STEP 5.1] WaitAndRestoreGameManagers 코루틴 시작</color>");
        
        float waitTime = 0f;
        const float maxWaitTime = 5f;
        
        Debug.Log("[STEP 5.1] GameManagers 복원 대기 시작...");
        Debug.Log($"[STEP 5.1] GameManagers.Instance: {(GameManagers.Instance != null ? "exists" : "NULL")}");
        
        // GameManagers가 준비될 때까지 대기
        while (waitTime < maxWaitTime)
        {
            bool instanceExists = GameManagers.Instance != null;
            bool isReady = instanceExists && GameManagers.Instance.IsReadyForNetworkAccess;
            
            // 0.5초마다만 로그 출력 (너무 많은 로그 방지)
            if (waitTime == 0 || (int)(waitTime * 10) % 5 == 0)
            {
                Debug.Log($"[STEP 5.2] 대기 중... ({waitTime:F1}s) - Instance: {instanceExists}, Ready: {isReady}");
            }
            
            if (isReady)
            {
                Debug.Log("<color=cyan>[STEP 5.3] GameManagers 준비 완료!</color>");
                
                // 추가 상태 정보 로깅
                var gm = GameManagers.Instance;
                Debug.Log($"[STEP 5.3] 복원 전 상태:");
                Debug.Log($"  - Round: {gm.currentRound}");
                Debug.Log($"  - State: {gm.currentState}");
                Debug.Log($"  - Timer: {gm.currentPhaseTimer:F1}s");
                Debug.Log($"  - HasStateAuthority: {gm.Object?.HasStateAuthority}");
                
                // ★★★ 새 Host라면 StateAuthority 먼저 요청 ★★★
                bool isNewHost = NetworkManager.Instance?._runner?.IsServer ?? false;
                if (isNewHost && gm.Object != null && !gm.Object.HasStateAuthority)
                {
                    Debug.Log("<color=yellow>[STEP 5.3] 새 Host - GameManagers StateAuthority 요청</color>");
                    gm.Object.RequestStateAuthority();
                    
                    // StateAuthority 획득 대기 (최대 2초)
                    float authWait = 0f;
                    while (authWait < 2f)
                    {
                        if (gm.Object.HasStateAuthority)
                        {
                            Debug.Log($"<color=green>[STEP 5.3] StateAuthority 획득 성공! ({authWait:F1}초)</color>");
                            break;
                        }
                        yield return new WaitForSeconds(0.1f);
                        authWait += 0.1f;
                    }
                    
                    if (!gm.Object.HasStateAuthority)
                    {
                        Debug.LogWarning("<color=orange>[STEP 5.3] StateAuthority 획득 대기 시간 초과</color>");
                    }
                }
                
                Debug.Log("[STEP 5.3] RestoreAfterHostMigration 호출...");
                GameManagers.Instance.RestoreAfterHostMigration();
                
                Debug.Log("<color=green>[STEP 5.4] GameManagers 로컬 상태 복원 완료!</color>");
                yield break;
            }
            
            yield return new WaitForSeconds(0.1f);
            waitTime += 0.1f;
        }
        
        // 시간 초과 - 수동 복원 시도
        Debug.LogWarning("<color=orange>[STEP 5] GameManagers 대기 시간 초과! (5초)</color>");
        Debug.LogWarning("[STEP 5] 수동 복원 시도...");
        
        if (GameManagers.Instance != null)
        {
            Debug.Log("[STEP 5] GameManagers.Instance 존재 - IsReadyForNetworkAccess 무시하고 강제 복원");
            
            // IsReadyForNetworkAccess가 false여도 강제 복원 시도
            try
            {
                GameManagers.Instance.RestoreAfterHostMigration();
                Debug.Log("<color=yellow>[STEP 5] 강제 복원 완료 (IsReadyForNetworkAccess 무시)</color>");
            }
            catch (Exception e)
            {
                Debug.LogError($"[STEP 5] 강제 복원 중 예외: {e.Message}");
            }
            
            // 캐싱된 게임 데이터로 UI 및 상태 복원
            if (_cachedGameData.CurrentRound > 0)
            {
                Debug.Log($"[STEP 5] 캐싱된 데이터 - Round: {_cachedGameData.CurrentRound}, State: {_cachedGameData.GameStateValue}");
            }
            
            // localPlayer 찾기 시도
            var allPlayerManagers = UnityEngine.Object.FindObjectsOfType<PlayerManager>();
            Debug.Log($"[STEP 5] 발견된 PlayerManager 수: {allPlayerManagers.Length}");
            
            foreach (var pm in allPlayerManagers)
            {
                if (pm != null && pm.Object != null && pm.Object.HasInputAuthority)
                {
                    GameManagers.Instance.localPlayer = pm;
                    Debug.Log($"[STEP 5] localPlayer 수동 설정: Player {pm.playerId}");
                    break;
                }
            }
            
            // CommandProcessor 확인
            if (GameManagers.Instance.CommandProcessor == null)
            {
                Debug.LogWarning("[STEP 5] CommandProcessor가 null입니다!");
            }
        }
        else
        {
            Debug.LogError("<color=red>[STEP 5] GameManagers.Instance가 null! 복원 불가!</color>");
            
            // Scene에서 GameManagers 찾기 시도
            var foundGM = UnityEngine.Object.FindObjectOfType<GameManagers>();
            if (foundGM != null)
            {
                Debug.Log($"<color=cyan>[STEP 5] Scene에서 GameManagers 발견: {foundGM.name}</color>");
                Debug.Log("[STEP 5] Instance에 수동 할당!");
                
                // ★★★ 수동으로 Instance 할당 ★★★
                GameManagers.Instance = foundGM;
                
                Debug.Log($"[STEP 5] Instance 할당 완료! Object: {foundGM.Object != null}");
                
                // StateAuthority 요청
                bool isNewHost = NetworkManager.Instance?._runner?.IsServer ?? false;
                if (isNewHost && foundGM.Object != null && !foundGM.Object.HasStateAuthority)
                {
                    Debug.Log("<color=yellow>[STEP 5] 새 Host - StateAuthority 요청</color>");
                    foundGM.Object.RequestStateAuthority();
                }
                
                // 복원 호출
                if (foundGM.IsReadyForNetworkAccess)
                {
                    Debug.Log("[STEP 5] RestoreAfterHostMigration 호출...");
                    foundGM.RestoreAfterHostMigration();
                }
                else
                {
                    Debug.LogWarning("[STEP 5] IsReadyForNetworkAccess가 False - 복원 건너뜀");
                }
            }
            else
            {
                Debug.LogError("<color=red>[STEP 5] Scene에서도 GameManagers를 찾을 수 없음!</color>");
            }
        }
        
        Debug.Log("<color=yellow>[STEP 5] WaitAndRestoreGameManagers 코루틴 종료</color>");
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

        // [Observer Pattern] Migration 완료 이벤트 발행
        bool isNewHost = NetworkManager.Instance?._runner?.IsServer ?? false;
        GameEvents.TriggerHostMigrationCompleted(isNewHost);

        Debug.Log("<color=green>═══════════════════════════════════════════</color>");
        Debug.Log($"<color=green>[MIGRATION COMPLETE] Host Migration 성공!</color>");
        Debug.Log($"<color=green>  역할: {(isNewHost ? "새 Host" : "클라이언트")}</color>");
        Debug.Log("<color=green>  게임이 계속됩니다!</color>");
        Debug.Log("<color=green>═══════════════════════════════════════════</color>");
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
