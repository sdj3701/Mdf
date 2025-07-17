using System.Collections;
using System.Collections.Generic;
using System.Numerics;
using UnityEditor;
using UnityEngine;
using System.Threading.Tasks;
using static UnityEngine.GridBrushBase;
using Cysharp.Threading.Tasks;

public class UIManagers : MonoBehaviour
{
    public static UIManagers Instance = null;

    // �ν����Ϳ��� UI �ʱ�ȭ (UI ��ҵ��� ����Ʈ�� ����)
    public List<GameObject> UILists;
    // UI ��Ҹ� �̸����� �����ϱ� ���� ����Ʈ (�� UI ��Һ��� Ǯ�� ����)
    private Dictionary<string, UIPool> uiPools;  // UI Ǯ ����

    private async void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(this.gameObject);
        }
        else
        {
            Destroy(this.gameObject);
        }

        uiPools = new Dictionary<string, UIPool>(); // �ʱ�ȭ

        // UI List use uiPools Dictionary Create
        foreach (GameObject ui in UILists)
        {
            if (ui != null)
            {
                // UI Ǯ�� �ʱ�ȭ�ϰ� ��ųʸ��� �߰�
                uiPools.Add(ui.name, new UIPool(ui));
            }
        }
    }

    // UI ��Ҹ� Ǯ���� ������
    public UniTask<GameObject> GetUIElement(string uiName)
    {
        if (uiPools.ContainsKey(uiName))
        {
            return uiPools[uiName].GetObject(uiName);
        }
        else
        {
            Debug.LogWarning($"UI element '{uiName}' is not found in the pool and Addressable Load.");
            // 여기가 문제 없어서 만들면 풀에 넣어야 하는데 안넣음
            UIPool ui = new UIPool(null, uiName);
            uiPools.Add(uiName, ui);
            return ui.GetObject(uiName);
        }    
    }

    // UI ��Ҹ� Ǯ�� ��ȯ�ϱ�
    public void ReturnUIElement(string uiName)
    {
        if (uiPools.ContainsKey(uiName))
        {
            uiPools[uiName].ReturnObject(uiName);
        }
        else
        {
            Debug.LogWarning($"UI element '{uiName}' is not found in the pool.");
        }
    }

}

/*
    LinkHero
    2D디펜스 게임에서 특수한 캐릭터를 누르면 3D 화면으로 전환되어서 화면을 여러번 터치하는 방식의 게임 (시간이 있음)
    특수 캐릭터는 누를 수 있게 되면 캐릭터에 대한 효과가 나타남
    

*/