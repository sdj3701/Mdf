using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Tilemaps;

public class CreateWall : GameDataCenter
{
    private bool CheckCreateWall = true;
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
        if (IsWallPlacement)
        {
            // 마우스 위치 업데이트
            UpdateMousePosition();

            // 마우스 입력 처리
            HandleMouseInput();

            // 프리뷰 업데이트 (벽 배치 모드일 때만)
            if (ShowPreview)
                UpdatePreview();
        }
        else
        {
            // 벽 배치 모드가 아닐 때는 프리뷰 비활성화
            if (ShowPreview && PreviewObject != null)
                PreviewObject.SetActive(false);
        }
    }

    // 마우스 위치 업데이트를 별도 함수로 분리
    void UpdateMousePosition()
    {
        // 마우스 위치를 월드 좌표로 변환
        Vector3 mouseWorldPosition = GetMouseWorldPosition();

        // 월드 좌표를 그리드 좌표로 변환
        CurrentMouseGridPosition = BreakWalltilemap.WorldToCell(mouseWorldPosition);
    }

    void HandleMouseInput()
    {
        // 좌클릭: 타일 배치
        if (Input.GetMouseButtonDown(0))
        {
            if (CreateWallCount <= 0)
            {
                Debug.LogWarning("더 이상 타일을 배치할 수 없습니다!");
                return;
            }
            PlaceTile();
            if(CheckCreateWall)
                CreateWallCount--; // 타일 배치 시 카운트 감소
        }

        // 우클릭: 타일 제거
        if (Input.GetMouseButtonDown(1))
        {
            if (CreateWallCount >= 5)
            {
                Debug.LogWarning("타일을 제거할 수 없습니다! 최대 타일 개수에 도달했습니다.");
                return;
            }
            RemoveTile();
            CreateWallCount++; // 타일 제거 시 카운트 증가
        }

        // 가운데 클릭: 타일 정보 확인
        if (Input.GetMouseButtonDown(2))
        {
            CheckTileInfo();
            CheckGroundTileInfo(); // 추가: Ground 타일 정보도 확인
        }
    }

    Vector3 GetMouseWorldPosition()
    {
        // 마우스 스크린 좌표 가져오기
        Vector3 mouseScreenPosition = Input.mousePosition;

        // 2D 게임용 (직교 카메라)
        if (PlayerCamera.orthographic)
        {
            mouseScreenPosition.z = PlayerCamera.nearClipPlane;
            return PlayerCamera.ScreenToWorldPoint(mouseScreenPosition);
        }
        // 3D 게임용 (원근 카메라) - 레이캐스트 사용
        else
        {
            Ray ray = PlayerCamera.ScreenPointToRay(mouseScreenPosition);

            // Z=0 평면에 투사 (2D 타일맵용)
            float distance = -PlayerCamera.transform.position.z / ray.direction.z;
            return ray.origin + ray.direction * distance;
        }
    }

    void PlaceTile()
    {
        // 배치할 타일이 설정되어 있는지 확인
        if (BreakWalltileToPlace == null)
        {
            Debug.LogWarning("배치할 타일이 설정되지 않았습니다!");
            CheckCreateWall = false;
            return;
        }

        // 현재 마우스 위치 정보 출력
        Vector3 mouseWorldPos = GetMouseWorldPosition();
        // Ground 레이어 확인
        bool isGroundLayer = IsGroundLayer();

        if (!isGroundLayer)
        {
            Debug.LogError("❌ Ground가 아닌 곳에는 벽을 설치할 수 없습니다!");
            CheckCreateWall = false;
            return;
        }

        // 해당 위치에 이미 타일이 있는지 확인
        TileBase existingTile = BreakWalltilemap.GetTile(CurrentMouseGridPosition);

        if (existingTile == null)
        {
            // 타일 배치
            BreakWalltilemap.SetTile(CurrentMouseGridPosition, BreakWalltileToPlace);
            CheckCreateWall = true;
            Debug.Log($"✅ 타일 배치 성공: {CurrentMouseGridPosition}");
        }
        else
        {
            Debug.LogWarning($"⚠️ 이미 타일이 존재합니다: {CurrentMouseGridPosition}");
            CheckCreateWall = false;
        }
    }

    void RemoveTile()
    {
        // 해당 위치의 타일 확인
        TileBase existingTile = BreakWalltilemap.GetTile(CurrentMouseGridPosition);

        if (existingTile != null)
        {
            // 타일 제거 (null로 설정)
            BreakWalltilemap.SetTile(CurrentMouseGridPosition, null);
            Debug.Log($"타일 제거: {CurrentMouseGridPosition}");
        }
        else
        {
            Debug.Log($"제거할 타일이 없습니다: {CurrentMouseGridPosition}");
        }
    }

    // Ground 레이어 확인 함수 
    bool IsGroundLayer()
    {
        // Tilemap 자체가 Ground 레이어에 있는지 확인
        bool isGroundTilemap = Groundtilemap.gameObject.layer == LayerMask.NameToLayer("Ground");

        Vector3 mouseWorldPos = GetMouseWorldPosition();
        Vector3Int gridPosition = Groundtilemap.WorldToCell(mouseWorldPos);

        // 해당 위치에 타일이 있는지 확인
        TileBase tile = Groundtilemap.GetTile(gridPosition);
        bool hasTile = tile != null;

        // 둘 다 만족하면 Ground 타일
        return isGroundTilemap && hasTile;
    }

    void CheckTileInfo()
    {
        // 현재 위치의 타일 정보 출력
        TileBase currentTile = BreakWalltilemap.GetTile(CurrentMouseGridPosition);
        Vector3 worldPosition = BreakWalltilemap.CellToWorld(CurrentMouseGridPosition);

        if (currentTile != null)
        {
            Debug.Log($"🧱 벽 타일 정보 - 그리드: {CurrentMouseGridPosition}, 월드: {worldPosition}, 타일명: {currentTile.name}");
        }
        else
        {
            Debug.Log($"⬜ 빈 공간 (벽 없음) - 그리드: {CurrentMouseGridPosition}, 월드: {worldPosition}");
        }
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

    void CreatePreviewObject()
    {
        // 프리뷰용 오브젝트 생성
        PreviewObject = new GameObject("TilePreview");

        // 스프라이트 렌더러 추가
        SpriteRenderer spriteRenderer = PreviewObject.AddComponent<SpriteRenderer>();

        // 간단한 사각형 스프라이트 생성
        Texture2D texture = new Texture2D(1, 1);
        texture.SetPixel(0, 0, Color.white);
        texture.Apply();

        // 스프라이트 생성 및 설정
        Sprite previewSprite = Sprite.Create(texture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1);
        spriteRenderer.sprite = previewSprite;
        spriteRenderer.color = PreviewColor;

        // 타일맵보다 위에 표시되도록 설정
        spriteRenderer.sortingOrder = 10;

        // 처음에는 비활성화
        PreviewObject.SetActive(false);
    }

    void UpdatePreview()
    {
        if (PreviewObject == null) return;

        // 프리뷰 오브젝트 위치 업데이트
        Vector3 previewWorldPosition = BreakWalltilemap.CellToWorld(CurrentMouseGridPosition);

        // 타일 중앙에 표시되도록 오프셋 추가
        previewWorldPosition += BreakWalltilemap.cellSize * 0.5f;
        PreviewObject.transform.position = previewWorldPosition;

        // 프리뷰 색상 변경 (타일이 있으면 빨간색, 없으면 초록색)
        SpriteRenderer spriteRenderer = PreviewObject.GetComponent<SpriteRenderer>();
        TileBase existingTile = BreakWalltilemap.GetTile(CurrentMouseGridPosition);

        if (existingTile != null)
        {
            spriteRenderer.color = Color.red;  // 이미 타일이 있으면 빨간색
        }
        else
        {
            spriteRenderer.color = PreviewColor;  // 빈 공간이면 초록색
        }

        // 프리뷰 활성화
        PreviewObject.SetActive(true);
    }

    // 에디터에서 디버그용 기즈모 그리기
    void OnDrawGizmos()
    {
        if (BreakWalltilemap == null) return;

        // 현재 마우스 위치의 그리드 셀을 노란색 와이어프레임으로 표시
        Vector3 worldPosition = BreakWalltilemap.CellToWorld(CurrentMouseGridPosition);
        Vector3 cellSize = BreakWalltilemap.cellSize;

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireCube(worldPosition + cellSize * 0.5f, cellSize);
    }

    // 외부에서 호출할 수 있는 유용한 함수들

    /// <summary>
    /// 사용할 타일 변경
    /// </summary>
    public void SetTileType(TileBase newTile)
    {
        BreakWalltileToPlace = newTile;
        Debug.Log($"타일 타입 변경: {newTile?.name}");
    }

    /// <summary>
    /// 전체 타일맵 초기화
    /// </summary>
    public void ClearAllTiles()
    {
        // 타일맵의 경계 가져오기
        BoundsInt bounds = BreakWalltilemap.cellBounds;

        // 모든 타일을 null로 설정하여 제거
        TileBase[] emptyTiles = new TileBase[bounds.size.x * bounds.size.y * bounds.size.z];
        BreakWalltilemap.SetTilesBlock(bounds, emptyTiles);

        Debug.Log("모든 타일이 제거되었습니다!");
    }

    /// <summary>
    /// 현재 마우스 위치의 그리드 좌표 반환
    /// </summary>
    public Vector3Int GetCurrentMouseGridPosition()
    {
        return CurrentMouseGridPosition;
    }
}