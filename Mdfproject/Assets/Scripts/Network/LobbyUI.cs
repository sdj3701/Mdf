// Assets/Scripts/UI/LobbyUI.cs
// ✅ Fusion 2.X 완전 최적화 버전

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Fusion;
using UnityEngine.SceneManagement;
using GameCore.Enums;

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
            SceneManager.LoadScene("Title");
            return;
        }
        
        // 게임 종료 후 돌아온 경우: Disconnected 상태이면 로비에 다시 참여
        if (_networkManager.State == ConnectionState.Disconnected)
        {
            Debug.Log("[LobbyUI] Disconnected 상태 감지. 로비에 다시 참여합니다.");
            _networkManager.JoinLobby();
        }
        
        ALLButtonListener();
        // 로비에 처음 들어왔을 때 방 목록을 한번 갱신합니다.
        UpdateRoomListUI();
    }

    private void OnEnable()
    {
        // 세션 목록 업데이트 이벤트 구독
        NetworkManager.OnSessionListUpdatedEvent += OnSessionListUpdatedHandler;
        // 연결 상태 변경 이벤트 구독
        NetworkManager.OnStateChanged += OnConnectionStateChanged;
    }

    private void OnDisable()
    {
        // 이벤트 구독 해제 (메모리 누수 방지)
        NetworkManager.OnSessionListUpdatedEvent -= OnSessionListUpdatedHandler;
        NetworkManager.OnStateChanged -= OnConnectionStateChanged;
    }

    /// <summary>
    /// 세션 목록이 업데이트될 때 호출되는 핸들러
    /// </summary>
    private void OnSessionListUpdatedHandler(List<SessionInfo> sessionList)
    {
        // 방 목록 UI를 갱신합니다.
        UpdateRoomListUI();
    }

    /// <summary>
    /// 네트워크 연결 상태가 변경될 때 호출되는 핸들러
    /// </summary>
    private void OnConnectionStateChanged(ConnectionState state)
    {
        switch (state)
        {
            case ConnectionState.Connecting:
                // 연결 중 - 패널 표시
                if (_networkJoinPanel != null)
                    _networkJoinPanel.SetActive(true);
                break;
                
            case ConnectionState.InLobby:
                // 로비 입장 완료 - 패널 숨김
                if (_networkJoinPanel != null)
                    _networkJoinPanel.SetActive(false);
                break;
                
            case ConnectionState.Disconnected:
                // 연결 끊김 - 패널 숨김
                if (_networkJoinPanel != null)
                    _networkJoinPanel.SetActive(false);
                break;
        }
    }

    private void ALLButtonListener()
    {
        // Panel 활성화 비활성화
        _createRoomButton.onClick.AddListener(() =>
        {
            _createRoomPanel.SetActive(true);
            _networkJoinPanel.SetActive(true);
            _roomListPanel.SetActive(false);
        });
        // 방 생성 및 연결?
        _confirmCreateButton.onClick.AddListener(() =>
        {
            string inputName = _roomNameInput != null ? _roomNameInput.text : null;
            _networkManager.StartGame(GameMode.Host, inputName, "JoinLobby");
        });
        // ⭐ [핵심 수정] 방 갱신 버튼에 새로운 UI 업데이트 함수를 연결합니다.
        _refreshButton.onClick.AddListener(UpdateRoomListUI);
        // 방 생성 취소
        _cancelCreateButton.onClick.AddListener(() =>
        {
            _createRoomPanel.SetActive(false);
            _roomListPanel.SetActive(true);
        });
        // 타이틀로 돌아가기
        _backToTitleButton.onClick.AddListener(() =>
        {
            Debug.Log("[LobbyUI] 타이틀 화면으로 돌아갑니다.");
            _networkManager.LeaveAndLoad("Title");
        });

        // 직접 방 참여 (방 이름으로 검색하여 참여)
        _directJoinButtonButton.onClick.AddListener(() =>
        {
            string roomName = _directJoinInput?.text?.Trim();
            
            if (string.IsNullOrWhiteSpace(roomName))
            {
                Debug.LogWarning("[LobbyUI] 방 이름을 입력해주세요.");
                return;
            }
            
            // 방 목록에서 해당 이름의 방을 찾아서 참여
            SessionInfo targetSession = _networkManager._sessionList.Find(
                session => session.Name == roomName && session.IsOpen && session.IsVisible
            );
            
            if (targetSession != null)
            {
                Debug.Log($"[LobbyUI] '{roomName}' 방에 참여합니다.");
                _networkManager.StartGame(GameMode.Client, roomName, "JoinLobby");
            }
            else
            {
                Debug.LogWarning($"[LobbyUI] '{roomName}' 방을 찾을 수 없습니다.");
                // 방을 찾을 수 없을 때 알림 패널 표시
                if (_roomNotFoundPanel != null)
                {
                    _roomNotFoundPanel.SetActive(true);
                }
            }
        });

        // 방 못 찾음 패널 닫기 버튼
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

    /// <summary>
    /// 방 목록 UI를 최신 정보로 갱신합니다.
    /// </summary>
    private void UpdateRoomListUI()
    {
        // 1. 기존에 생성된 방 목록 아이템들을 모두 삭제합니다.
        foreach (Transform child in _roomListContent)
        {
            Destroy(child.gameObject);
        }

        // 2. 네트워크 매니저로부터 현재 세션 목록을 가져옵니다.
        List<SessionInfo> sessions = _networkManager._sessionList;

        // 3. 방이 하나도 없으면 "No Rooms" 텍스트를 표시하고 함수를 종료합니다.
        _noRoomsText.gameObject.SetActive(sessions.Count == 0);
        if (sessions.Count == 0)
        {
            return;
        }

        // 4. 각 세션 정보에 대해 RoomItem 프리팹을 생성하고 설정합니다.
        foreach (var session in sessions)
        {
            // 이미 닫혔거나 보이지 않는 방은 목록에 표시하지 않습니다.
            if (!session.IsOpen || !session.IsVisible) continue;

            // RoomItem 프리팹을 _roomListContent 자식으로 생성합니다.
            GameObject itemGO = Instantiate(_roomItemPrefab, _roomListContent);
            RoomItem roomItem = itemGO.GetComponent<RoomItem>();
            
            // 클로저 문제를 피하기 위해 현재 세션을 지역 변수에 복사합니다.
            SessionInfo currentSession = session;

            // RoomItem의 Setup 함수를 호출하여 UI를 설정하고,
            // Join 버튼을 눌렀을 때 실행될 행동을 람다식으로 전달합니다.
            roomItem.Setup(
                currentSession.Name,
                currentSession.PlayerCount,
                currentSession.MaxPlayers,
                () => {
                    // 이 RoomItem의 Join 버튼을 누르면 해당 방으로 참가를 시도합니다.
                    _networkManager.StartGame(GameMode.Client, currentSession.Name, "JoinLobby");
                }
            );
        }
    }

}