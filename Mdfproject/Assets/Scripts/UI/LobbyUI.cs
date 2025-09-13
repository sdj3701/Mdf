// Assets/Scripts/UI/LobbyUI.cs
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Fusion;
using UnityEngine.SceneManagement;
using Cysharp.Threading.Tasks;

public class LobbyUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private GameObject _roomListPanel;
    [SerializeField] private GameObject _createRoomPanel;
    [SerializeField] private Button _createRoomButton;
    [SerializeField] private Button _refreshButton;
    [SerializeField] private Button _backToTitleButton;
    [SerializeField] private TMP_InputField _roomNameInput;
    [SerializeField] private Button _confirmCreateButton;
    [SerializeField] private Button _cancelCreateButton;
    [SerializeField] private Transform _roomListContent;
    [SerializeField] private GameObject _roomItemPrefab;
    [SerializeField] private TMP_Text _noRoomsText;
    [SerializeField] private TMP_Text _playerNicknameText;
    [SerializeField] private TMP_Text _statusText;
    [SerializeField] private GameObject _loadingPanel;
    
    private NetworkManager _networkManager;
    private List<GameObject> _roomListItems = new List<GameObject>();
    private bool _isRefreshing = false;
    private bool _isCreatingRoom = false;

    private void Start()
    {
        _networkManager = NetworkManager.Instance;
        
        if (_networkManager == null)
        {
            SceneManager.LoadScene("Title");
            return;
        }
    
        // ✅ [수정] LobbyUI에서는 LobbyRunner의 연결 상태를 확인해야 합니다.
        if (!_networkManager.IsConnectedToServer)
        {
            SceneManager.LoadScene("Title");
            return;
        }
    
        InitializeUI();
        SubscribeToEvents();
        
        if (_playerNicknameText != null)
        {
            _playerNicknameText.text = $"Player: {_networkManager.PlayerNickname}";
        }
        
        ShowRoomListPanel();
        RefreshRoomList().Forget();
    }

    private void InitializeUI()
    {
        _createRoomButton?.onClick.AddListener(ShowCreateRoomPanel);
        _refreshButton?.onClick.AddListener(() => RefreshRoomList().Forget());
        _backToTitleButton?.onClick.AddListener(BackToTitle);
        _confirmCreateButton?.onClick.AddListener(CreateRoom);
        _cancelCreateButton?.onClick.AddListener(ShowRoomListPanel);
    }

    private void SubscribeToEvents()
    {
        if (_networkManager == null) return;
        
        _networkManager.OnRoomListUpdated += OnRoomListUpdated;
        _networkManager.OnConnectionStatusChanged += OnConnectionStatusChanged;
        _networkManager.OnErrorOccurred += OnErrorOccurred;
        _networkManager.OnRoomCreationStarted += OnRoomCreationStarted;
        _networkManager.OnRoomCreationCompleted += OnRoomCreationCompleted;
    }

    private void OnDestroy()
    {
        if (_networkManager != null)
        {
            _networkManager.OnRoomListUpdated -= OnRoomListUpdated;
            _networkManager.OnConnectionStatusChanged -= OnConnectionStatusChanged;
            _networkManager.OnErrorOccurred -= OnErrorOccurred;
            _networkManager.OnRoomCreationStarted -= OnRoomCreationStarted;
            _networkManager.OnRoomCreationCompleted -= OnRoomCreationCompleted;
        }
    }

    private void ShowCreateRoomPanel()
    {
        _roomListPanel.SetActive(false);
        _createRoomPanel.SetActive(true);
        _roomNameInput.text = $"Room_{UnityEngine.Random.Range(1000, 9999)}";
    }

    private void ShowRoomListPanel()
    {
        _roomListPanel?.SetActive(true);
        _createRoomPanel?.SetActive(false);
    }

    private void BackToTitle()
    {
        _networkManager?.DisconnectCompletely();
        SceneManager.LoadScene("Title");
    }

    private async void CreateRoom()
    {
        if (string.IsNullOrWhiteSpace(_roomNameInput.text) || _roomNameInput.text.Length < 3)
        {
            ShowStatus("방 이름은 3자 이상이어야 합니다.", true);
            return;
        }
        if (_isCreatingRoom) return;

        await _networkManager.CreateRoom(_roomNameInput.text.Trim(), "JoinLobby");
    }

    private async void JoinRoom(string roomName)
    {
        ShowLoading(true);
        ShowStatus($"'{roomName}' 방에 참여 중...", false);
        await _networkManager.JoinRoom(roomName, "JoinLobby");
    }

    private async UniTask RefreshRoomList()
    {
        if (_isRefreshing) return;
        _isRefreshing = true;
        ShowStatus("방 목록 새로고침...", false);
        
        // ✅ [수정] NetworkManager의 public 메서드를 호출합니다.
        if (_networkManager != null)
        {
            await _networkManager.RefreshRoomList();
        }
        
        _isRefreshing = false;
    }

    private void OnRoomListUpdated(List<SessionInfo> rooms)
    {
        foreach (var item in _roomListItems)
        {
            Destroy(item);
        }
        _roomListItems.Clear();

        if (_roomItemPrefab == null || _roomListContent == null) return;

        if (rooms.Count == 0)
        {
            _noRoomsText.text = "현재 생성된 방이 없습니다.\n직접 방을 만들거나 다른 플레이어를 기다려주세요.";
            _noRoomsText.gameObject.SetActive(true);
            ShowStatus("생성된 방이 없습니다.", false);
        }
        else
        {
            _noRoomsText.gameObject.SetActive(false);
            ShowStatus($"{rooms.Count}개의 방을 찾았습니다.", false);
        }

        foreach (var room in rooms)
        {
            GameObject roomItemObj = Instantiate(_roomItemPrefab, _roomListContent);
            _roomListItems.Add(roomItemObj);
            RoomItem roomItem = roomItemObj.GetComponent<RoomItem>();
            if(roomItem != null) roomItem.Setup(room.Name, room.PlayerCount, room.MaxPlayers, () => JoinRoom(room.Name));
        }
    }

    private void OnConnectionStatusChanged(bool connected)
    {
        if (!connected)
        {
            ShowStatus("서버 연결이 끊어졌습니다.", true);
            ShowLoading(false);
        }
    }

    private void OnErrorOccurred(string error)
    {
        ShowStatus(error, true);
        ShowLoading(false);
        _isCreatingRoom = false;
        _confirmCreateButton.interactable = true;
    }
    
    private void OnRoomCreationStarted(string roomName)
    {
        _isCreatingRoom = true;
        ShowLoading(true);
        ShowStatus($"'{roomName}' 방 생성 중...", false);
        if(_confirmCreateButton != null) _confirmCreateButton.interactable = false;
    }

    private void OnRoomCreationCompleted(string roomName, bool success)
    {
        _isCreatingRoom = false;
        if (!success)
        {
            ShowLoading(false);
            if(_confirmCreateButton != null) _confirmCreateButton.interactable = true;
        }
    }

    private void ShowStatus(string message, bool isError)
    {
        if (_statusText != null)
        {
            _statusText.text = message;
            _statusText.color = isError ? Color.red : Color.white;
        }
    }

    private void ShowLoading(bool show)
    {
        if (_loadingPanel != null)
        {
            _loadingPanel.SetActive(show);
        }
    }
}