// Assets/Scripts/UI/UIPool.cs
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

public class UIPool
{
    // ✅ 'new' 키워드 제거!
    private static Dictionary<string, GameObject> pool = new Dictionary<string, GameObject>();
    private GameObject prefab;

    public UIPool(GameObject prefab, string uiname = null)
    {
        if (uiname == null && prefab != null)
        {
            this.prefab = prefab;
            string name = prefab.name;
            if (!pool.ContainsKey(name))
            {
                pool.Add(name, prefab);
            }
            else
            {
                pool[name] = prefab;
            }
        }
    }

    public async UniTask<GameObject> GetObject(string name, Transform parent)
    {
        if (pool.ContainsKey(name) && pool[name] != null)
        {
            GameObject obj = pool[name];
            obj.transform.SetParent(parent, false);
            obj.SetActive(true);
            return obj;
        }
        else
        {
            // ✅ 싱글톤 인스턴스를 통해 LoadObject 호출
            GameObject newInstance = await AddressablesManager.Instance.LoadObject(name, parent);
            if (newInstance != null)
            {
                pool[name] = newInstance;
                newInstance.SetActive(true);
            }
            return newInstance;
        }
    }
    
    // AddGetObject 함수는 GetObject에 통합되었으므로 제거해도 됩니다.
    // private async UniTask<GameObject> AddGetObject(...)

    public void ReturnObject(string name)
    {
        if (pool.ContainsKey(name) && pool[name] != null)
        {
            pool[name].SetActive(false);
        }
        else
        {
            GameObject objInScene = GameObject.Find(name + "(Clone)");
            if (objInScene != null)
            {
                objInScene.SetActive(false);
            }
            else
            {
                 Debug.LogWarning($"⚠️ 반환할 '{name}' 인스턴스를 찾을 수 없음");
            }
        }
    }
}