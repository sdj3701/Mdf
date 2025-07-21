using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SoundButton : BaseButton
{
    public override void OnClick()
    {
        uiManager.ReturnUIElement("OptionCanvas");
        uiManager.GetUIElement("SoundCanvas");
    }

    public override void BackButton()
    {
        uiManager.ReturnUIElement("SoundCanvas");
        uiManager.GetUIElement("OptionCanvas");
    }
}