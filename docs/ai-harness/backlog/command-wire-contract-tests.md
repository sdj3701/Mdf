# 백로그: 커맨드 와이어 계약 테스트

## 요약

MDF 커맨드의 네트워크 와이어 포맷 계약을 소스 문자열 검사 대신 실제 동작 기반 EditMode 테스트로 검증한다.

## 우선순위

P1 - 멀티플레이어 커맨드 라우팅 회귀 방지.

## 문제

`Assets/Scripts/Commands/Core/CommandProcessor.cs`의 `CommandProcessor`는 커맨드 객체를 네트워크 전송용 payload로 직렬화하고 다시 커맨드 객체로 복원하는 매핑을 큰 switch 문으로 수동 관리한다. 이 경로는 유닛 구매, 이동, 벽 배치, 리롤, 증강 선택, 동기화 알림 같은 핵심 플레이어 액션의 기반이다.

현재 하네스 커버리지에는 `Assets/Scripts/Testing/MP/Editor/MPTestHarnessEditModeTests.cs`에서 `File.ReadAllText(...).Contains(...)` 형태의 소스 문자열 검사가 많이 포함되어 있다. 이런 검사는 중요한 문구가 파일 안에 남아 있는지는 확인할 수 있지만, 실제 커맨드가 기대한 payload로 직렬화되고, 역직렬화되고, 검증되고, 올바른 경로로 라우팅되는지는 증명하지 못한다.

위험도가 가장 높은 지점은 다음이다.

- `CommandProcessor.SerializeCommand(...)`와 `DeserializeCommand(...)`.
- 클라이언트 요청을 검증하고 `playerId`를 권위 있는 값으로 보정하는 `PlayerManager.RPC_RequestCommandToServer(...)`.
- `BattleSpawnMonster`와 `UseMagicScroll`이 일반 `CommandProcessor` broadcast 경로를 우회하고 State Authority 전투 커맨드 RPC 경로를 사용하는 `GameManagers.BattleCommands`.

## 근거

- `Assets/Scripts/Commands/Core/CommandProcessor.cs`는 커맨드 wire payload를 수동 switch branch로 관리한다.
- `Assets/Scripts/Enums/CommandType.cs`에는 일반 command-stream 값과 별도 전투 커맨드 값이 함께 들어 있다.
- `Assets/Scripts/Managers/PlayerManager.cs`의 `ValidateClientCommandRequest(...)`는 알 수 없거나 서버 전용인 커맨드를 거부한다.
- `Assets/Scripts/Managers/GameManagers.BattleCommands.cs`는 별도 전투 커맨드 RPC 경로를 소유한다.
- `Assets/Scripts/Testing/MP/Editor/MPTestHarnessEditModeTests.cs`는 중요한 커맨드 동작을 소스 문자열 기반으로 넓게 검사하고 있다.

## 제안 작업

1. 테스트 가능한 커맨드 wire codec 표면을 만든다.
   - 프로젝트 스타일에 맞게 `CommandProcessor`의 직렬화/역직렬화 매핑을 `internal` helper로 분리하거나, 좁은 test-only 접근 지점을 제공한다.
   - 런타임 동작은 변경하지 않는다.

2. 일반 command-stream 커맨드에 대한 EditMode round-trip 테스트를 추가한다.
   - `CommandType`을 검증한다.
   - int, string, vector payload 형태를 검증한다.
   - 복원된 커맨드 타입과 핵심 public 필드를 검증한다.

3. 명시적인 라우트 분류 테스트를 추가한다.
   - 일반 player/sync/request 커맨드는 command-stream codec으로 커버되어야 한다.
   - `BattleSpawnMonster`와 `UseMagicScroll`은 `CommandProcessor` broadcast 커맨드가 아니라 별도 State Authority 전투 커맨드 경로임을 검증한다.

4. 위험도가 가장 높은 소스 문자열 검사만 동작 기반 테스트로 교체한다.
   - 큰 테스트 파일 전체를 한 번에 정리하려고 하지 않는다.
   - 씬 wiring 또는 asset path처럼 더 나은 런타임 테스트를 만들기 어려운 저위험 문자열 검사는 당장은 유지한다.

## 완료 기준

- `CommandType`의 모든 값이 command-stream 직렬화 대상 또는 의도적인 별도 라우트로 분류된다.
- 새 `CommandType`을 추가했는데 route/codec 테스트를 갱신하지 않으면 EditMode 테스트가 실패한다.
- round-trip 테스트가 최소한 다음 커맨드를 커버한다.
  - `BuyUnit`
  - `MoveUnit`
  - `SwapUnit`
  - `SellUnit`
  - `PlaceWall`
  - `RemoveWall`
  - `RerollShop`
  - `SelectAugment`
  - `ActivateSkill`
  - `SyncShopItems`
  - `SyncPresentedAugments`
  - `SyncPermanentBonuses`
  - `RegisterUnitAt`
  - `ApplyPermanentWalls`
  - `NotifyPurchaseSucceeded`
  - `NotifyAugmentSelected`
  - `NotifyWallPlacementSucceeded`
  - `NotifyWallRemovalSucceeded`
  - `RequestSyncData`
- `BattleSpawnMonster`와 `UseMagicScroll`은 non-`CommandProcessor` 전투 라우트로 테스트된다.
- 컴파일이 통과한다.
  - `unity-cli --project Mdfproject editor refresh --compile`
  - `unity-cli --project Mdfproject console --type error --stacktrace user`
- 집중 EditMode 테스트가 통과한다.

## 권장 테스트 형태

결정적인 payload를 가진 작은 커맨드 샘플을 사용한다.

- Player id: `2`
- Grid position: `(1, 0, 3)`, `(4, 0, 5)`
- Shop slot: `1`
- Augment index: `0`
- Network id raw: 기존 payload 표현에 들어갈 수 있는 고정 `uint`
- Wall flat positions: 두 칸 정도의 작은 position set

codec 테스트에는 전체 멀티플레이어 세션을 요구하지 않는다. 런타임 authority와 E2E 동작은 기존 MP 하네스 프로필에서 계속 커버한다.

## 검증 프로필

가장 작은 유효 검증 범위는 다음이다.

1. Unity CLI로 컴파일한다.
2. 새 커맨드 계약 fixture의 focused EditMode 테스트를 실행한다.

런타임 커맨드 라우팅을 변경하지 않는 한, 이 백로그 수행에 더 넓은 멀티플레이어 E2E는 필수 조건이 아니다.

## 메모

이 백로그는 `CommandProcessor`, `PlayerManager`, `MPTestHarnessEditModeTests.cs`의 광범위한 리팩터링을 의도하지 않는다. 첫 번째 가치 있는 단계는 wire contract를 고정해서 이후 gameplay/content 커맨드 작업에서 빠른 회귀 신호를 얻는 것이다.
