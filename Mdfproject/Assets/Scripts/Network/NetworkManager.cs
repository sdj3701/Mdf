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
using TMPro;


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
    public string LastFusionSceneName { get; private set; }

    public TMP_InputField NickNameInput;
    public TMP_InputField PassWordInput;

    [Header("Player")]
    // 스폰할 플레이어 프리팹입니다. Inspector에서 할당해야 합니다.
    [SerializeField] private NetworkObject _playerPrefab;
    // 세션 최대 플레이어 수 (Inspector에서 설정)
    [SerializeField, Range(2, 4)] private int maxSessionPlayers = 2;
    // 서버에서 플레이어들을 관리하기 위한 딕셔너리입니다.
    private readonly Dictionary<PlayerRef, NetworkObject> _spawnedCharacters = new Dictionary<PlayerRef, NetworkObject>();
    private readonly Dictionary<PlayerRef, string> _connectionTokensByPlayer = new Dictionary<PlayerRef, string>();
    private readonly Dictionary<int, PendingDisconnectedAiTakeover> _pendingDisconnectedAiTakeovers = new Dictionary<int, PendingDisconnectedAiTakeover>();
    private int _disconnectedAiTakeoverGeneration;

    private sealed class PendingDisconnectedAiTakeover
    {
        public int PlayerId;
        public string ConnectionToken;
        public PlayerRef DisconnectedPlayer;
        public NetworkRunner Runner;
        public PlayerManager PlayerManager;
        public int Generation;
        public Coroutine Coroutine;
    }

    [Header("Lobby & UI")]
    // 현재 로비에 있는 세션(방) 목록을 저장합니다.
    public List<SessionInfo> _sessionList = new List<SessionInfo>();
    // 유저가 입력할 방 제목을 저장하는 변수입니다.
    private string _roomNameInput = "MyFusionRoom";
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
    public static event Action OnNetworkUiBlockChanged;

    public bool IsGameRunnerActive => _runner != null && _runner.IsRunning;
    public bool IsNetworkUiBlocked => _networkUiBlockReason != NetworkUiBlockReason.None;

    private int playerCount;
    private NetworkUiBlockReason _networkUiBlockReason = NetworkUiBlockReason.None;
    private bool _startGameInProgress;
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
            CancelAllPendingDisconnectedAiTakeovers();
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

    private void SetNetworkUiBlock(NetworkUiBlockReason reason)
    {
        if (_networkUiBlockReason == reason)
        {
            return;
        }

        _networkUiBlockReason = reason;
        OnNetworkUiBlockChanged?.Invoke();
    }

    /// <summary>
    /// 특정 로비에 참여를 시작합니다.
    /// </summary>
    public async void JoinLobby()
    {
        if (_runner != null) return;
        State = ConnectionState.Connecting; // 새 중간 상태
        SetNetworkUiBlock(NetworkUiBlockReason.LobbyBootstrap);

        _runner = gameObject.AddComponent<NetworkRunner>();
        _runner.AddCallbacks(this);

        var result = await _runner.JoinSessionLobby(SessionLobby.Shared);
        if (!result.Ok) {
            // Debug.LogError($"Join lobby failed: {result.ShutdownReason}");
            State = ConnectionState.Disconnected;
            SetNetworkUiBlock(NetworkUiBlockReason.None);
            _ = _runner.Shutdown();
            _runner = null;
            return;
        }

        State = ConnectionState.InLobby;
        SetNetworkUiBlock(NetworkUiBlockReason.None);
        // Debug.Log("Joined Lobby.");
    }

    /// <summary>
    /// 게임 세션(방)을 시작하거나 참여합니다.
    /// </summary>
    /// <param name="mode">Host, Client 등 게임 모드</param>
    /// <param name="sessionName">참여하거나 생성할 방의 이름</param>
    public async void StartGame(GameMode mode, string sessionName, string sceneName = null)
    {
        if (_startGameInProgress)
        {
            Debug.LogWarning($"[NetworkManager] StartGame ignored because another session start is already in progress. mode={mode}, session={sessionName}");
            return;
        }

        if (_runner == null || State != ConnectionState.InLobby)
        {
            // Debug.LogWarning("로비 입장 중입니다. 완료될 때까지 기다리세요.");
            return;
        }
        // 로비에 있을 때만 게임을 시작할 수 있습니다.
        if (_state != ConnectionState.InLobby) return;

        string finalSessionName = string.IsNullOrWhiteSpace(sessionName)
            ? PlayerPrefs.GetString("PlayerNickname", "Host")
            : sessionName;
        _startGameInProgress = true;
        SetNetworkUiBlock(mode == GameMode.Host ? NetworkUiBlockReason.CreateRoom : NetworkUiBlockReason.JoinRoom);

        // Debug.Log($"Starting Game with session name: {finalSessionName}, loading scene: {sceneName}");

        // Runner가 없으면 새로 생성하고 콜백을 등록합니다.
        if (_runner == null)
        {
            _runner = gameObject.AddComponent<NetworkRunner>();
            _runner.AddCallbacks(this);
        }

        _runner.ProvideInput = true;
        // Debug.Log(sceneName);

        string resolvedSceneName = ResolveSceneName(sceneName, SceneDefine.Game);

        // 씬 이름을 기반으로 빌드 인덱스를 찾습니다.
        // ※ 주의: 로드할 씬은 반드시 File > Build Settings에 추가되어 있어야 합니다.
        int sceneIndex = GetBuildIndexForScene(resolvedSceneName);
        if (sceneIndex < 0)
        {
            // Debug.LogError($"'{sceneName}' 씬을 빌드 설정에서 찾을 수 없습니다!");
            _startGameInProgress = false;
            SetNetworkUiBlock(NetworkUiBlockReason.None);
            return;
        }
        LastFusionSceneName = resolvedSceneName;
        var scene = SceneRef.FromIndex(sceneIndex);

        var objectProvider = gameObject.GetComponent<PooledNetworkObjectProvider>();
        if (objectProvider == null)
        {
            objectProvider = gameObject.AddComponent<PooledNetworkObjectProvider>();
        }

        // StartGameArgs를 설정하여 게임을 시작합니다.
        // 참고: Host Migration은 Fusion > Network Project Config에서 활성화해야 합니다.
        try
        {
            var result = await _runner.StartGame(new StartGameArgs()
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

            if (!result.Ok)
            {
                Debug.LogError($"[NetworkManager] StartGame failed. mode={mode}, session={finalSessionName}, reason={result.ShutdownReason}");
                _startGameInProgress = false;
                SetNetworkUiBlock(NetworkUiBlockReason.None);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[NetworkManager] StartGame exception. mode={mode}, session={finalSessionName}, error={ex}");
            _startGameInProgress = false;
            SetNetworkUiBlock(NetworkUiBlockReason.None);
        }
    }

    /// <summary>
    /// 현재 실행 중인 게임 세션을 종료합니다.
    /// </summary>
    private void LeaveGame()
    {
        if (_runner != null)
        {
            _startGameInProgress = false;
            SetNetworkUiBlock(NetworkUiBlockReason.LeaveRoom);
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
        Debug.LogWarning($"[NetworkManager] Rejected legacy command RPC {type}. Client commands must route through the owned PlayerManager authority gate.");
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
        if (ResolveSceneName(currentSceneName, currentSceneName) == SceneDefine.Game)
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
        _startGameInProgress = false;
        State = ConnectionState.InGame; // 상태를 '게임 중'으로 변경
        SetNetworkUiBlock(NetworkUiBlockReason.None);

        if (runner.IsServer)
        {
            CachePlayerConnectionToken(runner, player);

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
                if (_playerPrefab == null)
                {
                    Debug.LogError($"[NetworkManager] Player prefab is missing or failed to load. Cannot spawn player {player}. Check the NetworkManager _playerPrefab reference in 00_Title and Assets/Prefabs/Player_Root.prefab.");
                    return;
                }

                NetworkObject networkPlayerObject = runner.Spawn(_playerPrefab, Vector3.zero, Quaternion.identity, player);
                _spawnedCharacters[player] = networkPlayerObject;
                PublishDurableConnectionTokenHash(runner, player, networkPlayerObject);
            }
        }

        // 플레이어 참가 이벤트 발생
        OnPlayerJoinedEvent?.Invoke(player);
    }

    // 플레이어가 게임 세션을 떠났을 때 호출됩니다.
    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        // Debug.Log($"Player {player} Left.");
        bool isMigrating = HostMigrationHandler.Instance != null && HostMigrationHandler.Instance.IsMigrating;
        _spawnedCharacters.TryGetValue(player, out NetworkObject networkObject);

        PlayerManager runtimePlayer = FindPlayerManagerForInputAuthority(runner, player);
        NetworkObject preservedObject = runtimePlayer != null ? runtimePlayer.Object : null;
        NetworkObject cacheObject = preservedObject != null && preservedObject.IsValid ? preservedObject : networkObject;
        string disconnectedToken = GetCachedOrCurrentConnectionToken(runner, player);

        CacheDisconnectedPlayerData(runner, player, cacheObject);

        if (runtimePlayer != null && preservedObject != null && preservedObject.IsValid)
        {
            TryClearInputAuthority(preservedObject, $"OnPlayerLeft:{player}");

            if (!isMigrating && runner != null && runner.IsServer)
            {
                int disconnectedPlayerId = runtimePlayer.playerId;
                CancelPendingDisconnectedAiTakeover(disconnectedPlayerId, null);
                if (!TryEnableDisconnectedAiTakeover(
                        runner,
                        player,
                        runtimePlayer,
                        disconnectedPlayerId,
                        disconnectedToken,
                        out string notReadyReason))
                {
                    ScheduleDisconnectedAiTakeoverRetry(
                        runner,
                        player,
                        runtimePlayer,
                        disconnectedPlayerId,
                        disconnectedToken,
                        notReadyReason);
                }
            }

            if (networkObject != null && networkObject.IsValid && networkObject != preservedObject && !isMigrating)
            {
                runner.Despawn(networkObject);
            }
        }
        else if (networkObject != null && networkObject.IsValid)
        {
            if (isMigrating)
            {
                // Migration snapshot에 포함되도록 player object를 유지하고 input만 해제한다.
                TryClearInputAuthority(networkObject, $"OnPlayerLeft.Migration:{player}");
            }
            else
            {
                runner.Despawn(networkObject);
            }
        }

        if (_spawnedCharacters.ContainsKey(player))
        {
            _spawnedCharacters.Remove(player);
        }

        // 플레이어 퇴장 이벤트 발생
        OnPlayerLeftEvent?.Invoke(player);
    }

    // Runner가 종료되었을 때 호출됩니다. (연결 끊김, 스스로 나가기 등)
    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
    {
        CancelPendingDisconnectedAiTakeoversForRunner(runner);

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

        HostMigrationHandler.Instance?.ClearReconnectCacheForMatchEnd();
        _connectionTokensByPlayer.Clear();

        State = ConnectionState.Disconnected; // 상태를 '연결 끊김'으로 변경
        _startGameInProgress = false;
        SetNetworkUiBlock(NetworkUiBlockReason.None);
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

        if (ShouldDelayFallbackForHostMigration(runner))
        {
            ScheduleHostMigrationFallbackGrace($"HostMigrationGrace:OnDisconnectedFromServer:{reason}", _cloudReconnectFallbackDelaySeconds);
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
        CancelPendingConnectionLossFallback();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        MPTestHostMigrationEvents.Record("network_manager_on_host_migration", runner, hostMigrationToken);
#endif
        
        // HostMigrationHandler에 처리 위임
        if (HostMigrationHandler.Instance != null)
        {
            HostMigrationHandler.Instance.StartMigration(runner, hostMigrationToken);
        }
        else
        {
            Debug.LogError("[NetworkManager] HostMigrationHandler가 없습니다! Host Migration 실패.");
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            MPTestHostMigrationEvents.Record("network_manager_on_host_migration_fail_no_handler", runner, hostMigrationToken);
#endif
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
    public void OnSceneLoadDone(NetworkRunner runner)
    {
        if (runner != null && runner == _runner)
        {
            LastFusionSceneName = SceneManager.GetActiveScene().name;
        }
    }
    public void OnSceneLoadStart(NetworkRunner runner) { }
    public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }

    private PlayerManager FindPlayerManagerForInputAuthority(NetworkRunner runner, PlayerRef player)
    {
        if (runner == null)
        {
            return null;
        }

        var candidates = FindObjectsOfType<PlayerManager>(true);
        foreach (var candidate in candidates)
        {
            if (candidate == null || candidate.Runner != runner || candidate.Object == null || !candidate.Object.IsValid)
            {
                continue;
            }

            if (candidate.Object.InputAuthority == player)
            {
                return candidate;
            }
        }

        return null;
    }

    private bool TryClearInputAuthority(NetworkObject networkObject, string context)
    {
        if (networkObject == null || !networkObject.IsValid || networkObject.InputAuthority == PlayerRef.None)
        {
            return true;
        }

        try
        {
            networkObject.AssignInputAuthority(PlayerRef.None);
            return true;
        }
        catch (Exception)
        {
            // Debug.LogWarning($"[NetworkManager] Failed to clear input authority ({context}): {e.Message}");
            return false;
        }
    }

    private bool TryEnableDisconnectedAiTakeover(
        NetworkRunner runner,
        PlayerRef player,
        PlayerManager playerManager,
        int expectedPlayerId,
        string expectedConnectionToken,
        out string reason)
    {
        reason = null;

        if (runner == null || !runner.IsServer)
        {
            reason = "runner_not_server";
            return false;
        }

        if (playerManager == null || playerManager.Object == null || !playerManager.Object.IsValid)
        {
            reason = "player_manager_invalid";
            return false;
        }

        if (playerManager.Runner != runner || !playerManager.Object.HasStateAuthority)
        {
            reason = "player_manager_not_authoritative";
            return false;
        }

        _connectionTokensByPlayer.TryGetValue(player, out string cachedConnectionToken);
        bool hasInputAuthority = playerManager.Object.InputAuthority != PlayerRef.None;
        bool hasActiveConnection = HasActiveConnectionForDurablePlayer(runner, expectedPlayerId);
        if (!ValidateDisconnectedAiTakeoverIdentity(
                expectedPlayerId,
                expectedConnectionToken,
                playerManager.playerId,
                cachedConnectionToken,
                hasInputAuthority,
                hasActiveConnection,
                out reason))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(expectedConnectionToken))
        {
            var migrationHandler = HostMigrationHandler.Instance;
            if (migrationHandler == null
                || !migrationHandler.TryGetCachedPlayerData(expectedConnectionToken, out var cachedPlayerData)
                || cachedPlayerData.PlayerId != expectedPlayerId)
            {
                reason = "durable_identity_cache_mismatch";
                return false;
            }
        }

        var gameManagers = GameManagers.Instance;
        if (gameManagers == null)
        {
            reason = "game_managers_missing";
            return false;
        }

        if (gameManagers.CommandProcessor == null)
        {
            reason = "command_processor_missing";
            return false;
        }

        playerManager.RebindRuntimeReferencesAfterMigration("NetworkManager.DisconnectAITakeover", false);
        if (playerManager.fieldManager == null)
        {
            reason = "field_manager_missing";
            return false;
        }

        playerManager.fieldManager.RebuildWallMapsAfterMigration(
            "NetworkManager.DisconnectAITakeover",
            false,
            out _,
            forceRebuild: true);

        if (!playerManager.IsRuntimeReady(out string runtimeReason) || !playerManager.fieldManager.IsWallMapReady)
        {
            reason = runtimeReason ?? "wall_map_not_ready";
            return false;
        }

        var aiController = playerManager.GetComponent<AIPlayerController>();
        if (aiController == null)
        {
            playerManager.mazePlanned = false;
            playerManager.mazePlannedOrder.Clear();
            playerManager.mazeBuildCursor = 0;
            playerManager.mazeConstructionComplete = false;
            playerManager.unitPurchaseComplete = false;

            aiController = playerManager.gameObject.AddComponent<AIPlayerController>();
        }

        aiController.Initialize(playerManager, gameManagers.CommandProcessor, MdfBotProfile.ServerAiDefault(playerManager.playerId));

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        MPTestLogger.Log("disconnect_ai_takeover", "pass", null, null, new Dictionary<string, object>
        {
            { "leftPlayerRef", player.ToString() },
            { "playerId", playerManager.playerId }
        });
#endif
        return true;
    }

    private static bool ValidateDisconnectedAiTakeoverIdentity(
        int expectedPlayerId,
        string expectedConnectionToken,
        int actualPlayerId,
        string cachedConnectionToken,
        bool hasInputAuthority,
        bool hasActiveConnection,
        out string reason)
    {
        if (expectedPlayerId < 0 || actualPlayerId != expectedPlayerId)
        {
            reason = "durable_player_id_mismatch";
            return false;
        }

        if (!string.IsNullOrEmpty(expectedConnectionToken)
            && !string.Equals(expectedConnectionToken, cachedConnectionToken, StringComparison.Ordinal))
        {
            reason = "durable_connection_token_mismatch";
            return false;
        }

        if (hasInputAuthority)
        {
            reason = "input_authority_reassigned";
            return false;
        }

        if (hasActiveConnection)
        {
            reason = "durable_player_reconnected";
            return false;
        }

        reason = null;
        return true;
    }

    private bool HasActiveConnectionForDurablePlayer(NetworkRunner runner, int expectedPlayerId)
    {
        if (runner == null || expectedPlayerId < 0)
        {
            return false;
        }

        var candidates = FindObjectsOfType<PlayerManager>(true);
        foreach (var candidate in candidates)
        {
            if (candidate == null
                || candidate.playerId != expectedPlayerId
                || candidate.Runner != runner
                || candidate.Object == null
                || !candidate.Object.IsValid)
            {
                continue;
            }

            PlayerRef inputAuthority = candidate.Object.InputAuthority;
            if (inputAuthority != PlayerRef.None && IsActivePlayer(runner, inputAuthority))
            {
                return true;
            }
        }

        return false;
    }

    private void ScheduleDisconnectedAiTakeoverRetry(
        NetworkRunner runner,
        PlayerRef player,
        PlayerManager playerManager,
        int expectedPlayerId,
        string expectedConnectionToken,
        string initialReason)
    {
        CancelPendingDisconnectedAiTakeover(expectedPlayerId, null);

        var pending = new PendingDisconnectedAiTakeover
        {
            PlayerId = expectedPlayerId,
            ConnectionToken = expectedConnectionToken,
            DisconnectedPlayer = player,
            Runner = runner,
            PlayerManager = playerManager,
            Generation = ++_disconnectedAiTakeoverGeneration
        };

        _pendingDisconnectedAiTakeovers[expectedPlayerId] = pending;
        Coroutine coroutine = StartCoroutine(RetryDisconnectedAiTakeover(pending, initialReason));
        if (IsCurrentDisconnectedAiTakeover(pending))
        {
            pending.Coroutine = coroutine;
        }
    }

    private IEnumerator RetryDisconnectedAiTakeover(PendingDisconnectedAiTakeover pending, string initialReason)
    {
        string reason = initialReason;
        float deadline = Time.realtimeSinceStartup + 10f;
        while (Time.realtimeSinceStartup < deadline)
        {
            if (!IsCurrentDisconnectedAiTakeover(pending))
            {
                yield break;
            }

            if (TryEnableDisconnectedAiTakeover(
                    pending.Runner,
                    pending.DisconnectedPlayer,
                    pending.PlayerManager,
                    pending.PlayerId,
                    pending.ConnectionToken,
                    out reason))
            {
                CompleteDisconnectedAiTakeoverRetry(pending);
                yield break;
            }

            yield return new WaitForSeconds(0.5f);
        }

        CompleteDisconnectedAiTakeoverRetry(pending);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        MPTestLogger.Log("disconnect_ai_takeover", "fail", "not_ready", reason, new Dictionary<string, object>
        {
            { "leftPlayerRef", pending.DisconnectedPlayer.ToString() },
            { "playerId", pending.PlayerId },
            { "generation", pending.Generation },
            { "initialReason", initialReason ?? "unknown" }
        });
#endif
    }

    private bool IsCurrentDisconnectedAiTakeover(PendingDisconnectedAiTakeover pending)
    {
        return pending != null
            && _pendingDisconnectedAiTakeovers.TryGetValue(pending.PlayerId, out var current)
            && ReferenceEquals(current, pending)
            && current.Generation == pending.Generation;
    }

    private void CompleteDisconnectedAiTakeoverRetry(PendingDisconnectedAiTakeover pending)
    {
        if (IsCurrentDisconnectedAiTakeover(pending))
        {
            _pendingDisconnectedAiTakeovers.Remove(pending.PlayerId);
        }
    }

    private void CancelPendingDisconnectedAiTakeover(int playerId, string expectedConnectionToken)
    {
        if (!_pendingDisconnectedAiTakeovers.TryGetValue(playerId, out var pending))
        {
            return;
        }

        if (!string.IsNullOrEmpty(expectedConnectionToken)
            && !string.IsNullOrEmpty(pending.ConnectionToken)
            && !string.Equals(expectedConnectionToken, pending.ConnectionToken, StringComparison.Ordinal))
        {
            return;
        }

        _pendingDisconnectedAiTakeovers.Remove(playerId);
        if (pending.Coroutine != null)
        {
            StopCoroutine(pending.Coroutine);
        }
    }

    private void CancelPendingDisconnectedAiTakeoversForRunner(NetworkRunner runner)
    {
        if (runner == null)
        {
            CancelAllPendingDisconnectedAiTakeovers();
            return;
        }

        var playerIds = new List<int>();
        foreach (var entry in _pendingDisconnectedAiTakeovers)
        {
            if (entry.Value.Runner == runner)
            {
                playerIds.Add(entry.Key);
            }
        }

        for (int i = 0; i < playerIds.Count; i++)
        {
            CancelPendingDisconnectedAiTakeover(playerIds[i], null);
        }
    }

    private void CancelAllPendingDisconnectedAiTakeovers()
    {
        var playerIds = new List<int>(_pendingDisconnectedAiTakeovers.Keys);
        for (int i = 0; i < playerIds.Count; i++)
        {
            CancelPendingDisconnectedAiTakeover(playerIds[i], null);
        }
    }

    private void ReleaseDisconnectedAiTakeover(PlayerManager playerManager, PlayerRef joinedPlayer)
    {
        if (playerManager == null)
        {
            return;
        }

        ComponentRegistry.Unregister<AIPlayerController>(playerManager.playerId.ToString());
        var aiController = playerManager.GetComponent<AIPlayerController>();
        if (aiController != null)
        {
            Destroy(aiController);
        }

        playerManager.RebindRuntimeReferencesAfterMigration("NetworkManager.Reconnect", false);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        MPTestLogger.Log("same_token_reconnect", "pass", null, null, new Dictionary<string, object>
        {
            { "joinedPlayerRef", joinedPlayer.ToString() },
            { "playerId", playerManager.playerId }
        });
#endif
    }

    private IEnumerator BroadcastReconnectStateSyncCoroutine(NetworkRunner runner, int reconnectedPlayerId)
    {
        for (int attempt = 0; attempt < 10; attempt++)
        {
            BroadcastRuntimeStateForLateJoin(runner, reconnectedPlayerId, attempt);
            yield return new WaitForSeconds(1f);
        }
    }

    private void BroadcastRuntimeStateForLateJoin(NetworkRunner runner, int reconnectedPlayerId, int attempt)
    {
        if (runner == null || !runner.IsServer || !runner.IsRunning)
        {
            return;
        }

        var candidates = FindObjectsOfType<PlayerManager>(true);
        int wallSyncs = 0;
        int attackPoolSyncs = 0;
        int battleCommandTelemetrySyncs = 0;
        int rebinds = 0;
        foreach (var player in candidates)
        {
            if (player == null || player.Runner != runner || player.Object == null || !player.Object.IsValid)
            {
                continue;
            }

            player.RebindRuntimeReferencesAfterMigration("NetworkManager.ReconnectLateJoinSync", false);
            player.RPC_RebindRuntimeStateAfterReconnect();
            player.ResendAttackMonsterPoolToClientsIfAuthoritative();
            attackPoolSyncs++;
            rebinds++;

            if (player.fieldManager == null)
            {
                continue;
            }

            player.fieldManager.RebuildWallMapsAfterMigration(
                "NetworkManager.ReconnectLateJoinSync",
                false,
                out _,
                forceRebuild: true);

            int[] permanentWalls = player.fieldManager.GetPermanentWallFlatPositions();
            if (permanentWalls.Length <= 0)
            {
                continue;
            }

            player.RPC_ApplyPermanentWalls(permanentWalls);
            wallSyncs++;
        }

        var gameManagersCandidates = FindObjectsOfType<GameManagers>(true);
        foreach (var gm in gameManagersCandidates)
        {
            if (gm == null || gm.Runner != runner)
            {
                continue;
            }

            gm.SyncBattleCommandTelemetryToClientsIfAuthoritative();
            battleCommandTelemetrySyncs++;
            break;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        MPTestLogger.Log("same_token_reconnect_state_sync", "info", null, null, new Dictionary<string, object>
        {
            { "playerId", reconnectedPlayerId },
            { "attempt", attempt },
            { "rebinds", rebinds },
            { "wallSyncs", wallSyncs },
            { "attackPoolSyncs", attackPoolSyncs },
            { "battleCommandTelemetrySyncs", battleCommandTelemetrySyncs }
        });
#endif
    }

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
        catch (Exception)
        {
            // Debug.LogWarning($"[NetworkManager] Failed to read connection token for {player}: {e.Message}");
            return null;
        }
    }

    private string GetCachedOrCurrentConnectionToken(NetworkRunner runner, PlayerRef player)
    {
        string token = TryGetConnectionTokenString(runner, player);
        if (!string.IsNullOrEmpty(token))
        {
            _connectionTokensByPlayer[player] = token;
            return token;
        }

        return _connectionTokensByPlayer.TryGetValue(player, out string cachedToken) ? cachedToken : null;
    }

    private void CachePlayerConnectionToken(NetworkRunner runner, PlayerRef player)
    {
        string token = TryGetConnectionTokenString(runner, player);
        if (!string.IsNullOrEmpty(token))
        {
            _connectionTokensByPlayer[player] = token;
            return;
        }

        // PlayerRef values are reusable across runners/sessions. Never let a missing
        // token read on a fresh join fall back to another session's raw token.
        _connectionTokensByPlayer.Remove(player);
    }

    private void PublishDurableConnectionTokenHash(
        NetworkRunner runner,
        PlayerRef player,
        NetworkObject playerObject = null)
    {
        if (runner == null || !runner.IsServer)
        {
            return;
        }

        string token = GetCachedOrCurrentConnectionToken(runner, player);
        string tokenHash = DurableConnectionTokenIdentity.BuildHash(token);
        if (!PlayerManager.IsValidDurableConnectionTokenHash(tokenHash))
        {
            return;
        }

        PlayerManager playerManager = null;
        if (playerObject != null && playerObject.IsValid)
        {
            playerObject.TryGetComponent(out playerManager);
        }
        playerManager ??= FindPlayerManagerForInputAuthority(runner, player);
        playerManager?.TrySetDurableConnectionTokenHashFromAuthority(tokenHash);
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

        string token = GetCachedOrCurrentConnectionToken(runner, player);
        if (string.IsNullOrEmpty(token))
        {
            Debug.LogWarning($"[NetworkManager] Disconnected player cache skipped: missing durable connection token for {player}.");
            return;
        }

        var data = new PlayerMigrationData
        {
            PlayerId = playerManager.playerId,
            Gold = playerManager.GetGold(),
            Health = playerManager.GetHealth(),
            ConnectionToken = token,
            IsAI = playerManager.GetComponent<AIPlayerController>() != null
        };

        playerManager.TrySetDurableConnectionTokenHashFromAuthority(
            DurableConnectionTokenIdentity.BuildHash(token));

        HostMigrationHandler.Instance.CacheDisconnectedPlayer(token, data);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        MPTestLogger.Log("disconnect_cache", "pass", null, null, new Dictionary<string, object>
        {
            { "leftPlayerRef", player.ToString() },
            { "playerId", playerManager.playerId },
            { "tokenHash", MPTestLogger.HashForLog(token) }
        });
#endif
    }

    private bool TryReassociateDisconnectedPlayer(NetworkRunner runner, PlayerRef joinedPlayer)
    {
        if (runner == null || !runner.IsServer || HostMigrationHandler.Instance == null)
        {
            return false;
        }

        string token = GetCachedOrCurrentConnectionToken(runner, joinedPlayer);
        if (string.IsNullOrEmpty(token))
        {
            return false;
        }

        bool hasRawCache = HostMigrationHandler.Instance.TryGetCachedPlayerData(token, out var cachedData);
        string tokenHash = DurableConnectionTokenIdentity.BuildHash(token);
        if (!hasRawCache && !PlayerManager.IsValidDurableConnectionTokenHash(tokenHash))
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

            bool matchesRawCache = hasRawCache && candidate.playerId == cachedData.PlayerId;
            bool matchesMigratedHash = PlayerManager.IsValidDurableConnectionTokenHash(tokenHash)
                && string.Equals(candidate.GetDurableConnectionTokenHash(), tokenHash, StringComparison.OrdinalIgnoreCase);
            if (matchesRawCache || matchesMigratedHash)
            {
                targetPlayer = candidate;
                if (!hasRawCache)
                {
                    cachedData = new PlayerMigrationData
                    {
                        PlayerId = candidate.playerId,
                        ConnectionToken = token,
                        Health = candidate.GetHealth(),
                        Gold = candidate.GetGold(),
                        IsAI = candidate.GetComponent<AIPlayerController>() != null
                    };
                }
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
        catch (Exception)
        {
            // Debug.LogWarning($"[NetworkManager] Failed to reassign input authority for reconnect player {joinedPlayer}: {e.Message}");
            return false;
        }

        targetPlayer.TrySetDurableConnectionTokenHashFromAuthority(tokenHash);
        CancelPendingDisconnectedAiTakeover(cachedData.PlayerId, token);
        ReleaseDisconnectedAiTakeover(targetPlayer, joinedPlayer);
        StartCoroutine(BroadcastReconnectStateSyncCoroutine(runner, targetPlayer.playerId));

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
        if (hasRawCache)
        {
            HostMigrationHandler.Instance.ForgetCachedPlayerData(token);
        }
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
        catch (Exception)
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
        catch (Exception)
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

    private void ScheduleHostMigrationFallbackGrace(string source, float delaySeconds)
    {
        CancelPendingConnectionLossFallback();
        _pendingConnectionLossFallback = StartCoroutine(HostMigrationFallbackGraceCoroutine(source, delaySeconds));
        Debug.LogWarning(BuildConnectionLossTrace(
            "ScheduleHostMigrationGrace",
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

    private IEnumerator HostMigrationFallbackGraceCoroutine(string source, float delaySeconds)
    {
        float delay = Mathf.Max(0.5f, delaySeconds);
        yield return new WaitForSeconds(delay);

        _pendingConnectionLossFallback = null;
        bool isMigrating = HostMigrationHandler.Instance != null && HostMigrationHandler.Instance.IsMigrating;
        if (isMigrating)
        {
            Debug.Log(BuildConnectionLossTrace("CancelHostMigrationGrace", $"source={source}"));
            yield break;
        }

        ExecuteConnectionLossFallback($"HostMigrationGraceExpired:{source}");
    }

    private bool ShouldDelayFallbackForHostMigration(NetworkRunner runner)
    {
        if (runner == null || HostMigrationHandler.Instance == null)
        {
            return false;
        }

        if (runner.GameMode != GameMode.Client)
        {
            return false;
        }

        string sceneName = SceneManager.GetActiveScene().name;
        return sceneName != SceneDefine.MatchingLobby && sceneName != SceneDefine.Title;
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
        _startGameInProgress = false;
        _sessionList.Clear();

        Debug.LogWarning(BuildConnectionLossTrace("ExecuteFallback", $"source={source}"));

        if (ResolveSceneName(SceneManager.GetActiveScene().name, SceneManager.GetActiveScene().name) != SceneDefine.MatchingLobby)
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
        string uniqueId = PlayerPrefs.GetString("PlayerUUID", "");
        if (string.IsNullOrEmpty(uniqueId))
        {
            uniqueId = Guid.NewGuid().ToString();
            PlayerPrefs.SetString("PlayerUUID", uniqueId);
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

        string resolvedSceneName = ResolveSceneName(sceneName, SceneDefine.Game);
        int sceneIndex = GetBuildIndexForScene(resolvedSceneName);
        if (sceneIndex < 0)
        {
            // Debug.LogError($"[NetworkManager] '{sceneName}' 씬을 빌드 설정에서 찾을 수 없습니다!");
            return;
        }

        if (_runner != null && _runner.IsRunning)
        {
            LastFusionSceneName = resolvedSceneName;
            if (_runner.SceneManager == null)
            {
                // Debug.LogWarning("[NetworkManager] Runner.SceneManager is null. Falling back to Unity SceneManager. Ensure StartGame is called with a SceneManager.");
                SceneManager.LoadScene(sceneIndex);
                return;
            }
            _runner.LoadScene(SceneRef.FromIndex(sceneIndex), LoadSceneMode.Single);
        }
        else
        {
            SceneManager.LoadScene(sceneIndex);
        }
    }

    // 세션 종료 후 특정 씬으로 복귀
    public void LeaveAndLoad(string sceneName)
    {
        string resolvedSceneName = ResolveSceneName(sceneName, SceneDefine.MatchingLobby);
        int sceneIndex = GetBuildIndexForScene(resolvedSceneName);
        if (_runner != null)
        {
            _startGameInProgress = false;
            _runner.Shutdown();
        }
        if (sceneIndex >= 0)
        {
            SceneManager.LoadScene(sceneIndex);
            return;
        }

        SceneManager.LoadScene(resolvedSceneName);
    }

    private static string ResolveSceneName(string sceneName, string fallback)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
        {
            return fallback;
        }

        switch (sceneName)
        {
            case "Title":
            case SceneDefine.Title:
                return SceneDefine.Title;
            case "MatchingLobby":
            case "TestMatching":
            case SceneDefine.MatchingLobby:
                return SceneDefine.MatchingLobby;
            case "JoinLobby":
            case SceneDefine.JoinLobby:
                return SceneDefine.JoinLobby;
            case "Game":
            case SceneDefine.Game:
                return SceneDefine.Game;
            default:
                return sceneName;
        }
    }

    private static int GetBuildIndexForScene(string sceneName)
    {
        int sceneIndex = SceneUtility.GetBuildIndexByScenePath($"Assets/Scenes/{sceneName}.unity");
        if (sceneIndex >= 0)
        {
            return sceneIndex;
        }

        return SceneUtility.GetBuildIndexByScenePath(sceneName);
    }

    #endregion


}


