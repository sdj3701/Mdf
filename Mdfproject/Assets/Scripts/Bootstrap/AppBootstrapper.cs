using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(-1000)]
public sealed class AppBootstrapper : MonoBehaviour
{
    public static bool IsBootReady { get; private set; }
    public static bool IsBootFailed { get; private set; }
    public static string LastBootError { get; private set; } = string.Empty;

    private static bool _sceneHookRegistered;
    private static AppBootstrapper _instance;

    private bool _bootStarted;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterSceneHook()
    {
        if (_sceneHookRegistered)
        {
            return;
        }

        _sceneHookRegistered = true;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != SceneDefine.Title)
        {
            return;
        }

        EnsureExistsInScene();
    }

    public static void EnsureExistsInScene()
    {
        if (_instance != null)
        {
            _instance.BootIfNeeded().Forget();
            return;
        }

        var existing = FindObjectOfType<AppBootstrapper>(true);
        if (existing != null)
        {
            _instance = existing;
            DontDestroyOnLoad(_instance.gameObject);
            _instance.BootIfNeeded().Forget();
            return;
        }

        var go = new GameObject(nameof(AppBootstrapper));
        _instance = go.AddComponent<AppBootstrapper>();
        DontDestroyOnLoad(go);
    }

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        BootIfNeeded().Forget();
    }

    private async UniTaskVoid BootIfNeeded()
    {
        if (_bootStarted || IsBootReady)
        {
            return;
        }

        _bootStarted = true;
        IsBootFailed = false;
        LastBootError = string.Empty;

        try
        {
            if (!EnsureNetworkManager())
            {
                FailBoot("NetworkManager is missing in Title scene.");
                _bootStarted = false;
                return;
            }

            if (!EnsureAddressablesManager())
            {
                FailBoot("AddressablesManager is missing in Title scene.");
                _bootStarted = false;
                return;
            }

            if (!EnsureLoadManager())
            {
                FailBoot("LoadManager could not be prepared.");
                _bootStarted = false;
                return;
            }

            if (AddressablesManager.Instance.AutoPreloadAllOnStart)
            {
                await AddressablesManager.Instance.PreloadAllAsync();
            }
            await LoadManager.Instance.InitializeAsync();

            IsBootReady = true;
            Debug.Log("[AppBootstrapper] Boot completed.");
        }
        catch (Exception e)
        {
            FailBoot(e.Message);
            _bootStarted = false;
        }
    }

    private static bool EnsureNetworkManager()
    {
        if (NetworkManager.Instance != null)
        {
            if (!NetworkManager.Instance.gameObject.activeSelf)
            {
                NetworkManager.Instance.gameObject.SetActive(true);
            }

            return true;
        }

        var sceneManagers = FindObjectsOfType<NetworkManager>(true);
        for (int i = 0; i < sceneManagers.Length; i++)
        {
            if (sceneManagers[i] == null)
            {
                continue;
            }

            if (!sceneManagers[i].gameObject.activeSelf)
            {
                sceneManagers[i].gameObject.SetActive(true);
            }

            return true;
        }

        return false;
    }

    private static bool EnsureAddressablesManager()
    {
        if (AddressablesManager.Instance != null)
        {
            if (!AddressablesManager.Instance.gameObject.activeSelf)
            {
                AddressablesManager.Instance.gameObject.SetActive(true);
            }

            return true;
        }

        var managers = FindObjectsOfType<AddressablesManager>(true);
        for (int i = 0; i < managers.Length; i++)
        {
            if (managers[i] == null)
            {
                continue;
            }

            if (!managers[i].gameObject.activeSelf)
            {
                managers[i].gameObject.SetActive(true);
            }

            return true;
        }

        return false;
    }

    private static bool EnsureLoadManager()
    {
        if (LoadManager.Instance != null)
        {
            if (!LoadManager.Instance.gameObject.activeSelf)
            {
                LoadManager.Instance.gameObject.SetActive(true);
            }

            return true;
        }

        var managers = FindObjectsOfType<LoadManager>(true);
        for (int i = 0; i < managers.Length; i++)
        {
            if (managers[i] == null)
            {
                continue;
            }

            if (!managers[i].gameObject.activeSelf)
            {
                managers[i].gameObject.SetActive(true);
            }

            return true;
        }

        var fallback = new GameObject("LoadManager");
        fallback.AddComponent<LoadManager>();
        return LoadManager.Instance != null;
    }

    private static void FailBoot(string message)
    {
        IsBootReady = false;
        IsBootFailed = true;
        LastBootError = message ?? string.Empty;
        Debug.LogError($"[AppBootstrapper] Boot failed: {LastBootError}");
    }
}
