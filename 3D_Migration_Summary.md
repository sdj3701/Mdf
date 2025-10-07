# 3D 마이그레이션 완료 요약

## 수정된 주요 기능

### 1. 좌표 변환 문제 해결

**AstarGrid 좌표 변환 수정:**
- `position.y` → `position.z` 사용
- SpawnPoint/Goal이 3D 공간의 Z축을 올바르게 사용

**PlaceBestUnitAction (AI):**
```csharp
// 수정 전
Vector2Int startPos = new Vector2Int(
    Mathf.FloorToInt(start.position.x), 
    Mathf.FloorToInt(start.position.y)  // ❌ Y축
);

// 수정 후
Vector2Int startPos = new Vector2Int(
    Mathf.FloorToInt(start.position.x), 
    Mathf.FloorToInt(start.position.z)  // ✅ Z축
);
```

### 2. 유닛 배치 높이 시스템 추가

**FieldManager 인스펙터 설정:**
```
[Header("유닛 배치 높이 설정")]
├── Ground Y Offset: 0.0        // 일반 Ground에 배치될 때
└── Wall Y Offset: 1.0          // 벽(BreakWall) 위에 배치될 때
```

**동작 방식:**
- 유닛 배치 시 해당 위치에 벽이 있는지 자동 확인
- 벽이 없으면: `gridOrigin.y + groundYOffset`
- 벽이 있으면: `gridOrigin.y + wallYOffset`

**적용 메서드:**
- `CreateUnitAt()`: 유닛 생성 시
- `MoveUnit()`: 유닛 이동 시 (드래그 앤 드롭)
- `RegisterUnitAt()`: AI 배치 시
- `UpdatePreviewDisplay()`: 배치 프리뷰 표시 시

**수정된 GridToWorld 함수:**
```csharp
public Vector3 GridToWorld(Vector2Int gridPos, bool checkForWall = false)
{
    float worldX = gridOrigin.x + (gridPos.x + 0.5f) * cellSize;
    float worldZ = gridOrigin.z + (gridPos.y + 0.5f) * cellSize;
    
    // 벽 체크
    float yOffset = gridOrigin.y + groundYOffset;
    if (checkForWall && ground3D != null)
    {
        Vector3Int gridPos3D = new Vector3Int(gridPos.x, gridPos.y, 0);
        if (GetWallAt(gridPos3D) != null)
        {
            yOffset = gridOrigin.y + wallYOffset;
        }
    }
    
    return new Vector3(worldX, yOffset, worldZ);
}
```

## Unity 인스펙터 설정

### FieldManager 설정 예시
```
[FieldManager]
├── [3D 그리드 설정]
│   ├── Grid Origin: (0, 0, 0)          // 자동 계산
│   ├── Cell Size: 1.0
│   └── Grid Size: (10, 8)              // X=10칸, Z=8칸
│
└── [유닛 배치 높이 설정]
    ├── Ground Y Offset: 0.0            // Ground 높이
    └── Wall Y Offset: 1.0              // 벽 위 높이 (벽 높이만큼 올림)
```

### 벽 높이 설정 가이드
- **Ground Y Offset**: 보통 0으로 설정
- **Wall Y Offset**: 벽 3D 모델의 높이와 동일하게 설정
  - 예: 벽이 높이 1인 큐브 → `wallYOffset = 1.0`
  - 예: 벽이 높이 0.5인 큐브 → `wallYOffset = 0.5`

## 테스트 시나리오

### 1. 일반 Ground에 유닛 배치
1. 벽이 없는 빈 칸 클릭
2. 유닛이 Y = groundYOffset 높이에 배치됨
3. ✅ Ground 바닥에 정확히 배치

### 2. 벽 위에 유닛 배치
1. 벽이 설치된 칸 클릭
2. 유닛이 Y = wallYOffset 높이에 배치됨
3. ✅ 벽 위에 정확히 배치

### 3. 유닛 드래그 앤 드롭
1. 유닛을 Ground → 벽 위로 이동
2. 높이가 자동으로 변경됨
3. ✅ Ground/벽 위치에 따라 높이 조정

### 4. AI 자동 배치
1. AI가 유닛 구매 및 배치
2. 근접 유닛: Ground/벽 모두 배치 가능
3. 원거리 유닛: 벽 위에만 배치
4. ✅ 각 위치에 맞는 높이로 배치

### 5. 배치 프리뷰
1. 마우스를 Ground/벽 위로 이동
2. 프리뷰 큐브가 적절한 높이에 표시
3. ✅ 초록(유효) / 빨강(무효) 색상 표시

## 해결된 에러

### 1. AstarGrid 좌표 범위 에러
```
[AstarGrid] 시작점((3, 0)) 또는 끝점((-4, 0))이 그리드 범위를 벗어났습니다.
```
**원인**: `position.y` 대신 `position.z` 사용해야 함
**해결**: PlaceBestUnitAction에서 `position.z` 사용

### 2. AI 경로 탐색 실패
```
[AI] 몬스터 경로를 찾을 수 없습니다.
```
**원인**: SpawnPoint/Goal 좌표가 잘못 변환됨
**해결**: 3D 좌표계(X, Z) 올바르게 적용

### 3. 원거리 유닛 배치 실패
```
AI가 Ranged 타입의 유닛을 배치할 유효한 타일을 찾지 못했습니다.
```
**원인**: 3D 모드에서 벽 타일 정보가 올바르게 전달되지 않음
**해결**: `GetValidPlacementTiles()`에서 `placedWalls` 딕셔너리 사용

## 좌표 시스템 정리

### Vector2Int의 의미 (내부 사용)
- `x` → 3D의 **X축**
- `y` → 3D의 **Z축** (Y축 아님!)

### 실제 3D 월드 좌표
- X축: 좌우
- Y축: 높이 (groundYOffset / wallYOffset)
- Z축: 앞뒤

### 예시
```
gridSize = (10, 8)
→ X축: 0~9 (10칸)
→ Z축: 0~7 (8칸)
→ Y축: groundYOffset 또는 wallYOffset
```

## 다음 단계

1. **Ground 3D 모델 배치**
   - 이름: "Ground" 
   - MeshRenderer 필요
   - 크기에 맞게 Scale 조정

2. **벽 3D 모델 설정**
   - destructibleWallPrefab에 3D 모델 사용
   - 벽 높이에 맞게 wallYOffset 설정

3. **SpawnPoint/Goal 위치 조정**
   - 3D 공간 (X, Z)에 배치
   - Y축은 0으로 설정 권장

4. **테스트**
   - 유닛 배치 → 높이 확인
   - 벽 설치 후 유닛 배치 → 벽 위 높이 확인
   - AI 동작 → 경로 탐색 정상 작동 확인

## 주의사항

- **Y축 고정**: 유닛은 Y축으로 이동하지 않음 (Physics2D 사용)
- **Gizmos 확인**: Scene 뷰에서 AstarGrid의 Gizmos로 경로 확인 가능
- **디버그 로그**: SpawnPoint/Goal 좌표가 올바른지 콘솔 로그 확인
- **벽 높이**: wallYOffset은 벽 3D 모델의 실제 높이와 일치시켜야 함
