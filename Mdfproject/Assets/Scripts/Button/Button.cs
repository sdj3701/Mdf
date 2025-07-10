using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Button : MonoBehaviour
{
    protected UIManagers uiManager;

    private void Start()
    {
        if (uiManager == null)
        {
            uiManager = UIManagers.Instance;
        }
        //GOBtn.onClick.AddListener(OnClick);
    }

    virtual public void OnClick()
    {
        // Override this method in derived classes to handle button click events
        Debug.Log("Button clicked: " + gameObject.name);
    }

    virtual public void BackButton()
    {
        // Override this method in derived classes to handle back button events
        Debug.Log("Back button clicked: " + gameObject.name);
    }
}
