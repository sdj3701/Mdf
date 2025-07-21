using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class GameOptionButton : BaseButton
{
    public override void OnClick()
    {
        // �θ� Ŭ���� ���ǵ� OnClick �޼��带 ȣ��
        base.OnClick();
        uiManager.GetUIElement("OptionCanvas");
    }
}
