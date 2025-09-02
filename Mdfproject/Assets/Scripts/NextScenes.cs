using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class NextScenes : BaseButton
{
    [Header("Title Login UI")]
    [SerializeField] private TMP_InputField _nicknameInput;
    [SerializeField] private TMP_InputField _passwordInput;
    [SerializeField] private Button _connectButton;
    [SerializeField] private TMP_Text _statusText;
    [SerializeField] private GameObject _loadingPanel;
    [SerializeField] private GameObject loginUI;
    
    private NetworkManager _networkManager;
    
    protected override void Start()
    {
        base.Start();
        DontDestroyOnLoad(this.gameObject);
        
        // NetworkManager 찾기 또는 생성
        _networkManager = NetworkManager.Instance;
        if (_networkManager == null)
        {
            GameObject networkObj = new GameObject("NetworkManager");
            _networkManager = networkObj.AddComponent<NetworkManager>();
        }
        
        // Title 씬인 경우 로그인 UI 설정
        if (SceneManager.GetActiveScene().name == "Title")
        {
            SetupTitleUI();
        }
    }
    
    /// <summary>
    /// Title 씬 UI 설정
    /// </summary>
    private void SetupTitleUI()
    {
        // 저장된 정보가 있으면 자동으로 채우기
        if (_nicknameInput != null)
        {
            string savedNickname = PlayerPrefs.GetString("PlayerNickname", "");
            if (!string.IsNullOrEmpty(savedNickname))
            {
                _nicknameInput.text = savedNickname;
            }
        }
        
        if (_passwordInput != null)
        {
            string savedPassword = PlayerPrefs.GetString("PlayerPassword", "");
            if (!string.IsNullOrEmpty(savedPassword))
            {
                _passwordInput.text = savedPassword;
            }
        }
        
        // Connect 버튼 이벤트 연결
        if (_connectButton != null)
        {
            _connectButton.onClick.RemoveAllListeners();
            _connectButton.onClick.AddListener(ConnectToServer);
        }
        
        // 이벤트 구독
        if (_networkManager != null)
        {
            _networkManager.OnServerConnected += OnServerConnected;
            _networkManager.OnErrorOccurred += OnErrorOccurred;
        }
    }
    
    /// <summary>
    /// 서버 연결 (Title 씬에서 호출)
    /// </summary>
    private async void ConnectToServer()
    {
        if (_nicknameInput == null || _passwordInput == null)
        {
            ShowStatus("입력 필드를 찾을 수 없습니다.", true);
            return;
        }
        
        string nickname = _nicknameInput.text.Trim();
        string password = _passwordInput.text;
        
        // 유효성 검사
        if (string.IsNullOrEmpty(nickname))
        {
            ShowStatus("닉네임을 입력해주세요.", true);
            return;
        }
        
        if (nickname.Length < 2 || nickname.Length > 20)
        {
            ShowStatus("닉네임은 2-20자 사이여야 합니다.", true);
            return;
        }
        
        if (string.IsNullOrEmpty(password))
        {
            ShowStatus("패스워드를 입력해주세요.", true);
            return;
        }
        
        if (password.Length < 4)
        {
            ShowStatus("패스워드는 최소 4자 이상이어야 합니다.", true);
            return;
        }
        
        // UI 비활성화
        if (_connectButton != null)
            _connectButton.interactable = false;
        
        ShowLoading(true);
        ShowStatus("서버에 연결 중...", false);
        
        // NetworkManager를 통해 서버 연결
        bool success = await _networkManager.ConnectToServer(nickname, password);
        
        if (!success)
        {
            // 실패 시 UI 다시 활성화
            if (_connectButton != null)
                _connectButton.interactable = true;
            ShowLoading(false);
        }
        // 성공 시 NetworkManager가 자동으로 MatchingLobby 씬으로 이동시킴
    }
    
    /// <summary>
    /// 서버 연결 성공 콜백
    /// </summary>
    private void OnServerConnected(bool connected)
    {
        if (connected)
        {
            ShowStatus("서버 연결 성공! MatchingLobby로 이동합니다.", false);
            HideLoginUI();
        }
        else
        {
            ShowStatus("서버 연결 실패", true);
            if (_connectButton != null)
                _connectButton.interactable = true;
        }
        ShowLoading(false);
    }
    
    /// <summary>
    /// 에러 발생 콜백
    /// </summary>
    private void OnErrorOccurred(string error)
    {
        ShowStatus(error, true);
        ShowLoading(false);
        if (_connectButton != null)
            _connectButton.interactable = true;
    }
    
    /// <summary>
    /// 상태 메시지 표시
    /// </summary>
    private void ShowStatus(string message, bool isError)
    {
        if (_statusText != null)
        {
            _statusText.text = message;
            _statusText.color = isError ? Color.red : Color.white;
        }
        Debug.Log($"[Title] {message}");
    }
    
    /// <summary>
    /// 로딩 패널 표시/숨김
    /// </summary>
    private void ShowLoading(bool show)
    {
        if (_loadingPanel != null)
        {
            _loadingPanel.SetActive(show);
        }
    }

    private void HideLoginUI()
    {
        // TODO : 추후 
        loginUI.SetActive(false);
    }

    public override void OnClick()
    {
        // 이 메서드는 다른 버튼에서 사용될 수 있음
        // Title 씬에서는 ConnectToServer를 사용
        if (SceneManager.GetActiveScene().name != "Title")
        {
            SceneManager.LoadScene("MainLobby");
        }
    }

    public void GameStartScene()
    {
        if (gameManagers == null)
            gameManagers = FindObjectOfType<GameManagers>();

        if (gameManagers.CurrentQueueSize() == gameManagers.GetMaxSize())
        {
            SceneManager.sceneLoaded += OnGameSceneLoaded;
            SceneManager.LoadScene("Game");
            // for (int i = 0; i < 3; i++)
            // {
            //     TMP_Text text = gameManagers.SelectCharacterButton[i].GetComponentInChildren<TMP_Text>();
            //     text.text = gameManagers.GetCharacterName(i);
            // }
        }
        else
            Debug.Log($"{gameManagers.GetMaxSize()} 최대 캐릭터 갯수를 충족하지 못했습니다.");
    }

    void OnGameSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == "Game")
        {
            // 이벤트 해제 (한 번만 실행)
            SceneManager.sceneLoaded -= OnGameSceneLoaded;

            // 잠깐 대기 후 실행
            //StartCoroutine(SetupGameUI());
        }
    }
    
    private void OnDestroy()
    {
        // 이벤트 구독 해제
        if (_networkManager != null)
        {
            _networkManager.OnServerConnected -= OnServerConnected;
            _networkManager.OnErrorOccurred -= OnErrorOccurred;
        }
    }

    System.Collections.IEnumerator SetupGameUI()
    {
        yield return new WaitForSeconds(0.1f); // UI 초기화 대기
        
        // GameManagers 재참조 (새 씬에서)
        if (gameManagers == null)
            gameManagers = FindObjectOfType<GameManagers>();
        
        // 원래 하려던 작업 실행
        for (int i = 0; i < 3; i++)
        {
            TMP_Text text = gameManagers.SelectCharacterButton[i].GetComponentInChildren<TMP_Text>();
            text.text = gameManagers.GetCharacterName(i);
        }
        
        Debug.Log("✅ 캐릭터 버튼 설정 완료!");
    }

}
