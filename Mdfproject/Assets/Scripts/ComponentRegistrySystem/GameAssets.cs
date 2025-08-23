// Assets/Scripts/ComponentRegistrySystem/GameAssets.cs
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// ���� �� ���µ鿡 ���� ���� ������ �����ϴ� �߾� Ŭ�����Դϴ�.
/// �̸� ������� ���°� ������Ʈ�� �����ɴϴ�.
/// </summary>
public static class GameAssets
{
    #region StaticAssets(TileBases, Sounds, Prefabs) ��..
    #region Ÿ�� ���µ�
    public static class Tiles
    {
        public static TileBase BreakWall => AssetRegistry.GetTile("BreakWall");
        // �ʿ�� ĳ�� ����
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

    #region DynamicAssets(GameObject, TileMap, Camera) ��..
    #region TileMaps
    public static class TileMaps
    {
        // [����] ĳ�� �� null üũ ���� �߰�
        private static Tilemap _breakWallTilemap;
        private static Tilemap _groundTilemap;

        public static Tilemap BreakWallTilemap
        {
            get
            {
                // ĳ�õ� ���� ���ų� �ı��Ǿ��ٸ� �ٽ� ã���ϴ�.
                if (_breakWallTilemap == null)
                {
                    _breakWallTilemap = ComponentRegistry.Get<Tilemap>("BreakWall Tilemap");
                    if (_breakWallTilemap == null)
                    {
                        // Ÿ�ϸ��� ã�� ���ϸ� ���⼭ ��� ������ ����Ͽ� ���� �ľ��� ���� �մϴ�.
                        Debug.LogError("[GameAssets] 'BreakWall Tilemap'�� ã�� �� �����ϴ�! ���� Ÿ�ϸ� ������Ʈ �̸��� TilemapController.cs ��ũ��Ʈ ���� ���θ� Ȯ���ϼ���.");
                    }
                }
                return _breakWallTilemap;
            }
        }

        public static Tilemap GroundTilemap
        {
            get
            {
                // ĳ�õ� ���� ���ų� �ı��Ǿ��ٸ� �ٽ� ã���ϴ�.
                if (_groundTilemap == null)
                {
                    _groundTilemap = ComponentRegistry.Get<Tilemap>("Ground Tilemap");
                    if (_groundTilemap == null)
                    {
                        // Ÿ�ϸ��� ã�� ���ϸ� ���⼭ ��� ������ ����Ͽ� ���� �ľ��� ���� �մϴ�.
                        Debug.LogError("[GameAssets] 'Ground Tilemap'�� ã�� �� �����ϴ�! ���� Ÿ�ϸ� ������Ʈ �̸��� TilemapController.cs ��ũ��Ʈ ���� ���θ� Ȯ���ϼ���.");
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