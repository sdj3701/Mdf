using System.Collections;
using System.Collections.Generic;
using System.Numerics;
using UnityEditor;
using UnityEngine;
using System.Threading.Tasks;
using static UnityEngine.GridBrushBase;

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
    public Task<GameObject> GetUIElement(string uiName)
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
