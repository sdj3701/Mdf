# LobbyUI Plan

작성일: 2026-04-22  
수정일: 2026-04-22

기준 코드:
- `Assets/Scripts/Network/LobbyUI.cs`
- `Assets/Scripts/Network/NetworkManager.cs`
- `Assets/Scripts/Network/RoomItem.cs`
- `Assets/Scripts/Enums/GameEnums.cs`

대상 UI:
- Joined Delay Panel
- 현재 코드 기준 `LobbyUI._networkJoinPanel`

참고 로그:
- Fusion `SupportLogger Info: State: ConnectingToNameServer`
- Fusion `SupportLogger Info: State: ConnectingToMasterServer`

---

## 0. 문서 목적

이 문서는 Joined Delay Panel 문제를 Fusion 로그 기준으로 다시 해석하고, 기존 계획을 전면 수정한 문서다.

이번 기준의 핵심은 아래다.

1. `SupportLogger` 로그는 디버그 신호이지 UI 제어 신호가 아니다.
2. Joined Delay Panel은 "연결 상태 enum 하나"로 제어하면 안 된다.
3. 로비 부트스트랩, 방 생성/참여, 씬 전환을 서로 다른 작업으로 분리해야 한다.

---

## 1. 로그 기준 재해석

현재 관찰된 흐름:

1. `SupportLogger Info: State: ConnectingToNameServer`
2. 이후 `SupportLogger Info: State: ConnectingToMasterServer`

이 로그는 Photon Cloud/Fusion 내부 연결 단계다.

즉, 이 두 로그는 아래를 의미한다.

- Name Server 연결 시도
- Region/Auth 처리 후 Master Server 연결 진행

중요한 점:

- 이 로그는 "방 생성 완료"를 뜻하지 않는다.
- 이 로그는 "Joined Delay Panel을 꺼야 하는 정확한 시점"을 보장하지 않는다.
- Fusion 내부 단계는 빠르게 지나갈 수 있고, UI 수명주기와 1:1 대응되지 않는다.

정리하면, 기존처럼

- 1번째 로그 -> 패널 표시
- 2번째 로그 -> 패널 비활성화

로 구조를 잡는 방식은 신뢰할 수 없다.

---

## 2. 현재 문제를 다시 정의

기존에는 문제를 "빠르게 연결되면 패널이 안 꺼진다"로 봤지만, 로그 기준으로 다시 보면 실제 문제는 더 구조적이다.

실제 문제:

1. Joined Delay Panel이 Photon 내부 로그 단계에 간접 의존하고 있다.
2. `LobbyUI`는 로비 연결과 방 생성/참여를 같은 종류의 "Connecting"으로 취급하고 있다.
3. 패널이 어떤 작업의 시작/종료를 따라야 하는지 명확한 owner가 없다.

즉, 이 버그는 단순한 `SetActive(false)` 누락이 아니라, "UI가 잘못된 기준 신호를 보고 있다"는 문제다.

---

## 3. 왜 기존 계획이 부족했는가

이전 계획은 주로 아래를 중심으로 잡혀 있었다.

- 버튼 클릭에서 패널 직접 제어 제거
- `ConnectionState.InGame`에서 패널 닫기
- `StartGame(...)`에서 상태 전이 추가

이 방향 자체는 유효하지만, 이번 로그를 기준으로 보면 여전히 부족하다.

부족한 이유:

1. `ConnectionState` 하나만으로는 로비 부트스트랩과 방 참가를 구분하기 어렵다.
2. `Connecting`은 너무 넓은 상태라 "무슨 작업이 진행 중인지"를 설명하지 못한다.
3. SupportLogger 로그 단계는 엔진 내부 연결 단계라 UI 닫힘 신호로 쓰기 어렵다.
4. 방 생성/참여보다 먼저 진행 중인 로비 연결 작업이 있으면 서로 꼬일 수 있다.

즉, 단순히 `InGame` 닫기만 추가하는 수준으로는 구조가 깔끔해지지 않는다.

---

## 4. 새 기준

이번 문서에서 Joined Delay Panel은 아래 원칙으로 다시 정의한다.

### 4-1. 패널은 "로그"를 따라가지 않는다

Joined Delay Panel은 `SupportLogger` 문자열 로그를 기준으로 켜고 끄지 않는다.

로그의 역할:

- 디버그
- 타이밍 분석
- 실패 원인 추적

패널의 기준:

- 앱 레벨 네트워크 작업 시작
- 앱 레벨 네트워크 작업 종료

### 4-2. 패널은 "상태"보다 "작업"을 따라간다

핵심 전환:

- 기존: `ConnectionState`만 보고 패널을 제어
- 변경: "현재 어떤 네트워크 작업이 진행 중인가"를 보고 패널 제어

즉, Joined Delay Panel의 source of truth는 "연결 상태"가 아니라 "진행 중인 작업(operation)"이어야 한다.

### 4-3. 로비 부트스트랩과 방 참가를 분리한다

현재 구조에서 가장 많이 섞이는 두 가지:

1. 로비에 처음 붙는 작업
2. 방을 만들거나 들어가는 작업

이 둘은 UI 의미가 다르다.

- 로비 부트스트랩: 방 목록 준비
- 방 참가/생성: 씬 이동 및 방 입장 대기

같은 `Connecting`으로 묶으면 패널 종료 조건이 불명확해진다.

---

## 5. 새 구조의 핵심 모델

## 5-1. 두 종류의 상태를 분리한다

### A. 네트워크 수명주기 상태

예시:

- `Disconnected`
- `BootstrappingLobby`
- `LobbyReady`
- `StartingRoom`
- `InRoom`
- `Failed`

이 상태는 시스템이 지금 어디까지 왔는지 설명한다.

### B. UI 블로킹 작업 상태

예시:

- `None`
- `LobbyBootstrap`
- `CreateRoom`
- `JoinRoom`
- `LeaveRoom`
- `Recovery`

이 상태는 Joined Delay Panel이 왜 보여야 하는지 설명한다.

핵심은 B가 Joined Delay Panel의 직접 기준이 되는 것이다.

## 5-2. 권장 방식: Operation Token 또는 Busy Counter

가장 안전한 모델은 아래 둘 중 하나다.

### 방식 1. 단일 active operation

- 한 번에 하나의 UI 블로킹 작업만 허용
- 새 작업 시작 시 기존 작업이 끝났는지 반드시 확인

장점:

- 구현 단순
- 디버그 쉬움

단점:

- 로비 부트스트랩과 방 참가가 겹치면 충돌 가능

### 방식 2. operation token / busy counter

- 작업 시작 시 token 발급
- 작업 종료 시 token 반납
- active token 수가 1개 이상이면 패널 표시

장점:

- 겹치는 비동기 작업에 강함
- 빠른 성공/실패 race에 안전

단점:

- 구조가 조금 더 복잡

이번 이슈 기준으로는 token 또는 counter 방식이 더 적합하다.

---

## 6. 작업 단위로 본 Joined Delay Panel 정책

## 6-1. Lobby Bootstrap

시작:

- `NetworkManager.JoinLobby()` 시작 직전

종료:

- `JoinSessionLobby(SessionLobby.Shared)`가 `Ok`로 끝난 시점
- 또는 실패/예외 시점

패널 정책:

- 로비 진입이 아직 끝나지 않았으면 패널 유지
- `ConnectingToMasterServer` 로그가 나왔다고 해서 바로 닫지 않는다
- `JoinSessionLobby` 완료가 실제 종료 기준이다

## 6-2. Create Room

시작:

- 사용자가 Confirm Create를 눌러 실제 `StartGame(...)`가 시작되는 시점

종료:

- `StartGame(...)` 성공 후 룸 시작 handoff가 완료된 시점
- 또는 실패/예외 시점

중요:

- Create Room 팝업을 연 시점은 시작이 아니다
- SupportLogger의 `ConnectingToMasterServer`도 종료 기준이 아니다

Create Room 종료 기준 후보:

1. `StartGame(...)`가 `Ok`를 반환한 시점
2. `OnPlayerJoined(...)`로 실제 방 입장 완료가 확인된 시점
3. `JoinLobby` 씬 진입 또는 scene transition 시작 시점

이 셋 중 하나를 명시적으로 선택해야 한다.

권장:

- UI handoff 기준은 `StartGame(...) Ok` + scene transition 시작

## 6-3. Join Room

시작:

- RoomItem Join / Direct Join이 실제 `StartGame(...)`를 호출하는 시점

종료:

- Create Room과 동일하게 room handoff 완료 시점
- 실패/예외 시 즉시 종료

## 6-4. Force Close 규칙

아래 경로에서는 operation이 남아 있어도 강제 종료가 가능해야 한다.

- `OnDisable()`
- 씬 언로드
- `OnShutdown(...)`
- `OnDisconnectedFromServer(...)`
- 예외/fallback 진입

즉, Joined Delay Panel은 "정상 종료"뿐 아니라 "비정상 종료"에서도 반드시 닫히는 경로가 필요하다.

---

## 7. 로그를 어떻게 써야 하는가

이번 이슈에서 로그 사용 원칙은 다음과 같다.

### 써야 하는 로그

- operation 시작 로그
- operation 종료 로그
- operation 실패 로그
- Fusion callback 도착 로그
- scene transition 시작/완료 로그

### UI 기준으로 쓰면 안 되는 로그

- `SupportLogger Info: ConnectingToNameServer`
- `SupportLogger Info: ConnectingToMasterServer`
- 기타 Photon 내부 상태 문자열

이 로그들은 참고용이다.

즉, 문서 기준으로는

- "2번째 로그가 나왔으니 패널을 닫는다"

가 아니라

- "LobbyBootstrap/CreateRoom/JoinRoom operation이 끝났으니 패널을 닫는다"

가 되어야 한다.

---

## 8. 파일별 역할 재정의

## 8-1. `Assets/Scripts/Network/LobbyUI.cs`

역할:

- 패널 렌더링
- 버튼 입력 수집
- 현재 UI block reason 렌더

하면 안 되는 일:

- Fusion 내부 로그 해석
- 패널 종료 기준 자체 판단
- 네트워크 작업 성공/실패 판정

즉, `LobbyUI`는 "그려주는 쪽"이어야 한다.

## 8-2. `Assets/Scripts/Network/NetworkManager.cs`

역할:

- 로비 부트스트랩 시작/종료
- 방 생성/참여 시작/종료
- 예외/실패/취소/fallback 정리
- UI block operation 발행

즉, Joined Delay Panel의 실제 owner는 `NetworkManager` 쪽이 더 맞다.

## 8-3. 신규 권장 타입

권장 신규 타입 예시:

- `NetworkUiBlockReason`
- `NetworkOperationScope`
- `LobbyConnectionCoordinator`
- `RoomJoinOperationState`

핵심은 "연결 상태 enum"과 "UI block reason"을 분리하는 것이다.

---

## 9. 권장 구현 방향

## 9-1. 최소 변경안

현 구조를 많이 바꾸지 않는 선:

1. `ConnectionState`는 유지
2. 별도 `NetworkUiBlockReason` 추가
3. `JoinLobby`, `StartGame`, `Shutdown`, `Disconnect`에서 UI block 시작/종료 이벤트만 추가
4. `LobbyUI`는 UI block reason만 보고 패널 표시

장점:

- 기존 코드 영향 범위가 작다

한계:

- 여전히 연결 상태와 작업 상태가 분리되어 있어 관리 코드가 다소 분산된다

## 9-2. 권장 변경안

더 좋은 구조:

1. `ConnectionState`를 로비 수명주기 중심으로 재설계
2. `NetworkUiBlockReason` 또는 operation token을 별도 유지
3. `JoinLobby`와 `StartGame`을 명시적 operation scope로 감싼다
4. scene handoff 시점까지 operation을 유지한다

이 방식이 이번 로그 기반 문제를 가장 안정적으로 해결한다.

---

## 10. 단계별 작업 계획

### Phase 1. 개념 정리

1. Joined Delay Panel의 기준을 `SupportLogger` 로그가 아니라 operation으로 명시한다.
2. Lobby bootstrap / Create Room / Join Room을 문서상 별도 작업으로 분리한다.
3. 현재 어떤 callback이 실제 종료 기준이 될지 합의한다.

### Phase 2. 상태 모델 도입

1. `NetworkUiBlockReason` 또는 operation token 모델 도입
2. `NetworkManager`가 begin/end를 책임지게 정리
3. `LobbyUI`는 렌더 전용으로 축소

### Phase 3. 종료 기준 정교화

1. `JoinLobby` 종료 기준 고정
2. `CreateRoom` 종료 기준 고정
3. `JoinRoom` 종료 기준 고정
4. 실패/예외/취소/fallback 종료 기준 고정

### Phase 4. 검증 로그 추가

1. operation id별 시작/종료 로그
2. SupportLogger와 앱 operation 로그 대조 가능하게 정리
3. fast-path / slow-path 모두 재현 테스트

---

## 11. 테스트 시나리오

## 11-1. 로비 부트스트랩

- 앱 진입 직후 `ConnectingToNameServer`
- 이후 `ConnectingToMasterServer`
- `JoinSessionLobby Ok`
- 세션 목록 수신

기대 결과:

- 패널은 bootstrap operation 동안만 유지
- SupportLogger 2번째 로그가 아니라 bootstrap operation 종료에서 꺼진다

## 11-2. Create Room Fast Path

- 사용자가 Confirm Create
- `StartGame(...)` 즉시 빠르게 성공
- 바로 scene handoff

기대 결과:

- 패널은 빠른 성공이어도 남지 않는다
- scene 전환 또는 handoff 완료에서 정리된다

## 11-3. Create Room While Lobby Bootstrap Still Running

- 아직 로비 bootstrap 중인데 사용자가 create 경로를 연다

기대 결과:

- create popup open 자체는 panel 기준이 아니다
- bootstrap/create operation이 겹치면 token 기준으로 처리한다
- 먼저 끝난 작업이 다른 작업 패널을 꺼버리지 않는다

## 11-4. Join Room Failure

- direct join 또는 room join 시도
- 실패 또는 예외

기대 결과:

- 패널은 반드시 닫힌다
- 실패 원인이 operation 로그로 남는다

## 11-5. Scene Leave / Disable

- 패널 표시 중 씬이 언로드되거나 오브젝트가 disable

기대 결과:

- panel 잔상이 남지 않는다
- operation cleanup이 강제 수행된다

---

## 12. 최종 결론

이번 로그 기준으로 보면 Joined Delay Panel 문제는 단순한 UI 버그가 아니라 "Fusion 내부 연결 로그"와 "앱 레벨 네트워크 작업"을 섞어 쓴 데서 나온 구조 문제다.

따라서 앞으로의 기준은 아래로 고정한다.

1. `SupportLogger` 로그는 디버그용으로만 쓴다.
2. Joined Delay Panel은 `ConnectionState` 하나로 제어하지 않는다.
3. 패널은 `LobbyBootstrap`, `CreateRoom`, `JoinRoom` 같은 operation 기준으로 제어한다.
4. 종료 기준은 로그 문자열이 아니라 async operation 완료 / callback / scene handoff로 잡는다.

이 문서 기준으로 다시 작업하면, 지금처럼

- 1번째 로그에서 켜고
- 2번째 로그에서 꺼지는지 기대하는 구조

에서 벗어나, 실제 작업 수명주기를 기준으로 안정적으로 Joined Delay Panel을 제어할 수 있다.
