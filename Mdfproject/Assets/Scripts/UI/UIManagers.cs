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

    /*
        1. 문제점
        버그 지금 딕셔너리를 GameObject를 생성하는데 이름은 오브젝트 이름을 사용
        하지만 지금 내가 어드레스에이블을 사용해서 문제가 
        오브젝트 이름 != 어드레이스에이블 이름이 서로 다름
        2. 문제점
        오브젝트가 2개 생성됨 이유를 모르겠네? 인스턴트는 한개 뿐인데?
        왠지 1번 잡으면 문제 해결될듯
    */
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
