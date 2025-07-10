using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class GameOptionButton : Button
{
    virtual public void OnClick()
    {
        // 부모 클래스 정의된 OnClick 메서드를 호출
        base.OnClick();
        uiManager.GetUIElement("OptionCanvas");
    }
}
