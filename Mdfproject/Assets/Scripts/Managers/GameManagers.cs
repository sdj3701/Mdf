using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using UnityEngine.UI;
using TMPro;
using System.Numerics;

public class GameManagers : MonoBehaviour
{
    public enum GameState { Prepare, Combat, Augment, GameOver }
    public static GameManagers Instance = null;

    private Queue<string> characterselectdata = new Queue<string>();

    private int maxqueue = 3;

    public Button[] SelectCharacterButton;

    private List<string> selectCharacterName = new List<string>();
    public GameState CurrentState { get; private set; }
    public int currentRound = 1;
    // TODO: PlayerManager 클래스 생성 후 교체
    // public PlayerManager player1;
    // public PlayerManager player2;

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
            selectCharacterName.Add(name);
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
        return characterselectdata.ElementAt(count);
    }

    public int CurrentQueueSize()
    {
        return characterselectdata.Count;
    }

    public string GetSelectCharacterName(int i)
    {
        return selectCharacterName[i];
    }

    public void ChangeState(GameState newState)
    {
        CurrentState = newState;
        Debug.Log($"Game State Changed to: {newState}");
        // TODO: 각 상태에 맞는 로직 실행 (예: Combat 상태 -> 몬스터 스폰)
    }
}
