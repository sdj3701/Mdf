using System;
using Cysharp.Threading.Tasks;
using MDF.Runtime.Assets;
using UnityEngine;

/// <summary>
/// Addressables facade. Every cached load must have either an explicit owner or an explicit lease.
/// </summary>
public static class AssetLoader
{
    public static async UniTask<T> LoadAssetAsync<T>(string key, AddressableAssetOwner owner) where T : class
    {
        if (string.IsNullOrWhiteSpace(key) || owner == null)
        {
            return null;
        }

        try
        {
            return await owner.LoadAsync<T>(key);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (ObjectDisposedException)
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

}
