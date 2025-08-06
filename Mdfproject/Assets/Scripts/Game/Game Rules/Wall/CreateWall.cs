using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

public class CreateWall : MonoBehaviour
{
    [Header("타일맵 설정")]
    public Tilemap tilemap;           // 타일을 배치할 타일맵
    public TileBase tileToPlace;      // 배치할 타일
    public Camera playerCamera;       // 메인 카메라

    [Header("프리뷰 설정")]
    public bool showPreview = true;   // 마우스 위치 프리뷰 표시 여부
    public Color previewColor = Color.green;  // 프리뷰 색상

    private GameObject previewObject; // 프리뷰용 오브젝트
    private Vector3Int currentMouseGridPosition; // 현재 마우스의 그리드 좌표

    void Start()
    {
        // 컴포넌트 자동 할당
        if (tilemap == null)
            tilemap = FindObjectOfType<Tilemap>();

        if (playerCamera == null)
            playerCamera = Camera.main;

        // 프리뷰 오브젝트 생성
        if (showPreview)
            CreatePreviewObject();
    }

    void Update()
    {
        // 마우스 입력 처리
        HandleMouseInput();

        // 프리뷰 업데이트
        if (showPreview)
            UpdatePreview();
    }

    void HandleMouseInput()
    {
        // 마우스 위치를 월드 좌표로 변환
        Vector3 mouseWorldPosition = GetMouseWorldPosition();

        // 월드 좌표를 그리드 좌표로 변환
        currentMouseGridPosition = tilemap.WorldToCell(mouseWorldPosition);

        // 좌클릭: 타일 배치
        if (Input.GetMouseButtonDown(0))
        {
            PlaceTile();
        }

        // 우클릭: 타일 제거
        if (Input.GetMouseButtonDown(1))
        {
            RemoveTile();
        }

        // 가운데 클릭: 타일 정보 확인
        if (Input.GetMouseButtonDown(2))
        {
            CheckTileInfo();
        }
    }

    Vector3 GetMouseWorldPosition()
    {
        // 마우스 스크린 좌표 가져오기
        Vector3 mouseScreenPosition = Input.mousePosition;

        // 2D 게임용 (직교 카메라)
        if (playerCamera.orthographic)
        {
            mouseScreenPosition.z = playerCamera.nearClipPlane;
            return playerCamera.ScreenToWorldPoint(mouseScreenPosition);
        }
        // 3D 게임용 (원근 카메라) - 레이캐스트 사용
        else
        {
            Ray ray = playerCamera.ScreenPointToRay(mouseScreenPosition);

            // Z=0 평면에 투사 (2D 타일맵용)
            float distance = -playerCamera.transform.position.z / ray.direction.z;
            return ray.origin + ray.direction * distance;
        }
    }

    void PlaceTile()
    {
        // 배치할 타일이 설정되어 있는지 확인
        if (tileToPlace == null)
        {
            Debug.LogWarning("배치할 타일이 설정되지 않았습니다!");
            return;
        }

        // 해당 위치에 이미 타일이 있는지 확인
        TileBase existingTile = tilemap.GetTile(currentMouseGridPosition);

        if (existingTile == null)
        {
            // 타일 배치
            tilemap.SetTile(currentMouseGridPosition, tileToPlace);
            Debug.Log($"타일 배치: {currentMouseGridPosition}");
        }
        else
        {
            Debug.Log($"이미 타일이 존재합니다: {currentMouseGridPosition}");
        }
    }

    void RemoveTile()
    {
        // 해당 위치의 타일 확인
        TileBase existingTile = tilemap.GetTile(currentMouseGridPosition);

        if (existingTile != null)
        {
            // 타일 제거 (null로 설정)
            tilemap.SetTile(currentMouseGridPosition, null);
            Debug.Log($"타일 제거: {currentMouseGridPosition}");
        }
        else
        {
            Debug.Log($"제거할 타일이 없습니다: {currentMouseGridPosition}");
        }
    }

    void CheckTileInfo()
    {
        // 현재 위치의 타일 정보 출력
        TileBase currentTile = tilemap.GetTile(currentMouseGridPosition);
        Vector3 worldPosition = tilemap.CellToWorld(currentMouseGridPosition);

        if (currentTile != null)
        {
            Debug.Log($"타일 정보 - 그리드: {currentMouseGridPosition}, 월드: {worldPosition}, 타일명: {currentTile.name}");
        }
        else
        {
            Debug.Log($"빈 공간 - 그리드: {currentMouseGridPosition}, 월드: {worldPosition}");
        }
    }

    void CreatePreviewObject()
    {
        // 프리뷰용 오브젝트 생성
        previewObject = new GameObject("TilePreview");

        // 스프라이트 렌더러 추가
        SpriteRenderer spriteRenderer = previewObject.AddComponent<SpriteRenderer>();

        // 간단한 사각형 스프라이트 생성
        Texture2D texture = new Texture2D(1, 1);
        texture.SetPixel(0, 0, Color.white);
        texture.Apply();

        // 스프라이트 생성 및 설정
        Sprite previewSprite = Sprite.Create(texture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1);
        spriteRenderer.sprite = previewSprite;
        spriteRenderer.color = previewColor;

        // 타일맵보다 위에 표시되도록 설정
        spriteRenderer.sortingOrder = 10;

        // 처음에는 비활성화
        previewObject.SetActive(false);
    }

    void UpdatePreview()
    {
        if (previewObject == null) return;

        // 프리뷰 오브젝트 위치 업데이트
        Vector3 previewWorldPosition = tilemap.CellToWorld(currentMouseGridPosition);

        // 타일 중앙에 표시되도록 오프셋 추가
        previewWorldPosition += tilemap.cellSize * 0.5f;
        previewObject.transform.position = previewWorldPosition;

        // 프리뷰 색상 변경 (타일이 있으면 빨간색, 없으면 초록색)
        SpriteRenderer spriteRenderer = previewObject.GetComponent<SpriteRenderer>();
        TileBase existingTile = tilemap.GetTile(currentMouseGridPosition);

        if (existingTile != null)
        {
            spriteRenderer.color = Color.red;  // 이미 타일이 있으면 빨간색
        }
        else
        {
            spriteRenderer.color = previewColor;  // 빈 공간이면 초록색
        }

        // 프리뷰 활성화
        previewObject.SetActive(true);
    }

    // 에디터에서 디버그용 기즈모 그리기
    void OnDrawGizmos()
    {
        if (tilemap == null) return;

        // 현재 마우스 위치의 그리드 셀을 노란색 와이어프레임으로 표시
        Vector3 worldPosition = tilemap.CellToWorld(currentMouseGridPosition);
        Vector3 cellSize = tilemap.cellSize;

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireCube(worldPosition + cellSize * 0.5f, cellSize);
    }

    // 외부에서 호출할 수 있는 유용한 함수들

    /// <summary>
    /// 사용할 타일 변경
    /// </summary>
    public void SetTileType(TileBase newTile)
    {
        tileToPlace = newTile;
        Debug.Log($"타일 타입 변경: {newTile?.name}");
    }

    /// <summary>
    /// 전체 타일맵 지우기
    /// </summary>
    public void ClearAllTiles()
    {
        // 타일맵의 경계 가져오기
        BoundsInt bounds = tilemap.cellBounds;

        // 모든 타일을 null로 설정하여 제거
        TileBase[] emptyTiles = new TileBase[bounds.size.x * bounds.size.y * bounds.size.z];
        tilemap.SetTilesBlock(bounds, emptyTiles);

        Debug.Log("모든 타일이 제거되었습니다!");
    }

    /// <summary>
    /// 현재 마우스 위치의 그리드 좌표 반환
    /// </summary>
    public Vector3Int GetCurrentMouseGridPosition()
    {
        return currentMouseGridPosition;
    }
}
