using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneLobbyButton : BaseButton
{
    public GameObject EndUI;
    public override void OnClick()
    {
        SceneManager.LoadScene("MainLobby");
        EndUI.SetActive(false);
    }
}
