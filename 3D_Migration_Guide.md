# 3D 마이그레이션 완료 가이드

## 작업 개요
2D Tilemap 기반 프로토타입을 3D 공간으로 마이그레이션했습니다. 유닛/몬스터는 여전히 2D Physics(Physics2D, Collider2D)를 사용하며 Y축 이동은 없습니다.

## 주요 변경 사항

### 1. FieldManager
**추가된 기능:**
- **3D 논리 그리드 시스템**: 3D 공간 위에 논리적인 그리드 좌표계 구현
- **좌표 변환 함수들**:
  - `GridToWorld(Vector2Int)`: 논리 그리드 좌표 → 3D 월드 좌표
  - `WorldToGrid(Vector3)`: 3D 월드 좌표 → 논리 그리드 좌표
  - `GridToWorld(Vector3Int)`: 호환성용 오버로드
  - `WorldToGridInt(Vector3)`: Vector3Int 반환 버전
  - `IsValidGridPosition()`: 그리드 범위 검증

**설정 필드:**
```csharp
[Header("3D 그리드 설정")]
public Vector3 gridOrigin;      // 그리드 시작점
public float cellSize = 1f;     // 셀 크기 (미터)
public Vector2Int gridSize;     // 그리드 크기 (X, Z 칸 수)
public GameObject ground3D;     // 3D Ground 참조

[Header("유닛 배치 높이 설정")]
public float groundYOffset = 0f;  // 일반 Ground Y 오프셋
public float wallYOffset = 1f;    // 벽 위 배치 Y 오프셋
```

**초기화:**
- `Initialize(PlayerManager, GameObject)`: 3D Ground 기반 초기화 (새로 추가)
- `Initialize(PlayerManager, Tilemap, Tilemap)`: 2D Tilemap 기반 초기화 (호환성 유지)

**수정된 메서드들:**
- `CreateWallAt()`: Tilemap/3D 자동 감지
- `CreateUnitAt()`: Tilemap/3D 자동 감지
- `MoveUnit()`: Tilemap/3D 자동 감지
- `FindFirstEmptySlot()`: Tilemap/3D 자동 감지
- `GetMapBounds()`: 3D 그리드 범위 반환 지원
- `GetValidPlacementTiles()`: 3D 모드에서 논리 그리드 기반 계산
- `RegisterUnitAt()`: Tilemap/3D 자동 감지
- `GetMouseWorldPosition()`: 3D Raycast 또는 2D ScreenToWorldPoint

### 2. PlacementManager
**수정된 메서드들:**
- `GetMouseWorldPosition()`: 
  - 2D 모드: 기존 Plane Raycast
  - 3D 모드: Ground Plane에 Raycast
- `UpdateMousePosition()`: FieldManager의 `WorldToGridInt()` 사용
- `IsPositionValidForPlacement()`: 3D 모드에서 `FieldManager.IsValidGridPosition()` 사용
- `SetupPreviewObject()`: 
  - 2D 모드: SpriteRenderer 사용
  - 3D 모드: 투명 큐브(MeshRenderer) 프리뷰 생성
- `UpdatePreviewDisplay()`: FieldManager의 `GridToWorld()` 사용, 3D/2D 모드에 따라 색상 적용

**3D 프리뷰:**
- 유닛 배치: 낮은 큐브 (0.5 높이)
- 벽 배치: 정사각형 큐브
- 유효/무효 위치: 초록/빨강 투명 색상

### 3. PlayerManager
**초기화 로직 개선:**
```csharp
// 3D Ground 오브젝트 탐색
GameObject ground3D = gridInstance.transform.Find("Ground")?.gameObject;

// FieldManager 초기화 분기
    fieldManager.Initialize(this, ground3D);      // 3D 모드
else if (groundTilemap != null)
    fieldManager.Initialize(this, groundTilemap, obstacleTilemap); // 2D 모드

### 4. AstarGrid
**주요 수정:**
- `Initialize()`: `transform.position.y` → `transform.position.z` (3D 좌표)
- `OnDrawGizmos()`: 3D 공간(X, Z)에 Gizmos 표시

### 5. AI (PlaceBestUnitAction - RearrangeAllUnitsAction)
**주요 수정:**
- `RecalculateMonsterPath()`: SpawnPoint/Goal 좌표 변환 수정
  - `position.y` → `position.z` (3D의 Z축 사용)
- FieldManager의 좌표 변환 함수를 통해 자동으로 3D 호환
- `Collider2D` 사용은 유닛이 2D Physics를 유지하므로 그대로 유지
**AstarGrid 컴포넌트가 있는 GameObject**

```csharp
// PlayerManager에서 컴포넌트 찾기
this.astarGrid = gridInstance.GetComponentInChildren<AstarGrid>();
if (this.astarGrid != null)
{
    this.astarGrid.Initialize();
}
```

**필수 컴포넌트:**
- `AstarGrid` 스크립트 (프로젝트에 이미 존재하는 클래스)

**설정:**
- GameObject 생성
- AstarGrid 컴포넌트 추가
- 인스펙터에서 그리드 설정

**중요: Vector2Int는 (X, Z) 좌표를 의미합니다!**
- `bottomLeft.x` = 3D의 X 좌표
- `bottomLeft.y` = 3D의 **Z 좌표** (Y가 아님!)
- 예: `bottomLeft = (0, 0)`, `topRight = (7, 6)` → 8x7 그리드 (X: 0~7, Z: 0~6)

**예시:**
```
AstarGrid
├── Transform: Position (0, 0, 0)
└── AstarGrid (Script)
    ├── Bottom Left: (0, 0)      // X=0, Z=0
    ├── Top Right: (7, 6)         // X=7, Z=6 → 8x7 그리드
    ├── Wall Layers: Default, Wall, BreakWall
    ├── Breakable Wall Layer: BreakWall
    └── Detection Radius: 0.4
```

**주의:**
- AstarGrid는 내부적으로 `Vector2Int`를 사용하지만, 3D 공간의 (X, Z)에 매핑됩니다
- Gizmos는 3D 공간에 올바르게 표시됩니다 (Y축 0 높이)
- Physics2D는 유닛이 2D를 사용하므로 그대로 작동합니다)
   - `gridOrigin`, `gridSize`는 Ground의 bounds에서 자동 계산됨

4. **카메라**:
   - 쿼터뷰 각도 권장
   - PlacementManager와 FieldManager의 Raycast가 자동으로 3D 처리
### 2D 모드 유지하려면:
- 기존 Tilemap 구조 그대로 유지
- FieldManager가 자동으로 2D 모드로 작동

## 호환성
- **하위 호환성**: 2D Tilemap 프로젝트는 수정 없이 계속 작동
- **자동 감지**: FieldManager가 Tilemap 또는 3D Ground 존재 여부로 자동 모드 선택
- **2D Physics 유지**: 유닛/몬스터의 Collider2D, OnTriggerEnter2D 등은 그대로 사용

## 테스트 체크리스트
- [ ] 유닛 배치 (마우스 클릭)
- [ ] 유닛 드래그 앤 드롭
- [ ] 벽 설치/제거
- [ ] AI 유닛 자동 배치
- [ ] AI 유닛 재배치
- [ ] 전투 시작 시 유닛 동작
- [ ] 유닛 조합 (3성 업그레이드)
- [ ] 범위 표시 (공격/스킬)

## 주의사항
1. **Y축 고정**: 유닛은 Y축으로 이동하지 않으므로 `gridOrigin.y`는 바닥 높이로 고정
2. **2D Physics**: Collider2D, Rigidbody2D는 그대로 사용 (Physics2D.Raycast도 동일)
3. **Ground Layer**: 필요 시 Ground 오브젝트에 적절한 Layer 설정
4. **Grid 좌표계**: 논리 그리드는 (x, y)를 사용하지만 실제로는 3D의 (x, z)에 매핑됨
