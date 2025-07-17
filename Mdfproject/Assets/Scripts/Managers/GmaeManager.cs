using System.Collections;
using System.Collections.Generic;
using UnityEditor.Build.Content;
using UnityEngine;

public class GmaeManager : MonoBehaviour
{
    public static GmaeManager Instance = null;
    private Queue<string> characterselectdata = new Queue<string>();
    private int maxqueue = 3;

    private void Awake()
    {
        // 싱글톤 패턴
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject); // 씬 전환시에도 유지
        }
        else
        {
            Destroy(gameObject);
        }
    }

    // TODO : resize 필요할 때
    public void SetMaxQueueSize(int count)
    {
        maxqueue = count;
    }

    // input data
    public void Enqueue(string name)
    {
        if (characterselectdata.Count < maxqueue)
        {
            Debug.Log("데이터 넣기");
            characterselectdata.Enqueue(name);
        }
        else
        {
            Debug.Log("오버 또는 같으니까 하나를 삭제하고 데이터 넣기");
            characterselectdata.Dequeue();
            characterselectdata.Enqueue(name);
        }
    }

}
