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
            mainCanvas = FindObjectOfType<Canvas>();
            if (mainCanvas == null)
            {
                Debug.LogError("MainCanvas를 찾을 수 없습니다! UI를 표시할 수 없습니다.");
                return null;
            }
        }

        if (!uiPools.ContainsKey(uiName))
        {
            Debug.LogWarning($"UI 요소 '{uiName}'가 풀에 없습니다. Addressables를 통해 로드하고 새로 등록합니다.");
            uiPools.Add(uiName, new UIPool(null, uiName));
        }
        
        return await uiPools[uiName].GetObject(mainCanvas.transform);
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