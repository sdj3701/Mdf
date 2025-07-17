using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class VersionButton : Button
{
    override public void OnClick()
    {
        uiManager.ReturnUIElement("OptionCanvas");
        uiManager.GetUIElement("VersionCanvas");
    }
    override public void BackButton()
    {
        uiManager.ReturnUIElement("VersionCanvas");
        uiManager.GetUIElement("OptionCanvas");
    }
}
