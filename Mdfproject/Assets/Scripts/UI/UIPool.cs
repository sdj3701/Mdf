using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SearchService;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

public class UIPool : MonoBehaviour
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

    // UI ��Ҹ� Ǯ���� ������ (�̸��� ��������)
    public GameObject GetObject(string name)
    {
        if (pool.ContainsKey(name))
        {
            GameObject obj = pool[name];
            obj.SetActive(true);  // Ȱ��ȭ ���·� ��ȯ
            return obj;
        }
        else
        {
            return AddGetObject(name);
        }
    }

    // UI ��Ұ� ������ ���� �����Ͽ� ��ȯ
    public GameObject AddGetObject (string name, GameObject currentposition = null)
    {
        Debug.Log("test1");
        // Ȥ�� ������ �׳� Ǯ���� ��������
        if (pool.ContainsKey(name))
        {
            GameObject obj = pool[name];
            obj.SetActive(true);  // Ȱ��ȭ ���·� ��ȯ
            return obj;
        }
        else
        {
            Debug.Log("test2");

            // ������ Addressable�� �ҷ�����
            // name���� ���� �׸��� currentposition�� �ڽ����� ����
            GameObject obj = addressablesManager.LoadObject(name);
            pool.Add(obj.name, obj);
            return obj;
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
