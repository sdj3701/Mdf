# Migration Wall Plan

정리일: 2026-04-22

기준 코드:
- `Assets/Scripts/Managers/FieldManager.cs`
- `Assets/Scripts/Managers/PlayerManager.cs`
- `Assets/Scripts/Game/Game Rules/DestructibleWall.cs`

---

## 0. 문서 목적

이 문서는 현재 벽 복구 코드 위에 추가로 어떤 구조 개선을 할지 정리한 하위 계획 문서다.

`HostMigrationWallPlan.md`가 "현재 상태 정리"라면, 이 문서는 "다음 리팩토링 방향"에 가깝다.

---

## 1. 현재 코드 기준 베이스라인

현재 벽 복구는 아래 축으로 돌아간다.

- wall object 복원: Fusion snapshot / network spawn
- wall map 복원: `RebuildWallMapsAfterMigration(...)`
- unit map 복원: `RebuildUnitMapAfterMigration(...)`
- 영구벽 생성 차단: migration 중 `GeneratePermanentWallsIfNeeded()` skip
- 클라이언트 동기화: `ApplyPermanentWallsFromServer(...)`가 hint 역할 수행

이 베이스라인은 "복구는 된다" 쪽으로는 많이 좋아졌지만, 구조적으로는 아직 개선 여지가 있다.

---

## 2. 목표 구조

### 2-1. wall 좌표의 SSOT를 더 명확히 한다

현재는 실제 오브젝트와 wall map rebuild가 기준이지만, 앞으로는 각 wall object가 grid 좌표를 더 명확하게 보유하는 쪽이 안전하다.

권장 방향:

- wall object 자체가 authoritative grid 좌표를 가진다
- rebuild는 world position 역산보다 wall metadata를 우선 사용한다

### 2-2. generation state를 로컬 bool에서 더 신뢰 가능한 상태로 옮긴다

현재 `permanentWallsGenerated`는 보조 플래그 역할은 가능하지만 authoritative state는 아니다.

권장 방향:

- wall set 존재 여부
- revision
- expected cell count

같은 상태를 복구 기준으로 쓸 수 있도록 정리한다.

### 2-3. smoke check를 "개수 확인"에서 "내용 비교"로 확장한다

현재 smoke는 wall map ready 여부 중심이다.  
앞으로는 아래 비교가 가능해야 한다.

- expected permanent wall 좌표 집합
- rebuilt permanent wall 좌표 집합
- dynamic wall 좌표 집합
- pathfinding 결과 요약

---

## 3. 단계별 작업안

### Phase 1. 빠른 보강

1. wall rebuild 결과를 로그 문자열이 아니라 구조화된 summary로 반환
2. smoke check에서 expected/matched cell 수 비교
3. dynamic wall 케이스 분리 로그 추가

### Phase 2. 중간 리팩토링

1. `DestructibleWall` 또는 wall owner 컴포넌트에 grid 좌표 보존 강화
2. rebuild가 world position 역산보다 wall metadata를 우선 사용하도록 변경
3. `permanentWallsGenerated` 의존도를 줄이고 rebuilt state 파생값을 우선 사용

### Phase 3. 장기 안정화

1. wall state snapshot/revision 설계
2. migration 직후 pathfinding snapshot 비교 자동화
3. wall/unit/path 3종 smoke를 하나의 복구 validator로 묶기

---

## 4. 지금 바로 하지 않을 것

아래는 지금 당장 건드리기보다, 현 구조가 더 안정된 뒤 보는 편이 낫다.

- 전체 wall 시스템의 완전 재작성
- 오프라인/온라인 벽 생성 경로 통합 대수술
- pathfinding 시스템 전체 교체

---

## 5. 완료 기준

이 문서의 작업이 끝났다고 보려면 아래가 만족되어야 한다.

1. wall map rebuild가 좌표 역산 오차에 덜 민감해야 한다.
2. migration 직후 permanent wall 집합 비교가 가능해야 한다.
3. smoke check 로그만 보고도 어떤 벽이 빠졌는지 파악 가능해야 한다.
4. dynamic wall이 있는 라운드에서도 path 결과가 안정적이어야 한다.
