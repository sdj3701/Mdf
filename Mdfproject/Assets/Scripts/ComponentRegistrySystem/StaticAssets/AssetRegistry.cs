using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Tilemaps;


/// <summary>
/// 에셋(TileBase, Sprite 등)을 관리하는 별도 레지스트리
/// Addressable과 통합하여 사용
/// </summary>
public static class AssetRegistry
{
    private static Dictionary<string, TileBase> tiles = new Dictionary<string, TileBase>();
    private static Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();
    private static Dictionary<string, GameObject> prefabs = new Dictionary<string, GameObject>();
    private static Dictionary<string, AudioClip> sounds = new Dictionary<string, AudioClip>();

    #region Tile 관리

    public static void RegisterTile(string id, TileBase tile)
    {
        if (tile == null || string.IsNullOrEmpty(id)) return;

        tiles[id] = tile;
        Debug.Log($"🧩 타일 등록: {id}");
    }

    public static TileBase GetTile(string id)
    {
        return tiles.TryGetValue(id, out TileBase tile) ? tile : null;
    }

    public static bool HasTile(string id)
    {
        return tiles.ContainsKey(id);
    }

    /// <summary>
    /// Addressable을 통해 타일을 로드하고 등록
    /// </summary>
    public static async System.Threading.Tasks.Task<TileBase> LoadAndRegisterTile(string addressableKey, string registryId = null)
    {
        try
        {
            var handle = Addressables.LoadAssetAsync<TileBase>(addressableKey);
            var tile = await handle.Task;

            string id = registryId ?? addressableKey;
            RegisterTile(id, tile);

            return tile;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"타일 로드 실패: {addressableKey} - {e.Message}");
            Debug.LogWarning("혹은 Addressables가 설치되지 않았습니다.");

            return null;
        }
    }

    #endregion

    #region Sprite 관리

    public static void RegisterSprite(string id, Sprite sprite)
    {
        if (sprite == null || string.IsNullOrEmpty(id)) return;
        sprites[id] = sprite;
        Debug.Log($"🖼️ 스프라이트 등록: {id}");
    }

    public static Sprite GetSprite(string id)
    {
        return sprites.TryGetValue(id, out Sprite sprite) ? sprite : null;
    }

    public static async System.Threading.Tasks.Task<Sprite> LoadAndRegisterSprite(string addressableKey, string registryId = null)
    {
        try
        {
            var handle = Addressables.LoadAssetAsync<Sprite>(addressableKey);
            var tile = await handle.Task;

            string id = registryId ?? addressableKey;
            RegisterSprite(id, tile);

            return tile;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"타일 로드 실패: {addressableKey} - {e.Message}");
            Debug.LogWarning("혹은 Addressables가 설치되지 않았습니다.");

            return null;
        }
    }

    #endregion

    #region Prefab 관리

    public static void RegisterPrefab(string id, GameObject prefab)
    {
        if (prefab == null || string.IsNullOrEmpty(id)) return;
        prefabs[id] = prefab;
        Debug.Log($"🎮 프리팹 등록: {id}");
    }

    public static GameObject GetPrefab(string id)
    {
        return prefabs.TryGetValue(id, out GameObject prefab) ? prefab : null;
    }

    public static GameObject InstantiatePrefab(string id, Transform parent = null)
    {
        var prefab = GetPrefab(id);
        return prefab != null ? Object.Instantiate(prefab, parent) : null;
    }

    #endregion

    #region 유틸리티

    public static void PrintAssetStats()
    {
        Debug.Log("📊 AssetRegistry 통계:");
        Debug.Log($"   🧩 타일: {tiles.Count}개");
        Debug.Log($"   🖼️ 스프라이트: {sprites.Count}개");
        Debug.Log($"   🎮 프리팹: {prefabs.Count}개");
        Debug.Log($"   🔊 사운드: {sounds.Count}개");
    }

    public static void ClearAll()
    {
        tiles.Clear();
        sprites.Clear();
        prefabs.Clear();
        sounds.Clear();
        Debug.Log("🧹 AssetRegistry 초기화 완료");
    }

    /// <summary>
    /// Resources 폴더에서 에셋들을 일괄 로드 Addressable 안사용한거 
    /// </summary>
    public static void LoadFromResources()
    {
        // 타일들 로드
        var resourceTiles = Resources.LoadAll<TileBase>("Tiles");
        foreach (var tile in resourceTiles)
        {
            RegisterTile(tile.name, tile);
        }

        // 스프라이트들 로드
        var resourceSprites = Resources.LoadAll<Sprite>("Sprites");
        foreach (var sprite in resourceSprites)
        {
            RegisterSprite(sprite.name, sprite);
        }

        Debug.Log($"Resources에서 로드 완료: 타일 {resourceTiles.Length}개, 스프라이트 {resourceSprites.Length}개");
    }

    #endregion
}