using System.Collections.Generic;
using Fusion;
using GameCore.Enums;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
public sealed class TestMatchingUIToolkitController : MonoBehaviour
{
    private const float DesignWidth = 1672f;
    private const float DesignHeight = 941f;

    [Header("UI Toolkit")]
    [SerializeField] private UIDocument document;

    [Header("Scene")]
    [SerializeField] private string titleSceneName = SceneDefine.Title;
    [SerializeField] private string joinLobbySceneName = SceneDefine.JoinLobby;

    [Header("Room Creation")]
    [SerializeField] private bool createRoomUsesInputText = true;

    private VisualElement root;
    private VisualElement designSpace;
    private VisualElement roomListContent;
    private VisualElement emptyState;
    private VisualElement refreshButton;
    private VisualElement createRoomButton;
    private VisualElement backToTitleButton;
    private VisualElement directJoinButton;
    private VisualElement roomNameInputWrap;
    private VisualElement roomNotFoundModal;
    private VisualElement roomNotFoundCloseButton;
    private VisualElement networkBlockOverlay;

    private TextField roomNameField;
    private Label roomNamePlaceholder;
    private Label statusLabel;
    private Label roomNotFoundMessage;

    private NetworkManager networkManager;
    private bool callbacksRegistered;

    private enum RoomStatus
    {
        Waiting,
        Playing,
        Full,
        Unavailable
    }

    private enum StatusKind
    {
        Info,
        Success,
        Error
    }

    private struct RoomViewData
    {
        public readonly string RoomName;
        public readonly int CurrentPlayers;
        public readonly int MaxPlayers;
        public readonly string RuleText;
        public readonly RoomStatus Status;
        public readonly int VisualIndex;
        public readonly bool CanJoin;

        public RoomViewData(
            string roomName,
            int currentPlayers,
            int maxPlayers,
            string ruleText,
            RoomStatus status,
            int visualIndex,
            bool canJoin)
        {
            RoomName = roomName;
            CurrentPlayers = currentPlayers;
            MaxPlayers = maxPlayers;
            RuleText = ruleText;
            Status = status;
            VisualIndex = visualIndex;
            CanJoin = canJoin;
        }
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
    }

    private void OnEnable()
    {
        if (document == null || document.rootVisualElement == null)
        {
            Debug.LogError("[TestMatchingUIToolkitController] UIDocument is missing.");
            return;
        }

        root = document.rootVisualElement;
        BindElements();
        ConfigureInitialState();
        RegisterCallbacks();
        SubscribeNetworkEvents();
        ResolveNetworkManager();
        EnsureLobbyConnectionIfPossible();
        RefreshRoomList();
        RefreshNetworkBlockOverlay();
        UpdateDesignScale();
    }

    private void OnDisable()
    {
        UnregisterCallbacks();
        UnsubscribeNetworkEvents();
    }

    private void BindElements()
    {
        designSpace = Query<VisualElement>("testmatching-design-space");
        roomListContent = Query<VisualElement>("roomListContent");
        emptyState = Query<VisualElement>("emptyState");
        refreshButton = Query<VisualElement>("refreshButton");
        createRoomButton = Query<VisualElement>("createRoomButton");
        backToTitleButton = Query<VisualElement>("backToTitleButton");
        directJoinButton = Query<VisualElement>("directJoinButton");
        roomNameInputWrap = Query<VisualElement>("roomNameInputWrap");
        roomNotFoundModal = Query<VisualElement>("roomNotFoundModal");
        roomNotFoundCloseButton = Query<VisualElement>("roomNotFoundCloseButton");
        networkBlockOverlay = Query<VisualElement>("networkBlockOverlay");

        roomNameField = Query<TextField>("roomNameField");
        roomNamePlaceholder = Query<Label>("roomNamePlaceholder");
        statusLabel = Query<Label>("statusLabel");
        roomNotFoundMessage = Query<Label>("roomNotFoundMessage");
    }

    private T Query<T>(string elementName) where T : VisualElement
    {
        T element = root.Q<T>(elementName);
        if (element == null)
        {
            Debug.LogError($"[TestMatchingUIToolkitController] UXML element not found: {elementName}");
        }

        return element;
    }

    private void ConfigureInitialState()
    {
        if (roomNameField != null)
        {
            roomNameField.SetValueWithoutNotify(string.Empty);
        }

        SetDisplay(roomNotFoundModal, false);
        SetDisplay(networkBlockOverlay, false);
        ConfigurePicking();
        SetInputError(false);
        UpdatePlaceholder();
        HideStatus();
    }

    private void ConfigurePicking()
    {
        SetPickingMode(refreshButton, PickingMode.Position);
        SetPickingMode(createRoomButton, PickingMode.Position);
        SetPickingMode(backToTitleButton, PickingMode.Position);
        SetPickingMode(directJoinButton, PickingMode.Position);
        SetPickingMode(roomNameInputWrap, PickingMode.Position);
        SetPickingMode(roomNamePlaceholder, PickingMode.Ignore);
    }

    private void RegisterCallbacks()
    {
        if (callbacksRegistered)
        {
            return;
        }

        root?.RegisterCallback<GeometryChangedEvent>(OnRootGeometryChanged);
        refreshButton?.RegisterCallback<PointerUpEvent>(OnRefreshPointerUp);
        createRoomButton?.RegisterCallback<PointerUpEvent>(OnCreateRoomPointerUp);
        backToTitleButton?.RegisterCallback<PointerUpEvent>(OnBackToTitlePointerUp);
        directJoinButton?.RegisterCallback<PointerUpEvent>(OnDirectJoinPointerUp);
        roomNameInputWrap?.RegisterCallback<PointerDownEvent>(OnRoomNameInputPointerDown);
        roomNotFoundCloseButton?.RegisterCallback<PointerUpEvent>(OnRoomNotFoundClosePointerUp);

        roomNameField?.RegisterValueChangedCallback(OnRoomNameChanged);
        roomNameField?.RegisterCallback<KeyDownEvent>(OnRoomNameKeyDown);

        callbacksRegistered = true;
    }

    private void UnregisterCallbacks()
    {
        if (!callbacksRegistered)
        {
            return;
        }

        root?.UnregisterCallback<GeometryChangedEvent>(OnRootGeometryChanged);
        refreshButton?.UnregisterCallback<PointerUpEvent>(OnRefreshPointerUp);
        createRoomButton?.UnregisterCallback<PointerUpEvent>(OnCreateRoomPointerUp);
        backToTitleButton?.UnregisterCallback<PointerUpEvent>(OnBackToTitlePointerUp);
        directJoinButton?.UnregisterCallback<PointerUpEvent>(OnDirectJoinPointerUp);
        roomNameInputWrap?.UnregisterCallback<PointerDownEvent>(OnRoomNameInputPointerDown);
        roomNotFoundCloseButton?.UnregisterCallback<PointerUpEvent>(OnRoomNotFoundClosePointerUp);

        roomNameField?.UnregisterValueChangedCallback(OnRoomNameChanged);
        roomNameField?.UnregisterCallback<KeyDownEvent>(OnRoomNameKeyDown);

        callbacksRegistered = false;
    }

    private void SubscribeNetworkEvents()
    {
        NetworkManager.OnSessionListUpdatedEvent += OnSessionListUpdated;
        NetworkManager.OnNetworkUiBlockChanged += OnNetworkUiBlockChanged;
    }

    private void UnsubscribeNetworkEvents()
    {
        NetworkManager.OnSessionListUpdatedEvent -= OnSessionListUpdated;
        NetworkManager.OnNetworkUiBlockChanged -= OnNetworkUiBlockChanged;
    }

    private void ResolveNetworkManager()
    {
        networkManager = NetworkManager.Instance;
        if (networkManager == null)
        {
            networkManager = FindObjectOfType<NetworkManager>();
        }
    }

    private void EnsureLobbyConnectionIfPossible()
    {
        if (networkManager == null)
        {
            ShowStatus("NetworkManager가 없어 방 목록을 불러올 수 없습니다.", StatusKind.Info);
            return;
        }

        if (networkManager.State == ConnectionState.Disconnected)
        {
            ShowStatus("로비에 연결 중입니다.", StatusKind.Info);
            networkManager.JoinLobby();
        }
    }

    private void OnSessionListUpdated(List<SessionInfo> sessionList)
    {
        RefreshRoomList();
    }

    private void OnNetworkUiBlockChanged()
    {
        RefreshNetworkBlockOverlay();
    }

    private void OnRootGeometryChanged(GeometryChangedEvent evt)
    {
        UpdateDesignScale();
    }

    private void OnRefreshPointerUp(PointerUpEvent evt)
    {
        if (!IsPrimaryPointer(evt))
        {
            return;
        }

        ResolveNetworkManager();
        RefreshRoomList();
        RefreshNetworkBlockOverlay();
        ShowStatus("방 목록을 갱신했습니다.", StatusKind.Success);
        evt.StopPropagation();
    }

    private void OnCreateRoomPointerUp(PointerUpEvent evt)
    {
        if (!IsPrimaryPointer(evt))
        {
            return;
        }

        SetInputError(false);

        if (!TryPrepareNetworkAction("방을 만들려면 로비 연결이 필요합니다."))
        {
            evt.StopPropagation();
            return;
        }

        string roomName = createRoomUsesInputText ? GetTrimmedRoomName() : string.Empty;
        if (string.IsNullOrWhiteSpace(roomName))
        {
            roomName = NetworkDefine.DefaultRoomName;
        }

        ShowStatus($"'{roomName}' 방을 생성 중입니다.", StatusKind.Info);
        networkManager.StartGame(GameMode.Host, roomName, joinLobbySceneName);
        evt.StopPropagation();
    }

    private void OnBackToTitlePointerUp(PointerUpEvent evt)
    {
        if (!IsPrimaryPointer(evt))
        {
            return;
        }

        ResolveNetworkManager();

        if (networkManager != null)
        {
            networkManager.LeaveAndLoad(titleSceneName);
        }
        else if (!string.IsNullOrWhiteSpace(titleSceneName))
        {
            SceneManager.LoadScene(titleSceneName);
        }

        evt.StopPropagation();
    }

    private void OnDirectJoinPointerUp(PointerUpEvent evt)
    {
        if (!IsPrimaryPointer(evt))
        {
            return;
        }

        TryDirectJoin();
        evt.StopPropagation();
    }

    private void OnRoomNameInputPointerDown(PointerDownEvent evt)
    {
        roomNameField?.Focus();
    }

    private void OnRoomNotFoundClosePointerUp(PointerUpEvent evt)
    {
        if (!IsPrimaryPointer(evt))
        {
            return;
        }

        SetDisplay(roomNotFoundModal, false);
        evt.StopPropagation();
    }

    private void OnRoomNameChanged(ChangeEvent<string> evt)
    {
        SetInputError(false);
        UpdatePlaceholder();
        HideStatus();
    }

    private void OnRoomNameKeyDown(KeyDownEvent evt)
    {
        if (evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.KeypadEnter)
        {
            return;
        }

        TryDirectJoin();
        evt.StopPropagation();
    }

    private void TryDirectJoin()
    {
        string roomName = GetTrimmedRoomName();
        if (string.IsNullOrWhiteSpace(roomName))
        {
            SetInputError(true);
            ShowStatus("입장할 방 이름을 입력해 주세요.", StatusKind.Error);
            return;
        }

        if (!TryPrepareNetworkAction("직접 입장하려면 로비 연결이 필요합니다."))
        {
            return;
        }

        SessionInfo targetSession = FindJoinableSession(roomName);
        if (targetSession == null)
        {
            ShowRoomNotFound(roomName);
            return;
        }

        ShowStatus($"'{targetSession.Name}' 방에 입장 중입니다.", StatusKind.Info);
        networkManager.StartGame(GameMode.Client, targetSession.Name, joinLobbySceneName);
    }

    private bool TryPrepareNetworkAction(string missingManagerMessage)
    {
        ResolveNetworkManager();

        if (networkManager == null)
        {
            ShowStatus(missingManagerMessage, StatusKind.Error);
            return false;
        }

        if (networkManager.State == ConnectionState.Disconnected)
        {
            networkManager.JoinLobby();
            ShowStatus("로비에 연결 중입니다. 잠시 후 다시 시도해 주세요.", StatusKind.Info);
            return false;
        }

        if (networkManager.State == ConnectionState.Connecting)
        {
            ShowStatus("로비 연결이 완료될 때까지 기다려 주세요.", StatusKind.Info);
            return false;
        }

        if (networkManager.IsNetworkUiBlocked)
        {
            ShowStatus("Network action is in progress. Please wait.", StatusKind.Info);
            return false;
        }

        if (networkManager.State != ConnectionState.InLobby)
        {
            ShowStatus("현재 상태에서는 방 작업을 할 수 없습니다.", StatusKind.Error);
            return false;
        }

        return true;
    }

    private SessionInfo FindJoinableSession(string roomName)
    {
        if (networkManager == null || networkManager._sessionList == null)
        {
            return null;
        }

        foreach (SessionInfo session in networkManager._sessionList)
        {
            if (session == null)
            {
                continue;
            }

            if (!session.IsOpen || !session.IsVisible || session.PlayerCount >= session.MaxPlayers)
            {
                continue;
            }

            if (string.Equals(session.Name, roomName, System.StringComparison.OrdinalIgnoreCase))
            {
                return session;
            }
        }

        return null;
    }

    private void RefreshRoomList()
    {
        ResolveNetworkManager();

        List<RoomViewData> rooms = BuildRealRoomViewData();
        RenderRooms(rooms);

        if (rooms.Count == 0)
        {
            ShowStatus("현재 생성된 방이 없습니다.", StatusKind.Info);
        }
        else
        {
            HideStatus();
        }
    }

    private List<RoomViewData> BuildRealRoomViewData()
    {
        List<RoomViewData> rooms = new List<RoomViewData>();
        if (networkManager == null || networkManager._sessionList == null)
        {
            return rooms;
        }

        int visualIndex = 0;
        foreach (SessionInfo session in networkManager._sessionList)
        {
            if (session == null || !session.IsVisible)
            {
                continue;
            }

            RoomStatus status = GetStatus(session);
            bool canJoin = session.IsOpen && session.IsVisible && session.PlayerCount < session.MaxPlayers;
            rooms.Add(new RoomViewData(
                session.Name,
                session.PlayerCount,
                session.MaxPlayers,
                GetDefaultRuleText(visualIndex),
                status,
                visualIndex,
                canJoin));

            visualIndex++;
        }

        return rooms;
    }

    private static RoomStatus GetStatus(SessionInfo session)
    {
        if (!session.IsOpen || !session.IsVisible)
        {
            return RoomStatus.Playing;
        }

        if (session.PlayerCount >= session.MaxPlayers)
        {
            return RoomStatus.Full;
        }

        return RoomStatus.Waiting;
    }

    private static string GetDefaultRuleText(int visualIndex)
    {
        switch (visualIndex % 4)
        {
            case 0:
                return "스토리 모드";
            case 1:
                return "던전 모드";
            case 2:
                return "도전 모드";
            default:
                return "자유 모드";
        }
    }

    private void RenderRooms(List<RoomViewData> rooms)
    {
        if (roomListContent == null)
        {
            return;
        }

        roomListContent.Clear();

        foreach (RoomViewData room in rooms)
        {
            VisualElement roomItem = CreateRoomItem();
            SetupRoomItem(roomItem, room);
            roomListContent.Add(roomItem);
        }

        SetDisplay(emptyState, rooms.Count == 0);
    }

    private VisualElement CreateRoomItem()
    {
        VisualElement card = new VisualElement { name = "room-card" };
        card.AddToClassList("tm-room-card");

        Label rankIcon = new Label { name = "rankIcon" };
        rankIcon.AddToClassList("tm-room-rank-icon");
        card.Add(rankIcon);

        VisualElement info = new VisualElement();
        info.AddToClassList("tm-room-info");
        Label roomName = new Label { name = "roomNameLabel" };
        roomName.AddToClassList("tm-room-name-label");
        info.Add(roomName);

        VisualElement meta = new VisualElement();
        meta.AddToClassList("tm-room-meta-row");
        Label peopleIcon = new Label("P");
        peopleIcon.AddToClassList("tm-room-meta-icon");
        Label playerCount = new Label { name = "playerCountLabel" };
        playerCount.AddToClassList("tm-room-meta-label");
        VisualElement divider = new VisualElement();
        divider.AddToClassList("tm-room-meta-divider");
        Label trophyIcon = new Label("W");
        trophyIcon.AddToClassList("tm-room-meta-icon");
        Label rule = new Label { name = "ruleLabel" };
        rule.AddToClassList("tm-room-meta-label");
        meta.Add(peopleIcon);
        meta.Add(playerCount);
        meta.Add(divider);
        meta.Add(trophyIcon);
        meta.Add(rule);
        info.Add(meta);
        card.Add(info);

        VisualElement thumbnail = new VisualElement { name = "roomThumbnail" };
        thumbnail.AddToClassList("tm-room-thumbnail");
        card.Add(thumbnail);

        VisualElement statusWrap = new VisualElement { name = "statusWrap" };
        statusWrap.AddToClassList("tm-room-status-wrap");
        VisualElement statusDot = new VisualElement { name = "statusDot" };
        statusDot.AddToClassList("tm-room-status-dot");
        Label statusLabelElement = new Label { name = "statusLabel" };
        statusLabelElement.AddToClassList("tm-room-status-label");
        statusWrap.Add(statusDot);
        statusWrap.Add(statusLabelElement);
        card.Add(statusWrap);

        return card;
    }

    private void SetupRoomItem(VisualElement itemRoot, RoomViewData room)
    {
        VisualElement card = GetRoomCard(itemRoot);
        if (card == null)
        {
            return;
        }

        card.userData = room;
        card.AddToClassList(GetRoomCardStatusClass(room.Status));
        if (room.VisualIndex == 1)
        {
            card.AddToClassList("tm-room-card--selected");
        }

        card.RegisterCallback<PointerUpEvent>(OnRoomCardPointerUp);

        Label rankIcon = itemRoot.Q<Label>("rankIcon");
        Label roomNameLabel = itemRoot.Q<Label>("roomNameLabel");
        Label playerCountLabel = itemRoot.Q<Label>("playerCountLabel");
        Label ruleLabel = itemRoot.Q<Label>("ruleLabel");
        Label statusLabelElement = itemRoot.Q<Label>("statusLabel");
        VisualElement statusWrap = itemRoot.Q<VisualElement>("statusWrap");
        VisualElement thumbnail = itemRoot.Q<VisualElement>("roomThumbnail");

        if (rankIcon != null)
        {
            rankIcon.text = GetRankIcon(room.VisualIndex);
            rankIcon.AddToClassList(GetRankIconClass(room.VisualIndex));
        }

        if (roomNameLabel != null)
        {
            roomNameLabel.text = room.RoomName;
        }

        if (playerCountLabel != null)
        {
            playerCountLabel.text = $"{room.CurrentPlayers} / {room.MaxPlayers}";
        }

        if (ruleLabel != null)
        {
            ruleLabel.text = room.RuleText;
        }

        if (statusLabelElement != null)
        {
            statusLabelElement.text = GetStatusText(room.Status);
        }

        if (statusWrap != null)
        {
            statusWrap.AddToClassList(GetRoomStatusClass(room.Status));
        }

        if (thumbnail != null)
        {
            thumbnail.AddToClassList(GetThumbnailClass(room.VisualIndex));
        }
    }

    private static VisualElement GetRoomCard(VisualElement itemRoot)
    {
        if (itemRoot == null)
        {
            return null;
        }

        if (itemRoot.name == "room-card")
        {
            return itemRoot;
        }

        return itemRoot.Q<VisualElement>("room-card");
    }

    private void OnRoomCardPointerUp(PointerUpEvent evt)
    {
        if (!IsPrimaryPointer(evt))
        {
            return;
        }

        VisualElement card = evt.currentTarget as VisualElement;
        if (card == null || !(card.userData is RoomViewData room))
        {
            return;
        }

        if (!room.CanJoin)
        {
            ShowStatus("현재 입장할 수 없는 방입니다.", StatusKind.Error);
            evt.StopPropagation();
            return;
        }

        if (!TryPrepareNetworkAction("방에 입장하려면 로비 연결이 필요합니다."))
        {
            evt.StopPropagation();
            return;
        }

        ShowStatus($"'{room.RoomName}' 방에 입장 중입니다.", StatusKind.Info);
        networkManager.StartGame(GameMode.Client, room.RoomName, joinLobbySceneName);
        evt.StopPropagation();
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
        refreshButton?.SetEnabled(enabled);
        createRoomButton?.SetEnabled(enabled);
        backToTitleButton?.SetEnabled(enabled);
        directJoinButton?.SetEnabled(enabled);
        roomNameField?.SetEnabled(enabled);
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

    private string GetTrimmedRoomName()
    {
        return roomNameField?.value?.Trim() ?? string.Empty;
    }

    private void UpdatePlaceholder()
    {
        SetDisplay(roomNamePlaceholder, string.IsNullOrEmpty(roomNameField?.value));
    }

    private void SetInputError(bool hasError)
    {
        if (roomNameInputWrap == null)
        {
            return;
        }

        if (hasError)
        {
            roomNameInputWrap.AddToClassList("tm-room-name-input-wrap--error");
        }
        else
        {
            roomNameInputWrap.RemoveFromClassList("tm-room-name-input-wrap--error");
        }
    }

    private void ShowRoomNotFound(string roomName)
    {
        if (roomNotFoundMessage != null)
        {
            roomNotFoundMessage.text = $"'{roomName}' 방을 찾을 수 없습니다.";
        }

        SetDisplay(roomNotFoundModal, true);
        ShowStatus("방 이름을 다시 확인해 주세요.", StatusKind.Error);
    }

    private void ShowStatus(string message, StatusKind kind)
    {
        if (statusLabel == null)
        {
            return;
        }

        statusLabel.text = message;
        statusLabel.RemoveFromClassList("tm-status-label--error");
        statusLabel.RemoveFromClassList("tm-status-label--success");

        if (kind == StatusKind.Error)
        {
            statusLabel.AddToClassList("tm-status-label--error");
        }
        else if (kind == StatusKind.Success)
        {
            statusLabel.AddToClassList("tm-status-label--success");
        }

        SetDisplay(statusLabel, true);
    }

    private void HideStatus()
    {
        SetDisplay(statusLabel, false);
    }

    private static void SetDisplay(VisualElement element, bool visible)
    {
        if (element == null)
        {
            return;
        }

        element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private static bool IsPrimaryPointer(PointerUpEvent evt)
    {
        return evt == null || evt.button == 0;
    }

    private static string GetStatusText(RoomStatus status)
    {
        switch (status)
        {
            case RoomStatus.Playing:
                return "PLAYING";
            case RoomStatus.Full:
                return "FULL";
            case RoomStatus.Unavailable:
                return "CLOSED";
            default:
                return "WAITING";
        }
    }

    private static string GetRoomCardStatusClass(RoomStatus status)
    {
        switch (status)
        {
            case RoomStatus.Playing:
                return "tm-room-card--playing";
            case RoomStatus.Full:
                return "tm-room-card--full";
            case RoomStatus.Unavailable:
                return "tm-room-card--unavailable";
            default:
                return "tm-room-card--waiting";
        }
    }

    private static string GetRoomStatusClass(RoomStatus status)
    {
        switch (status)
        {
            case RoomStatus.Playing:
                return "tm-status--playing";
            case RoomStatus.Full:
                return "tm-status--full";
            case RoomStatus.Unavailable:
                return "tm-status--unavailable";
            default:
                return "tm-status--waiting";
        }
    }

    private static string GetRankIcon(int visualIndex)
    {
        return string.Empty;
    }

    private static string GetRankIconClass(int visualIndex)
    {
        switch (visualIndex % 4)
        {
            case 1:
                return "tm-room-rank-icon--silver";
            case 2:
                return "tm-room-rank-icon--bronze";
            default:
                return "tm-room-rank-icon--gold";
        }
    }

    private static string GetThumbnailClass(int visualIndex)
    {
        switch (visualIndex % 4)
        {
            case 1:
                return "tm-room-thumbnail--farm";
            case 2:
                return "tm-room-thumbnail--candy";
            case 3:
                return "tm-room-thumbnail--beach";
            default:
                return "tm-room-thumbnail--forest";
        }
    }

    private static void SetPickingMode(VisualElement element, PickingMode pickingMode)
    {
        if (element != null)
        {
            element.pickingMode = pickingMode;
        }
    }
}
