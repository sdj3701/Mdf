using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using TMPro;
using UnityEngine;

public class SelectCharacter : MonoBehaviour
{
    GameManagers gameManagers;

    public GameObject[] button;

    void Start()
    {
        if (gameManagers == null)
        {
            gameManagers = GameManagers.Instance;
        }
    }

    public void UpdateSelectCharacter()
    {
        if (gameManagers == null)
        {
            Debug.LogError("gameManagers가 null입니다!");
            return;
        }

        if (button == null || button.Length == 0)
        {
            Debug.LogError("button 배열이 설정되지 않았습니다!");
            return;
        }

        for (int i = 0; i < gameManagers.CurrentQueueSize(); i++)
        {
            TMP_Text text = button[i].GetComponentInChildren<TMP_Text>();
            text.text = gameManagers.GetCharacterName(i);
        }
        
        
    }

}
