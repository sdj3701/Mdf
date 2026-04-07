using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

public class NextScenes : BaseButton
{
    [Header("Login Input")]
    [SerializeField] private TMP_InputField nicknameInput;

    private NetworkManager _networkManager;
    private LoginUseCase _loginUseCase;

    protected override void Start()
    {
        AppBootstrapper.EnsureExistsInScene();
        _networkManager = NetworkManager.Instance;
        _loginUseCase = new LoginUseCase(AuthServiceFactory.CreateFromDefine());
        ResolveNicknameInputIfNeeded();
        base.Start();
    }

    public override void OnClick()
    {
        AppBootstrapper.EnsureExistsInScene();

        if (!AppBootstrapper.IsBootReady)
        {
            Debug.LogWarning("[NextScenes] Bootstrap is not ready yet.");
            return;
        }

        if (_loginUseCase == null)
        {
            _loginUseCase = new LoginUseCase(AuthServiceFactory.CreateFromDefine());
        }

        if (_networkManager == null)
        {
            _networkManager = NetworkManager.Instance;
        }

        ResolveNicknameInputIfNeeded();
        string nicknameInputText = nicknameInput != null ? nicknameInput.text : string.Empty;

        AuthResult authResult = _loginUseCase.Execute(nicknameInputText);
        if (!authResult.Success)
        {
            string userMessage = AuthErrorMapper.ToUserMessage(authResult);
            Debug.LogWarning($"[NextScenes] Login failed: code={authResult.ErrorCode}, message={userMessage}");
            return;
        }

        Debug.Log($"[NextScenes] Login complete. nickname={authResult.DisplayName}");

        if (_networkManager != null)
        {
            _networkManager.JoinLobby();
            _networkManager.LoadSceneSmart(SceneDefine.MatchingLobby);
        }
        else
        {
            SceneManager.LoadScene(SceneDefine.MatchingLobby);
        }
    }

    private void ResolveNicknameInputIfNeeded()
    {
        if (nicknameInput != null)
        {
            return;
        }

        var allInputs = FindObjectsOfType<TMP_InputField>(true);
        for (int i = 0; i < allInputs.Length; i++)
        {
            var input = allInputs[i];
            if (input == null || input.gameObject == null)
            {
                continue;
            }

            string nameLower = input.gameObject.name.ToLowerInvariant();
            if (nameLower.Contains("nickname"))
            {
                nicknameInput = input;
                break;
            }
        }
    }
}
