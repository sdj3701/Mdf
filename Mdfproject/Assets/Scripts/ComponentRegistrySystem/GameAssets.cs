// Assets/Scripts/ComponentRegistrySystem/GameAssets.cs
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// 게임 내 에셋들에 대한 정적 접근을 제공하는 중앙 클래스입니다.
/// 이름 기반으로 에셋과 컴포넌트를 가져옵니다.
/// </summary>
public static class GameAssets
{
    #region StaticAssets(TileBases, Sounds, Prefabs) 등..
    #region 타일 에셋들
    public static class Tiles
    {
        public static TileBase BreakWall => AssetRegistry.GetTile("BreakWall");
        // 필요시 캐싱 예제
        private static TileBase _cachedBreakWall;
        public static TileBase BreakWallCached
        {
            get
            {
                if (_cachedBreakWall == null)
                    _cachedBreakWall = AssetRegistry.GetTile("BreakWall");
                return _cachedBreakWall;
            }
        }
    }

    public static class Sprites
    {
        public static Sprite HeroSprite => AssetRegistry.GetSprite("HeroSprite");
    }
    #endregion
    #endregion

    #region DynamicAssets(GameObject, TileMap, Camera) 등..
    #region TileMaps
    public static class TileMaps
    {
        // [개선] 캐싱 및 null 체크 로직 추가
        private static Tilemap _breakWallTilemap;
        private static Tilemap _groundTilemap;

        public static Tilemap BreakWallTilemap
        {
            get
            {
                // 캐시된 값이 없거나 파괴되었다면 다시 찾습니다.
                if (_breakWallTilemap == null)
                {
                    _breakWallTilemap = ComponentRegistry.Get<Tilemap>("BreakWall Tilemap");
                    if (_breakWallTilemap == null)
                    {
                        // 타일맵을 찾지 못하면 여기서 즉시 에러를 출력하여 문제 파악을 쉽게 합니다.
                        Debug.LogError("[GameAssets] 'BreakWall Tilemap'을 찾을 수 없습니다! 씬의 타일맵 오브젝트 이름과 TilemapController.cs 스크립트 부착 여부를 확인하세요.");
                    }
                }
                return _breakWallTilemap;
            }
        }

        public static Tilemap GroundTilemap
        {
            get
            {
                // 캐시된 값이 없거나 파괴되었다면 다시 찾습니다.
                if (_groundTilemap == null)
                {
                    _groundTilemap = ComponentRegistry.Get<Tilemap>("Ground Tilemap");
                    if (_groundTilemap == null)
                    {
                        // 타일맵을 찾지 못하면 여기서 즉시 에러를 출력하여 문제 파악을 쉽게 합니다.
                        Debug.LogError("[GameAssets] 'Ground Tilemap'을 찾을 수 없습니다! 씬의 타일맵 오브젝트 이름과 TilemapController.cs 스크립트 부착 여부를 확인하세요.");
                    }
                }
                return _groundTilemap;
            }
        }
    }
    #endregion

    #region Cameras
    public static class Cameras
    {
        public static Camera MainCamera => ComponentRegistry.Get<Camera>("Main Camera");
    }
    #endregion

    #region UI
    public static class UI
    {
        public static TMP_Text CurrentBreakWall => ComponentRegistry.Get<TMP_Text>("CurrentBreakWall");
    }
    #endregion
    #endregion
}