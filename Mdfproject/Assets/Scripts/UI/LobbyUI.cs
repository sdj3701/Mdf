/*using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Fusion;
using System.Threading.Tasks;

public class LobbyUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private GameObject _mainPanel;
    [SerializeField] private GameObject _roomListPanel;
    [SerializeField] private GameObject _createRoomPanel;
    [SerializeField] private GameObject _inRoomPanel;
    
    [Header("Main Panel")]
    [SerializeField] private Button _createRoomButton;
    [SerializeField] private Button _joinRoomButton;
    [SerializeField] private Button _refreshButton;
    
    [Header("Create Room Panel")]
    [SerializeField] private TMP_InputField _roomNameInput;
    [SerializeField] private Button _confirmCreateButton;
    [SerializeField] private Button _cancelCreateButton;
    
    [Header("Room List")]
    [SerializeField] private Transform _roomListContent;
    [SerializeField] private GameObject _roomItemPrefab;
    [SerializeField] private TMP_Text _noRoomsText;
    
    [Header("In Room Panel")]
    [SerializeField] private TMP_Text _currentRoomName;
    [SerializeField] private TMP_Text _playerCountText;
    [SerializeField] private Button _startGameButton;
    [SerializeField] private Button _leaveRoomButton;
    [SerializeField] private Transform _playerListContent;
    [SerializeField] private GameObject _playerItemPrefab;
    
    [Header("Status")]
    [SerializeField] private TMP_Text _statusText;
    [SerializeField] private GameObject _loadingPanel;
    
    private NetworkManager _networkManager;
    private List<GameObject> _roomListItems = new List<GameObject>();
    private List<GameObject> _playerListItems = new List<GameObject>();
    
    private void Start()
    {
        _networkManager = NetworkManager.Instance;
        
        if (_networkManager == null)
        {
            Debug.LogError("NetworkManager not found!");
            return;
        }
        
        InitializeUI();
        SubscribeToEvents();
        
        // 시작 시 방 목록 새로고침
        RefreshRoomList();
    }
    
    private void InitializeUI()
    {
        // 버튼 이벤트 연결
        _createRoomButton.onClick.AddListener(ShowCreateRoomPanel);
        _joinRoomButton.onClick.AddListener(ShowRoomListPanel);
        _refreshButton.onClick.AddListener(RefreshRoomList);
        
        _confirmCreateButton.onClick.AddListener(CreateRoom);
        _cancelCreateButton.onClick.AddListener(ShowMainPanel);
        
        _startGameButton.onClick.AddListener(StartGame);
        _leaveRoomButton.onClick.AddListener(LeaveRoom);
        
        // 초기 패널 설정
        ShowMainPanel();
        
        // 호스트만 게임 시작 가능
        _startGameButton.gameObject.SetActive(false);
    }
    
    private void SubscribeToEvents()
    {
        _networkManager.OnRoomListUpdated += OnRoomListUpdated;
        _networkManager.OnConnectionStatusChanged += OnConnectionStatusChanged;
        _networkManager.OnErrorOccurred += OnErrorOccurred;
    }
    
    private void OnDestroy()
    {
        if (_networkManager != null)
        {
            _networkManager.OnRoomListUpdated -= OnRoomListUpdated;
            _networkManager.OnConnectionStatusChanged -= OnConnectionStatusChanged;
            _networkManager.OnErrorOccurred -= OnErrorOccurred;
        }
    }
    
    private void ShowMainPanel()
    {
        _mainPanel.SetActive(true);
        _roomListPanel.SetActive(false);
        _createRoomPanel.SetActive(false);
        _inRoomPanel.SetActive(false);
    }
    
    private void ShowCreateRoomPanel()
    {
        _mainPanel.SetActive(false);
        _createRoomPanel.SetActive(true);
        _roomNameInput.text = $"Room_{Random.Range(1000, 9999)}";
    }
    
    private void ShowRoomListPanel()
    {
        _mainPanel.SetActive(false);
        _roomListPanel.SetActive(true);
        RefreshRoomList();
    }
    
    private void ShowInRoomPanel()
    {
        _mainPanel.SetActive(false);
        _roomListPanel.SetActive(false);
        _createRoomPanel.SetActive(false);
        _inRoomPanel.SetActive(true);
        
        // 호스트인 경우 게임 시작 버튼 표시
        _startGameButton.gameObject.SetActive(_networkManager.IsHost);
        
        UpdateRoomInfo();
    }
    
    private async void CreateRoom()
    {
        string roomName = _roomNameInput.text.Trim();
        
        if (string.IsNullOrEmpty(roomName))
        {
            ShowStatus("Please enter a room name", true);
            return;
        }
        
        ShowLoading(true);
        ShowStatus("Creating room...", false);
        
        bool success = await _networkManager.CreateRoom(roomName);
        
        ShowLoading(false);
        
        if (success)
        {
            _currentRoomName.text = roomName;
            ShowInRoomPanel();
            ShowStatus($"Room '{roomName}' created successfully", false);
        }
    }
    
    private async void JoinRoom(string roomName)
    {
        ShowLoading(true);
        ShowStatus($"Joining room '{roomName}'...", false);
        
        bool success = await _networkManager.JoinRoom(roomName);
        
        ShowLoading(false);
        
        if (success)
        {
            _currentRoomName.text = roomName;
            ShowInRoomPanel();
            ShowStatus($"Joined room '{roomName}'", false);
        }
    }
    
    private void LeaveRoom()
    {
        _networkManager.Disconnect();
        ShowMainPanel();
        ShowStatus("Left the room", false);
    }
    
    private async void RefreshRoomList()
    {
        ShowStatus("Refreshing room list...", false);
        await _networkManager.RefreshRoomList();
    }
    
    private void OnRoomListUpdated(List<SessionInfo> rooms)
    {
        // 기존 방 목록 아이템 제거
        foreach (var item in _roomListItems)
        {
            Destroy(item);
        }
        _roomListItems.Clear();
        
        // 방이 없는 경우
        if (rooms == null || rooms.Count == 0)
        {
            _noRoomsText.gameObject.SetActive(true);
            ShowStatus("No rooms available", false);
            return;
        }
        
        _noRoomsText.gameObject.SetActive(false);
        
        // 방 목록 생성
        foreach (var room in rooms)
        {
            GameObject roomItem = Instantiate(_roomItemPrefab, _roomListContent);
            _roomListItems.Add(roomItem);
            
            // RoomItem 컴포넌트 설정
            RoomItem roomItemComponent = roomItem.GetComponent<RoomItem>();
            if (roomItemComponent != null)
            {
                roomItemComponent.Setup(room.Name, room.PlayerCount, room.MaxPlayers, () => JoinRoom(room.Name));
            }
            else
            {
                // RoomItem 컴포넌트가 없는 경우 기본 설정
                TMP_Text[] texts = roomItem.GetComponentsInChildren<TMP_Text>();
                if (texts.Length >= 2)
                {
                    texts[0].text = room.Name;
                    texts[1].text = $"{room.PlayerCount}/{room.MaxPlayers}";
                }
                
                Button button = roomItem.GetComponent<Button>();
                if (button != null)
                {
                    string roomName = room.Name;
                    button.onClick.AddListener(() => JoinRoom(roomName));
                }
            }
        }
        
        ShowStatus($"Found {rooms.Count} room(s)", false);
    }
    
    private void OnConnectionStatusChanged(bool connected)
    {
        if (connected)
        {
            UpdateRoomInfo();
        }
        else
        {
            ShowMainPanel();
        }
    }
    
    private void OnErrorOccurred(string error)
    {
        ShowStatus(error, true);
        ShowLoading(false);
    }
    
    private void UpdateRoomInfo()
    {
        if (!_networkManager.IsConnected)
            return;
        
        _playerCountText.text = $"Players: {_networkManager.CurrentPlayerCount}/{_networkManager.MaxPlayerCount}";
        
        // 2명이 모두 들어온 경우 호스트가 게임 시작 가능
        if (_networkManager.IsHost && _networkManager.CurrentPlayerCount == _networkManager.MaxPlayerCount)
        {
            _startGameButton.interactable = true;
            ShowStatus("Ready to start game!", false);
        }
        else if (_networkManager.IsHost)
        {
            _startGameButton.interactable = false;
            ShowStatus("Waiting for players...", false);
        }
        else
        {
            ShowStatus("Waiting for host to start...", false);
        }
    }
    
    private void StartGame()
    {
        if (_networkManager.IsHost && _networkManager.CurrentPlayerCount == _networkManager.MaxPlayerCount)
        {
            // MainLobby 씬으로 이동
            UnityEngine.SceneManagement.SceneManager.LoadScene("MainLobby");
        }
    }
    
    private void ShowStatus(string message, bool isError)
    {
        if (_statusText != null)
        {
            _statusText.text = message;
            _statusText.color = isError ? Color.red : Color.white;
        }
        
        Debug.Log($"[LobbyUI] {message}");
    }
    
    private void ShowLoading(bool show)
    {
        if (_loadingPanel != null)
        {
            _loadingPanel.SetActive(show);
        }
    }
}
*/
