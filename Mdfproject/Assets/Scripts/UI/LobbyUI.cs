// Assets/Scripts/UI/LobbyUI.cs
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Fusion;
using UnityEngine.SceneManagement;
using Cysharp.Threading.Tasks; // UniTask 사용을 위해 추가

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
    
    [Header("Settings")]
    [SerializeField] private float _refreshInterval = 5f; // 5초마다 새로고침

    private NetworkManager _networkManager;
    private List<GameObject> _roomListItems = new List<GameObject>();
    private bool _isRefreshing = false;

    private void Start()
    {
        _networkManager = NetworkManager.Instance;
        
        if (_networkManager == null)
        {
            Debug.LogError("NetworkManager를 찾을 수 없습니다! Title 씬으로 이동합니다.");
            SceneManager.LoadScene("Title");
            return;
        }
    
        if (!_networkManager.IsConnectedToServer)
        {
            Debug.LogError("로그인되지 않았습니다. Title 씬으로 이동합니다.");
            SceneManager.LoadScene("Title");
            return;
        }

        if (_roomItemPrefab == null)
        {
            Debug.LogError("<color=red>[LobbyUI] Room Item Prefab이 Inspector에 할당되지 않았습니다! UI를 생성할 수 없습니다.</color>", this.gameObject);
        }
    
        InitializeUI();
        SubscribeToEvents();
        
        if (_playerNicknameText != null)
        {
            _playerNicknameText.text = $"Player: {_networkManager.PlayerNickname}";
        }
        
        ShowRoomListPanel();
        
        PeriodicRefreshRoutine().Forget();
    }
    
    private async UniTaskVoid PeriodicRefreshRoutine()
    {
        var cancellationToken = this.GetCancellationTokenOnDestroy();

        while (!cancellationToken.IsCancellationRequested)
        {
            // [수정] 방 생성 패널이 비활성화 상태이고, 새로고침 중이 아닐 때만 자동 새로고침 실행
            if (_roomListPanel != null && _roomListPanel.activeSelf && !_isRefreshing)
            {
                await RefreshRoomList();
            }
            
            await UniTask.Delay(TimeSpan.FromSeconds(_refreshInterval), cancellationToken: cancellationToken);
        }
    }

    private void InitializeUI()
    {
        if (_createRoomButton != null)
            _createRoomButton.onClick.AddListener(ShowCreateRoomPanel);

        if (_refreshButton != null)
            _refreshButton.onClick.AddListener(async () => await RefreshRoomList());

        if (_backToTitleButton != null)
            _backToTitleButton.onClick.AddListener(BackToTitle);

        if (_confirmCreateButton != null)
            _confirmCreateButton.onClick.AddListener(CreateRoom);

        if (_cancelCreateButton != null)
            _cancelCreateButton.onClick.AddListener(ShowRoomListPanel);
    }

    private void SubscribeToEvents()
    {
        if (_networkManager == null) return;
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

    private void ShowCreateRoomPanel()
    {
        _roomListPanel.SetActive(false);
        _createRoomPanel.SetActive(true);

        if (_roomNameInput != null)
        {
            _roomNameInput.text = $"Room_{UnityEngine.Random.Range(1000, 9999)}";
        }
    }

    private async void ShowRoomListPanel()
    {
        if (_roomListPanel != null)
            _roomListPanel.SetActive(true);

        if (_createRoomPanel != null)
            _createRoomPanel.SetActive(false);

        await RefreshRoomList();
    }

    private void BackToTitle()
    {
        if (_networkManager != null)
        {
            _networkManager.Disconnect();
        }
        SceneManager.LoadScene("Title");
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

        if (success)
        {
            ShowStatus("방 생성 완료. JoinLobby로 이동합니다...", false);
            _confirmCreateButton.interactable = false;
            _cancelCreateButton.interactable = false;
        }
        else
        {
            ShowLoading(false);
            // [수정] 실패 메시지를 조금 더 구체적으로 변경
            ShowStatus("방 생성에 실패했습니다. 방 이름이 중복되거나 서버에 문제가 있을 수 있습니다.", true);
            ShowRoomListPanel();
        }
    }

    private async void JoinRoom(string roomName)
    {
        ShowLoading(true);
        ShowStatus($"'{roomName}' 방 참여 중...", false);

        bool success = await _networkManager.JoinRoom(roomName, "JoinLobby");

        if (!success)
        {
            ShowLoading(false);
            ShowStatus($"'{roomName}' 방 참여 실패", true);
        }
    }

    private async Task RefreshRoomList()
    {
        // [수정] 이미 새로고침이 진행 중이면, 추가 요청을 무시합니다.
        if (_isRefreshing) 
        {
            Debug.Log("[LobbyUI] 이미 새로고침이 진행 중입니다. 요청을 건너뜁니다.");
            return;
        }
        _isRefreshing = true;

        if (_networkManager == null)
        {
            ShowStatus("NetworkManager를 찾을 수 없습니다.", true);
            _isRefreshing = false;
            return;
        }
    
        if (!_networkManager.IsConnectedToServer)
        {
            ShowStatus("로그인이 필요합니다.", true);
            _isRefreshing = false;
            SceneManager.LoadScene("Title");
            return;
        }
        
        ShowStatus("방 목록 새로고침 중...", false);
        await _networkManager.RefreshRoomList();
        
        // [수정] OnRoomListUpdated 콜백이 호출된 후 _isRefreshing이 false가 되도록 NetworkManager에서 제어하므로,
        // 여기서는 바로 false로 만들지 않고 OnRoomListUpdated에서 처리하도록 기다립니다.
    }

    private void OnRoomListUpdated(List<SessionInfo> rooms)
    {
        Debug.Log($"<color=cyan>[LobbyUI] UI가 {rooms.Count}개의 방 정보를 전달받아 화면 업데이트를 시작합니다.</color>");

        foreach (var item in _roomListItems)
        {
            if (item != null)
                Destroy(item);
        }
        _roomListItems.Clear();
        
        if (_roomItemPrefab == null)
        {
            string criticalError = "치명적 오류: LobbyUI의 RoomItem Prefab이 Inspector에 할당되지 않았습니다!";
            Debug.LogError(criticalError, this.gameObject);
            ShowStatus(criticalError, true);
            if (_noRoomsText != null)
            {
                _noRoomsText.text = criticalError;
                _noRoomsText.gameObject.SetActive(true);
            }
            _isRefreshing = false; // [추가] 오류 발생 시에도 새로고침 상태를 해제
            return;
        }

        if (rooms == null || rooms.Count == 0)
        {
            if (_noRoomsText != null)
            {
                _noRoomsText.text = "현재 생성된 방이 없습니다.";
                _noRoomsText.gameObject.SetActive(true);
            }
            ShowStatus("생성된 방이 없습니다.", false);
        }
        else
        {
            if (_noRoomsText != null)
                _noRoomsText.gameObject.SetActive(false);
            ShowStatus($"{rooms.Count}개의 방을 찾았습니다.", false);
        }


        Debug.Log($"<color=yellow>[LobbyUI] 총 {rooms.Count}개의 RoomItem 프리팹 생성을 시작합니다. Parent: {_roomListContent.name}</color>");

        foreach (var room in rooms)
        {
            GameObject roomItem = Instantiate(_roomItemPrefab, _roomListContent);
            _roomListItems.Add(roomItem);

            if(roomItem == null)
            {
                Debug.LogError($"<color=red>[LobbyUI] Instantiate 결과가 NULL입니다! _roomItemPrefab에 문제가 있을 수 있습니다.</color>");
                continue;
            }

            RoomItem roomItemComponent = roomItem.GetComponent<RoomItem>();
            if (roomItemComponent != null)
            {
                roomItemComponent.Setup(room.Name, room.PlayerCount, room.MaxPlayers, () => JoinRoom(room.Name));
            }
        }
        
        _isRefreshing = false; // [추가] 방 목록 업데이트가 완료되면 새로고침 상태를 해제
    }

    private void OnConnectionStatusChanged(bool connected)
    {
        if (!connected)
        {
            ShowStatus("서버 연결이 끊어졌습니다.", true);
            _isRefreshing = false; // [추가] 연결이 끊어져도 새로고침 상태를 해제
        }
    }

    private void OnErrorOccurred(string error)
    {
        ShowStatus(error, true);
        ShowLoading(false);
        _isRefreshing = false; // [추가] 오류 발생 시에도 새로고침 상태를 해제
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