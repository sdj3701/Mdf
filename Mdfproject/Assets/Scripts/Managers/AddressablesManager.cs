// Assets/Scripts/Managers/AddressablesManager.cs
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;

public class AddressablesManager : MonoBehaviour
{
    // ✅ 싱글톤 인스턴스 추가
    public static AddressablesManager Instance { get; private set; }

    [Header("Preload Settings")]
    [SerializeField] private bool autoPreloadAllOnStart = true;
    [SerializeField] private bool logPreloadProgress = true;

    public bool AssetsReady { get; private set; }
    public bool IsPreloading { get; private set; }

    private bool _preloadCompleted;
    private AsyncOperationHandle<IList<Object>> _preloadHandle;

    private void Awake()
    {
        // ✅ 싱글톤 초기화 로직 추가
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject); // 씬이 바뀌어도 파괴되지 않도록 설정
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private async void Start()
    {
        if (autoPreloadAllOnStart)
        {
            await PreloadAllAsync();
        }
    }

    /// <summary>
    /// 모든 Addressables 에셋을 씬 시작 시점에 로드합니다.
    /// </summary>
    public async UniTask PreloadAllAsync()
    {
        if (_preloadCompleted)
        {
            AssetsReady = true;
            return;
        }

        if (IsPreloading)
        {
            await UniTask.WaitUntil(() => !IsPreloading);
            return;
        }

        IsPreloading = true;
        AssetsReady = false;

        if (logPreloadProgress)
        {
            Debug.Log("[AddressablesManager] PreloadAll 시작");
        }

        try
        {
            var initHandle = Addressables.InitializeAsync();
            await initHandle.Task;

            List<IResourceLocation> locations = CollectAllObjectLocations();
            if (locations.Count == 0)
            {
                Debug.LogWarning("[AddressablesManager] PreloadAll 대상 에셋이 없습니다.");
                AssetsReady = true;
                _preloadCompleted = true;
                return;
            }

            _preloadHandle = Addressables.LoadAssetsAsync<Object>(locations, null);
            await _preloadHandle.Task;

            if (_preloadHandle.Status == AsyncOperationStatus.Succeeded)
            {
                AssetsReady = true;
                _preloadCompleted = true;
                if (logPreloadProgress)
                {
                    Debug.Log($"[AddressablesManager] PreloadAll 완료: {locations.Count}개");
                }
            }
            else
            {
                Debug.LogError($"[AddressablesManager] PreloadAll 실패: {_preloadHandle.OperationException?.Message}");
            }
        }
        finally
        {
            IsPreloading = false;
        }
    }

    private static List<IResourceLocation> CollectAllObjectLocations()
    {
        var results = new List<IResourceLocation>();
        var seen = new HashSet<string>();

        foreach (var locator in Addressables.ResourceLocators)
        {
            foreach (var key in locator.Keys)
            {
                if (!locator.Locate(key, typeof(Object), out var locations))
                {
                    continue;
                }

                for (int i = 0; i < locations.Count; i++)
                {
                    var location = locations[i];
                    if (location == null)
                    {
                        continue;
                    }

                    if (!typeof(Object).IsAssignableFrom(location.ResourceType))
                    {
                        continue;
                    }

                    string id = string.IsNullOrEmpty(location.InternalId) ? location.PrimaryKey : location.InternalId;
                    if (string.IsNullOrEmpty(id) || !seen.Add(id))
                    {
                        continue;
                    }

                    results.Add(location);
                }
            }
        }

        return results;
    }

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
        GameObject instance = Instantiate(prefabAsset, parent);
        return instance;
    }
}
