# MDFNetworkPlan (v1)

작성일: 2026-04-07  
기준 코드: `Assets/Scripts/Network`, `Assets/Scripts/Managers`, `Assets/Scripts/Game`  
핵심 기준 클래스: `NetworkManager`

---

## 0) 문서 목적

이 문서는 현재 MDF 프로젝트의 네트워크 흐름을 `NetworkManager` 기준으로 정리하고, 다음 리팩토링 작업을 위한 기준선을 만드는 문서다.

1. 로그인(닉네임 입력) -> 로비 접속 -> 방 생성 -> 방 참여 -> 게임 진입의 실제 호출 흐름을 정리한다.
2. 네트워크 관련 클래스별 책임과 리스크를 명확히 분리한다.
3. SOLID 원칙을 기준으로 리팩토링 방향을 제시한다.
4. 하드코딩 값(씬 이름, PlayerPrefs 키, 타임아웃 등)을 `Define`으로 일원화하는 기준을 제시한다.
5. 추후 작성할 방 생성 전용 리팩토링 문서(`MDFRoomCreatePlan.md` 예정)의 뼈대를 제공한다.

---

## 1) 현재 네트워크 전체 흐름 (As-Is)

## 1-1. 로그인/진입 흐름

1. `NextScenes.OnClick()`
2. `NetworkManager.NickNameInput`에서 닉네임 읽기
3. `PlayerPrefs["PlayerNickname"]` 저장
4. `NetworkManager.JoinLobby()` 호출
5. `NetworkManager.LoadSceneSmart("MatchingLobby")`로 로비 씬 이동

핵심 포인트:
- 현재 "로그인"은 계정 인증이 아니라 닉네임 저장 기반 진입이다.
- `PassWordInput` 필드는 존재하지만 실제 인증 로직에는 사용되지 않는다.

## 1-2. 로비 접속/방 목록 흐름

1. `NetworkManager.JoinLobby()`에서 `NetworkRunner` 생성 후 `JoinSessionLobby(SessionLobby.Shared)` 호출
2. 성공 시 `State = InLobby`
3. Fusion 콜백 `OnSessionListUpdated()`에서 `_sessionList` 갱신
4. `OnSessionListUpdatedEvent` 이벤트를 `LobbyUI`가 구독해 목록 UI 재생성

핵심 포인트:
- `LobbyUI`가 `_networkManager._sessionList`를 직접 읽어 UI를 구성한다.
- 목록 갱신 때마다 아이템을 전체 제거 후 재생성한다.

## 1-3. 방 생성(Host) 흐름

1. `LobbyUI._confirmCreateButton` 클릭
2. `NetworkManager.StartGame(GameMode.Host, roomName, "JoinLobby")`
3. `StartGameArgs` 구성
4. `NetworkRunner.StartGame(...)` 실행
5. 성공 후 `JoinLobby` 씬에서 플레이어 준비 UI 진행

핵심 포인트:
- 씬 이름을 문자열로 직접 전달한다.
- `PlayerCount`는 `NetworkManager.maxSessionPlayers` 값 사용(Inspector).

## 1-4. 방 참여(Client) 흐름

1. `LobbyUI` RoomItem Join 버튼 또는 Direct Join 입력
2. `NetworkManager.StartGame(GameMode.Client, sessionName, "JoinLobby")`
3. 세션 접속 후 `NetworkPlayer` 동기화
4. `JoinLobbyUI`가 플레이어 목록/준비 상태 표시

## 1-5. 게임 시작 흐름

1. `JoinLobbyUI`에서 Host만 시작 버튼 활성화
2. 모든 플레이어 Ready일 때 Host가 시작 버튼 클릭
3. `_runner.LoadScene(Game)` 실행
4. 게임 씬에서 `GameManagers`/`PlayerManager` 기준 플레이 진행

## 1-6. 명령(Command) 전파 흐름

현재 코드에는 두 경로가 혼재한다.

1. `PlayerManager.RPC_RequestCommandToServer(...)` -> `GameManagers.RPC_BroadcastCommandToClients(...)` -> `CommandProcessor.ReceiveAndEnqueueCommand(...)`
2. `NetworkManager.RPC_RequestCommandToServer(...)` -> `NetworkManager.RPC_BroadcastCommandToClients(...)`

핵심 포인트:
- 실사용 경로는 `CommandProcessor`에서 `PlayerManager` RPC를 타는 구조다.
- `NetworkManager` 쪽 RPC도 남아 있어 중복/혼동 리스크가 존재한다.

## 1-7. Host Migration/연결 끊김 흐름

1. `NetworkManager.OnHostMigration(...)`에서 `HostMigrationHandler.StartMigration(...)` 위임
2. `HostMigrationToken` 기반 새 Runner 시작
3. `HostMigrationResume`에서 오브젝트/상태 복원
4. `GameManagers.RestoreAfterHostMigration()` 호출
5. 복원 완료 후 AI takeover reconciliation 및 smoke check 실행

연결 끊김 시:
- `ConnectionLossPolicyMode`에 따라 즉시 폴백 또는 지연 폴백
- 최종적으로 `MatchingLobby` 씬 복귀

---

## 2) 클래스별 책임/문제/리팩토링 방향

아래는 현재 네트워크 흐름에서 직접 관여하는 클래스 중심 분석이다.

| 클래스 | 현재 기능 | 현재 문제/리스크 | 리팩토링 방향 |
|---|---|---|---|
| `NetworkManager` | Runner 생성/로비 접속/세션 시작/콜백 처리/연결 끊김 정책/일부 RPC | 책임 과다(연결, 씬, 세션, 플레이어 스폰, 복구, UI 입력 참조까지 포함), 사용되지 않는 필드(`PassWordInput`), 중복 RPC 경로, 하드코딩 문자열 다수 | `INetworkSessionService`, `INetworkSceneService`, `IConnectionRecoveryService`, `IPlayerSpawnService`로 분리. `NetworkManager`는 오케스트레이터로 축소 |
| `HostMigrationHandler` | Host migration 전 과정(캐시, 재시작, 복원, AI takeover, smoke check) | 파일/책임 과대(1500+ lines), 복원/검증/AI처리/로그 정책이 하나로 결합 | `MigrationCoordinator`, `MigrationStateCache`, `MigrationRunnerFactory`, `MigrationRecoveryValidator`, `MigrationAITakeoverService`로 분할 |
| `LobbyUI` | 방 목록 표시, 방 생성/직접 참여 버튼, 로비 상태 반영 | `NetworkManager` 내부 필드 직접 접근, UI가 세션 질의와 생성 호출을 직접 수행, 전체 목록 재생성 비용 | `LobbyPresenter` + `IRoomQueryService` + `IRoomJoinUseCase` 도입. UI는 View 모델만 렌더 |
| `JoinLobbyUI` | 방 내부 준비/시작 UI, Ready 토글, 플레이어 목록 표시 | `FindObjectsOfType<NetworkPlayer>()` 의존, 네트워크 상태와 UI 상태 결합, `_runner` 직접 참조 | `IPlayerRosterService`로 플레이어 목록 공급, `IRoomReadyService`로 Ready 토글 분리 |
| `RoomItem` | 방 항목 렌더 및 Join 버튼 처리 | 텍스트 로직/UI 상태 로직 결합, 문자열 하드코딩("입장", "만석") | `RoomItemViewData` 기반 단순 렌더 컴포넌트로 축소 |
| `NetworkPlayer` | 닉네임/Ready 네트워크 동기화 | UI 갱신을 직접 트리거(JoinLobbyUI singleton 의존), PlayerPrefs 접근이 엔티티와 결합 | `NetworkPlayerState` + `PlayerProfileService` 분리, UI 갱신은 이벤트 버스로 전달 |
| `PooledNetworkObjectProvider` | Fusion 네트워크 오브젝트 풀링 | 풀 정책 값이 분산(`maxPoolCount`), 진단 정보 부족 | `NetworkPoolDefine`로 정책 상수화, 풀 상태 디버그 메서드 추가 |
| `BuildDebugGUI` | 개발 빌드 로그 오버레이 | 전역 singleton + 입력 처리 + 로그 저장 책임 결합 | `IDebugLogSink` 인터페이스 도입, GUI는 sink 표시 전용 |
| `GetPlayerCamera` | 로컬 플레이어 카메라 위치 적용 | 씬 이벤트/플레이어 이벤트/카메라 배치 로직 결합 | `ILocalPlayerLocator` + `ICameraRigApplier` 분리 |
| `NextScenes` | 닉네임 저장 후 로비 진입 | NRE 가능성(`_networkManager` null 체크 전에 접근), 로그인 책임과 씬 전환 책임 혼재 | `LoginEntryUseCase`로 분리, null-safe 처리 |
| `SceneLobbyButton` | 씬 전환 버튼 | 네트워크/로컬 씬 로딩 정책을 버튼이 직접 판단 | 씬 이동은 `ISceneNavigator`로 위임 |
| `BaseButton` | 공통 버튼 기반 클래스 | UI 계층에서 `GameManagers` 직접 참조 가능성 | UI Base는 참조 최소화, 필요한 의존성은 명시적으로 주입 |
| `GameManagers` | 게임 상태머신 + 플레이어 레지스트리 + 커맨드 처리 + UI 플로우 + 마이그레이션 복구 | 역할 과다, 네트워크/도메인/UI 결합, 유지보수 난이도 높음 | 상태머신/라운드플로우/UIFlow/Registry/Recovery를 모듈 분리(이미 partial이므로 분리 확장 용이) |
| `PlayerManager` | 플레이어 스탯/필드/상점/전투/커맨드 RPC/AI 보조 등 | 엔티티와 서비스 책임 혼재, 1700+ lines | `PlayerRuntimeContext`, `PlayerEconomyService`, `PlayerCombatService`, `PlayerRpcGateway` 분리 |
| `CommandProcessor` | 명령 직렬화/역직렬화/큐 처리 | 네트워크 전송 정책까지 포함되어 책임 증가 | `ICommandSerializer`, `ICommandTransport`, `ICommandQueueExecutor`로 분할 |
| `GameSceneInitializer` | 싱글/멀티 진입 모드 초기화 | 게임 초기화와 네트워크 정책이 결합 | `SingleModeBootstrapper`와 `MultiplayerBootstrapper` 분리 |

---

## 3) 핵심 문제 요약 (우선순위)

## P0 (즉시 정리 권장)

1. `NetworkManager`와 `PlayerManager`의 커맨드 RPC 이중화
2. `NetworkManager`의 과도한 책임 집중
3. 씬 이름/PlayerPrefs 키/기본 문자열 하드코딩 분산
4. 방 생성/참여 로직이 UI와 강결합 (`LobbyUI`)

## P1 (다음 스프린트 권장)

1. `HostMigrationHandler` 분해
2. `JoinLobbyUI`의 `FindObjectsOfType` 의존 제거
3. `NetworkPlayer`의 UI 직접 갱신 의존 제거

## P2 (점진 개선)

1. 디버그/카메라/버튼 공통계층 정리
2. 싱글 모드 초기화 경로 분리

---

## 4) SOLID 기준 리팩토링 원칙

## S: Single Responsibility Principle

원칙:
- 클래스는 "한 가지 변경 이유"만 갖게 한다.

적용:
1. `NetworkManager`에서 다음 책임 분리
2. 세션 접속/생성
3. 씬 로드
4. 연결 복구 정책
5. 플레이어 스폰
6. PlayerPrefs 기반 사용자 프로필 처리

## O: Open/Closed Principle

원칙:
- 정책 변경은 "새 구현 추가"로 해결하고 기존 코드는 최소 수정.

적용:
1. 연결 끊김 정책을 전략 패턴으로 분리 (`IConnectionLossPolicy`)
2. 방 이름 생성 규칙을 정책화 (`IRoomNamePolicy`)
3. 룸 필터(가시성/공개/정원)를 전략화 (`IRoomFilter`)

## L: Liskov Substitution Principle

원칙:
- 구현체 교체 시 호출부 동작이 깨지지 않아야 한다.

적용:
1. `INetworkSceneLoader`: Fusion 로더/Unity 로더 교체 가능
2. `IRunnerFactory`: 일반 시작/마이그레이션 시작 구현 교체 가능

## I: Interface Segregation Principle

원칙:
- 큰 인터페이스 1개보다 작은 인터페이스 여러 개를 사용.

적용:
1. `ILobbySessionReader` (목록 조회)
2. `IRoomCreator` (방 생성)
3. `IRoomJoiner` (방 참여)
4. `IRoomReadyController` (레디 토글/조회)

## D: Dependency Inversion Principle

원칙:
- UI가 구체 구현이 아닌 추상에 의존.

적용:
1. `LobbyUI`는 `NetworkManager.Instance` 직접 참조 대신 인터페이스 주입
2. `JoinLobbyUI`는 `NetworkPlayer` 직접 탐색 대신 `IPlayerRosterService` 사용

---

## 5) Define 정리 기준 (공용 상수 일원화)

## 5-1. 목표

1. 문자열 하드코딩 제거
2. 씬/키/기본값 변경 시 수정 지점 단일화
3. 테스트 코드에서도 동일 상수 사용

## 5-2. 권장 파일 구조

`Assets/Scripts/Defines/`

1. `SceneDefine.cs`
2. `PlayerPrefsDefine.cs`
3. `NetworkDefine.cs`
4. `NetworkTimingDefine.cs`
5. `UiTextDefine.cs`

## 5-3. Define 후보 목록

| 분류 | 현재 값(예시) | 권장 Define |
|---|---|---|
| 씬 이름 | `"Title"`, `"MatchingLobby"`, `"JoinLobby"`, `"Game"`, `"MainLobby"` | `SceneDefine.Title`, `SceneDefine.MatchingLobby` ... |
| PlayerPrefs 키 | `"PlayerNickname"`, `"PlayerUUID"` | `PlayerPrefsDefine.NicknameKey`, `PlayerPrefsDefine.PlayerUuidKey` |
| 기본 문자열 | `"MyFusionRoom"`, `"DefaultName"`, `"Host"` | `NetworkDefine.DefaultRoomName`, `NetworkDefine.DefaultNickname`, `NetworkDefine.DefaultHostName` |
| 플레이어 수 | 최소/최대 2~4, 기본 2 | `NetworkDefine.MinSessionPlayers`, `NetworkDefine.MaxSessionPlayers`, `NetworkDefine.DefaultSessionPlayers` |
| 타임아웃 | reconnect fallback 5s, migration wait 30s 등 | `NetworkTimingDefine.CloudReconnectFallbackSec`, `NetworkTimingDefine.MigrationStartTimeoutSec` |
| UI 문구 | `"입장"`, `"만석"`, `"You are the Host"` | `UiTextDefine.RoomJoin`, `UiTextDefine.RoomFull`, ... |

## 5-4. 샘플 코드

```csharp
public static class SceneDefine
{
    public const string Title = "Title";
    public const string MatchingLobby = "MatchingLobby";
    public const string JoinLobby = "JoinLobby";
    public const string Game = "Game";
    public const string MainLobby = "MainLobby";
}

public static class PlayerPrefsDefine
{
    public const string NicknameKey = "PlayerNickname";
    public const string PlayerUuidKey = "PlayerUUID";
}

public static class NetworkDefine
{
    public const string DefaultRoomName = "MyFusionRoom";
    public const string DefaultNickname = "DefaultName";
    public const string DefaultHostName = "Host";

    public const int MinSessionPlayers = 2;
    public const int MaxSessionPlayers = 4;
}
```

## 5-5. 운영 규칙

1. 문자열/숫자 literal을 네트워크 코드에 직접 쓰지 않는다.
2. 한 번 이상 재사용되면 즉시 Define로 이동한다.
3. Define는 도메인별 파일로 분리하고, 파일 간 순환 의존을 만들지 않는다.

---

## 6) 방 생성 기능 리팩토링 준비안 (후속 문서용)

추후 `MDFRoomCreatePlan.md`를 작성할 때 아래 구조를 그대로 사용한다.

## 6-1. 목표

1. 방 생성 로직을 UI에서 분리
2. 입력 검증/중복 방 처리/실패 피드백을 유스케이스로 표준화
3. 재시도/취소/타임아웃 정책 명확화

## 6-2. 권장 유스케이스 인터페이스

```csharp
public interface ICreateRoomUseCase
{
    UniTask<CreateRoomResult> ExecuteAsync(CreateRoomRequest request, CancellationToken ct);
}

public readonly struct CreateRoomRequest
{
    public string RoomName { get; }
    public int MaxPlayers { get; }
    public string LobbySceneName { get; }
}
```

## 6-3. 단계별 작업

1. Define 도입 완료 (`SceneDefine`, `PlayerPrefsDefine`, `NetworkDefine`)
2. `LobbyUI`에서 방 생성 버튼 로직을 `CreateRoomPresenter`로 추출
3. `NetworkManager.StartGame(GameMode.Host, ...)` 호출부를 `CreateRoomUseCase`로 캡슐화
4. 실패 코드(`ShutdownReason`)를 UI 친화 메시지로 매핑
5. 테스트 추가

## 6-4. 테스트 체크리스트

1. 빈 방 이름 입력 시 기본값 대체 여부
2. 중복 방 이름 처리 정책 일관성
3. 네트워크 실패 시 패널/버튼 상태 복구
4. 생성 성공 후 `JoinLobby` 씬 전환 확인
5. Host Migration 이후 재생성/재참여 시나리오 회귀 확인

---

## 7) 실행 로드맵 (권장)

## Phase 1: 기준선 정리

1. Define 파일 추가
2. 하드코딩 문자열 교체
3. `NetworkManager`의 미사용 필드/중복 로직 정리

## Phase 2: 방 생성 경로 분리

1. `CreateRoomUseCase` 도입
2. `LobbyUI` -> Presenter/UseCase 호출 구조 전환
3. 생성 실패/성공 결과 타입 정리

## Phase 3: 참가/준비 상태 분리

1. `JoinLobbyUI`에서 플레이어 탐색 로직 제거
2. `IPlayerRosterService` 기반 목록 갱신
3. Host 시작 조건(모두 Ready) 규칙 서비스화

## Phase 4: 복구 경로 정리

1. `HostMigrationHandler` 모듈 분할
2. 복구 검증 로직을 별도 validator로 이동

---

## 8) 완료 정의 (Definition of Done)

1. 씬 이름/PlayerPrefs 키/기본값 하드코딩 제거
2. UI 계층이 `NetworkManager` 내부 필드 직접 참조하지 않음
3. 방 생성/참여/준비 상태가 UseCase 단위로 분리됨
4. Host Migration 핵심 경로가 독립 테스트 가능한 구조로 분해됨
5. 네트워크 관련 클래스가 책임 기준으로 명확히 분리되어 클래스 길이와 복잡도가 감소함

---

## 9) 메모

- 본 문서는 "현재 코드 기준" 분석 문서다.
- 후속 리팩토링은 이 문서를 기준으로 `MDFRoomCreatePlan.md`를 추가 작성해 세부 구현으로 진행한다.
