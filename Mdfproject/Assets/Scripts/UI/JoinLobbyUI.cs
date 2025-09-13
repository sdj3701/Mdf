// Assets/Scripts/UI/JoinLobbyUI.cs
// ✅ [추가] 방 목록 조회 불가 상황에 대한 UI 개선

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;

public class JoinLobbyUI : MonoBehaviour
{
    public static JoinLobbyUI Instance { get; private set; }
    
    [Header("Room Info")]
    [SerializeField] private TMP_Text _roomNameText;
    [SerializeField] private TMP_Text _playerCountText;
    [SerializeField] private TMP_Text _hostIndicatorText;
    
    [Header("Player List")]
    [SerializeField] private Transform _playerListContent;
    [SerializeField] private GameObject _playerItemPrefab;
    [SerializeField] private TMP_Text _player1NameText;
    [SerializeField] private TMP_Text _player2NameText;
    [SerializeField] private Image _player1ReadyImage;
    [SerializeField] private Image _player2ReadyImage;
    
    [Header("Buttons")]
    [SerializeField] private Button _gameStartButton;
    [SerializeField] private Button _readyButton;
    [SerializeField] private Button _leaveRoomButton;
    
    [Header("Status")]
    [SerializeField] private TMP_Text _statusText;
    [SerializeField] private GameObject _waitingPanel;
    [SerializeField] private TMP_Text _waitingText;
    
    // ✅ [새로 추가] 다른 방 목록 관련 UI
    [Header("Other Rooms Info")]
    [SerializeField] private GameObject _otherRoomsPanel;
    [SerializeField] private TMP_Text _otherRoomsInfoText;
    [SerializeField] private Button _viewOtherRoomsButton;
    
    [Header("Ready Status Colors")]
    [SerializeField] private Color _readyColor = Color.green;
    [SerializeField] private Color _notReadyColor = Color.red;
    
    private NetworkManager _networkManager;
    private bool _isReady = false;
    private float _updateInterval = 0.5f;
    private float _lastUpdateTime;
    
    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }
    
    private void Start()
    {
        _networkManager = NetworkManager.Instance;
        
        if (_networkManager == null)
        {
            Debug.LogError("NetworkManager not found! Returning to MatchingLobby...");
            SceneManager.LoadScene("MatchingLobby");
            return;
        }
        
        if (!_networkManager.IsConnected)
        {
            Debug.LogError("Not connected to room! Returning to MatchingLobby...");
            SceneManager.LoadScene("MatchingLobby");
            return;
        }
        
        InitializeUI();
        SubscribeToEvents();
        UpdateRoomInfo();
    }
    
    private void InitializeUI()
    {
        // 방 정보 표시
        if (_roomNameText != null)
        {
            _roomNameText.text = $"방: {_networkManager.CurrentRoomName}";
        }
        
        // 호스트/클라이언트 표시
        if (_hostIndicatorText != null)
        {
            _hostIndicatorText.text = _networkManager.IsHost ? "[호스트]" : "[클라이언트]";
            _hostIndicatorText.color = _networkManager.IsHost ? Color.yellow : Color.white;
        }
        
        // 버튼 이벤트 연결
        if (_gameStartButton != null)
        {
            _gameStartButton.onClick.AddListener(StartGame);
            _gameStartButton.gameObject.SetActive(_networkManager.IsHost);
            _gameStartButton.interactable = false;
        }
        
        if (_readyButton != null)
        {
            _readyButton.onClick.AddListener(ToggleReady);
            _readyButton.gameObject.SetActive(!_networkManager.IsHost);
        }
        
        if (_leaveRoomButton != null)
        {
            _leaveRoomButton.onClick.AddListener(LeaveRoom);
        }
        
        // ✅ [새로 추가] 다른 방 목록 관련 UI 초기화
        InitializeOtherRoomsUI();
        
        ShowWaitingPanel(true, "다른 플레이어를 기다리는 중...");
    }
    
    /// <summary>
    /// ✅ [새로 추가] 다른 방 목록 관련 UI 초기화
    /// </summary>
    private void InitializeOtherRoomsUI()
    {
        // 다른 방 목록 조회 불가 상황 처리
        bool canViewOtherRooms = _networkManager.CanRefreshRoomList;
        
        if (_otherRoomsPanel != null)
        {
            _otherRoomsPanel.SetActive(!canViewOtherRooms);
        }
        
        if (_otherRoomsInfoText != null)
        {
            if (canViewOtherRooms)
            {
                _otherRoomsInfoText.text = "";
            }
            else
            {
                _otherRoomsInfoText.text = 
                    "🎮 현재 방에 참여 중입니다.\n" +
                    "다른 방 목록을 보려면 방을 나가고\n" +
                    "매칭 로비로 돌아가세요.";
                _otherRoomsInfoText.color = Color.yellow;
            }
        }
        
        if (_viewOtherRoomsButton != null)
        {
            _viewOtherRoomsButton.onClick.AddListener(GoToMatchingLobby);
            _viewOtherRoomsButton.gameObject.SetActive(!canViewOtherRooms);
            
            // 버튼 텍스트 설정
            TMP_Text buttonText = _viewOtherRoomsButton.GetComponentInChildren<TMP_Text>();
            if (buttonText != null)
            {
                buttonText.text = "매칭 로비로 가기";
            }
        }
    }
    
    /// <summary>
    /// ✅ [새로 추가] 매칭 로비로 이동 (방 나가기 없이)
    /// </summary>
    private void GoToMatchingLobby()
    {
        ShowStatus("매칭 로비로 이동 중...", false);
        
        // 확인 대화상자 표시 (선택사항)
        if (ConfirmLeaveRoom())
        {
            LeaveRoom();
        }
    }
    
    /// <summary>
    /// ✅ [새로 추가] 방 나가기 확인
    /// </summary>
    private bool ConfirmLeaveRoom()
    {
        // 간단한 확인 - 실제로는 더 나은 UI로 대체 가능
        return true; // 또는 확인 다이얼로그 구현
    }
    
    private void SubscribeToEvents()
    {
        if (_networkManager != null)
        {
            _networkManager.OnRoomPlayerCountChanged += OnPlayerCountChanged;
            _networkManager.OnConnectionStatusChanged += OnConnectionStatusChanged;
            _networkManager.OnErrorOccurred += OnErrorOccurred;
        }
    }
    
    public void OnPlayerReadyChanged(int playerNumber, bool isReady)
    {
        Debug.Log($"플레이어 {playerNumber} 준비 상태: {isReady}");
        
        if (playerNumber == 0 && _player1ReadyImage != null)
        {
            _player1ReadyImage.color = isReady ? _readyColor : _notReadyColor;
        }
        else if (playerNumber == 1 && _player2ReadyImage != null)
        {
            _player2ReadyImage.color = isReady ? _readyColor : _notReadyColor;
        }
        
        if (_networkManager != null && _networkManager.IsHost)
        {
            CheckAllPlayersReady();
        }
    }
    
    private void CheckAllPlayersReady()
    {
        NetworkPlayer[] players = FindObjectsOfType<NetworkPlayer>();
        bool allReady = players.Length == _networkManager.MaxPlayerCount;
        
        foreach (var player in players)
        {
            if (!player.IsReady && player.PlayerNumber != 0)
            {
                allReady = false;
                break;
            }
        }
        
        if (_gameStartButton != null)
        {
            _gameStartButton.interactable = allReady && players.Length == _networkManager.MaxPlayerCount;
        }
        
        if (allReady)
        {
            ShowStatus("모든 플레이어가 준비되었습니다!", false);
        }
    }
    
    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
        
        if (_networkManager != null)
        {
            _networkManager.OnRoomPlayerCountChanged -= OnPlayerCountChanged;
            _networkManager.OnConnectionStatusChanged -= OnConnectionStatusChanged;
            _networkManager.OnErrorOccurred -= OnErrorOccurred;
        }
    }
    
    private void Update()
    {
        if (Time.time - _lastUpdateTime > _updateInterval)
        {
            UpdateRoomInfo();
            UpdateOtherRoomsStatus(); // ✅ [추가]
            _lastUpdateTime = Time.time;
        }
    }
    
    /// <summary>
    /// ✅ [새로 추가] 다른 방 목록 상태 업데이트
    /// </summary>
    private void UpdateOtherRoomsStatus()
    {
        bool canViewOtherRooms = _networkManager.CanRefreshRoomList;
        
        if (_otherRoomsPanel != null && _otherRoomsPanel.activeSelf == canViewOtherRooms)
        {
            _otherRoomsPanel.SetActive(!canViewOtherRooms);
        }
        
        if (_viewOtherRoomsButton != null && _viewOtherRoomsButton.gameObject.activeSelf == canViewOtherRooms)
        {
            _viewOtherRoomsButton.gameObject.SetActive(!canViewOtherRooms);
        }
    }
    
    private void UpdateRoomInfo()
    {
        if (_networkManager == null || !_networkManager.IsConnected)
            return;
        
        int currentPlayers = _networkManager.CurrentPlayerCount;
        int maxPlayers = _networkManager.MaxPlayerCount;
        
        if (_playerCountText != null)
        {
            _playerCountText.text = $"플레이어: {currentPlayers}/{maxPlayers}";
            _playerCountText.color = currentPlayers == maxPlayers ? _readyColor : Color.white;
        }
        
        UpdatePlayerList(currentPlayers);
        
        if (currentPlayers == maxPlayers)
        {
            ShowWaitingPanel(false);
            
            if (_networkManager.IsHost)
            {
                if (_gameStartButton != null)
                {
                    _gameStartButton.interactable = true;
                }
                ShowStatus("모든 플레이어가 준비되었습니다. 게임을 시작할 수 있습니다!", false);
            }
            else
            {
                ShowStatus("준비 버튼을 눌러주세요.", false);
            }
        }
        else
        {
            ShowWaitingPanel(true, $"플레이어를 기다리는 중... ({currentPlayers}/{maxPlayers})");
            
            if (_networkManager.IsHost && _gameStartButton != null)
            {
                _gameStartButton.interactable = false;
            }
        }
    }
    
    private void UpdatePlayerList(int playerCount)
    {
        if (_player1NameText != null)
        {
            if (playerCount >= 1)
            {
                _player1NameText.text = _networkManager.IsHost ? 
                    $"{_networkManager.PlayerNickname} (나)" : 
                    "호스트";
                _player1NameText.color = Color.white;
            }
            else
            {
                _player1NameText.text = "대기 중...";
                _player1NameText.color = Color.gray;
            }
        }
        
        if (_player2NameText != null)
        {
            if (playerCount >= 2)
            {
                _player2NameText.text = !_networkManager.IsHost ? 
                    $"{_networkManager.PlayerNickname} (나)" : 
                    "클라이언트";
                _player2NameText.color = Color.white;
            }
            else
            {
                _player2NameText.text = "대기 중...";
                _player2NameText.color = Color.gray;
            }
        }
        
        if (_player1ReadyImage != null)
        {
            _player1ReadyImage.color = playerCount >= 1 ? _readyColor : _notReadyColor;
        }
        
        if (_player2ReadyImage != null)
        {
            _player2ReadyImage.color = playerCount >= 2 ? _readyColor : _notReadyColor;
        }
    }
    
    private void ToggleReady()
    {
        _isReady = !_isReady;
        
        if (_readyButton != null)
        {
            TMP_Text buttonText = _readyButton.GetComponentInChildren<TMP_Text>();
            if (buttonText != null)
            {
                buttonText.text = _isReady ? "준비 취소" : "준비";
            }
            
            Image buttonImage = _readyButton.GetComponent<Image>();
            if (buttonImage != null)
            {
                buttonImage.color = _isReady ? _readyColor : Color.white;
            }
        }
        
        ShowStatus(_isReady ? "준비 완료!" : "준비 취소됨", false);
        
        if (NetworkPlayer.LocalPlayer != null)
        {
            NetworkPlayer.LocalPlayer.RPC_SetReady(_isReady);
        }
    }
    
    private void StartGame()
    {
        if (!_networkManager.IsHost)
        {
            ShowStatus("호스트만 게임을 시작할 수 있습니다.", true);
            return;
        }
        
        if (_networkManager.CurrentPlayerCount != _networkManager.MaxPlayerCount)
        {
            ShowStatus("모든 플레이어가 입장해야 시작할 수 있습니다.", true);
            return;
        }
        
        ShowStatus("게임 시작!", false);
        ShowWaitingPanel(true, "게임 시작 중...");
        
        _networkManager.StartGame();
    }
    
    private void LeaveRoom()
    {
        ShowStatus("방을 나가는 중...", false);
        
        if (_networkManager != null)
        {
            // ✅ [수정] 방에서 나갈 때 MatchingLobby로 돌아가도록 개선된 Disconnect 호출
            _networkManager.Disconnect();
        }
        
        // Disconnect 메서드에서 자동으로 MatchingLobby로 이동하므로 별도 LoadScene 불필요
    }
    
    private void OnPlayerCountChanged(int playerCount)
    {
        Debug.Log($"Player count changed: {playerCount}");
        UpdateRoomInfo();
    }
    
    private void OnConnectionStatusChanged(bool connected)
    {
        if (!connected)
        {
            ShowStatus("연결이 끊어졌습니다. MatchingLobby로 돌아갑니다.", true);
            StartCoroutine(ReturnToMatchingLobby(2f));
        }
    }
    
    private void OnErrorOccurred(string error)
    {
        ShowStatus(error, true);
    }
    
    private void ShowStatus(string message, bool isError)
    {
        if (_statusText != null)
        {
            _statusText.text = message;
            _statusText.color = isError ? Color.red : Color.white;
        }
        
        Debug.Log($"[JoinLobby] {message}");
    }
    
    private void ShowWaitingPanel(bool show, string message = "")
    {
        if (_waitingPanel != null)
        {
            _waitingPanel.SetActive(show);
        }
        
        if (_waitingText != null && !string.IsNullOrEmpty(message))
        {
            _waitingText.text = message;
        }
    }
    
    private IEnumerator ReturnToMatchingLobby(float delay)
    {
        yield return new WaitForSeconds(delay);
        SceneManager.LoadScene("MatchingLobby");
    }
}