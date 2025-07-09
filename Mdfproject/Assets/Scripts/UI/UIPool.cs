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
            // 이름에 해당하는 UI 오브젝트가 풀에 없으면 새로 생성하여 반환
            // TODO : addressable로 변경 해야함
            GameObject obj = Object.Instantiate(prefab);
            obj.name = name;  // 이름 설정
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
