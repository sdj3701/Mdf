using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.VisualScripting.Dependencies.Sqlite;
using UnityEngine;
using UnityEngine.UI;

public class CharacterSelectButton : BaseButton
{
    [SerializeField]
    private SelectCharacter selectCharacter;

    protected override void Awake()
    {
        base.Awake();
        Debug.Log("where not Inspector selectCharacter script");
    }

    override public void OnClick()
    {
        Button button = this.gameObject.GetComponent<Button>();
        TMP_Text buttonText = button.GetComponentInChildren<TMP_Text>();
        if (buttonText != null)
        {
            Debug.Log(buttonText.text);
            gameManagers.Pushqueue(buttonText.text);
            selectCharacter.UpdateSelectCharacter();
        }
        else
        {
            string name = buttonText.text;
            Debug.LogWarning($"this '{name}' Button nullptr");
        }


    }   
}
