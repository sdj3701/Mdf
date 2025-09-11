using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class VersionButton : BaseButton
{
    public async override void OnClick()
    {
        _uiManager.ReturnUIElement("OptionCanvas");
        await _uiManager.GetUIElement("VersionCanvas");
    }
    
    public async override void BackButton()
    {
        _uiManager.ReturnUIElement("VersionCanvas");
        await _uiManager.GetUIElement("OptionCanvas");
    }
}