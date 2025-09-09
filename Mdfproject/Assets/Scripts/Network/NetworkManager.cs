// Assets/Scripts/Network/NetworkManager.cs
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

// Fusion 2와 호환되는 빈 입력 구조체
public struct NetworkInputData : INetworkInput
{
}

/// <summary>
/// Photon Fusion 2 네트워크 연결, 세션 관리(방 생성/참여/목록), 씬 전환을 담당하는 핵심 클래스입니다.
/// INetworkRunnerCallbacks 인터페이스를 구현하여 네트워크 이벤트를 처리합니다.
/// </summary>
public class NetworkManager : MonoBehaviour, INetworkRunnerCallbacks
{
    public static NetworkManager Instance { get; private set; }

    [Header("Network Settings")]
    [SerializeField] private int _maxPlayers = 2;
    [SerializeField] private GameObject _networkPlayerPrefab;
    
    [Header("Debug")]
    [Tooltip("활성화하면 화면 좌측 상단에 네트워크 연결 및 플레이어 수 정보가 표시됩니다.")]
    [SerializeField] private bool _showDebugInfo = true;

    // 게임 플레이를 위한 메인 NetworkRunner
    private NetworkRunner _runner;
    // 로비 연결 및 방 목록 조회를 위한 별도의 NetworkRunner
    private NetworkRunner _lobbyRunner;
    
    // 씬 전환 시 언로드할 이전 씬의 이름을 저장
    private string _previousSceneToUnload;
    
    // 로컬에 캐시된 방 목록
    private Dictionary<string, SessionInfo> _roomList = new Dictionary<string, SessionInfo>();
    
    // 플레이어 정보
    private string _playerNickname;
    private string _playerPassword;
    private string _currentRoomName;
    
    // 네트워크 상태 관련 이벤트
    private bool _isConnectedToServer = false;
    public event Action<List<SessionInfo>> OnRoomListUpdated;
    public event Action<bool> OnConnectionStatusChanged;
    public event Action<string> OnErrorOccurred;
    public event Action<bool> OnServerConnected;
    public event Action<int> OnRoomPlayerCountChanged;

    // 모든 빌드와 에디터에서 동일하게 유지되어야 하는 고정 값
    private const string FIXED_APP_VERSION = "MDF_1.0";
    private const string FIXED_REGION = "asia";
    private const string FIXED_LOBBY_NAME = "MainLobby";

    private bool _isRefreshingList = false;

    #region Unity Lifecycle & Initialization

    private void Awake()
    {
        // 싱글톤 패턴 구현
        if (Instance == null)
        {
            Instance = this;
            // [수정] DontDestroyOnload -> DontDestroyOnLoad (대소문자 수정)
            DontDestroyOnLoad(gameObject);
            gameObject.name = "NetworkManager (Singleton)";
            EnsurePhotonSettings(); // Photon 설정이 올바른지 확인 및 강제 적용
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
            DisconnectCompletely(); // 오브젝트 파괴 시 모든 연결 종료
            Instance = null;
        }
    }
    
    /// <summary>
    /// PhotonAppSettings 에셋을 찾아 올바른 값(앱 버전, 지역 등)이 설정되었는지 확인하고 강제로 업데이트합니다.
    /// 이를 통해 빌드와 에디터 간의 버전 불일치 문제를 예방합니다.
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
                    appSettings.AppSettings.AppVersion = FIXED_APP_VERSION;
                    needsSave = true;
                }
                
                if (appSettings.AppSettings.FixedRegion != FIXED_REGION)
                {
                    appSettings.AppSettings.FixedRegion = FIXED_REGION;
                    needsSave = true;
                }
                
                if (!appSettings.AppSettings.EnableLobbyStatistics)
                {
                    appSettings.AppSettings.EnableLobbyStatistics = true;
                    needsSave = true;
                }

#if UNITY_EDITOR
                if (needsSave)
                {
                    UnityEditor.EditorUtility.SetDirty(appSettings);
                    UnityEditor.AssetDatabase.SaveAssets();
                    Debug.Log("<color=green>[NetworkManager] PhotonAppSettings가 업데이트되고 저장되었습니다.</color>");
                }
#endif
                Debug.Log($"<color=green>[NetworkManager] PhotonAppSettings 확인 완료. AppVersion: {appSettings.AppSettings.AppVersion}, Region: {appSettings.AppSettings.FixedRegion}</color>");
            }
            else
            {
                Debug.LogError("<color=red>[NetworkManager] PhotonAppSettings를 찾을 수 없습니다!</color>");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[NetworkManager] PhotonAppSettings 설정 중 오류 발생: {e.Message}");
        }
    }

    /// <summary>
    /// 게임 플레이에 사용할 NetworkRunner를 초기화합니다.
    /// </summary>
    private void InitializeRunner()
    {
        if (_runner != null && _runner.IsRunning) return;
        if (_runner != null) Destroy(_runner.gameObject);

        GameObject runnerGo = new GameObject("GameRunner (Host/Client)");
        _runner = runnerGo.AddComponent<NetworkRunner>();
        _runner.ProvideInput = true;
        _runner.AddCallbacks(this);
    }
    
    /// <summary>
    /// 로비 연결 및 방 목록 조회에 사용할 NetworkRunner를 초기화합니다.
    /// </summary>
    private void InitializeLobbyRunner()
    {
        if (_lobbyRunner != null)
        {
            if (_lobbyRunner.IsRunning) _lobbyRunner.Shutdown();
            Destroy(_lobbyRunner.gameObject);
        }

        GameObject runnerGo = new GameObject("LobbyRunner (Persistent)");
        _lobbyRunner = runnerGo.AddComponent<NetworkRunner>();
        _lobbyRunner.AddCallbacks(this);
    }
    
    #endregion

    #region Core Network Functions (Connect, Create, Join)

    /// <summary>
    /// Photon Fusion 서버 및 메인 로비에 연결합니다.
    /// </summary>
    public async Task<bool> ConnectToServer(string nickname, string password)
    {
        _playerNickname = nickname;
        _playerPassword = password;
        PlayerPrefs.SetString("PlayerNickname", nickname);
        PlayerPrefs.SetString("PlayerPassword", password);
        PlayerPrefs.Save();
        
        InitializeLobbyRunner();
        
        var args = new StartGameArgs
        {
            GameMode = GameMode.Shared,
            SessionName = $"LobbyBrowser_{Guid.NewGuid().ToString().Substring(0, 6)}",
            CustomLobbyName = FIXED_LOBBY_NAME,
            PlayerCount = 1
        };
        
        var result = await _lobbyRunner.StartGame(args);
        
        if (result.Ok)
        {
            _isConnectedToServer = true;
            OnServerConnected?.Invoke(true);
            SceneManager.LoadScene("MatchingLobby");
            return true;
        }
        else
        {
            OnErrorOccurred?.Invoke($"서버 연결 실패: {result.ShutdownReason}");
            OnServerConnected?.Invoke(false);
            return false;
        }
    }

    /// <summary>
    /// 새로운 방을 생성합니다. (호스트 역할)
    /// </summary>
    public async Task<bool> CreateRoom(string roomName, string sceneName = "JoinLobby")
    {
        InitializeRunner();

        int sceneIndex = SceneUtility.GetBuildIndexByScenePath($"Assets/Scenes/{sceneName}.unity");
        if (sceneIndex < 0)
        {
            OnErrorOccurred?.Invoke($"씬 '{sceneName}'을(를) 빌드 설정에서 찾을 수 없습니다.");
            return false;
        }

        _previousSceneToUnload = SceneManager.GetActiveScene().name;

        var result = await _runner.StartGame(new StartGameArgs
        {
            GameMode = GameMode.Host,
            SessionName = roomName,
            Scene = SceneRef.FromIndex(sceneIndex),
            SceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>(),
            PlayerCount = _maxPlayers,
            CustomLobbyName = FIXED_LOBBY_NAME
        });

        if (result.Ok)
        {
            _currentRoomName = roomName;
            OnConnectionStatusChanged?.Invoke(true);
            return true;
        }
        else
        {
            OnErrorOccurred?.Invoke($"방 생성 실패: {result.ShutdownReason}");
            return false;
        }
    }
    
    /// <summary>
    /// 기존 방에 참여합니다. (클라이언트 역할)
    /// </summary>
    public async Task<bool> JoinRoom(string roomName, string sceneName = "JoinLobby")
    {
        InitializeRunner();

        int sceneIndex = SceneUtility.GetBuildIndexByScenePath($"Assets/Scenes/{sceneName}.unity");
        if (sceneIndex < 0)
        {
            OnErrorOccurred?.Invoke($"씬 '{sceneName}'을(를) 빌드 설정에서 찾을 수 없습니다.");
            return false;
        }

        _previousSceneToUnload = SceneManager.GetActiveScene().name;

        var result = await _runner.StartGame(new StartGameArgs
        {
            GameMode = GameMode.Client,
            SessionName = roomName,
            Scene = SceneRef.FromIndex(sceneIndex),
            SceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>(),
            CustomLobbyName = FIXED_LOBBY_NAME
        });

        if (result.Ok)
        {
            _currentRoomName = roomName;
            OnConnectionStatusChanged?.Invoke(true);
            return true;
        }
        else
        {
            OnErrorOccurred?.Invoke($"방 참여 실패: {result.ShutdownReason}");
            return false;
        }
    }
    
    /// <summary>
    /// 로비의 방 목록을 새로고침합니다.
    /// </summary>
    public async Task RefreshRoomList()
    {
        if (_isRefreshingList || _lobbyRunner == null || !_lobbyRunner.IsRunning) return;
        
        _isRefreshingList = true;
        Debug.Log($"<color=magenta>[NetworkManager] 방 목록 새로고침 시작...</color>");
        
        try
        {
            // OnSessionListUpdated 콜백이 호출될 때까지 잠시 대기합니다.
            // Fusion 2에서는 명시적인 새로고침 함수가 없으며, 로비에 연결되어 있으면 자동으로 목록이 업데이트됩니다.
            // 이 Task.Delay는 콜백이 수신될 시간을 주는 역할을 합니다.
            await Task.Delay(1000); 
            
            // 현재 캐시된 목록을 기반으로 UI를 업데이트합니다.
            var validRooms = _roomList.Values.Where(r => IsValidRoom(r)).ToList();
            OnRoomListUpdated?.Invoke(validRooms);
        }
        catch (Exception e)
        {
            OnErrorOccurred?.Invoke($"방 목록 조회 오류: {e.Message}");
        }
        finally
        {
            _isRefreshingList = false;
        }
    }
    
    /// <summary>
    /// 특정 이름의 방이 존재하는지 확인합니다.
    /// </summary>
    public async Task<bool> CheckRoomExists(string roomName)
    {
        // 3번 재시도하여 방 목록을 확인
        for (int i = 0; i < 3; i++)
        {
            await RefreshRoomList();
            if (_roomList.ContainsKey(roomName))
            {
                return true;
            }
            await Task.Delay(1000); // 1초 대기 후 재시도
        }
        return false;
    }

    /// <summary>
    /// 게임을 시작합니다. 호스트만 호출할 수 있습니다.
    /// </summary>
    public void StartGame()
    {
        if (IsHost && CurrentPlayerCount == _maxPlayers)
        {
            _runner.LoadScene("Game");
        }
        else
        {
            Debug.LogError($"게임 시작 조건 미충족: IsHost={IsHost}, PlayerCount={CurrentPlayerCount}/{_maxPlayers}");
        }
    }

    /// <summary>
    /// 현재 게임 세션에서 연결을 끊습니다. (로비 연결은 유지)
    /// </summary>
    public void Disconnect()
    {
        if (_runner != null && _runner.IsRunning)
        {
            _runner.Shutdown();
        }
    }

    /// <summary>
    /// 모든 네트워크 연결(게임, 로비)을 완전히 종료합니다.
    /// </summary>
    public void DisconnectCompletely()
    {
        if (_runner != null && _runner.IsRunning) _runner.Shutdown();
        if (_lobbyRunner != null && _lobbyRunner.IsRunning) _lobbyRunner.Shutdown();
        _isConnectedToServer = false;
    }

    #endregion
    
    #region Helper Functions & Properties
    
    private bool IsValidRoom(SessionInfo session)
    {
        if (session == null || !session.IsValid || !session.IsOpen || !session.IsVisible) return false;
        if (string.IsNullOrEmpty(session.Name)) return false;
        if (session.Name.StartsWith("LobbyBrowser_")) return false; // 임시 로비 세션 제외
        return true;
    }

    public bool IsHost => _runner != null && _runner.IsServer;
    public bool IsConnected => _runner != null && _runner.IsRunning;
    public bool IsConnectedToServer => _isConnectedToServer && _lobbyRunner != null && _lobbyRunner.IsRunning;
    public int CurrentPlayerCount => _runner != null && _runner.SessionInfo != null ? _runner.SessionInfo.PlayerCount : 0;
    public int MaxPlayerCount => _maxPlayers;
    public string PlayerNickname => _playerNickname;
    public string CurrentRoomName => _currentRoomName;

    private void OnGUI()
    {
        if (!_showDebugInfo) return;

        GUI.Box(new Rect(10, 10, 350, 140), "");
        GUILayout.BeginArea(new Rect(15, 15, 340, 130));
        GUILayout.Label($"AppVer: {FIXED_APP_VERSION} | Region: {FIXED_REGION}");
        GUILayout.Label($"Lobby: {(_lobbyRunner != null && _lobbyRunner.IsRunning ? "Connected" : "Disconnected")}");
        GUILayout.Label($"LobbyName: {FIXED_LOBBY_NAME}");
        
        if (_runner == null || !_runner.IsRunning)
        {
            GUILayout.Label("Game: Disconnected");
        }
        else
        {
            GUILayout.Label($"Game: Connected ({_runner.GameMode})");
            if (_runner.SessionInfo != null)
            {
                GUILayout.Label($"Room: {_runner.SessionInfo.Name}");
                GUILayout.Label($"Players: {_runner.SessionInfo.PlayerCount} / {_runner.SessionInfo.MaxPlayers}");
            }
        }
        GUILayout.EndArea();
    }

    #endregion

    #region INetworkRunnerCallbacks Implementation (Fusion 2)

    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        if (runner == _runner) // 게임 러너에 플레이어가 참여했을 때만 처리
        {
            Debug.Log($"[NetworkManager] 플레이어 참여: {player.PlayerId}. 현재 인원: {runner.SessionInfo.PlayerCount}/{_maxPlayers}");
            if (runner.IsServer)
            {
                // 호스트는 새로 참여한 플레이어에 대한 네트워크 플레이어 객체를 생성
                if (_networkPlayerPrefab != null)
                {
                    runner.Spawn(_networkPlayerPrefab, Vector3.zero, Quaternion.identity, player);
                }
            }
            OnRoomPlayerCountChanged?.Invoke(runner.SessionInfo.PlayerCount);
        }
    }

    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        if (runner == _runner && runner.SessionInfo != null)
        {
            Debug.Log($"[NetworkManager] 플레이어 떠남: {player.PlayerId}. 현재 인원: {runner.SessionInfo.PlayerCount}/{_maxPlayers}");
            OnRoomPlayerCountChanged?.Invoke(runner.SessionInfo.PlayerCount);
        }
    }

    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList)
    {
        // 로비 러너로부터 받은 세션 목록만 처리
        if (runner == _lobbyRunner)
        {
            Debug.Log($"[NetworkManager] 세션 목록 업데이트 수신: {sessionList.Count}개");
            // 기존 목록을 지우고 새로 받은 목록으로 갱신
            _roomList.Clear();
            foreach (var session in sessionList)
            {
                if (IsValidRoom(session))
                {
                    _roomList[session.Name] = session;
                }
            }
            // UI 업데이트 이벤트 호출
            OnRoomListUpdated?.Invoke(_roomList.Values.ToList());
        }
    }
    
    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
    {
        Debug.Log($"[NetworkManager] Runner Shutdown: {runner.name}, Reason: {shutdownReason}");
        
        // 게임 러너가 종료되면 연결 상태 변경 이벤트를 호출
        if(runner == _runner) OnConnectionStatusChanged?.Invoke(false);

        if (runner != null && runner.gameObject != null)
        {
            Destroy(runner.gameObject);
        }
        
        if (runner == _runner) _runner = null;
        if (runner == _lobbyRunner)
        {
            _lobbyRunner = null;
            _isConnectedToServer = false;
        }
    }

    public void OnSceneLoadDone(NetworkRunner runner)
    {
        // [수정] 현재 활성화된 씬의 이름을 가져오는 방식으로 변경
        Debug.Log($"[NetworkManager] 씬 로드 완료: {SceneManager.GetActiveScene().name}");
        if (!string.IsNullOrEmpty(_previousSceneToUnload))
        {
            SceneManager.UnloadSceneAsync(_previousSceneToUnload);
            _previousSceneToUnload = null;
        }
    }
    
    public void OnConnectedToServer(NetworkRunner runner)
    {
        Debug.Log($"[NetworkManager] 서버에 연결되었습니다: {runner.name}");
    }

    /// <summary>
    /// [Fusion 2 변경점] OnDisconnectedFromServer 콜백의 시그니처가 변경되었습니다.
    /// </summary>
    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
    {
        Debug.LogWarning($"[NetworkManager] 서버로부터 연결이 끊겼습니다: {reason}");
        // 연결 종료에 대한 전체적인 처리는 OnShutdown에서 담당합니다.
    }
    
    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token)
    {
        // 방이 가득 차지 않았고, 열려있는 경우에만 연결을 수락
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
        Debug.LogError($"[NetworkManager] 연결에 실패했습니다: {reason}");
    }

    // --- 이하 콜백들은 현재 프로젝트에서 사용하지 않지만, 인터페이스 구현을 위해 필요합니다. ---
    
    public void OnInput(NetworkRunner runner, NetworkInput input) { }
    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
    public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
    public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
    public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
    public void OnSceneLoadStart(NetworkRunner runner) { }
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    
    /// <summary>
    /// [Fusion 2 변경점] OnReliableDataReceived 콜백의 시그니처가 변경되었습니다.
    /// </summary>
    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ArraySegment<byte> data) { }

    /// <summary>
    /// [Fusion 2 변경점] OnReliableDataProgress 콜백의 시그니처가 변경되었습니다.
    /// </summary>
    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, float progress) { }

    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data)
    {
        throw new NotImplementedException();
    }



    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress)
    {
        throw new NotImplementedException();
    }

    #endregion
}