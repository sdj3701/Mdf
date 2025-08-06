using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

public class CreateWall : MonoBehaviour
{
    [Header("Ÿ�ϸ� ����")]
    public Tilemap tilemap;           // Ÿ���� ��ġ�� Ÿ�ϸ�
    public TileBase tileToPlace;      // ��ġ�� Ÿ��
    public Camera playerCamera;       // ���� ī�޶�

    [Header("������ ����")]
    public bool showPreview = true;   // ���콺 ��ġ ������ ǥ�� ����
    public Color previewColor = Color.green;  // ������ ����

    private GameObject previewObject; // ������� ������Ʈ
    private Vector3Int currentMouseGridPosition; // ���� ���콺�� �׸��� ��ǥ

    void Start()
    {
        // ������Ʈ �ڵ� �Ҵ�
        if (tilemap == null)
            tilemap = FindObjectOfType<Tilemap>();

        if (playerCamera == null)
            playerCamera = Camera.main;

        // ������ ������Ʈ ����
        if (showPreview)
            CreatePreviewObject();
    }

    void Update()
    {
        // ���콺 �Է� ó��
        HandleMouseInput();

        // ������ ������Ʈ
        if (showPreview)
            UpdatePreview();
    }

    void HandleMouseInput()
    {
        // ���콺 ��ġ�� ���� ��ǥ�� ��ȯ
        Vector3 mouseWorldPosition = GetMouseWorldPosition();

        // ���� ��ǥ�� �׸��� ��ǥ�� ��ȯ
        currentMouseGridPosition = tilemap.WorldToCell(mouseWorldPosition);

        // ��Ŭ��: Ÿ�� ��ġ
        if (Input.GetMouseButtonDown(0))
        {
            PlaceTile();
        }

        // ��Ŭ��: Ÿ�� ����
        if (Input.GetMouseButtonDown(1))
        {
            RemoveTile();
        }

        // ��� Ŭ��: Ÿ�� ���� Ȯ��
        if (Input.GetMouseButtonDown(2))
        {
            CheckTileInfo();
        }
    }

    Vector3 GetMouseWorldPosition()
    {
        // ���콺 ��ũ�� ��ǥ ��������
        Vector3 mouseScreenPosition = Input.mousePosition;

        // 2D ���ӿ� (���� ī�޶�)
        if (playerCamera.orthographic)
        {
            mouseScreenPosition.z = playerCamera.nearClipPlane;
            return playerCamera.ScreenToWorldPoint(mouseScreenPosition);
        }
        // 3D ���ӿ� (���� ī�޶�) - ����ĳ��Ʈ ���
        else
        {
            Ray ray = playerCamera.ScreenPointToRay(mouseScreenPosition);

            // Z=0 ��鿡 ���� (2D Ÿ�ϸʿ�)
            float distance = -playerCamera.transform.position.z / ray.direction.z;
            return ray.origin + ray.direction * distance;
        }
    }

    void PlaceTile()
    {
        // ��ġ�� Ÿ���� �����Ǿ� �ִ��� Ȯ��
        if (tileToPlace == null)
        {
            Debug.LogWarning("��ġ�� Ÿ���� �������� �ʾҽ��ϴ�!");
            return;
        }

        // �ش� ��ġ�� �̹� Ÿ���� �ִ��� Ȯ��
        TileBase existingTile = tilemap.GetTile(currentMouseGridPosition);

        if (existingTile == null)
        {
            // Ÿ�� ��ġ
            tilemap.SetTile(currentMouseGridPosition, tileToPlace);
            Debug.Log($"Ÿ�� ��ġ: {currentMouseGridPosition}");
        }
        else
        {
            Debug.Log($"�̹� Ÿ���� �����մϴ�: {currentMouseGridPosition}");
        }
    }

    void RemoveTile()
    {
        // �ش� ��ġ�� Ÿ�� Ȯ��
        TileBase existingTile = tilemap.GetTile(currentMouseGridPosition);

        if (existingTile != null)
        {
            // Ÿ�� ���� (null�� ����)
            tilemap.SetTile(currentMouseGridPosition, null);
            Debug.Log($"Ÿ�� ����: {currentMouseGridPosition}");
        }
        else
        {
            Debug.Log($"������ Ÿ���� �����ϴ�: {currentMouseGridPosition}");
        }
    }

    void CheckTileInfo()
    {
        // ���� ��ġ�� Ÿ�� ���� ���
        TileBase currentTile = tilemap.GetTile(currentMouseGridPosition);
        Vector3 worldPosition = tilemap.CellToWorld(currentMouseGridPosition);

        if (currentTile != null)
        {
            Debug.Log($"Ÿ�� ���� - �׸���: {currentMouseGridPosition}, ����: {worldPosition}, Ÿ�ϸ�: {currentTile.name}");
        }
        else
        {
            Debug.Log($"�� ���� - �׸���: {currentMouseGridPosition}, ����: {worldPosition}");
        }
    }

    void CreatePreviewObject()
    {
        // ������� ������Ʈ ����
        previewObject = new GameObject("TilePreview");

        // ��������Ʈ ������ �߰�
        SpriteRenderer spriteRenderer = previewObject.AddComponent<SpriteRenderer>();

        // ������ �簢�� ��������Ʈ ����
        Texture2D texture = new Texture2D(1, 1);
        texture.SetPixel(0, 0, Color.white);
        texture.Apply();

        // ��������Ʈ ���� �� ����
        Sprite previewSprite = Sprite.Create(texture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1);
        spriteRenderer.sprite = previewSprite;
        spriteRenderer.color = previewColor;

        // Ÿ�ϸʺ��� ���� ǥ�õǵ��� ����
        spriteRenderer.sortingOrder = 10;

        // ó������ ��Ȱ��ȭ
        previewObject.SetActive(false);
    }

    void UpdatePreview()
    {
        if (previewObject == null) return;

        // ������ ������Ʈ ��ġ ������Ʈ
        Vector3 previewWorldPosition = tilemap.CellToWorld(currentMouseGridPosition);

        // Ÿ�� �߾ӿ� ǥ�õǵ��� ������ �߰�
        previewWorldPosition += tilemap.cellSize * 0.5f;
        previewObject.transform.position = previewWorldPosition;

        // ������ ���� ���� (Ÿ���� ������ ������, ������ �ʷϻ�)
        SpriteRenderer spriteRenderer = previewObject.GetComponent<SpriteRenderer>();
        TileBase existingTile = tilemap.GetTile(currentMouseGridPosition);

        if (existingTile != null)
        {
            spriteRenderer.color = Color.red;  // �̹� Ÿ���� ������ ������
        }
        else
        {
            spriteRenderer.color = previewColor;  // �� �����̸� �ʷϻ�
        }

        // ������ Ȱ��ȭ
        previewObject.SetActive(true);
    }

    // �����Ϳ��� ����׿� ����� �׸���
    void OnDrawGizmos()
    {
        if (tilemap == null) return;

        // ���� ���콺 ��ġ�� �׸��� ���� ����� ���̾����������� ǥ��
        Vector3 worldPosition = tilemap.CellToWorld(currentMouseGridPosition);
        Vector3 cellSize = tilemap.cellSize;

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireCube(worldPosition + cellSize * 0.5f, cellSize);
    }

    // �ܺο��� ȣ���� �� �ִ� ������ �Լ���

    /// <summary>
    /// ����� Ÿ�� ����
    /// </summary>
    public void SetTileType(TileBase newTile)
    {
        tileToPlace = newTile;
        Debug.Log($"Ÿ�� Ÿ�� ����: {newTile?.name}");
    }

    /// <summary>
    /// ��ü Ÿ�ϸ� �����
    /// </summary>
    public void ClearAllTiles()
    {
        // Ÿ�ϸ��� ��� ��������
        BoundsInt bounds = tilemap.cellBounds;

        // ��� Ÿ���� null�� �����Ͽ� ����
        TileBase[] emptyTiles = new TileBase[bounds.size.x * bounds.size.y * bounds.size.z];
        tilemap.SetTilesBlock(bounds, emptyTiles);

        Debug.Log("��� Ÿ���� ���ŵǾ����ϴ�!");
    }

    /// <summary>
    /// ���� ���콺 ��ġ�� �׸��� ��ǥ ��ȯ
    /// </summary>
    public Vector3Int GetCurrentMouseGridPosition()
    {
        return currentMouseGridPosition;
    }
}
