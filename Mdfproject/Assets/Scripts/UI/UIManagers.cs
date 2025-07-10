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

        foreach (GameObject ui in UILists)
        {
            if (ui != null)
            {
                // UI Ǯ�� �ʱ�ȭ�ϰ� ��ųʸ��� �߰�
                uiPools.Add(gameObject.name, new UIPool(ui));
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
            // (�̷� �̸����� ����, ���� ������Ʈ �ڽ����� ����)
            // �츮���Դ� UI �̸��� ������ Addressable�� �ҷ��ͼ� ���� 
            UIPool ui = new UIPool(null, uiName);
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
