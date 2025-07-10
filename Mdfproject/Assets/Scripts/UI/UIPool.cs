using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SearchService;

public class UIPool : MonoBehaviour
{
    private Dictionary<string, GameObject> pool;  // UI 오브젝트들을 관리하는 Dictionary
    private GameObject prefab;

    public UIPool(GameObject prefab)
    {
        this.prefab = prefab;
        string name = prefab.name;

        pool = new Dictionary<string, GameObject>();
        pool.Add(name, prefab);
    }

    // UI 요소를 풀에서 꺼내기 (이름을 기준으로)
    public GameObject GetObject(string name)
    {
        if (pool.ContainsKey(name))
        {
            GameObject obj = pool[name];
            obj.SetActive(true);  // 활성화 상태로 반환
            return obj;
        }
        else
        {
            return AddGetObject(name);
        }
    }

    // UI 요소가 없으면 새로 생성하여 반환
    public GameObject AddGetObject (string name)
    {
        // 혹시 있으면 그냥 풀에서 꺼내쓰고
        if (pool.ContainsKey(name))
        {
            GameObject obj = pool[name];
            obj.SetActive(true);  // 활성화 상태로 반환
            return obj;
        }
        else
        {
            // 없으면 Addressable로 불러오기
            GameObject obj = Object.Instantiate(prefab);
            return obj;
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
