# Host Migration Master Plan

작성일: 2026-03-02  
정리일: 2026-04-22

기준 코드:
- `Assets/Scripts/Network/HostMigrationHandler.cs`
- `Assets/Scripts/Network/NetworkManager.cs`
- `Assets/Scripts/Managers/GameManagers.MigrationRecovery.cs`
- `Assets/Scripts/Managers/GameManagers.PlayerRegistry.cs`
- `Assets/Scripts/Managers/GameManagers.cs`
- `Assets/Scripts/Managers/PlayerManager.cs`
- `Assets/Scripts/Managers/FieldManager.cs`
- `Assets/Scripts/Game/Monsters/MonsterSpawner.cs`

---

## 0. 문서 목적

이 문서는 현재 `HostMigrationHandler` 구현을 기준으로 MDF 프로젝트의 Host Migration 복구 흐름을 다시 정리한 기준 문서다.

이제 문서의 기준은 "예정 설계"가 아니라 "현재 코드에 실제로 들어간 복구 단계"다.

---

## 1. 현재 구현 요약

현재 Host Migration은 아래 순서로 동작한다.

1. `NetworkManager.OnHostMigration(...)`가 `HostMigrationHandler.StartMigration(...)`으로 위임한다.
2. `HostMigrationHandler`가 마이그레이션 플래그, UI, 게임 상태 캐시를 준비한다.
3. 기존 `Runner`를 먼저 `ShutdownReason.HostMigration`으로 정리한다.
4. `HostMigrationToken`으로 새 `Runner`를 시작한다.
5. `HostMigrationResume(...)`에서 runtime `NetworkObject`를 다시 spawn하고 snapshot state를 복사한다.
6. scene `NetworkObject`는 `GetResumeSnapshotNetworkSceneObjects()` 기반으로 `CopyStateFrom(...)`만 수행한다.
7. 새 `Runner` 기준 `GameManagers.Instance`를 재바인딩한다.
8. `WaitAndRestoreGameManagers(...)`가 `Runner`, `StateAuthority`, `GameManagers`, `PlayerManager` 준비 상태를 점검한 뒤 `GameManagers.RestoreAfterHostMigration()`을 호출한다.
9. `GameManagers`가 UI 복구, 플레이어 재연결, 상점 복구, 타이머 정지/복원, 전투 재부트스트랩을 진행한다.
10. `HostMigrationHandler`가 AI takeover reconciliation과 smoke check를 수행한다.

---

## 2. 실제 복구 플로우

### 2-1. 진입

- `NetworkManager.OnHostMigration(...)`
- `HostMigrationHandler.StartMigration(...)`

`StartMigration(...)`에서 이미 반영된 항목:

- `_isMigrating = true`
- `_migrationRecoverySucceeded = false`
- `_aiTakeoverReady = false`
- migration UI 표시
- `GameEvents.TriggerHostMigrationStarted()`
- `CacheCurrentGameState(...)`

### 2-2. Runner 전환

- `RestartAsNewHostCoroutine(...)`
- `ShutdownRunnerForMigration(...)`
- `StartGameWithMigrationTokenAsync(...)`
- `StartGameWithMigrationToken(...)`

현재 구현 특징:

- old runner를 먼저 정리한 뒤 new runner를 시작한다.
- 새 runner는 별도 `GameObject("NetworkRunner_Migrated")`에 생성된다.
- `NetworkManager.Instance`를 callback으로 다시 등록한다.
- `HostMigrationResume`를 `StartGameArgs.HostMigrationResume`에 연결한다.
- 시작 성공 후 `NetworkManager.SetRunnerAfterMigration(...)`로 활성 runner를 교체한다.

### 2-3. Snapshot 복원

- `HostMigrationResume(...)`
- `RestoreSceneObjectsFromSnapshot(...)`

현재 구현 특징:

- runtime object는 `runner.GetResumeSnapshotNetworkObjects()`를 순회하며 `runner.Spawn(...) + CopyStateFrom(...)`로 복원한다.
- 위치/회전은 가능하면 `NetworkTRSP.Data`에서 직접 읽는다.
- scene object는 spawn하지 않고 snapshot source를 scene object에 복사한다.
- 복원된 `GameManagers`를 `_restoredGameManagersCandidate`에 캐시하고 `GameManagers.Instance`를 새 runner 기준으로 교체한다.

### 2-4. GameManagers 재개 게이트

- `WaitAndRestoreGameManagers(...)`
- `ResolveGameManagersForRunner(...)`
- `EnsurePlayersRuntimeReady(...)`
- `TryApplyCachedStateBeforeRestore(...)`

현재 게이트:

- `GameManagers.Instance.Runner == expectedRunner`
- 새 host면 `GameManagers.Object.HasStateAuthority == true`
- `GameManagers.IsReadyForNetworkAccess == true`
- `PlayerManager.RebindRuntimeReferencesAfterMigration(...)` 이후 `PlayerManager.IsRuntimeReady(...) == true`

이 게이트를 통과하면 `GameManagers.RestoreAfterHostMigration()`가 호출된다.

### 2-5. GameManagers 로컬 복구

- `RestoreAfterHostMigration()`
- `RestoreLocalUIAfterMigrationAsync()`
- `WaitForRestoreDependenciesAndResumeFlow()`
- `ResumeGameFlowFromCurrentState()`

현재 구현 특징:

- `ChangeDetector` 재초기화
- `RelinkLocalPlayer()` / `RebuildNetworkPlayersAfterMigration(...)`
- `CommandProcessor` 보정
- `SetupGameUI(forceRefresh: true)` 기반 UI 복구
- Prepare 상태에서 상점 snapshot 우선 복구, 실패 시 host 한정 fallback reroll
- phase timer를 잠시 멈췄다가 조건 충족 후 복원
- Battle 상태면 `RebootstrapBattleAfterMigrationIfNeeded(...)`로 진입 부수효과를 다시 적용

### 2-6. AI takeover / smoke check

- `ReconcileAIControllersAfterMigrationCoroutine(...)`
- `EnsureAIControllersAfterMigration(...)`
- `RunMigrationSmokeChecksCoroutine()`

현재 구현 특징:

- 입력 권한 소유자가 사라진 `PlayerManager`는 `InputAuthority = None`으로 정리한다.
- `AIPlayerController`를 붙여 AI takeover를 활성화한다.
- 사람 플레이어에는 AI 컨트롤러를 제거한다.
- smoke check는 아래 항목을 본다.
  - `GameManagers.Runner` 일치 여부
  - `AllPlayers` 수와 runtime player 수 일치 여부
  - 각 player의 wall map / unit map 준비 여부
  - 필드 유닛 data / animation proxy 누락 여부
  - Prepare 상태 shop snapshot 존재 여부
  - Battle 상태 AI attacker 스폰 누락 여부
  - `_aiTakeoverReady` 최종 상태

---

## 3. 현재 기준 불변조건

현재 문서 기준의 핵심 불변조건은 아래다.

1. `NetworkManager._runner`는 마이그레이션 완료 후 새 runner를 가리켜야 한다.
2. `GameManagers.Instance`는 새 runner에 속한 복원 객체여야 한다.
3. 새 host는 `GameManagers.Object.HasStateAuthority == true`여야 한다.
4. `AllPlayers`에 들어간 모든 `PlayerManager`는 runtime reference 재결선이 끝나 있어야 한다.
5. Prepare/Battle에서 필요한 `phaseTimer`는 pause 후 정상 복원되어야 한다.
6. Battle 상태면 `_battleOpponents`, `_matchFirstAttacker`, `AttackMonsterPool`, defender field monster 상태가 서로 맞아야 한다.
7. 벽/유닛 맵은 `FieldManager.RebuildWallMapsAfterMigration(...)`, `RebuildUnitMapAfterMigration(...)` 이후 ready 상태여야 한다.
8. migration 중에는 `CommandProcessor.ProcessCommands()`가 보류되어야 한다.

---

## 4. 이미 코드에 반영된 항목

기존 계획 문서에서 "해야 함"으로 적혀 있었지만 지금은 실제 코드에 반영된 항목:

- old runner 선종료 후 new runner 시작 순서
- `HostMigrationToken` 기반 세션 재시작
- cached state promote (`TryApplyCachedStateForMigration`)
- `GameManagers.Instance` 재바인딩
- migration 중 command hold
- Prepare UI / 상점 복구 게이트
- Battle rebootstrap
- AI takeover reconciliation
- wall map / unit map smoke check
- connection token 기반 reconnect reassociation

---

## 5. 현재 코드 기준 남은 갭

### G-01. reconnect cache 유지 범위가 짧다

`OnMigrationComplete()`에서 `_cachedPlayerData.Clear()`를 호출한다.  
즉, migration 직후 늦게 재접속하는 플레이어는 `TryReassociateDisconnectedPlayer(...)` 경로를 더 이상 타지 못한다.

### G-02. 실패 시 강제 fallback이 없다

복구 실패 시 현재 핸들러는 로그와 이벤트까지만 처리한다.  
자동 로비 복귀나 재시도 UX는 `HostMigrationHandler` 내부에 직접 연결되어 있지 않다.

### G-03. `RequestStateAuthorityForOrphanedObjects(...)`는 구현만 있고 사용되지 않는다

현재 실질적인 권한 복구는 `GameManagers` 직접 요청과 runner 복원 흐름에 의존한다.  
orphan object 일괄 권한 요청 루틴은 아직 호출되지 않는다.

### G-04. smoke check는 탐지 중심이다

현재 smoke check는 문제를 기록하지만 자동 self-heal을 추가로 수행하지는 않는다.

---

## 6. 다음 작업 우선순위

1. reconnect cache를 migration 완료 직후 바로 비우지 말고, 일정 시간 또는 재연결 완료까지 유지한다.
2. 복구 실패 시 정책을 명확히 정한다.
   - 즉시 로비 복귀
   - 재시도
   - 복구 실패 UI + 사용자 선택
3. orphan state authority 보정 루틴을 실제 flow에 연결할지 결정한다.
4. smoke check에서 탐지된 항목 중 self-heal 가능한 항목을 분리한다.
   - wall map rebuild 재시도
   - Prepare shop snapshot 재적용
   - Battle auto-spawn 재기동

---

## 7. 문서 운영 기준

- 이 문서는 Host Migration 전체 흐름의 상위 기준 문서다.
- `Prepare`, `Battle`, `Wall` 세부 계획은 각 하위 문서에서 관리한다.
- 이후 코드가 바뀌면 "예상 설계"보다 "실제 메서드 흐름"을 먼저 반영한다.
