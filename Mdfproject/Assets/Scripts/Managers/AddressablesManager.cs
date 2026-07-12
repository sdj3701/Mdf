// Assets/Scripts/Managers/AddressablesManager.cs
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.Serialization;
using UnityEngine.SceneManagement;

public enum AddressablesBootLoadPolicy
{
    CatalogOnly = 0
}

public class AddressablesManager : MonoBehaviour
{
    public static AddressablesManager Instance { get; private set; }

    [Header("Addressables Initialization")]
    [SerializeField, FormerlySerializedAs("autoPreloadAllOnStart")] private bool autoInitializeOnStart = true;
    [SerializeField, FormerlySerializedAs("logPreloadProgress")] private bool logInitializationProgress = true;

    [Header("Game Prefabs (Addressables AssetReference)")]
    [SerializeField] private AssetReference playerManagerPrefabRef;
    [SerializeField] private AssetReference gridPrefabRef;
    [SerializeField] private AssetReference defaultMonsterPrefabRef;
    [SerializeField] private AssetReference waveDatabaseRef;

    public bool AssetsReady { get; private set; }
    public bool IsPreloading { get; private set; }
    public bool GamePrefabsLoaded { get; private set; }
    public static AddressablesBootLoadPolicy BootLoadPolicy => AddressablesBootLoadPolicy.CatalogOnly;

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

    private bool _initializationCompleted;
    private System.Exception _initializationFailure;
    private bool _gamePrefabsLoading;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            SceneManager.activeSceneChanged += HandleActiveSceneChanged;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private async void Start()
    {
        if (autoInitializeOnStart)
        {
            try
            {
                await InitializeAsync();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[AddressablesManager] Initialization failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Initializes the Addressables catalog without loading every registered asset.
    /// </summary>
    public async UniTask InitializeAsync()
    {
        if (_initializationCompleted)
        {
            AssetsReady = true;
            return;
        }

        if (IsPreloading)
        {
            await UniTask.WaitUntil(() => !IsPreloading);

            if (!_initializationCompleted)
            {
                throw _initializationFailure ?? new System.InvalidOperationException("Addressables initialization did not complete.");
            }

            return;
        }

        IsPreloading = true;
        AssetsReady = false;

        if (logInitializationProgress)
        {
            Debug.Log("[AddressablesManager] Catalog initialization started.");
        }

        try
        {
            var initHandle = Addressables.InitializeAsync(false);
            try
            {
                await initHandle.Task;

                if (initHandle.Status != AsyncOperationStatus.Succeeded)
                {
                    throw initHandle.OperationException ??
                          new System.InvalidOperationException("Addressables initialization failed.");
                }
            }
            finally
            {
                if (initHandle.IsValid())
                {
                    Addressables.Release(initHandle);
                }
            }

            AssetsReady = true;
            _initializationCompleted = true;
            _initializationFailure = null;
            if (logInitializationProgress)
            {
                Debug.Log("[AddressablesManager] Catalog initialization completed. Assets will load on demand.");
            }
        }
        catch (System.Exception e)
        {
            _initializationFailure = e;
            throw;
        }
        finally
        {
            IsPreloading = false;
        }
    }

    /// <summary>
    /// Backward-compatible entry point. This now initializes only the catalog and does not preload all assets.
    /// </summary>
    [System.Obsolete("Use InitializeAsync. Full-catalog preload has been removed.")]
    public UniTask PreloadAllAsync()
    {
        return InitializeAsync();
    }

    /// <summary>
    /// 게임에서 사용하는 핵심 프리팹들을 로드합니다.
    /// </summary>
    public async UniTask<bool> LoadGamePrefabsAsync()
    {
        if (GamePrefabsLoaded && AreGamePrefabCachesReady())
            return true;

        if (_gamePrefabsLoading)
        {
            await UniTask.WaitUntil(() => !_gamePrefabsLoading);

            if (AreGamePrefabCachesReady())
            {
                GamePrefabsLoaded = true;
                return true;
            }
        }

        Debug.Log("[AddressablesManager] 게임 프리팹 로딩 시작...");
        _gamePrefabsLoading = true;

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
            _gamePrefabsLoading = false;
        }

        return GamePrefabsLoaded;
    }

    private async UniTask LoadPrefabAsync(AssetReference assetRef, System.Action<GameObject> onLoaded, string prefabName)
    {
        if (TryUseCachedAsset(assetRef, onLoaded, prefabName))
            return;

        if (await TryUseExistingOperationHandleAsync(assetRef, onLoaded, prefabName))
            return;

        GameObject loaded = await LoadReferenceWithRetryAsync<GameObject>(assetRef, prefabName);
        onLoaded?.Invoke(loaded);
        Debug.Log($"[AddressablesManager] Required prefab loaded: {prefabName}");
    }

    private async UniTask LoadWaveDatabaseAsync()
    {
        if (TryUseCachedAsset<WaveDatabase>(waveDatabaseRef, database => _waveDatabase = database, "WaveDatabase"))
            return;

        if (await TryUseExistingOperationHandleAsync<WaveDatabase>(waveDatabaseRef, database => _waveDatabase = database, "WaveDatabase"))
            return;

        _waveDatabase = await LoadReferenceWithRetryAsync<WaveDatabase>(waveDatabaseRef, "WaveDatabase");
        Debug.Log("[AddressablesManager] Required WaveDatabase loaded.");
    }

    private static async UniTask<T> LoadReferenceWithRetryAsync<T>(AssetReference assetReference, string assetName)
        where T : class
    {
        System.Exception lastFailure = null;
        for (int attempt = 1; attempt <= 2; attempt++)
        {
            AsyncOperationHandle<T> handle = assetReference.LoadAssetAsync<T>();
            try
            {
                T loaded = await handle.Task;
                if (handle.Status == AsyncOperationStatus.Succeeded && IsLoadedAssetValid(loaded))
                {
                    return loaded;
                }

                lastFailure = handle.OperationException ??
                              new System.InvalidOperationException($"Addressables returned no asset for {assetName}.");
            }
            catch (System.Exception exception)
            {
                lastFailure = exception;
            }

            ReleaseAssetReference(assetReference);
            if (attempt < 2)
            {
                Debug.LogWarning($"[AddressablesManager] {assetName} load attempt {attempt} failed; retrying once. {lastFailure?.Message}");
                await UniTask.Yield();
            }
        }

        throw new System.InvalidOperationException(
            $"Addressables failed to load required asset {assetName} after retry.",
            lastFailure);
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
        return IsReferenceLoadable(assetRef) && IsLoadedAssetValid(cachedAsset);
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

        AsyncOperationHandle handle = assetRef.OperationHandle;
        if (!handle.IsValid())
            return false;

        try
        {
            await handle.Task;
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning($"[AddressablesManager] {assetName} existing operation failed and will be retried: {exception.Message}");
            ReleaseAssetReference(assetRef);
            return false;
        }

        if (handle.Status == AsyncOperationStatus.Succeeded && handle.Result is T loadedAsset && IsLoadedAssetValid(loadedAsset))
        {
            onLoaded?.Invoke(loadedAsset);
            Debug.Log($"[AddressablesManager] Reused existing operation: {assetName}");
            return true;
        }

        Debug.LogWarning($"[AddressablesManager] {assetName} existing operation is unusable and will be retried: {handle.OperationException?.Message}");
        ReleaseAssetReference(assetRef);
        return false;
    }

    private string BuildGamePrefabCacheSummary()
    {
        return $"PlayerManager={IsLoadedAssetValid(_playerManagerPrefab)}, Grid={IsLoadedAssetValid(_gridPrefab)}, Monster={IsLoadedAssetValid(_defaultMonsterPrefab)}, WaveDatabase={IsLoadedAssetValid(_waveDatabase)}";
    }

    /// <summary>
    /// 어드레서블 에셋을 로드하고 지정된 부모 아래에 인스턴스화합니다.
    /// </summary>
    public async UniTask<GameObject> LoadObject(string name, Transform parent = null)
    {
        GameObject prefab = await AssetLoader.LoadAssetAsync<GameObject>(name);
        if (prefab == null)
        {
            return null;
        }

        return Instantiate(prefab, parent);
    }

    private void OnDestroy()
    {
        if (Instance != this)
        {
            return;
        }

        ReleaseAssetReference(playerManagerPrefabRef);
        ReleaseAssetReference(gridPrefabRef);
        ReleaseAssetReference(defaultMonsterPrefabRef);
        ReleaseAssetReference(waveDatabaseRef);
        SceneManager.activeSceneChanged -= HandleActiveSceneChanged;
        AssetLoader.ClearCache();
        Instance = null;
    }

    private static void HandleActiveSceneChanged(Scene previousScene, Scene nextScene)
    {
        if (IsGameScene(previousScene.name) && !IsGameScene(nextScene.name))
        {
            AssetLoader.ClearCache();
        }
    }

    private static bool IsGameScene(string sceneName)
    {
        return string.Equals(sceneName, "03_Game", System.StringComparison.Ordinal) ||
               string.Equals(sceneName, "Game", System.StringComparison.Ordinal);
    }

    private static void ReleaseAssetReference(AssetReference assetReference)
    {
        if (assetReference != null && assetReference.OperationHandle.IsValid())
        {
            assetReference.ReleaseAsset();
        }
    }
}
