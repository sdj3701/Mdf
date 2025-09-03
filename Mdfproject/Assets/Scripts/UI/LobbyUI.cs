using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Fusion;
using System.Threading.Tasks;

public class LobbyUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private GameObject _roomListPanel;
    [SerializeField] private GameObject _createRoomPanel;

    [Header("Room List Panel")]
    [SerializeField] private Button _createRoomButton;
    [SerializeField] private Button _refreshButton;
    [SerializeField] private Button _backToTitleButton;

    [Header("Create Room Panel")]
    [SerializeField] private TMP_InputField _roomNameInput;
    [SerializeField] private Button _confirmCreateButton;
    [SerializeField] private Button _cancelCreateButton;

    [Header("Room List")]
    [SerializeField] private Transform _roomListContent;
    [SerializeField] private GameObject _roomItemPrefab;
    [SerializeField] private TMP_Text _noRoomsText;

    [Header("Player Info")]
    [SerializeField] private TMP_Text _playerNicknameText;

    [Header("Status")]
    [SerializeField] private TMP_Text _statusText;
    [SerializeField] private GameObject _loadingPanel;

    private NetworkManager _networkManager;
    private List<GameObject> _roomListItems = new List<GameObject>();
    private float _refreshInterval = 3f;
    private float _lastRefreshTime;

    private void Start()
    {
        // ✅ [수정] GetOrCreateInstance() 대신 Instance 프로퍼티를 사용하여 싱글톤에 접근합니다.
        _networkManager = NetworkManager.Instance;
        
        if (_networkManager == null)
        {
            Debug.LogError("NetworkManager를 찾을 수 없습니다! Title 씬으로 이동합니다.");
            UnityEngine.SceneManagement.SceneManager.LoadScene("Title");
            return;
        }
    
        if (!_networkManager.IsConnectedToServer)
        {
            Debug.LogError("로그인되지 않았습니다. Title 씬으로 이동합니다.");
            UnityEngine.SceneManagement.SceneManager.LoadScene("Title");
            return;
        }
    
        InitializeUI();
        SubscribeToEvents();
        
        if (_playerNicknameText != null)
        {
            _playerNicknameText.text = $"Player: {_networkManager.PlayerNickname}";
        }
        
        ShowRoomListPanel();
        
        StartCoroutine(DelayedRefresh());
    }
    
    private System.Collections.IEnumerator DelayedRefresh()
    {
        yield return new WaitForSeconds(0.5f);
        RefreshRoomList();
    }

    private void InitializeUI()
    {
        if (_createRoomButton != null)
            _createRoomButton.onClick.AddListener(ShowCreateRoomPanel);

        if (_refreshButton != null)
            _refreshButton.onClick.AddListener(RefreshRoomList);

        if (_backToTitleButton != null)
            _backToTitleButton.onClick.AddListener(BackToTitle);

        if (_confirmCreateButton != null)
            _confirmCreateButton.onClick.AddListener(CreateRoom);

        if (_cancelCreateButton != null)
            _cancelCreateButton.onClick.AddListener(ShowRoomListPanel);
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

    private void Update()
    {
        if (Time.time - _lastRefreshTime > _refreshInterval && _roomListPanel.activeSelf)
        {
            RefreshRoomList();
            _lastRefreshTime = Time.time;
        }
    }

    private void ShowCreateRoomPanel()
    {
        _roomListPanel.SetActive(false);
        _createRoomPanel.SetActive(true);

        if (_roomNameInput != null)
        {
            _roomNameInput.text = $"Room_{Random.Range(1000, 9999)}";
        }
    }

    private void ShowRoomListPanel()
    {
        if (_roomListPanel != null)
            _roomListPanel.SetActive(true);

        if (_createRoomPanel != null)
            _createRoomPanel.SetActive(false);

        RefreshRoomList();
    }

    private void BackToTitle()
    {
        if (_networkManager != null)
        {
            _networkManager.Disconnect();
        }
        UnityEngine.SceneManagement.SceneManager.LoadScene("Title");
    }

    private async void CreateRoom()
    {
        if (_roomNameInput == null)
        {
            ShowStatus("방 이름 입력 필드를 찾을 수 없습니다.", true);
            return;
        }

        string roomName = _roomNameInput.text.Trim();

        if (string.IsNullOrEmpty(roomName))
        {
            ShowStatus("방 이름을 입력해주세요.", true);
            return;
        }

        if (roomName.Length < 3 || roomName.Length > 20)
        {
            ShowStatus("방 이름은 3-20자 사이여야 합니다.", true);
            return;
        }

        ShowLoading(true);
        ShowStatus("방 생성 중...", false);

        bool success = await _networkManager.CreateRoom(roomName, "JoinLobby");

        ShowLoading(false);

        if (!success)
        {
            ShowStatus("방 생성 실패", true);
            ShowRoomListPanel();
        }
    }

    private async void JoinRoom(string roomName)
    {
        ShowLoading(true);
        ShowStatus($"'{roomName}' 방 참여 중...", false);

        bool success = await _networkManager.JoinRoom(roomName, "JoinLobby");

        ShowLoading(false);

        if (!success)
        {
            ShowStatus($"'{roomName}' 방 참여 실패", true);
        }
    }

    private async void RefreshRoomList()
    {
        if (_networkManager == null)
        {
            ShowStatus("NetworkManager를 찾을 수 없습니다.", true);
            return;
        }
    
        if (!_networkManager.IsConnectedToServer)
        {
            ShowStatus("로그인이 필요합니다.", true);
            UnityEngine.SceneManagement.SceneManager.LoadScene("Title");
            return;
        }
        
        ShowStatus("방 목록 새로고침 중...", false);
        await _networkManager.RefreshRoomList();
    }

    private void OnRoomListUpdated(List<SessionInfo> rooms)
    {
        foreach (var item in _roomListItems)
        {
            if (item != null)
                Destroy(item);
        }
        _roomListItems.Clear();
    
        if (rooms == null || rooms.Count == 0)
        {
            if (_noRoomsText != null)
                _noRoomsText.gameObject.SetActive(true);
            ShowStatus("생성된 방이 없습니다.", false);
            return;
        }

        if (_noRoomsText != null)
            _noRoomsText.gameObject.SetActive(false);
        ShowStatus($"{rooms.Count}개의 방을 찾았습니다.", false);

        foreach (var room in rooms)
        {
            GameObject roomItem = Instantiate(_roomItemPrefab, _roomListContent);
            _roomListItems.Add(roomItem);

            RoomItem roomItemComponent = roomItem.GetComponent<RoomItem>();
            if (roomItemComponent != null)
            {
                roomItemComponent.Setup(room.Name, room.PlayerCount, room.MaxPlayers, () => JoinRoom(room.Name));
            }
        }
    }

    private void OnConnectionStatusChanged(bool connected)
    {
        if (!connected)
        {
            ShowStatus("서버 연결이 끊어졌습니다.", true);
        }
    }

    private void OnErrorOccurred(string error)
    {
        ShowStatus(error, true);
        ShowLoading(false);
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