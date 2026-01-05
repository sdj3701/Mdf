// Assets/Scripts/UI/UIManagers.cs
using System.Collections.Generic;
using UnityEngine;
using Cysharp.Threading.Tasks;

public class UIManagers : MonoBehaviour
{
    public static UIManagers Instance = null;

    public List<GameObject> UILists;
    private Dictionary<string, UIPool> uiPools;
    public Canvas mainCanvas;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(this.gameObject);
            InitializePools();
        }
        else
        {
            Destroy(this.gameObject);
        }
    }

    private void InitializePools()
    {
        uiPools = new Dictionary<string, UIPool>();
        foreach (GameObject uiPrefab in UILists)
        {
            if (uiPrefab != null)
            {
                uiPools.Add(uiPrefab.name, new UIPool(uiPrefab));
            }
        }
    }

    public async UniTask<GameObject> GetUIElement(string uiName)
    {
        if (mainCanvas == null)
        {
            if (BuildDebugGUI.Instance != null)
            {
                BuildDebugGUI.Instance.Log("GetUIElement: MainCanvas 탐색 시도...");
            }
            mainCanvas = FindObjectOfType<Canvas>();
            if (mainCanvas == null)
            {
                if (BuildDebugGUI.Instance != null)
                {
                    BuildDebugGUI.Instance.Log("<color=red>GetUIElement: MainCanvas를 찾을 수 없음!</color>");
                }
                return null;
            }
            if (BuildDebugGUI.Instance != null)
            {
                BuildDebugGUI.Instance.Log("<color=green>GetUIElement: MainCanvas 찾음!</color>");
            }
        }

        if (!uiPools.ContainsKey(uiName))
        {
            if (BuildDebugGUI.Instance != null)
            {
                BuildDebugGUI.Instance.Log($"<color=yellow>GetUIElement: '{uiName}' 풀 없음. Addressables 로드 시작.</color>");
            }
            uiPools.Add(uiName, new UIPool(null, uiName));
        }
        
        return await uiPools[uiName].GetObject(mainCanvas.transform);
    }
    
    /// <summary>
    /// 지정된 UI 요소가 현재 활성 상태인지 확인합니다.
    /// </summary>
    public bool IsUIElementActive(string uiName)
    {
        string originalName = uiName.Replace("(Clone)", "");
        if (uiPools.ContainsKey(originalName))
        {
            return uiPools[originalName].IsActive();
        }
        return false;
    }

    public void ReturnUIElement(string uiName)
    {
        // [개선] (Clone)이 붙어있을 가능성을 제거
        string originalName = uiName.Replace("(Clone)", "");

        if (uiPools.ContainsKey(originalName))
        {
            uiPools[originalName].ReturnObject();
        }
        else
        {
            Debug.LogWarning($"UI 요소 '{originalName}'가 풀에 등록되어 있지 않습니다.");
        }
    }
}
