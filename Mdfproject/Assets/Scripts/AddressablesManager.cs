using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

public class AddressablesManager : MonoBehaviour
{
    // TODO : ������ �� �� ������? LoadObject ���� GameObject�� �����ؼ� ���� ��ȯ���ָ� �ɰ� ������
    [SerializeField]
    private AssetReferenceGameObject[] UIObject;

    private void Start()
    {
        StartCoroutine(InitAddressable());
    }

    // TODO : �����½�ũ�� ���� ����
    IEnumerator InitAddressable()
    {
        var init = Addressables.InitializeAsync();
        yield return init;
    }

    public GameObject LoadObject(string name)
    {
        AsyncOperationHandle<GameObject> go = Addressables.LoadAssetAsync<GameObject>(name);
        go.Completed += (handle) =>
        {
            // �ε� ���� ���� Ȯ��
            OnAssetLoaded(go, name);
        };

        GameObject loadObject = go.Result;
        return loadObject;
    }


    private void OnAssetLoaded(AsyncOperationHandle<GameObject> handle, string name)
    {
        // �ε� ���� ���� Ȯ��
        if (handle.Status == AsyncOperationStatus.Succeeded)
        {
            GameObject loadedObject = handle.Result;
            Debug.Log($"{name} �񵿱������� �ε� ����!");
            Instantiate(loadedObject, transform.position, Quaternion.identity);
        }
        else
        {
            Debug.LogError($"{name} �񵿱������� �ε� ����: {handle.OperationException?.Message}");
        }

        // �� �̻� �ʿ� ���� ��� �ڵ� ���� (���ҽ� ��ε�)
        // Addressables.Release(handle); // OnDestroy �� ������ ������ ȣ��
    }




}
