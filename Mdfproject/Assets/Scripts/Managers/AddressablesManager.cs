// Assets/Scripts/Managers/AddressablesManager.cs
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

public class AddressablesManager : MonoBehaviour
{
    /// <summary>
    /// 어드레서블 에셋을 로드하고 지정된 부모 아래에 인스턴스화합니다.
    /// </summary>
    /// <param name="name">로드할 에셋의 어드레서블 주소</param>
    /// <param name="parent">생성된 오브젝트가 자식으로 속할 부모 Transform</param>
    /// <returns>생성된 게임 오브젝트</returns>
    public async UniTask<GameObject> LoadObject(string name, Transform parent = null)
    {
        var handle = Addressables.LoadAssetAsync<GameObject>(name);
        await handle.Task;

        if (handle.Status == AsyncOperationStatus.Succeeded)
        {
            return OnAssetLoaded(handle, name, parent);
        }
        else
        {
            Debug.LogError($"{name} 비동기 로드 실패: {handle.OperationException?.Message}");
            return null;
        }
    }

    /// <summary>
    /// 에셋 로드가 성공했을 때 호출되어 인스턴스를 생성합니다.
    /// </summary>
    private GameObject OnAssetLoaded(AsyncOperationHandle<GameObject> handle, string name, Transform parent)
    {
        GameObject prefabAsset = handle.Result;

        // Instantiate 시에 parent를 지정하여 인스턴스를 생성합니다.
        // parent가 null이면 씬 최상단에 생성됩니다.
        GameObject instance = Instantiate(prefabAsset, parent);

        //SetActive(true)는 호출하는 쪽에서 관리하므로 여기서는 생략해도 좋습니다.
        //instance.SetActive(true); 

        return instance;
    }
}