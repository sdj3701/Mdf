using System.Collections.Generic;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using UnityEngine;


public class UIPool
{
    private AddressablesManager addressablesManager = new AddressablesManager(); // AddressablesManager �ν��Ͻ� ��������

    private static Dictionary<string, GameObject> pool = new Dictionary<string, GameObject>();  // UI ������Ʈ���� �����ϴ� Dictionary
    private GameObject prefab;

    public UIPool(GameObject prefab, string uiname = null)
    {
        if (prefab == null && uiname == null)
        {
            Debug.LogError("UIPool: prefab null and uiname nmull.");
            return;
        }
        else if (uiname == null)
        {
            this.prefab = prefab;
            string name = prefab.name;

            pool.Add(name, this.prefab);
        }
    }

    // UI ��Ҹ� Ǯ���� ������ (�̸��� ��������)
    public UniTask<GameObject> GetObject(string name)
    {
        if (pool.ContainsKey(name))
        {
            GameObject obj = pool[name];
            obj.SetActive(true);  // Ȱ��ȭ ���·� ��ȯ
            return UniTask.FromResult(obj);
        }
        else
        {
            return AddGetObject(name);
        }
    }

    // UI ��Ұ� ������ ���� �����Ͽ� ��ȯ
    public async UniTask<GameObject> AddGetObject(string name, GameObject currentposition = null)
    {
        // Ȥ�� ������ �׳� Ǯ���� ��������
        if (pool.ContainsKey(name))
        {
            GameObject obj = pool[name];
            obj.SetActive(true);  // Ȱ��ȭ ���·� ��ȯßß
            return obj;
        }
        else
        {
            //return addressablesManager.LoadObject(name);
            GameObject newInstance = await addressablesManager.LoadObject(name);
            if (newInstance != null)
            {
                newInstance.name = name;
                pool[name] = newInstance;
                newInstance.SetActive(true);
            }
            return newInstance;
        }
    }

    // ����� ���� UI ��Ҹ� Ǯ�� ��ȯ
    public void ReturnObject(string name)
    {
        // UIManager에 는 있지만 uiPool에는 없다
        if (pool.ContainsKey(name))
        {
            // GameObject obj = pool[name];
            // obj.SetActive(false);  // ��Ȱ��ȭ ���·� ��ȯ
            // Debug.Log("re : " + obj.name);
            // ✅ Object.FindObjectsOfType은 정적 메서드이므로 사용 가능
            GameObject[] allObjects = Object.FindObjectsOfType<GameObject>();
            bool foundAny = false;
            
            foreach (GameObject obj in allObjects)
            {
                string cleanName = obj.name.Replace("(Clone)", "").Trim();
                if (cleanName == name && obj.activeInHierarchy)
                {
                    obj.SetActive(false);
                    Debug.Log($"✅ Instance 비활성화: {obj.name}");
                    foundAny = true;
                }
            }
            
            if (!foundAny)
            {
                Debug.LogWarning($"⚠️ 활성화된 '{name}' Instance를 찾을 수 없음");
            }
        }
        else
        {
            Debug.LogWarning($"UI element '{name}' not found in the pool.");
        }
    }

    public void AddUIPoolData(string name, GameObject gameObject)
    {
        pool.Add(name, gameObject);
    }
}
