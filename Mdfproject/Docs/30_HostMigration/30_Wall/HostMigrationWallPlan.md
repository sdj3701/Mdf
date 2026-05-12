# Host Migration Wall Plan

정리일: 2026-04-22

기준 코드:
- `Assets/Scripts/Managers/FieldManager.cs`
- `Assets/Scripts/Managers/PlayerManager.cs`
- `Assets/Scripts/Network/HostMigrationHandler.cs`
- `Assets/Scripts/Managers/GameManagers.MigrationRecovery.cs`

---

## 0. 문서 목적

이 문서는 Host Migration 이후 벽 위치가 달라지거나, 벽 맵이 비어 있거나, 유닛/경로 계산이 꼬이는 문제를 현재 코드 기준으로 정리한 문서다.

핵심은 "벽 생성"보다 "벽 맵 재구성"이다.

---

## 1. 현재 코드 상태

### 1-1. migration 중 랜덤 영구벽 재생성은 막혀 있다

`FieldManager.GeneratePermanentWallsIfNeeded()`는 migration 중이면 랜덤 생성으로 들어가지 않는다.

현재 동작:

- 기존 영구벽이 이미 복원되어 있으면 그것을 채택
- 없으면 네트워크 복원/동기화 결과를 기다림
- migration 중 랜덤 재생성을 하지 않음

즉, 예전처럼 migration 순간에 새 랜덤 벽을 다시 뽑는 구조는 아니다.

### 1-2. 벽 맵 복구의 표준 진입점은 `RebuildWallMapsAfterMigration(...)`다

현재 복구 흐름에서 벽 관련 표준 엔트리는 아래다.

- `PlayerManager.RebindRuntimeReferencesAfterMigration(...)`
- `GameManagers.AreWallMapsReadyForMigration(...)`
- `HostMigrationHandler.RunMigrationSmokeChecksCoroutine()`

모두 `FieldManager.RebuildWallMapsAfterMigration(...)`를 사용한다.

즉, Host Migration 이후 벽 딕셔너리의 정합성은 "실제 오브젝트를 다시 스캔해서 맵을 재구성"하는 방식으로 맞춘다.

### 1-3. `CreatePermanentWallAt(...)`는 네트워크 prefab이면 `Runner.Spawn(...)`을 쓴다

현재 구현:

- prefab이 `NetworkObject`를 가지면 `Runner.Spawn(...)`
- 아니면 `Instantiate(...)`
- 네트워크 spawn 경로는 state authority가 있는 경우만 허용

즉, 영구벽을 네트워크 오브젝트로 운용할 때 host migration 복원 경로와 자연스럽게 맞물린다.

### 1-4. `ApplyPermanentWallsFromServer(...)`는 이제 "생성 명령"보다 "복원 힌트"에 가깝다

네트워크 영구벽을 사용하는 경우:

- RPC가 벽을 직접 만들지 않는다.
- expected cell 목록만 받고
- 실제 네트워크로 복원된 wall object를 기다린 뒤
- `RebuildWallMapsAfterMigration(...)`로 딕셔너리를 복구한다.

이 구조는 wall object와 wall map의 source를 맞추는 데 유리하다.

### 1-5. wall readiness는 flow resume 게이트와 smoke check 둘 다 본다

현재 wall 관련 보정은 두 번 걸린다.

1. `GameManagers.WaitForRestoreDependenciesAndResumeFlow()`
   - `AreWallMapsReadyForMigration(...)`
2. `HostMigrationHandler.RunMigrationSmokeChecksCoroutine()`
   - `RebuildWallMapsAfterMigration(...)`
   - `RebuildUnitMapAfterMigration(...)`

즉, 벽 맵이 준비되지 않으면 flow resume과 post-check 양쪽에서 잡힌다.

---

## 2. 현재 기준 결론

벽 문제는 예전 문서처럼 "벽 생성 버그" 하나로 보기보다, 현재는 아래 셋으로 나눠 보는 편이 맞다.

1. wall object 복원
2. wall dictionary 재구성
3. wall map 기준으로 unit/path 계산 재연결

현재 코드는 2번과 3번 쪽에 상당한 보강이 들어가 있다.

---

## 3. 아직 남아 있는 리스크

### W-01. `permanentWallsGenerated`는 여전히 로컬 bool이다

현재는 rebuilt map 결과에 따라 `true`로 승격시키는 보정이 들어가 있지만, 이 값 자체가 네트워크 authoritative state는 아니다.

### W-02. world position 기반 역산 민감도는 완전히 사라지지 않았다

벽 맵 재구성은 좋아졌지만, 그리드/ground 재초기화 순서에 따라 좌표 역산 민감도가 완전히 없어졌다고 보기는 어렵다.

### W-03. 동적 파괴/재배치까지 완전한 SSOT는 아니다

현재 문서 범위는 영구벽과 기본 wall map 복구가 중심이다.  
전투 중 파괴/생성되는 동적 벽 규칙까지 완전한 versioned state로 관리하는 구조는 아직 아니다.

---

## 4. 다음 작업

1. wall readiness 검증 강화
   - expected permanent wall 수
   - rebuilt dictionary 수
   - pathfinding 결과 비교
2. wall source of truth 정리
   - 가능하면 wall object가 자신의 grid 좌표를 직접 보유
3. dynamic wall 이벤트 범위 점검
   - 파괴 직후 migration
   - 재생성 직후 migration

---

## 5. 테스트 체크리스트

- host migration 후 영구벽 위치가 이전과 동일한가
- 벽 맵 재구성 후 유닛 배치/이동이 정상인가
- pathfinding 결과가 migration 전후 동일한가
- 네트워크 wall prefab 사용 시 클라이언트가 RPC로 중복 생성하지 않는가
- wall map ready 전에는 game flow가 resume되지 않는가
