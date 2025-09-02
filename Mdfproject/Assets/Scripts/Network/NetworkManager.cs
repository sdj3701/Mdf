using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Fusion;
using Fusion.Sockets;
using UnityEngine.SceneManagement;

public class NetworkManager : MonoBehaviour, INetworkRunnerCallbacks
{
    public static NetworkManager Instance { get; private set; }
    
    [Header("Network Settings")]
    [SerializeField] private NetworkRunner _runnerPrefab;
    [SerializeField] private int _maxPlayers = 2;
    
    private NetworkRunner _runner;
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
    public event Action<bool> OnServerConnected; // Title 씬에서 서버 연결 상태
    public event Action<int> OnRoomPlayerCountChanged; // 방 인원 변경 이벤트
    
    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }
    
    private void Start()
    {
        // Start에서는 InitializeRunner를 호출하지 않음
        // ConnectToServer 메서드에서 호출할 예정
    }
    
    private void InitializeRunner()
    {
        if (_runner == null)
        {
            _runner = gameObject.AddComponent<NetworkRunner>();
            _runner.ProvideInput = true;
        }
    }
    
    /// <summary>
    /// Title 씬에서 서버 연결 (닉네임과 패스워드로 인증)
    /// </summary>
    public async Task<bool> ConnectToServer(string nickname, string password)
    {
        try
        {
            // 닉네임과 패스워드 유효성 검사
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
            
            // PlayerPrefs에 저장 (자동 로그인용)
            PlayerPrefs.SetString("PlayerNickname", nickname);
            PlayerPrefs.SetString("PlayerPassword", password);
            PlayerPrefs.Save();
            
            // Runner 초기화
            InitializeRunner();
            
            // 서버 연결 (Shared 모드로 로비 접속)
            var result = await _runner.StartGame(new StartGameArgs
            {
                GameMode = GameMode.Shared,
                SessionName = "TitleLobby",
                Scene = SceneRef.FromIndex(0), // Title 씬 인덱스
                CustomLobbyName = "MainLobby",
                PlayerCount = 100 // 로비 최대 인원
            });
            
            if (result.Ok)
            {
                _isConnectedToServer = true;
                Debug.Log($"서버 연결 성공! 닉네임: {nickname}");
                OnServerConnected?.Invoke(true);
                
                // MatchingLobby 씬으로 이동
                await Task.Delay(500); // 잠시 대기
                SceneManager.LoadScene("MatchingLobby");
                return true;
            }
            else
            {
                Debug.LogError($"서버 연결 실패: {result.ShutdownReason}");
                OnErrorOccurred?.Invoke($"서버 연결 실패: {result.ShutdownReason}");
                OnServerConnected?.Invoke(false);
                return false;
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"서버 연결 오류: {e.Message}");
            OnErrorOccurred?.Invoke($"서버 연결 오류: {e.Message}");
            OnServerConnected?.Invoke(false);
            return false;
        }
    }
    
    /// <summary>
    /// 호스트로 방 생성 (MatchingLobby에서 사용)
    /// </summary>
    public async Task<bool> CreateRoom(string roomName, string sceneName = "JoinLobby")
    {
        try
        {
            if (_runner == null)
                InitializeRunner();
            
            var result = await _runner.StartGame(new StartGameArgs
            {
                GameMode = GameMode.Host,
                SessionName = roomName,
                Scene = SceneRef.FromIndex(SceneManager.GetSceneByName(sceneName).buildIndex),
                SceneManager = gameObject.GetComponent<NetworkSceneManagerDefault>() ?? gameObject.AddComponent<NetworkSceneManagerDefault>(),
                PlayerCount = _maxPlayers,
                CustomLobbyName = "GameRooms"
            });
            
            if (result.Ok)
            {
                _currentRoomName = roomName;
                Debug.Log($"방 '{roomName}' 생성 성공 (호스트)");
                OnConnectionStatusChanged?.Invoke(true);
                
                // JoinLobby 씬으로 이동
                SceneManager.LoadScene("JoinLobby");
                return true;
            }
            else
            {
                Debug.LogError($"Failed to create room: {result.ShutdownReason}");
                OnErrorOccurred?.Invoke($"Failed to create room: {result.ShutdownReason}");
                return false;
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Error creating room: {e.Message}");
            OnErrorOccurred?.Invoke($"Error creating room: {e.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// 클라이언트로 방 참여 (MatchingLobby에서 사용)
    /// </summary>
    public async Task<bool> JoinRoom(string roomName, string sceneName = "JoinLobby")
    {
        try
        {
            if (_runner == null)
                InitializeRunner();
            
            var result = await _runner.StartGame(new StartGameArgs
            {
                GameMode = GameMode.Client,
                SessionName = roomName,
                Scene = SceneRef.FromIndex(SceneManager.GetSceneByName(sceneName).buildIndex),
                SceneManager = gameObject.GetComponent<NetworkSceneManagerDefault>() ?? gameObject.AddComponent<NetworkSceneManagerDefault>(),
                PlayerCount = _maxPlayers,
                CustomLobbyName = "GameRooms"
            });
            
            if (result.Ok)
            {
                _currentRoomName = roomName;
                Debug.Log($"방 '{roomName}' 참여 성공 (클라이언트)");
                OnConnectionStatusChanged?.Invoke(true);
                
                // JoinLobby 씬으로 이동
                SceneManager.LoadScene("JoinLobby");
                return true;
            }
            else
            {
                Debug.LogError($"Failed to join room: {result.ShutdownReason}");
                OnErrorOccurred?.Invoke($"Failed to join room: {result.ShutdownReason}");
                return false;
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Error joining room: {e.Message}");
            OnErrorOccurred?.Invoke($"Error joining room: {e.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// 방 목록 조회 (MatchingLobby에서 사용)
    /// </summary>
    public async Task RefreshRoomList()
    {
        try
        {
            if (_runner == null)
            {
                _runner = gameObject.AddComponent<NetworkRunner>();
            }
            
            var result = await _runner.JoinSessionLobby(SessionLobby.Custom, "GameRooms");
            
            /*if (result)
            {
                Debug.Log("Successfully joined lobby for room list");
            }
            else
            {
                Debug.LogError("Failed to join lobby");
                OnErrorOccurred?.Invoke("Failed to get room list");
            }*/
        }
        catch (Exception e)
        {
            Debug.LogError($"Error refreshing room list: {e.Message}");
            OnErrorOccurred?.Invoke($"Error refreshing room list: {e.Message}");
        }
    }
    
    /// <summary>
    /// 연결 종료
    /// </summary>
    public void Disconnect()
    {
        if (_runner != null)
        {
            _runner.Shutdown();
            OnConnectionStatusChanged?.Invoke(false);
        }
    }
    
    public bool IsHost => _runner != null && _runner.IsServer;
    public bool IsConnected => _runner != null && _runner.IsRunning;
    public bool IsConnectedToServer => _isConnectedToServer;
    public int CurrentPlayerCount => _runner != null ? _runner.SessionInfo.PlayerCount : 0;
    public int MaxPlayerCount => _maxPlayers;
    public string PlayerNickname => _playerNickname;
    public string CurrentRoomName => _currentRoomName;
    
    /// <summary>
    /// Game 씬으로 전환 (JoinLobby에서 호스트가 호출)
    /// </summary>
    public void StartGame()
    {
        if (IsHost && CurrentPlayerCount == _maxPlayers)
        {
            // 모든 플레이어를 Game 씬으로 이동
            _runner.LoadScene("Game");
        }
        else
        {
            Debug.LogError("게임 시작 조건 미충족: 호스트이고 2명이 모여야 합니다.");
        }
    }
    
    // INetworkRunnerCallbacks 구현
    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        Debug.Log($"플레이어 {player.PlayerId} 참여");
        
        if (runner.IsServer)
        {
            Debug.Log($"현재 플레이어: {runner.SessionInfo.PlayerCount}/{_maxPlayers}");
        }
        
        // 방 인원 변경 이벤트 발생
        OnRoomPlayerCountChanged?.Invoke(runner.SessionInfo.PlayerCount);
    }
    
    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        Debug.Log($"플레이어 {player.PlayerId} 퇴장");
        
        // 방 인원 변경 이벤트 발생
        OnRoomPlayerCountChanged?.Invoke(runner.SessionInfo.PlayerCount);
    }
    
    public void OnInput(NetworkRunner runner, NetworkInput input) { }
    
    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
    
    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
    {
        Debug.Log($"Network shutdown: {shutdownReason}");
        OnConnectionStatusChanged?.Invoke(false);
    }
    
    public void OnConnectedToServer(NetworkRunner runner)
    {
        Debug.Log("Connected to server");
        OnConnectionStatusChanged?.Invoke(true);
    }
    
    public void OnDisconnectedFromServer(NetworkRunner runner)
    {
        Debug.Log("Disconnected from server");
        OnConnectionStatusChanged?.Invoke(false);
    }
    
    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token)
    {
        if (runner.SessionInfo.PlayerCount >= _maxPlayers)
        {
            request.Refuse();
            Debug.Log($"Connection refused: Room is full ({_maxPlayers} players)");
        }
        else
        {
            request.Accept();
        }
    }
    
    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason)
    {
        Debug.LogError($"Connect failed: {reason}");
        OnErrorOccurred?.Invoke($"Connection failed: {reason}");
    }
    
    public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
    
    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList)
    {
        _roomList.Clear();
        
        foreach (var session in sessionList)
        {
            _roomList[session.Name] = session;
            Debug.Log($"Room: {session.Name}, Players: {session.PlayerCount}/{session.MaxPlayers}");
        }
        
        OnRoomListUpdated?.Invoke(sessionList);
    }
    
    public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
    
    public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
    
    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ArraySegment<byte> data) { }
    
    public void OnSceneLoadDone(NetworkRunner runner) { }
    
    public void OnSceneLoadStart(NetworkRunner runner) { }

    // 누락된 메서드들을 구현
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player)
    {
        // 오브젝트가 플레이어의 관심 영역(AOI)에서 벗어날 때 호출
        // 필요한 로직이 없다면 비워둬도 됩니다
    }

    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player)
    {
        // 오브젝트가 플레이어의 관심 영역(AOI)에 들어올 때 호출
        // 필요한 로직이 없다면 비워둬도 됩니다
    }

    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
    {
        // 서버에서 연결이 끊어졌을 때 호출
        Debug.Log($"서버 연결 끊어짐: {reason}");
    }

    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data)
    {
        // 신뢰성 있는 데이터를 받았을 때 호출
        // 필요한 로직이 없다면 비워둬도 됩니다
    }

    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress)
    {
        // 신뢰성 있는 데이터 전송 진행률을 알려주는 콜백
        // 필요한 로직이 없다면 비워둬도 됩니다
    }
}
