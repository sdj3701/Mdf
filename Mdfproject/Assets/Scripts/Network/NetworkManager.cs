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

    private const string FIXED_APP_VERSION = "1.0";
    private const string FIXED_REGION = "kr";

    private bool _isRefreshingList = false;

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
    
    private void EnsurePhotonSettings()
    {
        try
        {
            var allSettings = Resources.LoadAll<Fusion.Photon.Realtime.PhotonAppSettings>("");
            Fusion.Photon.Realtime.PhotonAppSettings appSettings = null;
            
            appSettings = Resources.Load<Fusion.Photon.Realtime.PhotonAppSettings>("PhotonFusionSettings");
            
            if (appSettings == null)
            {
                string[] possibleNames = { "PhotonAppSettings", "FusionAppSettings", "AppSettings" };
                foreach (string name in possibleNames)
                {
                    appSettings = Resources.Load<Fusion.Photon.Realtime.PhotonAppSettings>(name);
                    if (appSettings != null) break;
                }
            }
            
            if (appSettings == null && allSettings.Length > 0)
            {
                appSettings = allSettings[0];
            }
            
            if (appSettings != null)
            {
                appSettings.AppSettings.AppVersion = FIXED_APP_VERSION;
                appSettings.AppSettings.FixedRegion = FIXED_REGION;
                Debug.Log($"<color=green>[NetworkManager] PhotonAppSettings 설정 완료! Version: {FIXED_APP_VERSION}, Region: {FIXED_REGION}</color>");
            }
            else
            {
                Debug.LogError("<color=red>[NetworkManager] PhotonAppSettings를 찾을 수 없습니다!</color>");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[NetworkManager] PhotonAppSettings 설정 실패: {e.Message}");
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
        
        Debug.Log($"<color=blue>[NetworkManager] 게임 러너 초기화 완료</color>");
    }

    private void InitializeLobbyRunner()
    {
        if (_lobbyRunner != null)
        {
            if (_lobbyRunner.IsRunning)
            {
                _lobbyRunner.Shutdown();
            }
            Destroy(_lobbyRunner.gameObject);
            _lobbyRunner = null;
        }

        GameObject runnerGo = new GameObject("LobbyRunner (Temp)");
        _lobbyRunner = runnerGo.AddComponent<NetworkRunner>();
        _lobbyRunner.ProvideInput = true; 
        _lobbyRunner.AddCallbacks(this);
        _lobbyRunner.name = "LobbyRunner (Temp)";
        
        Debug.Log($"<color=blue>[NetworkManager] 로비 러너 초기화 완료</color>");
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
            Debug.Log($"[NetworkManager] 로그인 성공! 닉네임: {nickname}");
            OnServerConnected?.Invoke(true);

            await Task.Delay(100);
            SceneManager.LoadScene("MatchingLobby");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"[NetworkManager] 로그인 오류: {e.Message}");
            OnErrorOccurred?.Invoke($"로그인 오류: {e.Message}");
            OnServerConnected?.Invoke(false);
            return false;
        }
    }
    #endregion
    
    public async Task<bool> CreateRoom(string roomName, string sceneName = "JoinLobby")
    {
        Debug.Log($"<color=yellow>[NetworkManager] === 방 생성 시작 ===</color>");
        Debug.Log($"<color=yellow>[NetworkManager] 방 이름: '{roomName}', 씬: '{sceneName}'</color>");
        
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
            Debug.LogError($"[NetworkManager] {errorMsg}");
            OnErrorOccurred?.Invoke(errorMsg);
            return false;
        }

        Debug.Log($"<color=yellow>[NetworkManager] 씬 인덱스 찾음: {sceneIndex}</color>");

        var sceneManager = _runner.gameObject.AddComponent<NetworkSceneManagerDefault>();
        _previousSceneToUnload = SceneManager.GetActiveScene().name;

        var args = new StartGameArgs
        {
            GameMode = GameMode.Host,
            SessionName = roomName,
            Scene = SceneRef.FromIndex(sceneIndex),
            SceneManager = sceneManager,
            PlayerCount = _maxPlayers
        };

        Debug.Log($"<color=yellow>[NetworkManager] StartGame 호출 시작...</color>");
        Debug.Log($"<color=yellow>[NetworkManager] GameMode: {args.GameMode}, SessionName: {args.SessionName}, PlayerCount: {args.PlayerCount}</color>");

        var result = await _runner.StartGame(args);

        Debug.Log($"<color=yellow>[NetworkManager] StartGame 결과: Ok={result.Ok}, ShutdownReason={result.ShutdownReason}</color>");

        if (result.Ok)
        {
            _currentRoomName = roomName;
            Debug.Log($"<color=green>[NetworkManager] ✅ 방 '{roomName}' 생성 성공!</color>");
            Debug.Log($"<color=green>[NetworkManager] 러너 상태: IsServer={_runner.IsServer}, IsRunning={_runner.IsRunning}</color>");
            
            if (_runner.SessionInfo != null)
            {
                Debug.Log($"<color=green>[NetworkManager] 세션 정보: Name={_runner.SessionInfo.Name}, IsOpen={_runner.SessionInfo.IsOpen}, IsVisible={_runner.SessionInfo.IsVisible}, PlayerCount={_runner.SessionInfo.PlayerCount}</color>");
            }
            
            OnConnectionStatusChanged?.Invoke(true);
            
            await Task.Delay(2000);
            
            Debug.Log($"<color=green>[NetworkManager] === 방 생성 완료 ===</color>");
            return true;
        }
        else
        {
            _previousSceneToUnload = null;
            Debug.LogError($"<color=red>[NetworkManager] ❌ 방 생성 실패: {result.ShutdownReason}</color>");
            OnErrorOccurred?.Invoke($"방 생성 실패: {result.ShutdownReason}");
            return false;
        }
    }
    
    public async Task<bool> JoinRoom(string roomName, string sceneName = "JoinLobby")
    {
        Debug.Log($"<color=cyan>[NetworkManager] === 방 참여 시작 ===</color>");
        Debug.Log($"<color=cyan>[NetworkManager] 방 이름: '{roomName}', 씬: '{sceneName}'</color>");
        
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
            Debug.LogError($"[NetworkManager] {errorMsg}");
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
            PlayerCount = _maxPlayers
        });

        if (result.Ok)
        {
            _currentRoomName = roomName;
            Debug.Log($"<color=cyan>[NetworkManager] ✅ 방 '{roomName}' 참여 성공!</color>");
            OnConnectionStatusChanged?.Invoke(true);
            return true;
        }
        else
        {
            _previousSceneToUnload = null;
            Debug.LogError($"<color=red>[NetworkManager] ❌ 방 참여 실패: {result.ShutdownReason}</color>");
            OnErrorOccurred?.Invoke($"방 참여 실패: {result.ShutdownReason}");
            return false;
        }
    }

#region 방 목록 조회 (올바른 Shared Mode 사용)
    public async Task RefreshRoomList()
    {
        if (_isRefreshingList) 
        {
            Debug.Log("[NetworkManager] 이미 새로고침 진행 중. 스킵.");
            return;
        }
        _isRefreshingList = true;

        Debug.Log($"<color=magenta>[NetworkManager] === 방 목록 새로고침 시작 (Shared 모드) ===</color>");

        try
        {
            InitializeLobbyRunner();
            
            // [수정] 다시 Shared 모드 사용 - 방 목록 조회는 Shared에서만 가능
            string sessionName = $"LobbyBrowser_{Guid.NewGuid().ToString().Substring(0, 8)}";
            var args = new StartGameArgs
            {
                GameMode = GameMode.Shared,  // Client -> Shared로 변경
                SessionName = sessionName
            };
            
            Debug.Log($"<color=magenta>[NetworkManager] Shared 모드로 방 목록 조회: {sessionName}</color>");

            var result = await _lobbyRunner.StartGame(args);

            if (!result.Ok)
            {
                Debug.LogError($"<color=red>[NetworkManager] ❌ 로비 접속 실패: {result.ShutdownReason}</color>");
                OnErrorOccurred?.Invoke("방 목록을 가져올 수 없습니다.");
                _isRefreshingList = false;
            }
            else
            {
                Debug.Log($"<color=magenta>[NetworkManager] ✅ Shared 모드 로비 접속 성공!</color>");
                
                // [추가] 연결 직후 대기 시간을 늘려서 OnSessionListUpdated 호출 대기
                await Task.Delay(2000); // 2초 대기
                
                // OnSessionListUpdated가 호출될 때까지 최대 15초 대기
                for (int i = 0; i < 15; i++)
                {
                    if (!_isRefreshingList) // OnSessionListUpdated에서 false로 변경됨
                        break;
                        
                    await Task.Delay(1000);
                    Debug.Log($"<color=magenta>[NetworkManager] OnSessionListUpdated 대기 중... ({i+1}/15초)</color>");
                }
                
                if (_isRefreshingList) // 여전히 true라면 타임아웃
                {
                    Debug.LogWarning("<color=orange>[NetworkManager] ⚠️ 방 목록 조회 타임아웃 (Shared Mode)</color>");
                    _lobbyRunner.Shutdown();
                    _isRefreshingList = false;
                    OnRoomListUpdated?.Invoke(new List<SessionInfo>());
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"<color=red>[NetworkManager] 방 목록 조회 오류: {e.Message}</color>");
            OnErrorOccurred?.Invoke($"방 목록 조회 오류: {e.Message}");
            _isRefreshingList = false;
        }
    }
    #endregion

    public void Disconnect()
    {
        Debug.Log($"<color=gray>[NetworkManager] 연결 해제 시작...</color>");
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
            Debug.Log($"<color=purple>[NetworkManager] 게임 시작!</color>");
            _runner.LoadScene("Game");
        }
        else
        {
            Debug.LogError($"<color=red>[NetworkManager] 게임 시작 조건 미충족: IsHost={IsHost}, PlayerCount={CurrentPlayerCount}/{_maxPlayers}</color>");
        }
    }
    
    public bool IsHost => _runner != null && _runner.IsServer;
    public bool IsConnected => _runner != null && _runner.IsRunning;
    public bool IsConnectedToServer => _isConnectedToServer;
    public int CurrentPlayerCount => _runner != null ? _runner.SessionInfo.PlayerCount : 0;
    public int MaxPlayerCount => _maxPlayers;
    public string PlayerNickname => _playerNickname;
    public string CurrentRoomName => _currentRoomName;

    #region 콜백 함수
    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        if (runner == _runner)
        {
            Debug.Log($"<color=yellow>[NetworkManager] 🎮 플레이어 {player.PlayerId} 참여. 현재 인원: {runner.SessionInfo.PlayerCount}/{runner.SessionInfo.MaxPlayers}</color>");
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
        Debug.Log($"<color=gray>[NetworkManager] Runner Shutdown: {runner.name}, Reason: {shutdownReason}</color>");
        
        if(runner == _runner) OnConnectionStatusChanged?.Invoke(false);

        if (runner.gameObject != null)
        {
            Destroy(runner.gameObject);
        }
        
        if (runner == _runner) _runner = null;
        if (runner == _lobbyRunner) 
        {
            _lobbyRunner = null;
            _isRefreshingList = false;
        }
    }

    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList)
    {
        // [추가] 강화된 디버깅
        Debug.Log($"<color=red>🚨 OnSessionListUpdated 호출됨!</color>");
        Debug.Log($"<color=red>🚨 Runner 이름: {runner.name}</color>");
        Debug.Log($"<color=red>🚨 LobbyRunner와 같은가?: {runner == _lobbyRunner}</color>");
        Debug.Log($"<color=red>🚨 Runner 상태: IsRunning={runner.IsRunning}, GameMode={runner.GameMode}</color>");
        
        if (runner == _lobbyRunner)
        {
            Debug.Log($"<color=lime>[NetworkManager] 🔍 OnSessionListUpdated 호출됨! (Shared Mode)</color>");
            Debug.Log($"<color=lime>[NetworkManager] 📊 서버로부터 총 {sessionList.Count}개의 세션 수신</color>");
            
            if (sessionList.Count == 0)
            {
                Debug.Log($"<color=orange>[NetworkManager] ⚠️ 받은 세션 리스트가 비어있습니다!</color>");
            }
            else
            {
                Debug.Log($"<color=lime>[NetworkManager] === 받은 세션 상세 정보 ===</color>");
                for (int i = 0; i < sessionList.Count; i++)
                {
                    var session = sessionList[i];
                    Debug.Log($"<color=lime>[NetworkManager] [{i}] 이름: '{session.Name}' | 플레이어: {session.PlayerCount}/{session.MaxPlayers} | 유효: {session.IsValid} | 열림: {session.IsOpen} | 보이기: {session.IsVisible}</color>");
                }
                Debug.Log($"<color=lime>[NetworkManager] === 세션 정보 끝 ===</color>");
            }

            var filteredList = sessionList.Where(s => 
                s.IsValid && 
                s.IsOpen && 
                s.IsVisible && 
                !string.IsNullOrEmpty(s.Name)
            ).ToList();
            
            Debug.Log($"<color=cyan>[NetworkManager] 📋 필터링 후 {filteredList.Count}개의 방이 유효합니다.</color>");
            
            if (filteredList.Count > 0)
            {
                Debug.Log($"<color=cyan>[NetworkManager] 유효한 방 목록:</color>");
                foreach (var room in filteredList)
                {
                    Debug.Log($"<color=cyan>[NetworkManager] - '{room.Name}' ({room.PlayerCount}/{room.MaxPlayers})</color>");
                }
            }
            
            OnRoomListUpdated?.Invoke(filteredList);
            
            // [수정] 콜백이 호출되었으므로 새로고침 상태 해제
            _isRefreshingList = false;
            
            Task.Run(async () =>
            {
                await Task.Delay(1000);
                if (_lobbyRunner != null && _lobbyRunner.IsRunning)
                {
                    Debug.Log($"<color=magenta>[NetworkManager] 로비러너 정리 중...</color>");
                    _lobbyRunner.Shutdown();
                }
            });
        }
        else
        {
            Debug.Log($"<color=red>🚨 Runner 불일치! 현재 러너: {runner.name}, 로비러너: {_lobbyRunner?.name ?? "null"}</color>");
        }
    }
    
    public void OnSceneLoadDone(NetworkRunner runner) 
    {
        Debug.Log($"<color=blue>[NetworkManager] 씬 로드 완료: {runner.name}</color>");
        if (!string.IsNullOrEmpty(_previousSceneToUnload))
        {
            Debug.Log($"[NetworkManager] 이전 씬 '{_previousSceneToUnload}' 언로드");
            SceneManager.UnloadSceneAsync(_previousSceneToUnload);
            _previousSceneToUnload = null;
        }
    }
    
    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player) 
    {
        if(runner == _runner) 
        {
            Debug.Log($"<color=yellow>[NetworkManager] 🚪 플레이어 {player.PlayerId} 떠남</color>");
            OnRoomPlayerCountChanged?.Invoke(runner.SessionInfo.PlayerCount);
        }
    }
    
    public void OnConnectedToServer(NetworkRunner runner) 
    { 
        Debug.Log($"<color=green>[NetworkManager] 🌐 서버 연결됨: {runner.name}</color>");
    }
    
    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
    {
        Debug.LogWarning($"<color=orange>[NetworkManager] ⚠️ 서버 연결 끊김: {reason}</color>");
    }
    
    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) 
    {
        bool shouldAccept = runner.SessionInfo.PlayerCount < _maxPlayers;
        Debug.Log($"<color=blue>[NetworkManager] 연결 요청: 수락={shouldAccept} (현재 {runner.SessionInfo.PlayerCount}/{_maxPlayers})</color>");
        
        if (shouldAccept) request.Accept();
        else request.Refuse();
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