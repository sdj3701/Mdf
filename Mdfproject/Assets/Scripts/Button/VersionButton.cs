using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class VersionButton : Button
{
    virtual public void OnClick()
    {
        uiManager.ReturnUIElement("Assets/Prefabs/OptionCanvas.prefab");
        uiManager.GetUIElement("Assets/Prefabs/VersionCanvas.prefab");
    }
    virtual public void BackButton()
    {
        uiManager.ReturnUIElement("Assets/Prefabs/VersionCanvas.prefab");
        uiManager.GetUIElement("Assets/Prefabs/OptionCanvas.prefab");
    }
}
