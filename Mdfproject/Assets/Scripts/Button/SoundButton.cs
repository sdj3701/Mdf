// Assets/Scripts/Button/SoundButton.cs
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SoundButton : BaseButton
{
    public async override void OnClick()
    {
        uiManager.ReturnUIElement("OptionCanvas");
        await uiManager.GetUIElement("SoundCanvas");
    }

    public async override void BackButton()
    {
        uiManager.ReturnUIElement("SoundCanvas");
        await uiManager.GetUIElement("OptionCanvas");
    }
}