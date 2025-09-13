// Assets/Scripts/Network/NetworkManager.cs
// ✅ [상세 분석] 각 함수의 역할과 잠재적 문제점 분석

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using UnityEngine;
using Fusion;
using Fusion.Sockets;
using UnityEngine.SceneManagement;
using System.IO;
using Fusion.Photon.Realtime;

public struct NetworkInputData : INetworkInput
{
}

/// <summary>
/// ✅ [전체 구조 분석]
/// 1. LobbyRunner (MatchingLobby): 모든 플레이어가 "SharedMainLobby"에 접속하여 방 목록 공유
/// 2. GameRunner (JoinLobby/Game): 개별 게임 방에 참여하여 실제 게임 플레이
/// 
/// ⚠️ [잠재적 문제점]
/// - CustomLobbyName 불일치로 인한 방 목록 분리
/// - 방 생성 후 OnSessionListUpdated 콜백 지연
/// - 네트워크 지연으로 인한 방 목록 동기화 시간차
/// </summary>
public class NetworkManager : MonoBehaviour, INetworkRunnerCallbacks
{
    public static NetworkManager Instance { get; private set; }

    [Header("Network Settings")]
    [SerializeField] private int _maxPlayers = 2;
    [SerializeField] private GameObject _networkPlayerPrefab;
    
    [Header("Debug")]
    [SerializeField] private bool _showDebugInfo = true;
    [SerializeField] private bool _verboseLogging = true;
    [SerializeField] private bool _extraVerboseLogging = true; // ✅ [추가] 매우 상세한 로그

    // ✅ [핵심 설계] 이중 Runner 구조
    private NetworkRunner _lobbyRunner;    // 🏠 공통 로비 - 방 목록 브로드캐스팅
    private NetworkRunner _gameRunner;     // 🎮 개별 방 - 실제 게임 세션
    
    private string _previousSceneToUnload;
    private Dictionary<string, SessionInfo> _roomList = new Dictionary<string, SessionInfo>();
    private DateTime _lastRoomListUpdate = DateTime.MinValue;
    private readonly TimeSpan _roomListUpdateInterval = TimeSpan.FromSeconds(1); // ✅ [수정] 더 빠른 업데이트
    
    private string _playerNickname;
    private string _playerPassword;
    private string _currentRoomName;
    
    private bool _isConnectedToServer = false;
    public event Action<List<SessionInfo>> OnRoomListUpdated;
    public event Action<bool> OnConnectionStatusChanged;
    public event Action<string> OnErrorOccurred;
    public event Action<bool> OnServerConnected;
    public event Action<int> OnRoomPlayerCountChanged;
    public event Action<string> OnRoomCreationStarted;
    public event Action<string, bool> OnRoomCreationCompleted;

    // ✅ [중요] 모든 클라이언트가 동일해야 하는 정보
    private const string FIXED_APP_VERSION = "MDF_1.0";           // 🔄 앱 버전 일치 필수
    private const string FIXED_REGION = "asia";                   // 🔄 지역 일치 필수  
    private const string FIXED_LOBBY_NAME = "MainLobby";          // 🔄 로비 이름 일치 필수
    private const string SHARED_LOBBY_SESSION_NAME = "SharedMainLobby"; // 🔄 공통 로비 세션 이름 일치 필수

    private bool _isRefreshingList = false;

    #region Unity Lifecycle & Initialization

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            gameObject.name = "NetworkManager (Singleton)";
            EnsurePhotonSettings();
        }
        else if (Instance != this)
        {
            Destroy(gameObject);
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            DisconnectCompletely();
            Instance = null;
        }
    }
    
    /// <summary>
    /// ✅ [중요 함수] Photon 설정 일치 보장
    /// 역할: 모든 클라이언트가 같은 앱 버전, 지역을 사용하도록 강제 설정
    /// 문제점: 빌드와 에디터 간 설정 차이 가능성
    /// </summary>
    private void EnsurePhotonSettings()
    {
        try
        {
            var appSettings = Resources.Load<Fusion.Photon.Realtime.PhotonAppSettings>("PhotonAppSettings");
            
            if (appSettings != null)
            {
                bool needsSave = false;
                
                if (appSettings.AppSettings.AppVersion != FIXED_APP_VERSION)
                {
                    if (_extraVerboseLogging) Debug.Log($"[NetworkManager] AppVersion 수정: {appSettings.AppSettings.AppVersion} → {FIXED_APP_VERSION}");
                    appSettings.AppSettings.AppVersion = FIXED_APP_VERSION;
                    needsSave = true;
                }
                
                if (appSettings.AppSettings.FixedRegion != FIXED_REGION)
                {
                    if (_extraVerboseLogging) Debug.Log($"[NetworkManager] Region 수정: {appSettings.AppSettings.FixedRegion} → {FIXED_REGION}");
                    appSettings.AppSettings.FixedRegion = FIXED_REGION;
                    needsSave = true;
                }
                
                if (!appSettings.AppSettings.EnableLobbyStatistics)
                {
                    if (_extraVerboseLogging) Debug.Log($"[NetworkManager] EnableLobbyStatistics 활성화");
                    appSettings.AppSettings.EnableLobbyStatistics = true;
                    needsSave = true;
                }

#if UNITY_EDITOR
                if (needsSave)
                {
                    UnityEditor.EditorUtility.SetDirty(appSettings);
                    UnityEditor.AssetDatabase.SaveAssets();
                    Debug.Log("<color=green>[NetworkManager] PhotonAppSettings 업데이트 및 저장 완료</color>");
                }
#endif
                Debug.Log($"<color=green>[NetworkManager] ✅ Photon 설정 확인 완료</color>\n" +
                         $"  - AppVersion: {appSettings.AppSettings.AppVersion}\n" +
                         $"  - Region: {appSettings.AppSettings.FixedRegion}\n" +
                         $"  - LobbyStats: {appSettings.AppSettings.EnableLobbyStatistics}");
            }
            else
            {
                Debug.LogError("<color=red>[NetworkManager] ❌ PhotonAppSettings를 찾을 수 없습니다!</color>");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[NetworkManager] PhotonAppSettings 설정 중 오류: {e.Message}");
        }
    }

    /// <summary>
    /// ✅ [핵심 함수] LobbyRunner 초기화 및 관리
    /// 역할: 모든 클라이언트가 접속할 공통 로비 Runner 생성
    /// 중요: 이 Runner는 방 목록 브로드캐스팅을 담당
    /// </summary>
    private void InitializeLobbyRunner()
    {
        if (_lobbyRunner != null && _lobbyRunner.IsRunning)
        {
            Debug.Log("[NetworkManager] 🏠 LobbyRunner 이미 실행 중 - 유지");
            return;
        }
        
        // ✅ [정리] 기존 LobbyRunner 완전 정리
        if (_lobbyRunner != null)
        {
            if (_extraVerboseLogging) Debug.Log("[NetworkManager] 🧹 기존 LobbyRunner 정리 중...");
            _lobbyRunner.RemoveCallbacks(this);
            if (_lobbyRunner.IsRunning) _lobbyRunner.Shutdown();
            Destroy(_lobbyRunner.gameObject);
        }

        GameObject runnerGo = new GameObject("LobbyRunner (Shared Room List)");
        DontDestroyOnLoad(runnerGo);
        _lobbyRunner = runnerGo.AddComponent<NetworkRunner>();
        _lobbyRunner.AddCallbacks(this);
        
        Debug.Log("[NetworkManager] 🏠✅ LobbyRunner 초기화 완료 (공통 방 목록 관리)");
    }
    
    /// <summary>
    /// ✅ [게임 함수] GameRunner 초기화 및 관리  
    /// 역할: 개별 게임 방 참여/호스팅을 위한 Runner 생성
    /// 중요: 실제 게임 플레이와 플레이어 간 상호작용 담당
    /// </summary>
    private void InitializeGameRunner()
    {
        if (_gameRunner != null && _gameRunner.IsRunning) 
        {
            Debug.Log("[NetworkManager] 🎮 GameRunner 이미 실행 중 - 유지");
            return;
        }
        
        // ✅ [정리] 기존 GameRunner 완전 정리
        if (_gameRunner != null) 
        {
            if (_extraVerboseLogging) Debug.Log("[NetworkManager] 🧹 기존 GameRunner 정리 중...");
            _gameRunner.RemoveCallbacks(this);
            Destroy(_gameRunner.gameObject);
        }

        GameObject runnerGo = new GameObject("GameRunner (Individual Game Room)");
        _gameRunner = runnerGo.AddComponent<NetworkRunner>();
        _gameRunner.ProvideInput = true;
        _gameRunner.AddCallbacks(this);
        
        Debug.Log("[NetworkManager] 🎮✅ GameRunner 초기화 완료 (개별 게임 방 전용)");
    }
    
    #endregion

    #region Core Network Functions

    /// <summary>
    /// ✅ [1단계 - 서버 연결] 공통 로비 접속
    /// 역할: 모든 플레이어를 같은 "SharedMainLobby" 세션에 접속시켜 방 목록 공유 가능하게 함
    /// 성공 조건: LobbyRunner가 Shared 모드로 SharedMainLobby에 접속
    /// 실패 원인: 네트워크 문제, 앱 버전 불일치, 지역 불일치
    /// </summary>
    public async Task<bool> ConnectToServer(string nickname, string password)
    {
        _playerNickname = nickname;
        _playerPassword = password;
        PlayerPrefs.SetString("PlayerNickname", nickname);
        PlayerPrefs.SetString("PlayerPassword", password);
        PlayerPrefs.Save();
        
        Debug.Log($"[NetworkManager] 🌐 1단계: 공통 로비 접속 시도\n" +
                 $"  - 플레이어: {nickname}\n" +
                 $"  - 대상 세션: {SHARED_LOBBY_SESSION_NAME}\n" +
                 $"  - 로비 이름: {FIXED_LOBBY_NAME}");
        
        InitializeLobbyRunner();
        
        var args = new StartGameArgs
        {
            GameMode = GameMode.Shared,                    // 🔄 공유 모드 - 방 목록 브로드캐스팅
            SessionName = SHARED_LOBBY_SESSION_NAME,       // 🔄 모든 클라이언트 동일 세션
            CustomLobbyName = FIXED_LOBBY_NAME,           // 🔄 로비 이름 일치
            PlayerCount = 100                             // 🔄 대용량 로비
        };
        
        if (_extraVerboseLogging)
        {
            Debug.Log($"[NetworkManager] 🔧 StartGame 인자:\n" +
                     $"  - GameMode: {args.GameMode}\n" +
                     $"  - SessionName: {args.SessionName}\n" +
                     $"  - CustomLobbyName: {args.CustomLobbyName}\n" +
                     $"  - PlayerCount: {args.PlayerCount}");
        }
        
        var result = await _lobbyRunner.StartGame(args);
        
        if (result.Ok)
        {
            _isConnectedToServer = true;
            OnServerConnected?.Invoke(true);
            Debug.Log($"[NetworkManager] ✅ 1단계 성공: 공통 로비 '{SHARED_LOBBY_SESSION_NAME}' 접속 완료");
            SceneManager.LoadScene("MatchingLobby");
            return true;
        }
        else
        {
            string errorMsg = $"공통 로비 접속 실패: {result.ShutdownReason}";
            Debug.LogError($"[NetworkManager] ❌ 1단계 실패: {errorMsg}");
            OnErrorOccurred?.Invoke(errorMsg);
            OnServerConnected?.Invoke(false);
            return false;
        }
    }

    /// <summary>
    /// ✅ [2단계 - 방 생성] 개별 게임 방 생성 및 호스팅
    /// 역할: GameRunner로 새로운 게임 방을 생성하고 호스트가 됨
    /// 중요: 생성된 방이 공통 로비의 다른 플레이어들에게 브로드캐스팅되어야 함
    /// 
    /// ⚠️ [잠재적 문제점]
    /// 1. CustomLobbyName 불일치로 방이 다른 로비에 생성될 수 있음
    /// 2. 방 생성 후 OnSessionListUpdated 콜백이 즉시 호출되지 않을 수 있음
    /// 3. 네트워크 지연으로 인한 방 목록 동기화 시간차
    /// </summary>
    public async Task<bool> CreateRoom(string roomName, string sceneName = "JoinLobby")
    {
        OnRoomCreationStarted?.Invoke(roomName);
        
        if (_lobbyRunner == null || !_lobbyRunner.IsRunning)
        {
            string errorMsg = "공통 로비에 연결되어 있지 않습니다.";
            Debug.LogError($"[NetworkManager] ❌ 2단계 전제조건 실패: {errorMsg}");
            OnErrorOccurred?.Invoke(errorMsg);
            OnRoomCreationCompleted?.Invoke(roomName, false);
            return false;
        }

        try
        {
            Debug.Log($"[NetworkManager] 🏠 2단계: 개별 게임 방 생성 시작\n" +
                     $"  - 방 이름: {roomName}\n" +
                     $"  - 대상 씬: {sceneName}\n" +
                     $"  - 로비 이름: {FIXED_LOBBY_NAME}\n" +
                     $"  - 최대 인원: {_maxPlayers}명");
            
            InitializeGameRunner();
            
            int sceneIndex = SceneUtility.GetBuildIndexByScenePath($"Assets/Scenes/{sceneName}.unity");
            if (sceneIndex < 0)
            {
                string errorMsg = $"씬 '{sceneName}'을(를) 빌드 설정에서 찾을 수 없습니다.";
                Debug.LogError($"[NetworkManager] ❌ 씬 인덱스 오류: {errorMsg}");
                OnErrorOccurred?.Invoke(errorMsg);
                OnRoomCreationCompleted?.Invoke(roomName, false);
                return false;
            }

            string currentScene = SceneManager.GetActiveScene().name;
            if (!string.IsNullOrEmpty(currentScene) && currentScene != sceneName)
            {
                _previousSceneToUnload = currentScene;
                if (_extraVerboseLogging) Debug.Log($"[NetworkManager] 📂 이전 씬 저장: {_previousSceneToUnload}");
            }

            var gameArgs = new StartGameArgs
            {
                GameMode = GameMode.Host,                     // 🔄 호스트 모드 - 방 생성
                SessionName = roomName,                       // 🔄 생성할 방 이름
                Scene = SceneRef.FromIndex(sceneIndex),
                SceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>(),
                PlayerCount = _maxPlayers,
                CustomLobbyName = FIXED_LOBBY_NAME           // ⚠️ [중요] LobbyRunner와 동일한 로비 이름
            };

            if (_extraVerboseLogging)
            {
                Debug.Log($"[NetworkManager] 🔧 GameRunner StartGame 인자:\n" +
                         $"  - GameMode: {gameArgs.GameMode}\n" +
                         $"  - SessionName: {gameArgs.SessionName}\n" +
                         $"  - CustomLobbyName: {gameArgs.CustomLobbyName}\n" +
                         $"  - PlayerCount: {gameArgs.PlayerCount}\n" +
                         $"  - Scene: {sceneName} (Index: {sceneIndex})");
            }

            var result = await _gameRunner.StartGame(gameArgs);

            if (result.Ok)
            {
                _currentRoomName = roomName;
                OnConnectionStatusChanged?.Invoke(true);
                OnRoomCreationCompleted?.Invoke(roomName, true);
                
                Debug.Log($"[NetworkManager] ✅ 2단계 성공: 게임 방 '{roomName}' 생성 완료\n" +
                         $"  - GameRunner: Host 모드 활성화\n" +
                         $"  - LobbyRunner: 백그라운드에서 '{SHARED_LOBBY_SESSION_NAME}' 유지\n" +
                         $"  - 예상 결과: 다른 플레이어들이 이 방을 목록에서 확인 가능");
                
                // ✅ [추가] 방 생성 후 강제 방 목록 업데이트 시도
                if (_extraVerboseLogging)
                {
                    Debug.Log("[NetworkManager] 🔄 방 생성 후 2초 뒤 강제 방 목록 업데이트 예정");
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(2000);
                        await RefreshRoomList();
                    });
                }
                
                return true;
            }
            else
            {
                string errorMsg = $"게임 방 생성 실패: {result.ShutdownReason}";
                Debug.LogError($"[NetworkManager] ❌ 2단계 실패: {errorMsg}");
                OnErrorOccurred?.Invoke(errorMsg);
                OnRoomCreationCompleted?.Invoke(roomName, false);
                return false;
            }
        }
        catch (Exception e)
        {
            string errorMsg = $"방 생성 중 예외 발생: {e.Message}";
            Debug.LogError($"[NetworkManager] ❌ 2단계 예외: {errorMsg}\n스택트레이스: {e.StackTrace}");
            OnErrorOccurred?.Invoke(errorMsg);
            OnRoomCreationCompleted?.Invoke(roomName, false);
            return false;
        }
    }
    
    /// <summary>
    /// ✅ [3단계 - 방 참여] 기존 게임 방에 클라이언트로 참여
    /// 역할: GameRunner로 기존 게임 방에 클라이언트로 접속
    /// 중요: 호스트가 이미 생성한 방의 SessionName과 정확히 일치해야 함
    /// </summary>
    public async Task<bool> JoinRoom(string roomName, string sceneName = "JoinLobby")
    {
        if (_lobbyRunner == null || !_lobbyRunner.IsRunning)
        {
            OnErrorOccurred?.Invoke("공통 로비에 연결되어 있지 않습니다.");
            return false;
        }

        try
        {
            Debug.Log($"[NetworkManager] 🏠 3단계: 게임 방 참여 시작\n" +
                     $"  - 참여할 방: {roomName}\n" +
                     $"  - 대상 씬: {sceneName}");
            
            InitializeGameRunner();
            
            int sceneIndex = SceneUtility.GetBuildIndexByScenePath($"Assets/Scenes/{sceneName}.unity");
            if (sceneIndex < 0)
            {
                OnErrorOccurred?.Invoke($"씬 '{sceneName}'을(를) 빌드 설정에서 찾을 수 없습니다.");
                return false;
            }

            string currentScene = SceneManager.GetActiveScene().name;
            if (!string.IsNullOrEmpty(currentScene) && currentScene != sceneName)
            {
                _previousSceneToUnload = currentScene;
            }

            var result = await _gameRunner.StartGame(new StartGameArgs
            {
                GameMode = GameMode.Client,                   // 🔄 클라이언트 모드 - 방 참여
                SessionName = roomName,                       // 🔄 참여할 방 이름 (호스트와 일치)
                Scene = SceneRef.FromIndex(sceneIndex),
                SceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>(),
                CustomLobbyName = FIXED_LOBBY_NAME           // ⚠️ [중요] 호스트와 동일한 로비 이름
            });

            if (result.Ok)
            {
                _currentRoomName = roomName;
                OnConnectionStatusChanged?.Invoke(true);
                Debug.Log($"[NetworkManager] ✅ 3단계 성공: 게임 방 '{roomName}' 참여 완료 (Client)");
                return true;
            }
            else
            {
                OnErrorOccurred?.Invoke($"방 참여 실패: {result.ShutdownReason}");
                return false;
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[NetworkManager] 방 참여 중 오류 발생: {e.Message}");
            OnErrorOccurred?.Invoke($"방 참여 중 오류 발생: {e.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// ✅ [게임 시작] GameRunner에서 Game 씬으로 전환
    /// 역할: JoinLobby에서 실제 Game 씬으로 이동 (씬 전환만, 새로운 연결 없음)
    /// </summary>
    public async Task<bool> StartGame(string gameSceneName = "Game")
    {
        if (!IsHost)
        {
            Debug.LogError("[NetworkManager] 게임 시작은 호스트만 가능합니다.");
            OnErrorOccurred?.Invoke("게임 시작은 호스트만 가능합니다.");
            return false;
        }

        if (CurrentPlayerCount < _maxPlayers)
        {
            Debug.LogError($"[NetworkManager] 게임 시작 조건 미충족: {CurrentPlayerCount}/{_maxPlayers}");
            OnErrorOccurred?.Invoke("모든 플레이어가 준비될 때까지 기다려주세요.");
            return false;
        }

        Debug.Log($"[NetworkManager] 🎮 게임 시작: GameRunner로 '{gameSceneName}' 씬 전환");

        try
        {
            int sceneIndex = SceneUtility.GetBuildIndexByScenePath($"Assets/Scenes/{gameSceneName}.unity");
            if (sceneIndex < 0)
            {
                OnErrorOccurred?.Invoke($"씬 '{gameSceneName}'을(를) 빌드 설정에서 찾을 수 없습니다.");
                return false;
            }

            string currentScene = SceneManager.GetActiveScene().name;
            if (!string.IsNullOrEmpty(currentScene) && currentScene != gameSceneName)
            {
                _previousSceneToUnload = currentScene;
            }

            if (_gameRunner != null && _gameRunner.IsRunning)
            {
                _gameRunner.LoadScene(SceneRef.FromIndex(sceneIndex));
                Debug.Log($"[NetworkManager] ✅ 게임 시작 성공: {gameSceneName} 씬 로드 완료");
                return true;
            }
            else
            {
                Debug.LogError($"[NetworkManager] GameRunner가 실행 중이지 않습니다.");
                OnErrorOccurred?.Invoke("게임 세션이 활성화되어 있지 않습니다.");
                return false;
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[NetworkManager] 게임 시작 중 오류: {e.Message}");
            OnErrorOccurred?.Invoke($"게임 시작 중 오류: {e.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// ✅ [핵심 함수] 방 목록 새로고침 및 동기화
    /// 역할: LobbyRunner(Shared 모드)에서 현재 로비의 모든 세션 목록을 조회
    /// 중요: OnSessionListUpdated 콜백을 통해 방 목록이 업데이트됨
    /// 
    /// ⚠️ [디버깅 포인트] 
    /// 1. 이 함수 호출 → OnSessionListUpdated 콜백 호출 → 방 목록 UI 업데이트 순서 확인
    /// 2. CustomLobbyName이 일치하지 않으면 다른 로비의 방은 보이지 않음
    /// 3. 네트워크 지연으로 방 생성 후 즉시 보이지 않을 수 있음
    /// </summary>
    public async Task RefreshRoomList()
    {
        if (_isRefreshingList)
        {
            if (_extraVerboseLogging) Debug.Log("[NetworkManager] 🔄 방 목록 새로고침 이미 진행 중 - 건너뜀");
            return;
        }
        
        if (_lobbyRunner == null || !_lobbyRunner.IsRunning) 
        {
            Debug.LogWarning($"[NetworkManager] ⚠️ 방 목록 새로고침 불가: LobbyRunner 상태 = " +
                           $"{(_lobbyRunner != null ? (_lobbyRunner.IsRunning ? "실행중" : "중지됨") : "null")}");
            return;
        }
        
        // MatchingLobby에서만 방 목록 새로고침 허용
        string currentScene = SceneManager.GetActiveScene().name;
        if (currentScene != "MatchingLobby")
        {
            if (_extraVerboseLogging) Debug.Log($"[NetworkManager] 🔄 방 목록 새로고침 건너뜀: 현재 씬 '{currentScene}' (MatchingLobby만 허용)");
            return;
        }
        
        // Shared 모드에서만 방 목록 조회 가능
        if (_lobbyRunner.GameMode != GameMode.Shared)
        {
            Debug.LogWarning($"[NetworkManager] ⚠️ 방 목록은 Shared 모드에서만 조회 가능. 현재: {_lobbyRunner.GameMode}");
            return;
        }
        
        var timeSinceLastUpdate = DateTime.Now - _lastRoomListUpdate;
        if (timeSinceLastUpdate < _roomListUpdateInterval)
        {
            if (_extraVerboseLogging) 
            {
                Debug.Log($"[NetworkManager] 🔄 방 목록 업데이트 대기 중: {_roomListUpdateInterval.TotalSeconds - timeSinceLastUpdate.TotalSeconds:F1}초 남음");
            }
            return;
        }
        
        _isRefreshingList = true;
        _lastRoomListUpdate = DateTime.Now;
        
        Debug.Log($"<color=cyan>[NetworkManager] 🔄 방 목록 새로고침 시작</color>\n" +
                 $"  - 현재 LobbyRunner 세션: {(_lobbyRunner.SessionInfo != null ? _lobbyRunner.SessionInfo.Name : "null")}\n" +
                 $"  - 로비 모드: {_lobbyRunner.GameMode}\n" +
                 $"  - 로비 이름: {FIXED_LOBBY_NAME}\n" +
                 $"  - 캐시된 방 수: {_roomList.Count}개");
        
        try
        {
            // ✅ [대기] OnSessionListUpdated 콜백이 호출될 시간 확보
            await Task.Delay(1000); 
            
            var validRooms = _roomList.Values.Where(r => IsValidRoom(r)).ToList();
            
            Debug.Log($"<color=cyan>[NetworkManager] 📋 방 목록 새로고침 결과</color>\n" +
                     $"  - 전체 세션: {_roomList.Count}개\n" +
                     $"  - 유효한 게임 방: {validRooms.Count}개\n" +
                     $"  - 필터링됨: {_roomList.Count - validRooms.Count}개");
            
            // ✅ [상세 디버깅] 모든 세션 정보 출력
            if (_extraVerboseLogging && _roomList.Count > 0)
            {
                Debug.Log($"[NetworkManager] 🔍 전체 세션 목록 상세 분석:");
                int index = 0;
                foreach (var kvp in _roomList)
                {
                    var session = kvp.Value;
                    string sessionType = GetSessionType(session);
                    string validStatus = IsValidRoom(session) ? "✅유효" : "❌무효";
                    
                    Debug.Log($"  [{index}] {validStatus} {session.Name} ({sessionType})\n" +
                             $"      - Players: {session.PlayerCount}/{session.MaxPlayers}\n" +
                             $"      - Valid: {session.IsValid}, Open: {session.IsOpen}, Visible: {session.IsVisible}");
                    index++;
                }
            }
            
            OnRoomListUpdated?.Invoke(validRooms);
        }
        catch (Exception e)
        {
            Debug.LogError($"[NetworkManager] ❌ 방 목록 조회 중 예외: {e.Message}");
            OnErrorOccurred?.Invoke($"방 목록 조회 오류: {e.Message}");
        }
        finally
        {
            _isRefreshingList = false;
        }
    }

    /// <summary>
    /// ✅ [방 나가기] GameRunner 종료, LobbyRunner 유지
    /// 역할: 게임 방에서 나가서 MatchingLobby로 복귀
    /// </summary>
    public async void Disconnect()
    {
        Debug.Log("[NetworkManager] 🚪 게임 방에서 나가는 중...");
        
        if (_gameRunner != null && _gameRunner.IsRunning)
        {
            await _gameRunner.Shutdown();
            Debug.Log("[NetworkManager] ✅ GameRunner 종료 완료");
        }
        
        Debug.Log($"[NetworkManager] 🔄 LobbyRunner 상태 유지: {(_lobbyRunner != null && _lobbyRunner.IsRunning ? "활성" : "비활성")}");
        SceneManager.LoadScene("MatchingLobby");
    }

    public void DisconnectCompletely()
    {
        Debug.Log("[NetworkManager] 🔌 모든 연결 종료");
        if (_gameRunner != null && _gameRunner.IsRunning) _gameRunner.Shutdown();
        if (_lobbyRunner != null && _lobbyRunner.IsRunning) _lobbyRunner.Shutdown();
        _isConnectedToServer = false;
    }

    #endregion
    
    #region Helper Functions & Properties
    
    /// <summary>
    /// ✅ [필터링 함수] 유효한 게임 방 판별
    /// 역할: 세션 목록에서 실제 게임 방만 필터링 (공통 로비, 브라우저 세션 제외)
    /// </summary>
    private bool IsValidRoom(SessionInfo session)
    {
        if (session == null) 
        {
            if (_extraVerboseLogging) Debug.Log("[NetworkManager] 🔍 필터링: session null");
            return false;
        }
        
        if (!session.IsValid)
        {
            if (_extraVerboseLogging) Debug.Log($"[NetworkManager] 🔍 필터링: {session.Name} - IsValid=false");
            return false;
        }
        
        if (!session.IsOpen)
        {
            if (_extraVerboseLogging) Debug.Log($"[NetworkManager] 🔍 필터링: {session.Name} - IsOpen=false");
            return false;
        }
        
        if (!session.IsVisible)
        {
            if (_extraVerboseLogging) Debug.Log($"[NetworkManager] 🔍 필터링: {session.Name} - IsVisible=false");
            return false;
        }
        
        if (string.IsNullOrEmpty(session.Name))
        {
            if (_extraVerboseLogging) Debug.Log($"[NetworkManager] 🔍 필터링: 이름 비어있음");
            return false;
        }
        
        // ✅ [제외] 공통 로비 세션
        if (session.Name == SHARED_LOBBY_SESSION_NAME)
        {
            if (_extraVerboseLogging) Debug.Log($"[NetworkManager] 🔍 필터링: {session.Name} - 공통 로비 제외");
            return false;
        }
        
        // ✅ [제외] 브라우저 세션
        if (session.Name.StartsWith("LobbyBrowser_"))
        {
            if (_extraVerboseLogging) Debug.Log($"[NetworkManager] 🔍 필터링: {session.Name} - 브라우저 세션 제외");
            return false;
        }
        
        if (_extraVerboseLogging) Debug.Log($"[NetworkManager] ✅ 필터링: {session.Name} - 유효한 게임 방");
        return true;
    }

    /// <summary>
    /// ✅ [디버깅 함수] 세션 타입 분류
    /// </summary>
    private string GetSessionType(SessionInfo session)
    {
        if (session.Name == SHARED_LOBBY_SESSION_NAME) return "공통로비";
        if (session.Name.StartsWith("LobbyBrowser_")) return "브라우저";
        return "게임방";
    }

    public bool IsHost => _gameRunner != null && _gameRunner.IsServer;
    public bool IsConnected => (_lobbyRunner != null && _lobbyRunner.IsRunning) || (_gameRunner != null && _gameRunner.IsRunning);
    public bool IsConnectedToServer => _isConnectedToServer && _lobbyRunner != null && _lobbyRunner.IsRunning;
    
    public int CurrentPlayerCount 
    { 
        get 
        {
            if (_gameRunner != null && _gameRunner.IsRunning && _gameRunner.SessionInfo != null)
                return _gameRunner.SessionInfo.PlayerCount;
                
            if (_lobbyRunner != null && _lobbyRunner.IsRunning && _lobbyRunner.SessionInfo != null)
                return _lobbyRunner.SessionInfo.PlayerCount;
                
            return 0;
        }
    }
    
    public int MaxPlayerCount => _maxPlayers;
    public string PlayerNickname => _playerNickname;
    public string CurrentRoomName => _currentRoomName;
    
    public bool CanRefreshRoomList 
    { 
        get 
        {
            string currentScene = SceneManager.GetActiveScene().name;
            return currentScene == "MatchingLobby" && 
                   _lobbyRunner != null && 
                   _lobbyRunner.IsRunning && 
                   _lobbyRunner.GameMode == GameMode.Shared && 
                   !_isRefreshingList;
        }
    }

    #endregion

    #region INetworkRunnerCallbacks Implementation

    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        string runnerType = GetRunnerTypeName(runner);
        Debug.Log($"[NetworkManager] 👤 플레이어 참여 ({runnerType}): Player#{player.PlayerId}, 현재 총 {runner.SessionInfo.PlayerCount}명");
        
        // GameRunner에서만 플레이어 오브젝트 생성
        if (runner == _gameRunner && runner.IsServer && _networkPlayerPrefab != null)
        {
            Debug.Log($"[NetworkManager] 🎮 GameRunner에서 플레이어 오브젝트 생성: Player#{player.PlayerId}");
            runner.Spawn(_networkPlayerPrefab, Vector3.zero, Quaternion.identity, player);
        }
        
        OnRoomPlayerCountChanged?.Invoke(runner.SessionInfo.PlayerCount);
    }

    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        if (runner.SessionInfo != null)
        {
            string runnerType = GetRunnerTypeName(runner);
            Debug.Log($"[NetworkManager] 👤 플레이어 떠남 ({runnerType}): Player#{player.PlayerId}, 남은 인원: {runner.SessionInfo.PlayerCount}명");
            OnRoomPlayerCountChanged?.Invoke(runner.SessionInfo.PlayerCount);
        }
    }

    /// <summary>
    /// ✅ [핵심 콜백] 세션 목록 업데이트 수신
    /// 역할: Photon 서버에서 현재 로비의 모든 세션 정보를 받아와 방 목록 갱신
    /// 중요: 이 콜백이 호출되어야 방 목록이 UI에 표시됨
    /// 
    /// ⚠️ [문제 진단]
    /// 1. 이 콜백이 호출되지 않으면 → Photon 서버 연결 문제
    /// 2. 콜백은 호출되지만 방이 없으면 → CustomLobbyName 불일치 또는 방 생성 실패  
    /// 3. 방은 있지만 필터링됨 → IsValidRoom 함수의 필터링 조건 확인
    /// </summary>
    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList)
    {
        if (runner == _lobbyRunner && runner.GameMode == GameMode.Shared)
        {
            Debug.Log($"<color=lime>[NetworkManager] 📋 [중요] 세션 목록 업데이트 콜백 수신</color>\n" +
                     $"  - Runner: LobbyRunner (Shared 모드)\n" +
                     $"  - 로비 이름: {FIXED_LOBBY_NAME}\n" +
                     $"  - 받은 세션 수: {sessionList.Count}개\n" +
                     $"  - 이전 캐시: {_roomList.Count}개");
            
            // ✅ [상세 분석] 받은 모든 세션 로그
            if (_extraVerboseLogging)
            {
                Debug.Log($"[NetworkManager] 🔍 받은 세션 목록 전체 분석:");
                for (int i = 0; i < sessionList.Count; i++)
                {
                    var session = sessionList[i];
                    string sessionType = GetSessionType(session);
                    Debug.Log($"  [{i}] {session.Name} ({sessionType})\n" +
                             $"      - Players: {session.PlayerCount}/{session.MaxPlayers}\n" +
                             $"      - Status: Valid={session.IsValid}, Open={session.IsOpen}, Visible={session.IsVisible}");
                }
            }
            
            // ✅ [캐시 업데이트] 받은 세션으로 로컬 캐시 갱신
            _roomList.Clear();
            foreach (var session in sessionList)
            {
                _roomList[session.Name] = session;
            }
            
            // ✅ [필터링 및 UI 업데이트] 유효한 게임 방만 추출하여 UI 업데이트
            var validRooms = _roomList.Values.Where(r => IsValidRoom(r)).ToList();
            
            Debug.Log($"<color=lime>[NetworkManager] 📋 방 목록 UI 업데이트 호출</color>\n" +
                     $"  - 전체 세션: {sessionList.Count}개 → 캐시: {_roomList.Count}개\n" +
                     $"  - 유효한 게임 방: {validRooms.Count}개\n" +
                     $"  - OnRoomListUpdated 이벤트 호출");
                     
            OnRoomListUpdated?.Invoke(validRooms);
        }
        else
        {
            if (_extraVerboseLogging)
            {
                Debug.Log($"[NetworkManager] 📋 세션 목록 업데이트 무시됨: {GetRunnerTypeName(runner)} (LobbyRunner Shared만 처리)");
            }
        }
    }
    
    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
    {
        string runnerType = GetRunnerTypeName(runner);
        Debug.Log($"[NetworkManager] 🔌 Runner 종료: {runnerType}, 이유: {shutdownReason}");
        
        if(runner == _gameRunner) 
        {
            OnConnectionStatusChanged?.Invoke(false);
            _gameRunner = null;
        }
        
        if (runner == _lobbyRunner)
        {
            _lobbyRunner = null;
            _isConnectedToServer = false;
        }

        if (runner != null && runner.gameObject != null)
        {
            Destroy(runner.gameObject);
        }
    }

    public void OnSceneLoadDone(NetworkRunner runner)
    {
        string currentSceneName = SceneManager.GetActiveScene().name;
        string runnerType = GetRunnerTypeName(runner);
        Debug.Log($"[NetworkManager] 🎬 씬 로드 완료: {currentSceneName} ({runnerType})");
        
        if (!string.IsNullOrEmpty(_previousSceneToUnload))
        {
            try
            {
                bool sceneExists = false;
                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    Scene scene = SceneManager.GetSceneAt(i);
                    if (scene.name == _previousSceneToUnload && scene.isLoaded)
                    {
                        sceneExists = true;
                        break;
                    }
                }
                
                if (sceneExists)
                {
                    Debug.Log($"[NetworkManager] 🗑️ 이전 씬 언로드: {_previousSceneToUnload}");
                    SceneManager.UnloadSceneAsync(_previousSceneToUnload);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[NetworkManager] ❌ 씬 언로드 오류: {e.Message}");
            }
            finally
            {
                _previousSceneToUnload = null;
            }
        }
    }

    private string GetRunnerTypeName(NetworkRunner runner)
    {
        if (runner == _lobbyRunner) return $"LobbyRunner({runner.GameMode})";
        if (runner == _gameRunner) return $"GameRunner({runner.GameMode})";
        return "Unknown";
    }

    public void OnConnectedToServer(NetworkRunner runner)
    {
        string runnerType = GetRunnerTypeName(runner);
        Debug.Log($"[NetworkManager] 🌐 서버 연결됨: {runnerType}");
    }

    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
    {
        string runnerType = GetRunnerTypeName(runner);
        Debug.LogWarning($"[NetworkManager] ⚠️ 서버 연결 해제: {runnerType}, 이유: {reason}");
    }
    
    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token)
    {
        if (runner.SessionInfo != null && runner.SessionInfo.IsOpen && runner.SessionInfo.PlayerCount < _maxPlayers)
        {
            request.Accept();
        }
        else
        {
            request.Refuse();
        }
    }

    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason)
    {
        Debug.LogError($"[NetworkManager] 연결 실패: {reason}");
    }

    // 기타 콜백들
    public void OnInput(NetworkRunner runner, NetworkInput input) { }
    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
    public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
    public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
    public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
    public void OnSceneLoadStart(NetworkRunner runner) { }
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data) { }
    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }

    #endregion

    #region Debug GUI

    private void OnGUI()
    {
        if (!_showDebugInfo) return;

        GUI.Box(new Rect(10, 10, 550, 500), "");
        GUILayout.BeginArea(new Rect(15, 15, 540, 490));
        
        GUILayout.Label($"🔧 Network Settings (All Clients Must Match)");
        GUILayout.Label($"   AppVersion: {FIXED_APP_VERSION} | Region: {FIXED_REGION}");
        GUILayout.Label($"   LobbyName: {FIXED_LOBBY_NAME} | SharedSession: {SHARED_LOBBY_SESSION_NAME}");
        GUILayout.Space(5);
        
        // LobbyRunner 상태
        if (_lobbyRunner != null && _lobbyRunner.IsRunning)
        {
            GUILayout.Label($"🏠 공통 로비: <color=green>✅ Connected ({_lobbyRunner.GameMode})</color>", GUI.skin.label);
            if (_lobbyRunner.SessionInfo != null)
            {
                GUILayout.Label($"   세션 이름: {_lobbyRunner.SessionInfo.Name}");
                GUILayout.Label($"   접속 인원: {_lobbyRunner.SessionInfo.PlayerCount}명");
            }
        }
        else
        {
            GUILayout.Label($"🏠 공통 로비: <color=red>❌ Disconnected</color>", GUI.skin.label);
        }
        
        GUILayout.Space(5);
        
        // GameRunner 상태
        if (_gameRunner != null && _gameRunner.IsRunning)
        {
            GUILayout.Label($"🎮 게임 방: <color=green>✅ Connected ({_gameRunner.GameMode})</color>", GUI.skin.label);
            if (_gameRunner.SessionInfo != null)
            {
                GUILayout.Label($"   방 이름: {_gameRunner.SessionInfo.Name}");
                GUILayout.Label($"   방 인원: {_gameRunner.SessionInfo.PlayerCount}/{_gameRunner.SessionInfo.MaxPlayers}");
                GUILayout.Label($"   👑 Host: {(_gameRunner.IsServer ? "Yes" : "No")}");
            }
        }
        else
        {
            GUILayout.Label($"🎮 게임 방: <color=red>❌ Disconnected</color>", GUI.skin.label);
        }
        
        GUILayout.Space(5);
        
        // 방 목록 정보
        GUILayout.Label($"📦 발견된 세션: {_roomList.Count}개");
        var validRooms = _roomList.Values.Where(r => IsValidRoom(r)).ToList();
        GUILayout.Label($"📋 유효한 게임 방: {validRooms.Count}개");
        
        // 새로고침 상태
        string refreshStatus = CanRefreshRoomList ? "<color=green>✅ 가능</color>" : "<color=red>❌ 불가</color>";
        GUILayout.Label($"🔄 새로고침: {refreshStatus}", GUI.skin.label);
        
        // 현재 씬
        GUILayout.Label($"🎬 현재 씬: {SceneManager.GetActiveScene().name}");
        
        // 현재 상태 요약
        string currentStatus = "❓ Unknown";
        if (_gameRunner != null && _gameRunner.IsRunning)
        {
            if (_gameRunner.GameMode == GameMode.Host)
                currentStatus = "🏠 게임 방 호스팅 중";
            else if (_gameRunner.GameMode == GameMode.Client)
                currentStatus = "🔗 게임 방 참여 중";
        }
        else if (_lobbyRunner != null && _lobbyRunner.IsRunning && _lobbyRunner.GameMode == GameMode.Shared)
        {
            currentStatus = $"🔍 공통 로비에서 대기 중";
        }
            
        GUILayout.Label($"📊 상태: {currentStatus}");
        
        // 세션 목록 상세 정보
        if (_roomList.Count > 0)
        {
            GUILayout.Space(5);
            GUILayout.Label($"📋 발견된 세션 목록:");
            int count = 0;
            foreach (var kvp in _roomList)
            {
                if (count >= 6) // 최대 6개만 표시
                {
                    GUILayout.Label($"   ... 외 {_roomList.Count - 6}개 더");
                    break;
                }
                
                var session = kvp.Value;
                string sessionType = GetSessionType(session);
                string status = IsValidRoom(session) ? "<color=green>✅</color>" : "<color=red>❌</color>";
                GUILayout.Label($"   {status} {session.Name} ({sessionType}) - {session.PlayerCount}명", GUI.skin.label);
                count++;
            }
        }
        else
        {
            GUILayout.Space(5);
            GUILayout.Label($"<color=yellow>⚠️ 발견된 세션이 없습니다</color>", GUI.skin.label);
            GUILayout.Label("   가능한 원인:");
            GUILayout.Label("   1. 아직 방이 생성되지 않음");
            GUILayout.Label("   2. OnSessionListUpdated 콜백 미호출");
            GUILayout.Label("   3. CustomLobbyName 불일치");
        }
        
        GUILayout.EndArea();
    }

    #endregion
}