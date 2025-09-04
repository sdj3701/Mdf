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

public struct NetworkInputData : INetworkInput
{
}

public class NetworkManager : MonoBehaviour, INetworkRunnerCallbacks
{
    public static NetworkManager Instance { get; private set; }

    [Header("Network Settings")]
    [SerializeField] private int _maxPlayers = 2;
    [SerializeField] private GameObject _networkPlayerPrefab;
    
    // Custom Lobby Name은 계속해서 모든 StartGameArgs에서 사용합니다.
    private const string GameLobbyName = "MySuperUniqueGameLobby";

    private NetworkRunner _runner;
    private NetworkRunner _lobbyRunner;
    
    private string _previousSceneToUnload;

    #region 기본 함수
    private Dictionary<string, SessionInfo> _roomList = new Dictionary<string, SessionInfo>();
    
    private string _playerNickname;
    private string _playerPassword;
    private bool _isConnectedToServer = false;
    private string _currentRoomName;

    public event Action<List<SessionInfo>> OnRoomListUpdated;
    public event Action<bool> OnConnectionStatusChanged;
    public event Action<string> OnErrorOccurred;
    public event Action<bool> OnServerConnected;
    public event Action<int> OnRoomPlayerCountChanged;

    private bool _isRefreshingList = false;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            gameObject.name = "NetworkManager (Singleton)";
            
            // [핵심 수정] 모든 클라이언트가 동일한 네트워크 설정을 사용하도록 강제합니다.
            EnsurePhotonSettings();
        }
        else if (Instance != this)
        {
            Destroy(gameObject);
        }
    }
    
    // [핵심 수정] 이 메서드를 통해 AppVersion과 Region을 강제로 설정하여 네트워크 불일치 문제를 해결합니다.
    private void EnsurePhotonSettings()
    {
        try
        {
            // [수정됨] Resources 폴더에 있는 Photon 설정 파일을 불러옵니다. 이 방식이 ScriptableObject를 로드하는 올바른 방법입니다.
            var appSettings = Resources.Load<Fusion.Photon.Realtime.PhotonAppSettings>("PhotonAppSettings");
            if (appSettings == null)
            {
                Debug.LogError("Resources 폴더에서 'PhotonAppSettings' 파일을 찾을 수 없습니다! Photon Fusion Hub를 통해 설정 파일을 생성하고 Resources 폴더로 옮겼는지 확인해주세요. 포톤 설정이 올바르지 않을 수 있습니다.");
                return;
            }

            // 모든 클라이언트가 동일한 채널에 접속하도록 값을 강제로 고정합니다.
            // 이 값이 다르면 서로 다른 로비에 접속하게 되어 방을 찾을 수 없습니다.
            appSettings.AppSettings.AppVersion = "1.0";
            appSettings.AppSettings.FixedRegion = "kr";

            Debug.Log($"<color=cyan>==================== 포톤 설정 강제 적용 ====================</color>");
            Debug.Log($"<color=cyan>App ID: {appSettings.AppSettings.AppIdFusion}</color>");
            Debug.Log($"<color=cyan>App Version: {appSettings.AppSettings.AppVersion} (이 값이 모든 클라이언트에서 동일해야 합니다)</color>");
            Debug.Log($"<color=cyan>Fixed Region: {appSettings.AppSettings.FixedRegion} (이 값이 모든 클라이언트에서 동일해야 합니다)</color>");
            Debug.Log($"<color=cyan>===========================================================</color>");
        }
        catch (Exception e)
        {
            Debug.LogError($"Photon AppSettings를 강제 설정하는 데 실패했습니다: {e.Message}");
        }
    }


    private void OnDestroy()
    {
        if (Instance == this)
        {
            Disconnect();
            Instance = null;
        }
    }

    private void InitializeRunner()
    {
        if (_runner != null && _runner.IsRunning) return;
        if (_runner != null) Destroy(_runner.gameObject);

        GameObject runnerGo = new GameObject("GameRunner (Host/Client)");
        _runner = runnerGo.AddComponent<NetworkRunner>();
        _runner.ProvideInput = true;
        _runner.AddCallbacks(this);
    }

    private void InitializeLobbyRunner()
    {
        if (_lobbyRunner != null && _lobbyRunner.IsRunning) return;
        if (_lobbyRunner != null) Destroy(_lobbyRunner.gameObject);

        GameObject runnerGo = new GameObject("LobbyRunner (Temp)");
        _lobbyRunner = runnerGo.AddComponent<NetworkRunner>();
        _lobbyRunner.ProvideInput = true; 
        _lobbyRunner.AddCallbacks(this);
        _lobbyRunner.name = "LobbyRunner (Temp)";
    }
    
    public async Task<bool> ConnectToServer(string nickname, string password)
    {
        try
        {
            if (string.IsNullOrEmpty(nickname) || nickname.Length < 2 || nickname.Length > 20)
            {
                OnErrorOccurred?.Invoke("닉네임은 2-20자 사이여야 합니다.");
                return false;
            }
            if (string.IsNullOrEmpty(password) || password.Length < 4)
            {
                OnErrorOccurred?.Invoke("패스워드는 최소 4자 이상이어야 합니다.");
                return false;
            }

            _playerNickname = nickname;
            _playerPassword = password;

            PlayerPrefs.SetString("PlayerNickname", nickname);
            PlayerPrefs.SetString("PlayerPassword", password);
            PlayerPrefs.Save();

            _isConnectedToServer = true;
            Debug.Log($"로그인 성공! 닉네임: {nickname}");
            OnServerConnected?.Invoke(true);

            await Task.Delay(100);
            SceneManager.LoadScene("MatchingLobby");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"로그인 오류: {e.Message}");
            OnErrorOccurred?.Invoke($"로그인 오류: {e.Message}");
            OnServerConnected?.Invoke(false);
            return false;
        }
    }
    #endregion
    
    public async Task<bool> CreateRoom(string roomName, string sceneName = "JoinLobby")
    {
        InitializeRunner();

        int sceneIndex = -1;
        for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
        {
            string path = SceneUtility.GetScenePathByBuildIndex(i);
            string nameInBuild = Path.GetFileNameWithoutExtension(path);
            if (nameInBuild.Equals(sceneName))
            {
                sceneIndex = i;
                break;
            }
        }

        if (sceneIndex < 0)
        {
            string errorMsg = $"씬 '{sceneName}'을(를) 빌드 설정에서 찾을 수 없습니다.";
            Debug.LogError(errorMsg);
            OnErrorOccurred?.Invoke(errorMsg);
            return false;
        }

        var sceneManager = _runner.gameObject.AddComponent<NetworkSceneManagerDefault>();
        _previousSceneToUnload = SceneManager.GetActiveScene().name;

        var args = new StartGameArgs
        {
            GameMode = GameMode.Host,
            SessionName = roomName,
            Scene = SceneRef.FromIndex(sceneIndex),
            SceneManager = sceneManager,
            PlayerCount = _maxPlayers,
            CustomLobbyName = GameLobbyName 
            // [오류 수정] AppVersion, Region 속성을 제거하여 중앙 설정(PhotonAppSettings)을 따르도록 합니다.
        };

        Debug.Log($"[NetworkManager] 방 '{roomName}'을(를) CustomLobby '{args.CustomLobbyName}'에 생성 시도...");

        var result = await _runner.StartGame(args);

        if (result.Ok)
        {
            _currentRoomName = roomName;
            Debug.Log($"<color=green>방 '{roomName}' 생성 성공 (호스트)</color>");
            OnConnectionStatusChanged?.Invoke(true);
            return true;
        }
        else
        {
            _previousSceneToUnload = null;
            Debug.LogError($"Failed to create room: {result.ShutdownReason}");
            OnErrorOccurred?.Invoke($"Failed to create room: {result.ShutdownReason}");
            return false;
        }
    }
    
    public async Task<bool> JoinRoom(string roomName, string sceneName = "JoinLobby")
    {
        InitializeRunner();

        int sceneIndex = -1;
        for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
        {
            string path = SceneUtility.GetScenePathByBuildIndex(i);
            string nameInBuild = Path.GetFileNameWithoutExtension(path);
            if (nameInBuild.Equals(sceneName))
            {
                sceneIndex = i;
                break;
            }
        }

        if (sceneIndex < 0)
        {
            string errorMsg = $"씬 '{sceneName}'을(를) 빌드 설정에서 찾을 수 없습니다.";
            Debug.LogError(errorMsg);
            OnErrorOccurred?.Invoke(errorMsg);
            return false;
        }
        
        var sceneManager = _runner.gameObject.AddComponent<NetworkSceneManagerDefault>();
        _previousSceneToUnload = SceneManager.GetActiveScene().name;

        var result = await _runner.StartGame(new StartGameArgs
        {
            GameMode = GameMode.Client,
            SessionName = roomName,
            Scene = SceneRef.FromIndex(sceneIndex),
            SceneManager = sceneManager,
            PlayerCount = _maxPlayers,
            CustomLobbyName = GameLobbyName
            // [오류 수정] AppVersion, Region 속성을 제거하여 중앙 설정(PhotonAppSettings)을 따르도록 합니다.
        });

        if (result.Ok)
        {
            _currentRoomName = roomName;
            Debug.Log($"방 '{roomName}' 참여 성공 (클라이언트)");
            OnConnectionStatusChanged?.Invoke(true);
            return true;
        }
        else
        {
            _previousSceneToUnload = null;
            Debug.LogError($"Failed to join room: {result.ShutdownReason}");
            OnErrorOccurred?.Invoke($"Failed to join room: {result.ShutdownReason}");
            return false;
        }
    }

    #region 나머지 함수
    public async Task RefreshRoomList()
    {
        if (_isRefreshingList) return;
        _isRefreshingList = true;

        try
        {
            InitializeLobbyRunner();
            
            if (_lobbyRunner.IsRunning)
            {
                Debug.LogWarning("LobbyRunner가 이미 실행 중입니다. 새로고침 요청을 무시합니다.");
                _isRefreshingList = false;
                return;
            }
            
            var args = new StartGameArgs
            {
                GameMode = GameMode.Shared,
                SessionName = $"LobbyClient_{Guid.NewGuid()}",
                CustomLobbyName = GameLobbyName
                // [오류 수정] AppVersion, Region 속성을 제거하여 중앙 설정(PhotonAppSettings)을 따르도록 합니다.
            };
            
            Debug.Log($"[NetworkManager] CustomLobby '{args.CustomLobbyName}'의 방 목록 새로고침 시작...");

            var result = await _lobbyRunner.StartGame(args);

            if (!result.Ok)
            {
                Debug.LogError($"로비 접속 실패: {result.ShutdownReason}");
                OnErrorOccurred?.Invoke("방 목록을 가져올 수 없습니다.");
                _isRefreshingList = false;
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"방 목록 조회 오류: {e.Message}");
            OnErrorOccurred?.Invoke($"방 목록 조회 오류: {e.Message}");
            _isRefreshingList = false;
        }
    }

    public void Disconnect()
    {
        if (_runner != null && _runner.IsRunning)
        {
            _runner.Shutdown();
        }
        if (_lobbyRunner != null && _lobbyRunner.IsRunning)
        {
            _lobbyRunner.Shutdown();
        }
    }

    public void StartGame()
    {
        if (IsHost && CurrentPlayerCount == _maxPlayers)
        {
            _runner.LoadScene("Game");
        }
        else
        {
            Debug.LogError("게임 시작 조건 미충족: 호스트이고 2명이 모여야 합니다.");
        }
    }
    
    public bool IsHost => _runner != null && _runner.IsServer;
    public bool IsConnected => _runner != null && _runner.IsRunning;
    public bool IsConnectedToServer => _isConnectedToServer;
    public int CurrentPlayerCount => _runner != null ? _runner.SessionInfo.PlayerCount : 0;
    public int MaxPlayerCount => _maxPlayers;
    public string PlayerNickname => _playerNickname;
    public string CurrentRoomName => _currentRoomName;
    #endregion

    #region 콜백 함수
    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        if (runner == _runner)
        {
            Debug.Log($"<color=yellow>[NetworkManager] 플레이어 {player.PlayerId} 참여. 현재 인원: {runner.SessionInfo.PlayerCount}/{runner.SessionInfo.MaxPlayers}</color>");
            if (runner.IsServer)
            {
                if (_networkPlayerPrefab != null)
                {
                    runner.Spawn(_networkPlayerPrefab, Vector3.zero, Quaternion.identity, player);
                }
            }
            OnRoomPlayerCountChanged?.Invoke(runner.SessionInfo.PlayerCount);
        }
    }
    
    public void OnInput(NetworkRunner runner, NetworkInput input)
    {
        input.Set(new NetworkInputData());
    }

    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
    {
        Debug.Log($"Runner Shutdown: {runner.name}, Reason: {shutdownReason}");
        
        if(runner == _runner) OnConnectionStatusChanged?.Invoke(false);

        if (runner.gameObject != null)
        {
            Destroy(runner.gameObject);
        }
        
        if (runner == _runner) _runner = null;
        if (runner == _lobbyRunner) _lobbyRunner = null;
    }

    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList)
    {
        if (runner == _lobbyRunner)
        {
            Debug.Log($"<color=lime>[NetworkManager] 서버로부터 {sessionList.Count}개의 방 정보 수신.</color>");
            foreach (var session in sessionList)
            {
                Debug.Log($"<color=lime>  -> 방: [{session.Name}], 인원: [{session.PlayerCount}/{session.MaxPlayers}]</color>");
            }

            var filteredList = sessionList.Where(s => s.IsValid && s.IsOpen).ToList();
            
            OnRoomListUpdated?.Invoke(filteredList);
            
            if(runner.IsRunning) runner.Shutdown();
            _isRefreshingList = false;
        }
    }
    
    public void OnSceneLoadDone(NetworkRunner runner) 
    {
        if (!string.IsNullOrEmpty(_previousSceneToUnload))
        {
            Debug.Log($"새 씬 로드 완료. 이전 씬 '{_previousSceneToUnload}'을(를) 언로드합니다.");
            SceneManager.UnloadSceneAsync(_previousSceneToUnload);
            _previousSceneToUnload = null;
        }
    }
    
    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player) 
    {
        if(runner == _runner) OnRoomPlayerCountChanged?.Invoke(runner.SessionInfo.PlayerCount);
    }
    public void OnConnectedToServer(NetworkRunner runner) { }
    
    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
    {
        Debug.LogWarning($"Disconnected from server. Reason: {reason}");
    }
    
    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) 
    {
        if (runner.SessionInfo.PlayerCount >= _maxPlayers) request.Refuse();
        else request.Accept();
    }
    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
    public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
    public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
    public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data) { }
    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
    public void OnSceneLoadStart(NetworkRunner runner) { }

    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    #endregion
}