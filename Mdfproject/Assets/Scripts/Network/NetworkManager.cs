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
    // ?¤í°???Œë ˆ?´ì–´ ?„ë¦¬?¹ì…?ˆë‹¤. Inspector?ì„œ ? ë‹¹?´ì•¼ ?©ë‹ˆ??
    [SerializeField] private NetworkObject _playerPrefab;
    // ?œë²„?ì„œ ?Œë ˆ?´ì–´?¤ì„ ê´€ë¦¬í•˜ê¸??„í•œ ?•ì…”?ˆë¦¬?…ë‹ˆ??
    private readonly Dictionary<PlayerRef, NetworkObject> _spawnedCharacters = new Dictionary<PlayerRef, NetworkObject>();

    [Header("Lobby & UI")]
    // ?„ì¬ ë¡œë¹„???ˆëŠ” ?¸ì…˜(ë°? ëª©ë¡???€?¥í•©?ˆë‹¤.
    public List<SessionInfo> _sessionList = new List<SessionInfo>();
    // ? ì?ê°€ ?…ë ¥??ë°??œëª©???€?¥í•˜??ë³€?˜ì…?ˆë‹¤.
    private string _roomNameInput = "MyFusionRoom";
    // ?„ì¬ ?¤íŠ¸?Œí¬ ?íƒœë¥?ê´€ë¦¬í•©?ˆë‹¤. (?°ê²° ?Šê?, ë¡œë¹„, ê²Œì„ ì¤?

    // 2. ?„ì¬ ?íƒœë¥??€?¥í•˜ê³? ë³€ê²????´ë²¤?¸ë? ë°œìƒ?œí‚¤???„ë¡œ?¼í‹°
    private ConnectionState _state;
    public ConnectionState State
    {
        get => _state;
        private set
        {
            _state = value;
            // ?íƒœê°€ ë³€ê²½ë  ?Œë§ˆ??OnStateChanged ?´ë²¤?¸ë? ?¸ì¶œ(ë°©ì†¡)
            OnStateChanged?.Invoke(_state);
        }
    }

    // 3. ?íƒœ ë³€ê²??´ë²¤?¸ë? ?•ì˜ (Action ?¸ë¦¬ê²Œì´???¬ìš©)
    public static event Action<ConnectionState> OnStateChanged;

    public bool IsGameRunnerActive => _runner != null && _runner.IsRunning;

    private int playerCount;

    private void Awake()
    {
        Application.targetFrameRate = 60;
        // ?´ë? ?¸ìŠ¤?´ìŠ¤ê°€ ?ˆëŠ”ì§€ ?•ì¸
        if (Instance == null)
        {
            // ?¸ìŠ¤?´ìŠ¤ê°€ ?†ìœ¼ë©? ???¤ë¸Œ?íŠ¸ë¥??¸ìŠ¤?´ìŠ¤ë¡?ì§€??
            Instance = this;
            // ?¬ì´ ?„í™˜?˜ì–´????ê²Œì„ ?¤ë¸Œ?íŠ¸ê°€ ?Œê´´?˜ì? ?Šë„ë¡??¤ì •
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            // ?´ë? ?¸ìŠ¤?´ìŠ¤ê°€ ì¡´ì¬?˜ë©´, ?ˆë¡œ ?ê¸´ ì¤‘ë³µ ?¤ë¸Œ?íŠ¸???Œê´´
            // (?? ë©”ì¸ ë©”ë‰´ ?¬ì—??ê²Œì„ ?¬ìœ¼ë¡??Œì•„?”ì„ ??ë§¤ë‹ˆ?€ê°€ ì¤‘ë³µ ?ì„±?˜ëŠ” ê²ƒì„ ë°©ì?)
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
    /// ?¹ì • ë¡œë¹„??ì°¸ì—¬ë¥??œì‘?©ë‹ˆ??
    /// </summary>
    public async void JoinLobby()
    {
        // ?´ë? ?°ê²° ì¤‘ì´ê±°ë‚˜ ê²Œì„ ì¤‘ì´ë©??¤í–‰?˜ì? ?ŠìŠµ?ˆë‹¤.
        if (_runner != null) return;

        Debug.Log("Joining Lobby...");
        _state = ConnectionState.InLobby; // ?íƒœë¥?'ë¡œë¹„'ë¡?ë³€ê²?

        // NetworkRunner ?¸ìŠ¤?´ìŠ¤ë¥??ì„±?˜ê³  ì½œë°±??ë°›ê¸° ?„í•´ ?±ë¡?©ë‹ˆ??
        _runner = gameObject.AddComponent<NetworkRunner>();
        _runner.AddCallbacks(this);

        // ê¸°ë³¸ ë¡œë¹„??ì°¸ì—¬?©ë‹ˆ??
        await _runner.JoinSessionLobby(SessionLobby.Shared);

        Debug.Log("Joined Lobby.");
    }

    /// <summary>
    /// ê²Œì„ ?¸ì…˜(ë°????œì‘?˜ê±°??ì°¸ì—¬?©ë‹ˆ??
    /// </summary>
    /// <param name="mode">Host, Client ??ê²Œì„ ëª¨ë“œ</param>
    /// <param name="sessionName">ì°¸ì—¬?˜ê±°???ì„±??ë°©ì˜ ?´ë¦„</param>
    public async void StartGame(GameMode mode, string sessionName, string sceneName = null)
    {
        // ë¡œë¹„???ˆì„ ?Œë§Œ ê²Œì„???œì‘?????ˆìŠµ?ˆë‹¤.
        if (_state != ConnectionState.InLobby) return;

        string finalSessionName = string.IsNullOrWhiteSpace(sessionName)
            ? PlayerPrefs.GetString("PlayerNickname", "Host")
            : sessionName;

        Debug.Log($"Starting Game with session name: {finalSessionName}, loading scene: {sceneName}");

        // Runnerê°€ ?†ìœ¼ë©??ˆë¡œ ?ì„±?˜ê³  ì½œë°±???±ë¡?©ë‹ˆ??
        if (_runner == null)
        {
            _runner = gameObject.AddComponent<NetworkRunner>();
            _runner.AddCallbacks(this);
        }

        _runner.ProvideInput = true;
        Debug.Log(sceneName);

        // ???´ë¦„??ê¸°ë°˜?¼ë¡œ ë¹Œë“œ ?¸ë±?¤ë? ì°¾ìŠµ?ˆë‹¤.
        // ??ì£¼ì˜: ë¡œë“œ???¬ì? ë°˜ë“œ??File > Build Settings??ì¶”ê??˜ì–´ ?ˆì–´???©ë‹ˆ??
        int sceneIndex = SceneUtility.GetBuildIndexByScenePath($"Assets/Scenes/{sceneName}.unity");
        if (sceneIndex < 0)
        {
            Debug.LogError($"'{sceneName}' ?¬ì„ ë¹Œë“œ ?¤ì •?ì„œ ì°¾ì„ ???†ìŠµ?ˆë‹¤!");
            return;
        }
        var scene = SceneRef.FromIndex(sceneIndex);

        var objectProvider = gameObject.GetComponent<PooledNetworkObjectProvider>();
        if (objectProvider == null)
        {
            objectProvider = gameObject.AddComponent<PooledNetworkObjectProvider>();
        }

        // StartGameArgsë¥??¤ì •?˜ì—¬ ê²Œì„???œì‘?©ë‹ˆ??
        await _runner.StartGame(new StartGameArgs()
        {
            GameMode = mode,
            SessionName = finalSessionName,
            Scene = scene, // Fusion?????¬ì„ ë¡œë“œ?˜ë„ë¡?ì§€?•í•©?ˆë‹¤.
            SceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>(),
            PlayerCount = 2,
            ObjectProvider = objectProvider
        });
    }

    /// <summary>
    /// ?„ì¬ ?¤í–‰ ì¤‘ì¸ ê²Œì„ ?¸ì…˜??ì¢…ë£Œ?©ë‹ˆ??
    /// </summary>
    private void LeaveGame()
    {
        if (_runner != null)
        {
            // Runnerë¥?ì¢…ë£Œ?˜ë©´ OnShutdown ì½œë°±???¸ì¶œ?©ë‹ˆ??
            _runner.Shutdown();
        }
    }

    /// <summary>
    /// [?´ë¼?´ì–¸??-> ?œë²„] ì»¤ë§¨???¤í–‰???œë²„???”ì²­?˜ëŠ” RPC
    /// </summary>
    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_RequestCommandToServer(CommandType type, int[] intParams, string[] stringParams, Vector3[] vectorParams, RpcInfo info = default)
    {
        // TODO: ?¬ê¸°???œë²„??ì»¤ë§¨?œì˜ ? íš¨?±ì„ ê²€?¬í•´???©ë‹ˆ??
        // ?? ?Œë ˆ?´ì–´ê°€ ê³¨ë“œê°€ ì¶©ë¶„?œì?, ? ë‹› ë°°ì¹˜ê°€ ? íš¨???„ì¹˜?¸ì? ??
        // ? íš¨??ê²€?¬ëŠ” ë³´ì•ˆ(ì¹˜íŒ… ë°©ì?)??ë§¤ìš° ì¤‘ìš”?©ë‹ˆ??
        // bool isValid = ValidateCommand(type, intParams, stringParams, vectorParams, info.Source);
        bool isValid = true; // ì§€ê¸ˆì? ëª¨ë“  ?”ì²­??? íš¨?˜ë‹¤ê³?ê°€??

        if (isValid)
        {
            // ? íš¨??ê²€?¬ë? ?µê³¼?˜ë©´, ëª¨ë“  ?´ë¼?´ì–¸?¸ì—ê²???ì»¤ë§¨?œë? ?¤í–‰?˜ë¼ê³?ë¸Œë¡œ?œìº?¤íŒ…?©ë‹ˆ??
            RPC_BroadcastCommandToClients(type, intParams, stringParams, vectorParams);
        }
        else
        {
            // (? íƒ?? ?”ì²­??ë³´ë‚¸ ?´ë¼?´ì–¸?¸ì—ê²Œë§Œ ?¤íŒ¨ë¥??Œë¦´ ???ˆìŠµ?ˆë‹¤.
            Debug.LogWarning($"Player {info.Source.PlayerId}??{type} ì»¤ë§¨???”ì²­??? íš¨??ê²€?¬ì— ?¤íŒ¨?ˆìŠµ?ˆë‹¤.");
        }
    }

    /// <summary>
    /// [?œë²„ -> ëª¨ë“  ?´ë¼?´ì–¸?? ?œë²„ê°€ ?¹ì¸??ì»¤ë§¨?œë? ëª¨ë“  ?´ë¼?´ì–¸?¸ì—???¤í–‰?˜ë„ë¡?ë¸Œë¡œ?œìº?¤íŒ…?˜ëŠ” RPC
    /// </summary>
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_BroadcastCommandToClients(CommandType type, int[] intParams, string[] stringParams, Vector3[] vectorParams)
    {
        if (GameManagers.Instance != null && GameManagers.Instance.CommandProcessor != null)
        {
            GameManagers.Instance.CommandProcessor.ReceiveAndEnqueueCommand(type, intParams, stringParams, vectorParams);
        }
    }


    #region UI ê·¸ë¦¬ê¸?(OnGUI)
    // ??ë¶€ë¶„ì? ?¤ì œ ê²Œì„?ì„œ??UGUI(ë²„íŠ¼, ?ìŠ¤????ë¡?êµ¬í˜„?˜ëŠ” ê²ƒì´ ì¢‹ìŠµ?ˆë‹¤.
    // ?ŒìŠ¤?¸ë? ?„í•´ ê°„ë‹¨??OnGUIë¥??¬ìš©?©ë‹ˆ??
    private void OnGUI()
    {
        GUI.skin.button.fontSize = 20;
        GUI.skin.textField.fontSize = 20;
        GUI.skin.label.fontSize = 20;

        switch (_state)
        {
            // Title ?¬ì—???¬ìš©
            // case ConnectionState.Disconnected:
            //     // [?°ê²° ?Šê?] ?íƒœ???? ë¡œë¹„ ?‘ì† ë²„íŠ¼ë§??œì‹œ
            //     if (GUI.Button(new Rect(10, 10, 200, 50), "Join Lobby"))
            //     {
            //         JoinLobby();
            //     }
            //     break;

            // case ConnectionState.InLobby:
            //     // ?¬ê¸°ê°€ LobbyUI?ì„œ ë°??ì„± ?„ë¥´ê¸?ë²„íŠ¼
            //     // [ë¡œë¹„] ?íƒœ???? ë°?ë§Œë“¤ê¸?UI?€ ë°?ëª©ë¡ ?œì‹œ 
            //     GUI.Label(new Rect(10, 10, 200, 30), "Room Name:");
            //     //_roomNameInput = GUI.TextField(new Rect(10, 40, 200, 40), _roomNameInput);

            //     if (GUI.Button(new Rect(10, 90, 200, 50), "Create Room"))
            //     {
            //         // ?…ë ¥???´ë¦„?¼ë¡œ ë°©ì„ ?ì„±(Host)?©ë‹ˆ??
            //         StartGame(GameMode.Host, GetRoomNameInput());
            //     }

            //     // ?¬ê¸°ê°€ LObbyUI?ì‚¬ ë°??•ì¸ else ë¬¸ì´ ë¦¬ìŠ¤??ì¶œë ¥
            //     // ë°?ëª©ë¡ ?œì‹œ
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
            //                 // ?´ë‹¹ ë°©ì— ì°¸ê?(Client)?©ë‹ˆ??
            //                 StartGame(GameMode.Client, session.Name, "JoinLobby");
            //             }
            //         }
            //     }
            //     break;

            case ConnectionState.InGame:
                // [ê²Œì„ ì¤? ?íƒœ???? ?˜ê?ê¸?ë²„íŠ¼ê³?ë°??•ë³´, ?Œë ˆ?´ì–´ ???œì‹œ
                GUI.Label(new Rect(10, 10, 300, 30), $"In Room: {_runner.SessionInfo.Name}");

                // --- ??ì¶”ê???ë¶€ë¶??œì‘ ??---
                if (_runner != null && _runner.SessionInfo != null)
                {
                    // ?„ì¬ ?Œë ˆ?´ì–´ ?˜ì? ìµœë? ?Œë ˆ?´ì–´ ?˜ë? ê°€?¸ì????œì‹œ?©ë‹ˆ??
                    playerCount = _runner.SessionInfo.PlayerCount;
                    int maxPlayers = _runner.SessionInfo.MaxPlayers;
                    GUI.Label(new Rect(10, 50, 300, 30), $"Players: {playerCount} / {maxPlayers}");
                }
                // --- ??ì¶”ê???ë¶€ë¶?ì¢…ë£Œ ??---

                // ê¸°ì¡´ 'Leave Game' ë²„íŠ¼???„ì¹˜ë¥??„ë˜ë¡?ì¡°ì •?©ë‹ˆ??(y: 50 -> 90)
                if (GUI.Button(new Rect(10, 90, 200, 50), "Leave Game"))
                {
                    LeaveGame();
                }
                break;
        }
    }
    #endregion


    #region INetworkRunnerCallbacks êµ¬í˜„
    // ??ì½œë°±?€ ë¡œë¹„???ˆëŠ” ë°?ëª©ë¡???…ë°?´íŠ¸???Œë§ˆ???¸ì¶œ?©ë‹ˆ??
    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList)
    {
        Debug.Log("Session list updated. Found " + sessionList.Count + " sessions.");
        // ë°›ì? ëª©ë¡?¼ë¡œ ë¡œì»¬ ëª©ë¡??ê°±ì‹ ?©ë‹ˆ??
        _sessionList = sessionList;
    }

    // ?Œë ˆ?´ì–´ê°€ ê²Œì„ ?¸ì…˜???±ê³µ?ìœ¼ë¡?ì°¸ì—¬?ˆì„ ???¸ì¶œ?©ë‹ˆ??
    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        Debug.Log($"Player {player} Joined.");
        _state = ConnectionState.InGame; // ?íƒœë¥?'ê²Œì„ ì¤??¼ë¡œ ë³€ê²?

        if (runner.IsServer)
        {
            Debug.Log("Spawning player character...");
            // ?œë²„(?¸ìŠ¤?????ˆë¡œ ì°¸ì—¬???Œë ˆ?´ì–´??ìºë¦­?°ë? ?¤í°?©ë‹ˆ??
            // ???„ë˜ ì¤„ì˜ ì£¼ì„???´ì œ?˜ì–´ ?ˆëŠ”ì§€ ?•ì¸?˜ì„¸??
            NetworkObject networkPlayerObject = runner.Spawn(_playerPrefab, Vector3.zero, Quaternion.identity, player);
            _spawnedCharacters.Add(player, networkPlayerObject);
        }
    }

    // ?Œë ˆ?´ì–´ê°€ ê²Œì„ ?¸ì…˜??? ë‚¬?????¸ì¶œ?©ë‹ˆ??
    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        Debug.Log($"Player {player} Left.");
        if (_spawnedCharacters.TryGetValue(player, out NetworkObject networkObject))
        {
            runner.Despawn(networkObject);
            _spawnedCharacters.Remove(player);
        }
    }

    // Runnerê°€ ì¢…ë£Œ?˜ì—ˆ?????¸ì¶œ?©ë‹ˆ?? (?°ê²° ?Šê?, ?¤ìŠ¤ë¡??˜ê?ê¸???
    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
    {
        Debug.Log("OnShutdown: " + shutdownReason);
        _state = ConnectionState.Disconnected; // ?íƒœë¥?'?°ê²° ?Šê?'?¼ë¡œ ë³€ê²?
        _sessionList.Clear(); // ë°?ëª©ë¡ ì´ˆê¸°??

        // Runner ?¤ë¸Œ?íŠ¸ë¥??Œê´´?˜ì—¬ ?•ë¦¬?©ë‹ˆ??
        if (runner != null && runner.gameObject != null)
        {
            Destroy(runner.gameObject);
        }
        _runner = null; // ì°¸ì¡°ë¥?nullë¡??¤ì •?˜ì—¬ ì¤‘ë³µ ?ì„±??ë°©ì??©ë‹ˆ??
    }

    // --- ?´í•˜ ì½œë°±?¤ì? ???ˆì œ?ì„œ ?¬ìš©?˜ì? ?Šì?ë§? ?¸í„°?˜ì´??êµ¬í˜„???„í•´ ?„ìš”?©ë‹ˆ?? ---
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

    #region ?¸ë? ?´ë˜???‘ê·¼ ?¨ìˆ˜
    public void SetRunner()
    {

    }

    // ?¸ë? ?´ë˜?¤ì—???Œë ˆ?´ì–´ ëª‡ëª… ?ì„± ?´ì•¼?˜ëŠ”ì§€ ?•ì¸?????„ìš”???¨ìˆ˜
    public int GetPlayerCount()
    {
        return playerCount;
    }

    // Fusion ?œì?: ?¸ì…˜ ì¤‘ì´ë©?Runnerë¡??¬ì„ ?„í™˜, ?„ë‹ˆë©?Unity ??ë¡œë“œ ?¬ìš©
    public void LoadSceneSmart(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName))
        {
            Debug.LogError("[NetworkManager] sceneName is null or empty");
            return;
        }

        int sceneIndex = SceneUtility.GetBuildIndexByScenePath($"Assets/Scenes/{sceneName}.unity");
        if (sceneIndex < 0)
        {
            Debug.LogError($"[NetworkManager] '{sceneName}' ?¬ì„ ë¹Œë“œ ?¤ì •?ì„œ ì°¾ì„ ???†ìŠµ?ˆë‹¤!");
            return;
        }

        if (_runner != null && _runner.IsRunning)
        {
            if (_runner.SceneManager == null)
            {
                Debug.LogWarning("[NetworkManager] Runner.SceneManager is null. Falling back to Unity SceneManager. Ensure StartGame is called with a SceneManager.");
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

    // ?¸ì…˜ ì¢…ë£Œ ???¹ì • ?¬ìœ¼ë¡?ë³µê?
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

