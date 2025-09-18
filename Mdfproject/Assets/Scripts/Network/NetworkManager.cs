using System;
using System.Collections.Generic;
using Fusion;
using Fusion.Sockets;
using UnityEngine;
using UnityEngine.SceneManagement;
using GameCore.Enums;
using TMPro;


public class NetworkManager : MonoBehaviour, INetworkRunnerCallbacks
{
    public static NetworkManager Instance { get; private set; }

    public NetworkRunner _runner { get; private set; }

    public TMP_InputField NickNameInput;
    public TMP_InputField PassWordInput;

    [Header("Player")]
    // 스폰할 플레이어 프리팹입니다. Inspector에서 할당해야 합니다.
    [SerializeField] private NetworkObject _playerPrefab;
    // 서버에서 플레이어들을 관리하기 위한 딕셔너리입니다.
    private readonly Dictionary<PlayerRef, NetworkObject> _spawnedCharacters = new Dictionary<PlayerRef, NetworkObject>();

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



    private void Awake()
    {
        // 이미 인스턴스가 있는지 확인
        if (Instance == null)
        {
            // 인스턴스가 없으면, 이 오브젝트를 인스턴스로 지정
            Instance = this;
            // 씬이 전환되어도 이 게임 오브젝트가 파괴되지 않도록 설정
            DontDestroyOnLoad(gameObject);
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

    public void SetRoomNameInput(string roomname)
    {
        _roomNameInput = roomname;
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
        // 이미 연결 중이거나 게임 중이면 실행하지 않습니다.
        if (_runner != null) return;

        Debug.Log("Joining Lobby...");
        _state = ConnectionState.InLobby; // 상태를 '로비'로 변경

        // NetworkRunner 인스턴스를 생성하고 콜백을 받기 위해 등록합니다.
        _runner = gameObject.AddComponent<NetworkRunner>();
        _runner.AddCallbacks(this);

        // 기본 로비에 참여합니다.
        await _runner.JoinSessionLobby(SessionLobby.Shared);

        Debug.Log("Joined Lobby.");
    }

    /// <summary>
    /// 게임 세션(방)을 시작하거나 참여합니다.
    /// </summary>
    /// <param name="mode">Host, Client 등 게임 모드</param>
    /// <param name="sessionName">참여하거나 생성할 방의 이름</param>
    public async void StartGame(GameMode mode, string sessionName, string sceneName = null)
    {
        // 로비에 있을 때만 게임을 시작할 수 있습니다.
        if (_state != ConnectionState.InLobby) return;

        Debug.Log($"Starting Game with session name: {sessionName}, loading scene: {sceneName}");

        // Runner가 없으면 새로 생성하고 콜백을 등록합니다.
        if (_runner == null)
        {
            _runner = gameObject.AddComponent<NetworkRunner>();
            _runner.AddCallbacks(this);
        }
        
        _runner.ProvideInput = true;
        Debug.Log(sceneName);

        // 씬 이름을 기반으로 빌드 인덱스를 찾습니다.
        // ※ 주의: 로드할 씬은 반드시 File > Build Settings에 추가되어 있어야 합니다.
        int sceneIndex = SceneUtility.GetBuildIndexByScenePath($"Assets/Scenes/{sceneName}.unity");
        if (sceneIndex < 0)
        {
            Debug.LogError($"'{sceneName}' 씬을 빌드 설정에서 찾을 수 없습니다!");
            return;
        }
        var scene = SceneRef.FromIndex(sceneIndex);

        // StartGameArgs를 설정하여 게임을 시작합니다.
        await _runner.StartGame(new StartGameArgs()
        {
            GameMode = mode,
            SessionName = sessionName,
            Scene = scene, // Fusion이 이 씬을 로드하도록 지정합니다.
            SceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>(),
            PlayerCount = 2 // 최대 플레이어 수를 2명으로 설정
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

    #region UI 그리기 (OnGUI)
    // 이 부분은 실제 게임에서는 UGUI(버튼, 텍스트 등)로 구현하는 것이 좋습니다.
    // 테스트를 위해 간단히 OnGUI를 사용합니다.
    private void OnGUI()
    {
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

            case ConnectionState.InLobby:
                // 여기가 LobbyUI에서 방 생성 누르기 버튼
                // [로비] 상태일 때: 방 만들기 UI와 방 목록 표시 
                GUI.Label(new Rect(10, 10, 200, 30), "Room Name:");
                //_roomNameInput = GUI.TextField(new Rect(10, 40, 200, 40), _roomNameInput);

                if (GUI.Button(new Rect(10, 90, 200, 50), "Create Room"))
                {
                    // 입력된 이름으로 방을 생성(Host)합니다.
                    StartGame(GameMode.Host, GetRoomNameInput());
                }

                // 여기가 LObbyUI에사 방 확인 else 문이 리스트 출력
                // 방 목록 표시
                GUI.Label(new Rect(250, 10, 300, 30), "Available Rooms");
                if (_sessionList.Count == 0)
                {
                    GUI.Label(new Rect(250, 50, 300, 30), "No rooms available.");
                }
                else
                {
                    for (int i = 0; i < _sessionList.Count; i++)
                    {
                        var session = _sessionList[i];
                        string roomInfo = $"{session.Name} ({session.PlayerCount}/{session.MaxPlayers})";
                        if (GUI.Button(new Rect(250, 50 + (i * 60), 300, 50), roomInfo))
                        {
                            // 해당 방에 참가(Client)합니다.
                            StartGame(GameMode.Client, session.Name, "JoinLobby");
                        }
                    }
                }
                break;

            case ConnectionState.InGame:
                // [게임 중] 상태일 때: 나가기 버튼과 방 정보 표시
                GUI.Label(new Rect(10, 10, 300, 30), $"In Room: {_runner.SessionInfo.Name}");
                if (GUI.Button(new Rect(10, 50, 200, 50), "Leave Game"))
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
        Debug.Log("Session list updated. Found " + sessionList.Count + " sessions.");
        // 받은 목록으로 로컬 목록을 갱신합니다.
        _sessionList = sessionList;
    }

    // 플레이어가 게임 세션에 성공적으로 참여했을 때 호출됩니다.
    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        Debug.Log($"Player {player} Joined.");
        _state = ConnectionState.InGame; // 상태를 '게임 중'으로 변경

        if (runner.IsServer)
        {
            Debug.Log("Spawning player character...");
            // 서버(호스트)는 새로 참여한 플레이어의 캐릭터를 스폰합니다.
            // ✅ 아래 줄의 주석이 해제되어 있는지 확인하세요.
            NetworkObject networkPlayerObject = runner.Spawn(_playerPrefab, Vector3.zero, Quaternion.identity, player);
            _spawnedCharacters.Add(player, networkPlayerObject);
        }
    }

    // 플레이어가 게임 세션을 떠났을 때 호출됩니다.
    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        Debug.Log($"Player {player} Left.");
        if (_spawnedCharacters.TryGetValue(player, out NetworkObject networkObject))
        {
            runner.Despawn(networkObject);
            _spawnedCharacters.Remove(player);
        }
    }

    // Runner가 종료되었을 때 호출됩니다. (연결 끊김, 스스로 나가기 등)
    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
    {
        Debug.Log("OnShutdown: " + shutdownReason);
        _state = ConnectionState.Disconnected; // 상태를 '연결 끊김'으로 변경
        _sessionList.Clear(); // 방 목록 초기화

        // Runner 오브젝트를 파괴하여 정리합니다.
        if (runner != null && runner.gameObject != null)
        {
            Destroy(runner.gameObject);
        }
        _runner = null; // 참조를 null로 설정하여 중복 생성을 방지합니다.
    }

    // --- 이하 콜백들은 이 예제에서 사용되지 않지만, 인터페이스 구현을 위해 필요합니다. ---
    public void OnConnectedToServer(NetworkRunner runner) { }
    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
    public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason) { }
    public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
    public void OnInput(NetworkRunner runner, NetworkInput input) { }
    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data) { }
    public void OnSceneLoadDone(NetworkRunner runner) { }
    public void OnSceneLoadStart(NetworkRunner runner) { }
    public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
    #endregion

    #region 외부 클래스 접근 함수
    public void SetRunner()
    {

    }

    #endregion


}
