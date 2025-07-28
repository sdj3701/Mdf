using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

public class NextScenes : BaseButton
{
    protected override void Start()
    {
        base.Start();
        DontDestroyOnLoad(this.gameObject);
    }

    public override void OnClick()
    {
        SceneManager.LoadScene("MainLobby");
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
