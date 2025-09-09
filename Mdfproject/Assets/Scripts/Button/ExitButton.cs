using UnityEngine;
using UnityEngine.SceneManagement;

public class ExitButton : BaseButton
{
    [Tooltip("로드할 씬의 이름")]
    public string sceneNameToLoad = "Lobby";

    public override void OnClick()
    {
        Debug.Log($"나가기 버튼 클릭됨 - {sceneNameToLoad} 씬으로 이동합니다.");
        if (!string.IsNullOrEmpty(sceneNameToLoad))
        {
            // 씬을 전환하기 전에 게임 상태를 정리합니다.
            if (GameManagers.Instance != null)
            {
                Destroy(GameManagers.Instance.gameObject);
            }
            ComponentRegistry.Clear();

            SceneManager.LoadScene(sceneNameToLoad);
        }
        else
        {
            Debug.LogError("로드할 씬 이름이 지정되지 않았습니다.");
        }
    }
}

