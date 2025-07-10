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

    // 인스펙터에서 UI 초기화 (UI 요소들은 리스트로 관리)
    public List<GameObject> UILists;
    // UI 요소를 이름으로 관리하기 위한 리스트 (각 UI 요소별로 풀을 관리)
    private Dictionary<string, UIPool> uiPools;  // UI 풀 관리

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

        uiPools = new Dictionary<string, UIPool>(); // 초기화

        foreach (GameObject ui in UILists)
        {
            if (ui != null)
            {
                // UI 풀을 초기화하고 딕셔너리에 추가
                uiPools.Add(gameObject.name, new UIPool(ui));
            }
        }
    }

    // UI 요소를 풀에서 꺼내기
    public Task<GameObject> GetUIElement(string uiName)
    {
        if (uiPools.ContainsKey(uiName))
        {
            return uiPools[uiName].GetObject(uiName);
        }
        else
        {
            Debug.LogWarning($"UI element '{uiName}' is not found in the pool and Addressable Load.");
            // (이런 이름으로 생성, 여기 오브젝트 자식으로 생성)
            // 우리에게는 UI 이름이 있으니 Addressable로 불러와서 생성 
            UIPool ui = new UIPool(null, uiName);
            return ui.GetObject(uiName);
        }
    }

    // UI 요소를 풀에 반환하기
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
