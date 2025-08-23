// Assets/Scripts/UI/UIPool.cs
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

public class UIPool
{
    private GameObject prefab;
    private string addressableKey;
    private Transform poolParent;

    // [개선] 이제 static이 아닌, 각 풀 인스턴스에 속한 큐(Queue)를 사용합니다.
    private Queue<GameObject> availableObjects = new Queue<GameObject>();
    // [개선] 현재 활성화된 오브젝트를 추적합니다.
    private GameObject activeObject = null;

    public UIPool(GameObject prefab, string key = null)
    {
        this.prefab = prefab;
        this.addressableKey = key ?? (prefab != null ? prefab.name : string.Empty);
    }
    
    public async UniTask<GameObject> GetObject(Transform parent)
    {
        if (activeObject != null)
        {
            activeObject.transform.SetParent(parent, false);
            activeObject.SetActive(true);
            return activeObject;
        }

        if (availableObjects.Count > 0)
        {
            activeObject = availableObjects.Dequeue();
            activeObject.transform.SetParent(parent, false);
            activeObject.SetActive(true);
            return activeObject;
        }

        return await CreateNewObject(parent);
    }

    private async UniTask<GameObject> CreateNewObject(Transform parent)
    {
        GameObject newInstance = null;
        if (prefab != null)
        {
            newInstance = Object.Instantiate(prefab, parent);
        }
        else if (!string.IsNullOrEmpty(addressableKey))
        {
            newInstance = await AddressablesManager.Instance.LoadObject(addressableKey, parent);
        }

        if (newInstance != null)
        {
            newInstance.name = addressableKey; // (Clone) 접미사 제거
            activeObject = newInstance;
            activeObject.SetActive(true);
        }
        return newInstance;
    }

    public void ReturnObject()
    {
        if (activeObject != null)
        {
            activeObject.SetActive(false);
            // 필요하다면 풀의 부모 오브젝트 아래로 이동시켜 정리할 수 있습니다.
            // activeObject.transform.SetParent(poolParent);
            availableObjects.Enqueue(activeObject);
            activeObject = null;
        }
    }
}