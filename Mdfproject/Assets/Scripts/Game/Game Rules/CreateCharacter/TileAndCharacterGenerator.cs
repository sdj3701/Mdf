using UnityEngine;
using UnityEngine.Tilemaps;
using System.Collections.Generic;

[System.Serializable]
public class CharacterData
{
    [Header("ĳ���� �⺻ ����")]
    public string characterName;        // ĳ���� �̸�
    public GameObject characterPrefab;  // ������ ĳ���� ������
    public KeyCode hotkey;             // ����Ű

    [Header("���� ����")]
    public SpawnCondition spawnCondition;  // ���� ����
    public List<TileBase> allowedTiles;    // ���� ������ Ÿ�ϵ�
    public bool canSpawnOnEmpty = false;   // �� �������� ���� ��������

    [Header("�ð��� ����")]
    public Color previewColor = Color.blue; // ������ ����
}

public enum SpawnCondition
{
    OnGroundOnly,      // Ground Ÿ�Ͽ�����
    OnBreakWallOnly,   // BreakWall Ÿ�Ͽ�����
    OnAnyTile,         // ��� Ÿ�Ͽ���
    OnSpecificTiles    // ������ Ÿ�Ͽ�����
}

public class TileAndCharacterGenerator : MonoBehaviour
{
    [Header("Ÿ�ϸ� ����")]
    public Tilemap groundTilemap;      // �ٴ� Ÿ�ϸ�
    public Tilemap wallTilemap;        // �� Ÿ�ϸ� (breakWall��)
    public Camera playerCamera;        // ���� ī�޶�

    [Header("Ÿ�� ����")]
    public TileBase groundTile;        // �ٴ� Ÿ��
    public TileBase breakWallTile;     // �μ� �� �ִ� �� Ÿ��
    public TileBase normalWallTile;    // �Ϲ� �� Ÿ��

    [Header("ĳ���� ����")]
    public List<CharacterData> availableCharacters; // ���� ������ ĳ���͵�

    [Header("��� ����")]
    public bool isTileMode = true;     // true: Ÿ�� ���, false: ĳ���� ���
    public bool showPreview = true;    // ������ ǥ�� ����

    // ���� ���õ� �͵�
    private int currentTileIndex = 0;      // ���� Ÿ�� �ε��� (0: ground, 1: breakWall, 2: normalWall)
    private int currentCharacterIndex = 0; // ���� ĳ���� �ε���

    // ������ �� ��ġ
    private GameObject previewObject;
    private Vector3Int currentMouseGridPosition;

    // ������ ĳ���� ����
    private Dictionary<Vector3Int, GameObject> spawnedCharacters = new Dictionary<Vector3Int, GameObject>();

    void Start()
    {
        InitializeComponents();
        CreatePreviewObject();
    }

    void InitializeComponents()
    {
        // ������Ʈ �ڵ� �Ҵ�
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
        // ��� ��ȯ (T: Ÿ�� ���, C: ĳ���� ���)
        if (Input.GetKeyDown(KeyCode.T))
        {
            isTileMode = true;
            Debug.Log("Ÿ�� ��ġ ���");
        }

        if (Input.GetKeyDown(KeyCode.C))
        {
            isTileMode = false;
            Debug.Log("ĳ���� ���� ���");
        }

        // Ÿ��/ĳ���� Ÿ�� ���� (���콺 ��)
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (scroll > 0f)
            ChangeType(1);  // ���� Ÿ��
        else if (scroll < 0f)
            ChangeType(-1); // ���� Ÿ��

        // ĳ���� ����Ű ó��
        if (!isTileMode)
        {
            for (int i = 0; i < availableCharacters.Count; i++)
            {
                if (Input.GetKeyDown(availableCharacters[i].hotkey))
                {
                    currentCharacterIndex = i;
                    Debug.Log($"ĳ���� ����: {availableCharacters[i].characterName}");
                }
            }
        }

        // ���콺 Ŭ�� ó��
        if (Input.GetMouseButtonDown(0))
        {
            if (isTileMode)
                PlaceTile();
            else
                SpawnCharacter();
        }

        // ��Ŭ������ ����
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
            // Ÿ�� Ÿ�� ���� (0: ground, 1: breakWall, 2: normalWall)
            currentTileIndex = (currentTileIndex + direction + 3) % 3;
            string[] tileNames = { "�ٴ�", "�μ� �� �ִ� ��", "�Ϲ� ��" };
            Debug.Log($"���õ� Ÿ��: {tileNames[currentTileIndex]}");
        }
        else
        {
            // ĳ���� Ÿ�� ����
            if (availableCharacters.Count > 0)
            {
                currentCharacterIndex = (currentCharacterIndex + direction + availableCharacters.Count) % availableCharacters.Count;
                Debug.Log($"���õ� ĳ����: {availableCharacters[currentCharacterIndex].characterName}");
            }
        }
    }

    void PlaceTile()
    {
        TileBase tileToPlace = GetCurrentTile();
        Tilemap targetTilemap = GetTargetTilemap();

        if (tileToPlace == null || targetTilemap == null)
        {
            Debug.LogWarning("Ÿ�� �Ǵ� Ÿ�ϸ��� �������� �ʾҽ��ϴ�!");
            return;
        }

        // �ش� ��ġ�� Ÿ���� �ִ��� Ȯ��
        TileBase existingTile = targetTilemap.GetTile(currentMouseGridPosition);

        if (existingTile == null)
        {
            targetTilemap.SetTile(currentMouseGridPosition, tileToPlace);
            Debug.Log($"Ÿ�� ��ġ: {tileToPlace.name} at {currentMouseGridPosition}");
        }
        else
        {
            Debug.Log("�̹� Ÿ���� �����մϴ�!");
        }
    }

    void RemoveTile()
    {
        // Ground Ÿ�ϸʿ��� ���� Ȯ��
        TileBase groundTileAtPos = groundTilemap.GetTile(currentMouseGridPosition);
        if (groundTileAtPos != null)
        {
            groundTilemap.SetTile(currentMouseGridPosition, null);
            Debug.Log($"�ٴ� Ÿ�� ����: {currentMouseGridPosition}");
            return;
        }

        // Wall Ÿ�ϸʿ��� Ȯ��
        if (wallTilemap != null)
        {
            TileBase wallTileAtPos = wallTilemap.GetTile(currentMouseGridPosition);
            if (wallTileAtPos != null)
            {
                wallTilemap.SetTile(currentMouseGridPosition, null);
                Debug.Log($"�� Ÿ�� ����: {currentMouseGridPosition}");
                return;
            }
        }

        Debug.Log("������ Ÿ���� �����ϴ�!");
    }

    void SpawnCharacter()
    {
        if (availableCharacters.Count == 0 || currentCharacterIndex >= availableCharacters.Count)
        {
            Debug.LogWarning("������ ĳ���Ͱ� �����ϴ�!");
            return;
        }

        CharacterData characterData = availableCharacters[currentCharacterIndex];

        // �̹� ĳ���Ͱ� �ִ��� Ȯ��
        if (spawnedCharacters.ContainsKey(currentMouseGridPosition))
        {
            Debug.Log("�̹� ĳ���Ͱ� �����մϴ�!");
            return;
        }

        // ���� ���� Ȯ��
        if (!CanSpawnCharacterAt(currentMouseGridPosition, characterData))
        {
            Debug.Log($"{characterData.characterName}��(��) �� ��ġ�� ������ �� �����ϴ�!");
            return;
        }

        // ĳ���� ����
        Vector3 worldPosition = groundTilemap.CellToWorld(currentMouseGridPosition);
        worldPosition += new Vector3(groundTilemap.cellSize.x * 0.5f, groundTilemap.cellSize.y * 0.5f, 0); // �߾� ��ġ

        GameObject newCharacter = Instantiate(characterData.characterPrefab, worldPosition, Quaternion.identity);
        newCharacter.name = $"{characterData.characterName}_{currentMouseGridPosition}";

        // ĳ���� ������ �߰�
        spawnedCharacters[currentMouseGridPosition] = newCharacter;

        Debug.Log($"{characterData.characterName} ����: {currentMouseGridPosition}");
    }

    void RemoveCharacter()
    {
        if (spawnedCharacters.ContainsKey(currentMouseGridPosition))
        {
            GameObject characterToRemove = spawnedCharacters[currentMouseGridPosition];
            spawnedCharacters.Remove(currentMouseGridPosition);
            Destroy(characterToRemove);
            Debug.Log($"ĳ���� ����: {currentMouseGridPosition}");
        }
        else
        {
            Debug.Log("������ ĳ���Ͱ� �����ϴ�!");
        }
    }

    bool CanSpawnCharacterAt(Vector3Int position, CharacterData characterData)
    {
        TileBase groundTileAtPos = groundTilemap.GetTile(position);
        TileBase wallTileAtPos = wallTilemap?.GetTile(position);

        switch (characterData.spawnCondition)
        {
            case SpawnCondition.OnGroundOnly:
                // �ٴ� Ÿ��(Ground)������ ���� ����
                return groundTileAtPos == groundTile;

            case SpawnCondition.OnBreakWallOnly:
                // �μ� �� �ִ� �������� ���� ����
                return wallTileAtPos == breakWallTile;

            case SpawnCondition.OnAnyTile:
                // � Ÿ���̵� ������ ���� ����
                return groundTileAtPos != null || wallTileAtPos != null;

            case SpawnCondition.OnSpecificTiles:
                // ������ Ÿ�Ͽ����� ���� ����
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
            case 0: return groundTilemap;      // �ٴ��� Ground Ÿ�ϸʿ�
            case 1: return wallTilemap;        // �μ� �� �ִ� ���� Wall Ÿ�ϸʿ�
            case 2: return wallTilemap;        // �Ϲ� ���� Wall Ÿ�ϸʿ�
            default: return groundTilemap;
        }
    }

    void CreatePreviewObject()
    {
        if (!showPreview) return;

        previewObject = new GameObject("Preview");
        SpriteRenderer spriteRenderer = previewObject.AddComponent<SpriteRenderer>();

        // ������ ������ ��������Ʈ ����
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

        // ������ ��ġ ������Ʈ
        Vector3 previewWorldPosition = groundTilemap.CellToWorld(currentMouseGridPosition);
        previewWorldPosition += groundTilemap.cellSize * 0.5f;
        previewObject.transform.position = previewWorldPosition;

        SpriteRenderer spriteRenderer = previewObject.GetComponent<SpriteRenderer>();

        if (isTileMode)
        {
            // Ÿ�� ��� ������
            bool canPlace = GetTargetTilemap().GetTile(currentMouseGridPosition) == null;
            spriteRenderer.color = canPlace ? Color.green : Color.red;
        }
        else
        {
            // ĳ���� ��� ������
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

    // UI�� ǥ���� ���� ��������
    public string GetCurrentModeInfo()
    {
        if (isTileMode)
        {
            string[] tileNames = { "�ٴ�", "�μ� �� �ִ� ��", "�Ϲ� ��" };
            return $"Ÿ�� ��� - {tileNames[currentTileIndex]}";
        }
        else
        {
            if (availableCharacters.Count > 0 && currentCharacterIndex < availableCharacters.Count)
            {
                return $"ĳ���� ��� - {availableCharacters[currentCharacterIndex].characterName}";
            }
            return "ĳ���� ��� - ĳ���� ����";
        }
    }

    // ����׿� �����
    void OnDrawGizmos()
    {
        if (groundTilemap == null) return;

        Vector3 worldPosition = groundTilemap.CellToWorld(currentMouseGridPosition);
        Vector3 cellSize = groundTilemap.cellSize;

        Gizmos.color = isTileMode ? Color.yellow : Color.cyan;
        Gizmos.DrawWireCube(worldPosition + cellSize * 0.5f, cellSize);
    }

    // ��ü ����
    public void ClearAll()
    {
        // ��� ĳ���� ����
        foreach (var character in spawnedCharacters.Values)
        {
            if (character != null)
                Destroy(character);
        }
        spawnedCharacters.Clear();

        // ��� Ÿ�� ����
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

        Debug.Log("��� Ÿ�ϰ� ĳ���Ͱ� ���ŵǾ����ϴ�!");
    }
}