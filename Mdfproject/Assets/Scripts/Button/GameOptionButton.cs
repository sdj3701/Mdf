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

    public async override void OnClick()
    {
        await uiManager.GetUIElement("OptionCanvas");
    }
}