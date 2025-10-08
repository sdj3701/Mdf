using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Tilemap을 관리하는 범용 컨트롤러 클래스
/// Unity의 Tilemap 컴포넌트를 ComponentRegistry 시스템에 연결
/// </summary>
public class TilemapController : RegisteredComponent
{
    [Header("Tilemap 설정")]
    [SerializeField] private Tilemap tilemap;
    [SerializeField] private TilemapRenderer tilemapRenderer;
    [SerializeField] private string tilemapType; // Ground, Wall, Decoration 등

    private int _ownerPlayerId = -1; // 소유자 플레이어 ID

    protected override void Awake()
    {
        // Tilemap 컴포넌트 자동 할당
        if (tilemap == null) tilemap = GetComponent<Tilemap>();
        if (tilemapRenderer == null) tilemapRenderer = GetComponent<TilemapRenderer>();
        
        // tilemapType이 인스펙터에서 설정되지 않은 경우에만 게임 오브젝트 이름을 기본값으로 사용
        if (string.IsNullOrEmpty(tilemapType))
        {
            tilemapType = this.gameObject.name;
        }

        // Awake에서는 기본 ID로 등록
        componentId = tilemapType;

        base.Awake();
    }

    // 플레이어 ID를 받아 고유 ID로 재등록하는 메서드
    public void SetPlayerOwner(int playerId)
    {
        // 이미 소유자가 설정되었으면 변경하지 않음
        if (_ownerPlayerId != -1) return;

        _ownerPlayerId = playerId;

        // 기존의 일반 ID 등록 해제
        UnregisterSelf();

        // 플레이어 ID를 포함한 고유 ID 생성
        componentId = $"P{_ownerPlayerId}_{tilemapType}";

        // 새로운 고유 ID로 다시 등록
        RegisterSelf();
    }

    public override void RegisterSelf()
    {
        // TilemapController 자체 등록
        ComponentRegistry.Register<TilemapController>(componentId, this);

        // ⭐ Tilemap 컴포넌트도 별도로 등록 (GameAssets에서 사용)
        if (tilemap != null)
        {
            ComponentRegistry.Register<Tilemap>(componentId, tilemap);
        }

        // TilemapRenderer도 등록 (필요시)
        if (tilemapRenderer != null)
        {
            ComponentRegistry.Register<TilemapRenderer>($"{componentId}Renderer", tilemapRenderer);
        }
    }

    public override void UnregisterSelf()
    {
        ComponentRegistry.Unregister<TilemapController>(componentId);
        ComponentRegistry.Unregister<Tilemap>(componentId);
        ComponentRegistry.Unregister<TilemapRenderer>($"{componentId}Renderer");
    }

    // Tilemap 조작들을 래핑
    public void SetTile(Vector3Int position, TileBase tile)
    {
        tilemap.SetTile(position, tile);
    }

    public TileBase GetTile(Vector3Int position)
    {
        return tilemap.GetTile(position);
    }

    public void SetTilesBlock(BoundsInt area, TileBase[] tileArray)
    {
        tilemap.SetTilesBlock(area, tileArray);
    }

    // 에셋 레지스트리 연동
    public void SetTileFromRegistry(Vector3Int position, string tileId)
    {
        var tile = AssetRegistry.GetTile(tileId);
        if (tile != null)
        {
            SetTile(position, tile);
        }
        else
        {
            Debug.LogWarning($"타일을 찾을 수 없음: {tileId}");
        }
    }

    public bool HasTileAt(Vector3Int position)
    {
        return tilemap.HasTile(position);
    }

    public void ClearArea(BoundsInt area)
    {
        tilemap.SetTilesBlock(area, new TileBase[area.size.x * area.size.y * area.size.z]);
    }

    // 공개용 프로퍼티
    public Tilemap Tilemap => tilemap;
    public TilemapRenderer Renderer => tilemapRenderer;
    public string Type => tilemapType;
}
