using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using Fusion;
using GameCore.Enums;
using MDF.Runtime.Assets;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
public sealed class JoinLobbyUI : MonoBehaviour
{
    private const float DesignWidth = 1672f;
    private const float DesignHeight = 941f;
    private const int SlotCount = 4;
    private const int KingCount = 7;
    private const float MatchContentLoadTimeoutSeconds = 90f;

    public static JoinLobbyUI Instance { get; private set; }

    [Header("UI Toolkit")]
    [SerializeField] private UIDocument document;

    [Header("Scene")]
    [SerializeField] private string matchingLobbySceneName = SceneDefine.TestMatching;
    [SerializeField] private string gameScenePath = "Assets/Scenes/03_Game.unity";

    [Header("Preview")]
    [SerializeField] private bool useMockPlayersWhenNoPlayers;

    private readonly SlotView[] slots = new SlotView[SlotCount];
    private readonly KingCardView[] kingCards = new KingCardView[KingCount];
    private readonly MapThemeCardView[] mapThemeCards =
        new MapThemeCardView[MapThemeCatalog.Entries.Count];
    private readonly AddressableAssetLease<Sprite>[] kingIconLeases =
        new AddressableAssetLease<Sprite>[KingCount];
    private readonly Dictionary<int, Sprite> kingSprites = new Dictionary<int, Sprite>();

    private VisualElement root;
    private VisualElement designSpace;
    private VisualElement gameStartButton;
    private VisualElement readyButton;
    private VisualElement leaveRoomButton;
    private VisualElement networkBlockOverlay;
    private Label networkBlockLabel;

    private Label roomNameLabel;
    private Label playerCountLabel;
    private Label readyButtonLabel;
    private Label statusLabel;

    private NetworkManager networkManager;
    private NetworkPlayer localPlayer;
    private bool callbacksRegistered;
    private int kingIconLoadVersion;
    private bool matchStartInProgress;
    private int activeMatchLoadRevision;
    private CancellationTokenSource matchStartCancellation;
    private float nextOrphanedMatchLoadCheckRealtime;

    public bool IsMatchStartInProgress => matchStartInProgress;
    public int ActiveMatchLoadRevision => activeMatchLoadRevision;

    private struct PlayerViewData
    {
        public readonly string Name;
        public readonly int Level;
        public readonly bool IsReady;
        public readonly int VisualIndex;
        public readonly int KingKeyHash;
        public readonly int MapThemeId;
        public readonly bool IsEmpty;

        public PlayerViewData(
            string name,
            int level,
            bool isReady,
            int visualIndex,
            int kingKeyHash,
            int mapThemeId,
            bool isEmpty = false)
        {
            Name = name;
            Level = level;
            IsReady = isReady;
            VisualIndex = visualIndex;
            KingKeyHash = kingKeyHash;
            MapThemeId = mapThemeId;
            IsEmpty = isEmpty;
        }
    }

    private sealed class SlotView
    {
        public VisualElement Root;
        public VisualElement Portrait;
        public Label Name;
        public Label Level;
        public Label King;
        public Label Theme;
        public Label Ready;
    }

    private sealed class KingCardView
    {
        public VisualElement Root;
        public VisualElement Portrait;
        public Label Name;
        public Label Selection;
    }

    private sealed class MapThemeCardView
    {
        public VisualElement Root;
        public Label Name;
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
        BeginLoadKingIcons();
        RefreshNetworkBlockOverlay();
        UpdateDesignScale();
    }

    private void OnDisable()
    {
        if (matchStartInProgress && activeMatchLoadRevision > 0)
        {
            ResolveNetworkManager();
            if (networkManager != null
                && networkManager._runner != null
                && networkManager._runner.IsRunning
                && networkManager._runner.IsServer)
            {
                List<NetworkPlayer> players = CollectActiveLobbyPlayers(out _, out _);
                foreach (NetworkPlayer player in players)
                {
                    player?.ResetMatchContentLoadingAuthority(activeMatchLoadRevision);
                }
            }
        }

        matchStartInProgress = false;
        activeMatchLoadRevision = 0;
        matchStartCancellation?.Cancel();
        matchStartCancellation?.Dispose();
        matchStartCancellation = null;
        UnregisterCallbacks();
        UnsubscribeNetworkEvents();
        ReleaseKingIcons();
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
        networkBlockLabel = networkBlockOverlay?.Q<Label>(className: "jl-network-block-label");

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
                King = Query<Label>($"slotKing{i}"),
                Theme = Query<Label>($"slotTheme{i}"),
                Ready = Query<Label>($"slotReady{i}")
            };
        }

        for (int i = 0; i < kingCards.Length; i++)
        {
            kingCards[i] = new KingCardView
            {
                Root = Query<VisualElement>($"kingCard{i}"),
                Portrait = Query<VisualElement>($"kingPortrait{i}"),
                Name = Query<Label>($"kingName{i}"),
                Selection = Query<Label>($"kingSelection{i}")
            };

            if (kingCards[i].Root != null)
            {
                kingCards[i].Root.userData = i;
            }
        }

        for (int i = 0; i < mapThemeCards.Length; i++)
        {
            mapThemeCards[i] = new MapThemeCardView
            {
                Root = Query<VisualElement>($"mapThemeCard{i}"),
                Name = Query<Label>($"mapThemeName{i}")
            };

            if (mapThemeCards[i].Root != null)
            {
                mapThemeCards[i].Root.userData = i;
            }
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
        for (int i = 0; i < kingCards.Length; i++)
        {
            SetPickingMode(kingCards[i]?.Root, PickingMode.Position);
        }
        for (int i = 0; i < mapThemeCards.Length; i++)
        {
            SetPickingMode(mapThemeCards[i]?.Root, PickingMode.Position);
        }
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
        for (int i = 0; i < kingCards.Length; i++)
        {
            kingCards[i]?.Root?.RegisterCallback<PointerUpEvent>(OnKingCardPointerUp);
        }
        for (int i = 0; i < mapThemeCards.Length; i++)
        {
            mapThemeCards[i]?.Root?.RegisterCallback<PointerUpEvent>(OnMapThemeCardPointerUp);
        }

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
        for (int i = 0; i < kingCards.Length; i++)
        {
            kingCards[i]?.Root?.UnregisterCallback<PointerUpEvent>(OnKingCardPointerUp);
        }
        for (int i = 0; i < mapThemeCards.Length; i++)
        {
            mapThemeCards[i]?.Root?.UnregisterCallback<PointerUpEvent>(OnMapThemeCardPointerUp);
        }

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

        RequestMatchStart();
        evt.StopPropagation();
    }

    private void Update()
    {
        if (matchStartInProgress || Time.realtimeSinceStartup < nextOrphanedMatchLoadCheckRealtime)
        {
            return;
        }

        nextOrphanedMatchLoadCheckRealtime = Time.realtimeSinceStartup + 0.25f;
        ResetOrphanedAuthorityMatchLoad();
    }

    private void ResetOrphanedAuthorityMatchLoad()
    {
        ResolveNetworkManager();
        NetworkRunner runner = networkManager != null ? networkManager._runner : null;
        if (runner == null || !runner.IsRunning || !runner.IsServer)
        {
            return;
        }

        List<NetworkPlayer> players = CollectActiveLobbyPlayers(out _, out _);
        int orphanedRevision = ResolveActiveMatchLoadingRevision(players);
        if (orphanedRevision <= 0)
        {
            return;
        }

        foreach (NetworkPlayer player in players)
        {
            player?.ResetMatchContentLoadingAuthority(orphanedRevision);
        }

        Debug.LogWarning(
            $"[MatchPrewarm] Reset orphaned lobby loading state after authority/lifecycle change. " +
            $"revision={orphanedRevision}");
        MPTestLogger.Log(
            "match_prewarm_gate",
            "reset",
            "orphaned_authority_gate",
            "stale lobby loading state reset after authority/lifecycle change",
            new Dictionary<string, object> { { "revision", orphanedRevision } });
        UpdatePlayerList();
    }

    /// <summary>
    /// Production and DEVELOPMENT_BUILD automation entry point for the exact same lobby gate.
    /// The host-side validation and peer ACK collection remain inside TryStartGameAsync.
    /// </summary>
    public bool RequestMatchStart()
    {
        return TryStartGame();
    }

    private bool TryStartGame()
    {
        ResolveNetworkManager();
        List<NetworkPlayer> players = CollectActiveLobbyPlayers(
            out int activePlayerCount,
            out bool hasDuplicateAuthorities);
        bool valid = !matchStartInProgress
                     && isActiveAndEnabled
                     && networkManager != null
                     && networkManager._runner != null
                     && networkManager._runner.IsRunning
                     && networkManager._runner.IsServer
                      && IsLobbyRosterComplete(activePlayerCount, players.Count, hasDuplicateAuthorities)
                      && players.Count > 0
                      && players.All(player => KingSelectionCatalog.IsAllowedHash(player.SelectedKingUnitKeyHash))
                      && players.All(player => player.IsReady)
                     && SceneUtility.GetBuildIndexByScenePath(gameScenePath) >= 0;
        if (!valid)
        {
            UpdatePlayerList();
            return false;
        }

        TryStartGameAsync().Forget();
        return true;
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

    private void OnKingCardPointerUp(PointerUpEvent evt)
    {
        if (!IsPrimaryPointer(evt)
            || !(evt.currentTarget is VisualElement card)
            || !(card.userData is int catalogIndex)
            || catalogIndex < 0
            || catalogIndex >= KingSelectionCatalog.Entries.Count)
        {
            return;
        }

        SelectKing(KingSelectionCatalog.Entries[catalogIndex].KeyHash);
        evt.StopPropagation();
    }

    private void OnMapThemeCardPointerUp(PointerUpEvent evt)
    {
        if (!IsPrimaryPointer(evt)
            || !(evt.currentTarget is VisualElement card)
            || !(card.userData is int catalogIndex)
            || catalogIndex < 0
            || catalogIndex >= MapThemeCatalog.Entries.Count)
        {
            return;
        }

        SelectMapTheme((int)MapThemeCatalog.Entries[catalogIndex].Id);
        evt.StopPropagation();
    }

    public void UpdatePlayerList()
    {
        if (this == null)
        {
            return;
        }

        ResolveNetworkManager();

        List<NetworkPlayer> players = CollectActiveLobbyPlayers(
            out int activePlayerCount,
            out bool hasDuplicateAuthorities);

        localPlayer = players.FirstOrDefault(player => player.HasInputAuthority);

        bool usingMockPlayers = useMockPlayersWhenNoPlayers && players.Count == 0;
        List<PlayerViewData> viewData = usingMockPlayers
            ? BuildMockPlayers()
            : BuildPlayerViewData(players);

        RenderSlots(viewData);
        RenderKingSelection();
        RenderMapThemeSelection();
        UpdateRoomInfo(activePlayerCount, usingMockPlayers);
        UpdateButtons(players, activePlayerCount, hasDuplicateAuthorities, usingMockPlayers);
        RefreshNetworkBlockOverlay();
    }

    private List<NetworkPlayer> CollectActiveLobbyPlayers(
        out int activePlayerCount,
        out bool hasDuplicateAuthorities)
    {
        activePlayerCount = 0;
        hasDuplicateAuthorities = false;
        NetworkRunner runner = networkManager != null ? networkManager._runner : null;
        if (runner == null || !runner.IsRunning)
        {
            return new List<NetworkPlayer>();
        }

        List<PlayerRef> activePlayers = runner.ActivePlayers.ToList();
        activePlayerCount = activePlayers.Count;
        var activeSet = new HashSet<PlayerRef>(activePlayers);
        var playerByAuthority = new Dictionary<PlayerRef, NetworkPlayer>();

        foreach (NetworkPlayer player in FindObjectsOfType<NetworkPlayer>())
        {
            if (player == null
                || player.Runner != runner
                || player.Object == null
                || !player.Object.IsValid)
            {
                continue;
            }

            PlayerRef inputAuthority = player.Object.InputAuthority;
            if (inputAuthority == PlayerRef.None || !activeSet.Contains(inputAuthority))
            {
                continue;
            }

            // A reconnect can briefly leave two scene objects visible. Render one row,
            // but flag the roster incomplete so duplicates can never satisfy ready/start.
            if (!playerByAuthority.ContainsKey(inputAuthority))
            {
                playerByAuthority.Add(inputAuthority, player);
            }
            else
            {
                hasDuplicateAuthorities = true;
            }
        }

        return playerByAuthority.Values
            .OrderBy(GetPlayerSortKey)
            .ToList();
    }

    public static bool IsLobbyRosterComplete(
        int activePlayerCount,
        int rosterPlayerCount,
        bool hasDuplicateAuthorities = false)
    {
        return !hasDuplicateAuthorities
            && activePlayerCount > 0
            && rosterPlayerCount == activePlayerCount;
    }

    private static List<PlayerViewData> BuildMockPlayers()
    {
        return new List<PlayerViewData>
        {
            new PlayerViewData("루나", 42, true, 0, KingSelectionCatalog.Entries[0].KeyHash, (int)MapThemeId.Arena),
            new PlayerViewData("카인", 45, true, 1, KingSelectionCatalog.Entries[3].KeyHash, (int)MapThemeId.Classic),
            new PlayerViewData("미르", 40, true, 2, KingSelectionCatalog.Entries[4].KeyHash, (int)MapThemeId.Arena)
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
                i,
                player.SelectedKingUnitKeyHash,
                player.SelectedMapThemeId));
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

    private void RenderOccupiedSlot(SlotView slot, PlayerViewData player)
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
        SetKingPortrait(slot.Portrait, player.KingKeyHash, player.VisualIndex);

        if (slot.Name != null)
        {
            slot.Name.text = player.Name;
        }

        if (slot.Level != null)
        {
            slot.Level.text = $"Lv. {player.Level}";
        }

        if (slot.King != null)
        {
            slot.King.text = KingSelectionCatalog.TryGetByHash(player.KingKeyHash, out KingSelectionCatalog.Entry king)
                ? $"국왕 {king.DisplayName}"
                : "국왕 미선택";
        }

        if (slot.Theme != null)
        {
            int themeId = MapThemeCatalog.NormalizeOrDefault(player.MapThemeId);
            slot.Theme.text = MapThemeCatalog.TryGet(themeId, out MapThemeCatalog.Entry theme)
                ? $"맵 {theme.DisplayName}"
                : string.Empty;
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
            slot.Name.text = string.Empty;
        }

        if (slot.Level != null)
        {
            slot.Level.text = string.Empty;
        }

        if (slot.King != null)
        {
            slot.King.text = string.Empty;
        }

        if (slot.Theme != null)
        {
            slot.Theme.text = string.Empty;
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

    private void UpdateButtons(
        List<NetworkPlayer> players,
        int activePlayerCount,
        bool hasDuplicateAuthorities,
        bool usingMockPlayers)
    {
        bool hasNetworkRunner = networkManager != null && networkManager._runner != null;
        bool isHost = hasNetworkRunner && networkManager._runner.IsServer;
        bool rosterComplete = !usingMockPlayers
            && IsLobbyRosterComplete(activePlayerCount, players.Count, hasDuplicateAuthorities);
        bool hasRealPlayers = rosterComplete && players.Count > 0;
        bool allKingsSelected = hasRealPlayers
            && players.All(player => KingSelectionCatalog.IsAllowedHash(player.SelectedKingUnitKeyHash));
        bool allReady = allKingsSelected && players.All(player => player.IsReady);
        bool canStart = isHost && allReady && !matchStartInProgress && !IsMatchLoadingActive(players);
        bool localKingSelected = localPlayer != null
            && KingSelectionCatalog.IsAllowedHash(localPlayer.SelectedKingUnitKeyHash);

        gameStartButton?.EnableInClassList("jl-button--disabled", !canStart);
        readyButton?.EnableInClassList("jl-button--disabled", !localKingSelected);

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
            statusLabel.text = localKingSelected
                ? "ⓘ 방장이 게임을 시작할 수 있습니다."
                : "ⓘ 국왕을 선택해야 준비할 수 있습니다.";
        }
        else if (!rosterComplete)
        {
            statusLabel.text = "플레이어 로스터 동기화를 기다리고 있습니다.";
        }
        else if (!allKingsSelected)
        {
            statusLabel.text = "ⓘ 모든 인원이 국왕을 선택해야 준비할 수 있습니다.";
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

    private async UniTaskVoid TryStartGameAsync()
    {
        if (matchStartInProgress)
        {
            return;
        }

        ResolveNetworkManager();
        List<NetworkPlayer> players = CollectActiveLobbyPlayers(
            out int activePlayerCount,
            out bool hasDuplicateAuthorities);

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

        if (!IsLobbyRosterComplete(activePlayerCount, players.Count, hasDuplicateAuthorities))
        {
            ShowStatus("플레이어 로스터 동기화를 기다리고 있습니다.");
            return;
        }

        if (players.Count == 0
            || !players.All(player => KingSelectionCatalog.IsAllowedHash(player.SelectedKingUnitKeyHash)))
        {
            ShowStatus("ⓘ 모든 인원이 국왕을 선택해야 게임을 시작할 수 있습니다.");
            return;
        }

        if (!players.All(player => player.IsReady))
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

        NetworkRunner runner = networkManager._runner;
        PlayerRef[] expectedAuthorities = players
            .Select(player => player.Object.InputAuthority)
            .OrderBy(player => player.PlayerId)
            .ToArray();
        int revision = LobbyMatchLoadPolicy.NextRevision(
            players.Select(player => player.MatchContentLoadRevision));

        matchStartInProgress = true;
        activeMatchLoadRevision = revision;
        matchStartCancellation?.Cancel();
        matchStartCancellation?.Dispose();
        var attemptCancellation = new CancellationTokenSource();
        matchStartCancellation = attemptCancellation;

        try
        {
            Debug.Log(
                $"[MatchPrewarm] Authority gate started. revision={revision}, peers={expectedAuthorities.Length}");
            MPTestLogger.Log(
                "match_prewarm_gate",
                "begin",
                null,
                "authority waiting for active peer readiness",
                new Dictionary<string, object>
                {
                    { "revision", revision },
                    { "expectedPeers", expectedAuthorities.Length }
                });

            bool beganAll = players.All(player => player.BeginMatchContentLoadingAuthority(revision));
            if (!beganAll)
            {
                FailMatchStart(players, revision, "전투 데이터 준비를 시작하지 못했습니다. 다시 시도해 주세요.");
                return;
            }

            UpdatePlayerList();
            int[] expectedAuthorityIds = expectedAuthorities
                .Select(authority => authority.PlayerId)
                .ToArray();
            float startedAt = Time.realtimeSinceStartup;
            while (!attemptCancellation.IsCancellationRequested
                   && Time.realtimeSinceStartup - startedAt < MatchContentLoadTimeoutSeconds)
            {
                if (runner == null || !runner.IsRunning || !runner.IsServer)
                {
                    FailMatchStart(players, revision, "방 연결이 변경되어 게임 시작을 중단했습니다.");
                    return;
                }

                List<NetworkPlayer> currentPlayers = CollectActiveLobbyPlayers(
                    out int currentActiveCount,
                    out bool currentDuplicates);
                PlayerRef[] currentAuthorities = currentPlayers
                    .Select(player => player.Object.InputAuthority)
                    .OrderBy(player => player.PlayerId)
                    .ToArray();
                int[] currentAuthorityIds = currentAuthorities
                    .Select(authority => authority.PlayerId)
                    .ToArray();
                if (!IsLobbyRosterComplete(currentActiveCount, currentPlayers.Count, currentDuplicates)
                    || !LobbyMatchLoadPolicy.HasSameRoster(expectedAuthorityIds, currentAuthorityIds))
                {
                    FailMatchStart(currentPlayers, revision, "인원 구성이 변경되어 게임 시작을 중단했습니다.");
                    return;
                }

                if (currentPlayers.Any(player => player.MatchContentLoadRevision != revision))
                {
                    FailMatchStart(currentPlayers, revision, "전투 데이터 준비 상태가 변경되었습니다. 다시 시도해 주세요.");
                    return;
                }

                if (currentPlayers.Any(player =>
                        player.MatchContentLoadRevision == revision
                        && player.MatchContentLoadState == LobbyMatchLoadingState.Failed))
                {
                    FailMatchStart(currentPlayers, revision, "전투 데이터 준비에 실패한 인원이 있습니다. 다시 시도해 주세요.");
                    return;
                }

                if (currentPlayers.Any(player =>
                        !player.IsReady
                        || !KingSelectionCatalog.IsAllowedHash(player.SelectedKingUnitKeyHash)))
                {
                    FailMatchStart(
                        currentPlayers,
                        revision,
                        "준비 중 선택 정보가 변경되어 게임 시작을 중단했습니다.");
                    return;
                }

                LobbyMatchPeerLoadState[] peerLoadStates = currentPlayers
                    .OrderBy(player => player.Object.InputAuthority.PlayerId)
                    .Select(player => new LobbyMatchPeerLoadState(
                        player.Object.InputAuthority.PlayerId,
                        player.MatchContentLoadRevision,
                        player.MatchContentLoadState))
                    .ToArray();
                if (LobbyMatchLoadPolicy.CanLoadScene(
                        revision,
                        expectedAuthorityIds,
                        peerLoadStates))
                {
                    Debug.Log(
                        $"[MatchPrewarm] All active peer ACKs ready. revision={revision}, " +
                        $"peers={currentPlayers.Count}. Loading {SceneDefine.Game}.");
                    MPTestLogger.Pass(
                        "match_prewarm_gate",
                        "all active peers acknowledged; loading game scene",
                        new Dictionary<string, object>
                        {
                            { "revision", revision },
                            { "readyPeers", currentPlayers.Count }
                        });
                    runner.LoadScene(SceneRef.FromIndex(sceneIndex), LoadSceneMode.Single);
                    return;
                }

                UpdatePlayerList();
                await UniTask.Delay(
                    100,
                    DelayType.Realtime,
                    PlayerLoopTiming.Update,
                    attemptCancellation.Token);
            }

            if (!attemptCancellation.IsCancellationRequested)
            {
                List<NetworkPlayer> currentPlayers = CollectActiveLobbyPlayers(out _, out _);
                FailMatchStart(
                    currentPlayers,
                    revision,
                    "전투 데이터 준비 시간이 초과되었습니다. 연결을 확인한 뒤 다시 시도해 주세요.");
                Debug.LogError(
                    $"[MatchPrewarm] Authority gate timed out after {MatchContentLoadTimeoutSeconds:F0}s. " +
                    $"revision={revision}");
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when the lobby UI is disabled or the room is left.
        }
        catch (Exception exception)
        {
            if (matchStartInProgress && activeMatchLoadRevision == revision)
            {
                List<NetworkPlayer> currentPlayers = CollectActiveLobbyPlayers(out _, out _);
                FailMatchStart(
                    currentPlayers,
                    revision,
                    "전투 데이터 준비 중 오류가 발생했습니다. 다시 시도해 주세요.");
            }

            Debug.LogException(exception);
        }
        finally
        {
            DisposeMatchStartAttempt(attemptCancellation);
        }
    }

    private void FailMatchStart(
        IEnumerable<NetworkPlayer> players,
        int revision,
        string userMessage)
    {
        if (players != null)
        {
            foreach (NetworkPlayer player in players)
            {
                player?.ResetMatchContentLoadingAuthority(revision);
            }
        }

        matchStartInProgress = false;
        activeMatchLoadRevision = 0;
        MPTestLogger.Fail(
            "match_prewarm_gate",
            "match_content_not_ready",
            userMessage,
            new Dictionary<string, object> { { "revision", revision } });
        UpdatePlayerList();
        ShowStatus(userMessage);
    }

    private void DisposeMatchStartAttempt(CancellationTokenSource attemptCancellation)
    {
        if (ReferenceEquals(matchStartCancellation, attemptCancellation))
        {
            matchStartCancellation = null;
        }

        attemptCancellation?.Dispose();
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

        if (!KingSelectionCatalog.IsAllowedHash(localPlayer.SelectedKingUnitKeyHash))
        {
            ShowStatus("ⓘ 국왕을 먼저 선택해야 준비할 수 있습니다.");
            return;
        }

        localPlayer.RPC_ToggleReady();
    }

    private void SelectKing(int selectedKingHash)
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

        if (localPlayer.SelectedKingUnitKeyHash == selectedKingHash)
        {
            return;
        }

        if (!localPlayer.RequestKingSelection(selectedKingHash))
        {
            ShowStatus("ⓘ 선택할 수 없는 국왕입니다.");
            return;
        }

        RenderKingSelection(selectedKingHash);
        ShowStatus(localPlayer.IsReady
            ? "ⓘ 국왕을 변경했습니다. 준비 상태가 해제됩니다."
            : "ⓘ 국왕을 선택했습니다.");
    }

    private void RenderKingSelection(int? optimisticSelectionHash = null)
    {
        int selectedHash = optimisticSelectionHash
            ?? (localPlayer != null ? localPlayer.SelectedKingUnitKeyHash : KingSelectionCatalog.DefaultKeyHash);

        for (int i = 0; i < kingCards.Length; i++)
        {
            KingCardView card = kingCards[i];
            if (card == null || i >= KingSelectionCatalog.Entries.Count)
            {
                continue;
            }

            KingSelectionCatalog.Entry entry = KingSelectionCatalog.Entries[i];
            bool selected = entry.KeyHash == selectedHash;
            card.Root?.EnableInClassList("jl-king-card--selected", selected);

            if (card.Name != null)
            {
                card.Name.text = entry.DisplayName;
            }

            if (card.Selection != null)
            {
                card.Selection.text = selected ? "선택됨" : "선택";
            }

            SetBackgroundSprite(card.Portrait,
                kingSprites.TryGetValue(entry.KeyHash, out Sprite sprite) ? sprite : null);
        }
    }

    private void SelectMapTheme(int selectedThemeId)
    {
        if (localPlayer == null)
        {
            UpdatePlayerList();
        }

        if (localPlayer == null)
        {
            ShowStatus("로컬 플레이어 정보를 아직 찾을 수 없습니다.");
            return;
        }

        int themeId = MapThemeCatalog.NormalizeOrDefault(selectedThemeId);
        if (localPlayer.SelectedMapThemeId == themeId)
        {
            return;
        }

        if (!localPlayer.RequestMapThemeSelection(themeId))
        {
            ShowStatus("선택할 수 없는 맵 테마입니다.");
            return;
        }

        RenderMapThemeSelection(themeId);
        ShowStatus(localPlayer.IsReady
            ? "내 필드 테마를 변경했습니다. 준비 상태가 해제됩니다."
            : "내 필드 테마를 선택했습니다.");
    }

    private void RenderMapThemeSelection(int? optimisticThemeId = null)
    {
        int selectedThemeId = MapThemeCatalog.NormalizeOrDefault(
            optimisticThemeId ?? (localPlayer != null
                ? localPlayer.SelectedMapThemeId
                : MapThemeCatalog.DefaultId));

        for (int i = 0; i < mapThemeCards.Length; i++)
        {
            MapThemeCardView card = mapThemeCards[i];
            if (card == null || i >= MapThemeCatalog.Entries.Count)
            {
                continue;
            }

            MapThemeCatalog.Entry entry = MapThemeCatalog.Entries[i];
            bool selected = (int)entry.Id == selectedThemeId;
            card.Root?.EnableInClassList("jl-map-theme-card--selected", selected);
            if (card.Name != null)
            {
                card.Name.text = selected ? $"{entry.DisplayName} · 선택됨" : entry.DisplayName;
            }
        }
    }

    private void SetKingPortrait(VisualElement portrait, int kingKeyHash, int fallbackVisualIndex)
    {
        if (portrait == null)
        {
            return;
        }

        if (kingSprites.TryGetValue(kingKeyHash, out Sprite sprite) && sprite != null)
        {
            RemovePortraitClasses(portrait);
            SetBackgroundSprite(portrait, sprite);
            return;
        }

        SetBackgroundSprite(portrait, null);
        SetPortraitClass(portrait, fallbackVisualIndex);
    }

    private void BeginLoadKingIcons()
    {
        int version = ++kingIconLoadVersion;
        for (int i = 0; i < kingCards.Length && i < KingSelectionCatalog.Entries.Count; i++)
        {
            LoadKingIconAsync(i, version).Forget();
        }
    }

    private async UniTask LoadKingIconAsync(int catalogIndex, int version)
    {
        KingSelectionCatalog.Entry entry = KingSelectionCatalog.Entries[catalogIndex];
        AddressableAssetLease<Sprite> lease = await AssetLoader.AcquireAssetAsync<Sprite>(entry.IconKey);
        if (this == null || !isActiveAndEnabled || version != kingIconLoadVersion)
        {
            lease?.Dispose();
            return;
        }

        kingIconLeases[catalogIndex]?.Dispose();
        kingIconLeases[catalogIndex] = lease;
        if (lease?.Asset != null)
        {
            kingSprites[entry.KeyHash] = lease.Asset;
        }

        UpdatePlayerList();
    }

    private void ReleaseKingIcons()
    {
        kingIconLoadVersion++;
        kingSprites.Clear();
        for (int i = 0; i < kingIconLeases.Length; i++)
        {
            kingIconLeases[i]?.Dispose();
            kingIconLeases[i] = null;
        }
    }

    private void RefreshNetworkBlockOverlay()
    {
        ResolveNetworkManager();
        List<NetworkPlayer> players = CollectActiveLobbyPlayers(out _, out _);
        int matchLoadRevision = ResolveActiveMatchLoadingRevision(players);
        bool matchLoading = matchStartInProgress || matchLoadRevision > 0;
        bool shouldBlock = (networkManager != null && networkManager.IsNetworkUiBlocked) || matchLoading;
        if (networkBlockLabel != null)
        {
            if (matchLoading)
            {
                int revision = activeMatchLoadRevision > 0 ? activeMatchLoadRevision : matchLoadRevision;
                int readyCount = players.Count(player => player.IsMatchContentReadyFor(revision));
                networkBlockLabel.text = $"전투 데이터를 준비하고 있습니다... ({readyCount}/{players.Count})";
            }
            else
            {
                networkBlockLabel.text = "네트워크 처리 중입니다...";
            }
        }

        SetDisplay(networkBlockOverlay, shouldBlock);
        SetControlsEnabled(!shouldBlock);
    }

    private static bool IsMatchLoadingActive(IEnumerable<NetworkPlayer> players)
    {
        return ResolveActiveMatchLoadingRevision(players) > 0;
    }

    private static int ResolveActiveMatchLoadingRevision(IEnumerable<NetworkPlayer> players)
    {
        if (players == null)
        {
            return 0;
        }

        return players
            .Where(player => player != null
                             && player.MatchContentLoadRevision > 0
                             && player.MatchContentLoadState != LobbyMatchLoadingState.Idle)
            .Select(player => player.MatchContentLoadRevision)
            .DefaultIfEmpty(0)
            .Max();
    }

    private void SetControlsEnabled(bool enabled)
    {
        gameStartButton?.SetEnabled(enabled);
        readyButton?.SetEnabled(enabled);
        leaveRoomButton?.SetEnabled(enabled);
        for (int i = 0; i < kingCards.Length; i++)
        {
            kingCards[i]?.Root?.SetEnabled(enabled);
        }
        for (int i = 0; i < mapThemeCards.Length; i++)
        {
            mapThemeCards[i]?.Root?.SetEnabled(enabled);
        }
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

        RemovePortraitClasses(portrait);
        portrait.AddToClassList($"jl-player-portrait--{visualIndex % SlotCount}");
    }

    private static void RemovePortraitClasses(VisualElement portrait)
    {
        portrait.RemoveFromClassList("jl-player-portrait--0");
        portrait.RemoveFromClassList("jl-player-portrait--1");
        portrait.RemoveFromClassList("jl-player-portrait--2");
        portrait.RemoveFromClassList("jl-player-portrait--3");
    }

    private static void SetBackgroundSprite(VisualElement element, Sprite sprite)
    {
        if (element != null)
        {
            element.style.backgroundImage = sprite != null
                ? new StyleBackground(sprite)
                : new StyleBackground(StyleKeyword.Null);
        }
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
