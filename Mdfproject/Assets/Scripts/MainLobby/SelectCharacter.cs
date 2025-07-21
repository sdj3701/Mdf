using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Unity.VisualStudio.Editor;
using TMPro;
using UnityEngine;

public class SelectCharacter : MonoBehaviour
{
    GameManagers gameManagers;

    public GameObject[] button;

    void Start()
    {
        if (gameManagers = null)
            gameManagers = GameManagers.Instance;
    }

    public void UpdateSelectCharacter()
    {
        for (int i = 0; i < gameManagers.CurrentQueueSize(); i++)
        {
            TMP_Text text = button[i].GetComponentInChildren<TMP_Text>();
            text.text = gameManagers.GetCharacterName(i);
        }
        
        
    }

}
