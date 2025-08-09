using UnityEngine;
using UnityEngine.Tilemaps;
using System.Collections.Generic;

public class TileAndCharacterGenerator : GameDataCenter
{
    private bool CheckCreateCharacter = true;

    void Start()
    {
        // 컴포넌트 자동 할당
        if (BreakWalltilemap == null)
            BreakWalltilemap = FindObjectOfType<Tilemap>();

        if (PlayerCamera == null)
            PlayerCamera = Camera.main;

        // 프리뷰 오브젝트 생성
        if (ShowPreview)
            CreatePreviewObject();
    }

    void Update()
    {
        if (IsUnitPlacement)
        {
            // 마우스 위치 업데이트
            UpdateMousePosition();

            // 마우스 입력 처리
            HandleMouseInput();

            // 프리뷰 업데이트 (유닛 배치 모드일 때만)
            if (ShowPreview)
                UpdatePreview();
        }
        else
        {
            // 유닛 배치 모드가 아닐 때는 프리뷰 비활성화
            if (ShowPreview && PreviewObject != null)
                PreviewObject.SetActive(false);
        }
    }

    void CreatePreviewObject()
    {
        // 프리뷰용 오브젝트 생성
        PreviewObject = new GameObject("CharacterPreview");

        // 스프라이트 렌더러 추가
        SpriteRenderer spriteRenderer = PreviewObject.AddComponent<SpriteRenderer>();

        // 캐릭터 스프라이트가 설정되어 있으면 사용, 없으면 기본 사각형
        if (CharacterSprite != null)
        {
            spriteRenderer.sprite = CharacterSprite;
            spriteRenderer.color = new Color(1f, 1f, 1f, 0.5f); // 반투명하게
        }
        else
        {
            // 간단한 사각형 스프라이트 생성
            Texture2D texture = new Texture2D(1, 1);
            texture.SetPixel(0, 0, Color.white);
            texture.Apply();

            Sprite previewSprite = Sprite.Create(texture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1);
            spriteRenderer.sprite = previewSprite;
            spriteRenderer.color = PreviewColor;
        }

        // 캐릭터보다 위에 표시되도록 설정
        spriteRenderer.sortingOrder = (int)CharacterSortingOrder + 1;

        // 처음에는 비활성화
        PreviewObject.SetActive(false);
    }

    void UpdateMousePosition()
    {
        Vector3 mouseWorldPosition = GetMouseWorldPosition();
        CurrentMouseGridPosition = BreakWalltilemap.WorldToCell(mouseWorldPosition);
    }

    Vector3 GetMouseWorldPosition()
    {
        Vector3 mouseScreenPosition = Input.mousePosition;

        if (PlayerCamera.orthographic)
        {
            mouseScreenPosition.z = PlayerCamera.nearClipPlane;
            return PlayerCamera.ScreenToWorldPoint(mouseScreenPosition);
        }
        else
        {
            Ray ray = PlayerCamera.ScreenPointToRay(mouseScreenPosition);
            float distance = -PlayerCamera.transform.position.z / ray.direction.z;
            return ray.origin + ray.direction * distance;
        }
    }

    void HandleMouseInput()
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

        // 가운데 클릭: 캐릭터 정보 확인
        if (Input.GetMouseButtonDown(2))
        {
            CheckCharacterInfo();
            CheckGroundTileInfo();
        }
    }

    void CreateCharacter()
    {
        // Ground 레이어 확인
        bool isGroundLayer = IsGroundLayer();

        if (!isGroundLayer)
        {
            Debug.LogError("❌ Ground가 아닌 곳에는 캐릭터를 배치할 수 없습니다!");
            CheckCreateCharacter = false;
            return;
        }

        // BreakWall 타일이 있는지 확인 (벽이 있으면 캐릭터 배치 불가)
        TileBase existingWallTile = BreakWalltilemap.GetTile(CurrentMouseGridPosition);
        if (existingWallTile != null)
        {
            Debug.LogError("❌ 벽이 있는 곳에는 캐릭터를 배치할 수 없습니다!");
            CheckCreateCharacter = false;
            return;
        }

        // 해당 위치에 이미 캐릭터가 있는지 확인
        if (SpawnedCharacters.ContainsKey(CurrentMouseGridPosition))
        {
            Debug.LogWarning($"⚠️ 이미 캐릭터가 존재합니다: {CurrentMouseGridPosition}");
            CheckCreateCharacter = false;
            return;
        }

        GameObject newCharacter = null;

        // 프리팹이 설정되어 있으면 프리팹 사용, 없으면 스프라이트로 생성
        if (CharacterPrefab != null)
        {
            // 프리팹으로 캐릭터 생성
            Vector3 worldPosition = BreakWalltilemap.CellToWorld(CurrentMouseGridPosition);
            worldPosition += BreakWalltilemap.cellSize * 0.5f; // 타일 중앙에 배치

            newCharacter = Instantiate(CharacterPrefab, worldPosition, Quaternion.identity);
        }
        else if (CharacterSprite != null)
        {
            // 스프라이트로 캐릭터 생성
            newCharacter = new GameObject($"Character_{CurrentMouseGridPosition}");

            // 위치 설정
            Vector3 worldPosition = BreakWalltilemap.CellToWorld(CurrentMouseGridPosition);
            worldPosition += BreakWalltilemap.cellSize * 0.5f; // 타일 중앙에 배치
            newCharacter.transform.position = worldPosition;

            // 스프라이트 렌더러 추가 및 설정
            SpriteRenderer spriteRenderer = newCharacter.AddComponent<SpriteRenderer>();
            spriteRenderer.sprite = CharacterSprite;
            spriteRenderer.sortingOrder = (int)CharacterSortingOrder;

            // 선택적: 콜라이더 추가 (캐릭터 클릭 감지용)
            BoxCollider2D collider = newCharacter.AddComponent<BoxCollider2D>();
            collider.size = CharacterSprite.bounds.size;
        }
        else
        {
            Debug.LogError("❌ 캐릭터 스프라이트나 프리팹이 설정되지 않았습니다!");
            CheckCreateCharacter = false;
            return;
        }

        if (newCharacter != null)
        {
            // 생성된 캐릭터를 딕셔너리에 저장
            SpawnedCharacters[CurrentMouseGridPosition] = newCharacter;

            // 캐릭터에 위치 정보 태그 추가 (선택사항)
            newCharacter.name = $"Character_{CurrentMouseGridPosition.x}_{CurrentMouseGridPosition.y}";

            CheckCreateCharacter = true;
            Debug.Log($"✅ 캐릭터 생성 성공: {CurrentMouseGridPosition}");
        }
    }

    void RemoveCharacter()
    {
        // 해당 위치에 캐릭터가 있는지 확인
        if (SpawnedCharacters.ContainsKey(CurrentMouseGridPosition))
        {
            GameObject characterToRemove = SpawnedCharacters[CurrentMouseGridPosition];

            if (characterToRemove != null)
            {
                Destroy(characterToRemove);
            }

            SpawnedCharacters.Remove(CurrentMouseGridPosition);
            Debug.Log($"🗑️ 캐릭터 제거: {CurrentMouseGridPosition}");
        }
        else
        {
            Debug.Log($"❌ 제거할 캐릭터가 없습니다: {CurrentMouseGridPosition}");
        }
    }

    void CheckCharacterInfo()
    {
        if (SpawnedCharacters.ContainsKey(CurrentMouseGridPosition))
        {
            GameObject character = SpawnedCharacters[CurrentMouseGridPosition];
            Vector3 worldPosition = BreakWalltilemap.CellToWorld(CurrentMouseGridPosition);

            Debug.Log($"👤 캐릭터 정보:");
            Debug.Log($"   - 이름: {character.name}");
            Debug.Log($"   - 그리드 위치: {CurrentMouseGridPosition}");
            Debug.Log($"   - 월드 위치: {worldPosition}");
            Debug.Log($"   - 실제 위치: {character.transform.position}");
        }
        else
        {
            Debug.Log($"❌ 캐릭터 없음 - 그리드: {CurrentMouseGridPosition}");
        }
    }

    bool IsGroundLayer()
    {
        bool isGroundTilemap = Groundtilemap.gameObject.layer == LayerMask.NameToLayer("Ground");

        Vector3 mouseWorldPos = GetMouseWorldPosition();
        Vector3Int gridPosition = Groundtilemap.WorldToCell(mouseWorldPos);

        TileBase tile = Groundtilemap.GetTile(gridPosition);
        bool hasTile = tile != null;

        return isGroundTilemap && hasTile;
    }

    void CheckGroundTileInfo()
    {
        if (Groundtilemap == null)
        {
            Debug.LogError("❌ Ground 타일맵이 null입니다!");
            return;
        }

        Vector3 mouseWorldPos = GetMouseWorldPosition();
        Vector3Int gridPosition = Groundtilemap.WorldToCell(mouseWorldPos);
        TileBase groundTile = Groundtilemap.GetTile(gridPosition);
        Vector3 worldPosition = Groundtilemap.CellToWorld(gridPosition);

        Debug.Log($"🗺️ Ground 타일맵 정보:");
        Debug.Log($"   - 타일맵 이름: {Groundtilemap.gameObject.name}");
        Debug.Log($"   - 타일맵 레이어: {LayerMask.LayerToName(Groundtilemap.gameObject.layer)}");
        Debug.Log($"   - 마우스 월드 위치: {mouseWorldPos}");
        Debug.Log($"   - 그리드 위치: {gridPosition}");
        Debug.Log($"   - 셀 월드 위치: {worldPosition}");

        if (groundTile != null)
        {
            Debug.Log($"✅ Ground 타일 존재: {groundTile.name}");
        }
        else
        {
            Debug.Log($"❌ Ground 타일 없음");
        }
    }

    void UpdatePreview()
    {
        if (PreviewObject == null) return;

        // 프리뷰 오브젝트 위치 업데이트
        Vector3 previewWorldPosition = BreakWalltilemap.CellToWorld(CurrentMouseGridPosition);
        previewWorldPosition += BreakWalltilemap.cellSize * 0.5f;
        PreviewObject.transform.position = previewWorldPosition;

        // 프리뷰 색상 변경
        SpriteRenderer spriteRenderer = PreviewObject.GetComponent<SpriteRenderer>();

        // 이미 캐릭터가 있는 경우
        if (SpawnedCharacters.ContainsKey(CurrentMouseGridPosition))
        {
            spriteRenderer.color = new Color(1f, 0f, 0f, 0.5f);  // 빨간색 반투명
        }
        // 벽이 있는 경우 (새로 추가)
        else if (BreakWalltilemap.GetTile(CurrentMouseGridPosition) != null)
        {
            spriteRenderer.color = new Color(0.5f, 0f, 0.5f, 0.5f);  // 보라색 반투명 (벽이 있음)
        }
        // Ground 타일이 있고 배치 가능한 경우
        else if (IsGroundLayer())
        {
            spriteRenderer.color = new Color(0f, 1f, 0f, 0.5f);  // 초록색 반투명
        }
        // 배치 불가능한 경우 (Ground 타일이 없음)
        else
        {
            spriteRenderer.color = new Color(1f, 1f, 0f, 0.5f);  // 노란색 반투명
        }

        // 프리뷰 활성화
        PreviewObject.SetActive(true);
    }

    // 외부에서 호출할 수 있는 유용한 함수들

    /// <summary>
    /// 특정 위치의 캐릭터 가져오기
    /// </summary>
    public GameObject GetCharacterAt(Vector3Int gridPosition)
    {
        return SpawnedCharacters.ContainsKey(gridPosition) ? SpawnedCharacters[gridPosition] : null;
    }

    /// <summary>
    /// 모든 캐릭터 위치 가져오기
    /// </summary>
    public Vector3Int[] GetAllCharacterPositions()
    {
        Vector3Int[] positions = new Vector3Int[SpawnedCharacters.Count];
        SpawnedCharacters.Keys.CopyTo(positions, 0);
        return positions;
    }

    /// <summary>
    /// 생성된 캐릭터 수 반환
    /// </summary>
    public int GetCharacterCount()
    {
        return SpawnedCharacters.Count;
    }
}