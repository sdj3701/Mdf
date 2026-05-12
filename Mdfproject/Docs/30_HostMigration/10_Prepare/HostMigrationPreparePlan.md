# Host Migration Prepare Plan

정리일: 2026-04-22

기준 코드:
- `Assets/Scripts/Managers/GameManagers.MigrationRecovery.cs`
- `Assets/Scripts/Managers/GameManagers.cs`
- `Assets/Scripts/Managers/FieldManager.cs`
- `Assets/Scripts/Managers/PlayerManager.cs`

---

## 0. 문서 목적

이 문서는 Prepare 단계 Host Migration 복구를 현재 코드 기준으로 정리한 문서다.

이제 Prepare 이슈의 핵심은 두 가지다.

1. 로컬 필드 입력이 너무 일찍 열려서 드래그/배치가 꼬이지 않는가
2. Prepare UI와 shop 상태가 중복 생성 없이 복구되는가

---

## 1. 현재 코드 상태

### 1-1. 필드 입력 게이트는 이미 들어가 있다

현재 `FieldManager.Update()`는 바로 `HandleUnitDragAndDrop()`를 호출하지 않는다.

실제 경로:

- `FieldManager.Update()`
- `CanProcessLocalFieldInput()`
- `GameManagers.IsPrepareInteractionReadyForField(...)`

즉, 아래 조건이 준비되기 전에는 Prepare 입력이 차단된다.

- 로컬 제어 필드인지
- `GameManagers`가 migration restore 중이 아닌지
- UI restore가 끝났는지
- 현재 상태가 Prepare인지
- local player / runtime reference가 정상인지

정리하면 예전 문서의 "입력이 너무 빨리 열린다" 문제는 현재 코드에서 게이트 구조로 이미 완화되어 있다.

### 1-2. UI 복구는 `RestoreLocalUIAfterMigrationAsync()` 중심이다

현재 Prepare UI 복구 주 경로:

1. `SetupGameUI(forceRefresh: true)`
2. `TriggerMigrationReadyEventOnce(...)`
3. `TriggerMigrationStateChangedOnce(...)`
4. `HandleUIForNewState(currentState)`
5. Prepare 상태면 augment UI 확인
6. augment 선택지가 없으면 `ShowLocalShopFallbackAsync()`

중복 방지 장치:

- ready event는 1회만 발행
- state changed event는 state별 1회만 발행
- flow resume 전까지 `MigrationRestoreStage`로 단계 관리

### 1-3. shop 복구는 snapshot 우선, reroll fallback 후순위다

현재 Prepare shop 복구 경로:

- `EnsurePrepareShopRecoveredAndSyncedAsync(...)`

복구 순서:

1. `ShopManager.ApplySnapshotFromNetworkAsync(...)` 우선 시도
2. 실패 시 host + Prepare 상태에서만 fallback reroll 허용
3. reroll은 `TryAcquirePrepareShopRecoveryKey(...)`로 trace/player/round/state 기준 1회만 허용
4. 마지막에 다시 network snapshot 적용 재시도

즉, 현재 구조는 "복원은 read/apply 우선, mutate는 host fallback 한정"으로 정리돼 있다.

### 1-4. flow resume는 UI만 끝났다고 바로 열리지 않는다

`WaitForRestoreDependenciesAndResumeFlow()`는 아래 조건을 모두 본다.

- `hasAuthority`
- `runnerMatched`
- `uiReady`
- `playersReady`
- `mappingReady`
- `timerReady`
- `wallMapReady`
- `aiTakeoverReady`

Prepare는 특히 `uiReady`, `playersReady`, `wallMapReady`가 중요하다.

---

## 2. 현재 기준 결론

Prepare 쪽은 예전 문서보다 구현이 많이 진행돼 있다.

현재 상태를 짧게 정리하면:

- 입력 조기 개방 문제: 게이트 추가로 1차 대응 완료
- UI 중복 이벤트 문제: one-shot 이벤트로 1차 대응 완료
- shop 복구 문제: network snapshot 우선 + host fallback reroll 구조로 정리 완료
- flow resume race: restore stage + dependency gate로 1차 대응 완료

즉, Prepare 문서는 이제 "설계 제안"보다 "검증과 보강" 문서로 보는 편이 맞다.

---

## 3. 아직 남아 있는 리스크

### P-01. `pointerOverUI` / raycast stuck 회귀는 자동 검증이 없다

입력 게이트는 들어갔지만, 실제 UI panel의 `blocksRaycasts` 상태가 migration 후 항상 정상인지까지 smoke check 하지는 않는다.

### P-02. shop snapshot 정합성 검사는 존재 여부 수준이다

현재 smoke check는 Prepare 상태에서 `TryGetShopSnapshot(...)` 성공 여부만 본다.  
item count, revision, 실제 UI 반영 결과까지 비교하지는 않는다.

### P-03. fallback reroll은 best-effort다

network snapshot 복구가 늦게 오거나 shop database 로딩이 지연되는 경우, 현재 구조는 최대한 안전하게 처리하지만 완전한 transaction 구조는 아니다.

---

## 4. 다음 작업

1. migration 직후 Prepare UI 상태 자동 검증 추가
   - shop panel active 여부
   - raycast 차단 상태
   - local input gate 해제 시점
2. shop snapshot smoke check 강화
   - revision 비교
   - item count 비교
   - UI view 모델 비교
3. Prepare 입력 회귀 테스트 시나리오 정리
   - 드래그
   - 판매
   - reroll
   - augment UI

---

## 5. 테스트 체크리스트

- Prepare 도중 host 종료 후 로컬 필드 드래그가 정상 동작하는가
- shop panel이 2번 뜨지 않는가
- reroll 비용/아이템 수가 중복 반영되지 않는가
- augment UI가 있으면 shop fallback으로 잘못 내려가지 않는가
- migration 완료 전에는 입력이 막히고, 완료 후에는 정상 해제되는가
