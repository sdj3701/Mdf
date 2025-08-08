using UnityEngine;
using UnityEngine.Tilemaps;
using System.Collections.Generic;

[System.Serializable]
public class CharacterData
{
    [Header("캐릭터 기본 정보")]
    public string characterName;        // 캐릭터 이름
    public GameObject characterPrefab;  // 생성할 캐릭터 프리팹
    public KeyCode hotkey;             // 단축키

    [Header("생성 조건")]
    public SpawnCondition spawnCondition;  // 생성 조건
    public List<TileBase> allowedTiles;    // 생성 가능한 타일들
    public bool canSpawnOnEmpty = false;   // 빈 공간에도 생성 가능한지

    [Header("시각적 설정")]
    public Color previewColor = Color.blue; // 프리뷰 색상
}

public enum SpawnCondition
{
    OnGroundOnly,      // Ground 타일에서만
    OnBreakWallOnly,   // BreakWall 타일에서만
    OnAnyTile,         // 모든 타일에서
    OnSpecificTiles    // 지정된 타일에서만
}

public class TileAndCharacterGenerator : MonoBehaviour
{
    [Header("타일맵 설정")]
    public Tilemap groundTilemap;      // 바닥 타일맵
    public Tilemap wallTilemap;        // 벽 타일맵 (breakWall용)
    public Camera playerCamera;        // 메인 카메라

    [Header("타일 설정")]
    public TileBase groundTile;        // 바닥 타일
    public TileBase breakWallTile;     // 부술 수 있는 벽 타일
    public TileBase normalWallTile;    // 일반 벽 타일

    [Header("캐릭터 설정")]
    public List<CharacterData> availableCharacters; // 생성 가능한 캐릭터들

    [Header("모드 설정")]
    public bool isTileMode = true;     // true: 타일 모드, false: 캐릭터 모드
    public bool showPreview = true;    // 프리뷰 표시 여부

    // 현재 선택된 것들
    private int currentTileIndex = 0;      // 현재 타일 인덱스 (0: ground, 1: breakWall, 2: normalWall)
    private int currentCharacterIndex = 0; // 현재 캐릭터 인덱스

    // 프리뷰 및 위치
    private GameObject previewObject;
    private Vector3Int currentMouseGridPosition;

    // 생성된 캐릭터 추적
    private Dictionary<Vector3Int, GameObject> spawnedCharacters = new Dictionary<Vector3Int, GameObject>();

    void Start()
    {
        InitializeComponents();
        CreatePreviewObject();
    }

    void InitializeComponents()
    {
        // 컴포넌트 자동 할당
        if (playerCamera == null)
            playerCamera = Camera.main;

        if (groundTilemap == null)
            groundTilemap = GameObject.Find("Ground")?.GetComponent<Tilemap>();

        if (wallTilemap == null)
            wallTilemap = GameObject.Find("Wall")?.GetComponent<Tilemap>();
    }

    void Update()
    {
        HandleInput();
        UpdateMousePosition();
        UpdatePreview();
    }

    void HandleInput()
    {
        // 모드 전환 (T: 타일 모드, C: 캐릭터 모드)
        if (Input.GetKeyDown(KeyCode.T))
        {
            isTileMode = true;
            Debug.Log("타일 배치 모드");
        }

        if (Input.GetKeyDown(KeyCode.C))
        {
            isTileMode = false;
            Debug.Log("캐릭터 생성 모드");
        }

        // 타일/캐릭터 타입 변경 (마우스 휠)
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (scroll > 0f)
            ChangeType(1);  // 다음 타입
        else if (scroll < 0f)
            ChangeType(-1); // 이전 타입

        // 캐릭터 단축키 처리
        if (!isTileMode)
        {
            for (int i = 0; i < availableCharacters.Count; i++)
            {
                if (Input.GetKeyDown(availableCharacters[i].hotkey))
                {
                    currentCharacterIndex = i;
                    Debug.Log($"캐릭터 선택: {availableCharacters[i].characterName}");
                }
            }
        }

        // 마우스 클릭 처리
        if (Input.GetMouseButtonDown(0))
        {
            if (isTileMode)
                PlaceTile();
            else
                SpawnCharacter();
        }

        // 우클릭으로 제거
        if (Input.GetMouseButtonDown(1))
        {
            if (isTileMode)
                RemoveTile();
            else
                RemoveCharacter();
        }
    }

    void UpdateMousePosition()
    {
        Vector3 mouseWorldPosition = GetMouseWorldPosition();
        currentMouseGridPosition = groundTilemap.WorldToCell(mouseWorldPosition);
    }

    Vector3 GetMouseWorldPosition()
    {
        Vector3 mouseScreenPosition = Input.mousePosition;

        if (playerCamera.orthographic)
        {
            mouseScreenPosition.z = playerCamera.nearClipPlane;
            return playerCamera.ScreenToWorldPoint(mouseScreenPosition);
        }
        else
        {
            Ray ray = playerCamera.ScreenPointToRay(mouseScreenPosition);
            float distance = -playerCamera.transform.position.z / ray.direction.z;
            return ray.origin + ray.direction * distance;
        }
    }

    void ChangeType(int direction)
    {
        if (isTileMode)
        {
            // 타일 타입 변경 (0: ground, 1: breakWall, 2: normalWall)
            currentTileIndex = (currentTileIndex + direction + 3) % 3;
            string[] tileNames = { "바닥", "부술 수 있는 벽", "일반 벽" };
            Debug.Log($"선택된 타일: {tileNames[currentTileIndex]}");
        }
        else
        {
            // 캐릭터 타입 변경
            if (availableCharacters.Count > 0)
            {
                currentCharacterIndex = (currentCharacterIndex + direction + availableCharacters.Count) % availableCharacters.Count;
                Debug.Log($"선택된 캐릭터: {availableCharacters[currentCharacterIndex].characterName}");
            }
        }
    }

    void PlaceTile()
    {
        TileBase tileToPlace = GetCurrentTile();
        Tilemap targetTilemap = GetTargetTilemap();

        if (tileToPlace == null || targetTilemap == null)
        {
            Debug.LogWarning("타일 또는 타일맵이 설정되지 않았습니다!");
            return;
        }

        // 해당 위치에 타일이 있는지 확인
        TileBase existingTile = targetTilemap.GetTile(currentMouseGridPosition);

        if (existingTile == null)
        {
            targetTilemap.SetTile(currentMouseGridPosition, tileToPlace);
            Debug.Log($"타일 배치: {tileToPlace.name} at {currentMouseGridPosition}");
        }
        else
        {
            Debug.Log("이미 타일이 존재합니다!");
        }
    }

    void RemoveTile()
    {
        // Ground 타일맵에서 먼저 확인
        TileBase groundTileAtPos = groundTilemap.GetTile(currentMouseGridPosition);
        if (groundTileAtPos != null)
        {
            groundTilemap.SetTile(currentMouseGridPosition, null);
            Debug.Log($"바닥 타일 제거: {currentMouseGridPosition}");
            return;
        }

        // Wall 타일맵에서 확인
        if (wallTilemap != null)
        {
            TileBase wallTileAtPos = wallTilemap.GetTile(currentMouseGridPosition);
            if (wallTileAtPos != null)
            {
                wallTilemap.SetTile(currentMouseGridPosition, null);
                Debug.Log($"벽 타일 제거: {currentMouseGridPosition}");
                return;
            }
        }

        Debug.Log("제거할 타일이 없습니다!");
    }

    void SpawnCharacter()
    {
        if (availableCharacters.Count == 0 || currentCharacterIndex >= availableCharacters.Count)
        {
            Debug.LogWarning("생성할 캐릭터가 없습니다!");
            return;
        }

        CharacterData characterData = availableCharacters[currentCharacterIndex];

        // 이미 캐릭터가 있는지 확인
        if (spawnedCharacters.ContainsKey(currentMouseGridPosition))
        {
            Debug.Log("이미 캐릭터가 존재합니다!");
            return;
        }

        // 생성 조건 확인
        if (!CanSpawnCharacterAt(currentMouseGridPosition, characterData))
        {
            Debug.Log($"{characterData.characterName}을(를) 이 위치에 생성할 수 없습니다!");
            return;
        }

        // 캐릭터 생성
        Vector3 worldPosition = groundTilemap.CellToWorld(currentMouseGridPosition);
        worldPosition += new Vector3(groundTilemap.cellSize.x * 0.5f, groundTilemap.cellSize.y * 0.5f, 0); // 중앙 배치

        GameObject newCharacter = Instantiate(characterData.characterPrefab, worldPosition, Quaternion.identity);
        newCharacter.name = $"{characterData.characterName}_{currentMouseGridPosition}";

        // 캐릭터 추적에 추가
        spawnedCharacters[currentMouseGridPosition] = newCharacter;

        Debug.Log($"{characterData.characterName} 생성: {currentMouseGridPosition}");
    }

    void RemoveCharacter()
    {
        if (spawnedCharacters.ContainsKey(currentMouseGridPosition))
        {
            GameObject characterToRemove = spawnedCharacters[currentMouseGridPosition];
            spawnedCharacters.Remove(currentMouseGridPosition);
            Destroy(characterToRemove);
            Debug.Log($"캐릭터 제거: {currentMouseGridPosition}");
        }
        else
        {
            Debug.Log("제거할 캐릭터가 없습니다!");
        }
    }

    bool CanSpawnCharacterAt(Vector3Int position, CharacterData characterData)
    {
        TileBase groundTileAtPos = groundTilemap.GetTile(position);
        TileBase wallTileAtPos = wallTilemap?.GetTile(position);

        switch (characterData.spawnCondition)
        {
            case SpawnCondition.OnGroundOnly:
                // 바닥 타일(Ground)에서만 생성 가능
                return groundTileAtPos == groundTile;

            case SpawnCondition.OnBreakWallOnly:
                // 부술 수 있는 벽에서만 생성 가능
                return wallTileAtPos == breakWallTile;

            case SpawnCondition.OnAnyTile:
                // 어떤 타일이든 있으면 생성 가능
                return groundTileAtPos != null || wallTileAtPos != null;

            case SpawnCondition.OnSpecificTiles:
                // 지정된 타일에서만 생성 가능
                foreach (TileBase allowedTile in characterData.allowedTiles)
                {
                    if (groundTileAtPos == allowedTile || wallTileAtPos == allowedTile)
                        return true;
                }
                return false;

            default:
                return characterData.canSpawnOnEmpty || groundTileAtPos != null || wallTileAtPos != null;
        }
    }

    TileBase GetCurrentTile()
    {
        switch (currentTileIndex)
        {
            case 0: return groundTile;
            case 1: return breakWallTile;
            case 2: return normalWallTile;
            default: return groundTile;
        }
    }

    Tilemap GetTargetTilemap()
    {
        switch (currentTileIndex)
        {
            case 0: return groundTilemap;      // 바닥은 Ground 타일맵에
            case 1: return wallTilemap;        // 부술 수 있는 벽은 Wall 타일맵에
            case 2: return wallTilemap;        // 일반 벽도 Wall 타일맵에
            default: return groundTilemap;
        }
    }

    void CreatePreviewObject()
    {
        if (!showPreview) return;

        previewObject = new GameObject("Preview");
        SpriteRenderer spriteRenderer = previewObject.AddComponent<SpriteRenderer>();

        // 간단한 프리뷰 스프라이트 생성
        Texture2D texture = new Texture2D(1, 1);
        texture.SetPixel(0, 0, Color.white);
        texture.Apply();

        Sprite previewSprite = Sprite.Create(texture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1);
        spriteRenderer.sprite = previewSprite;
        spriteRenderer.sortingOrder = 100;

        previewObject.SetActive(false);
    }

    void UpdatePreview()
    {
        if (!showPreview || previewObject == null) return;

        // 프리뷰 위치 업데이트
        Vector3 previewWorldPosition = groundTilemap.CellToWorld(currentMouseGridPosition);
        previewWorldPosition += groundTilemap.cellSize * 0.5f;
        previewObject.transform.position = previewWorldPosition;

        SpriteRenderer spriteRenderer = previewObject.GetComponent<SpriteRenderer>();

        if (isTileMode)
        {
            // 타일 모드 프리뷰
            bool canPlace = GetTargetTilemap().GetTile(currentMouseGridPosition) == null;
            spriteRenderer.color = canPlace ? Color.green : Color.red;
        }
        else
        {
            // 캐릭터 모드 프리뷰
            if (availableCharacters.Count > 0 && currentCharacterIndex < availableCharacters.Count)
            {
                CharacterData currentCharacter = availableCharacters[currentCharacterIndex];
                bool canSpawn = !spawnedCharacters.ContainsKey(currentMouseGridPosition) &&
                               CanSpawnCharacterAt(currentMouseGridPosition, currentCharacter);

                spriteRenderer.color = canSpawn ? currentCharacter.previewColor : Color.red;
            }
        }

        previewObject.SetActive(true);
    }

    // UI에 표시할 정보 가져오기
    public string GetCurrentModeInfo()
    {
        if (isTileMode)
        {
            string[] tileNames = { "바닥", "부술 수 있는 벽", "일반 벽" };
            return $"타일 모드 - {tileNames[currentTileIndex]}";
        }
        else
        {
            if (availableCharacters.Count > 0 && currentCharacterIndex < availableCharacters.Count)
            {
                return $"캐릭터 모드 - {availableCharacters[currentCharacterIndex].characterName}";
            }
            return "캐릭터 모드 - 캐릭터 없음";
        }
    }

    // 디버그용 기즈모
    void OnDrawGizmos()
    {
        if (groundTilemap == null) return;

        Vector3 worldPosition = groundTilemap.CellToWorld(currentMouseGridPosition);
        Vector3 cellSize = groundTilemap.cellSize;

        Gizmos.color = isTileMode ? Color.yellow : Color.cyan;
        Gizmos.DrawWireCube(worldPosition + cellSize * 0.5f, cellSize);
    }

    // 전체 정리
    public void ClearAll()
    {
        // 모든 캐릭터 제거
        foreach (var character in spawnedCharacters.Values)
        {
            if (character != null)
                Destroy(character);
        }
        spawnedCharacters.Clear();

        // 모든 타일 제거
        if (groundTilemap != null)
        {
            BoundsInt bounds = groundTilemap.cellBounds;
            TileBase[] emptyTiles = new TileBase[bounds.size.x * bounds.size.y * bounds.size.z];
            groundTilemap.SetTilesBlock(bounds, emptyTiles);
        }

        if (wallTilemap != null)
        {
            BoundsInt bounds = wallTilemap.cellBounds;
            TileBase[] emptyTiles = new TileBase[bounds.size.x * bounds.size.y * bounds.size.z];
            wallTilemap.SetTilesBlock(bounds, emptyTiles);
        }

        Debug.Log("모든 타일과 캐릭터가 제거되었습니다!");
    }
}