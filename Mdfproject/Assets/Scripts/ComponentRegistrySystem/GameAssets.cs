using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// ���� ���µ鿡 ���� Ÿ�� ������ ������ �����ϴ� ���� Ŭ����
/// �̸��� �����̸� �״�� ���
/// </summary>
public static class GameAssets 
{
    #region StaticAssets(TileBases, Sounds, Prefabs) ��..
        #region Ÿ�� ���µ�
        public static class Tiles
        {
            public static TileBase BreakWall => AssetRegistry.GetTile("BreakWall");
            // �ʿ�� ĳ�̵� ����
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

    #region DynamicAssets(GameObject, TileMap, Camera) ��..
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
