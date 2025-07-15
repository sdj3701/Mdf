using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

public class AddressablesManager : MonoBehaviour
{
    // TODO : ������ �� �� ������? LoadObject ���� GameObject�� �����ؼ� ���� ��ȯ���ָ� �ɰ� ������
    [SerializeField]
    private AssetReferenceGameObject[] UIObject;

    public async Task<GameObject> LoadObject(string name)
    {

        var handle = Addressables.LoadAssetAsync<GameObject>(name);
        await handle.Task;

        if( handle.Status == AsyncOperationStatus.Succeeded)
        {
            return OnAssetLoaded(handle, name);
        }
        else
        {
            Debug.LogError($"{name} �񵿱������� �ε� ����: {handle.OperationException?.Message}");
            return null;
        }

    }


    private GameObject OnAssetLoaded(AsyncOperationHandle<GameObject> handle, string name)
    {
        GameObject loadedObject = handle.Result;
        Instantiate(loadedObject); // �ε�� ������Ʈ�� �ν��Ͻ�ȭ
        return loadedObject;
    }
}
