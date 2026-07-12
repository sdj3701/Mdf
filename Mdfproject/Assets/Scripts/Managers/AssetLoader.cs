using System;
using Cysharp.Threading.Tasks;
using MDF.Runtime.Assets;
using UnityEngine;

/// <summary>
/// Compatibility facade for existing gameplay callers. Legacy loads are pinned until ClearCache
/// is called; new bounded owners should prefer AcquireAssetAsync.
/// </summary>
public static class AssetLoader
{
    public static async UniTask<T> LoadAssetAsync<T>(string key) where T : class
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        try
        {
            return await AddressableAssetCache.LoadPinnedAsync<T>(key);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception e)
        {
            Debug.LogError($"[AssetLoader] Failed to load '{key}' as {typeof(T).Name}: {e.Message}");
            return null;
        }
    }

    public static async UniTask<AddressableAssetLease<T>> AcquireAssetAsync<T>(string key) where T : class
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        try
        {
            return await AddressableAssetCache.AcquireAsync<T>(key);
        }
        catch (Exception e)
        {
            Debug.LogError($"[AssetLoader] Failed to acquire '{key}' as {typeof(T).Name}: {e.Message}");
            return null;
        }
    }

    public static T GetCachedAsset<T>(string key) where T : class
    {
        return AddressableAssetCache.GetCached<T>(key);
    }

    public static void ClearCache()
    {
        AddressableAssetCache.ClearPinnedAssets();
    }
}
