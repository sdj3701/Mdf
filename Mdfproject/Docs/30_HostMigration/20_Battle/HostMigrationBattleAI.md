# Host Migration Battle AI Plan

정리일: 2026-04-22

기준 코드:
- `Assets/Scripts/Network/HostMigrationHandler.cs`
- `Assets/Scripts/Managers/GameManagers.MigrationRecovery.cs`
- `Assets/Scripts/Managers/GameManagers.PlayerRegistry.cs`
- `Assets/Scripts/Managers/PlayerManager.cs`
- `Assets/Scripts/Game/Monsters/MonsterSpawner.cs`

---

## 0. 문서 목적

이 문서는 Battle1 / Battle2 진행 중 Host Migration이 발생했을 때,

- 전투 매핑
- 공격자 풀
- AI takeover
- 자동 스폰
- 로컬 전투 UI 재시작

이 다섯 항목이 현재 코드에서 어떻게 복구되는지 정리한 문서다.

---

## 1. 현재 코드 상태

### 1-1. AI takeover reconciliation은 `HostMigrationHandler` 쪽에 있다

현재 경로:

- `ReconcileAIControllersAfterMigrationCoroutine(...)`
- `EnsureAIControllersAfterMigration(...)`

현재 구현 내용:

- active player 목록을 기준으로 입력 권한이 끊긴 슬롯을 찾는다.
- 입력 소유자가 이미 떠난 경우 `AssignInputAuthority(PlayerRef.None)`로 정리한다.
- 입력 권한이 없는 `PlayerManager`에는 `AIPlayerController`를 붙인다.
- 사람 플레이어 슬롯에 AI controller가 남아 있으면 제거한다.
- 이 과정이 안정적으로 2회 연속 통과하면 `_aiTakeoverReady = true`가 된다.

### 1-2. GameManagers flow resume은 AI takeover 완료를 기다린다

`WaitForRestoreDependenciesAndResumeFlow()`는 `IsMigrationAiTakeoverReady(...)`를 통해 아래를 게이트로 사용한다.

- server runner인지
- `HostMigrationHandler.Instance`가 존재하는지
- `HostMigrationHandler.IsAiTakeoverReady == true`인지

즉, Battle flow는 AI takeover가 끝나기 전에는 resume되지 않는다.

### 1-3. Battle rebootstrap이 실제로 구현되어 있다

현재 핵심 복구 메서드:

- `RebootstrapBattleAfterMigrationIfNeeded(...)`

이 메서드가 하는 일:

1. 현재 상태가 `Battle1` 또는 `Battle2`인지 확인
2. 새 host가 state authority를 갖는지 확인
3. `EnsureBattleMappingAfterMigration()`로 매핑 보정
4. 공격자/수비자 runtime reference 재결선
5. attacker / defender runtime ready 검사
6. 필요하면 `RefreshAttackMonsterPool(...)`
7. `TryAcquireBattleRebootstrapKey(...)`로 동일 battle 재실행 방지
8. defender 필드에 이미 살아있는 몬스터가 있으면 스킵
9. `RPC_NotifyBattleStart(...)`를 다시 발행해 로컬 UI/카메라/입력 경로 복원
10. AI attacker면 `SpawnAllMonstersToTargetField(...)` 재실행

### 1-4. 자동 스폰 중복 방지는 `MonsterSpawner`까지 연결돼 있다

`MonsterSpawner`는 battle bootstrap key를 받는다.

현재 중복 방지 장치:

- `_activeAutoSpawnKeys`
- `_completedAutoSpawnKeys`
- `TryBeginAutoSpawnForKey(...)`
- `EndAutoSpawnForKey(...)`

즉, `GameManagers`에서 한 번 막고, `MonsterSpawner`에서 한 번 더 막는 구조다.

### 1-5. smoke check도 Battle 누락 케이스를 본다

현재 `RunMigrationSmokeChecksCoroutine()`는 Battle 상태에서 아래 케이스를 실패로 기록한다.

- AI attacker가 존재
- attacker의 `AttackMonsterPool`이 남아 있음
- defender 필드에 살아있는 몬스터가 없음

이 경우 `battleSpawnPending` 오류를 남긴다.

---

## 2. 현재 기준 결론

Battle 쪽은 기존 문서의 핵심 우려였던

- "상태는 복원됐는데 전투 부수효과가 빠짐"

문제에 대해 현재 코드에 직접 대응이 들어가 있다.

현재 구조는 아래처럼 정리된다.

- 전투 상태 값 복원: snapshot + cached state promote
- 전투 매핑 복원: `EnsureBattleMappingAfterMigration()`
- AI takeover 복원: `HostMigrationHandler`
- 전투 부수효과 복원: `RebootstrapBattleAfterMigrationIfNeeded()`
- 중복 자동 스폰 방지: battle key + auto spawn key

즉, Battle 문서는 이제 "원인 추정"보다 "현재 구현 검증 + 남은 edge case 정리"가 중심이어야 한다.

---

## 3. 아직 남아 있는 리스크

### B-01. human attacker 경로는 smoke check가 약하다

사람 공격자는 auto spawn이 아니라 UI/manual input 경로를 탄다.  
현재 smoke check는 AI attacker 누락을 잘 보지만, human attacker 로컬 UI 경로는 상대적으로 약하게 본다.

### B-02. late migration에서는 공격자 풀 refresh가 제한된다

현재 `allowPoolRefresh`는 전투 초반 구간에서만 true가 되도록 제한되어 있다.  
이는 중복 재생성을 막기 위한 의도지만, late-phase edge case 검증은 더 필요하다.

### B-03. 로컬 카메라/전투 UI 회귀 테스트가 필요하다

`RPC_NotifyBattleStart(...)` 재발행은 들어가 있지만, 실제 클라이언트 체감 경로까지 자동 검증하지는 않는다.

---

## 4. 다음 작업

1. human attacker migration 회귀 테스트 추가
   - 전투 UI
   - 카메라 전환
   - 수동 몬스터 소환
2. battle smoke check 강화
   - human attacker local UI readiness
   - battle key별 auto spawn completion 여부
3. late-phase migration 정책 검토
   - 공격자 풀 refresh 제한이 맞는지
   - 타이머 말기에서 no-op가 맞는지

---

## 5. 테스트 체크리스트

- Battle1 시작 직후 host 종료 시 AI attacker 스폰이 다시 시작되는가
- Battle2 진행 중 host 종료 시 defender 필드 몬스터 상태가 유지되는가
- AI takeover 완료 전에는 flow가 resume되지 않는가
- 사람 공격자 화면에서 전투 UI / 카메라 / 입력 경로가 정상으로 돌아오는가
- 동일 battle key에서 자동 스폰이 2번 실행되지 않는가
