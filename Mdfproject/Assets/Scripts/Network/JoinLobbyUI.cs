// Assets/Scripts/UI/JoinLobbyUI.cs
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;
using System.Linq;
using Fusion;

public class JoinLobbyUI : MonoBehaviour
{
    public static JoinLobbyUI Instance { get; private set; }

    [Header("Room Info")]
    [SerializeField] private TMP_Text _roomNameText;
    [SerializeField] private TMP_Text _playerCountText;
    [SerializeField] private TMP_Text _hostIndicatorText;

    [Header("Player List")]
    [SerializeField] private TMP_Text _player1NameText;
    [SerializeField] private TMP_Text _player2NameText;
    [SerializeField] private Image _player1ReadyImage;
    [SerializeField] private Image _player2ReadyImage;

    [Header("Buttons")]
    [SerializeField] private Button _gameStartButton;
    [SerializeField] private Button _readyButton;
    [SerializeField] private Button _leaveRoomButton;

    private NetworkManager _networkManager;
    private NetworkPlayer _localPlayer;

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
            Debug.LogError("[JoinLobbyUI] NetworkManager를 찾을 수 없습니다. Title 씬으로 돌아갑니다.");
            SceneManager.LoadScene("Title");
            return;
        }
        
        ALLButtonListener();
        UpdatePlayerList();
    }
    
    private void OnEnable()
    {
        // Start()가 실행된 후 _networkManager가 할당되었을 것이므로, 여기서도 호출해줍니다.
        if (_networkManager != null)
        {
            UpdatePlayerList();
        }
    }

    private void ALLButtonListener()
    {
        // _gameStartButton의 리스너 부분을 아래 코드로 변경합니다.
        _gameStartButton.onClick.AddListener(() =>
        {
            // 게임 시작은 호스트만 할 수 있습니다.
            if (_networkManager != null && _networkManager._runner != null && _networkManager._runner.IsServer)
            {
                Debug.Log("호스트가 게임 시작을 명령합니다. 모든 클라이언트의 씬을 'Game'으로 전환합니다.");

                // Fusion의 네트워크 씬 로드 기능을 사용합니다.
                // 첫 번째 인자: 로드할 씬의 참조 (SceneRef)
                // 두 번째 인자: 씬 로드 방식 (Single은 기존 씬을 닫고 새 씬을 염)
                _networkManager._runner.LoadScene(SceneRef.FromIndex(SceneUtility.GetBuildIndexByScenePath("Assets/Scenes/Game.unity")), LoadSceneMode.Single);
            }
            else
            {
                Debug.LogWarning("호스트가 아니므로 게임을 시작할 수 없습니다.");
            }
        });

        _readyButton.onClick.AddListener(() =>
        {
            if (_localPlayer != null)
            {
                _localPlayer.RPC_ToggleReady();
            }
            else
            {
                Debug.LogWarning("로컬 플레이어 객체를 찾을 수 없어 준비 상태를 변경할 수 없습니다.");
            }
        });
        
        _leaveRoomButton.onClick.AddListener(() =>
        {
             if (_networkManager != null && _networkManager._runner != null)
             {
                 _networkManager._runner.Shutdown();
                 SceneManager.LoadScene("MatchingLobby");
             }
        });
    }

    public void UpdatePlayerList()
    {
        if (this == null) return;

        // [수정] UI 컴포넌트들이 할당되지 않았다면 오류를 출력하고 함수를 종료하는 안전장치
        if (_player1NameText == null || _player1ReadyImage == null || _player2NameText == null || _player2ReadyImage == null)
        {
            Debug.LogError("[JoinLobbyUI] Player List UI 요소 중 일부가 Inspector에 할당되지 않았습니다!");
            return;
        }

        List<NetworkPlayer> players = FindObjectsOfType<NetworkPlayer>().ToList();

        if (_localPlayer == null)
        {
            _localPlayer = players.FirstOrDefault(p => p.HasInputAuthority);
        }
        
        _player1NameText.text = "Waiting for Player...";
        _player1ReadyImage.color = Color.gray;
        _player2NameText.text = "Waiting for Player...";
        _player2ReadyImage.color = Color.gray;

        if (players.Count > 0)
        {
            var player1 = players[0];
            _player1NameText.text = player1.Nickname.ToString();
            _player1ReadyImage.color = player1.IsReady ? Color.green : Color.red;
        }

        if (players.Count > 1)
        {
            var player2 = players[1];
            _player2NameText.text = player2.Nickname.ToString();
            _player2ReadyImage.color = player2.IsReady ? Color.green : Color.red;
        }

        // [수정] _runner와 SessionInfo가 모두 유효한지 확인하는 이중 안전장치 추가
        if (_networkManager != null && _networkManager._runner != null && _networkManager._runner.SessionInfo != null)
        {
            // [수정] 각 UI 요소 사용 전 null 체크 추가
            if(_roomNameText) _roomNameText.text = _networkManager._runner.SessionInfo.Name;
            if(_playerCountText) _playerCountText.text = $"{players.Count} / {_networkManager._runner.SessionInfo.MaxPlayers}";
            if(_hostIndicatorText) _hostIndicatorText.text = _networkManager._runner.IsServer ? "You are the Host" : "You are a Client";
            
            if (_gameStartButton != null)
            {
                bool allReady = players.Count > 0 && players.All(p => p.IsReady);
                // 게임 시작 버튼은 호스트만, 그리고 모든 플레이어가 준비되었을 때만 활성화됩니다.
                _gameStartButton.interactable = _networkManager._runner.IsServer && allReady;
            }
            else
            {
                // 오류가 발생한 바로 그 지점입니다. _gameStartButton이 null일 경우 여기로 들어옵니다.
                Debug.LogError("[JoinLobbyUI] _gameStartButton이 Inspector에 할당되지 않았습니다!");
            }
        }
    }
}