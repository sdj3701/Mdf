using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class VersionButton : BaseButton
{
    public override void OnClick()
    {
        uiManager.ReturnUIElement("OptionCanvas");
        uiManager.GetUIElement("VersionCanvas");
    }
    public override void BackButton()
    {
        uiManager.ReturnUIElement("VersionCanvas");
        uiManager.GetUIElement("OptionCanvas");
    }
}
