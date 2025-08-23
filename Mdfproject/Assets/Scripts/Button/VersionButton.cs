using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class VersionButton : BaseButton
{
    public async override void OnClick()
    {
        uiManager.ReturnUIElement("OptionCanvas");
        await uiManager.GetUIElement("VersionCanvas");
    }
    
    public async override void BackButton()
    {
        uiManager.ReturnUIElement("VersionCanvas");
        await uiManager.GetUIElement("OptionCanvas");
    }
}