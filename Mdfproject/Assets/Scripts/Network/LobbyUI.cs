using System.Collections.Generic;
using Fusion;
using GameCore.Enums;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class LobbyUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private GameObject _roomListPanel;
    [SerializeField] private GameObject _createRoomPanel;
    [SerializeField] private Button _createRoomButton;
    [SerializeField] private Button _refreshButton;
    [SerializeField] private Button _backToTitleButton;
    [SerializeField] private TMP_InputField _roomNameInput;
    [SerializeField] private TMP_InputField _directJoinInput;
    [SerializeField] private Button _confirmCreateButton;
    [SerializeField] private Button _cancelCreateButton;
    [SerializeField] private Button _directJoinButtonButton;
    [SerializeField] private Transform _roomListContent;
    [SerializeField] private GameObject _roomItemPrefab;
    [SerializeField] private TMP_Text _noRoomsText;

    [Header("Direct Join - Room Not Found Panel")]
    [SerializeField] private GameObject _roomNotFoundPanel;
    [SerializeField] private Button _roomNotFoundCloseButton;

    public GameObject _networkJoinPanel;

    private NetworkManager _networkManager;

    private void Start()
    {
        _networkManager = NetworkManager.Instance;

        if (_networkManager == null)
        {
            Debug.LogError("[LobbyUI] NetworkManager가 없습니다. Title 화면으로 돌아갑니다.");
            SceneManager.LoadScene(SceneDefine.Title);
            return;
        }

        if (_networkManager.State == ConnectionState.Disconnected)
        {
            Debug.Log("[LobbyUI] Disconnected 상태 감지. 로비에 다시 참여합니다.");
            _networkManager.JoinLobby();
        }

        ALLButtonListener();
        RefreshNetworkJoinPanel();
        UpdateRoomListUI();
    }

    private void OnEnable()
    {
        _networkManager = NetworkManager.Instance;
        NetworkManager.OnSessionListUpdatedEvent += OnSessionListUpdatedHandler;
        NetworkManager.OnNetworkUiBlockChanged += OnNetworkUiBlockChanged;
        RefreshNetworkJoinPanel();
    }

    private void OnDisable()
    {
        NetworkManager.OnSessionListUpdatedEvent -= OnSessionListUpdatedHandler;
        NetworkManager.OnNetworkUiBlockChanged -= OnNetworkUiBlockChanged;
        HideNetworkJoinPanel();
    }

    private void OnSessionListUpdatedHandler(List<SessionInfo> sessionList)
    {
        UpdateRoomListUI();
    }

    private void OnNetworkUiBlockChanged()
    {
        RefreshNetworkJoinPanel();
    }

    private void RefreshNetworkJoinPanel()
    {
        if (_networkJoinPanel == null)
        {
            return;
        }

        bool shouldShow = _networkManager != null && _networkManager.IsNetworkUiBlocked;
        if (_networkJoinPanel.activeSelf != shouldShow)
        {
            _networkJoinPanel.SetActive(shouldShow);
        }
    }

    private void HideNetworkJoinPanel()
    {
        if (_networkJoinPanel != null && _networkJoinPanel.activeSelf)
        {
            _networkJoinPanel.SetActive(false);
        }
    }

    private void ALLButtonListener()
    {
        _createRoomButton.onClick.AddListener(() =>
        {
            _createRoomPanel.SetActive(true);
            _roomListPanel.SetActive(false);
        });

        _confirmCreateButton.onClick.AddListener(() =>
        {
            string inputName = _roomNameInput != null ? _roomNameInput.text : null;
            _networkManager.StartGame(GameMode.Host, inputName, SceneDefine.JoinLobby);
        });

        _refreshButton.onClick.AddListener(UpdateRoomListUI);

        _cancelCreateButton.onClick.AddListener(() =>
        {
            _createRoomPanel.SetActive(false);
            _roomListPanel.SetActive(true);
        });

        _backToTitleButton.onClick.AddListener(() =>
        {
            Debug.Log("[LobbyUI] 타이틀 화면으로 돌아갑니다.");
            _networkManager.LeaveAndLoad(SceneDefine.Title);
        });

        _directJoinButtonButton.onClick.AddListener(() =>
        {
            string roomName = _directJoinInput?.text?.Trim();

            if (string.IsNullOrWhiteSpace(roomName))
            {
                Debug.LogWarning("[LobbyUI] 방 이름을 입력해주세요.");
                return;
            }

            SessionInfo targetSession = _networkManager._sessionList.Find(
                session => session.Name == roomName && session.IsOpen && session.IsVisible);

            if (targetSession != null)
            {
                Debug.Log($"[LobbyUI] '{roomName}' 방에 참가합니다.");
                _networkManager.StartGame(GameMode.Client, roomName, SceneDefine.JoinLobby);
            }
            else if (_roomNotFoundPanel != null)
            {
                Debug.LogWarning($"[LobbyUI] '{roomName}' 방을 찾을 수 없습니다.");
                _roomNotFoundPanel.SetActive(true);
            }
        });

        if (_roomNotFoundCloseButton != null)
        {
            _roomNotFoundCloseButton.onClick.AddListener(() =>
            {
                if (_roomNotFoundPanel != null)
                {
                    _roomNotFoundPanel.SetActive(false);
                }
            });
        }
    }

    private void UpdateRoomListUI()
    {
        if (_networkManager == null || _roomListContent == null)
        {
            return;
        }

        foreach (Transform child in _roomListContent)
        {
            Destroy(child.gameObject);
        }

        List<SessionInfo> sessions = _networkManager._sessionList;

        if (_noRoomsText != null)
        {
            _noRoomsText.gameObject.SetActive(sessions.Count == 0);
        }

        if (sessions.Count == 0)
        {
            return;
        }

        foreach (var session in sessions)
        {
            if (!session.IsOpen || !session.IsVisible)
            {
                continue;
            }

            GameObject itemGO = Instantiate(_roomItemPrefab, _roomListContent);
            RoomItem roomItem = itemGO.GetComponent<RoomItem>();
            SessionInfo currentSession = session;

            roomItem.Setup(
                currentSession.Name,
                currentSession.PlayerCount,
                currentSession.MaxPlayers,
                () => _networkManager.StartGame(GameMode.Client, currentSession.Name, SceneDefine.JoinLobby));
        }
    }
}
