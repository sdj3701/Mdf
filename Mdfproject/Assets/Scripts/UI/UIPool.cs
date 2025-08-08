// Assets/Scripts/UI/UIPool.cs
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

public class UIPool
{
    private AddressablesManager addressablesManager = new AddressablesManager();
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

    /// <summary>
    /// 풀에서 UI 객체를 가져옵니다. 없으면 새로 로드하여 지정된 부모 아래에 생성합니다.
    /// </summary>
    public UniTask<GameObject> GetObject(string name, Transform parent)
    {
        // 풀에 이미 인스턴스가 있는지 확인
        if (pool.ContainsKey(name) && pool[name] != null)
        {
            GameObject obj = pool[name];
            // 부모를 설정하고 활성화합니다.
            obj.transform.SetParent(parent, false); // worldPositionStays: false
            obj.SetActive(true);
            return UniTask.FromResult(obj);
        }
        // 풀에 없으면 Addressables로 로드
        else
        {
            return AddGetObject(name, parent);
        }
    }

    /// <summary>
    /// Addressables를 통해 UI를 새로 로드하고 풀에 추가한 뒤 반환합니다.
    /// </summary>
    private async UniTask<GameObject> AddGetObject(string name, Transform parent)
    {
        GameObject newInstance = await addressablesManager.LoadObject(name, parent);
        if (newInstance != null)
        {
            // 나중에 재활용할 수 있도록 풀에 인스턴스를 저장합니다.
            pool[name] = newInstance;
            newInstance.SetActive(true);
        }
        return newInstance;
    }

    /// <summary>
    /// 사용이 끝난 UI 객체를 비활성화합니다.
    /// </summary>
    public void ReturnObject(string name)
    {
        if (pool.ContainsKey(name) && pool[name] != null)
        {
            pool[name].SetActive(false);
        }
        else
        {
            // 씬에 직접 생성된 경우를 대비해 이름을 기반으로 찾아봅니다.
            // 이 로직은 백업용이며, 풀을 통해 관리하는 것이 가장 이상적입니다.
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