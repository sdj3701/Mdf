// Assets/Scripts/NextScenes.cs

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
    
    // [제거] NetworkManager 프리팹 변수를 제거합니다.
    // [Header("Network Prefab")]
    // [SerializeField] private GameObject _networkManagerPrefab;
    
    protected override void Start()
    {
        base.Start();
        
        // [핵심 수정] 씬에 이미 존재하는 NetworkManager 인스턴스를 찾습니다.
        _networkManager = NetworkManager.Instance;
        if (_networkManager == null)
        {
            Debug.LogError("<color=red>씬에 NetworkManager 인스턴스가 존재하지 않습니다! Title 씬의 Hierarchy를 확인하여 NetworkManager 프리팹이 배치되어 있는지 확인해주세요.</color>");
            // 중요한 시스템이 없으므로 더 이상 진행하지 않도록 버튼을 비활성화합니다.
            if(_connectButton != null) _connectButton.interactable = false;
            return;
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
        
        if (_connectButton != null)
        {
            _connectButton.onClick.RemoveAllListeners();
            _connectButton.onClick.AddListener(ConnectToServer);
        }
        
        if (_networkManager != null)
        {
            _networkManager.OnServerConnected += OnServerConnected;
            _networkManager.OnErrorOccurred += OnErrorOccurred;
        }
    }
    
    private async void ConnectToServer()
    {
        if (_nicknameInput == null || _passwordInput == null)
        {
            ShowStatus("입력 필드를 찾을 수 없습니다.", true);
            return;
        }
        
        string nickname = _nicknameInput.text.Trim();
        string password = _passwordInput.text;
        
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
        
        if (_connectButton != null)
            _connectButton.interactable = false;
        
        ShowLoading(true);
        ShowStatus("서버에 연결 중...", false);
        
        bool success = await _networkManager.ConnectToServer(nickname, password);
        
        if (!success)
        {
            if (_connectButton != null)
                _connectButton.interactable = true;
            ShowLoading(false);
        }
    }
    
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
    
    private void OnErrorOccurred(string error)
    {
        ShowStatus(error, true);
        ShowLoading(false);
        if (_connectButton != null)
            _connectButton.interactable = true;
    }
    
    private void ShowStatus(string message, bool isError)
    {
        if (_statusText != null)
        {
            _statusText.text = message;
            _statusText.color = isError ? Color.red : Color.white;
        }
        Debug.Log($"[Title] {message}");
    }
    
    private void ShowLoading(bool show)
    {
        if (_loadingPanel != null)
        {
            _loadingPanel.SetActive(show);
        }
    }

    private void HideLoginUI()
    {
        if(loginUI != null) loginUI.SetActive(false);
    }

    public override void OnClick()
    {
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
        }
        else
            Debug.Log($"{gameManagers.GetMaxSize()} 최대 캐릭터 갯수를 충족하지 못했습니다.");
    }

    void OnGameSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == "Game")
        {
            SceneManager.sceneLoaded -= OnGameSceneLoaded;
        }
    }
    
    private void OnDestroy()
    {
        if (_networkManager != null)
        {
            _networkManager.OnServerConnected -= OnServerConnected;
            _networkManager.OnErrorOccurred -= OnErrorOccurred;
        }
    }
}