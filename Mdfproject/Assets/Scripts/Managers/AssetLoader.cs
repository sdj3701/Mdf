using UnityEngine;
using UnityEngine.AddressableAssets;
using System.Collections.Generic;
using Cysharp.Threading.Tasks; // UniTask 사용

public static class AssetLoader
{
    // 로드된 에셋을 캐싱하여 중복 로드를 방지하는 딕셔너리
    private static readonly Dictionary<string, object> _assetCache = new Dictionary<string, object>();

    /// <summary>
    /// 어드레서블 키를 사용해 에셋을 비동기적으로 로드합니다. 이미 로드된 에셋은 캐시에서 즉시 반환합니다.
    /// </summary>
    /// <typeparam name="T">로드할 에셋의 타입 (GameObject, Sprite, SkillData 등)</typeparam>
    /// <param name="key">어드레서블 주소(키)</param>
    /// <returns>로드된 에셋</returns>
    public static async UniTask<T> LoadAssetAsync<T>(string key) where T : class
    {
        if (string.IsNullOrEmpty(key))
        {
            return null;
        }

        // 1. 캐시에 이미 에셋이 있는지 확인
        if (_assetCache.TryGetValue(key, out object cachedAsset))
        {
            return cachedAsset as T;
        }

        // 2. 캐시에 없으면 어드레서블을 통해 새로 로드
        try
        {
            var handle = Addressables.LoadAssetAsync<T>(key);
            T loadedAsset = await handle.Task;

            // 3. 로드 성공 시 캐시에 저장
            if (loadedAsset != null)
            {
                _assetCache[key] = loadedAsset;
            }
            
            return loadedAsset;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"에셋 로드 실패: '{key}' (타입: {typeof(T).Name}). 에러: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// 이미 캐시에 로드된 에셋을 동기적으로 반환합니다. 캐시에 없으면 null 반환.
    /// </summary>
    /// <typeparam name="T">에셋 타입</typeparam>
    /// <param name="key">어드레서블 주소(키)</param>
    /// <returns>캐시된 에셋 또는 null</returns>
    public static T GetCachedAsset<T>(string key) where T : class
    {
        if (string.IsNullOrEmpty(key))
        {
            return null;
        }

        if (_assetCache.TryGetValue(key, out object cachedAsset))
        {
            return cachedAsset as T;
        }

        return null;
    }

    // 참고: 씬이 바뀔 때 캐시를 비워주는 로직을 추가할 수도 있습니다.
    public static void ClearCache()
    {
        _assetCache.Clear();
        // Addressables.ClearDependencyCacheAsync(); // 필요 시 더 강력한 캐시 클리어
    }
}