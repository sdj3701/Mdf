using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SceneManagers : MonoBehaviour
{
    public static SceneManagers Instance = null;

    private void Awake()
    {
        // 싱글톤 패턴
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(this.gameObject); // 씬 전환시에도 유지
        }
        else
        {
            Destroy(this.gameObject);
        }
    }

    

}
