using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

public class AddressablesManager : MonoBehaviour
{
    // TODO : 지워도 될 거 같은데? LoadObject 에서 GameObject를 생성해서 직접 반환해주면 될거 같은데
    [SerializeField]
    private AssetReferenceGameObject[] UIObject;

    private void Start()
    {
        StartCoroutine(InitAddressable());
    }

    // TODO : 유니태스크로 변경 예정
    IEnumerator InitAddressable()
    {
        var init = Addressables.InitializeAsync();
        yield return init;
    }

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
            Debug.LogError($"{name} 비동기적으로 로드 실패: {handle.OperationException?.Message}");
            return null;
        }

    }


    private GameObject OnAssetLoaded(AsyncOperationHandle<GameObject> handle, string name)
    {
        GameObject loadedObject = handle.Result;
        Instantiate(loadedObject); // 로드된 오브젝트를 인스턴스화
        return loadedObject;
    }
}
