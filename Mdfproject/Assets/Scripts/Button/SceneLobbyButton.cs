using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneLobbyButton : BaseButton
{
    public GameObject EndUI;
    public override void OnClick()
    {
        var nm = NetworkManager.Instance;
        if (nm != null)
        {
            nm.LoadSceneSmart("MainLobby");
        }
        else
        {
            SceneManager.LoadScene("MainLobby");
        }
        EndUI.SetActive(false);
    }
}
