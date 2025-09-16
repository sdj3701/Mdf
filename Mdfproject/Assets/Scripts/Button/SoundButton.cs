// Assets/Scripts/Button/SoundButton.cs
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SoundButton : BaseButton
{
    public async override void OnClick()
    {
        _uiManager.ReturnUIElement("OptionCanvas");
        await _uiManager.GetUIElement("SoundCanvas");
    }

    public async override void BackButton()
    {
        _uiManager.ReturnUIElement("SoundCanvas");
        await _uiManager.GetUIElement("OptionCanvas");
    }
}