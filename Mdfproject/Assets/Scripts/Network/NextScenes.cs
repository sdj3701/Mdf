// Assets/Scripts/TestNetwork/NextScenes.cs
using UnityEngine;
using UnityEngine.SceneManagement;

public class NextScenes : BaseButton
{
    NetworkManager _networkManager;

    protected override void Start()
    {
        // DontDestroyOnLoad로 유지되는 싱글톤 인스턴스를 사용합니다.
        _networkManager = NetworkManager.Instance;
        base.Start();
    }

    public override void OnClick()
    {
        // 1. 닉네임 입력 필드에서 값을 가져옵니다.
        string nickname = _networkManager.NickNameInput.text;

        // 2. 닉네임이 유효한지 확인하고, 비어있다면 기본값을 설정합니다.
        if (string.IsNullOrWhiteSpace(nickname))
        {
            nickname = "Player" + Random.Range(1000, 9999);
        }

        // 3. PlayerPrefs에 닉네임을 저장합니다.
        //    이렇게 하면 씬이 바뀌어도 이 값을 유지할 수 있습니다.
        PlayerPrefs.SetString("PlayerNickname", nickname);
        PlayerPrefs.Save(); // 즉시 저장 (선택사항이지만 안정성을 위해 추가)

        Debug.Log($"닉네임 '{nickname}'을 PlayerPrefs에 저장했습니다.");

        // 4. 로비에 접속하고 씬을 전환합니다.
        _networkManager.JoinLobby();
        SceneManager.LoadScene("MatchingLobby");
    }
}