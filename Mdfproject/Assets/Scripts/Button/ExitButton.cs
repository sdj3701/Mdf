using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ExitButton : BaseButton
{
    public override void OnClick()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit(); // ���ø����̼� ����
#endif
    }
}

