using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SoundButton : Button
{
    virtual public void OnClick()
    {
        uiManager.ReturnUIElement("Assets/Prefabs/OptionCanvas.prefab");
        uiManager.GetUIElement("Assets/Prefabs/SoundCanvas.prefab");
    }

    virtual public void BackButton()
    {
        uiManager.ReturnUIElement("Assets/Prefabs/SoundCanvas.prefab");
        uiManager.GetUIElement("Assets/Prefabs/OptionCanvas.prefab");
    }
}