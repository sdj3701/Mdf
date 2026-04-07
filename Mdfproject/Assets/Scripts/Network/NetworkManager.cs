using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Reflection;
using Fusion;
using Fusion.Sockets;
using UnityEngine;
using UnityEngine.SceneManagement;
using GameCore.Enums;


public class NetworkManager : MonoBehaviour, INetworkRunnerCallbacks
{
    private enum ConnectionLossPolicyMode
    {
        AutoReconnectThenFallback,
        ImmediateFallback,
        ObserveOnly
    }

    public static NetworkManager Instance { get; private set; }

    public NetworkRunner _runner { get; private set; }

    [Header("Player")]
    // 스폰할 플레이어 프리팹입니다. Inspector에서 할당해야 합니다.
    [SerializeField] private NetworkObject _playerPrefab;
    // 세션 최대 플레이어 수 (Inspector에서 설정)
    [SerializeField, Range(2, 4)] private int maxSessionPlayers = 2;
    // 서버에서 플레이어들을 관리하기 위한 딕셔너리입니다.
    private readonly Dictionary<PlayerRef, NetworkObject> _spawnedCharacters = new Dictionary<PlayerRef, NetworkObject>();

    [Header("Lobby & UI")]
    // 현재 로비에 있는 세션(방) 목록을 저장합니다.
    public List<SessionInfo> _sessionList = new List<SessionInfo>();
    // 유저가 입력할 방 제목을 저장하는 변수입니다.
    private string _roomNameInput = NetworkDefine.DefaultRoomName;
    // 현재 네트워크 상태를 관리합니다. (연결 끊김, 로비, 게임 중)

    // 2. 현재 상태를 저장하고, 변경 시 이벤트를 발생시키는 프로퍼티
    private ConnectionState _state;
    public ConnectionState State
    {
        get => _state;
        private set
        {
            _state = value;
            // 상태가 변경될 때마다 OnStateChanged 이벤트를 호출(방송)
            OnStateChanged?.Invoke(_state);
        }
    }

    // 3. 상태 변경 이벤트를 정의 (Action 델리게이트 사용)
    public static event Action<ConnectionState> OnStateChanged;

    // 4. 세션 목록 업데이트 이벤트 (방 목록 갱신 알림용)
    public static event Action<List<SessionInfo>> OnSessionListUpdatedEvent;

    // 5. 플레이어 참가/퇴장 이벤트 (UI 갱신용)
    public static event Action<PlayerRef> OnPlayerJoinedEvent;
    public static event Action<PlayerRef> OnPlayerLeftEvent;

    public bool IsGameRunnerActive => _runner != null && _runner.IsRunning;

    private int playerCount;
    private EventInfo _cloudConnectionLostEventInfo;
    private Delegate _cloudConnectionLostHandlerDelegate;
    private MethodInfo _getPlayerConnectionTokenMethod;
    [Header("Connection Loss Policy")]
    [SerializeField] private ConnectionLossPolicyMode _connectionLossPolicy = ConnectionLossPolicyMode.AutoReconnectThenFallback;
    [SerializeField, Range(1f, 15f)] private float _cloudReconnectFallbackDelaySeconds = 5f;
    private Coroutine _pendingConnectionLossFallback;

    private void Awake()
    {
        Application.targetFrameRate = 60;
        // 이미 인스턴스가 있는지 확인
        if (Instance == null)
        {
            // 인스턴스가 없으면, 이 오브젝트를 인스턴스로 지정
            Instance = this;
            // 씬이 전환되어도 이 게임 오브젝트가 파괴되지 않도록 설정
            DontDestroyOnLoad(gameObject);
            
            // HostMigrationHandler 초기화 (Host Migration 지원을 위해 필수)
            if (HostMigrationHandler.Instance == null)
            {
                gameObject.AddComponent<HostMigrationHandler>();
            }

            RegisterCloudConnectionLostHandlerIfAvailable();
        }
        else
        {
            // 이미 인스턴스가 존재하면, 새로 생긴 중복 오브젝트는 파괴
            // (예: 메인 메뉴 씬에서 게임 씬으로 돌아왔을 때 매니저가 중복 생성되는 것을 방지)
            if (Instance != this)
            {
                Destroy(gameObject);
            }
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            CancelPendingConnectionLossFallback();
            UnregisterCloudConnectionLostHandlerIfAvailable();
        }
    }

    public void SetRoomNameInput(string roomname)
    {
        _roomNameInput = roomname;
    }
    
    /// <summary>
    /// Host Migration 후 새 Runner를 설정합니다.
    /// </summary>
    public void SetRunnerAfterMigration(NetworkRunner newRunner)
    {
        Debug.Log($"<color=cyan>[NetworkManager] SetRunnerAfterMigration - 새 Runner 설정</color>");
        _runner = newRunner;
        
        // 콜백 다시 등록
        if (!newRunner.IsRunning)
        {
            // Debug.LogWarning("[NetworkManager] 새 Runner가 실행 중이 아닙니다!");
        }
        else
        {
            // Debug.Log($"[NetworkManager] 새 Runner 상태: GameMode={newRunner.GameMode}, IsServer={newRunner.IsServer}");
        }
    }

    public string GetRoomNameInput()
    {
        return _roomNameInput;
    }

    /// <summary>
    /// 특정 로비에 참여를 시작합니다.
    /// </summary>
    public async void JoinLobby() 
    {
        if (_runner != null) return;
        State = ConnectionState.Connecting; // 새 중간 상태

        _runner = gameObject.AddComponent<NetworkRunner>();
        _runner.AddCallbacks(this);

        var result = await _runner.JoinSessionLobby(SessionLobby.Shared);
        if (!result.Ok) {
            // Debug.LogError($"Join lobby failed: {result.ShutdownReason}");
            State = ConnectionState.Disconnected;
            _ = _runner.Shutdown();
            _runner = null;
            return;
        }

        State = ConnectionState.InLobby;
        // Debug.Log("Joined Lobby.");
    }

    /// <summary>
    /// 게임 세션(방)을 시작하거나 참여합니다.
    /// </summary>
    /// <param name="mode">Host, Client 등 게임 모드</param>
    /// <param name="sessionName">참여하거나 생성할 방의 이름</param>
    public async void StartGame(GameMode mode, string sessionName, string sceneName = null)
    {
        if (_runner == null || State != ConnectionState.InLobby) 
        {
            // Debug.LogWarning("로비 입장 중입니다. 완료될 때까지 기다리세요.");
            return;
        }
        // 로비에 있을 때만 게임을 시작할 수 있습니다.
        if (_state != ConnectionState.InLobby) return;

        string finalSessionName = string.IsNullOrWhiteSpace(sessionName)
            ? PlayerPrefs.GetString(PlayerPrefsDefine.NicknameKey, NetworkDefine.DefaultHostName)
            : sessionName;

        // Debug.Log($"Starting Game with session name: {finalSessionName}, loading scene: {sceneName}");

        // Runner가 없으면 새로 생성하고 콜백을 등록합니다.
        if (_runner == null)
        {
            _runner = gameObject.AddComponent<NetworkRunner>();
            _runner.AddCallbacks(this);
        }

        _runner.ProvideInput = true;
        // Debug.Log(sceneName);

        // 씬 이름을 기반으로 빌드 인덱스를 찾습니다.
        // ※ 주의: 로드할 씬은 반드시 File > Build Settings에 추가되어 있어야 합니다.
        int sceneIndex = SceneUtility.GetBuildIndexByScenePath($"Assets/Scenes/{sceneName}.unity");
        if (sceneIndex < 0)
        {
            // Debug.LogError($"'{sceneName}' 씬을 빌드 설정에서 찾을 수 없습니다!");
            return;
        }
        var scene = SceneRef.FromIndex(sceneIndex);

        var objectProvider = gameObject.GetComponent<PooledNetworkObjectProvider>();
        if (objectProvider == null)
        {
            objectProvider = gameObject.AddComponent<PooledNetworkObjectProvider>();
        }

        // StartGameArgs를 설정하여 게임을 시작합니다.
        // 참고: Host Migration은 Fusion > Network Project Config에서 활성화해야 합니다.
        await _runner.StartGame(new StartGameArgs()
        {
            GameMode = mode,
            SessionName = finalSessionName,
            Scene = scene, // Fusion이 이 씬을 로드하도록 지정합니다.
            SceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>(),
            ObjectProvider = objectProvider,
            PlayerCount = maxSessionPlayers, // Inspector에서 설정한 최대 플레이어 수
            
            // 플레이어 식별용 연결 토큰 (재참여 시 사용)
            ConnectionToken = GetConnectionToken(),
        });
    }

    /// <summary>
    /// 현재 실행 중인 게임 세션을 종료합니다.
    /// </summary>
    private void LeaveGame()
    {
        if (_runner != null)
        {
            // Runner를 종료하면 OnShutdown 콜백이 호출됩니다.
            _runner.Shutdown();
        }
    }

    /// <summary>
    /// [클라이언트 -> 서버] 커맨드 실행을 서버에 요청하는 RPC
    /// </summary>
    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_RequestCommandToServer(CommandType type, int[] intParams, string[] stringParams, Vector3[] vectorParams, RpcInfo info = default)
    {
        // TODO: 여기서 서버는 커맨드의 유효성을 검사해야 합니다.
        // 예: 플레이어가 골드가 충분한지, 유닛 배치가 유효한 위치인지 등.
        // 유효성 검사는 보안(치팅 방지)에 매우 중요합니다.
        // bool isValid = ValidateCommand(type, intParams, stringParams, vectorParams, info.Source);
        bool isValid = true; // 지금은 모든 요청을 유효하다고 가정

        if (isValid)
        {
            // 유효성 검사를 통과하면, 모든 클라이언트에게 이 커맨드를 실행하라고 브로드캐스팅합니다.
            RPC_BroadcastCommandToClients(type, intParams, stringParams, vectorParams);
        }
        else
        {
            // (선택적) 요청을 보낸 클라이언트에게만 실패를 알릴 수 있습니다.
            // Debug.LogWarning($"Player {info.Source.PlayerId}의 {type} 커맨드 요청이 유효성 검사에 실패했습니다.");
        }
    }

    /// <summary>
    /// [서버 -> 모든 클라이언트] 서버가 승인한 커맨드를 모든 클라이언트에서 실행하도록 브로드캐스팅하는 RPC
    /// </summary>
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_BroadcastCommandToClients(CommandType type, int[] intParams, string[] stringParams, Vector3[] vectorParams)
    {
        if (GameManagers.Instance != null && GameManagers.Instance.CommandProcessor != null)
        {
            GameManagers.Instance.CommandProcessor.ReceiveAndEnqueueCommand(type, intParams, stringParams, vectorParams);
        }
    }


    #region UI 그리기 (OnGUI)
    // 이 부분은 실제 게임에서는 UGUI(버튼, 텍스트 등)로 구현하는 것이 좋습니다.
    // 테스트를 위해 간단히 OnGUI를 사용합니다.
    private void OnGUI()
    {
        // 게임씬에서는 표시하지 않음
        string currentSceneName = SceneManager.GetActiveScene().name;
        if (currentSceneName == SceneDefine.Game)
        {
            return;
        }

        GUI.skin.button.fontSize = 20;
        GUI.skin.textField.fontSize = 20;
        GUI.skin.label.fontSize = 20;

        switch (_state)
        {
            // Title 씬에서 사용
            // case ConnectionState.Disconnected:
            //     // [연결 끊김] 상태일 때: 로비 접속 버튼만 표시
            //     if (GUI.Button(new Rect(10, 10, 200, 50), "Join Lobby"))
            //     {
            //         JoinLobby();
            //     }
            //     break;

            // case ConnectionState.InLobby:
            //     // 여기가 LobbyUI에서 방 생성 누르기 버튼
            //     // [로비] 상태일 때: 방 만들기 UI와 방 목록 표시 
            //     GUI.Label(new Rect(10, 10, 200, 30), "Room Name:");
            //     //_roomNameInput = GUI.TextField(new Rect(10, 40, 200, 40), _roomNameInput);

            //     if (GUI.Button(new Rect(10, 90, 200, 50), "Create Room"))
            //     {
            //         // 입력된 이름으로 방을 생성(Host)합니다.
            //         StartGame(GameMode.Host, GetRoomNameInput());
            //     }

            //     // 여기가 LObbyUI에사 방 확인 else 문이 리스트 출력
            //     // 방 목록 표시
            //     GUI.Label(new Rect(250, 10, 300, 30), "Available Rooms");
            //     if (_sessionList.Count == 0)
            //     {
            //         GUI.Label(new Rect(250, 50, 300, 30), "No rooms available.");
            //     }
            //     else
            //     {
            //         for (int i = 0; i < _sessionList.Count; i++)
            //         {
            //             var session = _sessionList[i];
            //             string roomInfo = $"{session.Name} ({session.PlayerCount}/{session.MaxPlayers})";
            //             if (GUI.Button(new Rect(250, 50 + (i * 60), 300, 50), roomInfo))
            //             {
            //                 // 해당 방에 참가(Client)합니다.
            //                 StartGame(GameMode.Client, session.Name, "JoinLobby");
            //             }
            //         }
            //     }
            //     break;

            case ConnectionState.InGame:
                // [게임 중] 상태일 때: 나가기 버튼과 방 정보, 플레이어 수 표시
                GUI.Label(new Rect(10, 10, 300, 30), $"In Room: {_runner.SessionInfo.Name}");

                // --- ✨ 추가된 부분 시작 ✨ ---
                if (_runner != null && _runner.SessionInfo != null)
                {
                    // 현재 플레이어 수와 최대 플레이어 수를 가져와서 표시합니다.
                    playerCount = _runner.SessionInfo.PlayerCount;
                    int maxPlayers = _runner.SessionInfo.MaxPlayers;
                    GUI.Label(new Rect(10, 50, 300, 30), $"Players: {playerCount} / {maxPlayers}");
                }
                // --- ✨ 추가된 부분 종료 ✨ ---

                // 기존 'Leave Game' 버튼의 위치를 아래로 조정합니다 (y: 50 -> 90)
                if (GUI.Button(new Rect(10, 90, 200, 50), "Leave Game"))
                {
                    LeaveGame();
                }
                break;
        }
    }
    #endregion


    #region INetworkRunnerCallbacks 구현
    // 이 콜백은 로비에 있는 방 목록이 업데이트될 때마다 호출됩니다.
    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList)
    {
        // Debug.Log("Session list updated. Found " + sessionList.Count + " sessions.");
        // 받은 목록으로 로컬 목록을 갱신합니다.
        _sessionList = sessionList;
        
        // 세션 목록 업데이트 이벤트 발생 (UI 갱신용)
        OnSessionListUpdatedEvent?.Invoke(sessionList);
    }

    // 플레이어가 게임 세션에 성공적으로 참여했을 때 호출됩니다.
    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        // Debug.Log($"Player {player} Joined.");
        State = ConnectionState.InGame; // 상태를 '게임 중'으로 변경

        if (runner.IsServer)
        {
            if (TryReassociateDisconnectedPlayer(runner, player))
            {
                OnPlayerJoinedEvent?.Invoke(player);
                return;
            }

            // Host Migration 중에는 이미 복원된 플레이어가 있으므로 스폰하지 않음
            if (HostMigrationHandler.Instance != null && HostMigrationHandler.Instance.IsMigrating)
            {
                Debug.Log($"[NetworkManager] Host Migration 중 - Player {player} 스폰 건너뜀 (이미 복원됨)");
                
                // 이미 복원된 PlayerManager 찾아서 등록
                var existingPlayers = FindObjectsOfType<PlayerManager>();
                foreach (var pm in existingPlayers)
                {
                    if (pm.Object != null && pm.Object.InputAuthority == player)
                    {
                        if (!_spawnedCharacters.ContainsKey(player))
                        {
                            _spawnedCharacters.Add(player, pm.Object);
                            Debug.Log($"[NetworkManager] 복원된 Player {player} 등록 완료");
                        }
                        break;
                    }
                }
            }
            else
            {
                // Debug.Log("Spawning player character...");
                // 서버(호스트)는 새로 참여한 플레이어의 캐릭터를 스폰합니다.
                NetworkObject networkPlayerObject = runner.Spawn(_playerPrefab, Vector3.zero, Quaternion.identity, player);
                _spawnedCharacters.Add(player, networkPlayerObject);
            }
        }

        // 플레이어 참가 이벤트 발생
        OnPlayerJoinedEvent?.Invoke(player);
    }

    // 플레이어가 게임 세션을 떠났을 때 호출됩니다.
    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        // Debug.Log($"Player {player} Left.");
        if (_spawnedCharacters.TryGetValue(player, out NetworkObject networkObject))
        {
            CacheDisconnectedPlayerData(runner, player, networkObject);

            bool isMigrating = HostMigrationHandler.Instance != null && HostMigrationHandler.Instance.IsMigrating;
            if (isMigrating)
            {
                // Migration snapshot에 포함되도록 player object를 유지하고 input만 해제한다.
                try
                {
                    if (networkObject != null && networkObject.IsValid)
                    {
                        networkObject.AssignInputAuthority(PlayerRef.None);
                    }
                }
                catch (Exception e)
                {
                    // Debug.LogWarning($"[NetworkManager] Failed to clear input authority for left player {player}: {e.Message}");
                }
            }
            else
            {
                runner.Despawn(networkObject);
            }

            _spawnedCharacters.Remove(player);
        }

        // 플레이어 퇴장 이벤트 발생
        OnPlayerLeftEvent?.Invoke(player);
    }

    // Runner가 종료되었을 때 호출됩니다. (연결 끊김, 스스로 나가기 등)
    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
    {
        string runnerName = runner != null ? runner.name : "null";
        string activeRunnerName = _runner != null ? _runner.name : "null";
        bool isMigrating = HostMigrationHandler.Instance != null && HostMigrationHandler.Instance.IsMigrating;
        // Debug.Log($"OnShutdown: reason={shutdownReason}, runner={runnerName}, activeRunner={activeRunnerName}, isMigrating={isMigrating}");

        // Host Migration 중에는 연결 상태를 유지한다.
        if (isMigrating)
        {
            Debug.Log("[NetworkManager] Host Migration 진행 중 - OnShutdown 기본 처리 생략");
            return;
        }

        // HostMigration 사유의 종료 콜백은 상태 리셋 대상으로 취급하지 않는다.
        if (shutdownReason == ShutdownReason.HostMigration)
        {
            Debug.Log("[NetworkManager] HostMigration 종료 콜백 수신 - 연결 상태 유지");
            if (runner != null && runner != _runner)
            {
                Destroy(runner);
            }
            return;
        }

        // 현재 활성 Runner가 아닌 경우(구 Runner 정리 콜백)는 무시한다.
        if (runner != null && _runner != null && runner != _runner)
        {
            // Debug.LogWarning("[NetworkManager] 활성 Runner가 아닌 OnShutdown 콜백 무시");
            Destroy(runner);
            return;
        }
        
        State = ConnectionState.Disconnected; // 상태를 '연결 끊김'으로 변경
        _sessionList.Clear(); // 방 목록 초기화

        // NetworkRunner 컴포넌트만 제거합니다. (gameObject 전체를 파괴하면 NetworkManager도 사라짐!)
        if (_runner != null)
        {
            Destroy(_runner);
        }
        _runner = null; // 참조를 null로 설정하여 중복 생성을 방지합니다.
    }

    // --- 이하 콜백들은 이 예제에서 사용되지 않지만, 인터페이스 구현을 위해 필요합니다. ---
    public void OnConnectedToServer(NetworkRunner runner)
    {
        CancelPendingConnectionLossFallback();
    }
    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
    public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
    {
        string runnerName = runner != null ? runner.name : "null";
        string activeRunnerName = _runner != null ? _runner.name : "null";
        bool isMigrating = HostMigrationHandler.Instance != null && HostMigrationHandler.Instance.IsMigrating;
        // Debug.LogWarning($"[NetworkManager] OnDisconnectedFromServer: reason={reason}, runner={runnerName}, activeRunner={activeRunnerName}, isMigrating={isMigrating}");

        // Host Migration 진행 중에는 복원 루틴을 우선한다.
        if (isMigrating)
        {
            Debug.Log("[NetworkManager] Host Migration 진행 중 - disconnect 기본 처리 생략");
            return;
        }

        // active runner가 아닌 disconnect 콜백은 무시한다.
        if (runner != null && _runner != null && runner != _runner)
        {
            // Debug.LogWarning("[NetworkManager] active runner가 아닌 disconnect 콜백 무시");
            return;
        }

        ApplyConnectionLossPolicy(
            source: $"OnDisconnectedFromServer:{reason}",
            reconnectingHint: false);
    }
    /// <summary>
    /// Host가 나갔을 때 호출됩니다. Client 중 하나가 새 Host가 됩니다.
    /// </summary>
    public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken)
    {
        Debug.Log("<color=yellow>[NetworkManager] OnHostMigration 호출됨!</color>");
        
        // HostMigrationHandler에 처리 위임
        if (HostMigrationHandler.Instance != null)
        {
            HostMigrationHandler.Instance.StartMigration(runner, hostMigrationToken);
        }
        else
        {
            Debug.LogError("[NetworkManager] HostMigrationHandler가 없습니다! Host Migration 실패.");
            // 폴백: 로비로 돌아가기
            LeaveAndLoad(SceneDefine.MatchingLobby);
        }
    }
    public void OnInput(NetworkRunner runner, NetworkInput input) { }
    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data) { }
    public void OnSceneLoadDone(NetworkRunner runner) { }
    public void OnSceneLoadStart(NetworkRunner runner) { }
    public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }

    private string TryGetConnectionTokenString(NetworkRunner runner, PlayerRef player)
    {
        if (runner == null)
        {
            return null;
        }

        try
        {
            if (_getPlayerConnectionTokenMethod == null)
            {
                _getPlayerConnectionTokenMethod = typeof(NetworkRunner).GetMethod(
                    "GetPlayerConnectionToken",
                    BindingFlags.Instance | BindingFlags.Public,
                    null,
                    new[] { typeof(PlayerRef) },
                    null);
            }

            if (_getPlayerConnectionTokenMethod == null)
            {
                return null;
            }

            object tokenValue = _getPlayerConnectionTokenMethod.Invoke(runner, new object[] { player });
            byte[] tokenBytes = null;
            if (tokenValue is byte[] bytes)
            {
                tokenBytes = bytes;
            }
            else if (tokenValue is ArraySegment<byte> segment && segment.Array != null)
            {
                tokenBytes = new byte[segment.Count];
                Buffer.BlockCopy(segment.Array, segment.Offset, tokenBytes, 0, segment.Count);
            }

            if (tokenBytes == null || tokenBytes.Length == 0)
            {
                return null;
            }

            return Encoding.UTF8.GetString(tokenBytes);
        }
        catch (Exception e)
        {
            // Debug.LogWarning($"[NetworkManager] Failed to read connection token for {player}: {e.Message}");
            return null;
        }
    }

    private void CacheDisconnectedPlayerData(NetworkRunner runner, PlayerRef player, NetworkObject networkObject)
    {
        if (HostMigrationHandler.Instance == null || networkObject == null || !networkObject.IsValid)
        {
            return;
        }

        if (!networkObject.TryGetComponent<PlayerManager>(out var playerManager) || playerManager == null)
        {
            return;
        }

        string token = TryGetConnectionTokenString(runner, player);
        if (string.IsNullOrEmpty(token))
        {
            token = $"playerRef:{player.PlayerId}";
        }

        var data = new PlayerMigrationData
        {
            PlayerId = playerManager.playerId,
            Gold = playerManager.GetGold(),
            Health = playerManager.GetHealth(),
            ConnectionToken = token,
            IsAI = playerManager.GetComponent<AIPlayerController>() != null
        };

        HostMigrationHandler.Instance.CacheDisconnectedPlayer(token, data);
    }

    private bool TryReassociateDisconnectedPlayer(NetworkRunner runner, PlayerRef joinedPlayer)
    {
        if (runner == null || !runner.IsServer || HostMigrationHandler.Instance == null)
        {
            return false;
        }

        string token = TryGetConnectionTokenString(runner, joinedPlayer);
        if (string.IsNullOrEmpty(token))
        {
            return false;
        }

        if (!HostMigrationHandler.Instance.TryGetCachedPlayerData(token, out var cachedData))
        {
            return false;
        }

        PlayerManager targetPlayer = null;
        var candidates = FindObjectsOfType<PlayerManager>(true);
        foreach (var candidate in candidates)
        {
            if (candidate == null || candidate.Runner != runner || candidate.Object == null || !candidate.Object.IsValid)
            {
                continue;
            }

            if (candidate.playerId == cachedData.PlayerId)
            {
                targetPlayer = candidate;
                break;
            }
        }

        if (targetPlayer == null || targetPlayer.Object == null || !targetPlayer.Object.IsValid)
        {
            return false;
        }

        PlayerRef currentInputAuthority = targetPlayer.Object.InputAuthority;
        if (currentInputAuthority != PlayerRef.None
            && currentInputAuthority != joinedPlayer
            && IsActivePlayer(runner, currentInputAuthority))
        {
            // Debug.LogWarning($"[NetworkManager] Reassociate skipped: playerId={cachedData.PlayerId} is still owned by active player {currentInputAuthority}.");
            return false;
        }

        try
        {
            if (targetPlayer.Object.InputAuthority != joinedPlayer)
            {
                targetPlayer.Object.AssignInputAuthority(joinedPlayer);
            }
        }
        catch (Exception e)
        {
            // Debug.LogWarning($"[NetworkManager] Failed to reassign input authority for reconnect player {joinedPlayer}: {e.Message}");
            return false;
        }

        var staleRefs = new List<PlayerRef>();
        foreach (var entry in _spawnedCharacters)
        {
            if (entry.Key != joinedPlayer && entry.Value == targetPlayer.Object)
            {
                staleRefs.Add(entry.Key);
            }
        }

        for (int i = 0; i < staleRefs.Count; i++)
        {
            _spawnedCharacters.Remove(staleRefs[i]);
        }

        _spawnedCharacters[joinedPlayer] = targetPlayer.Object;
        // Debug.Log($"[NetworkManager] Reassociated reconnect player {joinedPlayer} -> playerId={cachedData.PlayerId}, token={token}");
        return true;
    }

    private static bool IsActivePlayer(NetworkRunner runner, PlayerRef player)
    {
        if (runner == null)
        {
            return false;
        }

        foreach (var activePlayer in runner.ActivePlayers)
        {
            if (activePlayer == player)
            {
                return true;
            }
        }

        return false;
    }

    private void RegisterCloudConnectionLostHandlerIfAvailable()
    {
        if (_cloudConnectionLostEventInfo != null || _cloudConnectionLostHandlerDelegate != null)
        {
            return;
        }

        try
        {
            _cloudConnectionLostEventInfo = typeof(NetworkRunner).GetEvent(
                "CloudConnectionLost",
                BindingFlags.Public | BindingFlags.Static);

            if (_cloudConnectionLostEventInfo == null)
            {
                return;
            }

            _cloudConnectionLostHandlerDelegate = Delegate.CreateDelegate(
                _cloudConnectionLostEventInfo.EventHandlerType,
                this,
                nameof(OnCloudConnectionLostCompat),
                false);

            if (_cloudConnectionLostHandlerDelegate == null)
            {
                _cloudConnectionLostEventInfo = null;
                return;
            }

            _cloudConnectionLostEventInfo.AddEventHandler(null, _cloudConnectionLostHandlerDelegate);
            // Debug.Log("[NetworkManager] CloudConnectionLost handler registered.");
        }
        catch (Exception e)
        {
            // Debug.LogWarning($"[NetworkManager] CloudConnectionLost handler registration skipped: {e.Message}");
            _cloudConnectionLostEventInfo = null;
            _cloudConnectionLostHandlerDelegate = null;
        }
    }

    private void UnregisterCloudConnectionLostHandlerIfAvailable()
    {
        if (_cloudConnectionLostEventInfo == null || _cloudConnectionLostHandlerDelegate == null)
        {
            return;
        }

        try
        {
            _cloudConnectionLostEventInfo.RemoveEventHandler(null, _cloudConnectionLostHandlerDelegate);
        }
        catch (Exception e)
        {
            // Debug.LogWarning($"[NetworkManager] CloudConnectionLost handler remove failed: {e.Message}");
        }
        finally
        {
            _cloudConnectionLostEventInfo = null;
            _cloudConnectionLostHandlerDelegate = null;
        }
    }

    private void OnCloudConnectionLostCompat(NetworkRunner runner, ShutdownReason reason, bool reconnecting)
    {
        string runnerName = runner != null ? runner.name : "null";
        // Debug.LogWarning($"[NetworkManager] CloudConnectionLost: reason={reason}, reconnecting={reconnecting}, runner={runnerName}");

        bool isMigrating = HostMigrationHandler.Instance != null && HostMigrationHandler.Instance.IsMigrating;
        if (isMigrating)
        {
            return;
        }

        ApplyConnectionLossPolicy(
            source: $"CloudConnectionLost:{reason}:runner={runnerName}",
            reconnectingHint: reconnecting);
    }

    private void ApplyConnectionLossPolicy(string source, bool reconnectingHint)
    {
        bool isMigrating = HostMigrationHandler.Instance != null && HostMigrationHandler.Instance.IsMigrating;
        if (isMigrating)
        {
            return;
        }

        Debug.LogWarning(BuildConnectionLossTrace(
            "ApplyPolicy",
            $"source={source}, reconnectingHint={reconnectingHint}"));

        switch (_connectionLossPolicy)
        {
            case ConnectionLossPolicyMode.ObserveOnly:
                Debug.LogWarning(BuildConnectionLossTrace(
                    "ObserveOnly",
                    $"source={source}, reconnectingHint={reconnectingHint}"));
                break;

            case ConnectionLossPolicyMode.ImmediateFallback:
                ExecuteConnectionLossFallback($"ImmediateFallback:{source}");
                break;

            case ConnectionLossPolicyMode.AutoReconnectThenFallback:
            default:
                if (reconnectingHint)
                {
                    ScheduleConnectionLossFallback(source, _cloudReconnectFallbackDelaySeconds);
                    return;
                }

                ExecuteConnectionLossFallback($"ReconnectUnavailable:{source}");
                break;
        }
    }

    private void ScheduleConnectionLossFallback(string source, float delaySeconds)
    {
        CancelPendingConnectionLossFallback();
        _pendingConnectionLossFallback = StartCoroutine(ConnectionLossFallbackCoroutine(source, delaySeconds));
        Debug.LogWarning(BuildConnectionLossTrace(
            "ScheduleFallback",
            $"delay={delaySeconds:F1}, source={source}"));
    }

    private IEnumerator ConnectionLossFallbackCoroutine(string source, float delaySeconds)
    {
        float delay = Mathf.Max(0f, delaySeconds);
        if (delay > 0f)
        {
            yield return new WaitForSeconds(delay);
        }

        // reconnect가 성공해 runner가 정상 동작 중이면 fallback을 취소한다.
        if (_runner != null && _runner.IsRunning)
        {
            _pendingConnectionLossFallback = null;
            Debug.Log(BuildConnectionLossTrace("CancelFallbackReconnected", $"source={source}"));
            yield break;
        }

        _pendingConnectionLossFallback = null;
        ExecuteConnectionLossFallback($"DelayedFallback:{source}");
    }

    private void CancelPendingConnectionLossFallback()
    {
        if (_pendingConnectionLossFallback == null)
        {
            return;
        }

        StopCoroutine(_pendingConnectionLossFallback);
        _pendingConnectionLossFallback = null;
    }

    private void ExecuteConnectionLossFallback(string source)
    {
        CancelPendingConnectionLossFallback();
        State = ConnectionState.Disconnected;
        _sessionList.Clear();

        Debug.LogWarning(BuildConnectionLossTrace("ExecuteFallback", $"source={source}"));

        if (SceneManager.GetActiveScene().name != SceneDefine.MatchingLobby)
        {
            SceneManager.LoadScene(SceneDefine.MatchingLobby);
        }
    }

    private string BuildConnectionLossTrace(string step, string extra = null)
    {
        string runnerName = _runner != null ? _runner.name : "null";
        bool runnerRunning = _runner != null && _runner.IsRunning;
        bool migrating = HostMigrationHandler.Instance != null && HostMigrationHandler.Instance.IsMigrating;
        return $"[LC-TRACE] step={step} policy={_connectionLossPolicy} state={State} runner={runnerName} runnerRunning={runnerRunning} migrating={migrating}{(string.IsNullOrEmpty(extra) ? string.Empty : $" | {extra}")}";
    }
    #endregion

    #region 외부 클래스 접근 함수
    
    /// <summary>
    /// 플레이어 고유 연결 토큰을 생성합니다. (재참여 식별용)
    /// </summary>
    private byte[] GetConnectionToken()
    {
        // 유저 고유 ID 생성 또는 기존 ID 사용
        string uniqueId = PlayerPrefs.GetString(PlayerPrefsDefine.PlayerUuidKey, "");
        if (string.IsNullOrEmpty(uniqueId))
        {
            uniqueId = Guid.NewGuid().ToString();
            PlayerPrefs.SetString(PlayerPrefsDefine.PlayerUuidKey, uniqueId);
            PlayerPrefs.Save();
        }
        
        return Encoding.UTF8.GetBytes(uniqueId);
    }


    // 외부 클래스에서 플레이어 몇명 생성 해야하는지 확인할 떄 필요한 함수
    public int GetPlayerCount()
    {
        return playerCount;
    }

    // Fusion 표준: 세션 중이면 Runner로 씬을 전환, 아니면 Unity 씬 로드 사용
    public void LoadSceneSmart(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName))
        {
            // Debug.LogError("[NetworkManager] sceneName is null or empty");
            return;
        }

        int sceneIndex = SceneUtility.GetBuildIndexByScenePath($"Assets/Scenes/{sceneName}.unity");
        if (sceneIndex < 0)
        {
            // Debug.LogError($"[NetworkManager] '{sceneName}' 씬을 빌드 설정에서 찾을 수 없습니다!");
            return;
        }

        if (_runner != null && _runner.IsRunning)
        {
            if (_runner.SceneManager == null)
            {
                // Debug.LogWarning("[NetworkManager] Runner.SceneManager is null. Falling back to Unity SceneManager. Ensure StartGame is called with a SceneManager.");
                SceneManager.LoadScene(sceneName);
                return;
            }
            _runner.LoadScene(SceneRef.FromIndex(sceneIndex), LoadSceneMode.Single);
        }
        else
        {
            SceneManager.LoadScene(sceneName);
        }
    }

    // 세션 종료 후 특정 씬으로 복귀
    public void LeaveAndLoad(string sceneName)
    {
        if (_runner != null)
        {
            _runner.Shutdown();
        }
        SceneManager.LoadScene(sceneName);
    }

    #endregion


}


