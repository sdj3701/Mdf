using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// 게임 에셋들에 대한 타입 안전한 접근을 제공하는 전용 클래스
/// 이름은 에셋이름 그대로 사용
/// </summary>
public static class GameAssets 
{
    #region StaticAssets(TileBases, Sounds, Prefabs) 등..
        #region 타일 에셋들
        public static class Tiles
        {
            public static TileBase BreakWall => AssetRegistry.GetTile("BreakWall");
            // 필요시 캐싱도 가능
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

        public static class  Sprites
        {
            public static Sprite HeroSprite => AssetRegistry.GetSprite("HeroSprite");
        }
        #endregion
    #endregion

    #region DynamicAssets(GameObject, TileMap, Camera) 등..
        #region TileMaps
        public static class  TileMaps
        {
            public static Tilemap BreakWallTilemap => ComponentRegistry.Get<Tilemap>("BreakWall Tilemap");
            public static Tilemap GroundTilemap => ComponentRegistry.Get<Tilemap>("Ground Tilemap");
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
