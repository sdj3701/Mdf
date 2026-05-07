using System.Collections.Generic;
using System.Linq;
using Fusion;
using GameCore.Enums;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
public sealed class JoinLobbyUI : MonoBehaviour
{
    private const float DesignWidth = 1672f;
    private const float DesignHeight = 941f;
    private const int SlotCount = 4;

    public static JoinLobbyUI Instance { get; private set; }

    [Header("UI Toolkit")]
    [SerializeField] private UIDocument document;

    [Header("Scene")]
    [SerializeField] private string matchingLobbySceneName = SceneDefine.MatchingLobby;
    [SerializeField] private string gameScenePath = "Assets/Scenes/Game.unity";

    [Header("Preview")]
    [SerializeField] private bool useMockPlayersWhenNoPlayers = true;

    private readonly SlotView[] slots = new SlotView[SlotCount];

    private VisualElement root;
    private VisualElement designSpace;
    private VisualElement gameStartButton;
    private VisualElement readyButton;
    private VisualElement leaveRoomButton;
    private VisualElement networkBlockOverlay;

    private Label roomNameLabel;
    private Label playerCountLabel;
    private Label readyButtonLabel;
    private Label statusLabel;

    private NetworkManager networkManager;
    private NetworkPlayer localPlayer;
    private bool callbacksRegistered;

    private struct PlayerViewData
    {
        public readonly string Name;
        public readonly int Level;
        public readonly bool IsReady;
        public readonly int VisualIndex;
        public readonly bool IsEmpty;

        public PlayerViewData(string name, int level, bool isReady, int visualIndex, bool isEmpty = false)
        {
            Name = name;
            Level = level;
            IsReady = isReady;
            VisualIndex = visualIndex;
            IsEmpty = isEmpty;
        }
    }

    private sealed class SlotView
    {
        public VisualElement Root;
        public VisualElement Portrait;
        public Label Name;
        public Label Level;
        public Label Ready;
    }

    private void Reset()
    {
        document = GetComponent<UIDocument>();
    }

    private void Awake()
    {
        if (document == null)
        {
            document = GetComponent<UIDocument>();
        }

        if (document == null)
        {
            enabled = false;
            return;
        }

        if (Instance == null)
        {
            Instance = this;
        }
        else if (Instance != this)
        {
            Destroy(gameObject);
        }
    }

    private void OnEnable()
    {
        if (document == null || document.rootVisualElement == null)
        {
            Debug.LogError("[JoinLobbyUI] UIDocument is missing.");
            return;
        }

        root = document.rootVisualElement;
        BindElements();
        ConfigureInitialState();
        RegisterCallbacks();
        SubscribeNetworkEvents();
        ResolveNetworkManager();
        UpdatePlayerList();
        RefreshNetworkBlockOverlay();
        UpdateDesignScale();
    }

    private void OnDisable()
    {
        UnregisterCallbacks();
        UnsubscribeNetworkEvents();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void BindElements()
    {
        designSpace = Query<VisualElement>("joinlobby-design-space");
        gameStartButton = Query<VisualElement>("gameStartButton");
        readyButton = Query<VisualElement>("readyButton");
        leaveRoomButton = Query<VisualElement>("leaveRoomButton");
        networkBlockOverlay = Query<VisualElement>("networkBlockOverlay");

        roomNameLabel = Query<Label>("roomNameLabel");
        playerCountLabel = Query<Label>("playerCountLabel");
        readyButtonLabel = Query<Label>("readyButtonLabel");
        statusLabel = Query<Label>("statusLabel");

        for (int i = 0; i < slots.Length; i++)
        {
            slots[i] = new SlotView
            {
                Root = Query<VisualElement>($"slot{i}"),
                Portrait = Query<VisualElement>($"portrait{i}"),
                Name = Query<Label>($"slotName{i}"),
                Level = Query<Label>($"slotLevel{i}"),
                Ready = Query<Label>($"slotReady{i}")
            };
        }
    }

    private T Query<T>(string elementName) where T : VisualElement
    {
        T element = root.Q<T>(elementName);
        if (element == null)
        {
            Debug.LogError($"[JoinLobbyUI] UXML element not found: {elementName}");
        }

        return element;
    }

    private void ConfigureInitialState()
    {
        SetDisplay(networkBlockOverlay, false);
        SetPickingMode(gameStartButton, PickingMode.Position);
        SetPickingMode(readyButton, PickingMode.Position);
        SetPickingMode(leaveRoomButton, PickingMode.Position);
    }

    private void RegisterCallbacks()
    {
        if (callbacksRegistered)
        {
            return;
        }

        root?.RegisterCallback<GeometryChangedEvent>(OnRootGeometryChanged);
        gameStartButton?.RegisterCallback<PointerUpEvent>(OnGameStartPointerUp);
        readyButton?.RegisterCallback<PointerUpEvent>(OnReadyPointerUp);
        leaveRoomButton?.RegisterCallback<PointerUpEvent>(OnLeaveRoomPointerUp);

        callbacksRegistered = true;
    }

    private void UnregisterCallbacks()
    {
        if (!callbacksRegistered)
        {
            return;
        }

        root?.UnregisterCallback<GeometryChangedEvent>(OnRootGeometryChanged);
        gameStartButton?.UnregisterCallback<PointerUpEvent>(OnGameStartPointerUp);
        readyButton?.UnregisterCallback<PointerUpEvent>(OnReadyPointerUp);
        leaveRoomButton?.UnregisterCallback<PointerUpEvent>(OnLeaveRoomPointerUp);

        callbacksRegistered = false;
    }

    private void SubscribeNetworkEvents()
    {
        NetworkManager.OnPlayerJoinedEvent += OnPlayerJoined;
        NetworkManager.OnPlayerLeftEvent += OnPlayerLeft;
        NetworkManager.OnNetworkUiBlockChanged += OnNetworkUiBlockChanged;
        NetworkManager.OnStateChanged += OnNetworkStateChanged;
    }

    private void UnsubscribeNetworkEvents()
    {
        NetworkManager.OnPlayerJoinedEvent -= OnPlayerJoined;
        NetworkManager.OnPlayerLeftEvent -= OnPlayerLeft;
        NetworkManager.OnNetworkUiBlockChanged -= OnNetworkUiBlockChanged;
        NetworkManager.OnStateChanged -= OnNetworkStateChanged;
    }

    private void ResolveNetworkManager()
    {
        networkManager = NetworkManager.Instance;
        if (networkManager == null)
        {
            networkManager = FindObjectOfType<NetworkManager>();
        }
    }

    private void OnPlayerJoined(PlayerRef player)
    {
        Invoke(nameof(UpdatePlayerList), 0.1f);
    }

    private void OnPlayerLeft(PlayerRef player)
    {
        Invoke(nameof(UpdatePlayerList), 0.1f);
    }

    private void OnNetworkUiBlockChanged()
    {
        RefreshNetworkBlockOverlay();
    }

    private void OnNetworkStateChanged(ConnectionState state)
    {
        UpdatePlayerList();
        RefreshNetworkBlockOverlay();
    }

    private void OnRootGeometryChanged(GeometryChangedEvent evt)
    {
        UpdateDesignScale();
    }

    private void OnGameStartPointerUp(PointerUpEvent evt)
    {
        if (!IsPrimaryPointer(evt))
        {
            return;
        }

        TryStartGame();
        evt.StopPropagation();
    }

    private void OnReadyPointerUp(PointerUpEvent evt)
    {
        if (!IsPrimaryPointer(evt))
        {
            return;
        }

        ToggleReady();
        evt.StopPropagation();
    }

    private void OnLeaveRoomPointerUp(PointerUpEvent evt)
    {
        if (!IsPrimaryPointer(evt))
        {
            return;
        }

        ResolveNetworkManager();
        if (networkManager != null)
        {
            networkManager.LeaveAndLoad(matchingLobbySceneName);
        }
        else if (!string.IsNullOrWhiteSpace(matchingLobbySceneName))
        {
            SceneManager.LoadScene(matchingLobbySceneName);
        }

        evt.StopPropagation();
    }

    public void UpdatePlayerList()
    {
        if (this == null)
        {
            return;
        }

        ResolveNetworkManager();

        List<NetworkPlayer> players = FindObjectsOfType<NetworkPlayer>()
            .Where(player => player != null)
            .OrderBy(GetPlayerSortKey)
            .ToList();

        localPlayer = players.FirstOrDefault(player => player.HasInputAuthority) ?? localPlayer;

        bool usingMockPlayers = useMockPlayersWhenNoPlayers && players.Count == 0;
        List<PlayerViewData> viewData = usingMockPlayers
            ? BuildMockPlayers()
            : BuildPlayerViewData(players);

        RenderSlots(viewData);
        UpdateRoomInfo(players.Count, usingMockPlayers);
        UpdateButtons(players, usingMockPlayers);
    }

    private static List<PlayerViewData> BuildMockPlayers()
    {
        return new List<PlayerViewData>
        {
            new PlayerViewData("루나", 42, true, 0),
            new PlayerViewData("카인", 45, true, 1),
            new PlayerViewData("미르", 40, true, 2)
        };
    }

    private static List<PlayerViewData> BuildPlayerViewData(List<NetworkPlayer> players)
    {
        List<PlayerViewData> viewData = new List<PlayerViewData>();
        for (int i = 0; i < players.Count && i < SlotCount; i++)
        {
            NetworkPlayer player = players[i];
            string nickname = player.Nickname.ToString();
            if (string.IsNullOrWhiteSpace(nickname))
            {
                nickname = NetworkDefine.DefaultNickname;
            }

            viewData.Add(new PlayerViewData(
                nickname,
                GetDisplayLevel(i),
                player.IsReady,
                i));
        }

        return viewData;
    }

    private void RenderSlots(List<PlayerViewData> players)
    {
        for (int i = 0; i < slots.Length; i++)
        {
            if (i < players.Count)
            {
                RenderOccupiedSlot(slots[i], players[i]);
            }
            else
            {
                RenderEmptySlot(slots[i]);
            }
        }
    }

    private static void RenderOccupiedSlot(SlotView slot, PlayerViewData player)
    {
        if (slot == null || slot.Root == null)
        {
            return;
        }

        slot.Root.RemoveFromClassList("jl-slot--empty");
        slot.Root.RemoveFromClassList("jl-slot--ready");
        slot.Root.RemoveFromClassList("jl-slot--waiting");
        slot.Root.AddToClassList(player.IsReady ? "jl-slot--ready" : "jl-slot--waiting");

        SetDisplay(slot.Portrait, true);
        SetPortraitClass(slot.Portrait, player.VisualIndex);

        if (slot.Name != null)
        {
            slot.Name.text = player.Name;
        }

        if (slot.Level != null)
        {
            slot.Level.text = $"Lv. {player.Level}";
        }

        if (slot.Ready != null)
        {
            slot.Ready.text = player.IsReady ? "READY" : "대기";
        }
    }

    private static void RenderEmptySlot(SlotView slot)
    {
        if (slot == null || slot.Root == null)
        {
            return;
        }

        slot.Root.RemoveFromClassList("jl-slot--ready");
        slot.Root.RemoveFromClassList("jl-slot--waiting");
        slot.Root.AddToClassList("jl-slot--empty");

        SetDisplay(slot.Portrait, false);

        if (slot.Name != null)
        {
            slot.Name.text = "빈 자리";
        }

        if (slot.Level != null)
        {
            slot.Level.text = string.Empty;
        }

        if (slot.Ready != null)
        {
            slot.Ready.text = "대기";
        }
    }

    private void UpdateRoomInfo(int realPlayerCount, bool usingMockPlayers)
    {
        int maxPlayers = GetMaxPlayers();
        int currentPlayers = usingMockPlayers ? 3 : Mathf.Clamp(realPlayerCount, 0, maxPlayers);

        if (roomNameLabel != null)
        {
            roomNameLabel.text = GetRoomName();
        }

        if (playerCountLabel != null)
        {
            playerCountLabel.text = $"방 인원 {currentPlayers} / {maxPlayers}";
        }
    }

    private void UpdateButtons(List<NetworkPlayer> players, bool usingMockPlayers)
    {
        bool hasNetworkRunner = networkManager != null && networkManager._runner != null;
        bool isHost = hasNetworkRunner && networkManager._runner.IsServer;
        bool hasRealPlayers = !usingMockPlayers && players.Count > 0;
        bool allReady = hasRealPlayers && players.All(player => player.IsReady);
        bool canStart = isHost && allReady;

        gameStartButton?.EnableInClassList("jl-button--disabled", !canStart);

        if (readyButtonLabel != null)
        {
            readyButtonLabel.text = localPlayer != null && localPlayer.IsReady ? "준비 취소" : "준비";
        }

        if (statusLabel == null)
        {
            return;
        }

        if (!hasNetworkRunner || usingMockPlayers)
        {
            statusLabel.text = "ⓘ 모든 인원이 준비를 완료해야 게임을 시작할 수 있습니다.";
        }
        else if (!isHost)
        {
            statusLabel.text = "ⓘ 방장이 게임을 시작할 수 있습니다.";
        }
        else if (!allReady)
        {
            statusLabel.text = "ⓘ 모든 인원이 준비를 완료해야 게임을 시작할 수 있습니다.";
        }
        else
        {
            statusLabel.text = "ⓘ 모든 준비가 완료되었습니다. 게임을 시작할 수 있습니다.";
        }
    }

    private void TryStartGame()
    {
        ResolveNetworkManager();
        List<NetworkPlayer> players = FindObjectsOfType<NetworkPlayer>()
            .Where(player => player != null)
            .ToList();

        if (networkManager == null || networkManager._runner == null)
        {
            ShowStatus("ⓘ 네트워크 세션 정보를 찾을 수 없습니다.");
            return;
        }

        if (!networkManager._runner.IsServer)
        {
            ShowStatus("ⓘ 방장만 게임을 시작할 수 있습니다.");
            return;
        }

        if (players.Count == 0 || !players.All(player => player.IsReady))
        {
            ShowStatus("ⓘ 모든 인원이 준비를 완료해야 게임을 시작할 수 있습니다.");
            return;
        }

        int sceneIndex = SceneUtility.GetBuildIndexByScenePath(gameScenePath);
        if (sceneIndex < 0)
        {
            ShowStatus("ⓘ Game 씬을 빌드 설정에서 찾을 수 없습니다.");
            return;
        }

        networkManager._runner.LoadScene(SceneRef.FromIndex(sceneIndex), LoadSceneMode.Single);
    }

    private void ToggleReady()
    {
        if (localPlayer == null)
        {
            UpdatePlayerList();
        }

        if (localPlayer == null)
        {
            ShowStatus("ⓘ 로컬 플레이어 정보를 아직 찾을 수 없습니다.");
            return;
        }

        localPlayer.RPC_ToggleReady();
    }

    private void RefreshNetworkBlockOverlay()
    {
        ResolveNetworkManager();
        bool shouldBlock = networkManager != null && networkManager.IsNetworkUiBlocked;
        SetDisplay(networkBlockOverlay, shouldBlock);
        SetControlsEnabled(!shouldBlock);
    }

    private void SetControlsEnabled(bool enabled)
    {
        gameStartButton?.SetEnabled(enabled);
        readyButton?.SetEnabled(enabled);
        leaveRoomButton?.SetEnabled(enabled);
    }

    private void UpdateDesignScale()
    {
        if (root == null || designSpace == null)
        {
            return;
        }

        float rootWidth = root.resolvedStyle.width;
        float rootHeight = root.resolvedStyle.height;

        if (rootWidth <= 0f)
        {
            rootWidth = Screen.width;
        }

        if (rootHeight <= 0f)
        {
            rootHeight = Screen.height;
        }

        float scale = Mathf.Min(rootWidth / DesignWidth, rootHeight / DesignHeight);
        float left = (rootWidth - DesignWidth * scale) * 0.5f;
        float top = (rootHeight - DesignHeight * scale) * 0.5f;

        designSpace.style.left = left;
        designSpace.style.top = top;
        designSpace.transform.scale = new Vector3(scale, scale, 1f);
    }

    private string GetRoomName()
    {
        if (networkManager != null && networkManager._runner != null && networkManager._runner.SessionInfo != null)
        {
            string sessionName = networkManager._runner.SessionInfo.Name;
            if (!string.IsNullOrWhiteSpace(sessionName))
            {
                return sessionName;
            }
        }

        return "빛의 성채 원정대";
    }

    private int GetMaxPlayers()
    {
        if (networkManager != null && networkManager._runner != null && networkManager._runner.SessionInfo != null)
        {
            int maxPlayers = networkManager._runner.SessionInfo.MaxPlayers;
            if (maxPlayers > 0)
            {
                return Mathf.Clamp(maxPlayers, 2, SlotCount);
            }
        }

        return SlotCount;
    }

    private void ShowStatus(string message)
    {
        if (statusLabel != null)
        {
            statusLabel.text = message;
        }
    }

    private static int GetDisplayLevel(int index)
    {
        switch (index % SlotCount)
        {
            case 1:
                return 45;
            case 2:
                return 40;
            case 3:
                return 43;
            default:
                return 42;
        }
    }

    private static int GetPlayerSortKey(NetworkPlayer player)
    {
        if (player != null && player.Object != null)
        {
            return player.Object.InputAuthority.PlayerId;
        }

        return int.MaxValue;
    }

    private static void SetPortraitClass(VisualElement portrait, int visualIndex)
    {
        if (portrait == null)
        {
            return;
        }

        portrait.RemoveFromClassList("jl-player-portrait--0");
        portrait.RemoveFromClassList("jl-player-portrait--1");
        portrait.RemoveFromClassList("jl-player-portrait--2");
        portrait.RemoveFromClassList("jl-player-portrait--3");
        portrait.AddToClassList($"jl-player-portrait--{visualIndex % SlotCount}");
    }

    private static void SetDisplay(VisualElement element, bool visible)
    {
        if (element != null)
        {
            element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }

    private static void SetPickingMode(VisualElement element, PickingMode pickingMode)
    {
        if (element != null)
        {
            element.pickingMode = pickingMode;
        }
    }

    private static bool IsPrimaryPointer(PointerUpEvent evt)
    {
        return evt == null || evt.button == 0;
    }
}
