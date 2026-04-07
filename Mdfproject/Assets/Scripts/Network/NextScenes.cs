using UnityEngine;
using UnityEngine.SceneManagement;

public class NextScenes : BaseButton
{
    private NetworkManager _networkManager;
    private LoginUseCase _loginUseCase;

    protected override void Start()
    {
        _networkManager = NetworkManager.Instance;
        _loginUseCase = new LoginUseCase(new LocalStringAuthService());
        base.Start();
    }

    public override void OnClick()
    {
        if (_loginUseCase == null)
        {
            _loginUseCase = new LoginUseCase(new LocalStringAuthService());
        }

        if (_networkManager == null)
        {
            _networkManager = NetworkManager.Instance;
        }

        string nicknameInput = _networkManager != null && _networkManager.NickNameInput != null
            ? _networkManager.NickNameInput.text
            : string.Empty;

        AuthResult authResult = _loginUseCase.Execute(nicknameInput);
        if (!authResult.Success)
        {
            Debug.LogWarning($"[NextScenes] Login failed: code={authResult.ErrorCode}, message={authResult.ErrorMessage}");
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
}
