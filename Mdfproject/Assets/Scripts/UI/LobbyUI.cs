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
using Cysharp.Threading.Tasks;

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

    [Header("Direct Join")]
    [SerializeField] private GameObject _directJoinPanel;
    [SerializeField] private TMP_InputField _directJoinInput;
    [SerializeField] private Button _directJoinButton;
    [SerializeField] private Button _showDirectJoinButton;
    [SerializeField] private Button _hideDirectJoinButton;
    
    [Header("Status")]
    [SerializeField] private TMP_Text _statusText;
    [SerializeField] private GameObject _loadingPanel;
    
    [Header("Settings")]
    [SerializeField] private float _refreshInterval = 2f; // ✅ 2초로 설정 (더 안정적)

    private NetworkManager _networkManager;
    private List<GameObject> _roomListItems = new List<GameObject>();
    private bool _isRefreshing = false;
    
    // ✅ [추가] 마지막 새로고침 시간 추적
    private float _lastRefreshTime = 0f;
    private const float MIN_REFRESH_INTERVAL = 2f; // 최소 2초 간격

    // ✅ [추가] 재시도 관련 변수
    private int _refreshRetryCount = 0;
    private const int MAX_REFRESH_RETRIES = 5;

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
            Debug.LogError("<color=red>[LobbyUI] Room Item Prefab이 Inspector에 할당되지 않았습니다!</color>", this.gameObject);
        }
    
        InitializeUI();
        SubscribeToEvents();
        
        if (_playerNicknameText != null)
        {
            _playerNicknameText.text = $"Player: {_networkManager.PlayerNickname}";
        }
        
        ShowRoomListPanel();
        
        // ✅ [수정] 더 안정적인 새로고침 시작
        PeriodicRefreshRoutine().Forget();
    }
    
    private void Update()
    {
        // T키로 테스트 방 추가
        if (Input.GetKeyDown(KeyCode.T))
        {
            TestAddRoom();
        }
        
        // ✅ [추가] R키로 강제 새로고침
        if (Input.GetKeyDown(KeyCode.R))
        {
            Debug.Log("🔑 R키 강제 새로고침!");
            //ForceRefreshRoomList().Forget();
        }
    }

    private void TestAddRoom()
    {
        Debug.Log("🔑 T키 테스트 방 추가!");
        
        // 가짜 방 목록 생성
        var testRooms = new List<Fusion.SessionInfo>();
        
        // 현재 연결된 방이 있다면 표시
        if (_networkManager != null && _networkManager.IsConnected)
        {
            Debug.Log("현재 방 상태 확인 중...");
        }
        
        // 강제로 UI 업데이트 (빈 목록이라도)
        OnRoomListUpdated(testRooms);
        ShowStatus("테스트: T키를 눌렀습니다. 빌드에서 방을 생성하세요.", false);
    }

    // ✅ [개선] 더 안정적인 주기적 새로고침
    private async UniTaskVoid PeriodicRefreshRoutine()
    {
        var cancellationToken = this.GetCancellationTokenOnDestroy();

        // 첫 시작할 때 잠시 대기 후 새로고침
        await UniTask.Delay(1000, cancellationToken: cancellationToken);
        await RefreshRoomList();

        while (!cancellationToken.IsCancellationRequested)
        {
            await UniTask.Delay(TimeSpan.FromSeconds(_refreshInterval), cancellationToken: cancellationToken);

            if (_roomListPanel != null && _roomListPanel.activeSelf)
            {
                // ✅ [개선] 더 안정적인 새로고침 조건
                if (Time.time - _lastRefreshTime > MIN_REFRESH_INTERVAL && !_isRefreshing)
                {
                    await RefreshRoomList();
                }
            }
        }
    }

    private void InitializeUI()
    {
        if (_createRoomButton != null)
            _createRoomButton.onClick.AddListener(ShowCreateRoomPanel);

        // ✅ [개선] 새로고침 버튼에 강제 새로고침 추가
        if (_refreshButton != null)
            _refreshButton.onClick.AddListener(async () => await ForceRefreshRoomList());

        if (_backToTitleButton != null)
            _backToTitleButton.onClick.AddListener(BackToTitle);

        if (_confirmCreateButton != null)
            _confirmCreateButton.onClick.AddListener(CreateRoom);

        if (_cancelCreateButton != null)
            _cancelCreateButton.onClick.AddListener(ShowRoomListPanel);
            
        // 직접 연결 버튼들
        if (_showDirectJoinButton != null)
            _showDirectJoinButton.onClick.AddListener(ShowDirectJoinPanel);
            
        if (_hideDirectJoinButton != null)
            _hideDirectJoinButton.onClick.AddListener(HideDirectJoinPanel);
            
        if (_directJoinButton != null)
            _directJoinButton.onClick.AddListener(DirectJoinRoom);
            
        // 초기 상태: 직접 연결 패널 숨김
        if (_directJoinPanel != null)
            _directJoinPanel.SetActive(false);
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
            _networkManager.DisconnectCompletely();
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
            
            if (_confirmCreateButton != null)
                _confirmCreateButton.interactable = false;
            if (_cancelCreateButton != null)
                _cancelCreateButton.interactable = false;
        }
        else
        {
            ShowLoading(false);
            ShowStatus("방 생성에 실패했습니다.", true);
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

    // ✅ [개선] 표준 새로고침 - 재시도 로직 추가
    private async Task RefreshRoomList()
    {
        if (_isRefreshing) 
        {
            Debug.Log("[LobbyUI] 이미 새로고침이 진행 중입니다.");
            return;
        }
        
        _lastRefreshTime = Time.time;
        await InternalRefreshRoomList(false);
    }
    
    // ✅ [개선] 강제 새로고침 - 재시도와 함께
    private async Task ForceRefreshRoomList()
    {
        Debug.Log("<color=orange>[LobbyUI] 🔄 강제 새로고침 시작</color>");
        
        // 진행 중인 새로고침 취소
        _isRefreshing = false;
        _refreshRetryCount = 0; // 재시도 카운트 리셋
        
        await UniTask.Delay(200); // 짧은 대기
        await InternalRefreshRoomList(true);
    }

    // ✅ [개선] 내부 새로고침 로직 - 재시도 기능 강화
    private async Task InternalRefreshRoomList(bool isForced = false)
    {
        if (_isRefreshing && !isForced) return;
        
        _isRefreshing = true;

        if (_networkManager == null)
        {
            ShowStatus("NetworkManager를 찾을 수 없습니다.", true);
            _isRefreshing = false;
            return;
        }

        if (!_networkManager.IsConnectedToServer)
        {
            ShowStatus("서버 연결이 끊어졌습니다. Title 씬으로 이동합니다.", true);
            _isRefreshing = false;
            SceneManager.LoadScene("Title");
            return;
        }
        
        string statusPrefix = isForced ? "강제 새로고침" : "방 목록 새로고침";
        ShowStatus($"{statusPrefix} 중... ({_refreshRetryCount + 1}/{MAX_REFRESH_RETRIES + 1})", false);
        
        try
        {
            await _networkManager.RefreshRoomList();
            
            // ✅ [개선] 타임아웃을 10초로 단축하고 재시도 로직 추가
            bool hasTimeout = await Task.WhenAny(
                Task.Delay(10000), // 10초 타임아웃
                WaitForRefreshComplete()
            ) == WaitForRefreshComplete();

            if (!hasTimeout)
            {
                Debug.LogWarning($"[LobbyUI] 방 목록 새로고침 타임아웃 ({_refreshRetryCount + 1}/{MAX_REFRESH_RETRIES + 1})");
                
                if (_refreshRetryCount < MAX_REFRESH_RETRIES)
                {
                    _refreshRetryCount++;
                    ShowStatus($"타임아웃 발생. 재시도 중... ({_refreshRetryCount}/{MAX_REFRESH_RETRIES})", false);
                    _isRefreshing = false;
                    
                    await UniTask.Delay(1000); // 1초 대기 후 재시도
                    await InternalRefreshRoomList(isForced);
                    return;
                }
                else
                {
                    _refreshRetryCount = 0;
                    _isRefreshing = false;
                    ShowStatus("새로고침 실패. R키를 눌러 다시 시도해주세요.", true);
                    
                    // 재시도 한계에 도달했을 때도 빈 목록이라도 UI 업데이트
                    OnRoomListUpdated(new List<SessionInfo>());
                }
            }
            else
            {
                _refreshRetryCount = 0; // 성공 시 재시도 카운트 리셋
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"방 목록 새로고침 오류: {e.Message}");
            _refreshRetryCount++;
            
            if (_refreshRetryCount <= MAX_REFRESH_RETRIES)
            {
                ShowStatus($"새로고침 오류. 재시도 중... ({_refreshRetryCount}/{MAX_REFRESH_RETRIES})", false);
                _isRefreshing = false;
                await UniTask.Delay(2000); // 2초 대기 후 재시도
                await InternalRefreshRoomList(isForced);
                return;
            }
            else
            {
                _refreshRetryCount = 0;
                _isRefreshing = false;
                ShowStatus("새로고침 실패. 네트워크 상태를 확인해주세요.", true);
                OnRoomListUpdated(new List<SessionInfo>());
            }
        }
    }
    
    // ✅ [추가] 새로고침 완료 대기 헬퍼
    private async Task WaitForRefreshComplete()
    {
        while (_isRefreshing)
        {
            await Task.Delay(100);
        }
    }

    // ✅ [개선] OnRoomListUpdated - 더 상세한 로그와 오류 처리
    private void OnRoomListUpdated(List<SessionInfo> rooms)
    {
        Debug.Log($"<color=cyan>[LobbyUI] 🎯 UI 업데이트 시작! 받은 방 개수: {rooms.Count}</color>");
        Debug.Log($"<color=cyan>[LobbyUI] 현재 _roomListItems 개수: {_roomListItems.Count}</color>");

        // 기존 방 아이템들 완전히 정리
        foreach (var item in _roomListItems)
        {
            if (item != null)
            {
                Debug.Log($"<color=red>[LobbyUI] 기존 방 아이템 삭제: {item.name}</color>");
                Destroy(item);
            }
        }
        _roomListItems.Clear();
        
        Debug.Log($"<color=green>[LobbyUI] ✅ 기존 방 아이템 모두 정리 완료</color>");
        
        // 프리팹 유효성 검사 강화
        if (_roomItemPrefab == null)
        {
            string criticalError = "❌ 치명적 오류: RoomItem Prefab이 할당되지 않았습니다!";
            Debug.LogError($"<color=red>[LobbyUI] {criticalError}</color>", this.gameObject);
            ShowStatus(criticalError, true);
            
            if (_noRoomsText != null)
            {
                _noRoomsText.text = criticalError;
                _noRoomsText.gameObject.SetActive(true);
            }
            _isRefreshing = false;
            return;
        }

        // _roomListContent 유효성 검사
        if (_roomListContent == null)
        {
            string criticalError = "❌ 치명적 오류: Room List Content가 할당되지 않았습니다!";
            Debug.LogError($"<color=red>[LobbyUI] {criticalError}</color>", this.gameObject);
            ShowStatus(criticalError, true);
            _isRefreshing = false;
            return;
        }

        Debug.Log($"<color=blue>[LobbyUI] 📋 프리팹 검증 완료. Parent: {_roomListContent.name}</color>");

        // ✅ [개선] 방 목록 표시 로직 - 더 자세한 안내
        if (rooms == null || rooms.Count == 0)
        {
            if (_noRoomsText != null)
            {
                _noRoomsText.text = $"현재 생성된 방이 없습니다.\n\n" +
                                  $"💡 해결 방법:\n" +
                                  $"1. 빌드 플레이어에서 방을 생성해주세요\n" +
                                  $"2. R키를 눌러 강제 새로고침 시도\n" +
                                  $"3. 빌드와 에디터가 같은 네트워크에 있는지 확인\n" +
                                  $"4. 방 이름을 직접 입력하여 연결 시도";
                _noRoomsText.gameObject.SetActive(true);
            }
            ShowStatus("생성된 방이 없습니다.", false);
            Debug.Log("<color=yellow>[LobbyUI] 📭 방 목록이 비어있습니다.</color>");
        }
        else
        {
            if (_noRoomsText != null)
                _noRoomsText.gameObject.SetActive(false);
            ShowStatus($"{rooms.Count}개의 방을 찾았습니다.", false);
            Debug.Log($"<color=green>[LobbyUI] 📋 {rooms.Count}개의 방 발견!</color>");
        }

        // RoomItem 생성 로직 강화
        Debug.Log($"<color=yellow>[LobbyUI] 🏗️ {rooms.Count}개의 RoomItem 생성 시작</color>");

        for (int i = 0; i < rooms.Count; i++)
        {
            var room = rooms[i];
            Debug.Log($"<color=lime>[LobbyUI] [{i}] 방 생성 중: '{room.Name}' ({room.PlayerCount}/{room.MaxPlayers})</color>");

            try
            {
                // Instantiate 전에 다시 한번 검증
                if (_roomItemPrefab == null || _roomListContent == null)
                {
                    Debug.LogError($"<color=red>[LobbyUI] [{i}] 생성 중 프리팹 또는 부모가 null이 되었습니다!</color>");
                    break;
                }

                GameObject roomItem = Instantiate(_roomItemPrefab, _roomListContent);
                
                if (roomItem == null)
                {
                    Debug.LogError($"<color=red>[LobbyUI] [{i}] Instantiate 실패! 결과가 null입니다.</color>");
                    continue;
                }

                // 생성된 오브젝트 이름 설정
                roomItem.name = $"RoomItem_{room.Name}_{i}";
                
                Debug.Log($"<color=lime>[LobbyUI] [{i}] ✅ GameObject 생성 성공: {roomItem.name}</color>");

                // _roomListItems에 추가
                _roomListItems.Add(roomItem);
                Debug.Log($"<color=cyan>[LobbyUI] [{i}] ✅ _roomListItems에 추가 완료. 총 개수: {_roomListItems.Count}</color>");

                // RoomItem 컴포넌트 설정
                RoomItem roomItemComponent = roomItem.GetComponent<RoomItem>();
                if (roomItemComponent != null)
                {
                    roomItemComponent.Setup(room.Name, room.PlayerCount, room.MaxPlayers, () => JoinRoom(room.Name));
                    Debug.Log($"<color=lime>[LobbyUI] [{i}] ✅ RoomItem 컴포넌트 설정 완료</color>");
                }
                else
                {
                    Debug.LogError($"<color=red>[LobbyUI] [{i}] RoomItem 컴포넌트를 찾을 수 없습니다!</color>");
                    
                    // RoomItem 컴포넌트가 없어도 기본적인 텍스트 표시는 시도
                    var texts = roomItem.GetComponentsInChildren<TMP_Text>();
                    if (texts.Length >= 2)
                    {
                        texts[0].text = room.Name;
                        texts[1].text = $"{room.PlayerCount}/{room.MaxPlayers}";
                        Debug.Log($"<color=yellow>[LobbyUI] [{i}] 기본 텍스트 설정 완료</color>");
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"<color=red>[LobbyUI] [{i}] 방 아이템 생성 오류: {e.Message}</color>");
                Debug.LogError($"<color=red>[LobbyUI] [{i}] 스택 트레이스: {e.StackTrace}</color>");
            }
        }
        
        Debug.Log($"<color=green>[LobbyUI] 🎉 UI 업데이트 완료! 최종 _roomListItems 개수: {_roomListItems.Count}</color>");
        
        _isRefreshing = false;
    }

    private void OnConnectionStatusChanged(bool connected)
    {
        if (!connected)
        {
            ShowStatus("서버 연결이 끊어졌습니다.", true);
            _isRefreshing = false;
        }
    }

    private void OnErrorOccurred(string error)
    {
        ShowStatus(error, true);
        ShowLoading(false);
        _isRefreshing = false;
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
    
    // 직접 연결 기능들
    private void ShowDirectJoinPanel()
    {
        if (_directJoinPanel != null)
        {
            _directJoinPanel.SetActive(true);
            ShowStatus("방 이름을 입력하여 직접 연결하세요. (예: TestRoom123)", false);
            
            // 입력 필드에 포커스
            if (_directJoinInput != null)
            {
                _directJoinInput.text = "TestRoom123"; // ✅ 기본값으로 TestRoom123 설정
                _directJoinInput.Select();
                _directJoinInput.ActivateInputField();
            }
        }
    }
    
    private void HideDirectJoinPanel()
    {
        if (_directJoinPanel != null)
        {
            _directJoinPanel.SetActive(false);
            ShowStatus("직접 연결 취소됨.", false);
        }
    }
    
    private async void DirectJoinRoom()
    {
        if (_directJoinInput == null)
        {
            ShowStatus("입력 필드를 찾을 수 없습니다.", true);
            return;
        }
        
        string roomName = _directJoinInput.text.Trim();
        
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
        
        Debug.Log($"<color=cyan>[LobbyUI] 🎯 직접 연결 시도: '{roomName}'</color>");
        
        ShowLoading(true);
        
        // ✅ [개선] 방 존재 여부를 여러 번 확인하여 정확도 향상
        ShowStatus($"'{roomName}' 방 존재 여부 확인 중... (최대 3회 시도)", false);
        
        bool roomExists = false;
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            ShowStatus($"'{roomName}' 방 검색 중... ({attempt}/3)", false);
            roomExists = await _networkManager.CheckRoomExists(roomName);
            
            if (roomExists)
            {
                Debug.Log($"<color=green>[LobbyUI] ✅ {attempt}번째 시도에서 방 발견!</color>");
                break;
            }
            
            if (attempt < 3)
            {
                await UniTask.Delay(2000); // 2초 대기 후 재시도
            }
        }
        
        if (!roomExists)
        {
            ShowLoading(false);
            ShowStatus($"'{roomName}' 방을 찾을 수 없습니다.\n" +
                      $"확인사항:\n" +
                      $"1. 빌드에서 해당 이름의 방을 생성했는지 확인\n" +
                      $"2. 빌드와 에디터가 같은 네트워크에 연결되어 있는지 확인\n" +
                      $"3. 방 이름 철자가 정확한지 확인", true);
            Debug.LogWarning($"<color=orange>[LobbyUI] ⚠️ 3회 시도 후에도 방 찾을 수 없음: '{roomName}'</color>");
            
            if (_directJoinInput != null)
            {
                _directJoinInput.Select();
                _directJoinInput.ActivateInputField();
            }
            return;
        }
        
        // 방이 존재하면 연결 시도
        ShowStatus($"'{roomName}' 방 발견! 연결 시도 중...", false);
        bool success = await _networkManager.JoinRoom(roomName, "JoinLobby");
        
        if (success)
        {
            ShowStatus($"'{roomName}' 방 연결 성공! JoinLobby로 이동합니다.", false);
            Debug.Log($"<color=green>[LobbyUI] ✅ 직접 연결 성공: '{roomName}'</color>");
        }
        else
        {
            ShowLoading(false);
            ShowStatus($"'{roomName}' 방 연결에 실패했습니다.\n" +
                      $"가능한 원인:\n" +
                      $"1. 방이 가득 참\n" +
                      $"2. 네트워크 오류\n" +
                      $"3. 방이 게임 중", true);
            Debug.LogError($"<color=red>[LobbyUI] ❌ 직접 연결 실패: '{roomName}'</color>");
            
            if (_directJoinInput != null)
            {
                _directJoinInput.Select();
                _directJoinInput.ActivateInputField();
            }
        }
    }
}
