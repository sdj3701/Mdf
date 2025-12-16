using UnityEngine;
using UnityEngine.SceneManagement;

public class ExitButton : BaseButton
{
    [Tooltip("로드할 씬의 이름")]
    public string sceneNameToLoad = "Lobby";

    public override void OnClick()
    {
        Debug.Log($"나가기 버튼 클릭됨 - {sceneNameToLoad} 씬으로 이동합니다.");
        if (string.IsNullOrEmpty(sceneNameToLoad))
        {
            Debug.LogError("로드할 씬 이름이 지정되지 않았습니다.");
            return;
        }
        if (GameManagers.Instance != null)
        {
            Destroy(GameManagers.Instance.gameObject);
        }
        // 씬을 떠나기 전에 레지스트리를 초기화합니다.
        ComponentRegistry.Clear();

        var nm = NetworkManager.Instance;
        if (nm != null)
        {
            nm.LeaveAndLoad(sceneNameToLoad);
        }
        else
        {
            SceneManager.LoadScene(sceneNameToLoad);
        }
    }
}

