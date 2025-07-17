using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SoundButton : Button
{
    override public void OnClick()
    {
        uiManager.ReturnUIElement("OptionCanvas");
        uiManager.GetUIElement("SoundCanvas");
    }

    override public void BackButton()
    {
        uiManager.ReturnUIElement("SoundCanvas");
        uiManager.GetUIElement("OptionCanvas");
    }
}