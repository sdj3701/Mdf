// Assets/Scripts/Network/NetworkManager.cs

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using UnityEngine;
using Fusion;
using Fusion.Sockets;
using UnityEngine.SceneManagement;

public struct NetworkInputData : INetworkInput
{
    // 입력 데이터가 필요할 경우 여기에 변수를 추가합니다.
}

public class NetworkManager : MonoBehaviour, INetworkRunnerCallbacks
{
    public static NetworkManager Instance { get; private set; }

    [Header("Network Settings")]
    [SerializeField] private int _maxPlayers = 2;
    [SerializeField] private GameObject _networkPlayerPrefab;

    private NetworkRunner _runner;
    private NetworkRunner _lobbyRunner;

    private Dictionary<string, SessionInfo> _roomList = new Dictionary<string, SessionInfo>();
    
    // Player Information
    private string _playerNickname;
    private string _playerPassword;
    private bool _isConnectedToServer = false;
    private string _currentRoomName;

    // Events
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
            Disconnect();
            Instance = null;
        }
    }

    private void InitializeRunner()
    {
        if (_runner != null && _runner.IsRunning) return;
        if (_runner != null) Destroy(_runner.gameObject);

        GameObject runnerGo = new GameObject("GameRunner (Host/Client)");
        runnerGo.transform.SetParent(transform);
        
        _runner = runnerGo.AddComponent<NetworkRunner>();
        _runner.ProvideInput = true;
        _runner.AddCallbacks(this);
    }

    private void InitializeLobbyRunner()
    {
        if (_lobbyRunner != null && _lobbyRunner.IsRunning) return;
        if (_lobbyRunner != null) Destroy(_lobbyRunner.gameObject);

        GameObject runnerGo = new GameObject("LobbyRunner (Temp)");
        runnerGo.transform.SetParent(transform);

        _lobbyRunner = runnerGo.AddComponent<NetworkRunner>();
        
        // ✅ [핵심 수정] LobbyRunner도 입력을 제공한다고 명시적으로 설정합니다.
        // 이것이 IndexOutOfRangeException을 근본적으로 해결합니다.
        _lobbyRunner.ProvideInput = true; 
        
        _lobbyRunner.AddCallbacks(this);
        _lobbyRunner.name = "LobbyRunner (Temp)"; // 이름 재설정
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

    public async Task<bool> CreateRoom(string roomName, string sceneName = "JoinLobby")
    {
        InitializeRunner();
        
        var sceneManager = _runner.gameObject.AddComponent<NetworkSceneManagerDefault>();

        var result = await _runner.StartGame(new StartGameArgs
        {
            GameMode = GameMode.Host,
            SessionName = roomName,
            Scene = SceneRef.FromIndex(SceneManager.GetSceneByName(sceneName).buildIndex),
            SceneManager = sceneManager,
            PlayerCount = _maxPlayers,
            CustomLobbyName = "GameRooms"
        });

        if (result.Ok)
        {
            _currentRoomName = roomName;
            Debug.Log($"방 '{roomName}' 생성 성공 (호스트)");
            OnConnectionStatusChanged?.Invoke(true);
            return true;
        }
        else
        {
            Debug.LogError($"Failed to create room: {result.ShutdownReason}");
            OnErrorOccurred?.Invoke($"Failed to create room: {result.ShutdownReason}");
            return false;
        }
    }

    public async Task<bool> JoinRoom(string roomName, string sceneName = "JoinLobby")
    {
        InitializeRunner();
        
        var sceneManager = _runner.gameObject.AddComponent<NetworkSceneManagerDefault>();

        var result = await _runner.StartGame(new StartGameArgs
        {
            GameMode = GameMode.Client,
            SessionName = roomName,
            Scene = SceneRef.FromIndex(SceneManager.GetSceneByName(sceneName).buildIndex),
            SceneManager = sceneManager,
            PlayerCount = _maxPlayers,
            CustomLobbyName = "GameRooms"
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
            Debug.LogError($"Failed to join room: {result.ShutdownReason}");
            OnErrorOccurred?.Invoke($"Failed to join room: {result.ShutdownReason}");
            return false;
        }
    }

    public async Task RefreshRoomList()
    {
        if (_isRefreshingList) return;
        _isRefreshingList = true;

        try
        {
            InitializeLobbyRunner();

            if (!_lobbyRunner.IsRunning)
            {
                var result = await _lobbyRunner.StartGame(new StartGameArgs
                {
                    GameMode = GameMode.Shared,
                    SessionName = "LobbySession",
                    CustomLobbyName = "GameRooms",
                });

                if (!result.Ok)
                {
                    Debug.LogError($"로비 접속 실패: {result.ShutdownReason}");
                    OnErrorOccurred?.Invoke("방 목록을 가져올 수 없습니다.");
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"방 목록 조회 오류: {e.Message}");
            OnErrorOccurred?.Invoke($"방 목록 조회 오류: {e.Message}");
        }
        finally
        {
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

    // --- INetworkRunnerCallbacks 구현 ---

    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        if (runner == _runner) // 게임 Runner에만 해당
        {
            Debug.Log($"플레이어 {player.PlayerId} 참여");
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
    
    // ✅ [핵심 수정] Runner를 구분하지 않고 항상 빈 데이터를 제공하여 오류를 원천 차단합니다.
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
            var filteredList = sessionList.Where(s => s.IsValid && s.IsOpen).ToList();
            OnRoomListUpdated?.Invoke(filteredList);
            
            runner.Shutdown(); // 목록을 받았으므로 즉시 종료
        }
    }
    
    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player) 
    {
        if(runner == _runner) OnRoomPlayerCountChanged?.Invoke(runner.SessionInfo.PlayerCount);
    }
    public void OnConnectedToServer(NetworkRunner runner) { }
    public void OnDisconnectedFromServer(NetworkRunner runner) { }
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
    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ArraySegment<byte> data) { }
    public void OnSceneLoadDone(NetworkRunner runner) { }
    public void OnSceneLoadStart(NetworkRunner runner) { }

    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player)
    {
        throw new NotImplementedException();
    }

    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player)
    {
        throw new NotImplementedException();
    }

    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
    {
        throw new NotImplementedException();
    }

    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data)
    {
        throw new NotImplementedException();
    }

    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress)
    {
        throw new NotImplementedException();
    }
}