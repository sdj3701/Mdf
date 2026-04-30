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
using System.Text;
using System.Reflection;

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
    public int FirstAttackerPlayerId;
    public string BattleOpponentsSnapshot;
    public string MatchFirstAttackerSnapshot;
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
    private float _cachedGameDataCapturedRealtime = -1f;
    private Dictionary<string, PlayerMigrationData> _cachedPlayerData = new Dictionary<string, PlayerMigrationData>();
    
    // 마이그레이션 상태
    private bool _isMigrating = false;
    public bool IsMigrating => _isMigrating;
    private bool _migrationRecoverySucceeded = false;

    // HostMigrationResume에서 스폰된 GameManagers 캐시 (복원 대기 루틴 폴백용)
    private GameManagers _restoredGameManagersCandidate;
    private float _lastResolveDebugLogTime = -10f;
    private MethodInfo _pushHostMigrationSnapshotMethod;
    private bool _pushHostMigrationSnapshotMethodResolved;
    private bool _pushHostMigrationSnapshotUnsupportedLogged;
    private Coroutine _aiReconciliationCoroutine;
    private bool _aiTakeoverReady = true;
    public bool IsAiTakeoverReady => _aiTakeoverReady;

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

        // Debug.Log("<color=yellow>═══════════════════════════════════════════</color>");
        Debug.Log("<color=yellow>[HostMigrationHandler] Host Migration 시작! (세션 재시작 방식)</color>");
        // Debug.Log("<color=yellow>═══════════════════════════════════════════</color>");
        Debug.Log($"[HostMigrationHandler] StartMigration 입력 runner: {DescribeRunner(runner)}");
        Debug.Log($"[HostMigrationHandler] StartMigration 시점 GameManagers: {DescribeGameManagers(GameManagers.Instance)}");
        _isMigrating = true;
        _migrationRecoverySucceeded = false;
        _aiTakeoverReady = false;
        if (_aiReconciliationCoroutine != null)
        {
            StopCoroutine(_aiReconciliationCoroutine);
            _aiReconciliationCoroutine = null;
        }

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
        _cachedGameDataCapturedRealtime = Time.realtimeSinceStartup;
        
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
            GameManagers.Instance.CaptureBattleSnapshotForMigration(
                out _cachedGameData.BattleOpponentsSnapshot,
                out _cachedGameData.MatchFirstAttackerSnapshot,
                out _cachedGameData.FirstAttackerPlayerId);
            
            Debug.Log($"<color=cyan>[STEP 1] 캐싱된 상태:</color>");
            // Debug.Log($"  상태: {(GameManagers.GameState)_cachedGameData.GameStateValue}");
            // Debug.Log($"  라운드: {_cachedGameData.CurrentRound}");
            // Debug.Log($"  남은 시간: {_cachedGameData.RemainingPhaseTime:F1}초");
            // Debug.Log($"  선공자: {_cachedGameData.FirstAttackerPlayerId}");
            // Debug.Log($"  씨: {_cachedGameData.CurrentSceneName}");
            // Debug.Log($"  캐시 시각: {_cachedGameDataCapturedRealtime:F3}s");
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
        int migrationStartFrame = Time.frameCount;
        
        // 기존 Runner 정리를 위해 잠시 대기
        yield return new WaitForSeconds(0.5f);
        
        Debug.Log("[STEP 2] 대기 완료, 기존 Runner 상태:");
        // Debug.Log($"  - oldRunner null? {oldRunner == null}");
        // Debug.Log($"  - oldRunner.IsRunning? {oldRunner?.IsRunning}");
        
        // 문서 권장 수순: old runner를 먼저 정리하고 새 runner를 시작한다.
        NetworkRunner runnerToCleanup = oldRunner;
        int oldRunnerShutdownCompletedFrame = -1;

        Debug.Log("<color=magenta>═══ [STEP 2.5] 기존 Runner 정리 ═══</color>");
        yield return ShutdownRunnerForMigration(runnerToCleanup, null, "STEP 2.5");
        oldRunnerShutdownCompletedFrame = Time.frameCount;
        
        Debug.Log("<color=magenta>═══ [STEP 3] 새 Runner로 세션 재시작 ═══</color>");
        
        // async 메서드를 별도로 실행하고 완료를 기다림
        var startTask = StartGameWithMigrationTokenAsync(hostMigrationToken);
        NetworkRunner newRunner = null;
        
        // 완료 대기 (최대 30초)
        float timeout = 30f;
        float elapsed = 0f;
        while (!startTask.IsCompleted && elapsed < timeout)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }
        
        if (!startTask.IsCompleted)
        {
            Debug.LogError("<color=red>[HostMigrationHandler] 세션 재시작 타임아웃!</color>");
            OnMigrationComplete();
            yield break;
        }
        
        if (startTask.IsFaulted)
        {
            string taskError = startTask.Exception?.GetBaseException()?.Message ?? "Unknown";
            Debug.LogError($"<color=red>[HostMigrationHandler] 세션 재시작 실패: {taskError}</color>");
            OnMigrationComplete();
            yield break;
        }
        newRunner = startTask.Result;
        
        if (newRunner == null || !newRunner.IsRunning)
        {
            Debug.LogError("<color=red>[HostMigrationHandler] 새 Runner 시작 실패!</color>");
            OnMigrationComplete();
            yield break;
        }

        var expectedMode = hostMigrationToken.GameMode;
        if (newRunner.GameMode != expectedMode)
        {
            Debug.LogError($"<color=red>[HostMigrationHandler] GameMode 불일치: expected={expectedMode}, actual={newRunner.GameMode}</color>");
            _migrationRecoverySucceeded = false;
            OnMigrationComplete();
            yield break;
        }

        Debug.Log("<color=magenta>═══ [STEP 4] 새 Runner 등록 완료 ═══</color>");
        Debug.Log($"<color=green>[STEP 4] 세션 재시작 성공! role={newRunner.GameMode}</color>");
        // Debug.Log($"  - {DescribeRunner(newRunner)}");
        
        // NetworkManager에 새 Runner 설정
        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.SetRunnerAfterMigration(newRunner);
            Debug.Log("[STEP 4] NetworkManager에 새 Runner 설정 완료");
        }

        int newRunnerReadyFrame = Time.frameCount;
        int coexistFrames = 0;
        if (oldRunnerShutdownCompletedFrame >= 0)
        {
            coexistFrames = Mathf.Max(0, oldRunnerShutdownCompletedFrame - newRunnerReadyFrame);
        }
        Debug.Log($"[STEP 4] migrationFrames total={newRunnerReadyFrame - migrationStartFrame}, oldRunnerShutdownFrame={oldRunnerShutdownCompletedFrame}, newRunnerReadyFrame={newRunnerReadyFrame}, coexistFrames={coexistFrames}");

        Debug.Log("<color=magenta>═══ [STEP 5] GameManagers 복원 시작 ═══</color>");
        
        // GameManagers Spawned 대기 및 복원
        yield return WaitAndRestoreGameManagers(newRunner);

        if (!_migrationRecoverySucceeded)
        {
            Debug.LogError("<color=red>[STEP 5] GameManagers 복원 게이트 실패 - 이후 단계 진행 중단</color>");
            OnMigrationComplete();
            yield break;
        }
        
        // New host must take over disconnected player slots with server-driven AI.
        if (_aiReconciliationCoroutine != null)
        {
            StopCoroutine(_aiReconciliationCoroutine);
            _aiReconciliationCoroutine = null;
        }

        _aiReconciliationCoroutine = StartCoroutine(ReconcileAIControllersAfterMigrationCoroutine(newRunner));
        
        // 완료!
        Debug.Log("[STEP 6] OnMigrationComplete 호출...");
        OnMigrationComplete();
    }
    
    /// <summary>
    /// 코루틴에서 await 결과를 명시적으로 다룰 수 있도록 Task를 반환합니다.
    /// </summary>
    private async System.Threading.Tasks.Task<NetworkRunner> StartGameWithMigrationTokenAsync(
        HostMigrationToken hostMigrationToken)
    {
        try
        {
            Debug.Log("[HostMigrationHandler] StartGameWithMigrationTokenAsync 시작...");
            var runner = await StartGameWithMigrationToken(hostMigrationToken);
            Debug.Log($"[HostMigrationHandler] StartGameWithMigrationToken 완료, runner: {runner?.name}");
            return runner;
        }
        catch (Exception e)
        {
            Debug.LogError($"[HostMigrationHandler] StartGameWithMigrationTokenAsync 예외: {e.Message}");
            // Debug.LogException(e);
            return null;
        }
    }

    private IEnumerator ShutdownRunnerForMigration(NetworkRunner runnerToCleanup, NetworkRunner newRunner, string stepLabel)
    {
        if (runnerToCleanup == null || runnerToCleanup == newRunner)
        {
            yield break;
        }

        Debug.Log($"[{stepLabel}] 기존 Runner Shutdown(HostMigration)...");
        System.Threading.Tasks.Task shutdownTask = null;
        try
        {
            if (NetworkManager.Instance != null)
            {
                runnerToCleanup.RemoveCallbacks(NetworkManager.Instance);
            }

            shutdownTask = runnerToCleanup.Shutdown(false, ShutdownReason.HostMigration, false);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[HostMigrationHandler] 기존 Runner 정리 중 예외 (무시됨): {e.Message}");
        }

        while (shutdownTask != null && !shutdownTask.IsCompleted)
        {
            yield return null;
        }

        if (shutdownTask != null && shutdownTask.IsFaulted)
        {
            Debug.LogWarning($"[HostMigrationHandler] 기존 Runner Shutdown Task 실패: {shutdownTask.Exception?.GetBaseException().Message}");
        }

        if (runnerToCleanup != null)
        {
            runnerToCleanup.enabled = false;
        }

        // Debug.Log($"[{stepLabel}] 기존 Runner Shutdown 완료");
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
            
            var requestedMode = hostMigrationToken.GameMode;
            Debug.Log($"[HostMigrationHandler] HostMigrationToken.GameMode={requestedMode}");

            // HostMigrationToken 기반으로 역할(Host/Client) 확정 후 시작
            var result = await newRunner.StartGame(new StartGameArgs()
            {
                GameMode = requestedMode,
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
            // Debug.LogException(e);
            return null;
        }
    }
    
    /// <summary>
    /// Host Migration 시 네트워크 오브젝트 복원 콜백
    /// Fusion 스냅샷에서 받은 resume 오브젝트를 새 Runner에 Spawn하고 상태를 복사합니다.
    /// </summary>
    private void HostMigrationResume(NetworkRunner runner)
    {
        // Debug.Log("<color=cyan>═══════════════════════════════════════════</color>");
        Debug.Log("<color=cyan>[HostMigrationHandler] HostMigrationResume 시작!</color>");
        // Debug.Log("<color=cyan>═══════════════════════════════════════════</color>");
        // Debug.Log($"  - Runner: {runner?.name}");
        // Debug.Log($"  - IsServer: {runner?.IsServer}");
        
        // Resume Snapshot 오브젝트 가져오기
        var resumeObjects = runner.GetResumeSnapshotNetworkObjects().ToList();
        Debug.Log($"<color=yellow>[HostMigrationHandler] Resume Snapshot 오브젝트 수: {resumeObjects.Count}</color>");
        
        int gameManagerCount = 0;
        int playerCount = 0;
        int unitCount = 0;
        int otherCount = 0;
        
        GameManagers restoredGM = null;  // ★ 복원된 GameManagers 저장
        
        // 복원된 오브젝트를 실제로 Spawn + 상태 복사
        foreach (var resumeNO in resumeObjects)
        {
            if (resumeNO == null) continue;
            
            // 클로저 캡처 안전성 확보
            NetworkObject resumeSource = resumeNO;
            NetworkObject spawnedNO = null;
            Vector3 resumePosition = resumeSource.transform.position;
            Quaternion resumeRotation = resumeSource.transform.rotation;

            // Fusion 권장 패턴: NetworkTRSP에서 네트워크 스냅샷 위치/회전을 직접 읽어 복원 정확도를 높인다.
            var trsp = resumeSource.GetComponent<NetworkTRSP>();
            if (trsp != null)
            {
                try
                {
                    resumePosition = trsp.Data.Position;
                    resumeRotation = trsp.Data.Rotation;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[HostMigrationHandler] TRSP pose read fallback ({resumeSource.name}): {e.Message}");
                }
            }

            try
            {
                spawnedNO = runner.Spawn(
                    resumeSource,
                    position: resumePosition,
                    rotation: resumeRotation,
                    inputAuthority: resumeSource.InputAuthority,
                    onBeforeSpawned: (r, no) =>
                    {
                        no.CopyStateFrom(resumeSource);
                    });
            }
            catch (Exception e)
            {
                Debug.LogError($"<color=red>[HostMigrationHandler] Resume Spawn 실패: {resumeSource.name} - {e.Message}</color>");
            }

            if (spawnedNO == null)
            {
                Debug.LogWarning($"<color=orange>[HostMigrationHandler] Spawn 결과 null: {resumeSource.name}</color>");
                continue;
            }

            // GameManagers 확인
            if (spawnedNO.TryGetComponent<GameManagers>(out var gm))
            {
                gameManagerCount++;
                restoredGM = gm;  // ★ 복원된 GameManagers 저장
                Debug.Log($"<color=green>[HostMigrationHandler] GameManagers 복원됨: {gm.currentRound} 라운드, 상태: {gm.currentState}</color>");
                // Debug.Log($"<color=green>  - HasStateAuthority: {spawnedNO.HasStateAuthority}</color>");
            }
            // PlayerManager 확인
            else if (spawnedNO.TryGetComponent<PlayerManager>(out var pm))
            {
                playerCount++;
                // Debug.Log($"  - Player {pm.playerId}, InputAuthority: {spawnedNO.InputAuthority}");
            }
            // Unit 확인
            else if (spawnedNO.TryGetComponent<Unit>(out var unit))
            {
                unitCount++;
            }
            else
            {
                otherCount++;
            }
        }

        // Scene NetworkObject는 Spawn 대상이 아니므로, 기존 scene object에 snapshot state를 복사한다.
        RestoreSceneObjectsFromSnapshot(runner);
        
        Debug.Log($"<color=cyan>[HostMigrationHandler] 복원 요약:</color>");
        // Debug.Log($"  - GameManagers: {gameManagerCount}");
        // Debug.Log($"  - Players: {playerCount}");
        // Debug.Log($"  - Units: {unitCount}");
        // Debug.Log($"  - Others: {otherCount}");
        
        // ★★★ 핵심: GameManagers.Instance를 새 Runner에서 복원된 객체로 교체 ★★★
        if (restoredGM != null)
        {
            Debug.Log("<color=magenta>[HostMigrationHandler] GameManagers.Instance를 새 Runner의 객체로 교체!</color>");
            // Debug.Log($"  - 기존 Instance: {(GameManagers.Instance != null ? GameManagers.Instance.GetHashCode().ToString() : "null")}");
            Debug.Log($"  - 새 Instance: {restoredGM.GetHashCode()}");
            
            GameManagers.Instance = restoredGM;
            _restoredGameManagersCandidate = restoredGM;
            
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
        // Debug.Log($"  - Scene Objects: {sceneCount}");
        
        // 현재 존재하는 오브젝트 수 확인
        var allObjects = runner.GetAllNetworkObjects();
        Debug.Log($"<color=green>[HostMigrationHandler] 총 NetworkObject 수: {allObjects?.Count ?? 0}</color>");
        Debug.Log($"[HostMigrationHandler][Resume 상세]\n{BuildGameManagersDump(runner, restoredGM)}");
        // Debug.Log("<color=cyan>═══════════════════════════════════════════</color>");
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
        int skipped = 0;
        
        // Scene 오브젝트 순회 - (runtime scene object, snapshot source)
        foreach (var tuple in sceneObjects)
        {
            try
            {
                NetworkObject sceneNO = tuple.Item1;
                var resumeSource = tuple.Item2;
                if (sceneNO == null)
                {
                    skipped++;
                    continue;
                }
                
                Debug.Log($"[HostMigrationHandler] Scene 오브젝트 복원: {sceneNO.name}");
                
                // Fusion 2.x: Item2는 NetworkObjectHeaderPtr 이므로 그대로 CopyStateFrom에 전달한다.
                sceneNO.CopyStateFrom(resumeSource);
                count++;
            }
            catch (Exception e)
            {
                skipped++;
                Debug.LogWarning($"[HostMigrationHandler] Scene 오브젝트 복원 실패 - {e.Message}");
            }
        }
        
        Debug.Log($"<color=yellow>[HostMigrationHandler] Scene 오브젝트 복원 완료: copied={count}, skipped={skipped}</color>");
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
    private IEnumerator WaitAndRestoreGameManagers(NetworkRunner expectedRunner)
    {
        Debug.Log("<color=yellow>[STEP 5.1] WaitAndRestoreGameManagers 코루틴 시작</color>");
        
        float waitTime = 0f;
        const float maxWaitTime = 5f;
        
        if (expectedRunner == null)
        {
            Debug.LogError("[STEP 5.1] expectedRunner가 null입니다. 복원 중단");
            yield break;
        }

        Debug.Log("[STEP 5.1] GameManagers 복원 대기 시작...");
        Debug.Log($"[STEP 5.1] GameManagers.Instance: {(GameManagers.Instance != null ? "exists" : "NULL")}");

        // GameManagers가 준비될 때까지 대기
        while (waitTime < maxWaitTime)
        {
            var gm = ResolveGameManagersForRunner(expectedRunner);
            bool instanceExists = gm != null;
            bool isReady = instanceExists && gm.IsReadyForNetworkAccess;
            bool runnerMatched = instanceExists && gm.Runner == expectedRunner;
            bool hasAuthority = !expectedRunner.IsServer || (instanceExists && gm.Object != null && gm.Object.IsValid && gm.Object.HasStateAuthority);
            bool resumeGateSatisfied = runnerMatched && hasAuthority;
            bool verboseProbe = waitTime == 0 || (int)(waitTime * 10) % 5 == 0;
            string playersNotReadyReason = string.Empty;
            bool playersReady = instanceExists && EnsurePlayersRuntimeReady(
                expectedRunner,
                "WaitAndRestoreGameManagers",
                verboseProbe,
                out playersNotReadyReason);
            
            // 0.5초마다만 로그 출력 (너무 많은 로그 방지)
            if (verboseProbe)
            {
                Debug.Log($"[STEP 5.2] 대기 중... ({waitTime:F1}s) - Instance: {instanceExists}, Ready: {isReady}, RunnerMatched: {runnerMatched}, HasAuthority: {hasAuthority}, ResumeGate: {resumeGateSatisfied}, PlayersReady: {playersReady}");
                Debug.Log($"[STEP 5.2][DETAIL]\n{BuildGameManagersDump(expectedRunner, gm)}");
                if (!playersReady && !string.IsNullOrEmpty(playersNotReadyReason))
                {
                    Debug.LogWarning($"[STEP 5.2][PLAYERS] 런타임 준비 대기 중: {playersNotReadyReason}");
                }
                if (!resumeGateSatisfied)
                {
                    Debug.LogWarning($"[STEP 5.2][GATE] 재개 조건 미충족: runnerMatched={runnerMatched}, hasAuthority={hasAuthority}");
                }
            }
            
            if (instanceExists && expectedRunner.IsServer && gm.Object != null && !gm.Object.HasStateAuthority)
            {
                gm.Object.RequestStateAuthority();
            }

            if (isReady && resumeGateSatisfied && playersReady)
            {
                Debug.Log("<color=cyan>[STEP 5.3] GameManagers 준비 완료!</color>");
                
                // 추가 상태 정보 로깅
                Debug.Log($"[STEP 5.3] 복원 전 상태:");
                // Debug.Log($"  - Round: {gm.currentRound}");
                // Debug.Log($"  - State: {gm.currentState}");
                // Debug.Log($"  - Timer: {gm.currentPhaseTimer:F1}s");
                // Debug.Log($"  - HasStateAuthority: {gm.Object?.HasStateAuthority}");
                
                TryApplyCachedStateBeforeRestore(gm, "WaitAndRestoreGameManagers.Ready");
                Debug.Log("[STEP 5.3] RestoreAfterHostMigration 호출...");
                gm.RestoreAfterHostMigration();
                _migrationRecoverySucceeded = true;
                
                Debug.Log("<color=green>[STEP 5.4] GameManagers 로컬 상태 복원 완료!</color>");
                yield break;
            }
            
            yield return new WaitForSeconds(0.1f);
            waitTime += 0.1f;
        }
        
        // 시간 초과 - 수동 복원 시도
        Debug.LogWarning("<color=orange>[STEP 5] GameManagers 대기 시간 초과! (5초)</color>");
        Debug.LogWarning("[STEP 5] 수동 복원 시도...");
        
        var timeoutGM = ResolveGameManagersForRunner(expectedRunner);
        if (timeoutGM != null)
        {
            bool timeoutIsReady = timeoutGM.IsReadyForNetworkAccess;
            bool timeoutPlayersReady = EnsurePlayersRuntimeReady(expectedRunner, "WaitAndRestoreGameManagers.Timeout", true, out string timeoutPlayersReason);
            bool timeoutRunnerMatched = timeoutGM.Runner == expectedRunner;
            bool timeoutHasAuthority = !expectedRunner.IsServer
                || (timeoutGM.Object != null && timeoutGM.Object.IsValid && timeoutGM.Object.HasStateAuthority);
            if (!timeoutRunnerMatched || !timeoutHasAuthority || !timeoutIsReady || !timeoutPlayersReady)
            {
                Debug.LogError($"<color=red>[STEP 5] 복원 중단: runnerMatched={timeoutRunnerMatched}, hasAuthority={timeoutHasAuthority}, isReady={timeoutIsReady}, playersReady={timeoutPlayersReady}, playersReason={timeoutPlayersReason}</color>");
                _migrationRecoverySucceeded = false;
                yield break;
            }
            
            try
            {
                TryApplyCachedStateBeforeRestore(timeoutGM, "WaitAndRestoreGameManagers.Timeout");
                timeoutGM.RestoreAfterHostMigration();
                Debug.Log("<color=yellow>[STEP 5] 대기 타임아웃 후 복원 완료 (안전 게이트 통과)</color>");
                _migrationRecoverySucceeded = true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[STEP 5] 대기 타임아웃 복원 중 예외: {e.Message}");
                _migrationRecoverySucceeded = false;
            }
        }
        else
        {
            Debug.LogError("<color=red>[STEP 5] 새 Runner 소속 GameManagers를 찾지 못했습니다. 복원 중단</color>");
            _migrationRecoverySucceeded = false;
        }

        if (expectedRunner.IsServer && timeoutGM != null && timeoutGM.Object != null && !timeoutGM.Object.HasStateAuthority)
        {
            Debug.LogError("<color=red>[STEP 5] 새 Host인데 GameManagers StateAuthority를 획득하지 못했습니다. GameFlow 재개 중단</color>");
            _migrationRecoverySucceeded = false;
            yield break;
        }
        
        Debug.Log("<color=yellow>[STEP 5] WaitAndRestoreGameManagers 코루틴 종료</color>");
    }

    private bool EnsurePlayersRuntimeReady(NetworkRunner expectedRunner, string context, bool verboseLog, out string notReadySummary)
    {
        notReadySummary = string.Empty;
        if (expectedRunner == null)
        {
            notReadySummary = "expectedRunner=null";
            return false;
        }

        var players = UnityEngine.Object.FindObjectsOfType<PlayerManager>(true)
            .Where(player => player != null && player.Runner == expectedRunner)
            .Where(player => player.Object != null && player.Object.IsValid)
            .Where(player => player.playerId >= 0)
            .GroupBy(player => player.playerId)
            .Select(group => group
                .OrderByDescending(player => player.Object != null && player.Object.HasStateAuthority)
                .First())
            .ToList();

        if (players.Count == 0)
        {
            notReadySummary = "runnerPlayers=0(valid)";
            if (verboseLog)
            {
                Debug.LogWarning($"[HostMigrationHandler] {context}: expectedRunner 소속 PlayerManager가 없습니다.");
            }
            return false;
        }

        var notReady = new List<string>();
        foreach (var player in players)
        {
            if (player.Object == null || !player.Object.IsValid)
            {
                continue;
            }

            player.RebindRuntimeReferencesAfterMigration($"HostMigrationHandler.{context}", verboseLog);
            if (!player.IsRuntimeReady(out string reason))
            {
                notReady.Add($"P{player.playerId}:{reason}");
            }
        }

        if (notReady.Count > 0)
        {
            notReadySummary = string.Join(", ", notReady);
            if (verboseLog)
            {
                Debug.LogWarning($"[HostMigrationHandler] {context}: 플레이어 런타임 준비 미완료 - {notReadySummary}");
            }
            return false;
        }

        return true;
    }

    /// <summary>
    /// Host Migration 직후, 입력 권한 소유자가 사라진 슬롯을 AI가 이어받도록 반복 보정합니다.
    /// </summary>
    private IEnumerator ReconcileAIControllersAfterMigrationCoroutine(NetworkRunner runner)
    {
        if (runner == null || !runner.IsRunning || !runner.IsServer)
        {
            _aiTakeoverReady = true;
            _aiReconciliationCoroutine = null;
            yield break;
        }

        _aiTakeoverReady = false;
        float elapsed = 0f;
        const float passInterval = 0.5f;
        const float maxDuration = 8f;
        int stablePasses = 0;

        while (elapsed < maxDuration)
        {
            bool pending = EnsureAIControllersAfterMigration(runner);
            if (pending)
            {
                stablePasses = 0;
            }
            else
            {
                stablePasses++;
                if (stablePasses >= 2)
                {
                    _aiTakeoverReady = true;
                    _aiReconciliationCoroutine = null;
                    Debug.Log($"[HostMigrationHandler] AI takeover reconciliation complete. elapsed={elapsed:F1}s");
                    yield break;
                }
            }

            yield return new WaitForSeconds(passInterval);
            elapsed += passInterval;
        }

        _aiTakeoverReady = true;
        _aiReconciliationCoroutine = null;
        Debug.LogWarning("[HostMigrationHandler] AI takeover reconciliation timeout. Proceeding with best-effort state.");
    }

    /// <summary>
    /// 입력 권한 소유자가 사라진 슬롯을 AI가 이어받도록 보정합니다.
    /// true를 반환하면 아직 추가 reconciliation이 필요함을 의미합니다.
    /// </summary>
    private bool EnsureAIControllersAfterMigration(NetworkRunner runner)
    {
        if (runner == null || !runner.IsRunning || !runner.IsServer)
        {
            return false;
        }

        var gm = ResolveGameManagersForRunner(runner);
        if (gm == null)
        {
            Debug.LogWarning("[HostMigrationHandler] EnsureAIControllersAfterMigration skipped: GameManagers is null.");
            return true;
        }

        if (gm.CommandProcessor == null)
        {
            Debug.LogWarning("[HostMigrationHandler] EnsureAIControllersAfterMigration skipped: CommandProcessor is null.");
            return true;
        }

        var activePlayers = new HashSet<PlayerRef>(runner.ActivePlayers);
        var players = UnityEngine.Object.FindObjectsOfType<PlayerManager>(true)
            .Where(player => player != null && player.Runner == runner)
            .Where(player => player.Object != null && player.Object.IsValid)
            .Where(player => player.playerId >= 0)
            .ToList();

        int attached = 0;
        int removed = 0;
        bool pending = false;

        foreach (var player in players)
        {
            var no = player.Object;
            if (no == null || !no.IsValid)
            {
                continue;
            }

            bool hasInput = no.InputAuthority != PlayerRef.None;
            bool inputOwnerDisconnected = hasInput && !activePlayers.Contains(no.InputAuthority);

            if (inputOwnerDisconnected)
            {
                try
                {
                    no.AssignInputAuthority(PlayerRef.None);
                    Debug.Log($"[HostMigrationHandler] Player {player.playerId} input owner left; AI takeover enabled.");
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[HostMigrationHandler] Failed to clear input authority for Player {player.playerId}: {e.Message}");
                    pending = true;
                }
            }

            bool shouldRunAI = no.InputAuthority == PlayerRef.None;
            var aiController = player.GetComponent<AIPlayerController>();

            if (shouldRunAI)
            {
                player.RebindRuntimeReferencesAfterMigration("HostMigrationHandler.AITakeover", false);

                if (player.fieldManager == null)
                {
                    pending = true;
                    continue;
                }

                player.fieldManager.RebuildWallMapsAfterMigration(
                    "HostMigrationHandler.AITakeover",
                    false,
                    out _,
                    forceRebuild: true);

                if (!player.IsRuntimeReady(out _) || !player.fieldManager.IsWallMapReady)
                {
                    pending = true;
                    continue;
                }

                if (aiController == null)
                {
                    player.mazePlanned = false;
                    player.mazePlannedOrder.Clear();
                    player.mazeBuildCursor = 0;
                    player.mazeConstructionComplete = false;
                    player.unitPurchaseComplete = false;

                    aiController = player.gameObject.AddComponent<AIPlayerController>();
                    aiController.Initialize(player, gm.CommandProcessor);
                    attached++;
                    Debug.Log($"[HostMigrationHandler] AI controller attached to Player {player.playerId}.");
                }
            }
            else if (aiController != null)
            {
                UnityEngine.Object.Destroy(aiController);
                removed++;
                Debug.Log($"[HostMigrationHandler] AI controller removed from human Player {player.playerId}.");
            }
        }

        Debug.Log($"[HostMigrationHandler] AI takeover pass complete. attached={attached}, removed={removed}, activePlayers={activePlayers.Count}");
        return pending;
    }

    /// <summary>
    /// 새 Runner 기준으로 복원 대상 GameManagers를 결정하고, Instance를 강제로 재바인딩합니다.
    /// 재개 조건 1: GameManagers.Instance.Runner == expectedRunner 를 보장합니다.
    /// </summary>
    private GameManagers ResolveGameManagersForRunner(NetworkRunner expectedRunner)
    {
        if (expectedRunner == null) return null;

        if (GameManagers.Instance != null && GameManagers.Instance.Runner == expectedRunner)
        {
            return GameManagers.Instance;
        }

        GameManagers candidate = null;
        foreach (var no in expectedRunner.GetAllNetworkObjects())
        {
            if (no != null && no.TryGetComponent<GameManagers>(out var gm))
            {
                candidate = gm;
                break;
            }
        }

        if (candidate == null)
        {
            var allGameManagers = UnityEngine.Object.FindObjectsOfType<GameManagers>(true);
            candidate = allGameManagers.FirstOrDefault(gm => gm != null && gm.Runner == expectedRunner);
        }

        if (candidate == null
            && _restoredGameManagersCandidate != null
            && _restoredGameManagersCandidate.Runner == expectedRunner
            && _restoredGameManagersCandidate.Object != null
            && _restoredGameManagersCandidate.Object.IsValid)
        {
            candidate = _restoredGameManagersCandidate;
        }

        if (candidate != null && GameManagers.Instance != candidate)
        {
            Debug.Log($"<color=magenta>[HostMigrationHandler] GameManagers.Instance 재바인딩: oldRunner={(GameManagers.Instance?.Runner != null ? GameManagers.Instance.Runner.name : "null")} -> newRunner={expectedRunner.name}</color>");
            GameManagers.Instance = candidate;
        }

        if (candidate == null && (Time.unscaledTime - _lastResolveDebugLogTime) > 0.5f)
        {
            _lastResolveDebugLogTime = Time.unscaledTime;
            Debug.LogWarning($"[HostMigrationHandler] ResolveGameManagersForRunner 실패\n{BuildGameManagersDump(expectedRunner, null)}");
        }

        return candidate;
    }

    private static string DescribeRunner(NetworkRunner runner)
    {
        if (runner == null)
        {
            return "Runner=NULL";
        }

        string sceneManagerType = runner.SceneManager != null ? runner.SceneManager.GetType().Name : "null";
        string goName = runner.gameObject != null ? runner.gameObject.name : "null";
        bool goActive = runner.gameObject != null && runner.gameObject.activeInHierarchy;
        return $"Runner(name={runner.name}, isRunning={runner.IsRunning}, mode={runner.GameMode}, isServer={runner.IsServer}, sceneManager={sceneManagerType}, go={goName}, active={goActive})";
    }

    private static string DescribeGameManagers(GameManagers gm)
    {
        if (gm == null)
        {
            return "GameManagers=NULL";
        }

        var obj = gm.Object;
        var runner = gm.Runner;
        string runnerName = runner != null ? runner.name : "null";
        bool runnerRunning = runner != null && runner.IsRunning;
        bool objectValid = obj != null && obj.IsValid;
        string stateAuth = obj != null ? obj.HasStateAuthority.ToString() : "null";
        return $"GM(name={gm.name}, instanceId={gm.GetInstanceID()}, hash={gm.GetHashCode()}, runner={runnerName}, runnerRunning={runnerRunning}, objectValid={objectValid}, stateAuth={stateAuth}, ready={gm.IsReadyForNetworkAccess}, active={gm.gameObject.activeInHierarchy})";
    }

    private string BuildGameManagersDump(NetworkRunner expectedRunner, GameManagers resolved)
    {
        var sb = new StringBuilder(512);
        sb.AppendLine($"expectedRunner: {DescribeRunner(expectedRunner)}");
        sb.AppendLine($"resolved: {DescribeGameManagers(resolved)}");
        sb.AppendLine($"staticInstance: {DescribeGameManagers(GameManagers.Instance)}");
        sb.AppendLine($"restoredCandidate: {DescribeGameManagers(_restoredGameManagersCandidate)}");

        var allGameManagers = UnityEngine.Object.FindObjectsOfType<GameManagers>(true);
        sb.AppendLine($"sceneGameManagersCount: {allGameManagers.Length}");
        for (int i = 0; i < allGameManagers.Length; i++)
        {
            sb.AppendLine($"  [{i}] {DescribeGameManagers(allGameManagers[i])}");
        }

        if (expectedRunner != null)
        {
            var allObjects = expectedRunner.GetAllNetworkObjects();
            sb.AppendLine($"runnerNetworkObjectsCount: {allObjects?.Count ?? -1}");
            if (allObjects != null)
            {
                int gmCount = 0;
                foreach (var no in allObjects)
                {
                    if (no != null && no.TryGetComponent<GameManagers>(out var gm))
                    {
                        gmCount++;
                        sb.AppendLine($"  runnerGM[{gmCount}] NO(name={no.name}, id={no.Id}, stateAuth={no.HasStateAuthority}) => {DescribeGameManagers(gm)}");
                    }
                }
                sb.AppendLine($"runnerGameManagersCount: {gmCount}");
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Host Migration 완료 처리
    /// </summary>
    private void OnMigrationComplete()
    {
        _isMigrating = false;
        ShowMigrationUI(false);

        _restoredGameManagersCandidate = null;

        // 캐시 클리어
        _cachedPlayerData.Clear();

        // [Observer Pattern] Migration 완료 이벤트 발행
        bool isNewHost = NetworkManager.Instance?._runner?.IsServer ?? false;
        GameEvents.TriggerHostMigrationCompleted(isNewHost);

        if (_migrationRecoverySucceeded)
        {
            StartCoroutine(RunMigrationSmokeChecksCoroutine());

            // Debug.Log("<color=green>═══════════════════════════════════════════</color>");
            Debug.Log($"<color=green>[MIGRATION COMPLETE] Host Migration 성공!</color>");
            // Debug.Log($"<color=green>  역할: {(isNewHost ? "새 Host" : "클라이언트")}</color>");
            // Debug.Log("<color=green>  게임이 계속됩니다!</color>");
            // Debug.Log("<color=green>═══════════════════════════════════════════</color>");
        }
        else
        {
            _aiTakeoverReady = true;
            // Debug.Log("<color=red>═══════════════════════════════════════════</color>");
            Debug.Log("<color=red>[MIGRATION COMPLETE] 마이그레이션은 끝났지만 게임 복원은 실패했습니다.</color>");
            Debug.Log("<color=red>  새 Runner/권한/복원 오브젝트 상태를 확인하세요.</color>");
            // Debug.Log("<color=red>═══════════════════════════════════════════</color>");
        }
    }

    /// <summary>
    /// Host Migration 복원 직후 핵심 불변조건을 자동 점검하는 최소 smoke 루틴입니다.
    /// </summary>
    private IEnumerator RunMigrationSmokeChecksCoroutine()
    {
        const float timeout = 5f;
        float waited = 0f;

        NetworkRunner expectedRunner = NetworkManager.Instance?._runner;
        GameManagers gm = ResolveGameManagersForRunner(expectedRunner);

        while (waited < timeout && (gm == null || !gm.IsReadyForNetworkAccess))
        {
            yield return new WaitForSeconds(0.1f);
            waited += 0.1f;
            expectedRunner = NetworkManager.Instance?._runner;
            gm = ResolveGameManagersForRunner(expectedRunner);
        }

        var errors = new List<string>();
        if (expectedRunner == null)
        {
            errors.Add("expectedRunner=null");
        }

        if (gm == null)
        {
            errors.Add("gameManagers=null");
        }
        else
        {
            if (gm.Runner != expectedRunner)
            {
                errors.Add("gameManagers.runner mismatch");
            }

            var allPlayers = gm.AllPlayers?.Where(p => p != null).ToList() ?? new List<PlayerManager>();
            var runtimePlayers = UnityEngine.Object.FindObjectsOfType<PlayerManager>(true)
                .Where(p => p != null && p.Object != null && p.Object.IsValid && p.Runner == expectedRunner)
                .ToList();

            if (allPlayers.Count != runtimePlayers.Count)
            {
                errors.Add($"allPlayers mismatch gm={allPlayers.Count}, runtime={runtimePlayers.Count}");
            }

            foreach (var runtimePlayer in runtimePlayers)
            {
                if (runtimePlayer == null || runtimePlayer.fieldManager == null)
                {
                    errors.Add($"runtimePlayer fieldManager missing: P{runtimePlayer?.playerId}");
                    continue;
                }

                runtimePlayer.fieldManager.RebuildWallMapsAfterMigration("HM-SMOKE", false, out string wallSummary);
                if (!runtimePlayer.fieldManager.IsWallMapReady)
                {
                    errors.Add($"wallMap not ready: P{runtimePlayer.playerId} ({wallSummary})");
                }

                runtimePlayer.fieldManager.RebuildUnitMapAfterMigration("HM-SMOKE", false, out string unitSummary);
                if (!runtimePlayer.fieldManager.IsUnitMapReady)
                {
                    errors.Add($"unitMap not ready: P{runtimePlayer.playerId} ({unitSummary})");
                }

                var units = runtimePlayer.fieldManager.GetAlliedUnitsOnField();
                int missingDataCount = units.Count(unit => unit != null && unit.Data == null);
                int missingProxyCount = units.Count(unit => unit != null && !unit.HasAnimationEventProxy());
                if (missingDataCount > 0)
                {
                    errors.Add($"unitDataMissing: P{runtimePlayer.playerId} count={missingDataCount}");
                }
                if (missingProxyCount > 0)
                {
                    errors.Add($"unitAnimProxyMissing: P{runtimePlayer.playerId} count={missingProxyCount}");
                }

                bool prepareState = gm != null && gm.GetGameState() == GameManagers.GameState.Prepare;
                if (prepareState && !runtimePlayer.TryGetShopSnapshot(
                        out _,
                        out _,
                        out _,
                        out _,
                        out _))
                {
                    errors.Add($"shopSnapshotMissing: P{runtimePlayer.playerId}");
                }

                bool battleState = gm != null &&
                                   (gm.GetGameState() == GameManagers.GameState.Battle1 ||
                                    gm.GetGameState() == GameManagers.GameState.Battle2);
                if (battleState && runtimePlayer.IsAttackerInCurrentBattle)
                {
                    int opponentId = gm.GetBattleOpponent(runtimePlayer.playerId);
                    if (opponentId >= 0)
                    {
                        var defender = gm.GetPlayer(opponentId);
                        bool aiAttacker = ComponentRegistry.Has<AIPlayerController>(runtimePlayer.playerId.ToString());
                        bool attackerHasPool = runtimePlayer.AttackMonsterPool != null &&
                                               runtimePlayer.AttackMonsterPool.Any(entry => entry != null && !entry.IsEmpty);
                        bool defenderHasMonsters = defender != null &&
                                                   defender.monsterSpawner != null &&
                                                   defender.monsterSpawner.HasLivingMonsters();

                        if (aiAttacker && attackerHasPool && !defenderHasMonsters)
                        {
                            errors.Add($"battleSpawnPending: A{runtimePlayer.playerId}->D{opponentId}");
                        }
                    }
                }
            }
        }

        if (expectedRunner != null && expectedRunner.IsServer && !_aiTakeoverReady)
        {
            errors.Add("aiTakeoverReady=false");
        }

        if (errors.Count == 0)
        {
            Debug.Log($"<color=green>[HM-SMOKE] PASS waited={waited:F1}s runner={DescribeRunner(expectedRunner)} gm={DescribeGameManagers(gm)}</color>");
        }
        else
        {
            Debug.LogError($"<color=red>[HM-SMOKE] FAIL waited={waited:F1}s errors={string.Join(" | ", errors)}</color>");
        }
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
    /// Host Migration snapshot gap을 줄이기 위해 중요 전환 직전에 수동 snapshot push를 시도합니다.
    /// Fusion 버전별 API 차이를 고려해 reflection으로 안전 호출합니다.
    /// </summary>
    public bool TryPushHostMigrationSnapshot(NetworkRunner runner, string reason)
    {
        if (runner == null || !runner.IsRunning || !runner.IsServer)
        {
            return false;
        }

        if (!_pushHostMigrationSnapshotMethodResolved)
        {
            _pushHostMigrationSnapshotMethodResolved = true;
            var methods = typeof(NetworkRunner)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.Name == "PushHostMigrationSnapshot")
                .ToList();

            _pushHostMigrationSnapshotMethod =
                methods.FirstOrDefault(m => m.GetParameters().Length == 0) ??
                methods.FirstOrDefault(m =>
                {
                    var p = m.GetParameters();
                    return p.Length == 1 && p[0].ParameterType == typeof(bool);
                });
        }

        if (_pushHostMigrationSnapshotMethod == null)
        {
            if (!_pushHostMigrationSnapshotUnsupportedLogged)
            {
                _pushHostMigrationSnapshotUnsupportedLogged = true;
                Debug.LogWarning("[HostMigrationHandler] PushHostMigrationSnapshot API를 찾지 못했습니다. AutoUpdate snapshot에만 의존합니다.");
            }
            return false;
        }

        try
        {
            var parameters = _pushHostMigrationSnapshotMethod.GetParameters();
            if (parameters.Length == 0)
            {
                _pushHostMigrationSnapshotMethod.Invoke(runner, null);
            }
            else
            {
                object arg = parameters[0].HasDefaultValue ? parameters[0].DefaultValue : false;
                _pushHostMigrationSnapshotMethod.Invoke(runner, new object[] { arg });
            }

            Debug.Log($"[HostMigrationHandler] HostMigration snapshot push 성공 ({reason})");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[HostMigrationHandler] HostMigration snapshot push 실패 ({reason}): {e.Message}");
            return false;
        }
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

    private void TryApplyCachedStateBeforeRestore(GameManagers gm, string context)
    {
        if (gm == null)
        {
            return;
        }

        if (_cachedGameData.CurrentRound <= 0)
        {
            return;
        }

        float elapsedSinceCache = 0f;
        if (_cachedGameDataCapturedRealtime >= 0f)
        {
            elapsedSinceCache = Mathf.Max(0f, Time.realtimeSinceStartup - _cachedGameDataCapturedRealtime);
        }

        bool applied = gm.TryApplyCachedStateForMigration(
            _cachedGameData,
            elapsedSinceCache,
            context);

        bool battleApplied = gm.TryRestoreBattleSnapshotForMigration(
            _cachedGameData.BattleOpponentsSnapshot,
            _cachedGameData.MatchFirstAttackerSnapshot,
            _cachedGameData.FirstAttackerPlayerId,
            context);

        if (applied || battleApplied)
        {
            Debug.Log($"<color=magenta>[HostMigrationHandler] 캐시 상태 적용 성공 ({context}) - elapsed={elapsedSinceCache:F2}s, stateApplied={applied}, battleApplied={battleApplied}</color>");
        }
    }
}
