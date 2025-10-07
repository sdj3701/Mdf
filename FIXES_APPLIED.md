# 적용된 수정 사항

## 1. 좌표 변환 문제 수정 (position.y → position.z)

### 수정된 파일:
1. **PlaceBestUnitAction.cs** (AI 경로 계산)
   - SpawnPoint/Goal 좌표 변환: `position.y` → `position.z`
   
2. **MonsterSpawner.cs** (몬스터 스폰)
   - 경로 탐색 시작/끝 좌표: `position.y` → `position.z` (2곳)
   
3. **Monster.cs** (몬스터 이동)
   - 경로 재탐색 좌표: `position.y` → `position.z`

## 2. PlacementManager 초기화 문제 수정

### 문제:
- 3D 모드에서 Tilemap이 null이어서 프리뷰가 표시되지 않음

### 수정:
```csharp
// Update()에서 초기화 확인
if (fieldManager == null || (obstacleTilemap == null && fieldManager.ground3D == null))
{
    return; // 아직 초기화 안 됨
}
```

## 3. FieldManager 드래그 앤 드롭 초기화 확인 추가

### 문제:
- ground3D가 null일 때 GetMouseWorldPosition()이 실패

### 수정:
```csharp
// HandleUnitDragAndDrop()에서 초기화 확인
if (ObstacleTilemap == null && ground3D == null)
{
    Debug.LogWarning("[FieldManager] Not initialized yet!");
    return;
}
```

## 4. 유닛 높이 시스템 (이전에 추가됨)

### FieldManager 설정:
- `groundYOffset`: Ground 바닥 높이 (기본 0)
- `wallYOffset`: 벽 위 높이 (기본 1)

### 적용된 메서드:
- `CreateUnitAt()`: 유닛 생성 시
- `MoveUnit()`: 유닛 이동 시
- `RegisterUnitAt()`: AI 배치 시

## 테스트 체크리스트

### Unity 씬에서 확인해야 할 사항:

1. **Ground 오브젝트 확인**
   - 이름이 정확히 "Ground"인지 확인
   - MeshRenderer가 있는지 확인
   - 위치와 크기가 적절한지 확인

2. **SpawnPoint/Goal 확인**
   - 위치가 그리드 범위 내에 있는지 확인
   - Transform 컴포넌트만 있으면 됨

3. **AstarGrid 확인**
   - bottomLeft, topRight 설정 확인
   - 그리드 범위가 Ground 크기와 일치하는지 확인

4. **FieldManager 인스펙터**
   - Ground Y Offset: 0
   - Wall Y Offset: 벽 높이 (예: 1.0)

5. **콘솔 로그 확인**
   - `[FieldManager] 3D 그리드 초기화` 로그 확인
   - `[AI Path Debug]` 로그에서 좌표 확인
   - 경고/에러 메시지 확인

## 디버깅 팁

### 1. SpawnPoint/Goal 위치 확인
콘솔에 이렇게 표시됩니다:
```
[AI Path Debug] Player 1 - Start: (3.62, 0.50, -10.00) -> (3, -10)
```
- Start: (3, -10) → X=3, Z=-10
- 이 좌표가 AstarGrid 범위 내에 있어야 함

### 2. AstarGrid 범위 확인
```
[AstarGrid] 그리드 경계: (-6, -14) ~ (5, -6)
```
- X축: -6 ~ 5 (12칸)
- Z축: -14 ~ -6 (8칸)
- SpawnPoint(3, -10)는 X는 OK, Z=-10도 OK

### 3. Ground 크기 확인
```
[FieldManager] 3D 그리드 초기화: Origin=(-6, 0, -14), Size(X,Z)=(12, 8)
```
- Ground의 크기와 위치가 자동 계산됨
- AstarGrid와 일치해야 함

## 여전히 에러가 발생한다면

### 확인 1: Ground 설정
```
Hierarchy:
Player1Grid
├── Ground (이름 정확히)
│   ├── MeshRenderer ✓
│   └── Transform: 적절한 위치/크기
```

### 확인 2: AstarGrid bottomLeft/topRight
SpawnPoint/Goal이 이 범위 안에 있어야 합니다.
예시:
- SpawnPoint: (3, 0, -10)
- Goal: (-4, 0, -10)
- AstarGrid: bottomLeft=(-6, -14), topRight=(5, -6)
- Z=-10이 -14 ~ -6 범위에 있으므로 OK

### 확인 3: 콘솔 로그
다음 로그들이 보여야 합니다:
```
✓ [Player 1]: Ground Tilemap 찾음? -> False
✓ [Player 1]: 3D Ground 찾음? -> True
✓ [Player 1]: FieldManager를 3D 모드로 초기화합니다.
✓ [FieldManager] 3D 그리드 초기화: Origin=..., Size(X,Z)=...
```

다음 경고가 나오면 안 됩니다:
```
❌ [FieldManager] playerCamera is null!
❌ [FieldManager] Both ObstacleTilemap and ground3D are null!
```

## 다음 단계

1. **Unity에서 플레이 모드 실행**
2. **콘솔 로그 확인** - 초기화 메시지 확인
3. **유닛 구매** - 자동 배치되는지 확인
4. **유닛 드래그** - 마우스로 이동 가능한지 확인
5. **벽 설치** - 프리뷰가 보이고 설치되는지 확인

## 예상되는 문제와 해결책

### 문제: 여전히 좌표 범위 벗어남
**원인**: SpawnPoint/Goal의 Z 좌표가 AstarGrid 범위 밖
**해결**: SpawnPoint/Goal 위치를 조정하거나 AstarGrid bottomLeft/topRight 조정

### 문제: 드래그가 안 됨
**원인**: ground3D가 null
**해결**: Ground 오브젝트 이름을 정확히 "Ground"로 설정

### 문제: 벽 프리뷰가 안 보임
**원인**: PlacementManager가 초기화 전에 실행됨
**해결**: 코드에 이미 수정 적용됨, 재시작 필요

### 문제: 유닛이 Ground 아래로 떨어짐
**원인**: groundYOffset 설정 문제
**해결**: FieldManager 인스펙터에서 Ground Y Offset 조정
