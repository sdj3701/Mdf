# GameManagers 안정화/리팩토링 계획서 (심층 분석 v1)

작성일: 2026-03-02  
대상 클래스: `Assets/Scripts/Managers/GameManagers.cs` (약 2750 lines)  
연계 클래스: `HostMigrationHandler`, `NetworkManager`, `PlayerManager`, `GameEvents`

---

## 0) 문서 목적

이 문서는 `GameManagers`를 앞으로 안전하게 유지/개선하기 위한 "작업 기준서"다.

- 현재 구조와 상태 전이 흐름을 함수 단위로 정리
- Host Migration 복원 경로를 코드 기준으로 명확히 정의
- 불안정 요인을 우선순위로 분류하고 수정 방향 제시
- 후속 작업자가 바로 구현 가능한 체크리스트/테스트 매트릭스 제공

---

## 1) 클래스 책임 분석 (현재 구조)

`GameManagers`는 현재 아래 역할을 동시에 수행한다.

1. 게임 상태 머신 오케스트레이션
- `GameState` 관리 (`Setup/DataLoading/Prepare/Battle1/Battle2/GameOver`) (`GameManagers.cs:22, 25-33`)
- 라운드/타이머 전이 (`FixedUpdateNetwork`, `StartNextRound`, `StartBattle1/2Phase`)

2. 플레이어 레지스트리/조회
- `NetworkPlayers` 배열 기반 플레이어 열거 (`GameManagers.cs:39-73`)
- `GetPlayer`, `GetBattleOpponent`, 로컬 플레이어 재연결 (`RelinkLocalPlayer`)

3. 전투 매칭/공수 결정
- `_battleOpponents`, `_matchFirstAttacker` 유지 (`GameManagers.cs:212-218`)
- 라운드별 랜덤 매칭 + Battle1/2 공수 스왑

4. 커맨드 실행 허브
- `CommandProcessor` 보유 및 `Render()`에서 처리 (`GameManagers.cs:16, 595-598`)
- 커맨드 브로드캐스트/RPC 래퍼 제공

5. UI 동기화 브리지
- 상점/증강 UI 준비 (`SetupGameUI`)
- 상태 변경 시 UI 업데이트 (`HandleNetworkStateChange`, `HandleUIForNewState`)

6. Host Migration 복원 게이트
- `TryApplyCachedStateForMigration`
- `RestoreAfterHostMigration` -> `PausePhaseTimerForMigrationIfNeeded` -> `WaitForRestoreDependenciesAndResumeFlow` -> `ResumeGameFlowFromCurrentState`

결론: "게임 규칙 엔진 + UI 코디네이터 + 복원 오케스트레이터"가 한 클래스에 과집중되어 있다. 기능 자체는 풍부하지만 결합도가 높아 회귀 리스크가 큰 구조다.

---

## 2) 실행 흐름 상세 (코드 기준)

## 2-1. 일반 시작 경로

1. `Spawned()` 진입 (`GameManagers.cs:224`)
2. 싱글톤 교체/중복 러너 정리
3. `CommandProcessor`, `LoadManager`, `SurvivorBossManager`, `_changeDetector` 준비
4. 일반 경로면 `InitializeAndStartGame().Forget()` (`GameManagers.cs:291-293`)
5. `InitializeAndStartGame`:
- `LoadManager.InitializeAsync()`
- `GameFlow()`
- `_isSpawned=true`, `RelinkLocalPlayer`, `RebuildNetworkPlayersAfterMigration`, `GameEvents.TriggerGameManagersReady()`

6. `GameFlow()` (`GameManagers.cs:633-655`)
- 서버 authority면 `currentState=Setup`
- 프리팹 로드
- `SetupPlayersAndGrids()`
- `SetupGameUI()`
- 서버면 `StartNextRound()`

## 2-2. 틱 기반 상태 전이

`FixedUpdateNetwork()` 핵심 (`GameManagers.cs:384`):

- `HasStateAuthority` 없으면 즉시 return
- 타이머 만료 시:
- `Prepare -> StartBattle1Phase`
- `Battle1 -> StartBattle2Phase`
- `Battle2 -> StartNextRound().Forget()`

- 전투 조기 종료(모두 전투 완료) 시 남은 시간 3초로 단축
- 전투 종료 5초 전 버서커 모드 발동

## 2-3. 렌더 경로

`Render()` (`GameManagers.cs:585`):

- `_changeDetector.DetectChanges(this)`로 `currentState` 변화를 감지
- 변화 감지 시 `HandleNetworkStateChange(currentState)`
- 매 프레임 `CommandProcessor.ProcessCommands()` 실행

## 2-4. 라운드/전투 오케스트레이션

`StartNextRound()` (`GameManagers.cs:1277`):

- 전투 잔존 몬스터 정리, 카메라 복귀
- 탈락 판정
- 라운드 증가 + `currentState=Prepare`
- 플레이어별 골드/리롤/상점 동기화/증강 제시
- `HandleUIForNewState(Prepare)` await
- 준비 타이머 설정

`StartBattle1Phase()` (`GameManagers.cs:1433`):

- 미선택 증강 자동선택
- `AssignBattleOpponents()`
- `currentState=Battle1`, 전투 타이머 설정
- `StartBattleForPlayers(isFirstBattle:true)`

`StartBattle2Phase()` (`GameManagers.cs:1482`):

- 매칭 검증/복구 (`EnsureBattleMappingAfterMigration`)
- 잔여 몬스터 정리
- `currentState=Battle2`
- `StartBattleForPlayers(isFirstBattle:false)`

## 2-5. Host Migration 연동 흐름

외부 진입:
- `NetworkManager.OnHostMigration` -> `HostMigrationHandler.StartMigration` (`NetworkManager.cs:533-541`)
- `HostMigrationHandler.WaitAndRestoreGameManagers`에서 `gm.RestoreAfterHostMigration()` 호출 (`HostMigrationHandler.cs:809-812`)

`GameManagers` 내부 복원:

1. `RestoreAfterHostMigration()` (`GameManagers.cs:2101`)
- trace id/복원 플래그 초기화
- ready/runner/authority 게이트 검증
- `ChangeDetector` 재초기화
- `RelinkLocalPlayer`, `RebuildNetworkPlayersAfterMigration`
- UI 복원 비동기 시작 `RestoreLocalUIAfterMigrationAsync().Forget()`
- 상점/증강 동기화 보정
- 타이머 일시정지 후 `WaitForRestoreDependenciesAndResumeFlow` 코루틴 시작

2. `WaitForRestoreDependenciesAndResumeFlow()` (`GameManagers.cs:2273`)
- 조건: `hasAuthority && runnerMatched && uiReady && playersReady`
- 충족 시 `ResumeGameFlowFromCurrentState()`
- 타임아웃(8초) 후에도 최소 게이트 통과 시 재개 시도

3. `ResumeGameFlowFromCurrentState()` (`GameManagers.cs:2369`)
- 일시정지된 타이머 복원 우선
- 타이머 유효하면 유지
- 없거나 만료면 상태별 기본 타이머 재설정

---

## 3) 안정 동작을 위한 핵심 불변조건 (Invariants)

아래 조건이 깨지면 대부분의 버그가 발생한다.

1. `GameManagers.Instance`는 항상 활성 `Runner` 소속이어야 함
2. `NetworkPlayers`는 현재 러너의 유효 `PlayerManager`만 포함해야 함
3. `currentState/currentRound/phaseTimer`는 StateAuthority에서만 변경
4. `Battle` 진입 전 플레이어 런타임 참조(`field/grid/spawner/spawn/goal`)가 모두 준비되어야 함
5. `currentState` 변경 이벤트 발행 경로는 중복 없이 단일해야 함
6. 복원 중 `Prepare` 타이머 만료가 UI 복원보다 앞서면 안 됨
7. `StartBattleForPlayers`는 상대 플레이어 준비 여부까지 확인 후 실행해야 함
8. `Render`의 커맨드 처리 루프는 중복 enqueue/중복 execute가 없어야 함
9. 복원 타임아웃 fallback은 "안전 게이트 통과" 없이는 전진하면 안 됨
10. `FindObjectsOfType` fallback은 반드시 `Runner` 소유권 필터를 동반해야 함

---

## 4) 리스크 분석 (우선순위 포함)

## R-01 [Critical] 상태 변경 이벤트/처리 경로 중복

근거:
- `StartNextRound`에서 `GameEvents.TriggerGameStateChanged(currentState)` 직접 호출 (`GameManagers.cs:1339`)
- `Render`에서도 상태 변화 감지 후 `HandleNetworkStateChange` -> `TriggerGameStateChanged` 호출 (`GameManagers.cs:585-628`)
- `StartBattle1/2Phase`에서 직접 `HandleUIForNewState().Forget()` 호출 (`GameManagers.cs:1464, 1504`)

영향:
- UI가 동일 상태에서 중복 열림/닫힘
- 복원 직후 이벤트 순서 꼬임

권장 수정:
- 상태 전이 진입점 단일화 (`TransitionToState`) 후
- 상태 변경 이벤트 발행을 한 지점으로 고정
- UI 반영도 상태 이벤트 구독 기반으로 1회만 실행

## R-02 [High] `NetworkPlayers` 재구성 시 stale slot 잔존 가능

근거:
- `RebuildNetworkPlayersAfterMigration`는 유효 플레이어를 재할당만 하고, 남은 슬롯 clear 로직이 없음 (`GameManagers.cs:2483-2551`)

영향:
- `AllPlayers` 열거에서 낡은 참조/중복 참조 혼입 가능

권장 수정:
- 재구성 시작 시 `NetworkPlayers` 전체 clear 후 재할당
- 할당 후 integrity assert(`assigned == uniqueAlivePlayers`) 추가

## R-03 [High] 복원 플래그 수명 관리가 분산되어 경합 가능

근거:
- `_migrationRestoreInProgress`는 `RestoreAfterHostMigration`에서 true 설정
- `RestoreLocalUIAfterMigrationAsync` finally에서 false로 종료 (`GameManagers.cs:2699`)
- 실제 흐름 재개는 별도 코루틴 `WaitForRestoreDependenciesAndResumeFlow`가 담당

영향:
- UI 복원 완료 시점과 게임 흐름 재개 시점의 의미가 분리되어 디버깅이 어려움

권장 수정:
- 복원 상태를 명시적 enum 단계로 분리 (`Preparing/UiRestored/Resumed/Failed`)
- 플래그 기반 조합 대신 단계 전이 기반으로 관리

## R-04 [High] 매칭 복구(`EnsureBattleMappingAfterMigration`)의 다인전 정확도 리스크

근거:
- `_battleOpponents`가 비었을 때 `opponentManager`와 현재 attacker flag로 폴백 재구성 (`GameManagers.cs:2553-2637`)

영향:
- 3인/4인 중간 복원 시 매칭 방향이 원래 라운드와 어긋날 수 있음

권장 수정:
- Host migration cache에 "라운드 매칭 스냅샷"(opponents + firstAttacker map) 저장/복원
- 폴백 로직은 최후 수단으로만 사용

## R-05 [Medium] 비동기 Fire-and-Forget 남용으로 취소 제어 부족

근거:
- `InitializeAndStartGame().Forget()`, `HandleUIForNewState(...).Forget()`, `RestoreLocalUIAfterMigrationAsync().Forget()` 등 다수

영향:
- 씬 전환/러너 교체 시 늦게 완료된 task가 오래된 참조에 접근 가능

권장 수정:
- `CancellationTokenSource`를 `OnDestroy/OnDisable`와 연동
- migration 단계별 token 분리

## R-06 [Medium] `Render()`의 커맨드 처리 프레임 의존성

근거:
- `CommandProcessor.ProcessCommands()`가 `Render()`에서 매 프레임 실행 (`GameManagers.cs:595-598`)

영향:
- 클라이언트 FPS 변동 시 커맨드 처리 지연/버스트 가능

권장 수정:
- 명령 실행 시점을 틱 기준(`FixedUpdateNetwork`) 또는 고정 dispatch 스케줄러로 이동 검토

## R-07 [Medium] `HandleAugmentChosen`가 `async void`

근거:
- `private async void HandleAugmentChosen(...)` (`GameManagers.cs:1237`)

영향:
- 예외/취소 추적이 어려움

권장 수정:
- 이벤트 핸들러는 내부에서 `UniTask.Void` 래핑 + 공통 예외 처리기로 표준화

## R-08 [Medium] 클래스 책임 과다

근거:
- 전투 로직, UI 로딩, 커맨드 브로드캐스트, 복원 게이트, 플레이어 레지스트리까지 단일 클래스

영향:
- 작은 수정도 광범위 회귀 위험

권장 수정:
- 역할별 서비스 분리 (아래 6장 참조)

---

## 5) 안정화 우선 수정안 (실행 순서)

## Phase A: 동작 유지형 하드닝 (우선)

1. 상태 전이 단일화
- `TransitionToState(GameState next, string reason)` 도입
- `currentState` 설정, 이벤트 발행, UI 반영 트리거를 단일 경로화

2. `NetworkPlayers` 재구성 정합성 강화
- clear -> fill 방식
- 유효성 로그: assigned/outOfRange/staleCleared

3. 복원 게이트 가시화
- `_migrationRestoreInProgress` 등 bool을 단계 enum으로 교체
- 각 단계 진입/종료 로그 고정 포맷화

4. 비동기 취소 토큰 도입
- `GameManagers` 생명주기 token
- migration 전용 token

## Phase B: Host Migration 정확도 개선

1. 매칭 스냅샷 직렬화
- `_battleOpponents`, `_matchFirstAttacker`, `FirstAttackerPlayerId` 캐시

2. 타이머 복원 규칙 명문화
- "기존 타이머 유지" vs "캐시 타이머 승격" 우선순위 함수화

3. UI 복원 완료 조건 강화
- Prepare 단계에서 `shop/augment` 준비 검증 함수를 공통화

## Phase C: 구조 분리 (중기)

1. `GameFlowCoordinator`
- 라운드/상태 전이

2. `BattleOrchestrator`
- 매칭/공수/전투 시작

3. `MigrationRecoveryCoordinator`
- 복원 게이트/타이머 재개/런타임 준비 검사

4. `GameUiStateBridge`
- 상태 이벤트 구독 및 UI 표시

---

## 6) 추천 리팩토링 스케치 (코드 작업 지침)

## 6-1. 상태 전이 API 도입

```csharp
private void TransitionToState(GameState next, string reason)
{
    if (!Object.HasStateAuthority) return;
    if (currentState == next) return;

    currentState = next;
    _lastStateTransitionReason = reason;

    // 상태 이벤트 발행은 여기서만
    GameEvents.TriggerGameStateChanged(next);

    // UI 반영은 별도 bridge가 이벤트 구독으로 처리
}
```

적용 포인트:
- `StartNextRound`, `StartBattle1Phase`, `StartBattle2Phase`, `GameOver`
- `Render`에서 중복 발행 제거

## 6-2. 복원 단계 enum

```csharp
private enum MigrationStage
{
    None,
    Begin,
    RelinkDone,
    UiRestored,
    PlayersReady,
    FlowResumed,
    Failed
}
```

효과:
- 현재 bool 조합보다 상태 추적이 명확
- 타임아웃 fallback 시 원인 파악 용이

## 6-3. `NetworkPlayers` 재구성 강화

핵심 변경:
- 재구성 시작 시 슬롯 초기화
- 같은 `playerId` 중복 발견 시 선정 규칙 명시(현행: state authority 우선)
- 완료 후 integrity 검사 실패 시 경고를 에러로 승격

---

## 7) 테스트 매트릭스 (필수)

## A. 일반 게임 흐름

1. A-01: 2인, 첫 Prepare -> Battle1 -> Battle2 -> NextRound 정상 전이
2. A-02: 4인, 매칭/홀수 bye 처리 + 라운드 반복
3. A-03: 버서커 모드 5초 트리거 단발 보장

## B. Host Migration

1. H-01: Prepare 3초 남음 시 Host 종료
2. H-02: Battle1 시작 직후 Host 종료
3. H-03: Battle2 종료 직전 Host 종료
4. H-04: 3인/4인에서 복원 후 매칭 일치 여부
5. H-05: 복원 직후 상점/증강 UI 표시 정합성

## C. 경계/실패

1. E-01: `playersReady` 지연 시 8초 timeout fallback 동작
2. E-02: localPlayer 미해결 케이스에서 UI 경로 안전 종료
3. E-03: stale `NetworkPlayers` 존재 시 재구성으로 정상화

공통 검증 지표:
- `GameManagers.Instance.Runner == NetworkManager._runner`
- `AllPlayers` count == 실제 유효 `PlayerManager` count
- `currentState/currentRound/phaseTimer` 역행 없음
- 중복 UI 이벤트 발생 없음 (상태당 1회)

---

## 8) 즉시 작업 가능한 TODO 체크리스트

- [x] `TransitionToState` 도입 및 상태 변경 호출부 치환
- [x] `Render`의 상태 이벤트 중복 발행 제거
- [x] `RebuildNetworkPlayersAfterMigration` clear->fill 방식 적용
- [x] migration bool 플래그를 단계 enum으로 통합
- [x] `CancellationToken` 도입 후 주요 `.Forget()` 경로 정리
- [x] 매칭 스냅샷 캐시 구조 추가 (`GameMigrationData` 확장)
- [x] Host Migration 통합 회귀 테스트 시나리오 자동화(최소 smoke)

## 8-Hotfix) 긴급 버그(Hotfix)

- [x] Host(에디터)에서 증강/상점 UI가 표시되지 않는 문제 수정
- [x] Player2(Client) 벽 생성 시 Player1(Host) 위치에 생성되는 문제 수정

### Hotfix-1 상세 분석: Host(에디터) 증강/상점 UI 미표시

- 관측 로그: `NullReferenceException` in `GameManagers.GameFlow()` at `Assets/Scripts/Managers/GameManagers.cs:548`
- 관측 로그: `ShopUIController`가 `OnEnable` 시점에 `localPlayer`를 못 찾고 경고 출력 (`Assets/Scripts/UI/ShopUIController.cs:35`)
- 관측 로그: 이후 `HandleGameStateChange`에서 뒤늦게 `localPlayer`를 찾음 (`Assets/Scripts/UI/ShopUIController.cs:62`)
- 핵심 증상: `Prepare` 진입 시점에 증강/상점 UI 표시 이벤트 타이밍이 어긋나 초기 표시가 누락됨

### Hotfix-1 의심 지점(우선순위)

- `GameManagers.GameFlow` 초기 진입 시 `Object`/상태 접근 타이밍 경합 (`Assets/Scripts/Managers/GameManagers.cs`)
- `localPlayer` 결정 타이밍이 `SetupGameUI`/`StartNextRound`보다 늦는 경합 (`Assets/Scripts/Managers/GameManagers.cs`, `Assets/Scripts/Managers/GameManagers.UIFlow.cs`)
- `SyncAugmentsCommand`의 로컬 플레이어 판별 실패 시 `TriggerAugmentPhaseStart` 미발행 가능성 (`Assets/Scripts/Commands/Sync/SyncAugmentsCommand.cs`)
- `Render` 기반 상태 이벤트 발행 구조로 인해 준비 단계 UI 이벤트 순서가 뒤틀릴 가능성 (`Assets/Scripts/Managers/GameManagers.cs:Render`, `Assets/Scripts/Managers/GameManagers.UIFlow.cs`)

### Hotfix-1 점검/수정 체크리스트

- [ ] `GameFlow` 시작부 `Object` null/valid 가드 추가 및 예외 재발 방지
- [ ] `StartNextRound` 직전 `localPlayer`와 `playerId` 정합성 assert 로그 추가
- [ ] `SyncAugmentsCommand.Execute`에서 `PlayerId`, `InputAuthority`, `Runner.LocalPlayer` 비교 로그 추가
- [ ] `Prepare` 진입 프레임에 `TriggerAugmentPhaseStart`가 1회 이상 호출되는지 검증
- [ ] Host 기준 `UI_Pnl_Augment`, `UI_Pnl_Shop` 활성화 조건/시점 검증

### Hotfix-2 상세 분석: Client 벽이 Host 위치에 생성

- 관측 증상: Player2(Client)가 배치한 벽이 Player1(Host) 필드 좌표에 생성됨
- 구조상 영향 경로: `PlacementManager -> PlaceWallCommand(PlayerId, Position) -> GameManagers.GetPlayer(PlayerId) -> target FieldManager.CreateWallAt()`
- 핵심 의심: 잘못된 `PlayerId` 또는 잘못된 로컬 플레이어 바인딩으로 Host의 `FieldManager`를 타겟팅

### Hotfix-2 의심 지점(우선순위)

- 멀티플레이에서 `localPlayer` 폴백이 원격 플레이어를 가리키는 경로 (`Assets/Scripts/Managers/GameManagers.cs:Rpc_LinkSpawnedObjects`)
- `PlayerManager.playerId`가 초기화되기 전(기본값 0) 커맨드가 전송되는 경합 (`Assets/Scripts/Managers/PlayerManager.cs:Rpc_InitializePlayer`)
- 비소유 `PlacementManager`가 입력을 처리하는 경로 (`Assets/Scripts/Managers/PlacementManager.cs`)
- 버튼 UI가 잘못된 `localPlayer.fieldManager`를 대상으로 배치 모드를 시작하는 경로 (`Assets/Scripts/Button/PlacementButtonsUI.cs`)

### Hotfix-2 점검/수정 체크리스트

- [ ] 벽 배치 직전 `PlayerId`, `InputAuthority`, `localPlayer.playerId`, `target field owner` 로그 고정
- [ ] `playerId` 네트워크 초기화 완료 전에는 벽 배치 커맨드 차단
- [ ] `PlacementManager` 입력 처리 객체가 항상 `HasInputAuthority=true`인지 프레임 단위 검증
- [ ] `PlaceWallCommand.Execute`에서 `player.fieldManager.playerManager.playerId == PlayerId` assert 추가
- [ ] Host/Client 각각 10회 이상 벽 배치 교차 테스트로 오배치 재현 여부 확인

### 공통 진단 메모

- 현재 두 버그는 모두 `localPlayer/playerId/authority` 초기화 타이밍 경합 가능성이 높다.
- 따라서 기능 수정 전에 "로컬 플레이어 결정 시점"과 "커맨드 전송 시점"의 순서를 로그로 고정하는 것이 우선이다.
- 재현 테스트는 반드시 `Editor Host + Build Client` 조합으로 반복 수행한다.

## 8-1) 1차 역할/책임 분해 진행 현황 (이번 작업)

- [x] `GameManagers`를 `partial class`로 전환 (`Assets/Scripts/Managers/GameManagers.cs`)
- [x] Player Registry/Lookup 책임 분리 파일 생성 (`Assets/Scripts/Managers/GameManagers.PlayerRegistry.cs`)
- [x] `AllPlayers`, `IsPlayerReadable`, `TryGetPlayerIdSafe`를 분리 파일로 이동
- [x] `GetPlayer`, `GetBattleOpponent`를 분리 파일로 이동
- [x] `RelinkLocalPlayer`, `RebuildNetworkPlayersAfterMigration`를 분리 파일로 이동
- [x] `EnsureBattleMappingAfterMigration`, `ResolveFirstAttackerForResumePair`를 분리 파일로 이동
- [x] State 전이 책임 분리 (`TransitionToState` 중심)
- [x] UI 책임 분리 (`SetupGameUI/HandleUIForNewState` 모듈화)
- [x] Migration 복원 책임 분리 (`RestoreAfterHostMigration` 모듈화)

## 8-2) 2차 역할/책임 분해 진행 현황 (이번 작업)

- [x] 상태 전이 전용 파일 생성 (`Assets/Scripts/Managers/GameManagers.StateTransition.cs`)
- [x] 공통 전이 함수 도입 (`TransitionToState` + 상태별 헬퍼)
- [x] `GameFlow`의 `Setup` 상태 진입을 전이 함수로 치환
- [x] `StartNextRound`의 `Prepare` 전이를 전이 함수로 치환
- [x] `StartBattle1Phase/StartBattle2Phase` 상태 전이를 전이 함수로 치환
- [x] `GameOver` 상태 전이를 전이 함수로 치환
- [x] Host Migration 캐시 승격 시 상태 반영을 전이 함수로 치환

## 8-3) 3차 역할/책임 분해 진행 현황 (이번 작업)

- [x] UI 흐름 전용 파일 생성 (`Assets/Scripts/Managers/GameManagers.UIFlow.cs`)
- [x] 상태 변경 UI 브리지 이동 (`HandleNetworkStateChange`)
- [x] UI lifecycle 훅 이동 (`OnEnable/OnDisable`)
- [x] UI 초기화/로딩 책임 이동 (`SetupGameUI`)
- [x] 증강 선택 후 UI 처리 이동 (`HandleAugmentChosen`)
- [x] 상태별 UI 반영 책임 이동 (`HandleUIForNewState`)
- [x] 공격 시퀀스 UI 진입 로직 이동 (`ShowAttackSequenceUIAsync`)

## 8-4) 4차 역할/책임 분해 진행 현황 (이번 작업)

- [x] Migration 복원 전용 파일 생성 (`Assets/Scripts/Managers/GameManagers.MigrationRecovery.cs`)
- [x] 캐시 상태 반영/랭크 계산 로직 이동 (`TryApplyCachedStateForMigration`, `GetMigrationStateRank`)
- [x] 복원 진입/재개 게이트 로직 이동 (`RestoreAfterHostMigration`, `PausePhaseTimerForMigrationIfNeeded`, `WaitForRestoreDependenciesAndResumeFlow`, `ResumeGameFlowFromCurrentState`)
- [x] 복원 런타임 검증 로직 이동 (`IsBoundToActiveRunner`, `AreAllPlayersRuntimeReadyForMigration`)
- [x] 복원 UI 보정 비동기 로직 이동 (`RestoreLocalUIAfterMigrationAsync`, `ShowLocalShopFallbackAsync`)
- [x] 본체 파일에서 Host Migration 메서드 블록 제거로 책임 경계 명확화

---

## 9) 후속 작업자용 빠른 진입 포인트

먼저 읽을 함수 순서:

1. `Spawned` (`GameManagers.cs:224`)
2. `InitializeAndStartGame` / `GameFlow` (`GameManagers.cs:365, 633`)
3. `FixedUpdateNetwork` (`GameManagers.cs:384`)
4. `StartNextRound` (`GameManagers.cs:1277`)
5. `StartBattle1Phase` / `StartBattle2Phase` / `StartBattleForPlayers` (`GameManagers.cs:1433, 1482, 1582`)
6. `RestoreAfterHostMigration` + 하위 복원 함수 (`Assets/Scripts/Managers/GameManagers.MigrationRecovery.cs`)

연계 확인:

- Host Migration 진입/복원 게이트: `HostMigrationHandler.cs:118, 745`
- Runner 전환: `NetworkManager.cs:113, 533`
- 플레이어 런타임 준비/재결선: `PlayerManager.cs:291, 416`
- 이벤트 계약: `GameEvents.cs:12-17, 33-34, 98-113`

---

## 10) 결론

현재 `GameManagers`는 이미 Host Migration 안정화를 위해 많은 방어 로직이 들어가 있고, 실제 복원 시퀀스도 상당히 진화된 상태다. 다만 상태 전이/이벤트/UI/복원 게이트가 여러 경로로 중복되어 있어 "간헐적 불안정"이 남을 구조다.

가장 효과가 큰 개선은 다음 3가지다.

1. 상태 전이 단일화 (`TransitionToState`)  
2. 복원 단계 상태머신(enum) 도입  
3. `NetworkPlayers` 재구성 정합성 강화(clear->fill + integrity assert)

이 3가지를 먼저 적용하면 이후 기능 추가(보스/증강/다인전/재접속 정책) 시 회귀 위험을 크게 낮출 수 있다.
