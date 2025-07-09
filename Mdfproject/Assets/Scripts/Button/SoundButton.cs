using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SoundButton : Button
{
    virtual public void OnClick()
    {
        uiManager.ReturnUIElement("OptionCanvas");
        uiManager.GetUIElement("SoundCanvas");
    }
}