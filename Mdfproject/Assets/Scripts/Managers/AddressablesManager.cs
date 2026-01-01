// Assets/Scripts/Managers/AddressablesManager.cs
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;

public class AddressablesManager : MonoBehaviour
{
    public static AddressablesManager Instance { get; private set; }

    [Header("Preload Settings")]
    [SerializeField] private bool autoPreloadAllOnStart = true;
    [SerializeField] private bool logPreloadProgress = true;

    [Header("Game Prefabs (Addressables AssetReference)")]
    [SerializeField] private AssetReference playerManagerPrefabRef;
    [SerializeField] private AssetReference gridPrefabRef;
    [SerializeField] private AssetReference defaultMonsterPrefabRef;
    [SerializeField] private AssetReference waveDatabaseRef;

    public bool AssetsReady { get; private set; }
    public bool IsPreloading { get; private set; }
    public bool GamePrefabsLoaded { get; private set; }

    // 캐시된 게임 프리팹
    private GameObject _playerManagerPrefab;
    private GameObject _gridPrefab;
    private GameObject _defaultMonsterPrefab;
    private WaveDatabase _waveDatabase;

    // Public getters
    public GameObject PlayerManagerPrefab => _playerManagerPrefab;
    public GameObject GridPrefab => _gridPrefab;
    public GameObject DefaultMonsterPrefab => _defaultMonsterPrefab;
    public WaveDatabase WaveDatabase => _waveDatabase;

    private bool _preloadCompleted;
    private AsyncOperationHandle<IList<Object>> _preloadHandle;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
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

    /// <summary>
    /// 게임에서 사용하는 핵심 프리팹들을 로드합니다.
    /// </summary>
    public async UniTask LoadGamePrefabsAsync()
    {
        if (GamePrefabsLoaded) return;

        Debug.Log("[AddressablesManager] 게임 프리팹 로딩 시작...");

        try
        {
            var loadTasks = new List<UniTask>();

            if (playerManagerPrefabRef != null && playerManagerPrefabRef.RuntimeKeyIsValid())
            {
                loadTasks.Add(LoadPrefabAsync(playerManagerPrefabRef, prefab => _playerManagerPrefab = prefab, "PlayerManager"));
            }
            if (gridPrefabRef != null && gridPrefabRef.RuntimeKeyIsValid())
            {
                loadTasks.Add(LoadPrefabAsync(gridPrefabRef, prefab => _gridPrefab = prefab, "Grid"));
            }
            if (defaultMonsterPrefabRef != null && defaultMonsterPrefabRef.RuntimeKeyIsValid())
            {
                loadTasks.Add(LoadPrefabAsync(defaultMonsterPrefabRef, prefab => _defaultMonsterPrefab = prefab, "Monster"));
            }
            if (waveDatabaseRef != null && waveDatabaseRef.RuntimeKeyIsValid())
            {
                loadTasks.Add(LoadWaveDatabaseAsync());
            }

            await UniTask.WhenAll(loadTasks);
            GamePrefabsLoaded = true;

            Debug.Log("[AddressablesManager] 모든 게임 프리팹 로딩 완료!");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[AddressablesManager] 게임 프리팹 로딩 실패: {ex.Message}");
        }
    }

    private async UniTask LoadPrefabAsync(AssetReference assetRef, System.Action<GameObject> onLoaded, string prefabName)
    {
        var handle = assetRef.LoadAssetAsync<GameObject>();
        await handle.Task;

        if (handle.Status == AsyncOperationStatus.Succeeded)
        {
            onLoaded?.Invoke(handle.Result);
            Debug.Log($"[AddressablesManager] {prefabName} 프리팹 로드 성공");
        }
        else
        {
            Debug.LogError($"[AddressablesManager] {prefabName} 프리팹 로드 실패!");
        }
    }

    private async UniTask LoadWaveDatabaseAsync()
    {
        var handle = waveDatabaseRef.LoadAssetAsync<WaveDatabase>();
        await handle.Task;

        if (handle.Status == AsyncOperationStatus.Succeeded)
        {
            _waveDatabase = handle.Result;
            Debug.Log("[AddressablesManager] WaveDatabase 로드 완료");
        }
        else
        {
            Debug.LogError($"[AddressablesManager] WaveDatabase 로드 실패: {handle.OperationException?.Message}");
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

    private GameObject OnAssetLoaded(AsyncOperationHandle<GameObject> handle, string name, Transform parent)
    {
        GameObject prefabAsset = handle.Result;
        GameObject instance = Instantiate(prefabAsset, parent);
        return instance;
    }
}
