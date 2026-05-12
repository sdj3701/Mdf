# Host Migration Character Duplication Issue

작성일: 2026-04-23

기준 코드:
- `Assets/Scripts/Network/HostMigrationHandler.cs`
- `Assets/Scripts/Network/NetworkManager.cs`
- `Assets/Scripts/Managers/GameManagers.MigrationRecovery.cs`
- `Assets/Scripts/Managers/GameManagers.PlayerRegistry.cs`
- `Assets/Scripts/Managers/PlayerManager.cs`
- `Assets/Scripts/Game/Units/Unit.cs`

---

## 0. 문서 목적

이 문서는 Host Migration 직후 아래 증상이 왜 발생하는지 현재 코드 기준으로 정리한 분석 문서다.

- 원래 있던 캐릭터가 사라지지 않고 기존 위치에 그대로 남아 있음
- 남아 있는 캐릭터는 이동도 공격도 하지 않고 더미처럼 보임
- 동시에 같은 플레이어/같은 캐릭터로 보이는 다른 객체가 하나 더 생겨서 실제 이동과 공격은 그쪽에서만 진행됨

핵심 결론은 현재 Host Migration 구조가

- `기존 오브젝트를 남기는 정책`
- `snapshot으로 새 오브젝트를 다시 spawn하는 정책`

을 동시에 사용하고 있기 때문이라는 점이다.

---

## 1. 현재 증상의 정체

지금 보이는 두 객체는 역할이 다르다.

### 1-1. 움직이고 공격하는 캐릭터

- Host Migration 이후 새 `Runner`에 소속된 복원 객체다.
- `HostMigrationResume(...)`에서 resume snapshot 기준으로 새로 spawn된 객체일 가능성이 높다.
- `GameManagers`, `CommandProcessor`, `CombatScheduler`, `MonsterSpawner`가 참조하는 쪽도 이 객체다.

### 1-2. 움직이지 않고 공격도 안 하는 더미 캐릭터

- 이전 `Runner`에 묶여 있던 예전 객체가 장면에 남아 있는 것이다.
- 시각적으로는 남아 있지만 현재 게임 흐름에서는 더 이상 “활성 객체”로 취급되지 않는다.
- 그래서 위치는 남아 있는데 이동/공격/입력/전투 재개가 일어나지 않는다.

즉, “멈춘 캐릭터가 잘못된 상태”가 아니라 “이미 버려졌어야 할 예전 객체가 화면에 남아 있는 상태”에 가깝다.

---

## 2. 왜 예전 캐릭터가 장면에 남는가

## 2-1. 코드가 원래 오브젝트 생존을 허용하고 있다

`HostMigrationHandler.cs` 상단 주석 기준으로 현재 전제는 아래와 같다.

- `Destroy When State Authority Leaves = FALSE`
- `Allow State Authority Override = TRUE`

이 설정은 Host가 나가더라도 네트워크 오브젝트가 바로 파괴되지 않도록 만드는 방향이다.

즉, Host Migration이 발생했을 때 “기존 런타임 오브젝트가 일단 남아 있을 수 있는 구조”다.

## 2-2. 그런데 실제 복구 코드는 새 Runner에서 전부 다시 spawn한다

현재 복구 핵심은 `HostMigrationHandler.HostMigrationResume(...)`다.

여기서는 `runner.GetResumeSnapshotNetworkObjects()`로 받은 resume object를 순회하면서 각 객체마다:

- `runner.Spawn(...)`
- `CopyStateFrom(...)`

을 실행한다.

즉, 기존 객체를 재사용하는 것이 아니라 snapshot 기준으로 새 객체를 다시 만들고 있다.

## 2-3. old Runner 정리가 “비활성화” 수준에서 끝난다

`ShutdownRunnerForMigration(...)`에서 old runner는:

- `Shutdown(false, ShutdownReason.HostMigration, false)` 호출
- callback 제거
- `runnerToCleanup.enabled = false`

까지만 수행된다.

하지만 이 과정에서

- old runner가 관리하던 런타임 `NetworkObject`
- 기존 `PlayerManager`, `Unit`, `Monster`
- old runner용 `GameObject`

를 명시적으로 despawn/destroy하는 정리 단계는 없다.

즉, old runner 쪽 오브젝트를 화면에서 없애지 않은 상태로, new runner 쪽 오브젝트를 다시 spawn하고 있다.

이 구조면 캐릭터가 두 벌 생기는 것이 자연스럽다.

---

## 3. 왜 남은 캐릭터는 더미처럼 보이는가

남아 있는 old object는 시각적으로는 존재하지만 현재 게임 로직에서 거의 모두 제외된다.

## 3-1. GameManagers는 현재 Runner 기준 객체만 다시 묶는다

`GameManagers.PlayerRegistry`와 `HostMigrationHandler`는 복구 후 플레이어를 다시 묶을 때 아래 기준을 사용한다.

- `player.Runner == expectedRunner`
- `player.Runner == Runner`
- `player.Object != null && player.Object.IsValid`

즉, old runner 소속 객체는 재등록 대상에서 빠진다.

`RebuildNetworkPlayersAfterMigration(...)`도 같은 `playerId`가 여러 개 있어도 “현재 Runner 기준 하나”만 `NetworkPlayers`에 다시 넣고, 나머지는 논리적으로 버린다.  
하지만 이 메서드는 “버려진 객체를 파괴”하지는 않는다.

결과:

- 새 객체는 논리적으로 사용됨
- 예전 객체는 논리적으로는 버려지지만 화면에는 남음

## 3-2. 이동/공격/커맨드 경로는 State Authority와 활성 Runner가 필요하다

현재 전투/행동 코드는 대부분 아래 조건에 묶여 있다.

- `Runner != null && Runner.IsRunning`
- `Object.HasStateAuthority`
- `GameManagers.Instance`가 현재 active runner와 일치
- `CommandProcessor`가 현재 복구된 게임 흐름에 연결

`PlayerManager`, `Unit`, `Monster`, `FieldManager`, `CommandProcessor`가 전부 이 성격을 가진다.

따라서 old object는:

- 입력을 받지 못하고
- 커맨드 브로드캐스트에 참여하지 못하고
- 공격 스케줄링 대상도 아니고
- 전투 재개 루틴에서도 active runtime 대상으로 취급되지 않는다

그래서 “움직이지 않는 캐릭터”가 된다.

---

## 4. 왜 하나는 움직이고 하나는 안 움직이는가

현재 장면에는 사실상 아래 두 계층이 겹쳐 있을 가능성이 높다.

### A. old runner 계층

- Host Migration 이전에 존재하던 객체
- 화면에 남아 있음
- 더 이상 현재 게임 진행의 owner가 아님
- 입력/공격/AI/전투 재개와 단절됨

### B. new runner 계층

- resume snapshot 기준으로 새로 spawn된 객체
- `GameManagers.Instance`와 `NetworkManager._runner`가 참조하는 객체
- battle rebootstrap, AI takeover, command flow가 이쪽에 연결됨

그래서 같은 위치에 두 개가 보일 수 있고, 실제로는 new runner 객체만 움직이게 된다.

---

## 5. 현재 구조의 더 근본적인 문제

현재 `HostMigrationHandler` 설명과 실제 구현이 서로 다른 전략을 섞고 있다.

## 5-1. 전략 A: 기존 오브젝트 유지 + 권한 이전

이 전략이면:

- old object를 남긴다
- 새 Host가 `StateAuthority`만 가져간다
- 기존 객체를 재사용한다
- snapshot spawn은 최소화하거나 하지 않는다

## 5-2. 전략 B: 새 Runner 재시작 + snapshot 기준 재생성

이 전략이면:

- new runner를 만든다
- runtime object를 snapshot 기준으로 새로 spawn한다
- old runner/object는 반드시 정리한다

## 5-3. 현재 코드는 A와 B를 동시에 사용한다

현재 상태는:

- 주석/프리팹 정책은 A 쪽
- 실제 `HostMigrationResume(...)`는 B 쪽
- old object cleanup은 없음

즉, 복원 아키텍처가 단일 전략으로 정리되지 않았다.

이게 지금 캐릭터 중복과 더미 잔존의 가장 큰 원인이다.

---

## 6. 해결 방향

코드 수정은 이 문서 범위를 벗어나지만, 방향은 명확하다.

## 6-1. 먼저 한 가지 전략만 선택해야 한다

### 권장

현재 코드베이스는 이미 아래 쪽에 많이 기울어져 있다.

- old runner 종료
- new runner 생성
- `HostMigrationResume(...)`에서 resume snapshot spawn
- `GameManagers.Instance` 재바인딩
- battle/prepare/wall 복구 재개

즉, **현재 프로젝트는 “새 Runner 재시작 + 새 객체 복원” 전략으로 정리하는 것이 더 현실적**이다.

## 6-2. 이 전략을 유지한다면 반드시 stale object cleanup이 필요하다

필수 정리 대상:

- old runner `GameObject`
- old runner 소속 `PlayerManager`
- old runner 소속 `Unit`
- old runner 소속 `Monster`
- old runner 소속 runtime `GameManagers`
- old runner가 남긴 각종 parent/container 오브젝트

정리 기준은 “현재 active runner에 속하지 않는 runtime object”다.

중요한 점:

- scene object는 그대로 두고 state만 복사
- runtime object만 cleanup

으로 분리해야 한다.

## 6-3. 반대로 기존 객체 재사용 전략으로 갈 수도 있지만 비용이 크다

이 경우에는:

- `HostMigrationResume(...)`의 대량 spawn 루프를 다시 설계해야 하고
- 살아남은 old object를 current runner 기준으로 재귀적으로 재결선해야 하고
- `GameManagers`, `PlayerManager`, `Unit`, `MonsterSpawner`, `FieldManager`를 전부 “reuse-first”로 재작성해야 한다

현재 코드 상태에서는 이 방향이 더 큰 리스크다.

---

## 7. 문서 기준 해결 우선순위

### HM-CHAR-01. old runtime object cleanup 단계 추가

Host Migration 복구의 명시적 단계로 아래가 필요하다.

- old runner shutdown 완료
- old runner 소속 runtime object sweep
- new runner 객체만 남긴 상태에서 restore gate 진입

이 단계가 없으면 중복 객체 문제는 계속 재발한다.

### HM-CHAR-02. duplicate detection smoke check 추가

현재 smoke check는

- runner mismatch
- wall map
- unit map
- AI takeover

위주다.

여기에 아래를 추가해야 한다.

- 동일 `playerId`를 가진 `PlayerManager`가 2개 이상 존재하는지
- 동일 grid/cell에 old/new `Unit`이 동시에 남아 있는지
- current runner가 아닌 `NetworkObject`가 scene에 남아 있는지

### HM-CHAR-03. “논리적으로 버린 객체”를 시각적으로도 제거

지금은 `RebuildNetworkPlayersAfterMigration(...)`가 논리 등록만 다시 한다.

하지만 사용자 입장에서는 “안 쓰는 객체가 화면에 남아 있으면 버그”다.

즉, registry rebuild만으로는 부족하고 visual cleanup이 필요하다.

### HM-CHAR-04. runner ownership 로그 강화

중복 이슈를 빨리 잡으려면 각 runtime object에 대해 아래 로그가 필요하다.

- object name
- playerId
- current runner name
- active runner 일치 여부
- state authority 여부
- input authority 여부

지금 문제는 “두 개가 보인다”이지 “어느 쪽이 active인지 즉시 안 보인다”는 점도 크다.

---

## 8. 현재 증상에 대한 최종 해석

질문한 현상은 대체로 아래 순서로 해석할 수 있다.

1. Host Migration 발생
2. old runner 소속 캐릭터/유닛/몬스터가 장면에 남음
3. new runner가 resume snapshot 기준으로 같은 객체를 다시 spawn함
4. `GameManagers`와 복구 루틴은 new runner 객체만 active runtime으로 사용함
5. old object는 화면에는 보이지만 입력/이동/공격 루프에서 제외됨
6. 결과적으로
   - 하나는 실제로 움직이고 공격함
   - 다른 하나는 제자리에 남아 있는 더미처럼 보임

즉, 이 문제의 본질은

- AI 문제도 아니고
- 단순 animation 문제도 아니고
- 단순 state restore 누락 하나도 아니며

**Host Migration 이후 runtime object를 단일 집합으로 정리하지 못한 문제**다.

---

## 9. 바로 확인할 체크 포인트

실제 플레이에서 아래 로그/상태를 보면 원인 확인이 빠르다.

1. Host Migration 직후 `FindObjectsOfType<PlayerManager>(true)` 수
2. 같은 `playerId`를 가진 `PlayerManager`가 2개 이상 있는지
3. 각 `PlayerManager.Runner`가 `NetworkManager._runner`와 일치하는지
4. 멈춘 캐릭터의 `Object.HasStateAuthority`, `Object.InputAuthority`, `Runner.IsRunning`
5. `runner.GetAllNetworkObjects()`에 들어가는 객체와 장면에 보이는 객체 수가 다른지

이 체크에서 old runner 소속 객체가 남아 있으면 현재 문서의 원인 분석과 일치한다.

---

## 10. 결론

현재 캐릭터가 2개가 되고 하나가 더미처럼 남는 이유는 다음 한 줄로 정리할 수 있다.

**Host Migration 이후 old runtime object를 제거하지 않은 상태에서, new runner 기준 runtime object를 snapshot으로 다시 spawn하고 있기 때문이다.**

따라서 해결 방향도 명확하다.

- old object 재사용 전략으로 완전히 갈 것인지
- new object 복원 전략으로 갈 것인지

둘 중 하나만 선택해야 하며, 현재 코드 기준으로는 **new runner 복원 전략을 유지하되 old runtime object cleanup 단계를 추가하는 것이 가장 현실적인 해결 방향**이다.
