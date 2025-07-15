using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.SearchService;

public class UIPool
{
    private AddressablesManager addressablesManager = new AddressablesManager(); // AddressablesManager �ν��Ͻ� ��������

    private Dictionary<string, GameObject> pool = new Dictionary<string, GameObject>();  // UI ������Ʈ���� �����ϴ� Dictionary
    private GameObject prefab;

    public UIPool(GameObject prefab, string uiname = null)
    {
        if(prefab == null && uiname == null)
        {
            Debug.LogError("UIPool: prefab null and uiname nmull.");
            return;
        }
        else if(uiname == null)
        {
            this.prefab = prefab;
            string name = prefab.name;

            pool.Add(name, this.prefab);
        }
    }

    // UI ��Ҹ� Ǯ���� ������ (�̸��� ��������)
    public Task<GameObject> GetObject(string name)
    {
        if (pool.ContainsKey(name))
        {
            GameObject obj = pool[name];
            obj.SetActive(true);  // Ȱ��ȭ ���·� ��ȯ
            return Task.FromResult(obj);
        }
        else
        {
            return AddGetObject(name);
        }
    }

    // UI ��Ұ� ������ ���� �����Ͽ� ��ȯ
    public Task<GameObject> AddGetObject (string name, GameObject currentposition = null)
    {
        // Ȥ�� ������ �׳� Ǯ���� ��������
        if (pool.ContainsKey(name))
        {
            GameObject obj = pool[name];
            obj.SetActive(true);  // Ȱ��ȭ ���·� ��ȯ
            return Task.FromResult(obj);
        }
        else
        {
            return addressablesManager.LoadObject(name);
        }
    }

    // ����� ���� UI ��Ҹ� Ǯ�� ��ȯ
    public void ReturnObject(string name)
    {
        if (pool.ContainsKey(name))
        {
            GameObject obj = pool[name];
            obj.SetActive(false);  // ��Ȱ��ȭ ���·� ��ȯ
        }
        else
        {
            Debug.LogWarning($"UI element '{name}' not found in the pool.");
        }
    }
}
