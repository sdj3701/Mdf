using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class GameOptionButton : BaseButton
{
    protected override void Start()
    {
        base.Start();
    }


    public override void OnClick()
    {
        uiManager.GetUIElement("OptionCanvas");
    }
}
