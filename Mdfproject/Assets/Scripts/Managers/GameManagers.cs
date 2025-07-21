using System.Collections;
using System.Collections.Generic;
using UnityEditor.Build.Content;
using UnityEngine;
using System.Linq;

public class GameManagers : MonoBehaviour
{
    public static GameManagers Instance = null;
    private Queue<string> characterselectdata = new Queue<string>();
    private int maxqueue = 3;

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

    // TODO : resize 필요할 때 일단 사용 안함 하면 머리아픔
    public void SetMaxQueueSize(int count)
    {
        maxqueue = count;
    }

    public int GetMaxSize()
    {
        return maxqueue;
    }

    // input data
    public void Pushqueue(string name)
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

    public string GetCharacterName(int count)
    {
        return  characterselectdata.ElementAt(count);
    }

    public int CurrentQueueSize()
    {
        return characterselectdata.Count;
    }

}
