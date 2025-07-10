using System.Collections;
using System.Collections.Generic;
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

    public GameObject LoadObject(string name)
    {
        AsyncOperationHandle<GameObject> go = Addressables.LoadAssetAsync<GameObject>(name);
        go.Completed += (handle) =>
        {
            // 로드 성공 여부 확인
            OnAssetLoaded(go, name);
        };

        GameObject loadObject = go.Result;
        return loadObject;
    }


    private void OnAssetLoaded(AsyncOperationHandle<GameObject> handle, string name)
    {
        // 로드 성공 여부 확인
        if (handle.Status == AsyncOperationStatus.Succeeded)
        {
            GameObject loadedObject = handle.Result;
            Debug.Log($"{name} 비동기적으로 로드 성공!");
            Instantiate(loadedObject, transform.position, Quaternion.identity);
        }
        else
        {
            Debug.LogError($"{name} 비동기적으로 로드 실패: {handle.OperationException?.Message}");
        }

        // 더 이상 필요 없는 경우 핸들 해제 (리소스 언로드)
        // Addressables.Release(handle); // OnDestroy 등 적절한 시점에 호출
    }




}
