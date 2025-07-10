using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.SearchService;

public class UIPool
{
    private AddressablesManager addressablesManager = new AddressablesManager(); // AddressablesManager 인스턴스 가져오기

    private Dictionary<string, GameObject> pool = new Dictionary<string, GameObject>();  // UI 오브젝트들을 관리하는 Dictionary
    private GameObject prefab;

    public UIPool(GameObject prefab, string uiname = null)
    {
        if(prefab == null && uiname == null)
        {
            Debug.LogError("UIPool: prefab null and uiname nmull.");
            return;
        }
        else if (prefab == null)
        {
            AddGetObject(uiname);
        }
        else
        {
            this.prefab = prefab;
            string name = prefab.name;

            pool.Add(name, this.prefab);
        }
    }

    // UI 요소를 풀에서 꺼내기 (이름을 기준으로)
    public Task<GameObject> GetObject(string name)
    {
        if (pool.ContainsKey(name))
        {
            GameObject obj = pool[name];
            obj.SetActive(true);  // 활성화 상태로 반환
            return Task.FromResult(obj);
        }
        else
        {
            return AddGetObject(name);
        }
    }

    // UI 요소가 없으면 새로 생성하여 반환
    public Task<GameObject> AddGetObject (string name, GameObject currentposition = null)
    {
        // 혹시 있으면 그냥 풀에서 꺼내쓰고
        if (pool.ContainsKey(name))
        {
            GameObject obj = pool[name];
            obj.SetActive(true);  // 활성화 상태로 반환
            return Task.FromResult(obj);
        }
        else
        {
            return addressablesManager.LoadObject(name);
        }
    }

    // 사용이 끝난 UI 요소를 풀에 반환
    public void ReturnObject(string name)
    {
        if (pool.ContainsKey(name))
        {
            GameObject obj = pool[name];
            obj.SetActive(false);  // 비활성화 상태로 반환
        }
        else
        {
            Debug.LogWarning($"UI element '{name}' not found in the pool.");
        }
    }
}
