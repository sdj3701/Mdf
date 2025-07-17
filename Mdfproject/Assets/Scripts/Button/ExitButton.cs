using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ExitButton : Button
{
    override public void OnClick()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit(); // ���ø����̼� ����
#endif
    }
}

