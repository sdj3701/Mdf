using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class GameOptionButton : MonoBehaviour
{
    public Button GOBtn;
    private UIManagers uiManager;

    private void Start()
    {
        if (uiManager == null)
        {
            uiManager = UIManagers.Instance;
        }
        //GOBtn.onClick.AddListener(OnClick);
    }

    public void OnClick()
    {
        uiManager.GetUIElement("OptionCanvas");
        Debug.Log("GameOptionButton clicked");
        // Add your logic here for what happens when the button is clicked
    }
}
