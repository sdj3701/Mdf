using System.Collections;
using System.Collections.Generic;
using GameCore.Enums;
using UnityEngine;
using UnityEngine.Tilemaps;

public class CharacterPlacementHandler : MonoBehaviour, IPlacementHandler
{
    private PlacementManager placementManager;
    private GameObject previewObject;

    private void Start()
    {
        placementManager = PlacementManager.Instance;

        // 이벤트 구독
        placementManager.OnMousePositionChanged += OnMousePositionChanged;
        placementManager.OnPlacementModeChanged += OnModeChanged;

        CreatePreviewObject();
    }

    private void OnDestroy()
    {
        // 이벤트 구독 해제
        if (placementManager != null)
        {
            placementManager.OnMousePositionChanged -= OnMousePositionChanged;
            placementManager.OnPlacementModeChanged -= OnModeChanged;
        }
    }

    public bool CanHandle(PlacementMode mode)
    {
        return mode == PlacementMode.Character;
    }

    public void HandleInput()
    {
        // 좌클릭: 캐릭터 생성
        if (Input.GetMouseButtonDown(0))
        {
            CreateCharacter();
        }

        // 우클릭: 캐릭터 제거
        if (Input.GetMouseButtonDown(1))
        {
            RemoveCharacter();
        }

        // 가운데 클릭: 정보 확인
        if (Input.GetMouseButtonDown(2))
        {
            CheckCharacterInfo();
        }
    }

    public void OnMousePositionChanged(Vector3Int gridPosition)
    {
        if (placementManager.CurrentPlacementMode == PlacementMode.Character)
        {
            UpdatePreview();
        }
    }

    public void OnModeChanged(PlacementMode mode)
    {
        if (previewObject != null)
        {
            previewObject.SetActive(mode == PlacementMode.Character && placementManager.ShowPreview);
        }
    }

    private void CreateCharacter()
    {
        Vector3Int currentPos = placementManager.CurrentMouseGridPosition;

        if (!placementManager.IsGroundLayer())
        {
            Debug.LogError("❌ Ground가 아닌 곳에는 캐릭터를 배치할 수 없습니다!");
            return;
        }

        TileBase existingWallTile = GameAssets.TileMaps.BreakWallTilemap.GetTile(currentPos);

        if (existingWallTile != null)
        {
            Debug.LogError("❌ 벽이 있는 곳에는 캐릭터를 배치할 수 없습니다!");
            return;
        }

        if (PlacementManager.SpawnedCharacters.ContainsKey(currentPos))
        {
            Debug.LogWarning($"⚠️ 이미 캐릭터가 존재합니다: {currentPos}");
            return;
        }

        GameObject newCharacter = CreateCharacterObject(currentPos);

        if (newCharacter != null)
        {
            PlacementManager.SpawnedCharacters[currentPos] = newCharacter;
            newCharacter.name = $"Character_{currentPos.x}_{currentPos.y}";
            Debug.Log($"✅ 캐릭터 생성 성공: {currentPos}");
        }
    }

    private GameObject CreateCharacterObject(Vector3Int gridPosition)
    {
        Vector3 worldPosition = GameAssets.TileMaps.BreakWallTilemap.CellToWorld(gridPosition);
        worldPosition += GameAssets.TileMaps.BreakWallTilemap.cellSize * 0.5f;

        GameObject newCharacter = null;

        if (placementManager.GetCharacterPrefab() != null)
        {
            newCharacter = Instantiate(placementManager.GetCharacterPrefab(), worldPosition, Quaternion.identity);
        }
        else if (GameAssets.Sprites.HeroSprite != null)
        {
            newCharacter = new GameObject($"Character_{gridPosition}");
            newCharacter.transform.position = worldPosition;

            SpriteRenderer spriteRenderer = newCharacter.AddComponent<SpriteRenderer>();
            spriteRenderer.sprite = GameAssets.Sprites.HeroSprite;
            spriteRenderer.sortingOrder = (int)placementManager.CharacterSortingOrder;

            BoxCollider2D collider = newCharacter.AddComponent<BoxCollider2D>();
            collider.size = GameAssets.Sprites.HeroSprite.bounds.size;
        }
        else
        {
            Debug.LogError("❌ 캐릭터 스프라이트나 프리팹이 설정되지 않았습니다!");
        }

        return newCharacter;
    }

    private void RemoveCharacter()
    {
        Vector3Int currentPos = placementManager.CurrentMouseGridPosition;

        if (PlacementManager.SpawnedCharacters.ContainsKey(currentPos))
        {
            GameObject characterToRemove = PlacementManager.SpawnedCharacters[currentPos];

            if (characterToRemove != null)
            {
                Destroy(characterToRemove);
            }

            PlacementManager.SpawnedCharacters.Remove(currentPos);
            Debug.Log($"🗑️ 캐릭터 제거: {currentPos}");
        }
        else
        {
            Debug.Log($"❌ 제거할 캐릭터가 없습니다: {currentPos}");
        }
    }

    private void CheckCharacterInfo()
    {
        Vector3Int currentPos = placementManager.CurrentMouseGridPosition;

        if (PlacementManager.SpawnedCharacters.ContainsKey(currentPos))
        {
            GameObject character = PlacementManager.SpawnedCharacters[currentPos];
            Vector3 worldPosition = GameAssets.TileMaps.BreakWallTilemap.CellToWorld(currentPos);

            Debug.Log($"👤 캐릭터 정보:");
            Debug.Log($"   - 이름: {character.name}");
            Debug.Log($"   - 그리드 위치: {currentPos}");
            Debug.Log($"   - 월드 위치: {worldPosition}");
            Debug.Log($"   - 실제 위치: {character.transform.position}");
        }
        else
        {
            Debug.Log($"❌ 캐릭터 없음 - 그리드: {currentPos}");
        }
    }

    private void CreatePreviewObject()
    {
        previewObject = new GameObject("CharacterPreview");

        SpriteRenderer spriteRenderer = previewObject.AddComponent<SpriteRenderer>();

        if (GameAssets.Sprites.HeroSprite != null)
        {
            spriteRenderer.sprite = GameAssets.Sprites.HeroSprite;
            spriteRenderer.color = new Color(1f, 1f, 1f, 0.5f);
        }
        else
        {
            Texture2D texture = new Texture2D(1, 1);
            texture.SetPixel(0, 0, Color.white);
            texture.Apply();

            Sprite previewSprite = Sprite.Create(texture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1);
            spriteRenderer.sprite = previewSprite;
            spriteRenderer.color = placementManager.PreviewColor;
        }

        spriteRenderer.sortingOrder = (int)placementManager.CharacterSortingOrder + 1;
        previewObject.SetActive(false);
    }

    private void UpdatePreview()
    {
        if (previewObject == null) return;

        Vector3Int currentPos = placementManager.CurrentMouseGridPosition;
        Vector3 previewWorldPosition = GameAssets.TileMaps.BreakWallTilemap.CellToWorld(currentPos);
        previewWorldPosition += GameAssets.TileMaps.BreakWallTilemap.cellSize * 0.5f;
        previewObject.transform.position = previewWorldPosition;

        SpriteRenderer spriteRenderer = previewObject.GetComponent<SpriteRenderer>();

        if (PlacementManager.SpawnedCharacters.ContainsKey(currentPos))
        {
            spriteRenderer.color = new Color(1f, 0f, 0f, 0.5f);  // 빨간색 (이미 캐릭터 있음)
        }
        else if (GameAssets.TileMaps.BreakWallTilemap.GetTile(currentPos) != null)
        {
            spriteRenderer.color = new Color(0.5f, 0f, 0.5f, 0.5f);  // 보라색 (벽이 있음)
        }
        else if (placementManager.IsGroundLayer())
        {
            spriteRenderer.color = new Color(0f, 1f, 0f, 0.5f);  // 초록색 (배치 가능)
        }
        else
        {
            spriteRenderer.color = new Color(1f, 1f, 0f, 0.5f);  // 노란색 (Ground 없음)
        }

        previewObject.SetActive(true);
    }
}
