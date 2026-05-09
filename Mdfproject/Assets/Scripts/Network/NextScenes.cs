using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

public class NextScenes : BaseButton
{
    [Header("Login Input")]
    [SerializeField] private TMP_InputField nicknameInput;
    private readonly TitleLoginEntryFlow _loginEntryFlow = new TitleLoginEntryFlow();

    protected override void Start()
    {
        ResolveNicknameInputIfNeeded();
        base.Start();
    }

    public override void OnClick()
    {
        ResolveNicknameInputIfNeeded();
        string nicknameInputText = nicknameInput != null ? nicknameInput.text : string.Empty;
        _loginEntryFlow.TryLoginAndMoveToMatchingLobby(nicknameInputText);
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

internal sealed class TitleLoginEntryFlow
{
    private LoginUseCase _loginUseCase;
    private bool _isProcessing;

    public bool TryLoginAndMoveToMatchingLobby(string nicknameInputText)
    {
        if (_isProcessing)
        {
            Debug.LogWarning("[TitleLoginEntryFlow] Login already in progress.");
            return false;
        }

        _isProcessing = true;

        try
        {
            AppBootstrapper.EnsureExistsInScene();

            if (!AppBootstrapper.IsBootReady)
            {
                Debug.LogWarning("[TitleLoginEntryFlow] Bootstrap is not ready yet.");
                return false;
            }

            if (_loginUseCase == null)
            {
                _loginUseCase = new LoginUseCase(AuthServiceFactory.CreateFromDefine());
            }

            AuthResult authResult = _loginUseCase.Execute(nicknameInputText);
            if (!authResult.Success)
            {
                string userMessage = AuthErrorMapper.ToUserMessage(authResult);
                Debug.LogWarning($"[TitleLoginEntryFlow] Login failed: code={authResult.ErrorCode}, message={userMessage}");
                return false;
            }

            Debug.Log($"[TitleLoginEntryFlow] Login complete. nickname={authResult.DisplayName}");

            NetworkManager networkManager = NetworkManager.Instance;
            if (networkManager != null)
            {
                networkManager.JoinLobby();
                networkManager.LoadSceneSmart(SceneDefine.MatchingLobby);
            }
            else
            {
                SceneManager.LoadScene(SceneDefine.MatchingLobby);
            }

            return true;
        }
        finally
        {
            _isProcessing = false;
        }
    }
}
