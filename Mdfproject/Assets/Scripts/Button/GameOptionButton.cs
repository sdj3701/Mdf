using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class GameOptionButton : Button
{
    public Button GOBtn;

    virtual public void OnClick()
    {
        base.OnClick();
        uiManager.GetUIElement("OptionCanvas");
    }
}
