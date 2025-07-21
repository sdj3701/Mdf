using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

public class AddressablesManager : MonoBehaviour
{
    // TODO : ������ �� �� ������? LoadObject ���� GameObject�� �����ؼ� ���� ��ȯ���ָ� �ɰ� ������
    [SerializeField]
    private AssetReferenceGameObject[] UIObject;

    public async UniTask<GameObject> LoadObject(string name)
    {

        var handle = Addressables.LoadAssetAsync<GameObject>(name);
        await handle.Task;

        if (handle.Status == AsyncOperationStatus.Succeeded)
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
        // GameObject loadedObject = handle.Result;
        // // 생성하면 데이터 uipool에 추가
        // UIPool uipool = new UIPool(loadedObject);
        // Instantiate(loadedObject); 
        // loadedObject.SetActive(true);
        // return loadedObject;
        GameObject prefabAsset = handle.Result;  // Prefab Asset
        UIPool uipool = new UIPool(prefabAsset);
        // ✅ 인스턴스 생성하고 변수에 저장
        GameObject instance = Instantiate(prefabAsset);
        // ✅ 인스턴스에 SetActive 호출
        instance.SetActive(true);
        // ✅ 인스턴스 반환 (Prefab 아님!)
        return instance;
    }
}
