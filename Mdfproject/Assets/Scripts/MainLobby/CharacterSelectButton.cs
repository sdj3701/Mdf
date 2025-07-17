using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.VisualScripting.Dependencies.Sqlite;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;

public class CharacterSelectButton : Button
{
    
    override public void OnClick()
    {
        Button button = this.gameObject.GetComponent<Button>();
        TMP_Text buttonText = button.GetComponentInChildren<TMP_Text>();
        if (buttonText != null)
        {
            gmaeManager.Enqueue(buttonText.text);
            Debug.Log(buttonText.text);
        }
        else
        {
            string name = buttonText.text;
            Debug.LogWarning($"this '{name}' Button nullptr");
        }


    }   
}
