using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class VersionButton : Button
{
    virtual public void OnClick()
    {
        uiManager.ReturnUIElement("OptionCanvas");
        uiManager.GetUIElement("VersionCanvas");
    }
    virtual public void BackButton()
    {
        uiManager.ReturnUIElement("VersionCanvas");
        uiManager.GetUIElement("OptionCanvas");
    }
}
