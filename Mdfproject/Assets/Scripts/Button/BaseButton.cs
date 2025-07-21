using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BaseButton : MonoBehaviour
{
    protected UIManagers uiManager;
    protected GameManagers gameManagers;


    protected virtual void Awake()
    {
        if (uiManager == null)
        {
            uiManager = UIManagers.Instance;
        }
        
        if (gameManagers == null)
        {
            gameManagers = GameManagers.Instance;
        }
    }

    public virtual void OnClick()
    {
        // Override this method in derived classes to handle button click events
        Debug.Log("Button clicked: " + gameObject.name);
    }

    public virtual void BackButton()
    {
        // Override this method in derived classes to handle back button events
        Debug.Log("Back button clicked: " + gameObject.name);
    }
    
}
