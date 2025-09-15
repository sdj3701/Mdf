using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BaseButton : MonoBehaviour
{
    protected UIManagers _uiManager;
    protected GameManagers _gameManagers;


    protected virtual void Start()
    {
        if (_uiManager == null)
        {
            _uiManager = UIManagers.Instance;
        }

        if (_gameManagers == null)
        {
            _gameManagers = GameManagers.Instance;
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
