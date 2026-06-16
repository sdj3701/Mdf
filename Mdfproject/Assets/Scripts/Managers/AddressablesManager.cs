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
    [SerializeField] private bool autoPreloadAllOnStart = false;
    [SerializeField] private bool logPreloadProgress = true;

    [Header("Game Prefabs (Addressables AssetReference)")]
    [SerializeField] private AssetReference playerManagerPrefabRef;
    [SerializeField] private AssetReference gridPrefabRef;
    [SerializeField] private AssetReference defaultMonsterPrefabRef;
    [SerializeField] private AssetReference waveDatabaseRef;

    public bool AssetsReady { get; private set; }
    public bool IsPreloading { get; private set; }
    public bool GamePrefabsLoaded { get; private set; }
    public bool AutoPreloadAllOnStart => autoPreloadAllOnStart;

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
    private bool _gamePrefabsLoading;
    private string _gamePrefabsLoadReason = "direct";

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

    public void BeginGamePrefabsPreload(string reason)
    {
        string safeReason = string.IsNullOrEmpty(reason) ? "unknown" : reason;
        if (GamePrefabsLoaded && AreGamePrefabCachesReady())
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            LogLoadMarker("game_prefabs_preload_cached", new Dictionary<string, object>
            {
                { "reason", safeReason }
            });
#endif
            return;
        }

        if (_gamePrefabsLoading)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            LogLoadMarker("game_prefabs_preload_joined", new Dictionary<string, object>
            {
                { "reason", safeReason }
            });
#endif
            return;
        }

        _gamePrefabsLoadReason = safeReason;
        LoadGamePrefabsAsync().Forget();
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
        if (GamePrefabsLoaded && AreGamePrefabCachesReady())
            return;

        if (_gamePrefabsLoading)
        {
            await UniTask.WaitUntil(() => !_gamePrefabsLoading);

            if (AreGamePrefabCachesReady())
            {
                GamePrefabsLoaded = true;
                return;
            }
        }

        Debug.Log("[AddressablesManager] 게임 프리팹 로딩 시작...");
        _gamePrefabsLoading = true;
        float startTime = Time.realtimeSinceStartup;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        LogLoadMarker("game_prefabs_load_begin", new Dictionary<string, object>
        {
            { "reason", _gamePrefabsLoadReason },
            { "cacheSummary", BuildGamePrefabCacheSummary() }
        });
#endif

        try
        {
            var loadTasks = new List<UniTask>();

            if (_playerManagerPrefab == null && IsReferenceLoadable(playerManagerPrefabRef))
            {
                loadTasks.Add(LoadPrefabAsync(playerManagerPrefabRef, prefab => _playerManagerPrefab = prefab, "PlayerManager"));
            }
            if (_gridPrefab == null && IsReferenceLoadable(gridPrefabRef))
            {
                loadTasks.Add(LoadPrefabAsync(gridPrefabRef, prefab => _gridPrefab = prefab, "Grid"));
            }
            if (_defaultMonsterPrefab == null && IsReferenceLoadable(defaultMonsterPrefabRef))
            {
                loadTasks.Add(LoadPrefabAsync(defaultMonsterPrefabRef, prefab => _defaultMonsterPrefab = prefab, "Monster"));
            }
            if (_waveDatabase == null && IsReferenceLoadable(waveDatabaseRef))
            {
                loadTasks.Add(LoadWaveDatabaseAsync());
            }

            if (loadTasks.Count > 0)
                await UniTask.WhenAll(loadTasks);

            GamePrefabsLoaded = AreGamePrefabCachesReady();

            if (GamePrefabsLoaded)
                Debug.Log("[AddressablesManager] 모든 게임 프리팹 로딩 완료!");
            else
                Debug.LogError($"[AddressablesManager] 게임 프리팹 로딩 불완전: {BuildGamePrefabCacheSummary()}");
        }
        catch (System.Exception ex)
        {
            GamePrefabsLoaded = AreGamePrefabCachesReady();
            Debug.LogError($"[AddressablesManager] 게임 프리팹 로딩 실패: {ex.Message} | {BuildGamePrefabCacheSummary()}");
        }
        finally
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            LogLoadMarker("game_prefabs_load_end", new Dictionary<string, object>
            {
                { "reason", _gamePrefabsLoadReason },
                { "elapsedMs", Mathf.RoundToInt((Time.realtimeSinceStartup - startTime) * 1000f) },
                { "loaded", GamePrefabsLoaded },
                { "cacheSummary", BuildGamePrefabCacheSummary() }
            });
#endif
            _gamePrefabsLoadReason = "direct";
            _gamePrefabsLoading = false;
        }
    }

    private async UniTask LoadPrefabAsync(AssetReference assetRef, System.Action<GameObject> onLoaded, string prefabName)
    {
        if (TryUseCachedAsset(assetRef, onLoaded, prefabName))
            return;

        if (await TryUseExistingOperationHandleAsync(assetRef, onLoaded, prefabName))
            return;

        var handle = assetRef.LoadAssetAsync<GameObject>();
        await handle.Task;

        if (handle.Status == AsyncOperationStatus.Succeeded && handle.Result != null)
        {
            onLoaded?.Invoke(handle.Result);
            Debug.Log($"[AddressablesManager] {prefabName} 프리팹 로드 성공");
        }
        else
        {
            Debug.LogError($"[AddressablesManager] {prefabName} 프리팹 로드 실패: {handle.OperationException?.Message}");
        }
    }

    private async UniTask LoadWaveDatabaseAsync()
    {
        if (TryUseCachedAsset<WaveDatabase>(waveDatabaseRef, database => _waveDatabase = database, "WaveDatabase"))
            return;

        if (await TryUseExistingOperationHandleAsync<WaveDatabase>(waveDatabaseRef, database => _waveDatabase = database, "WaveDatabase"))
            return;

        var handle = waveDatabaseRef.LoadAssetAsync<WaveDatabase>();
        await handle.Task;

        if (handle.Status == AsyncOperationStatus.Succeeded && handle.Result != null)
        {
            _waveDatabase = handle.Result;
            Debug.Log("[AddressablesManager] WaveDatabase 로드 완료");
        }
        else
        {
            Debug.LogError($"[AddressablesManager] WaveDatabase 로드 실패: {handle.OperationException?.Message}");
        }
    }

    private bool AreGamePrefabCachesReady()
    {
        return IsCacheReady(playerManagerPrefabRef, _playerManagerPrefab)
               && IsCacheReady(gridPrefabRef, _gridPrefab)
               && IsCacheReady(defaultMonsterPrefabRef, _defaultMonsterPrefab)
               && IsCacheReady(waveDatabaseRef, _waveDatabase);
    }

    private static bool IsCacheReady<T>(AssetReference assetRef, T cachedAsset) where T : class
    {
        return !IsReferenceLoadable(assetRef) || IsLoadedAssetValid(cachedAsset);
    }

    private static bool IsReferenceLoadable(AssetReference assetRef)
    {
        return assetRef != null && assetRef.RuntimeKeyIsValid();
    }

    private static bool IsLoadedAssetValid<T>(T asset) where T : class
    {
        if (asset == null)
            return false;

        if (asset is Object unityObject)
            return unityObject != null;

        return true;
    }

    private static bool TryUseCachedAsset<T>(AssetReference assetRef, System.Action<T> onLoaded, string assetName) where T : class
    {
        if (assetRef != null && assetRef.Asset is T cachedAsset && IsLoadedAssetValid(cachedAsset))
        {
            onLoaded?.Invoke(cachedAsset);
            Debug.Log($"[AddressablesManager] {assetName} cached Asset 재사용");
            return true;
        }

        return false;
    }

    private async UniTask<bool> TryUseExistingOperationHandleAsync<T>(AssetReference assetRef, System.Action<T> onLoaded, string assetName) where T : class
    {
        if (assetRef == null)
            return false;

        var handle = assetRef.OperationHandle;
        if (!handle.IsValid())
            return false;

        await handle.Task;

        if (handle.Status == AsyncOperationStatus.Succeeded && handle.Result is T loadedAsset && IsLoadedAssetValid(loadedAsset))
        {
            onLoaded?.Invoke(loadedAsset);
            Debug.Log($"[AddressablesManager] {assetName} OperationHandle 재사용");
            return true;
        }

        Debug.LogError($"[AddressablesManager] {assetName} 기존 OperationHandle 사용 실패: {handle.OperationException?.Message}");
        return true;
    }

    private string BuildGamePrefabCacheSummary()
    {
        return $"PlayerManager={IsLoadedAssetValid(_playerManagerPrefab)}, Grid={IsLoadedAssetValid(_gridPrefab)}, Monster={IsLoadedAssetValid(_defaultMonsterPrefab)}, WaveDatabase={IsLoadedAssetValid(_waveDatabase)}";
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private static void LogLoadMarker(string code, IDictionary<string, object> fields)
    {
        if (!MPTestCommandLine.IsEnabled)
        {
            return;
        }

        MPTestLogger.Log("load_marker", "pass", code, null, fields);
    }
#endif

    private static List<IResourceLocation> CollectAllObjectLocations()
    {
        var results = new List<IResourceLocation>();
        var seen = new HashSet<string>();

        foreach (var locator in Addressables.ResourceLocators)
        {
            foreach (var key in locator.Keys)
            {
                IList<IResourceLocation> locations;
                bool wasLogEnabled = Debug.unityLogger.logEnabled;
                try
                {
                    // Unity Editor's AddressableAssetSettingsLocator can emit missing-script warnings
                    // while probing keys. These entries are skipped below, so keep the console clean.
                    Debug.unityLogger.logEnabled = false;
                    if (!locator.Locate(key, typeof(Object), out locations))
                    {
                        continue;
                    }
                }
                catch (System.Exception)
                {
                    // Missing Script 등의 문제가 있는 에셋은 건너뛰기
                    continue;
                }
                finally
                {
                    Debug.unityLogger.logEnabled = wasLogEnabled;
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
