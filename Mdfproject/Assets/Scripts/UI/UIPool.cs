using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SearchService;

public class UIPool : MonoBehaviour
{
    private Dictionary<string, GameObject> pool;  // UI ������Ʈ���� �����ϴ� Dictionary
    private GameObject prefab;

    public UIPool(GameObject prefab)
    {
        this.prefab = prefab;
        string name = prefab.name;

        pool = new Dictionary<string, GameObject>();
        pool.Add(name, prefab);
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
    public GameObject AddGetObject (string name)
    {
        // Ȥ�� ������ �׳� Ǯ���� ��������
        if (pool.ContainsKey(name))
        {
            GameObject obj = pool[name];
            obj.SetActive(true);  // Ȱ��ȭ ���·� ��ȯ
            return obj;
        }
        else
        {
            // ������ Addressable�� �ҷ�����
            GameObject obj = Object.Instantiate(prefab);
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
