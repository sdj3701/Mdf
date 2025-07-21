using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class NextScenes : BaseButton
{
    GameManagers gameManagers;

    public override void OnClick()
    {
        SceneManager.LoadScene("MainLobby");
    }

    public void GameStartScene()
    {
        if (gameManagers == null)
            gameManagers = FindObjectOfType<GameManagers>();

        if (gameManagers.CurrentQueueSize() == gameManagers.GetMaxSize())
            SceneManager.LoadScene("Game");
        else
            Debug.Log($"{gameManagers.GetMaxSize()} 최대 캐릭터 갯수를 충족하지 못했습니다.");
    }
}
