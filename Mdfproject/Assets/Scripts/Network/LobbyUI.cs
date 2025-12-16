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
        ALLButtonListener();
        // 로비에 처음 들어왔을 때 방 목록을 한번 갱신합니다.
        UpdateRoomListUI();

    }

    private void ALLButtonListener()
    {
        // Panel 활성화 비활성화
        _createRoomButton.onClick.AddListener(() =>
        {
            _createRoomPanel.SetActive(true);
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
            Debug.Log("추후 타이틀로 돌아가기 기능 구현하기");
        });
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